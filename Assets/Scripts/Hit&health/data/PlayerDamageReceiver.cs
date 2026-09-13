using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour, IHitReceiver, IDamageReceiver
{
    [Header("References")]
    public PlayerHealth health;
    public Animator anim;
    public PlayerGuardController guard;
    public PerfectDodgeController perfectDodge;
    public ParryFeedbackController feedback;

    [Header("Legacy Animator Fallback")]
    public string hitTriggerParam = "Hit";
    public string guardBlockTriggerParam = "GuardBlock";

    [Header("Debug")]
    public bool parryDebugLogs = true;

    [Header("Legacy Tuning")]
    public float frontArcDegrees = 100f;
    public float chipDamageMul = 0.1f;
    public bool suppressHitAnimWhenGuarding = true;
    public float lockMoveOnParry = 0.35f;
    public float lockMoveOnBlock = 0.2f;
    public PlayerMoveController moveController;

    [Header("Legacy Ultimate Fallback")]
    public GameObject ultimateTarget;
    public float ultimateGainOnParry = 10f;
    public string[] ultimateAddMethodNames =
    {
        "AddGauge",
        "AddUltimateGauge",
        "AddUltimate",
        "Gain",
        "OnParryUltimateGain",
    };

    int _hitTriggerHash;
    int _guardBlockTriggerHash;

    void Reset()
    {
        AutoWire();
        CacheHashes();
    }

    void Awake()
    {
        AutoWire();
        CacheHashes();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        CacheHashes();
    }
#endif

    public void ReceiveHit(HitData hit)
    {
        if (hit == null)
            return;

        ResolveHit(
            hit.damage,
            hit.attackerTransform,
            hit.hitPoint,
            hit.isParryable,
            true,
            false,
            false,
            hit.hitType);
    }

    public void ReceiveHit(int damage, Transform attacker, Vector3 hitPoint, bool isParryable)
    {
        ResolveHit(
            damage,
            attacker,
            hitPoint,
            isParryable,
            true,
            false,
            false,
            HitType.Normal);
    }

    public void ReceiveHit(HitPayload payload)
    {
        ResolveHit(
            Mathf.RoundToInt(payload.damage),
            payload.attacker,
            payload.hitPoint,
            payload.canParry,
            payload.canGuard,
            payload.isUnblockable,
            payload.causesGuardBreak,
            payload.hitType);
    }

    void ResolveHit(
        int damage,
        Transform attacker,
        Vector3 hitPoint,
        bool canParry,
        bool canGuard,
        bool isUnblockable,
        bool causesGuardBreak,
        HitType hitType)
    {
        AutoWire();

        if (health == null || health.IsDead || damage <= 0)
            return;

        if (perfectDodge != null && perfectDodge.ResolvePerfectDodge(hitPoint, attacker))
            return;

        if (guard != null)
        {
            if (guard.EvaluateDefense(hitPoint, out bool isParry, out bool isBlock, out float chipMul))
            {
                if (isParry && !canParry)
                {
                    isParry = false;
                    isBlock = canGuard && !isUnblockable;
                }

                if (isParry)
                {
                    if (parryDebugLogs)
                        Debug.Log($"[ParryDebug][DamageReceiver] ResolveHit -> parry frame={Time.frameCount} time={Time.time:0.000} attacker={(attacker ? attacker.name : "null")} hitPoint={hitPoint}", this);

                    guard.PlayParrySuccess();
                    TryNotifyParryReaction(attacker, hitPoint);
                    TryGrantUltimateFallbackIfNeeded();
                    return;
                }

                if (isBlock && canGuard && !isUnblockable)
                {
                    int chipDamage = Mathf.RoundToInt(damage * Mathf.Clamp01(chipMul));
                    if (chipDamage > 0)
                        health.ApplyChipDamage(chipDamage);

                    guard.PlayBlockReaction();

                    if (causesGuardBreak)
                        guard.SetGuarding(false);

                    return;
                }
            }
        }

        health.TakeDamage(damage, hitType, hitPoint);
    }

    void AutoWire()
    {
        if (health == null)
            health = GetComponent<PlayerHealth>()
                  ?? GetComponentInParent<PlayerHealth>()
                  ?? GetComponentInChildren<PlayerHealth>(true);

        if (anim == null)
            anim = GetComponentInChildren<Animator>(true)
                ?? GetComponent<Animator>()
                ?? GetComponentInParent<Animator>(true);

        if (guard == null)
            guard = GetComponent<PlayerGuardController>()
                 ?? GetComponentInParent<PlayerGuardController>()
                 ?? GetComponentInChildren<PlayerGuardController>(true);

        if (perfectDodge == null)
            perfectDodge = GetComponent<PerfectDodgeController>()
                         ?? GetComponentInParent<PerfectDodgeController>()
                         ?? GetComponentInChildren<PerfectDodgeController>(true);

        if (feedback == null)
            feedback = GetComponent<ParryFeedbackController>()
                     ?? GetComponentInParent<ParryFeedbackController>()
                     ?? GetComponentInChildren<ParryFeedbackController>(true);

        if (moveController == null)
            moveController = GetComponent<PlayerMoveController>()
                          ?? GetComponentInParent<PlayerMoveController>()
                          ?? GetComponentInChildren<PlayerMoveController>(true);

        if (ultimateTarget == null)
            ultimateTarget = gameObject;
    }

    void CacheHashes()
    {
        _hitTriggerHash = string.IsNullOrEmpty(hitTriggerParam) ? 0 : Animator.StringToHash(hitTriggerParam);
        _guardBlockTriggerHash = string.IsNullOrEmpty(guardBlockTriggerParam) ? 0 : Animator.StringToHash(guardBlockTriggerParam);
    }

    void TryGrantUltimateFallbackIfNeeded()
    {
        if (guard != null && guard.grantUltimateOnParry)
            return;

        if (ultimateTarget == null || ultimateAddMethodNames == null || ultimateAddMethodNames.Length == 0)
            return;

        var behaviours = ultimateTarget.GetComponents<MonoBehaviour>();
        foreach (string methodName in ultimateAddMethodNames)
        {
            if (string.IsNullOrEmpty(methodName))
                continue;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                var withFloat = type.GetMethod(methodName, new[] { typeof(float) });
                if (withFloat != null)
                {
                    withFloat.Invoke(behaviour, new object[] { ultimateGainOnParry });
                    return;
                }

                var withoutArgs = type.GetMethod(methodName, System.Type.EmptyTypes);
                if (withoutArgs != null)
                {
                    withoutArgs.Invoke(behaviour, null);
                    return;
                }
            }
        }
    }

    void TryNotifyParryReaction(Transform attacker, Vector3 hitPoint)
    {
        if (TryNotifyParryReactionFromTransform(attacker))
            return;

        BossController[] bosses = FindObjectsOfType<BossController>();
        BossController nearest = null;
        float nearestSq = float.PositiveInfinity;

        for (int i = 0; i < bosses.Length; i++)
        {
            BossController boss = bosses[i];
            if (boss == null || !boss.isActiveAndEnabled)
                continue;

            float sq = (boss.transform.position - hitPoint).sqrMagnitude;
            if (sq < nearestSq)
            {
                nearestSq = sq;
                nearest = boss;
            }
        }

        if (nearest != null)
        {
            if (parryDebugLogs)
                Debug.Log($"[ParryDebug][DamageReceiver] Fallback boss notify -> {nearest.name} frame={Time.frameCount} time={Time.time:0.000}", this);
            nearest.OnParried(gameObject, 0f, 0f);
        }
        else if (parryDebugLogs)
        {
            Debug.LogWarning($"[ParryDebug][DamageReceiver] No boss found for fallback notify frame={Time.frameCount} time={Time.time:0.000}", this);
        }
    }

    bool TryNotifyParryReactionFromTransform(Transform attacker)
    {
        if (attacker == null)
            return false;

        MonoBehaviour[] behaviours = attacker.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IParryReact parryReact)
            {
                if (parryDebugLogs)
                    Debug.Log($"[ParryDebug][DamageReceiver] Direct notify -> {behaviours[i].GetType().Name} on {behaviours[i].name} frame={Time.frameCount} time={Time.time:0.000}", this);
                parryReact.OnParried(gameObject, 0f, 0f);
                return true;
            }
        }

        return false;
    }
}
