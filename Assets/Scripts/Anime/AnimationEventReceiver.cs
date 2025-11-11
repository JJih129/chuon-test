using UnityEngine;

/// <summary>
/// 애니메이션 이벤트 수신기
/// - 패링 윈도우 열기/닫기
/// - 공격 시작/종료 시 Animator Bool("IsAttacking") 토글
/// </summary>
[DisallowMultipleComponent]
public class AnimationEventReceiver : MonoBehaviour
{
    [Header("참조")] // 유니티 툴에서 조절: 연결 참조
    [Tooltip("같은 캐릭터의 PlayerGuardController. 비우면 부모에서 자동 검색")]
    public PlayerGuardController guard;
    [Tooltip("공격 중 여부 Bool을 세팅할 Animator. 비우면 자신→부모→guard.animator 순으로 검색")]
    public Animator anim;

    [Header("파라미터 이름")] // 유니티 툴에서 조절: 파라미터명
    [Tooltip("Animator Bool: 공격 중 여부 파라미터명")]
    public string attackingBoolParam = "IsAttacking";

    int _attackingHash;
    bool _hasAttackingParamChecked;
    bool _hasAttackingParam;

    void Awake()
    {
        if (!guard) guard = GetComponentInParent<PlayerGuardController>();
        if (!anim)  anim  = GetComponent<Animator>();
        if (!anim && guard) anim = guard.animator;

        _attackingHash = Animator.StringToHash(attackingBoolParam);
    }

    // ===== 패링 관련 AnimationEvent에서 호출 =====
    public void ParryWindow_Pulse()
    {
        if (guard) guard.OpenParryWindow();
    }

    public void ParryWindow_Open(float seconds)
    {
        if (guard) guard.OpenParryWindow(seconds);
    }

    public void ParryWindow_Close()
    {
        if (guard) guard.CloseParryWindow();
    }

    // ===== 공격 상태 AnimationEvent에서 호출 =====
    public void AE_AttackStart()
    {
        if (!anim) return;
        EnsureHasAttackingParam();
        if (_hasAttackingParam) anim.SetBool(_attackingHash, true);
    }

    public void AE_AttackEnd()
    {
        if (!anim) return;
        EnsureHasAttackingParam();
        if (_hasAttackingParam) anim.SetBool(_attackingHash, false);
    }

    void EnsureHasAttackingParam()
    {
        if (_hasAttackingParamChecked || anim == null) return;
        _hasAttackingParamChecked = true;

        // 파라미터 존재 확인
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == attackingBoolParam)
            {
                _hasAttackingParam = true;
                return;
            }
        }
        Debug.LogWarning($"[AnimEventReceiver] Animator Bool '{attackingBoolParam}' 없음. AE_AttackStart/End 무시.", this);
    }
}
