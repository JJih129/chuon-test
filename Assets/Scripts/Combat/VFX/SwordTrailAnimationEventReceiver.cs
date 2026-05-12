using UnityEngine;

/// <summary>
/// Animation Event relay for sword trails.
/// Add these event names to attack clips:
/// AnimEvent_StartSwordTrail, AnimEvent_StopSwordTrail, AnimEvent_ClearSwordTrail.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordTrailAnimationEventReceiver : MonoBehaviour
{
    [Tooltip("Trail renderer controlled by animation events. Auto-assigned from children when empty.")]
    [SerializeField] SwordTrailMeshRenderer swordTrail;

    [Tooltip("If enabled, logs a warning when no SwordTrailMeshRenderer can be found.")]
    [SerializeField] bool warnIfMissing = true;

    bool warningLogged;

    void Reset()
    {
        AutoAssign();
    }

    void Awake()
    {
        AutoAssign();

        if (swordTrail == null && warnIfMissing && !warningLogged)
        {
            warningLogged = true;
            Debug.LogWarning("[SwordTrailAnimationEventReceiver] SwordTrailMeshRenderer was not found.", this);
        }
    }

    public void AnimEvent_StartSwordTrail()
    {
        if (swordTrail != null)
            swordTrail.StartTrail();
    }

    public void AnimEvent_StopSwordTrail()
    {
        if (swordTrail != null)
            swordTrail.StopTrail();
    }

    public void AnimEvent_ClearSwordTrail()
    {
        if (swordTrail != null)
            swordTrail.ClearTrail();
    }

    void AutoAssign()
    {
        if (swordTrail != null)
            return;

        swordTrail = GetComponentInChildren<SwordTrailMeshRenderer>(true);
        if (swordTrail == null)
            swordTrail = GetComponentInParent<SwordTrailMeshRenderer>();
    }
}
