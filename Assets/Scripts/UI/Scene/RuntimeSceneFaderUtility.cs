using UnityEngine;
using UnityEngine.UI;

public static class RuntimeSceneFaderUtility
{
    const string RuntimeFaderRootName = "RuntimeSceneFader";
    const string RuntimeOverlayName = "RuntimeFadeOverlay";

    public static SceneFader EnsureSceneFader()
    {
        if (SceneFader.Instance != null)
        {
            EnsureFadeCanvasGroup(SceneFader.Instance);
            return SceneFader.Instance;
        }

        GameObject rootObject = new GameObject(
            RuntimeFaderRootName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(SceneFader));

        Canvas canvas = rootObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = rootObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        SceneFader sceneFader = rootObject.GetComponent<SceneFader>();
        EnsureFadeCanvasGroup(sceneFader);
        return sceneFader;
    }

    static void EnsureFadeCanvasGroup(SceneFader sceneFader)
    {
        if (sceneFader == null)
            return;

        if (sceneFader.fadeCanvasGroup != null)
            return;

        Transform parent = sceneFader.transform;
        Transform existingOverlay = parent.Find(RuntimeOverlayName);
        RectTransform overlayRect;
        CanvasGroup canvasGroup;
        Image image;

        if (existingOverlay != null)
        {
            overlayRect = existingOverlay as RectTransform;
            canvasGroup = existingOverlay.GetComponent<CanvasGroup>();
            image = existingOverlay.GetComponent<Image>();
        }
        else
        {
            GameObject overlayObject = new GameObject(
                RuntimeOverlayName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            overlayObject.transform.SetParent(parent, false);
            overlayRect = overlayObject.GetComponent<RectTransform>();
            canvasGroup = overlayObject.GetComponent<CanvasGroup>();
            image = overlayObject.GetComponent<Image>();
        }

        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayRect.localScale = Vector3.one;

        if (image != null)
        {
            image.color = Color.black;
            image.raycastTarget = false;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        sceneFader.fadeCanvasGroup = canvasGroup;
    }
}
