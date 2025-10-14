// PlayerGuardController.cs
// 목적: 가드/패링 + 궁극기/브레이크 연동(패링 시 게이지 수급, 애니/VFX/SFX 트리거)
// 변경: IsWithinGuardCone 버그 수정, ResolveIncomingAttack 벡터 정리, unreachable 코드 제거

using UnityEngine;
using System;
using System.Collections.Generic;

public interface IBreakGauge { void AddBreak(float percent); }

[DisallowMultipleComponent]
public class PlayerGuardController : MonoBehaviour
{
    [Header("▶ 디버그 옵션")]
    [SerializeField] bool debugLog = true;
    [SerializeField] bool autoFix = true;

    [Header("▶ 가드 입력")]
    public KeyCode guardKey = KeyCode.E;
    public bool holdToGuard = true;

    [Header("▶ 가드 이동 제약")]
    [Range(0.1f, 1f)] public float guardMoveSpeedMultiplier = 0.7f;

    [Header("▶ 가드 피해 경감")]
    [Range(0f, 1f)] public float guardDamageMultiplier = 0.10f;

    [Header("▶ 패링 윈도우(초)")]
    [Range(0f, 0.20f)] public float parryWindowStartSec = 0.083f;
    [Range(0.10f, 0.40f)] public float parryWindowEndSec = 0.25f;

    [Header("▶ 애니메이션 파라미터")]
    public string animParamIsGuarding = "IsGuarding";
    public string animParamParryTrigger = "Parry";
    public string animParamBlockTrigger = "GuardBlock";

    [Header("▶ 가드 전용 레이어(선택)")]
    public bool useSeparateGuardLayer = true;
    public string guardLayerName = "GuardLayer";
    [Range(0f, 1f)] public float guardLayerWeightOn = 1f;
    [Range(0f, 1f)] public float guardLayerWeightOff = 0f;

    [Header("▶ 스테이트 이름(선택)")]
    public string guardIdleStateName = "GuardIdle";
    public string parryStateName = "ParrySuccess";
    public string guardBlockStateName = "GuardBlockHit";

    [Header("▶ 이펙트(선택)")]
    public AudioSource sfxGuardHit;
    public AudioSource sfxParry;
    public GameObject vfxGuardSparks;
    public GameObject vfxParryFlash;

    [Header("▶ 공격 우선 정책(Souls-like)")]
    public bool attackHasPriority = true;
    public string[] suppressLayerNamesDuringAttack = new string[] { "GuardLayer" };
    public bool enforceSuppressEveryFrame = true;
    public bool autoResumeGuardIfHolding = true;

    [Header("▶ 패링 성공 보상(%)")]
    public float addUltimateOnParry = 12.5f;
    public float addBreakOnParry = 12.5f;

    [Header("▶ 참조(비우면 자동 탐색)")]
    public MonoBehaviour combatStateReader; // ICombatStateReader
    public PlayerUltimateController ultimate;
    public MonoBehaviour breakGaugeProvider; // IBreakGauge

    [Header("▶ 디버그 비주얼")]
    public bool debugDrawGuardCone = true;

    [Header("▶ 가드 콘")]
    [SerializeField] float guardConeAngleDeg = 140f;
    [SerializeField] float guardConeEpsilonDeg = 3f;
    [SerializeField] Transform forwardRef;
    [SerializeField] float forwardYawOffsetDeg = 0f;

    // 내부 상태
    public bool IsGuarding { get; private set; }
    public bool IsInParryWindow { get; private set; }

    Animator animator;
    int guardLayerIndex = -1;
    float guardStartTime;

    ICombatStateReader _combat;
    IBreakGauge _break;

    bool _wasAttacking = false;
    readonly Dictionary<int, float> _suppressedBackup = new();
    readonly List<int> _suppressLayerIndices = new();

    bool _lockGuardByUltimate = false;

    Action<float> onGuardMoveScaleRequest;
    public event Action OnGuardStart;
    public event Action OnGuardEnd;
    public event Action OnParrySuccess;
    public event Action OnGuardBlock;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        _combat = combatStateReader as ICombatStateReader ?? GetComponent<ICombatStateReader>();
        if (!ultimate) ultimate = GetComponent<PlayerUltimateController>() ?? FindFirstObjectByType<PlayerUltimateController>();

        _break = breakGaugeProvider as IBreakGauge;
        if (_break == null && breakGaugeProvider == null)
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IBreakGauge ig) { _break = ig; break; }
        }

        ResolveGuardLayerIndex();
        ValidateAnimatorParameters();
        CacheSuppressLayerIndices();
    }

    void OnEnable()
    {
        if (ultimate != null)
        {
            ultimate.OnUltimateStarted += HandleUltimateStarted;
            ultimate.OnUltimateEnded += HandleUltimateEnded;
        }
    }
    void OnDisable()
    {
        if (ultimate != null)
        {
            ultimate.OnUltimateStarted -= HandleUltimateStarted;
            ultimate.OnUltimateEnded   -= HandleUltimateEnded;
        }
    }

    void Update()
    {
        HandleGuardInput();
        UpdateParryWindow();

        bool isAttacking = _combat != null && _combat.IsAttacking();
        if (attackHasPriority && isAttacking && !_wasAttacking) OnAttackStartedPolicy();
        if (attackHasPriority && !isAttacking && _wasAttacking) OnAttackEndedPolicy();
        _wasAttacking = isAttacking;

        if (attackHasPriority && isAttacking && enforceSuppressEveryFrame) ForceZeroSuppressedLayers();
    }

    void HandleGuardInput()
    {
        if (_lockGuardByUltimate) return;
        bool isAttacking = _combat != null && _combat.IsAttacking();
        if (attackHasPriority && isAttacking) return;

        if (holdToGuard)
        {
            if (Input.GetKeyDown(guardKey)) StartGuard();
            if (Input.GetKeyUp(guardKey))   EndGuard();
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
        Log("Start");
    }

    public void EndGuard()
    {
        if (!IsGuarding) return;
        IsGuarding = false;
        IsInParryWindow = false;

        ApplyAnimatorGuardOff();
        onGuardMoveScaleRequest?.Invoke(1f);
        OnGuardEnd?.Invoke();
        Log("End");
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
        Log("Attack started → guard off, layers suppressed");
    }
    void OnAttackEndedPolicy()
    {
        RestoreSuppressedLayers();
        if (autoResumeGuardIfHolding && holdToGuard && Input.GetKey(guardKey)) StartGuard();
        Log("Attack ended → layers restored, auto-resume if holding");
    }

    void CacheSuppressLayerIndices()
    {
        _suppressLayerIndices.Clear();
        if (!animator) return;

        if (suppressLayerNamesDuringAttack == null) suppressLayerNamesDuringAttack = Array.Empty<string>();
        foreach (string name in suppressLayerNamesDuringAttack)
        {
            if (string.IsNullOrEmpty(name)) continue;
            int idx = animator.GetLayerIndex(name);
            if (idx >= 0 && !_suppressLayerIndices.Contains(idx)) _suppressLayerIndices.Add(idx);
        }

        if (useSeparateGuardLayer && !string.IsNullOrEmpty(guardLayerName))
        {
            int g = animator.GetLayerIndex(guardLayerName);
            if (g >= 0 && !_suppressLayerIndices.Contains(g)) _suppressLayerIndices.Add(g);
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
        foreach (int idx in _suppressLayerIndices) animator.SetLayerWeight(idx, 0f);
    }
    void RestoreSuppressedLayers()
    {
        if (!animator) return;
        foreach (var kv in _suppressedBackup) animator.SetLayerWeight(kv.Key, kv.Value);
        _suppressedBackup.Clear();
    }

    // attackDirWorld: 공격자 -> 플레이어(월드)
    // attackIsParryable: 패링 가능 여부
    // return: 최종 데미지
    public float ResolveIncomingAttack(Vector3 attackDirWorld, bool attackIsParryable, float baseDamage)
    {
        if (!IsGuarding) return baseDamage;

        // Convert attacker->player -> player->attacker for cone test
        Vector3 playerToAttacker = -attackDirWorld;

        if (!IsWithinGuardCone(playerToAttacker)) return baseDamage;

        if (IsInParryWindow && attackIsParryable)
        {
            TriggerParrySuccess();
            return 0f;
        }

        TriggerGuardBlock();
        return baseDamage * Mathf.Clamp01(guardDamageMultiplier);
    }

    // 콘 판정: 수평면(Yaw) 비교 + 오프셋 적용
    bool IsWithinGuardCone(Vector3 playerToAttacker)
    {
        // forward reference and yaw offset applied first
        Vector3 fwd = forwardRef ? forwardRef.forward : transform.forward;
        fwd = Quaternion.AngleAxis(forwardYawOffsetDeg, Vector3.up) * fwd;

        // 수평면 투영
        Vector3 fwdFlat = new Vector3(fwd.x, 0f, fwd.z);
        Vector3 dirFlat = new Vector3(playerToAttacker.x, 0f, playerToAttacker.z);

        if (fwdFlat.sqrMagnitude < 0.0001f || dirFlat.sqrMagnitude < 0.0001f) return false;

        fwdFlat.Normalize();
        dirFlat.Normalize();

        float angle = Vector3.Angle(fwdFlat, dirFlat);
        if (debugLog) Debug.Log($"[Guard] cone angle={angle:F1} half={guardConeAngleDeg*0.5f:F1}", this);

        return angle <= (guardConeAngleDeg * 0.5f + guardConeEpsilonDeg);
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

    void TriggerParrySuccess()
    {
        OnParrySuccess?.Invoke();

        if (animator && !string.IsNullOrEmpty(animParamParryTrigger))
            animator.SetTrigger(animParamParryTrigger);

        int layer = (useSeparateGuardLayer && guardLayerIndex >= 0) ? guardLayerIndex : 0;
        if (animator && !string.IsNullOrEmpty(parryStateName))
            animator.CrossFadeInFixedTime(parryStateName, 0.03f, layer);

        if (sfxParry) sfxParry.Play();
        if (vfxParryFlash) SpawnVFX(vfxParryFlash, 0.6f);

        try { if (ultimate) ultimate.AddGauge(addUltimateOnParry); } catch { }
        _break?.AddBreak(addBreakOnParry);
    }

    void HandleUltimateStarted()
    {
        _lockGuardByUltimate = true;
        if (IsGuarding) EndGuard();
        SuppressLayersToZero();
        Log("Ultimate started → guard locked, layers suppressed");
    }

    void HandleUltimateEnded()
    {
        _lockGuardByUltimate = false;
        RestoreSuppressedLayers();
        Log("Ultimate ended → guard unlocked, layers restored");
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

    void SpawnVFX(GameObject prefab, float life)
    {
        if (!prefab) return;
        var go = Instantiate(prefab, transform.position + Vector3.up * 1.0f, Quaternion.identity);
        Destroy(go, life);
    }

    public void RegisterGuardMoveScaleHook(Action<float> setter) => onGuardMoveScaleRequest = setter;

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
        if (!HasParam(animParamIsGuarding, AnimatorControllerParameterType.Bool))
            Err($"Animator 파라미터 누락: Bool '{animParamIsGuarding}'");
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
        Quaternion left = Quaternion.AngleAxis(-half + forwardYawOffsetDeg, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(half + forwardYawOffsetDeg, Vector3.up);
        Vector3 pos = transform.position + Vector3.up * 1.0f;
        Gizmos.DrawRay(pos, left * transform.forward * 2.0f);
        Gizmos.DrawRay(pos, right * transform.forward * 2.0f);
        Gizmos.DrawWireSphere(pos, 0.1f);
    }
#endif
}
