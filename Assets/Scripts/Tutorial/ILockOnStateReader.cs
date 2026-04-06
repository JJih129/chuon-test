using UnityEngine;

public interface ILockOnStateReader
{
    Transform CurrentTarget { get; }
    bool IsLockedOn { get; }
}
