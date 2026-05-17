using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class RuntimeUiInputUtility
{
    static readonly System.Collections.Generic.List<GraphicRaycaster> DisabledRaycasters = new System.Collections.Generic.List<GraphicRaycaster>(16);
    static Canvas activeModalCanvas;

    public static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                .GetComponent<EventSystem>();
            return;
        }

        eventSystem.enabled = true;

        BaseInputModule inputModule = eventSystem.GetComponent<BaseInputModule>();
        if (inputModule == null)
            inputModule = eventSystem.gameObject.AddComponent<StandaloneInputModule>();

        inputModule.enabled = true;
    }

    public static void ForceMenuCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public static void BeginModalInput(Canvas modalCanvas)
    {
        RestoreModalInput();

        if (modalCanvas == null)
            return;

        activeModalCanvas = modalCanvas;
        GraphicRaycaster[] raycasters = Object.FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < raycasters.Length; i++)
        {
            GraphicRaycaster raycaster = raycasters[i];
            if (raycaster == null || !raycaster.enabled)
                continue;

            if (raycaster.transform == modalCanvas.transform || raycaster.transform.IsChildOf(modalCanvas.transform))
                continue;

            DisabledRaycasters.Add(raycaster);
            raycaster.enabled = false;
        }
    }

    public static void RestoreModalInput()
    {
        for (int i = 0; i < DisabledRaycasters.Count; i++)
        {
            GraphicRaycaster raycaster = DisabledRaycasters[i];
            if (raycaster != null)
                raycaster.enabled = true;
        }

        DisabledRaycasters.Clear();
        activeModalCanvas = null;
    }

    public static bool IsModalCanvas(Canvas canvas)
    {
        return activeModalCanvas != null && activeModalCanvas == canvas;
    }
}
