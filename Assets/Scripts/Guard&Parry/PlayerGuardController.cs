using UnityEngine;
using System.Collections;

/// 가드/패링 단일 판단자
[DisallowMultipleComponent]
public class PlayerGuardController : MonoBehaviour
{
    // ───────── 입력/애니 파라미터 ─────────
    [Header("입력 설정(신규 Input System)")]
    [Tooltip("가드키(InputActionReference). 비워도 됨")]
    public UnityEngine.InputSystem.InputActionReference guardAction;

    [Header("애니메이터 파라미터 이름")]
    [Tooltip("가드 여부 Bool")] public string guardBoolParam = "IsGuarding";
    [Tooltip("공격 중 Bool")]  public string isAttackingBoolParam = "IsAttacking";
    [Tooltip("블록 리액션 Trigger")] public string guardBlockTrigger = "GuardBlock";
    [Tooltip("패링 성공 Trigger")]   public string parrySuccessTrigger = "ParrySuccess";

    // ───────── 전방 기준 ─────────
    public enum ForwardMode { ModelForward, CameraForward }

    [Header("가드 전방 기준/판정")]
    [Tooltip("전방 벡터 기준")]
    public ForwardMode forwardMode = ForwardMode.ModelForward;
    [Tooltip("모델 Transform(선택)")] public Transform modelForwardSource;
    [Tooltip("카메라 Transform(선택)")] public Transform cameraTransform;
    [Tooltip("전방 반전")] public bool invertForward = false;
    [Range(10,360)] [Tooltip("정면 허용각(전체각)")] public float frontArcDegrees = 100f;
    [Tooltip("지면 기준 각도 판정")] public bool flattenToGround = true;

    // ───────── 정책 ─────────
    [Header("정책")]
    [Tooltip("공격 중 가드 불가")] public bool denyGuardWhileAttacking = true;
    [Tooltip("가드키 유지 중 공격 종료시 자동 재가드")] public bool autoResumeGuardIfHolding = true;

    // ───────── 대미지/패링 ─────────
    [Header("대미지/패링")]
    [Tooltip("블록 시 칩 대미지 비율")] [Range(0f,1f)] public float chipDamageOnBlock = 0.1f;
    [Tooltip("퍼펙트 가드 사용")] public bool enablePerfectGuard = true;
    [Tooltip("패링 기본 창(초)")] public float perfectGuardWindow = 0.12f;

    [Header("디버그")]
    public Animator animator;
    public bool debugDraw = false;

    // 내부 상태
    public bool IsGuarding { get; private set; }
    public bool IsAttacking { get; private set; }
    public bool IsParryWindowOpen => _parryOpen;

    int _hashGuardBool, _hashAttackBool, _hashBlockTrig, _hashParryTrig;
    bool _parryOpen;
    Coroutine _parryCo;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
        _hashGuardBool  = Animator.StringToHash(guardBoolParam);
        _hashAttackBool = Animator.StringToHash(isAttackingBoolParam);
        _hashBlockTrig  = Animator.StringToHash(guardBlockTrigger);
        _hashParryTrig  = Animator.StringToHash(parrySuccessTrigger);

        if (guardAction) guardAction.action.Enable();
    }

    void Update()
    {
        // 입력 유지 처리
        if (guardAction && guardAction.action != null)
        {
            bool pressed = guardAction.action.IsPressed();
            if (denyGuardWhileAttacking && IsAttacking) pressed = false;
            SetGuarding(pressed || (autoResumeGuardIfHolding && pressed));
        }

        // 애니 파라미터 동기화(외부 스크립트가 SetBool 해도 읽음)
        if (animator)
        {
            if (_hashAttackBool != 0) IsAttacking = animator.GetBool(_hashAttackBool);
            if (_hashGuardBool  != 0) IsGuarding  = animator.GetBool(_hashGuardBool);
        }
    }

    // ───────── 외부 제어 API(하위호환 포함) ─────────
    public void StartGuard() => SetGuarding(true);   // UltimateInputRouter 호환
    public void EndGuard()   => SetGuarding(false);  // UltimateInputRouter 호환

    public void SetGuarding(bool on)
    {
        if (animator && _hashGuardBool != 0) animator.SetBool(_hashGuardBool, on);
        IsGuarding = on;
    }

    // 애니메이션 이벤트용(오버로드 제공)
    public void OpenParryWindow()
    {
        if (!enablePerfectGuard) return;
        StartParryWindow(perfectGuardWindow);
    }
    public void OpenParryWindow(float seconds)
    {
        if (!enablePerfectGuard) return;
        StartParryWindow(Mathf.Max(0f, seconds));
    }
    public void OpenParryWindow(int frames)          // 일부 프로젝트가 프레임 단위로 호출
    {
        if (!enablePerfectGuard) return;
        float sec = Mathf.Max(0, frames) / 60f;      // 60fps 가정
        StartParryWindow(sec > 0f ? sec : perfectGuardWindow);
    }
    public void SetParryWindow(bool open)            // bool 인자 호출 호환
    {
        if (!enablePerfectGuard) return;
        if (_parryCo != null) { StopCoroutine(_parryCo); _parryCo = null; }
        _parryOpen = open;
    }
    public void CloseParryWindow()
    {
        if (_parryCo != null) { StopCoroutine(_parryCo); _parryCo = null; }
        _parryOpen = false;
    }

    void StartParryWindow(float seconds)
    {
        if (_parryCo != null) StopCoroutine(_parryCo);
        _parryCo = StartCoroutine(CoParry(seconds));
    }
    IEnumerator CoParry(float seconds)
    {
        _parryOpen = true;
        yield return new WaitForSeconds(seconds);
        _parryOpen = false;
        _parryCo = null;
    }

    public void PlayBlockReaction()
    {
        if (animator && _hashBlockTrig != 0) animator.SetTrigger(_hashBlockTrig);
    }
    public void PlayParrySuccess()
    {
        if (animator && _hashParryTrig != 0) animator.SetTrigger(_hashParryTrig);
    }

    // ───────── 가드 판정 ─────────
    public bool EvaluateDefense(Vector3 hitPoint, out bool isParry, out bool isBlock, out float chipMul)
    {
        isParry = false; isBlock = false; chipMul = chipDamageOnBlock;

        if (!IsGuarding) return false;
        if (denyGuardWhileAttacking && IsAttacking) return false;

        Vector3 fwd = GetGuardForward().normalized;
        Vector3 dir = (hitPoint - GetGuardOrigin()).normalized;

        if (flattenToGround) { fwd.y = 0f; dir.y = 0f; fwd.Normalize(); dir.Normalize(); }
        if (invertForward) fwd = -fwd;

        float angle = Vector3.Angle(fwd, dir);
        bool inFront = angle <= (frontArcDegrees * 0.5f);
        if (!inFront) return false;

        if (enablePerfectGuard && _parryOpen) { isParry = true; return true; }
        isBlock = true; return true;
    }

    Vector3 GetGuardForward()
    {
        if (forwardMode == ForwardMode.CameraForward && cameraTransform) return cameraTransform.forward;
        if (modelForwardSource) return modelForwardSource.forward;
        return transform.forward;
    }
    Vector3 GetGuardOrigin()
    {
        if (modelForwardSource) return modelForwardSource.position;
        return transform.position;
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDraw) return;
        Vector3 pos = Application.isPlaying ? GetGuardOrigin() : (modelForwardSource ? modelForwardSource.position : transform.position);
        Vector3 fwd = Application.isPlaying ? GetGuardForward() : (modelForwardSource ? modelForwardSource.forward : transform.forward);
        if (invertForward) fwd = -fwd;
        if (flattenToGround) fwd.y = 0f;
        fwd.Normalize();
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(pos, pos + fwd * 1.5f);
    }
}
