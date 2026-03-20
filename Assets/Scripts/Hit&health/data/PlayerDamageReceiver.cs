using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour, IDamageReceiver
{
    private PlayerReferences _playerReferences;
    private bool _hasHitTrigger;
    private bool _hasGuardBlockTrigger;

    [Header("Core References")]
    [SerializeField] private PlayerHealth health;
    [SerializeField] private Animator anim;
    [SerializeField] private PlayerGuardController guard;
    [SerializeField] private PerfectDodgeController perfectDodge;
    [SerializeField] private ParryFeedbackController feedback;

    [Header("Animator Parameters")]
    [SerializeField] private string hitTriggerParam = "Hit";
    [SerializeField] private string guardBlockTriggerParam = "GuardBlock";

    [Header("Defense Rules")]
    [Range(0f, 180f)]
    [SerializeField] private float frontArcDegrees = 120f;
    [Range(0f, 1f)]
    [SerializeField] private float chipDamageMul = 0.10f;
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;

    [Header("Move Lock")]
    [SerializeField] private float lockMoveOnParry = 0.35f;
    [SerializeField] private float lockMoveOnBlock = 0.20f;
    [SerializeField] private Behaviour moveController;

    [Header("Ultimate Gauge")]
    [SerializeField] private GameObject ultimateTarget;
    [SerializeField] private float ultimateGainOnParry = 10f;
    [SerializeField] private string[] ultimateAddMethodNames = { "AddGauge", "Gain" };

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
        if (!ultimateTarget) ultimateTarget = gameObject;
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
        ReceiveHit(payload.damage, payload.attacker, payload.hitPoint, false, payload.canPerfectDodge);
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable)
    {
        ReceiveHit(baseDamage, attacker, hitPoint, unblockable, true);
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable, bool allowPerfectDodge)
    {
        if (!health || health.IsDead) return;

        if (allowPerfectDodge && perfectDodge != null && perfectDodge.ResolvePerfectDodge(hitPoint, attacker))
        {
            Debug.Log(
                $"[PerfectDodge] SUCCESS attacker={(attacker ? attacker.name : "null")} hitPoint={hitPoint}",
                this);
            return;
        }

        if (allowPerfectDodge && perfectDodge != null && perfectDodge.enableLogs)
        {
            Debug.Log(
                $"[PerfectDodge] FAIL windowOpen={perfectDodge.IsWindowOpen} remain={perfectDodge.RemainingWindow:0.000} attacker={(attacker ? attacker.name : "null")}",
                this);
        }

        bool hasGuard = guard != null;
        bool isFront = IsFront(hitPoint, attacker);
        bool canDefense = hasGuard && isFront && !unblockable;

        bool isParryWindow = canDefense && guard.IsParryWindowOpen;
        bool isGuarding = canDefense && guard.IsGuarding && !isParryWindow;

        if (isParryWindow)
        {
            feedback?.PlayParryFeedback(hitPoint, attacker);
            LockMove(lockMoveOnParry);
            TryNotifyUltimateGain(ultimateGainOnParry);
            TryNotifyParryBreak(attacker);

            if (TutorialManager.Instance != null)
                TutorialManager.Instance.OnPlayerParrySuccess();

            Debug.Log("[PlayerDamageReceiver] Parry Success", this);
            return;
        }

        if (isGuarding)
        {
            float chip = baseDamage * chipDamageMul;
            if (chip > 0f)
                health.ApplyDamage(chip);

            feedback?.PlayGuardBlockFeedback(hitPoint, attacker);
            LockMove(lockMoveOnBlock);

            if (TutorialManager.Instance != null)
                TutorialManager.Instance.OnPlayerGuardSuccess();

            if (!suppressHitAnimWhenGuarding && anim && _hasGuardBlockTrigger)
            {
                anim.ResetTrigger(guardBlockTriggerParam);
                anim.SetTrigger(guardBlockTriggerParam);
            }

            Debug.Log($"[PlayerDamageReceiver] Guard Block, chip={chip}", this);
            return;
        }

        health.ApplyDamage(baseDamage);

        if (anim && _hasHitTrigger)
        {
            anim.ResetTrigger(hitTriggerParam);
            anim.SetTrigger(hitTriggerParam);
        }
    }

    void RefreshAnimatorParameterCache()
    {
        _hasHitTrigger = HasAnimatorParameter(anim, hitTriggerParam, AnimatorControllerParameterType.Trigger);
        _hasGuardBlockTrigger = HasAnimatorParameter(anim, guardBlockTriggerParam, AnimatorControllerParameterType.Trigger);
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

    bool IsFront(Vector3 hitPoint, Transform attacker)
    {
        Vector3 toHit = hitPoint - transform.position;
        toHit.y = 0f;

        if (toHit.sqrMagnitude < 0.0001f)
            return true;

        toHit.Normalize();

        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        float dot = Vector3.Dot(forward, toHit);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        return angle <= frontArcDegrees * 0.5f;
    }

    void LockMove(float duration)
    {
        if (!moveController || duration <= 0f) return;
        StartCoroutine(CoLockMove(duration));
    }

    IEnumerator CoLockMove(float duration)
    {
        moveController.enabled = false;
        yield return new WaitForSeconds(duration);
        moveController.enabled = true;
    }

    void TryNotifyUltimateGain(float amount)
    {
        if (ultimateTarget == null || amount <= 0f) return;

        MonoBehaviour[] comps = ultimateTarget.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour comp in comps)
        {
            if (comp == null) continue;

            System.Type type = comp.GetType();
            foreach (string methodName in ultimateAddMethodNames)
            {
                var method = type.GetMethod(methodName, new[] { typeof(float) });
                if (method == null) continue;

                method.Invoke(comp, new object[] { amount });
                return;
            }
        }
    }

    void TryNotifyParryBreak(Transform attacker)
    {
        if (attacker == null) return;

        BossBreakController breakController = attacker.GetComponentInParent<BossBreakController>();
        if (breakController == null) return;

        breakController.AddBreak(0f, BossBreakController.BreakSource.Parry);
    }
}
