using UnityEngine;

/// 애니메이션 이벤트 수신기
/// - 패링 창 열기/닫기(네이밍 호환: ParryWindow_Pulse / _Open / _Close)
/// - 공격 시작/종료 시 Animator Bool("IsAttacking") 토글
[DisallowMultipleComponent]
public class AnimationEventReceiver : MonoBehaviour
{
    [Header("참조: 같은 캐릭터의 컴포넌트 연결")]
    [Tooltip("가드/패링 로직 컨트롤러")]
    public PlayerGuardController guard;
    [Tooltip("공격 중 여부 Bool을 세팅할 Animator")]
    public Animator anim;

    [Header("애니메이터 파라미터명")]
    [Tooltip("공격 중 여부 Bool 파라미터명")]
    public string attackingBoolParam = "IsAttacking";

    int _attackingHash;
    bool _paramChecked, _hasAttackingBool;

    void Reset()
    {
        if (!guard) guard = GetComponentInParent<PlayerGuardController>();
        if (!anim)  anim  = GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
        _attackingHash = Animator.StringToHash(attackingBoolParam);
    }

    void Awake()
    {
        if (!guard) guard = GetComponentInParent<PlayerGuardController>();
        if (!anim)  anim  = GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
        _attackingHash = Animator.StringToHash(attackingBoolParam);
    }

    // ===== 패링 창 이벤트 =====
    // 기존 사용명 그대로: ParryWindow_Pulse → guard.perfectGuardWindow 길이만큼 ON
    public void ParryWindow_Pulse() { guard?.OpenParryWindow(); }

    // 선택: 편의용(초 단위). 현재 GuardController가 초 인자를 받지 않으면 무시됨 없이 기본 Pulse를 호출.
    public void ParryWindow_Open()  { guard?.OpenParryWindow(); }   // 인자 없는 오픈
    public void ParryWindow_Close() { guard?.CloseParryWindow(); }  // 강제 종료

    // ===== 공격 상태 이벤트 =====
    public void AE_AttackStart()
    {
        if (!anim) return;
        EnsureParam();
        if (_hasAttackingBool) anim.SetBool(_attackingHash, true);
    }
    public void AE_AttackEnd()
    {
        if (!anim) return;
        EnsureParam();
        if (_hasAttackingBool) anim.SetBool(_attackingHash, false);
    }

    void EnsureParam()
    {
        if (_paramChecked || anim == null) return;
        _paramChecked = true;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == attackingBoolParam)
            {
                _hasAttackingBool = true;
                return;
            }
        }
        Debug.LogWarning($"[AnimEventReceiver] Animator Bool '{attackingBoolParam}' 없음. AE_AttackStart/End 무시.", this);
    }
}
