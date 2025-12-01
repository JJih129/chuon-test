// Assets/Scripts/Hit&health/data/PlayerDamageReceiver.cs
using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어 피격/가드/패링/퍼펙트 회피 판정을 담당.
/// AttackHitbox → IDamageReceiver.ReceiveHit(HitPayload)를 통해 호출된다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour, IDamageReceiver
{
    //======================================================================
    // ① 필수 참조
    //======================================================================
    [Header("① 필수 참조")]
    [Tooltip("실제 체력 관리 컴포넌트(PlayerHealth 등)")]
    [SerializeField] private PlayerHealth health;

    [Tooltip("피격/가드 애니메이션을 재생할 애니메이터")]
    [SerializeField] private Animator anim;

    [Tooltip("가드/패링 상태 조회용 컨트롤러")]
    [SerializeField] private PlayerGuardController guard;

    [Tooltip("퍼펙트 회피 판정 컨트롤러")]
    [SerializeField] private PerfectDodgeController perfectDodge;

    [Tooltip("패링/가드 성공 시 카메라/VFX/SFX 연출")]
    [SerializeField] private ParryFeedbackController feedback;

    //======================================================================
    // ② 애니메이터 파라미터
    //======================================================================
    [Header("② 애니메이터 파라미터")]
    [Tooltip("일반 피격 시 사용할 트리거 이름")]
    [SerializeField] private string hitTriggerParam = "Hit";

    [Tooltip("가드 중 피격(블록) 시 사용할 트리거 이름")]
    [SerializeField] private string guardBlockTriggerParam = "GuardBlock";

    //======================================================================
    // ③ 가드/칩 데미지 설정
    //======================================================================
    [Header("③ 가드/칩 데미지 설정")]
    [Tooltip("가드가 유효한 전방 각도 (120 = 좌우 60도)")]
    [Range(0f, 180f)]
    [SerializeField] private float frontArcDegrees = 120f;

    [Tooltip("가드 성공 시 들어오는 칩 데미지 비율(0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float chipDamageMul = 0.10f;

    [Tooltip("가드 중일 때 일반 Hit 애니메이션은 막을지 여부")]
    [SerializeField] private bool suppressHitAnimWhenGuarding = true;

    //======================================================================
    // ④ 이동 잠금(경직)
    //======================================================================
    [Header("④ 이동 잠금(경직)")]
    [Tooltip("패링 성공 시 이동 불가 시간")]
    [SerializeField] private float lockMoveOnParry = 0.35f;

    [Tooltip("가드 성공 시 이동 불가 시간")]
    [SerializeField] private float lockMoveOnBlock = 0.20f;

    [Tooltip("이동을 제어하는 Behaviour (PlayerMoveController 등)")]
    [SerializeField] private Behaviour moveController;

    //======================================================================
    // ⑤ 궁극기 게이지(선택)
    //======================================================================
    [Header("⑤ 궁극기 게이지(선택)")]
    [Tooltip("게이지를 가지고 있는 오브젝트 (없으면 자기 자신)")]
    [SerializeField] private GameObject ultimateTarget;

    [Tooltip("패링 성공 시 증가량")]
    [SerializeField] private float ultimateGainOnParry = 10f;

    [Tooltip("궁극기 스크립트의 메서드 이름 후보 (float 인자 1개)")]
    [SerializeField] private string[] ultimateAddMethodNames = { "AddGauge", "Gain" };

    void Awake()
    {
        if (!health)        health        = GetComponent<PlayerHealth>();
        if (!anim)          anim          = GetComponentInChildren<Animator>();
        if (!guard)         guard         = GetComponent<PlayerGuardController>();
        if (!perfectDodge)  perfectDodge  = GetComponent<PerfectDodgeController>();
        if (!feedback)      feedback      = GetComponent<ParryFeedbackController>();
        if (!ultimateTarget) ultimateTarget = gameObject;
    }

    //======================================================================
    //  ★ IDamageReceiver 구현 – AttackHitbox에서 호출되는 엔트리
    //======================================================================
    public void ReceiveHit(HitPayload payload)
    {
        // 현재 설계에선 unblockable, hitType 등은 HitPayload 쪽에 정의돼 있다면
        // 추후 확장 가능. 지금은 기본 데미지/위치/공격자만 사용.
        ReceiveHit(payload.damage, payload.attacker, payload.hitPoint, false);
    }

    /// <summary>
    /// 실제 피격 처리 핵심 로직.
    /// baseDamage: 히트박스에서 넘어온 순수 데미지
    /// attacker  : 공격자(카메라 연출 등에 사용 가능)
    /// hitPoint  : 피격 위치(이펙트 스폰용)
    /// unblockable: 가드할 수 없는 공격 여부
    /// </summary>
    public void ReceiveHit(float baseDamage, Transform attacker, Vector3 hitPoint, bool unblockable)
    {
        if (!health || health.IsDead) return;

        // 1) 퍼펙트 회피 최우선 체크
        if (perfectDodge != null && perfectDodge.ResolvePerfectDodge(hitPoint, attacker))
        {
            // 퍼펙트 회피 성공 시 데미지/가드 모두 무시
            return;
        }

        // 2) 가드 가능 상황인지 판정
        bool hasGuard   = guard != null;
        bool isFront    = IsFront(hitPoint, attacker);
        bool canDefense = hasGuard && isFront && !unblockable;

        bool isParryWindow = canDefense && guard.IsParryWindowOpen;              // 패링 타이밍
        bool isGuarding    = canDefense && guard.IsGuarding && !isParryWindow;   // 일반 가드

        //------------------------------------------------------------------
        // [A] 패링 성공
        //------------------------------------------------------------------
        if (isParryWindow)
        {
            feedback?.PlayParryFeedback(hitPoint, attacker);
            LockMove(lockMoveOnParry);
            TryNotifyUltimateGain(ultimateGainOnParry);

            if (TutorialManager.Instance != null)
                TutorialManager.Instance.OnPlayerParrySuccess();

            Debug.Log("[PlayerDamageReceiver] Parry Success");
            return;
        }

        //------------------------------------------------------------------
        // [B] 가드 성공 (칩 데미지 + 블록 연출)
        //------------------------------------------------------------------
        if (isGuarding)
        {
            float chip = baseDamage * chipDamageMul;
            if (chip > 0f)
                health.ApplyDamage(chip);

            feedback?.PlayGuardBlockFeedback(hitPoint, attacker);
            LockMove(lockMoveOnBlock);

            if (TutorialManager.Instance != null)
                TutorialManager.Instance.OnPlayerGuardSuccess();

            // 가드 히트 애니메이션
            if (!suppressHitAnimWhenGuarding && anim && !string.IsNullOrEmpty(guardBlockTriggerParam))
            {
                anim.ResetTrigger(guardBlockTriggerParam);
                anim.SetTrigger(guardBlockTriggerParam);
            }

            Debug.Log($"[PlayerDamageReceiver] Guard Block, chip={chip}");
            return;
        }

        //------------------------------------------------------------------
        // [C] 일반 피격
        //------------------------------------------------------------------
        health.ApplyDamage(baseDamage);

        if (anim && !string.IsNullOrEmpty(hitTriggerParam))
        {
            anim.ResetTrigger(hitTriggerParam);
            anim.SetTrigger(hitTriggerParam);
        }

        // 튜토리얼 – 맞았을 때 콜백 (필요 없으면 주석 유지)
        // if (TutorialManager.Instance != null)
        //     TutorialManager.Instance.OnPlayerHit();

        // Debug.Log($"[PlayerDamageReceiver] Hit, damage={baseDamage}");
    }

    //======================================================================
    //  보조 함수들
    //======================================================================

    /// <summary>
    /// 공격이 플레이어 전방 각도 안에서 들어왔는지 판정.
    /// (가드가 앞만 막도록 하기 위함)
    /// </summary>
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

        float dot   = Vector3.Dot(forward, toHit);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;

        return angle <= (frontArcDegrees * 0.5f);
    }

    void LockMove(float duration)
    {
        if (!moveController || duration <= 0f) return;
        StartCoroutine(Co_LockMove(duration));
    }

    IEnumerator Co_LockMove(float duration)
    {
        moveController.enabled = false;
        yield return new WaitForSeconds(duration);
        moveController.enabled = true;
    }

    /// <summary>
    /// 궁극기 게이지에 float 인자를 하나 받는 메서드(AddGauge/Gain 등)를 반사로 찾아 호출.
    /// </summary>
    void TryNotifyUltimateGain(float amount)
    {
        if (ultimateTarget == null || amount <= 0f) return;

        var comps = ultimateTarget.GetComponents<MonoBehaviour>();
        foreach (var c in comps)
        {
            if (c == null) continue;

            var type = c.GetType();
            foreach (var methodName in ultimateAddMethodNames)
            {
                var m = type.GetMethod(methodName, new[] { typeof(float) });
                if (m != null)
                {
                    m.Invoke(c, new object[] { amount });
                    return;
                }
            }
        }
    }
}
