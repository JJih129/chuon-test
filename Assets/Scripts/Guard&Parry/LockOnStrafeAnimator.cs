using UnityEngine;

[DisallowMultipleComponent]
public class LockOnStrafeAnimator : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Animator animator;
    [SerializeField] PlayerReferences playerReferences;
    [SerializeField] PlayerMoveController moveController;
    [SerializeField] PlayerLockOn playerLockOn;
    [SerializeField] PlayerGuardController guardController;
    [SerializeField] Transform movementBasis;
    [SerializeField] CharacterController characterController;
    [SerializeField] Rigidbody rigidbodyRef;
    [SerializeField] Transform velocitySource;

    [Header("Animator Params")]
    [SerializeField] string p_IsLockOn = "IsLockOn";
    [SerializeField] string p_IsLockOnMoving = "IsLockOnMoving";
    [SerializeField] string p_LockOnSpeed = "LockOnSpeed";
    [SerializeField] string p_MoveX = "LockOnMoveX";
    [SerializeField] string p_MoveY = "LockOnMoveY";

    [Header("Tuning")]
    [SerializeField, Range(0.5f, 10f)] float normalizeSpeed = 4f;
    [SerializeField, Range(0f, 0.3f)] float deadZone = 0.12f;
    [SerializeField, Range(0f, 0.5f)] float dampTime = 0.08f;
    [SerializeField, Range(0f, 0.3f)] float idleSnap = 0.06f;
    [SerializeField, Range(0f, 0.5f)] float moveStateThreshold = 0.1f;
    [SerializeField] bool write2DBlend = true;

    int hIsLockOn;
    int hIsLockOnMoving;
    int hSpeed;
    int hX;
    int hY;

    Vector3 _prevPos;
    bool _hasPrev;
    bool _prevActive;
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
        if (!moveController) moveController = GetComponent<PlayerMoveController>();
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
        if (!guardController) guardController = GetComponent<PlayerGuardController>();
        if (!movementBasis) movementBasis = playerReferences != null && playerReferences.PlayerRoot != null
            ? playerReferences.PlayerRoot
            : transform;
        if (!velocitySource) velocitySource = movementBasis;
        if (!characterController) characterController = GetComponent<CharacterController>();
        if (!rigidbodyRef) rigidbodyRef = GetComponent<Rigidbody>();

        hIsLockOn = Animator.StringToHash(p_IsLockOn);
        hIsLockOnMoving = Animator.StringToHash(p_IsLockOnMoving);
        hSpeed = Animator.StringToHash(p_LockOnSpeed);
        hX = Animator.StringToHash(p_MoveX);
        hY = Animator.StringToHash(p_MoveY);
    }

    void Update()
    {
        if (!animator)
            return;

        bool guardActive = guardController != null && guardController.IsGuardMovementActive;
        bool active = playerLockOn != null && playerLockOn.IsLocked && !guardActive;

        if (hIsLockOn != 0)
            animator.SetBool(hIsLockOn, active);

        if (hIsLockOnMoving != 0 && !active)
            animator.SetBool(hIsLockOnMoving, false);

        if (!active && !_prevActive)
        {
            ApplyAnimatorValues(0f, 0f, 0f);
            return;
        }

        Vector3 velocity = HasMoveInput() ? GetWorldVelocity() : Vector3.zero;
        float speed = velocity.magnitude;

        Transform basis = movementBasis != null ? movementBasis : transform;
        Vector3 fwd = basis.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        else fwd.Normalize();

        Vector3 right = basis.right;
        right.y = 0f;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
        else right.Normalize();

        float nx = Mathf.Clamp(Vector3.Dot(velocity, right) / Mathf.Max(0.01f, normalizeSpeed), -1f, 1f);
        float ny = Mathf.Clamp(Vector3.Dot(velocity, fwd) / Mathf.Max(0.01f, normalizeSpeed), -1f, 1f);

        nx = Mathf.Abs(nx) < deadZone ? 0f : Mathf.Round(nx * 1000f) * 0.001f;
        ny = Mathf.Abs(ny) < deadZone ? 0f : Mathf.Round(ny * 1000f) * 0.001f;

        float s1d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny));
        if (s1d < idleSnap)
        {
            nx = 0f;
            ny = 0f;
            s1d = 0f;
        }

        if (hIsLockOnMoving != 0)
            animator.SetBool(hIsLockOnMoving, active && s1d > moveStateThreshold);

        ApplyAnimatorValues(active ? s1d : 0f, active ? nx : 0f, active ? ny : 0f);
        _prevActive = active;
    }

    void ApplyAnimatorValues(float speedValue, float xValue, float yValue)
    {
        if (hSpeed != 0 && ShouldWriteAnimatorValue(hSpeed, _lastSpeedValue, speedValue))
        {
            animator.SetFloat(hSpeed, speedValue, dampTime, Time.deltaTime);
            _lastSpeedValue = speedValue;
        }

        if (!write2DBlend)
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
        if (moveController != null)
            return moveController.CurrentPlanarVelocity;

        if (characterController != null)
            return characterController.velocity;

        if (rigidbodyRef != null)
            return rigidbodyRef.velocity;

        Vector3 position = velocitySource != null ? velocitySource.position : transform.position;
        Vector3 velocity = Vector3.zero;
        if (_hasPrev)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            velocity = (position - _prevPos) / dt;
        }

        _prevPos = position;
        _hasPrev = true;
        velocity.y = 0f;
        return velocity;
    }

    bool HasMoveInput()
    {
        if (moveController == null)
            return true;

        return moveController.CurrentMoveInput.sqrMagnitude > deadZone * deadZone;
    }
}
