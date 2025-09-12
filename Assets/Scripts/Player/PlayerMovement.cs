using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    // ============================== ▼ 변수 헤더(튜닝 가이드) ▼ ==============================
    // [이동 기본값]
    // runSpeed            : 러닝 기준 속도 (m/s)
    // rotationSpeed       : 회전 스무딩 속도

    // [애니메이터 파라미터]
    // speedParam          : 이동 블렌드트리에 쓸 Float 파라미터명 (프로젝트 규약: "speed" 소문자)
    // speedDampTime       : speed 파라미터 댐핑 시간
    // runStartTrigger     : 정지→이동 엣지에 쏘는 트리거(선택)

    // [이동 시작 감지/스무딩]
    // startThreshold      : 정지→이동 판정 임계값(0~1)
    // smoothRate          : 입력 스무딩 속도

    // [RunStart 상태/태그 감지]
    // runStartStateName   : RunStart 스테이트 이름
    // useRunStartStateTag : 이름 대신 태그로 감지할지
    // runStartStateTag    : RunStart 태그명

    // [RunStart 게이트 & 가속 커브]
    // runStartSpeedCurve  : RunStart 동안 속도 배율 커브(0~1)
    // gateNormalizedTime  : 정규화 시간 t<gateNormalizedTime 동안 속도 0
    // useGateTimer        : 트리거 시점부터 gateTimerSeconds 동안 추가로 속도 0 유지
    // gateTimerSeconds    : 추가 게이트 시간(초)
    // runStartRotationMultiplier : RunStart 중 회전 민감도 배율

    // [루트모션]
    // forceDisableRootMotion        : 항상 루트모션 끄기(권장)
    // disableRootMotionOnlyDuringGate : 항상 끄지 않을 때, 게이트 중에만 루트모션 끄기

    // [레퍼런스]
    // animator            : 애니메이터
    // playerLockOn        : 락온 컨트롤러(있으면 락온 이동/시선 사용)

    // [가드 연동]
    // guard               : PlayerGuardController 참조(가드 on/off 시 속도 배율을 콜백으로 수신)
    // useGuardRotationScale: 가드 중 회전도 같이 둔화시킬지
    // guardMoveMultiplier : 가드 중 이동 속도 배율(실시간 수신값, 디버그 표시용)
    // guardRotationMul    : 가드 중 회전 배율 (예: 0.8f)
    // ========================================================================================

    [Header("Move")]
    public float runSpeed = 7f;
    public float rotationSpeed = 10f;

    [Header("Animator Params")]
    public string speedParam = "speed";      // ★ 소문자 규약
    public float speedDampTime = 0.1f;
    public string runStartTrigger = "RunStart";

    [Header("Start Detection")]
    public float startThreshold = 0.2f;
    public float smoothRate = 10f;

    [Header("RunStart Detect (State/Tag)")]
    public string runStartStateName = "RunStart";
    public bool useRunStartStateTag = false;
    public string runStartStateTag = "RunStart";

    [Header("RunStart Gating & Accel")]
    [Tooltip("RunStart 동안 속도 배율 커브 (x:0~1 정규화 시간, y:0~1 비율)")]
    public AnimationCurve runStartSpeedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("정규화시간 기준 게이트 임계값(이 값 전까지 이동 0)")]
    [Range(0f, 1f)] public float gateNormalizedTime = 0.2f;

    [Tooltip("트리거 시점부터 초 단위로 추가 게이트(전이구간 보정)")]
    public bool useGateTimer = true;
    public float gateTimerSeconds = 0.18f;

    [Tooltip("RunStart 중 회전 민감도 배율")]
    public float runStartRotationMultiplier = 0.6f;

    [Header("Root Motion")]
    [Tooltip("항상 루트모션 끄기(권장)")]
    public bool forceDisableRootMotion = true;
    [Tooltip("항상 끄지 않을 때: RunStart 게이트 활성 중에는 루트모션 끄기")]
    public bool disableRootMotionOnlyDuringGate = true;

    [Header("Refs")]
    public Animator animator;
    public PlayerLockOn playerLockOn;

    [Header("Guard Link")]
    public PlayerGuardController guard;               // ★ 연결 필드
    public bool useGuardRotationScale = false;        // 가드 중 회전도 둔화할지
    [Range(0.1f, 1f)] public float guardRotationMul = 0.85f;

    // ───── 내부 상태 ─────
    private CharacterController controller;
    private PlayerHealth health;
    private float moveMagSmoothed;   // 0~1
    private bool wasMoving;          // 직전 프레임 이동 여부

    // 중력
    private float verticalVel;
    private const float Gravity = -20f;

    // 런스타트 게이트 타이머
    private float runStartGateTimer;
    private bool hasRunStartParam;

    // 외부 잠금(공격/피격 등)
    private bool externalLocked = false;

    // 가드 배율(훅으로 수신)
    private float guardMoveMultiplier = 1f;  // ★ 가드 중 0.7 등 실시간 반영

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        if (!animator) animator = GetComponent<Animator>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!guard) guard = GetComponent<PlayerGuardController>(); // 같은 오브젝트면 자동 연결

        // 가드 속도배율 훅 등록 ★★
        if (guard != null)
            guard.RegisterGuardMoveScaleHook(SetGuardMoveScale);
        else
            Debug.LogWarning("[PlayerMovement] PlayerGuardController가 없어 가드 속도 배율을 받지 못합니다.");

        // 런스타트 커브 기본값 보정
        if (runStartSpeedCurve.keys.Length <= 2)
        {
            runStartSpeedCurve = new AnimationCurve(
                new Keyframe(0.00f, 0.00f, 0f, 2f),
                new Keyframe(0.35f, 0.30f, 1f, 1f),
                new Keyframe(0.55f, 0.60f, 1f, 1f),
                new Keyframe(0.75f, 0.90f, 1f, 1f),
                new Keyframe(1.00f, 1.00f, 0.5f, 0f)
            );
        }

        // 파라미터 안전검사
        hasRunStartParam = HasAnimatorParam(animator, runStartTrigger, AnimatorControllerParameterType.Trigger);
        if (!hasRunStartParam)
            Debug.LogWarning($"[PlayerMovement] Animator Trigger '{runStartTrigger}' not found. Animator에 같은 이름의 Trigger를 추가하세요.");

        // 루트모션 차단(권장)
        if (animator && forceDisableRootMotion) animator.applyRootMotion = false;
    }

    void Update()
    {
        // ── 상단 상태 가드 ───────────────────────────────────────────
        bool isAttacking = GetComponent<PlayerCombatController>()?.IsAttacking ?? false;

        if (externalLocked || isAttacking)
        {
            SetAnimSpeed(0f);
            return;
        }

        if (GetComponent<PlayerDash>()?.IsDashing ?? false) { return; }
        if (health != null && health.IsStaggered) { SetAnimSpeed(0f); return; }

        // ── 입력 읽기 & 스무딩 ───────────────────────────────────────
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float magRaw = new Vector2(h, v).magnitude;                 // 0~1
        moveMagSmoothed = Mathf.MoveTowards(moveMagSmoothed, magRaw, Time.deltaTime * smoothRate);
        bool nowMoving = moveMagSmoothed > startThreshold;

        // 정지→이동 엣지: RunStart 트리거 + 게이트 타이머
        if (!wasMoving && nowMoving && animator && hasRunStartParam)
        {
            animator.SetTrigger(runStartTrigger);
            if (useGateTimer) runStartGateTimer = gateTimerSeconds;
        }
        wasMoving = nowMoving;

        // Animator speed 업데이트(댐핑)
        SetAnimSpeed(moveMagSmoothed);

        // RunStart 감지(현재/전이 모두)
        bool inRunStart = IsInRunStartOrTransition(out float normT);

        // 게이트 활성 여부
        bool gateActive = false;
        if (inRunStart && normT < gateNormalizedTime) gateActive = true;
        if (useGateTimer && runStartGateTimer > 0f) gateActive = true;

        // 루트모션 제어
        if (animator && !forceDisableRootMotion && disableRootMotionOnlyDuringGate)
            animator.applyRootMotion = !gateActive;

        // 가변 속도/회전
        float curRunSpeed = runSpeed;
        float curRotSpeed = rotationSpeed;

        // ★ 가드 배율 먼저 반영
        curRunSpeed *= guardMoveMultiplier;
        if (useGuardRotationScale) curRotSpeed *= Mathf.Lerp(1f, guardRotationMul, 1f - guardMoveMultiplier);

        // RunStart 보정 적용
        if (inRunStart)
        {
            if (gateActive)
            {
                curRunSpeed = 0f; // 발 딛기 전 이동 0
            }
            else
            {
                float mul = Mathf.Clamp01(runStartSpeedCurve.Evaluate(normT));
                curRunSpeed *= mul; // 가드 배율 이후에 곱해도 무방
            }
            curRotSpeed *= Mathf.Clamp01(runStartRotationMultiplier);
        }

        // 실제 이동/회전
        Vector3 moveDir = Vector3.zero;

        if (moveMagSmoothed > 0.05f)
        {
            if (playerLockOn != null && playerLockOn.IsLockOn)
            {
                moveDir = playerLockOn.GetLockOnMoveDirection(h, v);
                controller.Move(moveDir * curRunSpeed * Time.deltaTime);

                Vector3 lookDir = playerLockOn.GetLookDirection();
                if (lookDir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, Quaternion.LookRotation(lookDir),
                        curRotSpeed * Time.deltaTime
                    );
            }
            else
            {
                Transform cam = Camera.main ? Camera.main.transform : transform;
                Vector3 camFwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;

                moveDir = (camFwd * v + camRight * h).normalized;
                controller.Move(moveDir * curRunSpeed * Time.deltaTime);

                if (moveDir.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, Quaternion.LookRotation(moveDir),
                        curRotSpeed * Time.deltaTime
                    );
            }
        }

        // 중력
        if (!controller.isGrounded) verticalVel += Gravity * Time.deltaTime;
        else if (verticalVel < 0f) verticalVel = -2f;
        if (Mathf.Abs(verticalVel) > 0.001f)
            controller.Move(Vector3.up * verticalVel * Time.deltaTime);

        // 게이트 타이머 감쇠
        if (runStartGateTimer > 0f) runStartGateTimer -= Time.deltaTime;
    }

    // ▶ 전투/피격 등 외부 시스템이 이동을 잠그는 진입점
    public void SetExternalControl(bool locked)
    {
        externalLocked = locked;
        SetAnimSpeed(0f);
    }

    // ★ 가드 훅에서 호출될 Setter
    void SetGuardMoveScale(float scale)
    {
        guardMoveMultiplier = Mathf.Clamp(scale, 0.1f, 1.5f);
        // Debug.Log($"[Move] guardMoveMultiplier = {guardMoveMultiplier:0.00}");
    }

    private void SetAnimSpeed(float value)
    {
        if (!animator) return;
        animator.SetFloat(speedParam, value, speedDampTime, Time.deltaTime);
        // 호환용(혹시 다른 곳에서 "speed" 대문자/소문자 섞어 쓰는 경우)
        animator.SetFloat("speed", value, speedDampTime, Time.deltaTime);
    }

    public void HandleMovementExternal()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float magRaw = new Vector2(h, v).magnitude;
        moveMagSmoothed = Mathf.MoveTowards(moveMagSmoothed, magRaw, Time.deltaTime * smoothRate);
        SetAnimSpeed(moveMagSmoothed);
    }

    // ───── Helpers ──────────────────────────────────────────────
    private bool HasAnimatorParam(Animator anim, string name, AnimatorControllerParameterType type)
    {
        if (!anim || string.IsNullOrEmpty(name)) return false;
        foreach (var p in anim.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }

    // 현재 상태뿐 아니라 "전이 중 다음 상태"가 RunStart인지까지 체크
    private bool IsInRunStartOrTransition(out float normalized01)
    {
        normalized01 = 0f;
        if (!animator) return false;

        // Base layer = 0
        var cur = animator.GetCurrentAnimatorStateInfo(0);

        bool inCur = useRunStartStateTag ? cur.IsTag(runStartStateTag) : cur.IsName(runStartStateName);
        if (inCur)
        {
            normalized01 = Mathf.Repeat(cur.normalizedTime, 1f);
            return true;
        }

        if (animator.IsInTransition(0))
        {
            var next = animator.GetNextAnimatorStateInfo(0);
            bool inNext = useRunStartStateTag ? next.IsTag(runStartStateTag) : next.IsName(runStartStateName);
            if (inNext)
            {
                normalized01 = Mathf.Repeat(next.normalizedTime, 1f);
                return true;
            }
        }
        return false;
    }
}
