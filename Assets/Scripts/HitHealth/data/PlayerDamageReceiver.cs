using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour, IDamageReceiver
{
    private PlayerReferences _playerReferences;
    private bool _hasHitTrigger;

    [Header("Core References")]
    [SerializeField] private PlayerHealth health;
    [SerializeField] private Animator anim;
    [SerializeField] private PlayerGuardController guard;
    [SerializeField] private PerfectDodgeController perfectDodge;
    [SerializeField] private PlayerDodgeController dodgeController;
    [SerializeField] private ParryFeedbackController feedback;

    [Header("Animator Parameters")]
    [SerializeField] private string hitTriggerParam = "Hit";

    [Header("Defense Rules")]
    [Tooltip("Fallback only. When a PlayerGuardController exists, its frontArcDegrees is the single source of truth.")]
    [Range(0f, 180f)]
    [SerializeField] private float frontArcDegrees = 120f;
    [Range(0f, 1f)]
    [SerializeField] private float chipDamageMul = 0.10f;
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;
    [SerializeField] private bool invulnerableWhileDodging = true;

    [Header("String Damage Scaling (다단 연타 버스트 완화)")]
    [Tooltip("같은 공격자의 연속 피격(간격 stringGapSeconds 이하)을 하나의 '연타'로 보고 피해를 줄인다.")]
    [SerializeField] private bool enableStringDamageScaling = true;
    [Tooltip("이 시간(초, 실시간) 안에 같은 공격자에게 다시 맞으면 같은 연타로 본다.")]
    [SerializeField, Min(0.05f)] private float stringGapSeconds = 0.85f;
    [Tooltip("연타의 몇 번째 피격부터 배율을 적용할지 (1부터).")]
    [SerializeField, Min(1)] private int stringScaleFromHit = 3;
    [Tooltip("연타 후반 피격 배율.")]
    [SerializeField, Range(0.1f, 1f)] private float stringLateHitMultiplier = 0.6f;
    [Tooltip("한 연타가 줄 수 있는 총 피해 상한 (최대 HP 비율). 0이면 상한 없음.")]
    [SerializeField, Range(0f, 1f)] private float stringDamageCapFraction = 0.45f;
    // 반복 20: 보스 오버홀 연타(2·3페이즈)는 타격 간격이 0.87~1.2초라 stringGapSeconds(0.85)를 넘어
    // 매 타가 '새 연타'로 취급돼 상한을 우회했다 (c 34/34/24, d 24/24 → 1초에 48~92 피해). 보스 공격자는 창을 넓히고 상한을 낮춘다.
    [Tooltip("보스 공격자 전용 연타 간격(초, 실시간). 이 시간 안에 다시 맞으면 같은 연타.")]
    [SerializeField, Min(0.05f)] private float bossStringGapSeconds = 1.3f;
    [Tooltip("보스 연타 창의 최대 길이(초). 이보다 길게 이어지면 새 연타로 본다 (무한 상한 방지).")]
    [SerializeField, Min(0.5f)] private float bossStringMaxWindowSeconds = 2.6f;
    [Tooltip("보스 한 연타 창의 총 피해 상한 (최대 HP 비율).")]
    [SerializeField, Range(0f, 1f)] private float bossStringDamageCapFraction = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    Transform _stringAttacker;
    float _stringLastHitRealtime = float.NegativeInfinity;
    int _stringLandedHits;
    float _stringDamageTotal;
    float _stringStartRealtime = float.NegativeInfinity;
    Transform _bossCheckAttacker;
    bool _bossCheckResult;

    /// <summary>QA용: 연타 스케일링으로 깎인 누적 피해.</summary>
    public float StringDamageReduced { get; private set; }

    void Awake()
    {
        _playerReferences = GetComponent<PlayerReferences>();
        if (!health) health = GetComponent<PlayerHealth>();
        if (!anim)
            anim = _playerReferences != null
                ? _playerReferences.MainAnimator ?? GetComponentInChildren<Animator>()
                : GetComponentInChildren<Animator>();
        if (!guard) guard = GetComponent<PlayerGuardController>();
        if (!perfectDodge) perfectDodge = GetComponent<PerfectDodgeController>();
        if (!dodgeController) dodgeController = GetComponent<PlayerDodgeController>();
        if (!feedback) feedback = GetComponent<ParryFeedbackController>();
        RefreshAnimatorParameterCache();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Runtime animator cache is initialized in Awake().
    }
#endif

    public void ReceiveHit(HitPayload payload)
    {
        ReceiveResolvedHit(payload);
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable)
    {
        ReceiveResolvedHit(BuildLegacyPayload(baseDamage, attacker, hitPoint, unblockable, true, true));
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable, bool allowPerfectDodge)
    {
        ReceiveResolvedHit(BuildLegacyPayload(baseDamage, attacker, hitPoint, unblockable, allowPerfectDodge, true));
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable, bool allowPerfectDodge, bool allowParry)
    {
        ReceiveResolvedHit(BuildLegacyPayload(baseDamage, attacker, hitPoint, unblockable, allowPerfectDodge, allowParry));
    }

    /// <summary>반복 16(Type-D 개편): 해석·적용된 피격 (퍼펙트 회피 포함). hpLost = 실제로 깎인 HP. 띄워짐·가드 밀림·블링크·강패링이 구독한다.</summary>
    public static event System.Action<PlayerDamageReceiver, HitPayload, ResolvedHitResult, int> AnyHitResolved;

    void ReceiveResolvedHit(HitPayload payload)
    {
        if (!health || health.IsDead) return;

        PlayerDefenseResolution defenseResolution = PlayerDefenseResolver.Resolve(
            transform,
            guard,
            perfectDodge,
            payload.hitPoint,
            payload.attacker,
            payload.damage,
            payload.unblockable,
            payload.canPerfectDodge,
            payload.canParry,
            payload.canGuard,
            guard != null ? guard.frontArcDegrees : frontArcDegrees,
            chipDamageMul);
        ResolvedHitResult resolvedHit = PlayerIncomingHitResolver.Resolve(payload, defenseResolution);
        float stringRawDamage = 0f;
        if (resolvedHit.IsDamage && enableStringDamageScaling)
        {
            stringRawDamage = resolvedHit.HealthDamage;
            resolvedHit = ScaleStringHit(resolvedHit, payload.attacker);
        }
        int hpBefore = health.CurrentHP;

        if (resolvedHit.IsPerfectDodge)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[PerfectDodge] SUCCESS attacker={(payload.attacker ? payload.attacker.name : "null")} hitPoint={payload.hitPoint}",
                    this);
            }
            AnyHitResolved?.Invoke(this, payload, resolvedHit, 0);
            return;
        }

        // 회피 전체가 아니라 회피 시작 직후 i-frame 구간만 무적 처리.
        if (invulnerableWhileDodging && dodgeController != null && dodgeController.IsInvulnerable)
        {
            if (debugLogs)
                Debug.Log("[PlayerDamageReceiver] Dodge invulnerability ignored hit.", this);
            return;
        }

        if (payload.canPerfectDodge && perfectDodge != null && perfectDodge.enableLogs)
        {
            Debug.Log(
                $"[PerfectDodge] FAIL windowOpen={perfectDodge.IsWindowOpen} remain={perfectDodge.RemainingWindow:0.000} attacker={(payload.attacker ? payload.attacker.name : "null")}",
                this);
        }

        bool heavyHit = PlayerBalance.IsHeavyHit(payload.hitType, payload.unblockable);
        if (guard != null)
            guard.BeginIncomingHit(payload.damage, heavyHit);

        try
        {
            PlayerResolvedHitApplier.Apply(
                resolvedHit,
                payload,
                health,
                guard,
                feedback,
                anim,
                _hasHitTrigger,
                hitTriggerParam,
                suppressHitAnimWhenGuarding,
                debugLogs,
                this);

            // 연타 카운트는 실제로 HP가 깎인 피격만 센다 (피격 유예로 무시된 히트 제외)
            if (resolvedHit.IsDamage && enableStringDamageScaling && health.CurrentHP < hpBefore)
            {
                _stringAttacker = payload.attacker;
                _stringLastHitRealtime = Time.realtimeSinceStartup;
                _stringLandedHits++;
                _stringDamageTotal += hpBefore - health.CurrentHP;
                StringDamageReduced += Mathf.Max(0f, stringRawDamage - (hpBefore - health.CurrentHP));
            }

            // 가드 없이 맞으면 밸런스(체간) 누적
            if (resolvedHit.IsDamage && guard != null && health != null && !health.IsDead)
                guard.ApplyBalanceFromUnguardedHit(resolvedHit.HealthDamage, heavyHit);
        }
        finally
        {
            if (guard != null)
                guard.EndIncomingHit();
        }

        AnyHitResolved?.Invoke(this, payload, resolvedHit, Mathf.Max(0, hpBefore - health.CurrentHP));
    }

    // 같은 공격자의 연속 피격: N타째부터 배율, 연타 총 피해는 최대 HP 비율로 상한.
    ResolvedHitResult ScaleStringHit(ResolvedHitResult hit, Transform attacker)
    {
        float now = Time.realtimeSinceStartup;
        bool bossAttacker = IsBossAttacker(attacker);
        float gap = bossAttacker ? Mathf.Max(stringGapSeconds, bossStringGapSeconds) : stringGapSeconds;
        bool sameString = attacker != null && attacker == _stringAttacker && now - _stringLastHitRealtime <= gap
            && (!bossAttacker || now - _stringStartRealtime <= bossStringMaxWindowSeconds);
        if (!sameString)
        {
            _stringLandedHits = 0;
            _stringDamageTotal = 0f;
            _stringStartRealtime = now;
        }

        float damage = hit.HealthDamage;
        if (_stringLandedHits + 1 >= stringScaleFromHit)
            damage *= stringLateHitMultiplier;

        float capFraction = bossAttacker && bossStringDamageCapFraction > 0f
            ? (stringDamageCapFraction > 0f ? Mathf.Min(stringDamageCapFraction, bossStringDamageCapFraction) : bossStringDamageCapFraction)
            : stringDamageCapFraction;
        if (capFraction > 0f && health != null)
        {
            float cap = health.MaxHP * capFraction;
            // 첫 타는 상한과 무관하게 원래 피해 (단발 강공격은 그대로)
            if (_stringLandedHits > 0)
                damage = Mathf.Min(damage, Mathf.Max(0f, cap - _stringDamageTotal));
        }

        if (Mathf.Approximately(damage, hit.HealthDamage))
            return hit;

        if (debugLogs)
            Debug.Log($"[PlayerDamageReceiver] String hit #{_stringLandedHits + 1}: {hit.HealthDamage:0.#} -> {damage:0.#} (total {_stringDamageTotal:0.#})", this);

        return ResolvedHitResult.Damage(damage, hit.HitType, hit.BreakBonus, hit.ShouldSuppressHitAnimation);
    }

    bool IsBossAttacker(Transform attacker)
    {
        if (attacker == null)
            return false;
        if (attacker != _bossCheckAttacker)
        {
            _bossCheckAttacker = attacker;
            _bossCheckResult = attacker.GetComponentInParent<BossController>() != null;
        }
        return _bossCheckResult;
    }

    void RefreshAnimatorParameterCache()
    {
        _hasHitTrigger = HasAnimatorParameter(anim, hitTriggerParam, AnimatorControllerParameterType.Trigger);
    }

    static bool HasAnimatorParameter(Animator targetAnimator, string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null || string.IsNullOrWhiteSpace(parameterName))
            return false;

        foreach (var parameter in targetAnimator.parameters)
        {
            if (parameter.type == expectedType && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    static HitPayload BuildLegacyPayload(
        float baseDamage,
        Transform attacker,
        Vector3 hitPoint,
        bool unblockable,
        bool allowPerfectDodge,
        bool allowParry)
    {
        return new HitPayload
        {
            damage = baseDamage,
            hitType = HitType.Normal,
            hitPoint = hitPoint,
            hitDirection = Vector3.zero,
            attacker = attacker,
            canParry = allowParry,
            canPerfectDodge = allowPerfectDodge,
            canGuard = !unblockable,
            causesGuardBreak = false,
            unblockable = unblockable
        };
    }
}
