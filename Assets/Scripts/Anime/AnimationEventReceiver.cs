using UnityEngine;

/// 애니메이션 이벤트 수신기(가드/패링/퍼펙트회피/공격 플래그)
/// - 클립 이벤트용 메서드:
///   ParryWindow_Pulse / ParryWindow_Open / ParryWindow_Close
///   PerfectDodgeWindow_Pulse([seconds]) / PerfectDodgeWindow_Open([seconds]) / PerfectDodgeWindow_Close
///   AE_AttackStart / AE_AttackEnd
/// - 퍼펙트회피 창은 무인자/float 인자 모두 허용(클립 이벤트 편의)
[DisallowMultipleComponent]
public sealed class AnimationEventReceiver : MonoBehaviour
{
    [Header("참조(같은 캐릭터 오브젝트 내)")]
    [Tooltip("가드/패링 판정 컨트롤러")]
    [SerializeField] private PlayerGuardController guard;
    [Tooltip("퍼펙트 회피 판정 컨트롤러")]
    [SerializeField] private PerfectDodgeController perfectDodge;
    [Tooltip("애니메이터(공격 플래그 동기화용)")]
    [SerializeField] private Animator anim;
    [SerializeField] private PlayerReferences playerReferences;

    [Header("애니메이터 파라미터명")]
    [Tooltip("공격 중 여부 Bool 파라미터명(예: IsAttacking)")]
    [SerializeField] private string attackingBoolParam = "IsAttacking";

    // 내부 상태
    private int _attackingHash;
    private bool _hasAttackingBool;

    // 퍼펙트 회피 창 기본 지속시간(클립 이벤트에 인자를 안 넘긴 경우)
    private const float DefaultPerfectDodgeWindow = 0.16f;

    private void Reset()      => TryAutoWire();
    private void Awake()
    {
        TryAutoWire();
        _attackingHash = Animator.StringToHash(attackingBoolParam);
        _hasAttackingBool = HasBool(anim, attackingBoolParam);
        if (!_hasAttackingBool)
            Debug.LogWarning($"[AnimEventReceiver] Animator Bool '{attackingBoolParam}' 를 찾지 못했습니다. AE_AttackStart/End는 무시됩니다.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 에디터에서 참조 빠지면 자동 연결 시도
        TryAutoWire();
        _attackingHash = Animator.StringToHash(attackingBoolParam);
    }
#endif

    private void TryAutoWire()
    {
        if (playerReferences == null) playerReferences = GetComponentInParent<PlayerReferences>(true);
        if (anim == null)        anim = playerReferences != null ? playerReferences.MainAnimator ?? GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>() : GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
        if (guard == null)       guard = GetComponentInParent<PlayerGuardController>(true);
        if (perfectDodge == null)perfectDodge = GetComponentInParent<PerfectDodgeController>(true);
    }

    private static bool HasBool(Animator a, string name)
    {
        if (a == null || string.IsNullOrEmpty(name)) return false;
        foreach (var p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == name)
                return true;
        return false;
    }

    // ===================== 패링 창 =====================
    // 클립 이벤트에서 직접 호출
    public void ParryWindow_Pulse()  => guard?.OpenParryWindow();
    public void ParryWindow_Open()   => guard?.OpenParryWindow();
    public void ParryWindow_Close()  => guard?.CloseParryWindow();

    // ================= 퍼펙트 회피 창 =================
    // 무인자(클립 이벤트 인자 미전달 시) → 기본값으로 열기/펄스
    public void PerfectDodgeWindow_Pulse()               => perfectDodge?.PerfectDodgeWindow_Pulse(DefaultPerfectDodgeWindow);
    public void PerfectDodgeWindow_Open()                => perfectDodge?.PerfectDodgeWindow_Open(DefaultPerfectDodgeWindow);
    public void PerfectDodgeWindow_Close()               => perfectDodge?.PerfectDodgeWindow_Close();

    // 인자 버전(클립 이벤트에서 초 단위 duration 전달)
    public void PerfectDodgeWindow_Pulse(float seconds)  => perfectDodge?.PerfectDodgeWindow_Pulse(seconds);
    public void PerfectDodgeWindow_Open(float seconds)   => perfectDodge?.PerfectDodgeWindow_Open(seconds);

    // ===================== 공격 플래그 =================
    // 공격 모션 시작/종료 시 애니메이터 Bool을 안전하게 토글
    public void AE_AttackStart()
    {
        if (anim != null && _hasAttackingBool) anim.SetBool(_attackingHash, true);
    }
    public void AE_AttackEnd()
    {
        if (anim != null && _hasAttackingBool) anim.SetBool(_attackingHash, false);
    }
}
