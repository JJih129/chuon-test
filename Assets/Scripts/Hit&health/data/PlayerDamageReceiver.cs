using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private PlayerHealth health;
    [SerializeField] private Animator anim;
    [SerializeField] private PlayerGuardController guard;
    [SerializeField] private PerfectDodgeController perfectDodge;
    [SerializeField] private ParryFeedbackController feedback;

    [Header("애니메이터 파라미터명")]
    [SerializeField] private string hitTriggerParam = "Hit";
    [SerializeField] private string guardBlockTriggerParam = "GuardBlock";

    [Header("가드/방향/칩데미지")]
    [Range(0f,180f)][SerializeField] private float frontArcDegrees = 100f;
    [Range(0f,1f)]   [SerializeField] private float chipDamageMul = 0.10f;
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;

    [Header("이동 잠금")]
    [SerializeField] private float lockMoveOnParry = 0.35f;
    [SerializeField] private float lockMoveOnBlock = 0.20f;
    [SerializeField] private MonoBehaviour simpleInputBlocker; // BlockForSeconds(float)
    [SerializeField] private Behaviour moveController;

    [Header("패링 → 궁극기 수급(옵션)")]
    [SerializeField] private GameObject ultimateTarget;
    [SerializeField] private float ultimateGainOnParry = 10f;
    [SerializeField] private string[] ultimateAddMethodNames =
        { "AddGauge", "AddUltimateGauge", "AddUltimate", "Gain", "OnParryUltimateGain" };

    [Header("디버그")]
    [SerializeField] private bool debugLog = true;

    void Reset()
    {
        health        = GetComponent<PlayerHealth>();
        anim          = GetComponentInChildren<Animator>();
        guard         = GetComponent<PlayerGuardController>();
        perfectDodge  = GetComponent<PerfectDodgeController>();
        feedback      = GetComponent<ParryFeedbackController>();
        if (!ultimateTarget) ultimateTarget = gameObject;
        if (!moveController) moveController = GetComponent<Behaviour>();
    }

    void Awake()
    {
        if (!health)       health       = GetComponent<PlayerHealth>();
        if (!anim)         anim         = GetComponentInChildren<Animator>();
        if (!guard)        guard        = GetComponent<PlayerGuardController>();
        if (!perfectDodge) perfectDodge = GetComponent<PerfectDodgeController>();
        if (!feedback)     feedback     = GetComponent<ParryFeedbackController>();
        if (!ultimateTarget) ultimateTarget = gameObject;
    }

    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable)
    {
        if (!health || health.IsDead) return;

        // 0) 퍼펙트 회피 최우선 판정
        if (perfectDodge != null && perfectDodge.ResolvePerfectDodge(hitPoint, attacker))
        {
            if (debugLog) Debug.Log($"[PDR] RESULT=PERFECT_DODGE | dmg=0", this);
            return;
        }

        // 1) 전방 여부(가드/패링에 필요)
        bool isFront = IsFront(hitPoint, attacker);
        bool canDefense = !unblockable && guard != null && isFront;

        bool parry = canDefense && guard.IsParryWindowOpen;
        bool block = canDefense && guard.IsGuarding && !parry;

        if (debugLog)
            Debug.Log($"[PDR] FRONT={isFront} | parry={parry} block={block} | base={baseDamage}", this);

        // 2) 패링
        if (parry)
        {
            feedback?.PlayParryFeedback(hitPoint, attacker);
            LockMove(lockMoveOnParry);
            TryNotifyUltimateGain(ultimateGainOnParry);

            if (debugLog) Debug.Log($"[PDR] RESULT=PARRY | dmg=0", this);
            return;
        }

        // 3) 블록(칩)
        if (block)
        {
            int dmg = Mathf.CeilToInt(baseDamage * chipDamageMul);
            health.ApplyDamage(dmg);

            if (!string.IsNullOrEmpty(guardBlockTriggerParam) && HasAnimatorTrigger(guardBlockTriggerParam))
                anim.SetTrigger(guardBlockTriggerParam);

            feedback?.PlayBlockFeedback(hitPoint, attacker);
            LockMove(lockMoveOnBlock);

            if (debugLog) Debug.Log($"[PDR] RESULT=BLOCK | chip={dmg}", this);
            return;
        }

        // 4) 일반 피격
        health.ApplyDamage(baseDamage);

        // Hit 트리거 억제 옵션
        if (suppressHitAnimWhenGuarding && guard != null && (guard.IsGuarding || guard.IsParryWindowOpen))
        {
            if (debugLog) Debug.Log($"[PDR] RESULT=HIT(suppressed anim) | dmg={baseDamage}", this);
            return;
        }

        if (!string.IsNullOrEmpty(hitTriggerParam) && HasAnimatorTrigger(hitTriggerParam))
            anim.SetTrigger(hitTriggerParam);

        if (debugLog) Debug.Log($"[PDR] RESULT=HIT | dmg={baseDamage}", this);
    }

    // --- 유틸 ---
    bool IsFront(Vector3 hitPoint, Transform attacker)
    {
        Vector3 src = transform.position;
        Vector3 toHit = (attacker ? (attacker.position - src) : (hitPoint - src));
        toHit.y = 0f;
        if (toHit.sqrMagnitude < 0.0001f) toHit = transform.forward;
        float angle = Vector3.Angle(transform.forward, toHit.normalized);
        return angle <= frontArcDegrees * 0.5f;
    }

    bool HasAnimatorTrigger(string name)
    {
        if (!anim) return false;
        foreach (var p in anim.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
                return true;
        return false;
    }

    void LockMove(float seconds)
    {
        if (seconds <= 0f) return;

        if (simpleInputBlocker)
        {
            var m = simpleInputBlocker.GetType().GetMethod("BlockForSeconds", new[] { typeof(float) });
            if (m != null) { m.Invoke(simpleInputBlocker, new object[] { seconds }); return; }
        }

        if (moveController) StartCoroutine(CoDisableBehaviour(moveController, seconds));
    }

    IEnumerator CoDisableBehaviour(Behaviour b, float seconds)
    {
        if (!b.enabled) yield break;
        b.enabled = false;
        yield return new WaitForSecondsRealtime(seconds);
        b.enabled = true;
    }

    void TryNotifyUltimateGain(float amount)
    {
        if (!ultimateTarget || amount <= 0f) return;
        foreach (string method in ultimateAddMethodNames)
            ultimateTarget.BroadcastMessage(method, amount, SendMessageOptions.DontRequireReceiver);
    }
}
