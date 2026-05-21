using UnityEngine;

[DisallowMultipleComponent]
public class LockOnFacingDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerLockOn lockOn;
    [SerializeField] private Transform playerRoot;

    [Header("Facing")]
    [SerializeField, Range(180f, 3600f)] private float yawSpeedDeg = 1440f;
    [SerializeField, Range(0f, 180f)] private float snapAngleDeg = 75f;
    [SerializeField] private bool rotateOwnerRoot = true;
    [SerializeField] private bool autoUnlockWhenFar = true;
    [SerializeField, Min(0.1f)] private float maxDistance = 30f;

    float _maxDistanceSqr;

    void Reset()
    {
        ResolveReferences();
    }

    void Awake()
    {
        ResolveReferences();
        RefreshDistanceCache();
        RefreshTickState();
    }

    void LateUpdate()
    {
        if (lockOn == null || !lockOn.IsLocked || playerRoot == null)
        {
            RefreshTickState();
            return;
        }

        Transform target = lockOn.CurrentTarget;
        if (!target || !target.gameObject.activeInHierarchy)
        {
            lockOn.Unlock();
            return;
        }

        Vector3 origin = rotateOwnerRoot ? transform.position : playerRoot.position;
        if (autoUnlockWhenFar && (origin - target.position).sqrMagnitude > _maxDistanceSqr)
        {
            lockOn.Unlock();
            return;
        }

        Vector3 direction = target.position - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up);
        Quaternion current = rotateOwnerRoot ? transform.rotation : playerRoot.rotation;
        float angle = Quaternion.Angle(current, desired);
        float step = angle >= snapAngleDeg
            ? 3600f * Time.deltaTime
            : yawSpeedDeg * Time.deltaTime;
        ApplyFacingRotation(Quaternion.RotateTowards(current, desired, step));
    }

    void OnValidate()
    {
        RefreshDistanceCache();
    }

    public void ConfigureRuntime(PlayerLockOn sourceLockOn, Transform root)
    {
        lockOn = sourceLockOn != null ? sourceLockOn : lockOn;
        playerRoot = ResolveFacingRoot(root);
        ResolveReferences();
        RefreshDistanceCache();
        RefreshTickState();
    }

    public void FaceCurrentTargetImmediate()
    {
        if (lockOn == null || !lockOn.IsLocked || lockOn.CurrentTarget == null || playerRoot == null)
            return;

        Vector3 origin = rotateOwnerRoot ? transform.position : playerRoot.position;
        Vector3 direction = lockOn.CurrentTarget.position - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        ApplyFacingRotation(Quaternion.LookRotation(direction.normalized, Vector3.up));
    }

    public void RefreshTickState()
    {
        ResolveReferences();
        enabled = lockOn != null && lockOn.IsLocked && playerRoot != null;
    }

    void ResolveReferences()
    {
        if (lockOn == null)
            lockOn = GetComponent<PlayerLockOn>();

        if (playerRoot == null)
            playerRoot = ResolveFacingRoot(null);
    }

    void RefreshDistanceCache()
    {
        _maxDistanceSqr = maxDistance * maxDistance;
    }

    Transform ResolveFacingRoot(Transform fallback)
    {
        PlayerMoveController movement = GetComponent<PlayerMoveController>();
        if (movement != null && movement.FacingRoot != null)
            return movement.FacingRoot;

        if (fallback != null)
            return fallback;

        PlayerReferences references = GetComponent<PlayerReferences>();
        if (references != null && references.VisualRoot != null)
            return references.VisualRoot;

        return transform;
    }

    void ApplyFacingRotation(Quaternion rotation)
    {
        if (rotateOwnerRoot)
            transform.rotation = rotation;

        if (playerRoot != null)
            playerRoot.rotation = rotation;
    }
}
