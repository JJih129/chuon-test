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
    [SerializeField] private ParryFeedbackController feedback;

    [Header("Animator Parameters")]
    [SerializeField] private string hitTriggerParam = "Hit";

    [Header("Defense Rules")]
    [Range(0f, 180f)]
    [SerializeField] private float frontArcDegrees = 120f;
    [Range(0f, 1f)]
    [SerializeField] private float chipDamageMul = 0.10f;
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

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
            frontArcDegrees,
            chipDamageMul);
        ResolvedHitResult resolvedHit = PlayerIncomingHitResolver.Resolve(payload, defenseResolution);

        if (resolvedHit.IsPerfectDodge)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[PerfectDodge] SUCCESS attacker={(payload.attacker ? payload.attacker.name : "null")} hitPoint={payload.hitPoint}",
                    this);
            }
            return;
        }

        if (payload.canPerfectDodge && perfectDodge != null && perfectDodge.enableLogs)
        {
            Debug.Log(
                $"[PerfectDodge] FAIL windowOpen={perfectDodge.IsWindowOpen} remain={perfectDodge.RemainingWindow:0.000} attacker={(payload.attacker ? payload.attacker.name : "null")}",
                this);
        }

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
