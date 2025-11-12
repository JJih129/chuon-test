using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// 가드/패링 컨트롤러(소울라이크 표준)
/// - 애니 파라미터 존재 여부를 검사해 안전하게 동작
/// - 패링/블록 리액션 동안 이동 잠금
/// - 패링 성공 시 궁극기 게이지 수급
/// - 애니메이션 이벤트로 패링 윈도우 열기 지원
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("ChuOn/Combat/Player Guard Controller")]
public class PlayerGuardController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    // ① 입력 설정
    // ─────────────────────────────────────────────────────────────────────
    [Header("① 입력 설정(신규 Input System)")]
    [Tooltip("가드 키(InputActionReference). 비워두면 코드/애니에서만 제어")]
    public InputActionReference guardAction;

    // ─────────────────────────────────────────────────────────────────────
    // ② 애니메이터 파라미터 이름
    // ─────────────────────────────────────────────────────────────────────
    [Header("② 애니메이터 파라미터명")]
    [Tooltip("가드 여부 Bool")]
    public string guardBoolParam = "IsGuarding";
    [Tooltip("공격 중 Bool")]
    public string isAttackingBoolParam = "IsAttacking";
    [Tooltip("블록 리액션 Trigger")]
    public string guardBlockTrigger = "GuardBlock";
    [Tooltip("패링 성공 Trigger")]
    public string parrySuccessTrigger = "ParrySuccess";

    // ─────────────────────────────────────────────────────────────────────
    // ③ 가드 전방 기준/판정
    // ─────────────────────────────────────────────────────────────────────
    public enum ForwardMode { ModelForward, CameraForward }

    [Header("③ 가드 전방 기준/판정")]
    [Tooltip("전방 벡터 기준 선택")]
    public ForwardMode forwardMode = ForwardMode.ModelForward;
    [Tooltip("모델 Transform(미설정 시 this.transform 사용)")]
    public Transform modelForwardSource;
    [Tooltip("카메라 Transform(ForwardMode가 CameraForward일 때 사용)")]
    public Transform cameraTransform;
    [Tooltip("전방 반전")]
    public bool invertForward = false;
    [Range(10f, 360f)]
    [Tooltip("정면 허용각(전체각). 예: 100이면 ±50°")]
    public float frontArcDegrees = 100f;
    [Tooltip("수평면에서 각도 판정(높낮이 무시)")]
    public bool flattenToGround = true;

    // ─────────────────────────────────────────────────────────────────────
    // ④ 정책(공격 중 가드 차단 등)
    // ─────────────────────────────────────────────────────────────────────
    [Header("④ 정책(공격 중 가드 차단 등)")]
    [Tooltip("공격 중엔 가드 입력 무시")]
    public bool denyGuardWhileAttacking = true;
    [Tooltip("가드키 유지 중 공격 종료 시 자동 재가드")]
    public bool autoResumeGuardIfHolding = true;

    // ─────────────────────────────────────────────────────────────────────
    // ⑤ 대미지/패링
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑤ 대미지/패링")]
    [Range(0f, 1f)]
    [Tooltip("블록 시 칩 대미지 비율(0=무시, 0.1=10%)")]
    public float chipDamageOnBlock = 0.1f;
    [Tooltip("퍼펙트 가드 사용")]
    public bool enablePerfectGuard = true;
    [Tooltip("패링 기본 창(초). 이벤트에서 길이 미지정 시 사용")]
    public float perfectGuardWindow = 0.12f;

    // ─────────────────────────────────────────────────────────────────────
    // ⑥ 리액션 중 이동 잠금
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑥ 이동 잠금(리액션 중 이동 금지)")]
    [Tooltip("블록 리액션 동안 이동 잠금")]
    public bool lockMoveOnBlock = true;
    [Tooltip("블록 리액션 이동 잠금 시간(초)")]
    public float blockMoveLock = 0.35f;
    [Tooltip("패링 리액션 동안 이동 잠금")]
    public bool lockMoveOnParry = true;
    [Tooltip("패링 리액션 이동 잠금 시간(초)")]
    public float parryMoveLock = 0.45f;
    [Tooltip("잠금 시 즉시 속도 0으로 정지")]
    public bool stopVelocityOnLock = true;
    [Tooltip("잠금 동안 Animator RootMotion 비활성화")]
    public bool disableRootMotionDuringLock = false;

    [Tooltip("같은 오브젝트의 이동 컨트롤러 참조")]
    public PlayerMoveController move;
    [Tooltip("속도 0 처리를 위한 Rigidbody(선택)")]
    public Rigidbody rb;

    // ─────────────────────────────────────────────────────────────────────
    // ⑦ 패링 성공 → 궁극기 게이지 수급
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑦ 패링 성공 → 궁극기 게이지 수급")]
    [Tooltip("패링 성공 시 게이지 지급 활성화")]
    public bool grantUltimateOnParry = true;
    [Tooltip("패링 1회당 추가 게이지 양")]
    public float ultimateGainOnParry = 10f;
    [Tooltip("플레이어 궁극기 컨트롤러")]
    public PlayerUltimateController ultimate;

    // ─────────────────────────────────────────────────────────────────────
    // ⑧ 이벤트(필요 시 에디터에서 바인딩)
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑧ 이벤트(옵션)")]
    public UnityEvent OnParrySuccess;
    public UnityEvent OnGuardBlock;

    // ─────────────────────────────────────────────────────────────────────
    // ⑨ 디버그
    // ─────────────────────────────────────────────────────────────────────
    [Header("⑨ 디버그")]
    [Tooltip("애니메이터 참조(비워두면 자동 탐색)")]
    public Animator animator;
    [Tooltip("전방·각도 기즈모")]
    public bool debugDraw = false;
    [Tooltip("파라미터 미존재 경고 로그")]
    public bool warnMissingParams = true;

    // 내부 상태
    public bool IsGuarding  { get; private set; }
    public bool IsAttacking { get; private set; }
    public bool IsParryWindowOpen => _parryOpen;

    // 해시 및 존재 여부 캐시
    int _hashGuardBool, _hashAttackBool, _hashBlockTrig, _hashParryTrig;
    bool _hasGuardBool, _hasAttackBool, _hasBlockTrig, _hasParryTrig;

    // 패링 창
    bool _parryOpen;
    Coroutine _parryCo;

    // 이동 잠금
    int _moveLockRef = 0;
    bool _prevRootMotion;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>(true) ?? GetComponent<Animator>();
        if (!move)     move     = GetComponent<PlayerMoveController>();
        if (!rb)       rb       = GetComponent<Rigidbody>();
        if (!ultimate) ultimate = GetComponent<PlayerUltimateController>();

        CacheAnimatorParams();

        if (guardAction) guardAction.action.Enable();
    }

    void Update()
    {
        // 1) 입력 처리
        if (guardAction && guardAction.action != null)
        {
            bool holding = guardAction.action.IsPressed();
            if (denyGuardWhileAttacking && IsAttacking) holding = false;
            if (autoResumeGuardIfHolding && holding) SetGuarding(true);
            else if (!holding) SetGuarding(false);
        }

        // 2) 애니 파라미터 동기화(외부가 SetBool 해도 읽어온다)
        if (animator)
        {
            if (_hasAttackBool) IsAttacking = animator.GetBool(_hashAttackBool);
            if (_hasGuardBool)  IsGuarding  = animator.GetBool(_hashGuardBool);
        }
    }

    // ───────────────────── 외부 제어 API(하위 호환) ─────────────────────
    public void StartGuard() => SetGuarding(true);
    public void EndGuard()   => SetGuarding(false);

    public void SetGuarding(bool on)
    {
        if (_hasGuardBool && animator) animator.SetBool(_hashGuardBool, on);
        IsGuarding = on;
    }

    // ───────────────────── 패링 창(애니 이벤트용) ─────────────────────
    public void OpenParryWindow()                          { if (enablePerfectGuard) StartParryWindow(perfectGuardWindow); }
    public void OpenParryWindow(float seconds)             { if (enablePerfectGuard) StartParryWindow(Mathf.Max(0f, seconds)); }
    public void OpenParryWindow(int frames)                { if (enablePerfectGuard) StartParryWindow(Mathf.Max(0, frames) / 60f); }
    public void SetParryWindow(bool open)                  { if (!enablePerfectGuard) return; if (_parryCo != null) StopCoroutine(_parryCo); _parryCo = null; _parryOpen = open; }
    public void CloseParryWindow()                         { if (_parryCo != null) StopCoroutine(_parryCo); _parryCo = null; _parryOpen = false; }

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

    // ───────────────────── 리액션 트리거(이벤트 포함) ─────────────────────
    public void PlayBlockReaction()
    {
        if (_hasBlockTrig && animator) animator.SetTrigger(_hashBlockTrig);
        if (lockMoveOnBlock) LockMoveFor(blockMoveLock);
        OnGuardBlock?.Invoke();
    }

    public void PlayParrySuccess()
    {
        if (_hasParryTrig && animator) animator.SetTrigger(_hashParryTrig);
        if (lockMoveOnParry) LockMoveFor(parryMoveLock);
        if (grantUltimateOnParry && ultimate) ultimate.AddGauge(ultimateGainOnParry);
        OnParrySuccess?.Invoke();
    }

    // ───────────────────── 가드 판정(대미지 리시버에서 호출) ─────────────────────
    /// <summary>
    /// 히트 포인트 기준 가드/패링 판정
    /// </summary>
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

        if (enablePerfectGuard && _parryOpen) { isParry = true;  return true; }
        isBlock = true;  return true;
    }

    public Vector3 GetGuardForward()
    {
        if (forwardMode == ForwardMode.CameraForward && cameraTransform) return cameraTransform.forward;
        if (modelForwardSource) return modelForwardSource.forward;
        return transform.forward;
    }
    public Vector3 GetGuardOrigin()
    {
        if (modelForwardSource) return modelForwardSource.position;
        return transform.position;
    }

    // ───────────────────── 이동 잠금 로직 ─────────────────────
    void LockMoveFor(float seconds)
    {
        _moveLockRef++;
        if (move) move.SetExternalControl(true);
        if (stopVelocityOnLock && rb) rb.velocity = Vector3.zero;

        if (disableRootMotionDuringLock && animator && _moveLockRef == 1)
        {
            _prevRootMotion = animator.applyRootMotion;
            animator.applyRootMotion = false;
        }

        StartCoroutine(CoUnlockAfter(seconds));
    }

    IEnumerator CoUnlockAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _moveLockRef = Mathf.Max(0, _moveLockRef - 1);

        if (_moveLockRef == 0)
        {
            if (move) move.SetExternalControl(false);
            if (disableRootMotionDuringLock && animator)
                animator.applyRootMotion = _prevRootMotion;
        }
    }

    // ───────────────────── 애니 파라미터 캐시/검증 ─────────────────────
    void CacheAnimatorParams()
    {
        _hashGuardBool  = Animator.StringToHash(guardBoolParam);
        _hashAttackBool = Animator.StringToHash(isAttackingBoolParam);
        _hashBlockTrig  = Animator.StringToHash(guardBlockTrigger);
        _hashParryTrig  = Animator.StringToHash(parrySuccessTrigger);

        if (!animator) return;

        // 존재 여부 스캔
        _hasGuardBool  = false;
        _hasAttackBool = false;
        _hasBlockTrig  = false;
        _hasParryTrig  = false;

        foreach (var p in animator.parameters)
        {
            if (p.name == guardBoolParam && p.type == AnimatorControllerParameterType.Bool)    _hasGuardBool = true;
            if (p.name == isAttackingBoolParam && p.type == AnimatorControllerParameterType.Bool) _hasAttackBool = true;
            if (p.name == guardBlockTrigger && p.type == AnimatorControllerParameterType.Trigger) _hasBlockTrig = true;
            if (p.name == parrySuccessTrigger && p.type == AnimatorControllerParameterType.Trigger) _hasParryTrig = true;
        }

        if (warnMissingParams)
        {
            if (!_hasGuardBool)  Debug.LogWarning($"[Guard] Animator Bool '{guardBoolParam}' 없음", this);
            if (!_hasAttackBool) Debug.LogWarning($"[Guard] Animator Bool '{isAttackingBoolParam}' 없음", this);
            if (!_hasBlockTrig)  Debug.LogWarning($"[Guard] Animator Trigger '{guardBlockTrigger}' 없음", this);
            if (!_hasParryTrig)  Debug.LogWarning($"[Guard] Animator Trigger '{parrySuccessTrigger}' 없음", this);
        }
    }

    // ───────────────────── 기즈모 ─────────────────────
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

        // 정면 원뿔 시각화(간략)
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Quaternion left  = Quaternion.AngleAxis(+frontArcDegrees * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(-frontArcDegrees * 0.5f, Vector3.up);
        Vector3 l = left * fwd; Vector3 r = right * fwd;
        Gizmos.DrawLine(pos, pos + l * 1.2f);
        Gizmos.DrawLine(pos, pos + r * 1.2f);
    }
}
