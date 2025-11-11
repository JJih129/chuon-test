using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerMoveController : MonoBehaviour
{
    // ─────────[① 참조 설정]─────────
    [Header("① 참조 설정(필수)")]
    [Tooltip("이동/회전을 적용할 루트(캐릭터가 실제로 도는 트랜스폼)")]
    [SerializeField] private Transform playerRoot;                      // 조절값
    [Tooltip("카메라 기준 이동용 트랜스폼(비우면 MainCamera 자동 할당)")]
    [SerializeField] private Transform cameraTransform;                 // 조절값
    [Tooltip("락온 컨트롤러(락온 여부/타깃 확인)")]
    [SerializeField] private PlayerLockOn playerLockOn;                 // 조절값
    [Tooltip("가드 컨트롤러(가드 중 속도 배수 적용)")]
    [SerializeField] private PlayerGuardController guardController;     // 조절값
    [Tooltip("Animator(선택). speed 파라미터만 갱신")]
    [SerializeField] private Animator animator;                         // 조절값

    // ─────────[② 입력 설정]─────────
    [Header("② 입력 설정(신규 Input System 우선)")]
    [Tooltip("Move 액션(Vector2). 할당 시 Input System 사용, 없으면 WASD 폴백")]
    [SerializeField] private InputActionReference moveAction;           // 조절값
    [Tooltip("WASD 폴백 사용 여부(신규 액션이 없을 때만 동작)")]
    [SerializeField] private bool legacyFallback = true;                // 조절값
    [Tooltip("레거시 입력 축 이름(Horizontal/Vertical)")]
    [SerializeField] private string axisX = "Horizontal";               // 조절값
    [SerializeField] private string axisY = "Vertical";                 // 조절값

    // ─────────[③ 이동/회전]─────────
    [Header("③ 이동/회전(걷기/점프 없음)")]
    [Tooltip("달리기 속도(m/s)")]
    [SerializeField, Range(1f,10f)] private float runSpeed = 4.5f;      // 조절값
    [Tooltip("가속/감속(m/s²)")]
    [SerializeField, Range(2f,30f)] private float accel = 16f;          // 조절값
    [SerializeField, Range(2f,30f)] private float decel = 24f;          // 조절값
    [Tooltip("회전 속도(도/초) — 비락온에서만 적용")]
    [SerializeField, Range(90f,1080f)] private float rotationSpeed = 720f; // 조절값
    [Tooltip("이동 입력이 있을 때만 회전할지 여부")]
    [SerializeField] private bool rotateOnlyWhenMoving = true;          // 조절값

    // ─────────[④ 가드 중 속도 배수]─────────
    [Header("④ 가드 중 속도 배수")]
    [Tooltip("전진 시 배수")]
    [SerializeField, Range(0.1f,1f)] private float guardFwdMul = 0.8f;  // 조절값
    [Tooltip("후진 시 배수")]
    [SerializeField, Range(0.1f,1f)] private float guardBackMul = 0.6f; // 조절값
    [Tooltip("좌우 스트레이프 배수")]
    [SerializeField, Range(0.1f,1f)] private float guardStrafeMul = 0.7f; // 조절값

    // ─────────[⑤ 중력 설정]─────────
    [Header("⑤ 중력/접지")]
    [Tooltip("중력 가속도(|g|, m/s²)")]
    [SerializeField, Range(5f,30f)] private float gravity = 20f;        // 조절값
    [Tooltip("지면 스냅용 Y속도")]
    [SerializeField, Range(0.01f,0.3f)] private float groundSnap = 0.1f;// 조절값

    // ─────────[⑥ 애니 파라미터]─────────
    [Header("⑥ 애니 파라미터(선택)")]
    [Tooltip("이동 속도 정규화 파라미터명(없으면 비워두기)")]
    [SerializeField] private string p_Speed = "speed";                  // 조절값

    // ───────── 내부 상태 ─────────
    private CharacterController _cc;
    private Vector3 _velXZ;                 // 평면 속도
    private float _velY;                    // 수직 속도
    private bool _externLocked;             // 연출/회피 중 이동 잠금

    void Reset()
    {
        if (!playerRoot) playerRoot = transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!guardController) guardController = GetComponent<PlayerGuardController>();
    }

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (!playerRoot) playerRoot = transform;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!guardController) guardController = GetComponent<PlayerGuardController>();
    }

    void OnEnable()  { if (moveAction?.action != null) moveAction.action.Enable(); }
    void OnDisable() { if (moveAction?.action != null) moveAction.action.Disable(); }

    void Update()
    {
        // 외부 잠금 시 정지
        if (_externLocked) { MoveWithGravity(Vector3.zero); SetAnimSpeed(0f); return; }

        // 입력
        Vector2 input = ReadMoveInput();

        // 락온 여부
        bool locked = playerLockOn && playerLockOn.IsLocked;

        // 기준 축: 비락온=카메라, 락온=자기 전/우(스트레이프)
        Vector3 fwd, right;
        if (locked)
        {
            fwd = Flat(playerRoot.forward);
            right = Flat(playerRoot.right);
        }
        else
        {
            var cam = cameraTransform ? cameraTransform : (Camera.main ? Camera.main.transform : playerRoot);
            fwd = Flat(cam.forward);
            right = Flat(cam.right);
        }

        // 목표 속도
        Vector3 wishDir = (right * input.x + fwd * input.y);
        if (wishDir.sqrMagnitude > 1e-6f) wishDir.Normalize();

        float targetSpeed = ComputeTargetSpeed(input, IsGuarding());
        Vector3 targetVel = wishDir * targetSpeed;

        // 가감속
        float dt = Time.deltaTime;
        _velXZ = MoveTowardsXZ(_velXZ, targetVel, accel, decel, dt);

        // 회전(비락온에서만 적용)
        if (!locked)
        {
            bool shouldRotate = !rotateOnlyWhenMoving || _velXZ.sqrMagnitude > 0.0004f;
            if (shouldRotate && _velXZ.sqrMagnitude > 0.000001f)
            {
                Quaternion t = Quaternion.LookRotation(_velXZ.normalized, Vector3.up);
                playerRoot.rotation = Quaternion.RotateTowards(playerRoot.rotation, t, rotationSpeed * dt);
            }
        }

        // 이동 + 중력
        MoveWithGravity(_velXZ);

        // 애니 속도
        SetAnimSpeed(Mathf.Clamp01(_velXZ.magnitude / Mathf.Max(0.01f, runSpeed)));
    }

    // ───────── 외부 훅(호환용) ─────────
    // 연출/패링/공격 등에서 이동 잠금
    public void SetExternalControl(bool locked)
    {
        _externLocked = locked;
        if (locked) { _velXZ = Vector3.zero; SetAnimSpeed(0f); }
    }

    // 회피 시작/종료 훅
    public void OnDodgeStart()
    {
        _externLocked = true;
        _velXZ = Vector3.zero;
        SetAnimSpeed(0f);
    }

    public void OnDodgeEnd()
    {
        _externLocked = false;
    }

    // ───────── 유틸 ─────────
    Vector2 ReadMoveInput()
    {
        if (moveAction?.action != null)
            return moveAction.action.ReadValue<Vector2>();

        if (!legacyFallback) return Vector2.zero;

        float x = Input.GetAxisRaw(axisX);
        float y = Input.GetAxisRaw(axisY);
        Vector2 v = new Vector2(x, y);
        return v.sqrMagnitude > 1f ? v.normalized : v;
    }

    float ComputeTargetSpeed(Vector2 input, bool guarding)
    {
        if (input.sqrMagnitude <= 1e-6f) return 0f;
        if (!guarding) return runSpeed;
        float f = input.y;
        if (f >  0.5f) return runSpeed * guardFwdMul;
        if (f < -0.5f) return runSpeed * guardBackMul;
        return runSpeed * guardStrafeMul;
    }

    bool IsGuarding()
    {
        if (guardController) return guardController.IsGuarding;
        return false;
    }

    void MoveWithGravity(Vector3 vXZ)
    {
        bool grounded = _cc.isGrounded;
        if (grounded && _velY < 0f) _velY = -groundSnap;
        _velY -= gravity * Time.deltaTime;

        _cc.Move(vXZ * Time.deltaTime + Vector3.up * _velY * Time.deltaTime);
    }

    static Vector3 Flat(Vector3 v){ v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    static Vector3 MoveTowardsXZ(Vector3 cur, Vector3 tgt, float acc, float dec, float dt)
    {
        Vector3 diff = tgt - cur;
        float maxDelta = ((tgt.magnitude >= cur.magnitude) ? acc : dec) * dt;
        return (diff.magnitude <= maxDelta) ? tgt : cur + diff.normalized * maxDelta;
    }

    void SetAnimSpeed(float spd01)
    {
        if (!animator || string.IsNullOrEmpty(p_Speed)) return;
        animator.SetFloat(p_Speed, spd01);
    }
}
