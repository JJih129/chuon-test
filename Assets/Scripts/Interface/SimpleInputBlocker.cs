// SimpleInputBlocker.cs
using UnityEngine;

[DisallowMultipleComponent]
public class SimpleInputBlocker : MonoBehaviour, IInputBlocker
{
    int blockAllCount = 0;
    bool localBlocked = false;

    public bool IsBlocked => (blockAllCount > 0) || localBlocked;

    public void SetBlocked(bool blocked)
    {
        localBlocked = blocked;
    }

    public void BlockAll()
    {
        BlockAll(true);
    }

    public void BlockAll(bool block)
    {
        if (block) blockAllCount++;
        else blockAllCount = Mathf.Max(0, blockAllCount - 1);
    }
}
