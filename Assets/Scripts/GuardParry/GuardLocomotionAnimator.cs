using UnityEngine;

[DisallowMultipleComponent]
public class GuardLocomotionAnimator : MonoBehaviour
{
    [Header("① 애니메이터/기준")]
    [SerializeField] private Animator animator;            
    [SerializeField] private PlayerReferences playerReferences;
    [SerializeField] private Transform movementBasis;      

    [Header("① 속도 소스(있으면 우선)")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Rigidbody rigidbodyRef;
    [SerializeField] private Transform velocitySource;
    [SerializeField] private PlayerMoveController moveController;

    [Header("② 파라미터 이름")]
    [SerializeField] private string p_IsGuarding = "IsGuarding";
    [SerializeField] private string p_GuardSpeed = "GuardSpeed";
    [SerializeField] private string p_MoveX = "GuardMoveX";
    [SerializeField] private string p_MoveY = "GuardMoveY";

    [Header("③ 수치 조절")]
    [SerializeField, Range(0.5f,10f)] private float normalizeSpeed = 4f;
    [SerializeField, Range(0f,0.3f)] private float deadZone = 0.15f;
    [SerializeField, Range(0f,0.5f)] private float dampTime = 0.1f;
    [SerializeField] private bool use2DBlend = true;

    [Header("③ 카메라 기준 스트레이프")]
    [SerializeField] private bool useCameraAsBasis = true;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private bool autoAssignMainCamera = true; // [추가]

    [Header("④ 입력 폴백(정지 속도일 때도 애니 가동)")]
    [SerializeField] private bool useLegacyInputFallback = false; // [기본값 Off 권장]
    [SerializeField] private string axisX = "Horizontal";
    [SerializeField] private string axisY = "Vertical";
    [SerializeField, Range(0f,1f)] private float minSpeedToUseFallback = 0.05f;
    [SerializeField, Range(0.2f,2f)] private float inputFallbackScale = 1f;

    [Header("⑤ 드리프트 스냅")]
    [Tooltip("이 값 미만의 가드 속도는 0으로 스냅하여 Idle을 보장")]
    [SerializeField, Range(0f,0.3f)] private float guardIdleSnap = 0.08f; // [추가]

    int hIsGuard, hSpd, hX, hY;
    Vector3 prevPos; bool hasPrev;
    bool prevGuarding;
    float _lastSpeedValue = float.NaN;
    float _lastXValue = float.NaN;
    float _lastYValue = float.NaN;

    const float AnimatorWriteEpsilon = 0.0025f;

    void Awake()
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!animator) animator = playerReferences != null && playerReferences.MainAnimator != null
            ? playerReferences.MainAnimator
            : GetComponentInChildren<Animator>();
        if (!movementBasis) movementBasis = transform;
        if (!velocitySource) velocitySource = movementBasis;
        if (!moveController) moveController = GetComponent<PlayerMoveController>();

        if (useCameraAsBasis && !cameraTransform && autoAssignMainCamera && Camera.main)
            cameraTransform = Camera.main.transform; // [추가]

        hIsGuard = Animator.StringToHash(p_IsGuarding);
        hSpd     = Animator.StringToHash(p_GuardSpeed);
        hX       = Animator.StringToHash(p_MoveX);
        hY       = Animator.StringToHash(p_MoveY);
    }

    void Update()
    {
        if (!animator) return;

        bool isGuard = hIsGuard != 0 && animator.GetBool(hIsGuard);
        if (!isGuard && !prevGuarding)
        {
            ApplyAnimatorValues(0f, 0f, 0f);
            return;
        }

        Vector3 v = GetWorldVelocity();
        bool hasMoveInput = HasMoveInput();
        if (!hasMoveInput)
            v = Vector3.zero;
        float spd = v.magnitude;

        Transform basis = (useCameraAsBasis && cameraTransform) ? cameraTransform : movementBasis;
        Vector3 fwd = basis.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; else fwd.Normalize();
        Vector3 rgt = basis.right;  rgt.y = 0f; if (rgt.sqrMagnitude < 1e-6f) rgt = Vector3.right;   else rgt.Normalize();

        float nx = Mathf.Clamp(Vector3.Dot(v, rgt) / Mathf.Max(0.01f, normalizeSpeed), -1f, 1f);
        float ny = Mathf.Clamp(Vector3.Dot(v, fwd) / Mathf.Max(0.01f, normalizeSpeed), -1f, 1f);

        if (useLegacyInputFallback && spd < minSpeedToUseFallback)
        {
            float ix = Input.GetAxisRaw(axisX);
            float iy = Input.GetAxisRaw(axisY);
            Vector2 iv = new Vector2(ix, iy);
            if (iv.sqrMagnitude > deadZone * deadZone)
            {
                nx = Mathf.Clamp(ix * inputFallbackScale, -1f, 1f);
                ny = Mathf.Clamp(iy * inputFallbackScale, -1f, 1f);
            }
        }

        nx = Mathf.Abs(nx) < deadZone ? 0f : Mathf.Round(nx * 1000f) * 0.001f;
        ny = Mathf.Abs(ny) < deadZone ? 0f : Mathf.Round(ny * 1000f) * 0.001f;

        if (!prevGuarding && isGuard) { nx = 0f; ny = 0f; }

        float s1d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny));
        if (s1d < guardIdleSnap) { nx = 0f; ny = 0f; s1d = 0f; } // [추가]

        float tx = isGuard ? nx : 0f;
        float ty = isGuard ? ny : 0f;
        float ts = isGuard ? s1d : 0f;

        ApplyAnimatorValues(ts, tx, ty);

        prevGuarding = isGuard;
    }

    void ApplyAnimatorValues(float speedValue, float xValue, float yValue)
    {
        if (hSpd != 0 && ShouldWriteAnimatorValue(hSpd, _lastSpeedValue, speedValue))
        {
            animator.SetFloat(hSpd, speedValue, dampTime, Time.deltaTime);
            _lastSpeedValue = speedValue;
        }

        if (!use2DBlend)
            return;

        if (hX != 0 && ShouldWriteAnimatorValue(hX, _lastXValue, xValue))
        {
            animator.SetFloat(hX, xValue, dampTime, Time.deltaTime);
            _lastXValue = xValue;
        }

        if (hY != 0 && ShouldWriteAnimatorValue(hY, _lastYValue, yValue))
        {
            animator.SetFloat(hY, yValue, dampTime, Time.deltaTime);
            _lastYValue = yValue;
        }
    }

    bool ShouldWriteAnimatorValue(int hash, float cached, float next)
    {
        if (float.IsNaN(cached) || Mathf.Abs(cached - next) > AnimatorWriteEpsilon)
            return true;

        return animator != null && Mathf.Abs(animator.GetFloat(hash) - next) > AnimatorWriteEpsilon;
    }

    Vector3 GetWorldVelocity()
    {
        if (characterController) return characterController.velocity;
        if (rigidbodyRef) return rigidbodyRef.velocity;

        Vector3 p = velocitySource ? velocitySource.position : transform.position;
        Vector3 vel = Vector3.zero;
        if (hasPrev)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            vel = (p - prevPos) / dt;
        }
        prevPos = p; hasPrev = true;
        vel.y = 0f;
        return vel;
    }

    bool HasMoveInput()
    {
        if (moveController != null)
            return moveController.CurrentMoveInput.sqrMagnitude > deadZone * deadZone;

        if (!useLegacyInputFallback)
            return true;

        Vector2 input = new Vector2(Input.GetAxisRaw(axisX), Input.GetAxisRaw(axisY));
        return input.sqrMagnitude > deadZone * deadZone;
    }
}
