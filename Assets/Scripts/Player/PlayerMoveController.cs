using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerMoveController : MonoBehaviour
{
    public Vector2 CurrentMoveInput => _currentMoveInput;
    public Vector3 CurrentWishDirection => _currentWishDirection;
    public Vector3 CurrentPlanarVelocity => _velXZ;
    public Vector3 LastNonZeroMoveDirection => _lastNonZeroMoveDirection;

    // ─────────[① 참조 설정]─────────
    [Header("① 참조 설정")]
    [Tooltip("캐릭터 모델링 루트 (회전할 대상)")]
    [SerializeField] private Transform playerRoot;
    [Tooltip("카메라 트랜스폼 (이동 기준)")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("락온 시스템")]
    [SerializeField] private PlayerLockOn playerLockOn;
    [Tooltip("가드 시스템")]
    [SerializeField] private PlayerGuardController guardController;
    [Tooltip("애니메이터")]
    [SerializeField] private Animator animator;

    // ─────────[② 입력 설정]─────────
    [Header("② 입력 설정")]
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private bool legacyFallback = true;
    [SerializeField] private string axisX = "Horizontal";
    [SerializeField] private string axisY = "Vertical";

    // ─────────[③ 이동/회전]─────────
    [Header("③ 이동/회전")]
    [SerializeField, Range(1f, 10f)] private float runSpeed = 4.5f;
    [SerializeField, Range(2f, 30f)] private float accel = 16f;
    [SerializeField, Range(2f, 30f)] private float decel = 24f;
    [SerializeField, Range(90f, 1080f)] private float rotationSpeed = 720f;
    [SerializeField] private bool rotateOnlyWhenMoving = true;
    [SerializeField, Range(2f, 30f)] private float inputAccel = 14f;
    [SerializeField, Range(2f, 30f)] private float inputDecel = 18f;
    [SerializeField, Range(0f, 0.35f)] private float inputDeadzone = 0.08f;
    [SerializeField] private bool useSoulsLikeRotationTuning = true;
    [SerializeField, Range(180f, 720f)] private float soulsLikeRotationSpeed = 420f;
    [SerializeField, Range(0.01f, 0.4f)] private float rotationDirectionSmoothTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float rotationInputThreshold = 0.18f;

    // ─────────[④ 가드 중 속도 배수]─────────
    [Header("④ 가드 중 속도 배수")]
    [SerializeField, Range(0.1f, 1f)] private float guardFwdMul = 0.8f;
    [SerializeField, Range(0.1f, 1f)] private float guardBackMul = 0.6f;
    [SerializeField, Range(0.1f, 1f)] private float guardStrafeMul = 0.7f;

    [Header("④-1 락온 중 속도 배수")]
    [SerializeField, Range(0.1f, 1f)] private float lockOnFwdMul = 0.82f;
    [SerializeField, Range(0.1f, 1f)] private float lockOnBackMul = 0.62f;
    [SerializeField, Range(0.1f, 1f)] private float lockOnStrafeMul = 0.72f;

    // ─────────[⑤ 중력]─────────
    [Header("⑤ 중력")]
    [SerializeField, Range(5f, 30f)] private float gravity = 20f;
    [SerializeField, Range(0.005f, 0.3f)] private float groundSnap = 0.03f;

    [Header("⑥ 애니 파라미터")]
    [SerializeField] private string p_Speed = "speed";

    [Header("⑦ 정지 모션")]
    [SerializeField] private bool useRunStartMotion = true;
    [SerializeField] private string p_RunStartTrigger = "RunStart";
    [SerializeField, Min(0.01f)] private float runStartMinSpeed = 0.25f;
    [SerializeField, Range(0f, 1f)] private float runStartForwardInputThreshold = 0.45f;
    [SerializeField] private bool useRunStartRootMotion = true;
    [SerializeField, Min(0.05f)] private float runStartRootMotionDuration = 0.8f;
    [SerializeField] private bool useTurnStartMotion = true;
    [SerializeField] private string p_TurnL90Trigger = "TurnL90";
    [SerializeField] private string p_TurnR90Trigger = "TurnR90";
    [SerializeField] private string p_TurnL180Trigger = "TurnL180";
    [SerializeField] private string p_TurnR180Trigger = "TurnR180";
    [SerializeField, Range(10f, 180f)] private float turnStartMinAngle = 55f;
    [SerializeField, Range(90f, 180f)] private float turnStart180Angle = 135f;
    [SerializeField, Min(0f)] private float turnStartCooldown = 0.2f;
    [SerializeField] private bool useRunStopMotion = false;
    [SerializeField] private string p_RunStopTrigger = "RunStop";
    [SerializeField, Min(0.01f)] private float runStopMinSpeed = 0.35f;
    [SerializeField, Min(0.05f)] private float runStopDuration = 0.45f;
    [SerializeField, Min(0f)] private float runStopCooldown = 0.18f;

    // 내부 상태
    private CharacterController _cc;
    private Vector3 _velXZ;
    private float _velY;
    private bool _externLocked; // 외부에서 이동을 막았는지 여부
    private IInputBlocker _inputBlocker;
    private ICombatStateReader _combatStateReader;
    private PlayerReferences _playerReferences;
    private PlayerHealth _playerHealth;
    private float _lastAnimSpeed = float.NaN;
    private Vector2 _smoothedMoveInput;
    private Vector2 _currentMoveInput;
    private Vector3 _currentWishDirection;
    private Vector3 _lastNonZeroMoveDirection;
    private Vector3 _smoothedRotationDirection;
    private bool _runStartActive;
    private bool _runStartCachedRootMotion;
    private bool _runStartHasCachedRootMotion;
    private float _runStartEndTime;
    private bool _runStopActive;
    private bool _runStopCachedRootMotion;
    private bool _runStopHasCachedRootMotion;
    private float _runStopEndTime;
    private float _nextRunStopAllowedTime;
    private float _nextTurnStartAllowedTime;
    private bool _wasMovingForRunStart;
    private bool _wasMovingForRunStop;
    private bool _runStopEligible;

    const float AnimSpeedWriteEpsilon = 0.0025f;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _playerReferences = GetComponent<PlayerReferences>();
        
        // 참조 자동 할당 시도
        if (!playerRoot) playerRoot = _playerReferences ? _playerReferences.PlayerRoot : transform;
        if (!cameraTransform) cameraTransform = GameplaySceneCache.ResolveMainCameraTransform();
        if (!animator) animator = _playerReferences && _playerReferences.MainAnimator ? _playerReferences.MainAnimator : GetComponentInChildren<Animator>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!guardController) guardController = GetComponent<PlayerGuardController>();
        _playerHealth = GetComponent<PlayerHealth>();
        _combatStateReader = CombatStateReaderResolver.ResolveOrAttach(this);
        _inputBlocker = GetComponent<IInputBlocker>();
    }

    void OnEnable() { if (moveAction?.action != null) moveAction.action.Enable(); }
    void OnDisable()
    {
        if (moveAction?.action != null) moveAction.action.Disable();
        StopRunStartMotion();
        StopRunStopMotion();
    }

    void Start()
    {
        // 시작 시 잠금 해제 및 초기화
        _externLocked = false;
        _velXZ = Vector3.zero;
        _smoothedMoveInput = Vector2.zero;
        _currentMoveInput = Vector2.zero;
        _currentWishDirection = Vector3.zero;
        _lastNonZeroMoveDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
        _smoothedRotationDirection = _lastNonZeroMoveDirection;
        SetAnimSpeed(0f);
    }

    void Update()
    {
        // 1. 일시정지 체크 (커서 문제 해결용 필수 코드)
        if (Time.timeScale == 0f) return;

        if (_playerHealth != null && _playerHealth.IsDead)
        {
            _velXZ = Vector3.zero;
            _smoothedMoveInput = Vector2.zero;
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            _smoothedRotationDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            StopRunStopMotion();
            return;
        }

        // 2. 외부 잠금 체크 (공격, 피격, 대쉬 중일 때 이동 금지)
        if (_externLocked)
        {
            _currentMoveInput = Vector2.zero;
            _smoothedMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            _smoothedRotationDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            StopRunStopMotion();
            return;
        }

        if (guardController != null && guardController.IsMoveLockActive)
        {
            _velXZ = Vector3.zero;
            _currentMoveInput = Vector2.zero;
            _smoothedMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            _smoothedRotationDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            StopRunStopMotion();
            return;
        }

        if (IsInputBlocked())
        {
            _velXZ = Vector3.zero;
            _smoothedMoveInput = Vector2.zero;
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            _smoothedRotationDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            StopRunStopMotion();
            return;
        }

        // 3. 입력 받기 (Input System & Legacy & 비상용 강제 입력 통합)
        Vector2 rawInput = ReadMoveInput();
        float dt = Time.deltaTime;
        _smoothedMoveInput = MoveTowardsInput(_smoothedMoveInput, rawInput, inputAccel, inputDecel, dt);
        _currentMoveInput = _smoothedMoveInput;

        // 4. 방향 및 회전 계산
        bool locked = playerLockOn && playerLockOn.IsLocked;
        Transform cam = ResolveMovementCameraTransform();
        Vector3 fwd = Flat(cam.forward);
        Vector3 right = Flat(cam.right);

        Vector3 wishDir = (right * _smoothedMoveInput.x + fwd * _smoothedMoveInput.y);
        if (wishDir.sqrMagnitude > 1e-6f) wishDir.Normalize();
        _currentWishDirection = wishDir;
        if (wishDir.sqrMagnitude > 0.0004f)
            _lastNonZeroMoveDirection = wishDir;

        // 5. 속도 계산 (가속/감속)
        float targetSpeed = ComputeTargetSpeed(_smoothedMoveInput, IsGuarding(), locked) * Mathf.Clamp01(_smoothedMoveInput.magnitude);
        Vector3 targetVel = wishDir * targetSpeed;

        _velXZ = MoveTowardsXZ(_velXZ, targetVel, accel, decel, dt);

        // 6. 캐릭터 회전
        if (!locked)
        {
            Vector3 rotationDirection = wishDir.sqrMagnitude > 0.000001f ? wishDir : _velXZ;
            bool shouldRotate = !rotateOnlyWhenMoving || rotationDirection.sqrMagnitude > 0.0004f;
            if (shouldRotate && rotationDirection.sqrMagnitude > 0.000001f)
            {
                Vector3 desiredDirection = rotationDirection.normalized;
                if (useSoulsLikeRotationTuning)
                {
                    if (_smoothedMoveInput.magnitude < rotationInputThreshold && _velXZ.sqrMagnitude <= 0.0004f)
                        desiredDirection = _smoothedRotationDirection.sqrMagnitude > 0.0001f ? _smoothedRotationDirection : desiredDirection;

                    Vector3 currentDirection = _smoothedRotationDirection.sqrMagnitude > 0.0001f ? _smoothedRotationDirection : desiredDirection;
                    float smoothT = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, rotationDirectionSmoothTime));
                    _smoothedRotationDirection = Vector3.Slerp(currentDirection, desiredDirection, smoothT).normalized;
                    desiredDirection = _smoothedRotationDirection;
                }
                else
                {
                    _smoothedRotationDirection = desiredDirection;
                }

                Quaternion t = Quaternion.LookRotation(desiredDirection, Vector3.up);
                float turnSpeed = useSoulsLikeRotationTuning ? soulsLikeRotationSpeed : rotationSpeed;
                playerRoot.rotation = Quaternion.RotateTowards(playerRoot.rotation, t, turnSpeed * dt);
            }
        }

        // 7. 최종 이동 적용 (중력 포함)
        MoveWithGravity(_runStartActive ? Vector3.zero : _velXZ);

        // This controller currently drives an Idle/Run style locomotion setup.
        // Use desired move speed instead of smoothed velocity so run anim engages
        // reliably as soon as movement input is committed.
        float animSpeed01 = _velXZ.sqrMagnitude <= 0.0001f
            ? 0f
            : Mathf.Clamp01(_velXZ.magnitude / Mathf.Max(0.01f, runSpeed));
        UpdateRunStartMotion(animSpeed01, locked, IsGuarding());
        SetAnimSpeed(animSpeed01);
    }

    // ──────────────────────────────────────────────
    // 외부 제어 함수 (이게 없어서 에러가 났었습니다)
    // ──────────────────────────────────────────────

    // 공격이나 피격 시 이동을 막기 위해 호출하는 함수
    public void SetExternalControl(bool locked)
    {
        _externLocked = locked;
        if (locked)
        {
            _velXZ = Vector3.zero;
            _smoothedMoveInput = Vector2.zero;
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            _smoothedRotationDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
            SetAnimSpeed(0f);
            StopRunStopMotion();
        }
    }

    // 회피 시작 시 호출
    public void OnDodgeStart()
    {
        _externLocked = true;
        _velXZ = Vector3.zero;
        _smoothedMoveInput = Vector2.zero;
        _currentMoveInput = Vector2.zero;
        _currentWishDirection = Vector3.zero;
        _smoothedRotationDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
        SetAnimSpeed(0f);
        StopRunStopMotion();
    }

    // 회피 종료 시 호출
    public void OnDodgeEnd()
    {
        _externLocked = false;
    }

    // ──────────────────────────────────────────────
    // 내부 유틸리티
    // ──────────────────────────────────────────────

    Vector2 ReadMoveInput()
    {
        // 1. New Input System Action이 연결되어 있다면 사용
        if (moveAction != null && moveAction.action != null)
            return moveAction.action.ReadValue<Vector2>();

        // 2. Legacy Input (Project Settings가 Both/Old일 때)
        Vector2 input = Vector2.zero;
        if (legacyFallback)
        {
            try
            {
                input.x = Input.GetAxisRaw(axisX);
                input.y = Input.GetAxisRaw(axisY);
            }
            catch { }
        }

        // 3. ★ [비상용] 강제 키보드 입력 체크
        // (설정이 꼬여서 위 1,2번이 다 실패해도 이건 작동함)
        if (input == Vector2.zero && Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) input.y += 1;
            if (Keyboard.current.sKey.isPressed) input.y -= 1;
            if (Keyboard.current.dKey.isPressed) input.x += 1;
            if (Keyboard.current.aKey.isPressed) input.x -= 1;
        }

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        return input.magnitude <= inputDeadzone ? Vector2.zero : input;
    }

    float ComputeTargetSpeed(Vector2 input, bool guarding, bool locked)
    {
        if (input.sqrMagnitude <= 1e-6f) return 0f;

        if (guarding)
        {
            float f = input.y;
            if (f > 0.5f) return runSpeed * guardFwdMul;
            if (f < -0.5f) return runSpeed * guardBackMul;
            return runSpeed * guardStrafeMul;
        }

        if (locked)
        {
            float f = input.y;
            if (f > 0.5f) return runSpeed * lockOnFwdMul;
            if (f < -0.5f) return runSpeed * lockOnBackMul;
            return runSpeed * lockOnStrafeMul;
        }

        return runSpeed;
    }

    bool IsGuarding()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsGuardMovementActive();

        return guardController && guardController.IsGuardMovementActive;
    }

    Transform ResolveMovementCameraTransform()
    {
        Transform resolved = GameplaySceneCache.ResolveMainCameraTransform();
        if (IsValidBasisTransform(resolved))
        {
            cameraTransform = resolved;
            return resolved;
        }

        if (IsValidBasisTransform(cameraTransform))
            return cameraTransform;

        return playerRoot != null ? playerRoot : transform;
    }

    void MoveWithGravity(Vector3 vXZ)
    {
        if (_cc == null || !_cc.enabled)
            return;

        float dt = Time.deltaTime;
        float snapVelocity = -Mathf.Max(0.005f, groundSnap);
        bool groundedBeforeMove = _cc.isGrounded;

        if (groundedBeforeMove && _velY <= 0f)
            _velY = snapVelocity;
        else
            _velY -= gravity * dt;

        CollisionFlags flags = _cc.Move(vXZ * dt + Vector3.up * (_velY * dt));
        if ((flags & CollisionFlags.Below) != 0 && _velY <= 0f)
            _velY = snapVelocity;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    static bool IsValidBasisTransform(Transform candidate)
    {
        return candidate != null && candidate.gameObject.activeInHierarchy;
    }

    static Vector3 MoveTowardsXZ(Vector3 cur, Vector3 tgt, float acc, float dec, float dt)
    {
        Vector3 diff = tgt - cur;
        float maxDelta = ((tgt.magnitude >= cur.magnitude) ? acc : dec) * dt;
        return (diff.magnitude <= maxDelta) ? tgt : cur + diff.normalized * maxDelta;
    }

    static Vector2 MoveTowardsInput(Vector2 current, Vector2 target, float acc, float dec, float dt)
    {
        Vector2 diff = target - current;
        float maxDelta = ((target.magnitude >= current.magnitude) ? acc : dec) * dt;
        float diffMagnitude = diff.magnitude;
        if (diffMagnitude <= maxDelta || diffMagnitude <= 0.0001f)
            return target;

        return current + diff / diffMagnitude * maxDelta;
    }

    void SetAnimSpeed(float spd01)
    {
        if (!animator || string.IsNullOrEmpty(p_Speed)) return;

        if (!float.IsNaN(_lastAnimSpeed) && Mathf.Abs(_lastAnimSpeed - spd01) <= AnimSpeedWriteEpsilon)
            return;

        animator.SetFloat(p_Speed, spd01);
        _lastAnimSpeed = spd01;
    }

    void UpdateRunStopMotion(float speed01)
    {
        bool isMoving = speed01 > 0.05f || _currentMoveInput.sqrMagnitude > 0.0004f;
        if (isMoving && speed01 >= runStopMinSpeed)
            _runStopEligible = true;

        bool shouldStop = useRunStopMotion
            && !_runStopActive
            && _wasMovingForRunStop
            && !isMoving
            && _runStopEligible
            && Time.time >= _nextRunStopAllowedTime;

        if (shouldStop)
            PlayRunStopMotion();

        if (isMoving && _runStopActive)
            StopRunStopMotion();

        _wasMovingForRunStop = isMoving;

        if (_runStopActive && Time.time >= _runStopEndTime)
            StopRunStopMotion();
    }

    void UpdateRunStartMotion(float speed01, bool locked, bool guarding)
    {
        bool isMoving = speed01 >= runStartMinSpeed || _currentMoveInput.sqrMagnitude > 0.0004f;
        bool canPlayStart = CanPlayRunStartMotion(locked, guarding);

        if (!canPlayStart && _runStartActive)
            StopRunStartMotion();

        if (useRunStartMotion && canPlayStart && !_wasMovingForRunStart && isMoving)
            PlayRunStartMotion();

        _wasMovingForRunStart = isMoving;

        if (_runStartActive && (!isMoving || Time.time >= _runStartEndTime))
            StopRunStartMotion();
    }

    void PlayRunStartMotion()
    {
        if (animator == null || string.IsNullOrEmpty(p_RunStartTrigger))
            return;

        StopRunStartMotion();
        if (useRunStartRootMotion)
        {
            _runStartCachedRootMotion = animator.applyRootMotion;
            _runStartHasCachedRootMotion = true;
            animator.applyRootMotion = true;
            _runStartEndTime = Time.time + runStartRootMotionDuration;
            _runStartActive = true;
        }

        animator.ResetTrigger(p_RunStartTrigger);
        animator.SetTrigger(p_RunStartTrigger);
    }

    bool CanPlayRunStartMotion(bool locked, bool guarding)
    {
        if (locked || guarding)
            return false;

        return _currentMoveInput.y >= runStartForwardInputThreshold;
    }

    void StopRunStartMotion()
    {
        _runStartActive = false;
        if (animator != null && _runStartHasCachedRootMotion)
            animator.applyRootMotion = _runStartCachedRootMotion;
        _runStartHasCachedRootMotion = false;
    }

    bool TryPlayTurnStartMotion()
    {
        if (!useTurnStartMotion || animator == null || playerRoot == null || _currentWishDirection.sqrMagnitude <= 0.0004f)
            return false;
        if (playerLockOn != null && playerLockOn.IsLocked)
            return false;
        if (Time.time < _nextTurnStartAllowedTime)
            return false;

        float signedAngle = Vector3.SignedAngle(Flat(playerRoot.forward), _currentWishDirection.normalized, Vector3.up);
        float absAngle = Mathf.Abs(signedAngle);
        if (absAngle < turnStartMinAngle)
            return false;

        string trigger = absAngle >= turnStart180Angle
            ? (signedAngle < 0f ? p_TurnL180Trigger : p_TurnR180Trigger)
            : (signedAngle < 0f ? p_TurnL90Trigger : p_TurnR90Trigger);

        if (string.IsNullOrEmpty(trigger))
            return false;

        animator.ResetTrigger(trigger);
        animator.SetTrigger(trigger);
        _nextTurnStartAllowedTime = Time.time + turnStartCooldown;
        return true;
    }

    void PlayRunStopMotion()
    {
        if (animator == null || string.IsNullOrEmpty(p_RunStopTrigger))
            return;

        StopRunStopMotion();

        _runStopCachedRootMotion = animator.applyRootMotion;
        _runStopHasCachedRootMotion = true;
        animator.applyRootMotion = true;
        animator.ResetTrigger(p_RunStopTrigger);
        animator.SetTrigger(p_RunStopTrigger);
        _runStopEndTime = Time.time + runStopDuration;
        _nextRunStopAllowedTime = _runStopEndTime + runStopCooldown;
        _runStopActive = true;
        _runStopEligible = false;
    }

    void StopRunStopMotion()
    {
        StopRunStartMotion();
        _runStopActive = false;
        if (animator != null && _runStopHasCachedRootMotion)
            animator.applyRootMotion = _runStopCachedRootMotion;
        _runStopHasCachedRootMotion = false;
        ResetRunStopTracking();
    }

    void ResetRunStopTracking()
    {
        _wasMovingForRunStop = false;
        _runStopEligible = false;
    }

    void OnAnimatorMove()
    {
        if ((!_runStartActive && !_runStopActive) || animator == null || _cc == null || !_cc.enabled)
            return;

        Vector3 delta = animator.deltaPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0.000001f)
            _cc.Move(delta);

        if (playerRoot != null)
            playerRoot.rotation *= animator.deltaRotation;
    }

    bool IsInputBlocked()
    {
        return _inputBlocker != null && _inputBlocker.IsBlocked;
    }
}
