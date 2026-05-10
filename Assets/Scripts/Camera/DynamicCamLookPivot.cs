using UnityEngine;

[ExecuteAlways]
public class DynamicCamLookPivot : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Transform cameraReference;

    [Header("Composition")]
    public float forwardDistance = 1.15f;
    public float heightOffset = 1.35f;
    public float lateralOffset = 0f;

    [Header("Follow")]
    [Range(0f, 1f)] public float followLerp = 0.08f;
    [Min(0f)] public float followSmoothTime = 0.12f;
    [Min(0f)] public float stationarySmoothTime = 0.22f;
    [Min(0f)] public float recenterDeadZone = 0.18f;
    [Min(0f)] public float maxFollowSpeed = 32f;
    public bool useCameraPlanarBasis = true;

    [Header("Yaw")]
    public bool alignYawToTarget = true;
    [Range(0f, 1f)] public float yawLerp = 0.08f;

    Vector3 _followVelocity;
    CharacterController _controller;

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 forward = ResolveForward();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude <= 0.0001f)
            right = target.right;
        else
            right.Normalize();

        Vector3 desired = target.position
                        + forward * forwardDistance
                        + right * lateralOffset
                        + Vector3.up * heightOffset;

        if (!Application.isPlaying || followLerp <= 0f)
        {
            transform.position = desired;
            _followVelocity = Vector3.zero;
        }
        else
        {
            Vector3 delta = desired - transform.position;
            if (delta.sqrMagnitude <= recenterDeadZone * recenterDeadZone)
                desired = transform.position;

            float smoothTime = IsTargetMoving() ? followSmoothTime : stationarySmoothTime;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref _followVelocity,
                Mathf.Max(0.0001f, smoothTime),
                Mathf.Max(0.01f, maxFollowSpeed),
                Time.deltaTime);
        }

        if (!alignYawToTarget)
            return;

        Quaternion desiredRotation = Quaternion.LookRotation(forward, Vector3.up);
        if (!Application.isPlaying || yawLerp <= 0f)
            transform.rotation = desiredRotation;
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Pow(1f - yawLerp, Time.deltaTime * 60f));
    }

    Vector3 ResolveForward()
    {
        if (useCameraPlanarBasis)
        {
            if (cameraReference == null && Application.isPlaying && Camera.main != null)
                cameraReference = Camera.main.transform;

            if (cameraReference != null)
            {
                Vector3 cameraForward = cameraReference.forward;
                cameraForward.y = 0f;
                if (cameraForward.sqrMagnitude > 0.0001f)
                    return cameraForward.normalized;
            }
        }

        Vector3 targetForward = target.forward;
        targetForward.y = 0f;
        return targetForward.sqrMagnitude > 0.0001f ? targetForward.normalized : Vector3.forward;
    }

    bool IsTargetMoving()
    {
        if (_controller == null || (_controller.transform != target && !_controller.transform.IsChildOf(target)))
        {
            _controller = target.GetComponent<CharacterController>();
            if (_controller == null)
                _controller = target.GetComponentInParent<CharacterController>();
        }

        if (_controller == null)
            return true;

        Vector3 velocity = _controller.velocity;
        velocity.y = 0f;
        return velocity.sqrMagnitude > 0.04f;
    }
}
