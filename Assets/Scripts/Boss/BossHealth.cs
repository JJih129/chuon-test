// Assets/Scripts/Boss/BossHealth.cs
using System;
using UnityEngine;

/// <summary>
/// 보스용 체력 컴포넌트(IHealth 구현).
/// - HP/무적/경직/사망 이벤트 관리
/// - BossBreakController, BossController 등에서 공통으로 참조
/// </summary>
[DisallowMultipleComponent]
public class BossHealth : MonoBehaviour, IHealth, IUltimateTarget
{
    [Header("▶ 체력 설정")]
    [Tooltip("보스 최대 체력")]
    [Min(1)]
    [SerializeField] int maxHP = 100;

    [Tooltip("시작 체력 (0 이하이면 MaxHP로 시작)")]
    [SerializeField] int startHP = 0;

    [Header("▶ 무적/경직 옵션")]
    [Tooltip("시작 시 무적 상태로 시작할지 여부")]
    [SerializeField] bool invincibleOnStart = false;

    [Tooltip("피격 시 자동 무적 시간(초). 0이면 자동 무적 없음")]
    [Min(0f)]
    [SerializeField] float hitInvincibleDuration = 0f;

    [Tooltip("시작 시 경직 상태 여부(패턴에 따라 외부에서 변경 가능)")]
    [SerializeField] bool startStaggered = false;

    [Header("▶ 디버그/동작 옵션")]
    [Tooltip("사망 시 게임오브젝트 비활성화 여부")]
    [SerializeField] bool disableOnDeath = false;

    [Tooltip("디버그 로그 출력 여부")]
    [SerializeField] bool debugLog = false;

    // ===== IHealth 프로퍼티 =====
    public int MaxHP       => maxHP;
    public int CurrentHP   { get; private set; }
    public bool IsDead     { get; private set; }
    public bool IsStaggered { get; private set; }
    public bool isInvincible { get; set; }

    // ===== IHealth 이벤트 =====
    public event Action<int, int> OnHPChanged;
    public event Action<int, int> OnHealthChanged;

    public event Action<int> OnDamaged;
    public event Action<int, HitType> OnDamagedWithType;
    public event Action OnDied;

    public event Action OnStagger;

    // 내부 상태
    float _invincibleRemain;
    BossBreakController _breakController;

    void ResolveBreakController()
    {
        if (_breakController != null && _breakController.gameObject == gameObject)
            return;

        _breakController = GetComponent<BossBreakController>();
        if (_breakController == null)
            _breakController = GetComponentInParent<BossBreakController>();
    }

    void Awake()
    {
        if (startHP <= 0)
            CurrentHP = maxHP;
        else
            CurrentHP = Mathf.Clamp(startHP, 0, maxHP);

        IsDead      = CurrentHP <= 0;
        IsStaggered = startStaggered;
        isInvincible = invincibleOnStart;
        ResolveBreakController();

        if (invincibleOnStart && hitInvincibleDuration > 0f)
            _invincibleRemain = hitInvincibleDuration;

        // 초기 체력 브로드캐스트 (UI 동기화용)
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);

        if (debugLog)
            Debug.Log($"[BossHealth] Init HP={CurrentHP}/{MaxHP} invincible={isInvincible} staggered={IsStaggered}", this);

        enabled = _invincibleRemain > 0f;
    }

    void Update()
    {
        if (_invincibleRemain > 0f)
        {
            _invincibleRemain -= Time.deltaTime;
            if (_invincibleRemain <= 0f)
            {
                _invincibleRemain = 0f;
                isInvincible = false;

                if (debugLog)
                    Debug.Log("[BossHealth] Invincible OFF (timer end)", this);

                enabled = false;
            }
        }
    }

    // ===== IHealth 구현 =====

    /// <summary>체력 회복 (음수면 무시)</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;

        int prev = CurrentHP;
        CurrentHP = Mathf.Clamp(CurrentHP + amount, 0, MaxHP);
        if (CurrentHP == prev) return;

        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);

        if (debugLog)
            Debug.Log($"[BossHealth] Heal {amount} => {CurrentHP}/{MaxHP}", this);
    }

    /// <summary>부동소수 데미지는 내부에서 int로 변환 후 처리</summary>
    public void ApplyDamage(float amount)
    {
        int dmg = Mathf.RoundToInt(amount);
        if (dmg <= 0) return;

        TakeDamage(dmg, HitType.Normal, transform.position);
    }

    /// <summary>기본 데미지 처리(타입: Normal)</summary>
    public void ApplyDamage(int amount)
    {
        if (amount <= 0) return;

        TakeDamage(amount, HitType.Normal, transform.position);
    }

    /// <summary>
    /// 히트타입/히트 위치까지 포함한 데미지 처리.
    /// HitType.Force 는 무적을 무시하고 강제 데미지로 들어가도록 설계.
    /// </summary>
    public void TakeDamage(int damage, HitType hitType, Vector3 hitPoint)
    {
        if (damage <= 0) return;
        if (IsDead) return;

        // 강제 데미지가 아닌 이상 무적이면 무시
        if (isInvincible && hitType != HitType.Force) return;

        int prev = CurrentHP;
        CurrentHP = Mathf.Clamp(CurrentHP - damage, 0, MaxHP);

        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);
        OnDamaged?.Invoke(damage);
        OnDamagedWithType?.Invoke(damage, hitType);

        if (debugLog)
            Debug.Log($"[BossHealth] TakeDamage({hitType}) {damage} => {prev}->{CurrentHP}/{MaxHP}", this);

        if (CurrentHP <= 0 && !IsDead)
        {
            Die();
            return;
        }

        // 히트 후 자동 무적 (Force 타입 제외)
        if (hitInvincibleDuration > 0f && hitType != HitType.Force)
            SetInvincible(hitInvincibleDuration);
    }

    /// <summary>무적 토글 + 타이머 갱신</summary>
    public void SetInvincible(float duration)
    {
        if (duration <= 0f)
        {
            isInvincible = false;
            _invincibleRemain = 0f;
            enabled = false;

            if (debugLog)
                Debug.Log("[BossHealth] Invincible OFF (manual)", this);

            return;
        }

        isInvincible = true;
        _invincibleRemain = duration;
        enabled = true;

        if (debugLog)
            Debug.Log($"[BossHealth] Invincible ON ({duration:0.00}s)", this);
    }

    /// <summary>경직 상태 설정 (브레이크, 패턴 등에서 호출)</summary>
    // BossHealth.cs 안에 있는 SetStaggered를 이 버전으로 교체
    public void SetStaggered(bool stagger)
{
    if (IsStaggered == stagger)
        return;

    IsStaggered = stagger;

    if (debugLog)
        Debug.Log("[BossHealth] Stagger=" + (stagger ? "ON" : "OFF"), this);

    if (stagger)
        OnStagger?.Invoke();
}



    /// <summary>강제로 즉시 사망 처리</summary>
    public void Kill()
    {
        if (IsDead) return;

        CurrentHP = 0;
        Die();
    }

    public void ApplyUltimateDamage(int fixedDamage)
    {
        if (fixedDamage <= 0 || IsDead)
            return;

        int prev = CurrentHP;
        CurrentHP = Mathf.Clamp(CurrentHP - fixedDamage, 0, MaxHP);

        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);
        OnDamaged?.Invoke(fixedDamage);
        OnDamagedWithType?.Invoke(fixedDamage, HitType.Heavy);

        if (debugLog)
            Debug.Log($"[BossHealth] ApplyUltimateDamage {fixedDamage} => {prev}->{CurrentHP}/{MaxHP}", this);

        if (CurrentHP <= 0 && !IsDead)
        {
            Die();
            return;
        }
    }

    void Die()
    {
        if (IsDead) return;

        IsDead = true;

        if (debugLog)
            Debug.Log("[BossHealth] DIE", this);

        OnDied?.Invoke();

        if (disableOnDeath)
            gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        maxHP = Mathf.Max(1, maxHP);

        // 에디터에서 startHP를 0 이하로 두면 자동으로 MaxHP로 맞추기
        if (startHP <= 0)
            startHP = maxHP;
    }
#endif
}
