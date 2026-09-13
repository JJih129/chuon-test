using UnityEngine;

[DisallowMultipleComponent]
public abstract class BaseInteractable : MonoBehaviour, IInteractable
{
    [Header("Prompt")]
    [TextArea] public string promptText = "F: 상호작용";

    [Header("Highlight")]
    public InteractionOutlineRenderer highlight;

    [Header("Anchor")]
    public Transform anchor;

    [Header("Auto Pickup")]
    public bool autoPickup = false;

    [Header("Debug")]
    public bool debugHoverLogs = false;

    protected virtual void Reset()
    {
        if (!highlight) highlight = GetComponentInChildren<InteractionOutlineRenderer>();
        if (!anchor) anchor = transform;
    }

    public virtual string GetPromptText() => promptText;

    public virtual void OnHoverStart()
    {
        if (debugHoverLogs) Debug.Log($"[Hover] OnHoverStart: {name}");
        if (highlight) highlight.SetActive(true);
    }

    public virtual void OnHoverEnd()
    {
        if (debugHoverLogs) Debug.Log($"[Hover] OnHoverEnd: {name}");
        if (highlight) highlight.SetActive(false);
    }

    public abstract bool TryInteract(object invoker = null);
}
