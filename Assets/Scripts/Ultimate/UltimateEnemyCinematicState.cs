using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateEnemyCinematicState : MonoBehaviour
{
    [SerializeField] private BossController bossController;
    [SerializeField] private bool debugLog;

    UltimateTargetBinder.BoundTarget _target;
    bool _sessionActive;
    bool _disabledBossController;

    public void EnterCinematicState(UltimateTargetBinder.BoundTarget target)
    {
        _target = target;
        _sessionActive = true;

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

        if (debugLog)
            Debug.Log("[UltimateEnemyCinematicState] Restored target state.", this);

        _disabledBossController = false;
        _target = null;
        _sessionActive = false;
    }
}
