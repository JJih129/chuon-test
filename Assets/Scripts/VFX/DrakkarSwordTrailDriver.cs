using Drakkar.GameUtils;
using UnityEngine;
using Drakkar;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class DrakkarSwordTrailDriver : MonoBehaviour
{
    static DrakkarUpdater s_runtimeUpdater;

    DrakkarTrail _trail;
    Transform _baseAnchor;
    Transform _tipAnchor;
    bool _active;
    bool _hasDirection;
    Vector3 _lastDirection = Vector3.forward;
    Vector3 _lastUp = Vector3.up;

    void Awake()
    {
        EnsureUpdater();
        _trail = GetComponent<DrakkarTrail>();
        if (_trail == null)
            _trail = gameObject.AddComponent<DrakkarTrail>();
        _trail.Init();
    }

    public void Configure(Transform baseAnchor, Transform tipAnchor, Material trailMaterial, int layer)
    {
        _baseAnchor = baseAnchor;
        _tipAnchor = tipAnchor;

        EnsureUpdater();

        if (_trail == null)
            _trail = GetComponent<DrakkarTrail>() ?? gameObject.AddComponent<DrakkarTrail>();

        _trail.TrailMaterial = trailMaterial;
        _trail.Layer = layer;
        _trail.Init();
        SyncPose(forceDirection: true);
    }

    public void Begin()
    {
        if (_trail == null || _baseAnchor == null || _tipAnchor == null)
            return;

        EnsureUpdater();
        if (DrakkarUpdater.instance == null || _trail.TrailMaterial == null)
            return;
        SyncPose(forceDirection: true);
        _active = true;
        _trail.Begin();
    }

    public void End()
    {
        _active = false;
        if (_trail != null && DrakkarUpdater.instance != null)
            _trail.End();
    }

    public void Clear()
    {
        _active = false;
        if (_trail != null && DrakkarUpdater.instance != null)
            _trail.Clear();
    }

    void LateUpdate()
    {
        if (!_active || _trail == null || _baseAnchor == null || _tipAnchor == null)
            return;

        SyncPose(forceDirection: false);
    }

    void SyncPose(bool forceDirection)
    {
        if (_baseAnchor == null || _tipAnchor == null || _trail == null)
            return;

        Vector3 basePosition = _baseAnchor.position;
        Vector3 tipPosition = _tipAnchor.position;
        Vector3 direction = tipPosition - basePosition;
        float length = direction.magnitude;
        if (length > 0.0001f)
        {
            _lastDirection = direction / length;
            _hasDirection = true;
        }
        else if (!_hasDirection)
        {
            _lastDirection = Vector3.forward;
        }

        Vector3 upHint = _tipAnchor.up;
        Vector3 projectedUp = Vector3.ProjectOnPlane(upHint, _lastDirection);
        if (projectedUp.sqrMagnitude <= 0.0001f)
        {
            projectedUp = Vector3.ProjectOnPlane(_tipAnchor.right, _lastDirection);
        }
        if (projectedUp.sqrMagnitude > 0.0001f)
        {
            _lastUp = projectedUp.normalized;
        }

        transform.position = basePosition;

        if (forceDirection || _hasDirection)
            transform.rotation = Quaternion.LookRotation(_lastDirection, _lastUp);

        _trail.Length = Mathf.Max(0.01f, length);
    }

    static void EnsureUpdater()
    {
        if (DrakkarUpdater.instance != null)
            return;

        if (s_runtimeUpdater != null)
        {
            if (DrakkarUpdater.instance != null)
                return;

            s_runtimeUpdater = null;
        }

        GameObject updaterObject = new GameObject("DrakkarUpdater_Runtime");
        Object.DontDestroyOnLoad(updaterObject);

        updaterObject.AddComponent<DrakkarUpdaterPre>();
        updaterObject.AddComponent<DrakkarUpdaterPost>();
        s_runtimeUpdater = updaterObject.AddComponent<DrakkarUpdater>();
    }
}
