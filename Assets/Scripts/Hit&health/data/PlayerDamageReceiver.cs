using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 피격 처리 일원화:
///  - 패링/블록/일반피격 판정
///  - HP/칩데미지 적용
///  - 이동 잠금(선택)
///  - 애니메이션 트리거 호출/차단
///  - 패링/블록 피드백(히트스톱·카메라 쉐이크·VFX·SFX)
///  - 패링 시 궁극기 게이지 알림(메서드명 유연 호출)
/// </summary>
[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour
{
    [Header("필수 참조")]
    [SerializeField] private PlayerHealth health;                 // HP 모듈
    [SerializeField] private Animator anim;                       // 애니메이터(파라미터 트리거)
    [SerializeField] private PlayerGuardController guard;         // 가드 상태/패링 윈도우 조회
    [SerializeField] private ParryFeedbackController feedback;    // 히트스톱/임펄스/VFX/SFX

    [Header("애니메이터 파라미터명")]
    [SerializeField] private string hitTriggerParam = "Hit";          // 일반 피격
    [SerializeField] private string guardBlockTriggerParam = "GuardBlock"; // 가드 성공 리액션

    [Header("블록/패링 설정")]
    [Tooltip("가드 전방 허용 각도(정면 기준 반각). 예: 100이면 전방 약 ±50도")]
    [Range(0f,180f)] [SerializeField] private float frontArcDegrees = 100f;
    [Tooltip("가드 시 칩데미지 배율(0.1 = 10%)")]
    [Range(0f,1f)] [SerializeField] private float chipDamageMul = 0.10f;
    [Tooltip("블록/패링 중에는 Hit 트리거를 막는다")]
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;

    [Header("이동 잠금")]
    [Tooltip("패링 성공 후 이동 잠금 시간")]
    [SerializeField] private float lockMoveOnParry = 0.35f;
    [Tooltip("블록 리액션 동안 이동 잠금 시간")]
    [SerializeField] private float lockMoveOnBlock = 0.20f;
    [Tooltip("없으면 자동으로 PlayerMoveController를 잠시 Disable 처리")]
    [SerializeField] private MonoBehaviour simpleInputBlocker; // (옵션) SimpleInputBlocker 등
    [SerializeField] private Behaviour moveController;         // (옵션) PlayerMoveController 컴포넌트

    [Header("궁극기 게이지(패링 보상)")]
    [SerializeField] private GameObject ultimateTarget;            // 메서드 수신자(보통 Player)
    [SerializeField] private float ultimateGainOnParry = 10f;      // 패링 시 고정 수급량
    [Tooltip("해당 오브젝트에 아래 이름 중 하나의 메서드(float)를 두면 자동 호출됨")]
    [SerializeField] private string[] ultimateAddMethodNames = 
        { "AddGauge", "AddUltimateGauge", "AddUltimate", "Gain", "OnParryUltimateGain" };

    [Header("디버그")]
    [SerializeField] private bool debugLog;

    void Reset()
    {
        health = GetComponent<PlayerHealth>();
        anim   = GetComponentInChildren<Animator>();
        guard  = GetComponent<PlayerGuardController>();
        feedback = GetComponent<ParryFeedbackController>();
        if (!ultimateTarget) ultimateTarget = gameObject;
        if (!moveController) moveController = GetComponent<Behaviour>(); // 임의 자동 채움 시도
    }

    void Awake()
    {
        // 누락 방지
        if (!health)  health  = GetComponent<PlayerHealth>();
        if (!anim)    anim    = GetComponentInChildren<Animator>();
        if (!guard)   guard   = GetComponent<PlayerGuardController>();
        if (!feedback)feedback= GetComponent<ParryFeedbackController>();
        if (!ultimateTarget) ultimateTarget = gameObject;
    }

    /// <summary>
    /// 외부(투사체/무기 히트박스)에서 호출.
    /// </summary>
    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable)
    {
        if (!health || health.IsDead) return;

        // 전방 판정
        bool isFront = IsFront(hitPoint, attacker);

        // 패링/블록 판정
        bool parry = !unblockable && guard && guard.IsParryWindowOpen && isFront;
        bool block = !unblockable && guard && guard.IsGuarding && isFront && !parry;

        // 디버그
        if (debugLog)
            Debug.Log($"[PDR] base={baseDamage} | block={block} parry={parry} | front={isFront} | action={(parry?"PARRY":block?"BLOCK":"HIT")}", this);

        if (parry)
        {
            // 데미지 없음. 피드백 + 이동잠금 + 궁극기 수급
            feedback?.PlayParryFeedback(hitPoint, attacker);
            LockMove(lockMoveOnParry);
            TryNotifyUltimateGain(ultimateGainOnParry);
            return;
        }

        if (block)
        {
            // 칩데미지 + 블록 리액션, Hit 트리거는 억제
            int dmg = Mathf.CeilToInt(baseDamage * chipDamageMul);
            health.ApplyDamage(dmg);
            if (!string.IsNullOrEmpty(guardBlockTriggerParam) && HasAnimatorTrigger(guardBlockTriggerParam))
                anim.SetTrigger(guardBlockTriggerParam);

            feedback?.PlayBlockFeedback(hitPoint, attacker);
            LockMove(lockMoveOnBlock);
            return;
        }

        // 일반 피격 처리
        health.ApplyDamage(baseDamage);
        if (!string.IsNullOrEmpty(hitTriggerParam) && HasAnimatorTrigger(hitTriggerParam))
            anim.SetTrigger(hitTriggerParam);
    }

    // ───────────────────────── 내부 유틸 ─────────────────────────

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

        // 1) SimpleInputBlocker에 BlockForSeconds(float) 있으면 호출
        if (simpleInputBlocker)
        {
            var m = simpleInputBlocker.GetType().GetMethod("BlockForSeconds", new[] { typeof(float) });
            if (m != null) { m.Invoke(simpleInputBlocker, new object[] { seconds }); return; }
        }

        // 2) 이동 컴포넌트를 임시 비활성화
        if (moveController)
            StartCoroutine(CoDisableBehaviour(moveController, seconds));
    }

    IEnumerator CoDisableBehaviour(Behaviour b, float seconds)
    {
        if (!b.enabled) yield break;
        b.enabled = false;
        yield return new WaitForSeconds(seconds);
        b.enabled = true;
    }

    void TryNotifyUltimateGain(float amount)
    {
        if (!ultimateTarget || amount <= 0f) return;

        // 메서드(float)를 가진 컴포넌트에 순차 브로드캐스트
        foreach (string method in ultimateAddMethodNames)
            ultimateTarget.BroadcastMessage(method, amount, SendMessageOptions.DontRequireReceiver);
    }
}
