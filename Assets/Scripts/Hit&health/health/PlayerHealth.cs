using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 체력/피해 단일 처리 지점.
/// - ReactionMode:
///   CombatController: PlayerCombatController.ApplyHit/ApplyDeath 호출(권장)
///   AnimatorParam   : Animator 파라미터(Hit/Hurt) 사용
/// - HUD는 OnHPChanged만 구독.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IHealth
{
    public enum ReactionMode { CombatController, AnimatorParam }

    // ① HP 기본
    [Header("① HP 기본")]
    [Tooltip("최대 HP")]
    [SerializeField] private int maxHP = 100;
    [Tooltip("현재 HP(시작값)")]
    [SerializeField] private int currentHP = 100;

    // ② 무적/리액션 공통
    [Header("② 무적/리액션 공통")]
    [Tooltip("피격 후 자동 무적 시간(초). 0이면 비활성")]
    [SerializeField] private float invincibleDuration = 0.8f;
    [Tooltip("피격 이펙트(선택)")]
    [SerializeField] private GameObject hitEffectPrefab;
    [Tooltip("카메라 셰이크(선택)")]
    [SerializeField] private CameraShake cameraShake;
    [Tooltip("리액션 재생 모드 선택")]
    [SerializeField] private ReactionMode reactionMode = ReactionMode.CombatController;

    // ③ CombatController 모드 전용
    [Header("③ CombatController 모드 전용")]
    [Tooltip("피격/다운/사망 애니 재생 컨트롤러(없으면 자동 탐색)")]
    [SerializeField] private PlayerCombatController combatController;
    [Tooltip("이 값 이상 데미지면 강 피격(Hit_Heavy)로 처리")]
    [SerializeField] private int heavyDamageThreshold = 25;

    // ④ AnimatorParam 모드 전용
    [Header("④ AnimatorParam 모드 전용")]
    [Tooltip("리액션을 쏠 Animator(없으면 자동 탐색)")]
    [SerializeField] private Animator animator;
    [Tooltip("피격 파라미터명(Trigger 또는 Bool 허용)")]
    [SerializeField] private string hitParamName = "Hit";
    [Tooltip("대체 파라미터명(선택)")]
    [SerializeField] private string hurtParamName = "Hurt";

    // ⑤ 디버그
    [Header("⑤ 디버그")]
    [SerializeField] private bool debugLog = true;

    // 내부 상태
    private bool _isInvincible;
    private float _invTimer;
    private bool _isStaggered;

    // Animator 캐시
    private int _hitHash, _hurtHash;
    private bool _hitExists, _hurtExists, _hitIsTrigger, _hurtIsTrigger;
    private RuntimeAnimatorController _cachedCtrl;
    private bool _didRescanAfterSwap;
    private PlayerReferences _playerReferences;

    // ── IHealth ───────────────────────────────────────────────
    public int CurrentHP => currentHP;
    public int MaxHP => maxHP;
    public bool IsDead => currentHP <= 0;
    public bool IsStaggered => _isStaggered;
    public bool isInvincible { get => _isInvincible; set { _isInvincible = value; if (!value) _invTimer = 0f; } }

    public event Action<int, int> OnHPChanged;
    public event Action<int, int> OnHealthChanged;
    public event Action<int> OnDamaged;
    public event Action<int, HitType> OnDamagedWithType;
    public event Action OnDied;

    void Reset()
    {
        AutoWire();
        currentHP = Mathf.Clamp(currentHP, 0, Math.Max(1, maxHP));
    }

    void Awake()
    {
        AutoWire();
        currentHP = Mathf.Clamp(currentHP, 0, Math.Max(1, maxHP));

        if (reactionMode == ReactionMode.AnimatorParam)
            CacheAnimatorParams();

        if (debugLog)
        {
            if (reactionMode == ReactionMode.CombatController)
            {
                var ccName = combatController ? combatController.name : "NULL";
                Debug.Log("[Health] Mode=CombatController ctrl=" + ccName, this);
            }
            else
            {
                var animName = animator ? animator.name : "NULL";
                var ctrlName = (animator && animator.runtimeAnimatorController)
                               ? animator.runtimeAnimatorController.name : "NULL";
                var hitKind = _hitIsTrigger ? "Trig" : "Bool";
                Debug.Log("[Health] Mode=AnimatorParam anim=" + animName +
                          " ctrl=" + ctrlName +
                          " HitExists=" + _hitExists + "(" + hitKind + ")" +
                          " HurtExists=" + _hurtExists, this);
            }
        }

        RaiseHpEvents();
    }

    void Update()
    {
        if (_isInvincible)
        {
            _invTimer -= Time.deltaTime;
            if (_invTimer <= 0f) isInvincible = false;
        }

        if (reactionMode == ReactionMode.AnimatorParam && animator)
        {
            if (_cachedCtrl != animator.runtimeAnimatorController && !_didRescanAfterSwap)
            {
                _didRescanAfterSwap = true;
                CacheAnimatorParams();
                if (debugLog) Debug.Log("[Health] Animator controller swapped -> re-scan params", this);
            }
        }
    }

    // ── Damage: 일반(리액션 포함) ──────────────────────────────
    public void ApplyDamage(float damage) => ApplyDamage(Mathf.RoundToInt(damage));

    public void ApplyDamage(int amount)
    {
        if (amount <= 0) { if (debugLog) Debug.Log("[Health] Skip(amount<=0)", this); return; }
        if (_isInvincible) { if (debugLog) Debug.Log("[Health] Skip(invincible)", this); return; }
        if (IsDead) { if (debugLog) Debug.Log("[Health] Skip(dead)", this); return; }

        int before = currentHP;
        currentHP = Mathf.Max(0, currentHP - amount);

        if (hitEffectPrefab) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
        if (cameraShake) cameraShake.Shake(0.25f, 0.15f);

        // 리액션: 일반 히트만 재생
        PlayReaction(amount);

        if (debugLog) Debug.Log($"[Health] Damage {amount} | {before}->{currentHP}", this);

        RaiseHpEvents();
        OnDamaged?.Invoke(amount);
        OnDamagedWithType?.Invoke(amount, HitType.Normal);

        if (currentHP <= 0) OnDied?.Invoke();
        if (invincibleDuration > 0f) SetInvincible(invincibleDuration);
    }

    // ── Damage: 칩 대미지(리액션 없음) ─────────────────────────
    public void ApplyChipDamage(float damage) => ApplyChipDamage(Mathf.RoundToInt(damage));
    public void ApplyChipDamage(int amount)
    {
        if (amount <= 0 || IsDead) return;

        int before = currentHP;
        currentHP = Mathf.Max(0, currentHP - amount);

        if (debugLog) Debug.Log($"[Health] Chip {amount} | {before}->{currentHP}", this);

        // 리액션/무적 미적용. 블록/패링 연출은 가드 컨트롤러가 수행.
        RaiseHpEvents();

        if (currentHP <= 0) OnDied?.Invoke();
    }

    public void TakeDamage(int amount, HitType type, Vector3 point) => ApplyDamage(amount);

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = currentHP;
        currentHP = Mathf.Clamp(currentHP + amount, 0, Math.Max(1, maxHP));
        if (currentHP != before) RaiseHpEvents();
    }

    public void SetInvincible(float seconds)
    {
        isInvincible = seconds > 0f;
        _invTimer = seconds;
    }

    // ── Reaction ───────────────────────────────────────────────
    void PlayReaction(int amount)
    {
        if (reactionMode == ReactionMode.CombatController)
        {
            if (currentHP <= 0)
            {
                if (combatController) combatController.ApplyDeath();
                return;
            }

            bool heavy = amount >= heavyDamageThreshold;
            if (combatController) combatController.ApplyHit(heavy);
            return;
        }

        // AnimatorParam 모드
        if (!animator) { if (debugLog) Debug.LogWarning("[Health] Animator NULL (AnimatorParam mode)", this); return; }

        if (_hitExists) { SetParam(animator, _hitHash, hitParamName, _hitIsTrigger); return; }
        if (_hurtExists){ SetParam(animator, _hurtHash, hurtParamName, _hurtIsTrigger); return; }

        CacheAnimatorParams();
        if (_hitExists) { SetParam(animator, _hitHash, hitParamName, _hitIsTrigger); return; }
        if (_hurtExists){ SetParam(animator, _hurtHash, hurtParamName, _hurtIsTrigger); return; }

        var ctrl = (animator && animator.runtimeAnimatorController) ? animator.runtimeAnimatorController.name : "NULL";
        Debug.LogWarning("[Health] No param '" + hitParamName + "' or '" + hurtParamName + "' in controller '" + ctrl + "'", this);
    }

    // ── Wiring / Animator cache ────────────────────────────────
    void AutoWire()
    {
        if (!_playerReferences)
            _playerReferences = GetComponent<PlayerReferences>();

        if (!combatController)
            combatController = GetComponent<PlayerCombatController>()
                             ?? GetComponentInParent<PlayerCombatController>()
                             ?? GetComponentInChildren<PlayerCombatController>(true);

        if (!animator)
            animator = (_playerReferences && _playerReferences.MainAnimator ? _playerReferences.MainAnimator : null)
                    ?? GetComponentInChildren<Animator>(true)
                    ?? GetComponent<Animator>()
                    ?? GetComponentInParent<Animator>(true);
    }

    void CacheAnimatorParams()
    {
        _didRescanAfterSwap = false;
        _cachedCtrl = animator ? animator.runtimeAnimatorController : null;
        _hitExists = _hurtExists = _hitIsTrigger = _hurtIsTrigger = false;
        _hitHash = _hurtHash = 0;

        if (!animator) return;

        foreach (var p in animator.parameters)
        {
            if (p.name == hitParamName)
            {
                _hitHash = p.nameHash;
                _hitExists = true;
                _hitIsTrigger = (p.type == AnimatorControllerParameterType.Trigger);
            }
            if (p.name == hurtParamName)
            {
                _hurtHash = p.nameHash;
                _hurtExists = true;
                _hurtIsTrigger = (p.type == AnimatorControllerParameterType.Trigger);
            }
        }
    }

    void SetParam(Animator anim, int hash, string name, bool isTrigger)
    {
        if (isTrigger) anim.SetTrigger(hash);
        else { anim.SetBool(hash, true); StartCoroutine(ResetBoolNextFrame(anim, name)); }
    }

    IEnumerator ResetBoolNextFrame(Animator anim, string name)
    {
        yield return null;
        if (anim) anim.SetBool(name, false);
    }

    void RaiseHpEvents()
    {
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }
}
