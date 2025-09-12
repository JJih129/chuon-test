using UnityEngine;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class PlayerGuardController : MonoBehaviour
{
    [Header("▶ 디버그 옵션")]
    [SerializeField] bool debugLog = true;
    [SerializeField] bool autoFix = true;

    [Header("▶ 가드 입력")]
    public KeyCode guardKey = KeyCode.E;
    public bool holdToGuard = true;

    [Header("▶ 가드 판정")]
    [Range(30f, 180f)] public float guardConeAngleDeg = 110f;
    public bool requireFacingVelocity = true;

    [Header("▶ 가드 이동 제약")]
    [Range(0.1f, 1f)] public float guardMoveSpeedMultiplier = 0.7f;

    [Header("▶ 가드 피해 경감")]
    [Range(0f, 1f)] public float guardDamageMultiplier = 0.10f;

    [Header("▶ 패링 윈도우(초)")]
    [Range(0f, 0.20f)] public float parryWindowStartSec = 0.083f;
    [Range(0.10f, 0.40f)] public float parryWindowEndSec = 0.25f;

    [Header("▶ 애니메이션 & 이펙트")]
    public Animator animator;
    public string animParamIsGuarding = "IsGuarding";
    public string animParamParryTrigger = "Parry";
    public string animParamBlockTrigger = "GuardBlock";

    [Header("▶ 애니 레이어 (선택)")]
    public bool useSeparateGuardLayer = true;
    public string guardLayerName = "GuardLayer";
    [Range(0f, 1f)] public float guardLayerWeightOn = 1f;
    [Range(0f, 1f)] public float guardLayerWeightOff = 0f;

    [Header("▶ 스테이트 이름 (선택)")]
    public string guardIdleStateName = "GuardIdle";
    public string parryStateName = "ParrySuccess";
    public string guardBlockStateName = "GuardBlockHit";

    [Header("▶ 이펙트(선택)")]
    public AudioSource sfxGuardHit;
    public AudioSource sfxParry;
    public GameObject vfxGuardSparks;
    public GameObject vfxParryFlash;

    [Header("▶ 디버그 비주얼")]
    public bool debugDrawGuardCone = true;

    // ====== 공격 우선 정책 ======
    [Header("▶ 공격 우선 정책 (Souls-like)")]
    public bool attackHasPriority = true;
    public string[] suppressLayerNamesDuringAttack = new string[] { "GuardLayer" };
    public bool enforceSuppressEveryFrame = true;
    public bool autoResumeGuardIfHolding = true;

    // ====== (추가) 패링 성공 시 궁극기/브레이크 수급 ======
    [Header("▶ 패링 성공 시 수급(%) | 궁극기/브레이크")]
    [Tooltip("패링 성공 1회당 궁극기 게이지 증가량(%)")]
    public float addUltimateOnParry = 12.5f;
    [Tooltip("패링 성공 1회당 보스 브레이크 게이지 증가량(%)")]
    public float addBreakOnParry = 12.5f;

    [Header("▶ 참조(비우면 자동 탐색)")]
    public PlayerUltimateController player;   // 궁극기 컨트롤러
    public BossBreakController boss;          // 브레이크 컨트롤러

    // ====== 상태 ======
    public bool IsGuarding { get; private set; }
    public bool IsInParryWindow { get; private set; }

    float guardStartTime;
    Action<float> onGuardMoveScaleRequest;
    public event Action OnGuardStart;
    public event Action OnGuardEnd;
    public event Action OnParrySuccess;
    public event Action OnGuardBlock;

    int guardLayerIndex = -1;

    private bool _wasAttacking = false;
    private readonly Dictionary<int, float> _suppressedBackup = new Dictionary<int, float>();
    private readonly List<int> _suppressLayerIndices = new List<int>();

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        ResolveGuardLayerIndex();
        ValidateAnimatorParameters();
        CacheSuppressLayerIndices();

        // (추가) 자동 참조
        if (!player) player = FindObjectOfType<PlayerUltimateController>();
        if (!boss)   boss   = FindObjectOfType<BossBreakController>();
    }

    void Update()
    {
        HandleGuardInput();
        UpdateParryWindow();

        bool isAttacking = GetComponent<PlayerCombatController>()?.IsAttacking ?? false;

        if (attackHasPriority && isAttacking && !_wasAttacking)
            OnAttackStartedPolicy();

        if (attackHasPriority && !isAttacking && _wasAttacking)
            OnAttackEndedPolicy();

        _wasAttacking = isAttacking;

        if (attackHasPriority && isAttacking && enforceSuppressEveryFrame)
            ForceZeroSuppressedLayers();
    }

    void HandleGuardInput()
    {
        bool isAttacking = GetComponent<PlayerCombatController>()?.IsAttacking ?? false;
        if (attackHasPriority && isAttacking) return;

        if (holdToGuard)
        {
            if (Input.GetKeyDown(guardKey)) StartGuard();
            if (Input.GetKeyUp(guardKey)) EndGuard();
        }
        else
        {
            if (Input.GetKeyDown(guardKey))
            {
                if (!IsGuarding) StartGuard();
                else EndGuard();
            }
        }
    }

    public void StartGuard()
    {
        if (IsGuarding) return;
        IsGuarding = true;
        guardStartTime = Time.time;

        ApplyAnimatorGuardOn();
        onGuardMoveScaleRequest?.Invoke(guardMoveSpeedMultiplier);

        OnGuardStart?.Invoke();
        Log("[Guard] Start");
    }

    public void EndGuard()
    {
        if (!IsGuarding) return;
        IsGuarding = false;
        IsInParryWindow = false;

        ApplyAnimatorGuardOff();
        onGuardMoveScaleRequest?.Invoke(1f);

        OnGuardEnd?.Invoke();
        Log("[Guard] End");
    }

    void ApplyAnimatorGuardOn()
    {
        if (!animator) return;
        animator.SetBool(animParamIsGuarding, true);

        if (useSeparateGuardLayer && guardLayerIndex >= 0)
        {
            animator.SetLayerWeight(guardLayerIndex, guardLayerWeightOn);
            if (!string.IsNullOrEmpty(guardIdleStateName))
                animator.CrossFadeInFixedTime(guardIdleStateName, 0.05f, guardLayerIndex);
        }
        else if (autoFix && !useSeparateGuardLayer && !string.IsNullOrEmpty(guardIdleStateName))
        {
            animator.CrossFadeInFixedTime(guardIdleStateName, 0.05f, 0);
            Warn("autoFix: Base Layer로 GuardIdle 강제 진입");
        }
    }

    void ApplyAnimatorGuardOff()
    {
        if (!animator) return;
        animator.SetBool(animParamIsGuarding, false);

        if (useSeparateGuardLayer && guardLayerIndex >= 0)
            animator.SetLayerWeight(guardLayerIndex, guardLayerWeightOff);
    }

    void UpdateParryWindow()
    {
        if (!IsGuarding) { IsInParryWindow = false; return; }
        float t = Time.time - guardStartTime;
        IsInParryWindow = (t >= parryWindowStartSec && t <= parryWindowEndSec);
    }

    void OnAttackStartedPolicy()
    {
        if (IsGuarding) EndGuard();
        SuppressLayersToZero();
        Log("[Guard] Attack started: guard disabled and layers suppressed.");
    }

    void OnAttackEndedPolicy()
    {
        RestoreSuppressedLayers();
        if (autoResumeGuardIfHolding && holdToGuard && Input.GetKey(guardKey))
            StartGuard();

        Log("[Guard] Attack ended: layers restored, guard auto-resume if holding.");
    }

    void CacheSuppressLayerIndices()
    {
        _suppressLayerIndices.Clear();
        if (!animator) return;

        if (suppressLayerNamesDuringAttack == null) suppressLayerNamesDuringAttack = Array.Empty<string>();

        for (int i = 0; i < suppressLayerNamesDuringAttack.Length; i++)
        {
            string name = suppressLayerNamesDuringAttack[i];
            if (string.IsNullOrEmpty(name)) continue;
            int idx = animator.GetLayerIndex(name);
            if (idx >= 0)
            {
                if (!_suppressLayerIndices.Contains(idx))
                    _suppressLayerIndices.Add(idx);
            }
            else
            {
                Warn($"공격 중 억제할 레이어 '{name}' 를 찾지 못했습니다. (Animator 레이어명 확인)");
            }
        }

        if (useSeparateGuardLayer && !string.IsNullOrEmpty(guardLayerName))
        {
            int g = animator.GetLayerIndex(guardLayerName);
            if (g >= 0 && !_suppressLayerIndices.Contains(g))
                _suppressLayerIndices.Add(g);
        }
    }

    void SuppressLayersToZero()
    {
        if (!animator) return;
        _suppressedBackup.Clear();

        foreach (int idx in _suppressLayerIndices)
        {
            float prev = animator.GetLayerWeight(idx);
            _suppressedBackup[idx] = prev;
            animator.SetLayerWeight(idx, 0f);
        }
    }

    void ForceZeroSuppressedLayers()
    {
        if (!animator) return;
        foreach (int idx in _suppressLayerIndices)
            animator.SetLayerWeight(idx, 0f);
    }

    void RestoreSuppressedLayers()
    {
        if (!animator) return;

        foreach (var kv in _suppressedBackup)
        {
            animator.SetLayerWeight(kv.Key, kv.Value);
        }
        _suppressedBackup.Clear();
    }

    // ───────── 외부 공격 처리(원본 유지) ─────────
    public float ResolveIncomingAttack(Vector3 attackDirWorld, bool attackIsParryable, float baseDamage)
    {
        if (!IsGuarding) return baseDamage;
        if (!IsWithinGuardCone(attackDirWorld)) return baseDamage;

        if (IsInParryWindow && attackIsParryable)
        {
            TriggerParrySuccess();
            return 0f;
        }

        TriggerGuardBlock();
        float reduced = baseDamage * Mathf.Clamp01(guardDamageMultiplier);
        return reduced;
    }

    bool IsWithinGuardCone(Vector3 attackDirWorld)
    {
        Vector3 forward = transform.forward;
        Vector3 dirFromAttacker = (-attackDirWorld).normalized;
        float angle = Vector3.Angle(forward, dirFromAttacker);
        return angle <= (guardConeAngleDeg * 0.5f);
    }

    void TriggerParrySuccess()
    {
        OnParrySuccess?.Invoke();

        if (animator && !string.IsNullOrEmpty(animParamParryTrigger))
            animator.SetTrigger(animParamParryTrigger);

        int layer = (useSeparateGuardLayer && guardLayerIndex >= 0) ? guardLayerIndex : 0;
        if (animator && !string.IsNullOrEmpty(parryStateName))
            animator.CrossFadeInFixedTime(parryStateName, 0.03f, layer);

        if (sfxParry) sfxParry.Play();
        if (vfxParryFlash) SpawnVFX(vfxParryFlash, 0.25f);

        // ====== (추가) 패링 성공 시 수급 처리 ======
        if (player) player.AddGauge(addUltimateOnParry);
        if (boss)   boss.AddBreak(addBreakOnParry);
    }

    void TriggerGuardBlock()
    {
        OnGuardBlock?.Invoke();

        if (animator && !string.IsNullOrEmpty(animParamBlockTrigger))
            animator.SetTrigger(animParamBlockTrigger);

        int layer = (useSeparateGuardLayer && guardLayerIndex >= 0) ? guardLayerIndex : 0;
        if (animator && !string.IsNullOrEmpty(guardBlockStateName))
            animator.CrossFadeInFixedTime(guardBlockStateName, 0.03f, layer);

        if (sfxGuardHit) sfxGuardHit.Play();
        if (vfxGuardSparks) SpawnVFX(vfxGuardSparks, 0.5f);
    }

    void SpawnVFX(GameObject prefab, float life)
    {
        if (!prefab) return;
        var go = Instantiate(prefab, transform.position + Vector3.up * 1.0f, Quaternion.identity);
        Destroy(go, life);
    }

    public void RegisterGuardMoveScaleHook(Action<float> setter)
    {
        onGuardMoveScaleRequest = setter;
    }

    // ───────── 내부 유틸 ─────────
    void ResolveGuardLayerIndex()
    {
        guardLayerIndex = -1;
        if (animator && useSeparateGuardLayer)
        {
            guardLayerIndex = animator.GetLayerIndex(guardLayerName);
            if (guardLayerIndex < 0)
            {
                Warn($"Guard 레이어 '{guardLayerName}'를 찾지 못했습니다.");
                if (autoFix)
                {
                    useSeparateGuardLayer = false;
                    Warn("autoFix: 별도 레이어 사용 끔");
                }
            }
            else animator.SetLayerWeight(guardLayerIndex, guardLayerWeightOff);
        }
    }

    void ValidateAnimatorParameters()
    {
        if (!animator) return;
        bool hasGuarding = HasParam(animParamIsGuarding, AnimatorControllerParameterType.Bool);
        if (!hasGuarding) Err($"Animator 파라미터 누락: Bool '{animParamIsGuarding}'");
    }

    bool HasParam(string name, AnimatorControllerParameterType type)
    {
        foreach (var p in animator.parameters)
            if (p.type == type && p.name == name) return true;
        return false;
    }

    void Log(string msg) { if (debugLog) Debug.Log($"[Guard] {msg}", this); }
    void Warn(string msg) { Debug.LogWarning($"[Guard] {msg}", this); }
    void Err(string msg) { Debug.LogError($"[Guard] {msg}", this); }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!debugDrawGuardCone) return;
        Gizmos.color = Color.cyan;
        float half = guardConeAngleDeg * 0.5f;
        Quaternion left = Quaternion.AngleAxis(-half, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(half, Vector3.up);
        Vector3 pos = transform.position + Vector3.up * 1.0f;
        Gizmos.DrawRay(pos, left * transform.forward * 2.0f);
        Gizmos.DrawRay(pos, right * transform.forward * 2.0f);
        Gizmos.DrawWireSphere(pos, 0.1f);
    }
#endif
}
