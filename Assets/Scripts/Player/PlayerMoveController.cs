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

    // ─────────[④ 가드 중 속도 배수]─────────
    [Header("④ 가드 중 속도 배수")]
    [SerializeField, Range(0.1f, 1f)] private float guardFwdMul = 0.8f;
    [SerializeField, Range(0.1f, 1f)] private float guardBackMul = 0.6f;
    [SerializeField, Range(0.1f, 1f)] private float guardStrafeMul = 0.7f;

    // ─────────[⑤ 중력]─────────
    [Header("⑤ 중력")]
    [SerializeField, Range(5f, 30f)] private float gravity = 20f;
    [SerializeField, Range(0.005f, 0.3f)] private float groundSnap = 0.03f;

    [Header("⑥ 애니 파라미터")]
    [SerializeField] private string p_Speed = "speed";

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
    private Vector2 _currentMoveInput;
    private Vector3 _currentWishDirection;
    private Vector3 _lastNonZeroMoveDirection;

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
    void OnDisable() { if (moveAction?.action != null) moveAction.action.Disable(); }

    void Start()
    {
        // 시작 시 잠금 해제 및 초기화
        _externLocked = false;
        _velXZ = Vector3.zero;
        _currentMoveInput = Vector2.zero;
        _currentWishDirection = Vector3.zero;
        _lastNonZeroMoveDirection = playerRoot != null ? Flat(playerRoot.forward) : Vector3.forward;
        SetAnimSpeed(0f);
    }

    void Update()
    {
        // 1. 일시정지 체크 (커서 문제 해결용 필수 코드)
        if (Time.timeScale == 0f) return;

        if (_playerHealth != null && _playerHealth.IsDead)
        {
            _velXZ = Vector3.zero;
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            return;
        }

        // 2. 외부 잠금 체크 (공격, 피격, 대쉬 중일 때 이동 금지)
        if (_externLocked)
        {
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            return;
        }

        if (IsInputBlocked())
        {
            _velXZ = Vector3.zero;
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            MoveWithGravity(Vector3.zero);
            SetAnimSpeed(0f);
            return;
        }

        // 3. 입력 받기 (Input System & Legacy & 비상용 강제 입력 통합)
        Vector2 input = ReadMoveInput();
        _currentMoveInput = input;

        // 4. 방향 및 회전 계산
        bool locked = playerLockOn && playerLockOn.IsLocked;
        Vector3 fwd, right;

        if (locked) // 락온 상태: 타겟 중심 이동
        {
            fwd = Flat(playerRoot.forward);
            right = Flat(playerRoot.right);
        }
        else // 일반 상태: 카메라 기준 이동
        {
            Transform cam = cameraTransform;
            if (cam == null)
            {
                cam = GameplaySceneCache.ResolveMainCameraTransform();
                if (cam != null)
                    cameraTransform = cam;
                else
                    cam = playerRoot;
            }
            fwd = Flat(cam.forward);
            right = Flat(cam.right);
        }

        Vector3 wishDir = (right * input.x + fwd * input.y);
        if (wishDir.sqrMagnitude > 1e-6f) wishDir.Normalize();
        _currentWishDirection = wishDir;
        if (wishDir.sqrMagnitude > 0.0004f)
            _lastNonZeroMoveDirection = wishDir;

        // 5. 속도 계산 (가속/감속)
        float targetSpeed = ComputeTargetSpeed(input, IsGuarding());
        Vector3 targetVel = wishDir * targetSpeed;

        float dt = Time.deltaTime;
        _velXZ = MoveTowardsXZ(_velXZ, targetVel, accel, decel, dt);

        // 6. 캐릭터 회전
        if (!locked)
        {
            Vector3 rotationDirection = wishDir.sqrMagnitude > 0.000001f ? wishDir : _velXZ;
            bool shouldRotate = !rotateOnlyWhenMoving || rotationDirection.sqrMagnitude > 0.0004f;
            if (shouldRotate && rotationDirection.sqrMagnitude > 0.000001f)
            {
                Quaternion t = Quaternion.LookRotation(rotationDirection.normalized, Vector3.up);
                playerRoot.rotation = Quaternion.RotateTowards(playerRoot.rotation, t, rotationSpeed * dt);
            }
        }

        // 7. 최종 이동 적용 (중력 포함)
        MoveWithGravity(_velXZ);

        // This controller currently drives an Idle/Run style locomotion setup.
        // Use desired move speed instead of smoothed velocity so run anim engages
        // reliably as soon as movement input is committed.
        float animSpeed01 = targetSpeed <= 0.01f
            ? 0f
            : Mathf.Clamp01(targetSpeed / Mathf.Max(0.01f, runSpeed * 0.6f));
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
            _currentMoveInput = Vector2.zero;
            _currentWishDirection = Vector3.zero;
            SetAnimSpeed(0f);
        }
    }

    // 회피 시작 시 호출
    public void OnDodgeStart()
    {
        _externLocked = true;
        _velXZ = Vector3.zero;
        _currentMoveInput = Vector2.zero;
        _currentWishDirection = Vector3.zero;
        SetAnimSpeed(0f);
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

        return input.sqrMagnitude > 1f ? input.normalized : input;
    }

    float ComputeTargetSpeed(Vector2 input, bool guarding)
    {
        if (input.sqrMagnitude <= 1e-6f) return 0f;
        if (!guarding) return runSpeed;
        
        float f = input.y;
        if (f > 0.5f) return runSpeed * guardFwdMul;
        if (f < -0.5f) return runSpeed * guardBackMul;
        return runSpeed * guardStrafeMul;
    }

    bool IsGuarding()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsGuardMovementActive();

        return guardController && guardController.IsGuardMovementActive;
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

    static Vector3 MoveTowardsXZ(Vector3 cur, Vector3 tgt, float acc, float dec, float dt)
    {
        Vector3 diff = tgt - cur;
        float maxDelta = ((tgt.magnitude >= cur.magnitude) ? acc : dec) * dt;
        return (diff.magnitude <= maxDelta) ? tgt : cur + diff.normalized * maxDelta;
    }

    void SetAnimSpeed(float spd01)
    {
        if (!animator || string.IsNullOrEmpty(p_Speed)) return;

        if (!float.IsNaN(_lastAnimSpeed) && Mathf.Abs(_lastAnimSpeed - spd01) <= AnimSpeedWriteEpsilon)
            return;

        animator.SetFloat(p_Speed, spd01);
        _lastAnimSpeed = spd01;
    }

    bool IsInputBlocked()
    {
        return _inputBlocker != null && _inputBlocker.IsBlocked;
    }
}
