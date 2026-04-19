using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateEnemyCinematicState : MonoBehaviour
{
    [SerializeField] private BossController bossController;
    [SerializeField] private bool debugLog;

    UltimateTargetBinder.BoundTarget _target;
    bool _sessionActive;
    bool _disabledBossController;
    bool _cachedTargetPoseValid;
    Vector3 _cachedTargetPosition;
    Quaternion _cachedTargetRotation = Quaternion.identity;

    public void EnterCinematicState(UltimateTargetBinder.BoundTarget target)
    {
        _target = target;
        _sessionActive = true;
        _cachedTargetPoseValid = false;

        if (_target != null && _target.TargetRoot != null)
        {
            _cachedTargetPosition = _target.TargetRoot.position;
            _cachedTargetRotation = _target.TargetRoot.rotation;
            _cachedTargetPoseValid = true;
        }

        if ((_target == null || _target.VictimState == null) && bossController != null && bossController.enabled)
        {
            bossController.enabled = false;
            _disabledBossController = true;
        }
    }

    public void RefreshAnchor(Vector3 worldPosition, Vector3 lookTarget)
    {
        if (!_sessionActive || _target == null)
            return;

        if (_target.VictimState != null)
        {
            _target.VictimState.SetUltimateVictimAnchor(worldPosition, lookTarget);
            return;
        }

        if (_target.TargetRoot == null)
            return;

        Vector3 flat = lookTarget - worldPosition;
        flat.y = 0f;
        Quaternion rotation = flat.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flat.normalized, Vector3.up)
            : _target.TargetRoot.rotation;
        _target.TargetRoot.SetPositionAndRotation(worldPosition, rotation);
    }

    public void CleanupIfNeeded()
    {
        if (!_sessionActive)
            return;

        if (_disabledBossController && bossController != null)
            bossController.enabled = true;

        if (_target != null && _target.VictimState != null)
            _target.VictimState.EndUltimateVictimState();

        if (_cachedTargetPoseValid && _target != null && _target.TargetRoot != null)
            _target.TargetRoot.SetPositionAndRotation(_cachedTargetPosition, _cachedTargetRotation);

        _disabledBossController = false;
        _cachedTargetPoseValid = false;
        _target = null;
        _sessionActive = false;
    }
}
