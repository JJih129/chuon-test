using UnityEngine;

[DisallowMultipleComponent]
public class GuardLocomotionAnimator : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("① 애니메이터/기준")]
    [SerializeField] private Animator animator;            // [조절값] 제어 대상
    [SerializeField] private Transform movementBasis;      // [조절값] 전후좌우 분해 기준(보통 플레이어 루트)

    [Header("① 속도 소스(있으면 우선)")]
    [SerializeField] private CharacterController characterController; // [조절값]
    [SerializeField] private Rigidbody rigidbodyRef;                  // [조절값]
    [SerializeField] private Transform velocitySource;                // [조절값] 차분용 Transform

    [Header("② 파라미터 이름")]
    [SerializeField] private string p_IsGuarding = "IsGuarding"; // [조절값]
    [SerializeField] private string p_GuardSpeed = "GuardSpeed"; // [조절값]
    [SerializeField] private string p_MoveX = "GuardMoveX";      // [조절값]
    [SerializeField] private string p_MoveY = "GuardMoveY";      // [조절값]

    [Header("③ 수치 조절")]
    [SerializeField, Range(0.5f,10f)] private float normalizeSpeed = 4f; // [조절값] 1.0 스케일 기준 속도
    [SerializeField, Range(0f,0.3f)] private float deadZone = 0.15f;     // [조절값] 데드존
    [SerializeField, Range(0f,0.5f)] private float dampTime = 0.1f;      // [조절값] 감쇠
    [SerializeField] private bool use2DBlend = true;                     // [조절값] 2D 사용

    [Header("③ 카메라 기준 스트레이프")]
    [SerializeField] private bool useCameraAsBasis = true;               // [조절값]
    [SerializeField] private Transform cameraTransform;                  // [조절값]

    [Header("④ 입력 폴백(정지 속도일 때도 애니 가동)")]
    [SerializeField] private bool useLegacyInputFallback = true;         // [조절값]
    [SerializeField] private string axisX = "Horizontal";                // [조절값]
    [SerializeField] private string axisY = "Vertical";                  // [조절값]
    [SerializeField, Range(0f,1f)] private float minSpeedToUseFallback = 0.05f; // [조절값]
    [SerializeField, Range(0.2f,2f)] private float inputFallbackScale = 1f;     // [조절값]

    int hIsGuard, hSpd, hX, hY;
    Vector3 prevPos; bool hasPrev;
    bool prevGuarding;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!movementBasis) movementBasis = transform;
        if (!velocitySource) velocitySource = movementBasis;

        hIsGuard = Animator.StringToHash(p_IsGuarding);
        hSpd     = Animator.StringToHash(p_GuardSpeed);
        hX       = Animator.StringToHash(p_MoveX);
        hY       = Animator.StringToHash(p_MoveY);
    }

    void Update()
    {
        if (!animator) return;

        bool isGuard = hIsGuard != 0 && animator.GetBool(hIsGuard);

        Vector3 v = GetWorldVelocity();
        float spd = v.magnitude;

        Transform basis = (useCameraAsBasis && cameraTransform) ? cameraTransform : movementBasis;
        Vector3 fwd = basis.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 rgt = basis.right;  rgt.y = 0f;  rgt.Normalize();

        float nx = Mathf.Clamp(Vector3.Dot(v, rgt) / Mathf.Max(0.01f, normalizeSpeed), -1f, 1f);
        float ny = Mathf.Clamp(Vector3.Dot(v, fwd) / Mathf.Max(0.01f, normalizeSpeed), -1f, 1f);

        if (useLegacyInputFallback && spd < minSpeedToUseFallback)
        {
            float ix = Input.GetAxisRaw(axisX);
            float iy = Input.GetAxisRaw(axisY);
            if (new Vector2(ix, iy).sqrMagnitude > deadZone * deadZone)
            {
                nx = Mathf.Clamp(ix * inputFallbackScale, -1f, 1f);
                ny = Mathf.Clamp(iy * inputFallbackScale, -1f, 1f);
            }
        }

        nx = Mathf.Abs(nx) < deadZone ? 0f : Mathf.Round(nx * 1000f) * 0.001f;
        ny = Mathf.Abs(ny) < deadZone ? 0f : Mathf.Round(ny * 1000f) * 0.001f;

        if (!prevGuarding && isGuard) { nx = 0f; ny = 0f; }

        float s1d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny));
        float tx = isGuard ? nx : 0f;
        float ty = isGuard ? ny : 0f;
        float ts = isGuard ? s1d : 0f;

        if (hSpd != 0) animator.SetFloat(hSpd, ts, dampTime, Time.deltaTime);
        if (use2DBlend)
        {
            if (hX != 0) animator.SetFloat(hX, tx, dampTime, Time.deltaTime);
            if (hY != 0) animator.SetFloat(hY, ty, dampTime, Time.deltaTime);
        }

        prevGuarding = isGuard;
    }

    Vector3 GetWorldVelocity()
    {
        if (characterController) return characterController.velocity;
        if (rigidbodyRef) return rigidbodyRef.velocity;

        Vector3 p = velocitySource ? velocitySource.position : transform.position;
        Vector3 vel = Vector3.zero;
        if (hasPrev) vel = (p - prevPos) / Mathf.Max(Time.deltaTime, 1e-5f);
        prevPos = p; hasPrev = true;
        vel.y = 0f;
        return vel;
    }
}
