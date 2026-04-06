using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TitleSceneReturnBannerController : MonoBehaviour
{
    [Header("Presentation")]
    [SerializeField] private string titleText = "기록 저장 완료";
    [SerializeField] private string bodyText = "전투 기록이 저장되었습니다.";
    [SerializeField] private Vector2 panelSize = new Vector2(420f, 82f);
    [SerializeField] private Vector2 panelAnchoredPosition = new Vector2(0f, -108f);
    [SerializeField] private Color panelColor = new Color(0.03f, 0.08f, 0.12f, 0.82f);
    [SerializeField] private Color accentColor = new Color(0.52f, 0.96f, 1f, 0.95f);
    [SerializeField] private Color textColor = new Color(0.92f, 0.98f, 1f, 0.98f);

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay = 0.18f;
    [SerializeField, Min(0.05f)] private float fadeInDuration = 0.22f;
    [SerializeField, Min(0.1f)] private float holdDuration = 1.8f;
    [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.28f;

    bool _sequenceStarted;
    CanvasGroup _canvasGroup;
    RectTransform _root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !string.Equals(activeScene.name, "TitleScene", System.StringComparison.Ordinal))
            return;

        if (!TutorialSceneTransitionState.ConsumeCreditsToTitle())
            return;

        TitleSceneReturnBannerController controller = FindObjectOfType<TitleSceneReturnBannerController>(true);
        if (controller == null)
        {
            GameObject runtimeRoot = new GameObject("TitleSceneReturnBannerRuntime");
            controller = runtimeRoot.AddComponent<TitleSceneReturnBannerController>();
        }

        controller.ConfigureRuntime();
    }

    public void ConfigureRuntime()
    {
        if (_sequenceStarted)
            return;

        RuntimeMenuSceneStateUtility.PrepareForMenuScene();
        EnsureVisuals();
        StartCoroutine(CoPlayBanner());
    }

    IEnumerator CoPlayBanner()
    {
        _sequenceStarted = true;
        if (_canvasGroup == null)
            yield break;

        _canvasGroup.alpha = 0f;
        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        float fadeIn = Mathf.Max(0.05f, fadeInDuration);
        float elapsed = 0f;
        while (elapsed < fadeIn)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeIn);
            yield return null;
        }

        _canvasGroup.alpha = 1f;

        if (holdDuration > 0f)
            yield return new WaitForSecondsRealtime(holdDuration);

        float fadeOut = Mathf.Max(0.05f, fadeOutDuration);
        elapsed = 0f;
        while (elapsed < fadeOut)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeOut);
            yield return null;
        }

        _canvasGroup.alpha = 0f;
    }

    void EnsureVisuals()
    {
        if (_canvasGroup != null)
            return;

        GameObject canvasObject = new GameObject("TitleReturnBannerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        _root = panelObject.GetComponent<RectTransform>();
        _root.anchorMin = new Vector2(0.5f, 1f);
        _root.anchorMax = new Vector2(0.5f, 1f);
        _root.pivot = new Vector2(0.5f, 1f);
        _root.anchoredPosition = panelAnchoredPosition;
        _root.sizeDelta = panelSize;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = panelColor;
        panelImage.raycastTarget = false;

        Image accentImage = CreateImage("Accent", _root, accentColor);
        RectTransform accentRect = accentImage.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 4f);

        Text titleLabel = CreateText("Title", _root, titleText, 22, FontStyle.Bold, accentColor);
        ConfigureTextRect(titleLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), panelSize.x - 28f, 28f);

        Text bodyLabel = CreateText("Body", _root, bodyText, 14, FontStyle.Normal, textColor);
        ConfigureTextRect(bodyLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), panelSize.x - 32f, 22f);
    }

    static Image CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(string objectName, Transform parent, string textValue, int fontSize, FontStyle fontStyle, Color color)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.font = RuntimeBuiltInFontUtility.GetDefaultFont();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.text = textValue;
        text.raycastTarget = false;
        return text;
    }

    static void ConfigureTextRect(RectTransform rectTransform, Vector2 anchor, Vector2 anchoredPosition, float width, float height)
    {
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(width, height);
    }
}
