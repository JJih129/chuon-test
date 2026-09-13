using UnityEngine;

[DisallowMultipleComponent]
public class PlayerLockOn : MonoBehaviour
{
    [Header("Target Search Range / FOV")]
    [SerializeField, Min(0f)] private float lockOnRange = 18f;
    [SerializeField, Range(10f, 180f)] private float lockOnFOV = 70f;
    [SerializeField] private LayerMask obstacleMask = 0;
    [SerializeField] private LayerMask enemyMask = 1 << 9;

    [Header("Pivot Creation / Tracking")]
    [SerializeField] private string pivotName = "LockPivot";
    [SerializeField] private float defaultPivotY = 1.3f;

    [Header("Input (Legacy)")]
    [SerializeField] private bool useLegacyInput = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private KeyCode toggleKeyAlt = KeyCode.Mouse2;

    [Header("Camera Link")]
    [SerializeField] private LockOnCameraManager cameraMgr;

    [Header("Auto Unlock Option")]
    [Tooltip("Automatically unlock when the current target gets disabled.")]
    [SerializeField] private bool autoUnlockWhenTargetDisabled = true;

    public Transform CurrentTarget { get; private set; }
    public bool IsLocked => CurrentTarget != null;
    public bool IsLockOn => IsLocked;
    public bool HasTarget => CurrentTarget != null;
    public Transform Target => CurrentTarget;
    public Vector3 TargetPosition => CurrentTarget ? CurrentTarget.position : transform.position;

    public Vector3 DirectionFrom(Vector3 origin)
        => (TargetPosition - origin).sqrMagnitude > 0.0001f
            ? (TargetPosition - origin).normalized
            : transform.forward;

    Transform _playerPivot;
    Transform _cam;
    readonly Collider[] _targetBuffer = new Collider[64];
    bool _lockModeActive;
    float _lockOnRangeSq;
    float _lockOnMinDot;

    void Awake()
    {
        CacheDerivedValues();
        _playerPivot = EnsurePivot(transform, pivotName, defaultPivotY);
        if (!cameraMgr) cameraMgr = FindAnyObjectByType<LockOnCameraManager>();
        cameraMgr?.SetPlayerPivot(_playerPivot);
        _cam = Camera.main ? Camera.main.transform : null;
    }

    void OnValidate()
    {
        CacheDerivedValues();
    }

    void Start()
    {
        if (!_cam && Camera.main) _cam = Camera.main.transform;
    }

    void Update()
    {
        if (_lockModeActive && (!CurrentTarget || !CurrentTarget.gameObject.activeInHierarchy))
        {
            _lockModeActive = false;
            cameraMgr?.EndLockOn();
        }

        if (useLegacyInput && (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt)))
        {
            if (IsLocked)
            {
                Unlock();
            }
            else
            {
                var enemyRoot = FindBestTarget();
                if (enemyRoot) LockTo(enemyRoot);
            }
        }
    }

    public void LockTo(Transform enemyRoot)
    {
        CurrentTarget = EnsurePivot(enemyRoot, pivotName, defaultPivotY);
        _lockModeActive = CurrentTarget;
        if (CurrentTarget)
            cameraMgr?.StartLockOn(CurrentTarget);
    }

    public void Unlock()
    {
        _lockModeActive = false;
        CurrentTarget = null;
        cameraMgr?.EndLockOn();
    }

    public bool TryAutoLock()
    {
        var best = FindBestTarget();
        if (!best) return false;
        LockTo(best);
        return true;
    }

    Transform FindBestTarget()
    {
        if (!_cam)
        {
            if (!Camera.main) return null;
            _cam = Camera.main.transform;
        }

        float bestScore = float.MaxValue;
        Transform bestRoot = null;
        int hitCount = Physics.OverlapSphereNonAlloc(
            _cam.position,
            lockOnRange,
            _targetBuffer,
            enemyMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; ++i)
        {
            var hit = _targetBuffer[i];
            if (hit == null) continue;

            var root = hit.transform.root;
            var pivot = EnsurePivot(root, pivotName, defaultPivotY);
            if (!pivot) continue;

            Vector3 to = pivot.position - _cam.position;
            float distSq = to.sqrMagnitude;
            if (distSq > _lockOnRangeSq) continue;

            float dist = distSq > 0.0001f ? Mathf.Sqrt(distSq) : 0f;
            float invDist = dist > 0.0001f ? 1f / dist : 0f;
            float dot = Vector3.Dot(_cam.forward, to * invDist);
            if (dot < _lockOnMinDot) continue;

            if (obstacleMask.value != 0 &&
                Physics.Linecast(_cam.position, pivot.position, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            float ang = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
            float score = ang * 2f + dist;
            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = root;
            }
        }

        for (int i = 0; i < hitCount; ++i)
            _targetBuffer[i] = null;

        return bestRoot;
    }

    Transform EnsurePivot(Transform root, string name, float defaultY)
    {
        if (!root) return null;

        var existing = root.Find(name);
        if (existing) return existing;

        var rend = root.GetComponentInChildren<Renderer>();
        Vector3 worldPos;
        if (rend)
        {
            float cy = rend.bounds.center.y;
            float ty = rend.bounds.max.y;
            float y = Mathf.Lerp(cy, ty, 0.6f) - 0.1f;
            worldPos = new Vector3(rend.bounds.center.x, y, rend.bounds.center.z);
        }
        else
        {
            worldPos = root.position + Vector3.up * defaultY;
        }

        var go = new GameObject(name);
        go.transform.SetParent(root, true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.identity;

        var follower = go.AddComponent<LockPivotFollower>();
        follower.sourceRenderer = rend;
        follower.yOffset = rend ? go.transform.position.y - rend.bounds.center.y : defaultY;

        return go.transform;
    }

    void CacheDerivedValues()
    {
        float range = Mathf.Max(0f, lockOnRange);
        _lockOnRangeSq = range * range;
        _lockOnMinDot = Mathf.Cos(Mathf.Clamp(lockOnFOV, 10f, 180f) * 0.5f * Mathf.Deg2Rad);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var cam = Camera.main ? Camera.main.transform : null;
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        if (cam) Gizmos.DrawWireSphere(cam.position, lockOnRange);

        if (cam)
        {
            Gizmos.color = new Color(0f, 0.8f, 1f, 0.25f);
            Vector3 forward = cam.forward;
            Quaternion left = Quaternion.AngleAxis(-lockOnFOV * 0.5f, Vector3.up);
            Quaternion right = Quaternion.AngleAxis(lockOnFOV * 0.5f, Vector3.up);
            Gizmos.DrawLine(cam.position, cam.position + (left * forward).normalized * lockOnRange);
            Gizmos.DrawLine(cam.position, cam.position + (right * forward).normalized * lockOnRange);
        }
    }
#endif
}
