using UnityEngine;

/// <summary>
/// 상호작용 공통 베이스 (로그 포함)
/// </summary>
[DisallowMultipleComponent]
public abstract class BaseInteractable : MonoBehaviour, IInteractable
{
    [Header("프롬프트 문구 (예: F: 상호작용)")]
    [TextArea] public string promptText = "F: 상호작용";

    [Header("외곽선 하이라이트 제어 컴포넌트 (선택)")]
    public InteractionOutlineRenderer highlight;

    [Header("프롬프트/위젯이 따라다닐 기준 Transform (비우면 자기 자신의 Transform)")]
    public Transform anchor;

    [Header("접근만으로 자동 실행할지. 문/상자 등은 false 권장")]
    public bool autoPickup = false;

    protected virtual void Reset()
    {
        if (!highlight) highlight = GetComponentInChildren<InteractionOutlineRenderer>();
        if (!anchor) anchor = transform;
    }

    public virtual string GetPromptText() => promptText;

    // 로그 추가: OnHoverStart
    public virtual void OnHoverStart()
    {
        Debug.Log($"[Hover] OnHoverStart: {name}");
        if (highlight) highlight.SetActive(true);
    }

    // 로그 추가: OnHoverEnd
    public virtual void OnHoverEnd()
    {
        Debug.Log($"[Hover] OnHoverEnd: {name}");
        if (highlight) highlight.SetActive(false);
    }

    public abstract bool TryInteract(object invoker = null);
}
