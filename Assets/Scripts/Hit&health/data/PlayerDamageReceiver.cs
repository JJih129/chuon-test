using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어가 맞았을 때 실제 판정을 담당하는 컴포넌트.
/// - 퍼펙트 회피(PerfectDodgeController)
/// - 패링/가드(PlayerGuardController)
/// - 칩 데미지
/// - 피격 애니메이션, 이동 잠금, 궁극기 게이지 수급
/// </summary>
[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour
{
    //======================================================================
    //  필수 참조
    //======================================================================
    [Header("① 필수 참조")]
    [Tooltip("실제 체력/사망 판정을 담당하는 컴포넌트")]
    [SerializeField] private PlayerHealth health;

    [Tooltip("피격/가드 히트 애니메이션을 재생할 Animator (주로 캐릭터 본체)")]
    [SerializeField] private Animator anim;

    [Tooltip("가드/패링 상태 정보를 제공하는 가드 컨트롤러")]
    [SerializeField] private PlayerGuardController guard;

    [Tooltip("퍼펙트 회피 판정 및 슬로우 모션 담당")]
    [SerializeField] private PerfectDodgeController perfectDodge;

    [Tooltip("카메라 쉐이크, 사운드, VFX 등 패링/가드 피드백 담당")]
    [SerializeField] private ParryFeedbackController feedback;

    //======================================================================
    //  애니메이터 파라미터
    //======================================================================
    [Header("② 애니메이터 파라미터명")]
    [Tooltip("일반 피격시 SetTrigger 할 파라미터명")]
    [SerializeField] private string hitTriggerParam = "Hit";

    [Tooltip("가드 성공(블록)시 SetTrigger 할 파라미터명")]
    [SerializeField] private string guardBlockTriggerParam = "GuardBlock";

    //======================================================================
    //  가드/방향/칩 데미지 설정
    //======================================================================
    [Header("③ 가드/방향/칩 데미지")]
    [Tooltip("가드/패링이 인정되는 전방 시야각(도 단위). 100이면 앞 50도 좌우.")]
    [Range(0f, 180f)]
    [SerializeField] private float frontArcDegrees = 100f;

    [Tooltip("가드 성공 시 실제로 받는 칩 데미지 비율 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float chipDamageMul = 0.10f;

    [Tooltip("가드/패링 상태일 때 일반 피격 애니메이션 트리거를 생략할지 여부")]
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;

    //======================================================================
    //  이동 잠금
    //======================================================================
    [Header("④ 이동 잠금 옵션")]
    [Tooltip("패링 성공 시 이동을 잠그는 시간(초, 실시간 기준)")]
    [SerializeField] private float lockMoveOnParry = 0.35f;

    [Tooltip("가드 블록 시 이동을 잠그는 시간(초, 실시간 기준)")]
    [SerializeField] private float lockMoveOnBlock = 0.20f;

    [Tooltip("선택사항 : BlockForSeconds(float)를 가진 인풋 블로커 컴포넌트(있으면 우선 사용)")]
    [SerializeField] private MonoBehaviour simpleInputBlocker; // BlockForSeconds(float)를 가진 컴포넌트

    [Tooltip("없으면 기본 이동 컨트롤러(Behaviour)를 자동으로 찾아 사용")]
    [SerializeField] private Behaviour moveController;

    //======================================================================
    //  패링 → 궁극기 수급
    //======================================================================
    [Header("⑤ 패링 → 궁극기 수급(옵션)")]
    [Tooltip("궁극기 게이지를 가지고 있는 오브젝트 (없으면 자기 자신)")]
    [SerializeField] private GameObject ultimateTarget;

    [Tooltip("패링 성공 시 추가할 궁극기 게이지 양")]
    [SerializeField] private float ultimateGainOnParry = 10f;

    [Tooltip("궁극기 수급 시 BroadcastMessage로 호출을 시도할 메서드 이름 목록")]
    [SerializeField] private string[] ultimateAddMethodNames =
        { "AddGauge", "AddUltimateGauge", "AddUltimate", "Gain", "OnParryUltimateGain" };

    //======================================================================
    //  디버그
    //======================================================================
    [Header("⑥ 디버그")]
    [SerializeField] private bool debugLog = true;

    //======================================================================
    //  Unity 콜백
    //======================================================================

    // 에디터에서 Add Component 했을 때 기본 참조 자동 세팅
    void Reset()
    {
        if (!health)       health       = GetComponent<PlayerHealth>();
        if (!anim)         anim         = GetComponentInChildren<Animator>();
        if (!guard)        guard        = GetComponent<PlayerGuardController>();
        if (!perfectDodge) perfectDodge = GetComponent<PerfectDodgeController>();
        if (!feedback)     feedback     = GetComponent<ParryFeedbackController>();

        if (!ultimateTarget) ultimateTarget = gameObject;

        // moveController는 프로젝트마다 다양하므로 자동 추론만 하고,
        // 실제로는 인스펙터에서 직접 지정해 주는 걸 추천.
        if (!moveController)
        {
            // 가장 가까운 Behaviour 하나를 자동 할당 (필요 시 교체)
            moveController = GetComponent<Behaviour>();
        }
    }

    void Awake()
    {
        if (!health)       health       = GetComponent<PlayerHealth>();
        if (!anim)         anim         = GetComponentInChildren<Animator>();
        if (!guard)        guard        = GetComponent<PlayerGuardController>();
        if (!perfectDodge) perfectDodge = GetComponent<PerfectDodgeController>();
        if (!feedback)     feedback     = GetComponent<ParryFeedbackController>();
        if (!ultimateTarget) ultimateTarget = gameObject;

        if (debugLog && health == null)
        {
            Debug.LogError("[PlayerDamageReceiver] PlayerHealth 참조가 없습니다.", this);
        }
    }

    //======================================================================
    //  외부에서 호출하는 핵심 메서드
    //  Bullet/보스 공격 등이 이 메서드를 호출해서 실제 판정을 진행.
    //======================================================================
    /// <summary>
    /// 외부(총알, 보스 공격 등)에서 호출하는 단일 진입점.
    /// </summary>
    /// <param name="baseDamage">기본 데미지 값</param>
    /// <param name="attacker">공격자 Transform (없으면 hitPoint 기준)</param>
    /// <param name="hitPoint">타격 지점(월드 좌표)</param>
    /// <param name="unblockable">가드/패링 불가 공격인지 여부</param>
    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable)
    {
        if (!health || health.IsDead)
            return;

        // ------------------------------------------------------------------
        // 0) 퍼펙트 회피: 가장 먼저 체크 (성공 시 데미지, 가드 판정 전부 무효)
        // ------------------------------------------------------------------
        if (perfectDodge != null && perfectDodge.ResolvePerfectDodge(hitPoint, attacker))
        {
            if (debugLog)
                Debug.Log("[PDR] RESULT = PERFECT_DODGE | dmg = 0", this);
            return;
        }

        // ------------------------------------------------------------------
        // 1) 전방 여부 및 방어 가능 여부 계산
        // ------------------------------------------------------------------
        bool isFront     = IsFront(hitPoint, attacker);
        bool hasGuard    = guard != null;
        bool canDefense  = hasGuard && isFront && !unblockable;

        bool isParryWindow = canDefense && guard.IsParryWindowOpen;
        bool isGuarding    = canDefense && guard.IsGuarding && !isParryWindow;

        if (debugLog)
        {
            Debug.Log(
                $"[PDR] FRONT={isFront} | parry={isParryWindow} | block={isGuarding} | base={baseDamage}",
                this);
        }

        // ------------------------------------------------------------------
        // 2) 패링 성공
        // ------------------------------------------------------------------
        if (isParryWindow)
        {
            // 시각/청각 피드백
            feedback?.PlayParryFeedback(hitPoint, attacker);

            // 이동 잠금
            LockMove(lockMoveOnParry);

            // 궁극기 게이지 수급
            TryNotifyUltimateGain(ultimateGainOnParry);

            if (debugLog)
                Debug.Log("[PDR] RESULT = PARRY | dmg = 0", this);

            return;
        }

        // ------------------------------------------------------------------
        // 3) 가드 블록(칩 데미지 적용)
        // ------------------------------------------------------------------
        if (isGuarding)
        {
            int chipDamage = Mathf.CeilToInt(baseDamage * chipDamageMul);
            health.ApplyDamage(chipDamage);

            // 가드 블록 히트 애니메이션
            if (!string.IsNullOrEmpty(guardBlockTriggerParam) &&
                HasAnimatorTrigger(guardBlockTriggerParam))
            {
                anim.SetTrigger(guardBlockTriggerParam);
            }

            // 피드백 (카메라 쉐이크, 이펙트 등)
            feedback?.PlayBlockFeedback(hitPoint, attacker);

            // 이동 잠금
            LockMove(lockMoveOnBlock);

            if (debugLog)
                Debug.Log($"[PDR] RESULT = BLOCK | chip = {chipDamage}", this);

            return;
        }

        // ------------------------------------------------------------------
        // 4) 일반 피격
        // ------------------------------------------------------------------
        health.ApplyDamage(baseDamage);

        // 옵션: 가드/패링 상태에서는 피격 모션을 생략해서 어색함 방지
        if (suppressHitAnimWhenGuarding &&
            guard != null &&
            (guard.IsGuarding || guard.IsParryWindowOpen))
        {
            if (debugLog)
                Debug.Log($"[PDR] RESULT = HIT (anim suppressed) | dmg = {baseDamage}", this);
            return;
        }

        // 피격 애니메이션 트리거
        if (!string.IsNullOrEmpty(hitTriggerParam) &&
            HasAnimatorTrigger(hitTriggerParam))
        {
            anim.SetTrigger(hitTriggerParam);
        }

        if (debugLog)
            Debug.Log($"[PDR] RESULT = HIT | dmg = {baseDamage}", this);
    }

    //======================================================================
    //  유틸리티 메서드
    //======================================================================

    /// <summary>
    /// 공격이 플레이어의 전방(가드 허용 각도)에서 들어왔는지 판정.
    /// </summary>
    bool IsFront(Vector3 hitPoint, Transform attacker)
    {
        Vector3 src   = transform.position;
        Vector3 toHit = attacker ? (attacker.position - src) : (hitPoint - src);

        toHit.y = 0f;
        if (toHit.sqrMagnitude < 0.0001f)
        {
            // 거의 같은 위치라면 전방으로 간주
            toHit = transform.forward;
        }

        float angle = Vector3.Angle(transform.forward, toHit.normalized);
        return angle <= frontArcDegrees * 0.5f;
    }

    /// <summary>
    /// Animator에 특정 Trigger 파라미터가 존재하는지 검사.
    /// 잘못된 이름으로 인한 경고/에러 방지용.
    /// </summary>
    bool HasAnimatorTrigger(string name)
    {
        if (!anim) return false;

        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 일정 시간 동안 이동 컨트롤러/인풋을 잠그는 처리.
    /// simpleInputBlocker가 있으면 그쪽(BlockForSeconds)을 우선 사용.
    /// </summary>
    void LockMove(float seconds)
    {
        if (seconds <= 0f) return;

        // 1) SimpleInputBlocker 스타일 컴포넌트를 우선 사용
        if (simpleInputBlocker != null)
        {
            var method = simpleInputBlocker.GetType()
                                           .GetMethod("BlockForSeconds", new[] { typeof(float) });

            if (method != null)
            {
                method.Invoke(simpleInputBlocker, new object[] { seconds });
                return;
            }
        }

        // 2) 기본 이동 컨트롤러 비활성화 방식
        if (moveController != null)
        {
            StartCoroutine(CoDisableBehaviour(moveController, seconds));
        }
    }

    IEnumerator CoDisableBehaviour(Behaviour behaviour, float seconds)
    {
        if (!behaviour.enabled)
            yield break;

        behaviour.enabled = false;
        yield return new WaitForSecondsRealtime(seconds);
        behaviour.enabled = true;
    }

    /// <summary>
    /// 궁극기 게이지 수급용 BroadcastMessage 헬퍼.
    /// </summary>
    void TryNotifyUltimateGain(float amount)
    {
        if (!ultimateTarget || amount <= 0f) return;

        foreach (string method in ultimateAddMethodNames)
        {
            ultimateTarget.BroadcastMessage(
                method,
                amount,
                SendMessageOptions.DontRequireReceiver
            );
        }
    }
}
