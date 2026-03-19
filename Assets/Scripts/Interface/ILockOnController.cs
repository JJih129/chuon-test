using UnityEngine;

public interface ILockOnController
{
    Transform GetCurrentTarget();

    bool IsLockedOn();

    void GiveCameraControlToTimeline(bool give);
}
