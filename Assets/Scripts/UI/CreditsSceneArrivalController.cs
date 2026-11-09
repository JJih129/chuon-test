using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CreditsSceneArrivalController : MonoBehaviour
{
    [SerializeField] private string titleSceneName = "TitleScene";

    [Header("Preview")]
    [SerializeField] private bool enableEditorDirectPreview = true;
    [SerializeField] private string previewBadgeText = "DIRECT PREVIEW";

    [Header("Presentation")]
    [SerializeField] private string titleText = "MISSION CLEAR";
    [SerializeField] private string subtitleText = "\uc2e4\uc804 \uac80\uc99d \uc885\ub8cc";
    [SerializeField] private string bodyText = "EGO \ub9c1\ud06c \uc548\uc815\ud654 \uc644\ub8cc";
    [SerializeField] private string hintText = "\uc544\ubb34 \ud0a4\ub098 \ub20c\ub7ec \ud0c0\uc774\ud2c0\ub85c";
    [SerializeField] private string creditsResourcePath = "UI/Title/TitleCredits_Default";
    [SerializeField] private Vector2 panelSize = new Vector2(640f, 760f);
    [SerializeField] private Vector2 panelAnchoredPosition = new Vector2(-470f, 0f);
    [SerializeField] private Color panelColor = new Color(0.015f, 0.015f, 0.018f, 0.86f);
    [SerializeField] private Color accentColor = new Color(0.92f, 0.12f, 0.09f, 0.98f);
    [SerializeField] private Color titleColor = new Color(0.96f, 0.96f, 0.97f, 0.98f);
    [SerializeField] private Color bodyColor = new Color(0.78f, 0.78f, 0.80f, 0.96f);
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] private Color sweepColor = new Color(1f, 1f, 1f, 0.10f);

    [Header("Timing")]
    [SerializeField, Min(0f)] private float startDelay = 0.35f;
    [SerializeField, Min(0.05f)] private float fadeInDuration = 0.26f;
    [SerializeField, Min(0.05f)] private float hintFadeDelay = 0.38f;
    [SerializeField, Min(0.05f)] private float inputGateDelay = 0.85f;
    [SerializeField, Min(0.05f)] private float returnFadeDuration = 0.22f;
    [SerializeField, Min(0f)] private float autoReturnDelay = 18f;
    [SerializeField, Min(0.1f)] private float countdownRefreshInterval = 0.2f;
    [SerializeField, Min(0.1f)] private float sweepDuration = 0.46f;
    [SerializeField, Min(24f)] private float sweepWidth = 118f;

    [Header("Input")]
    [SerializeField] private KeyCode fallbackReturnKey = KeyCode.Space;

    bool _fromMainClear;
    bool _isDirectPreview;
    bool _sequenceStarted;
    bool _allowReturnInput;
    bool _returnTriggered;
    Canvas _canvas;
    CanvasGroup _canvasGroup;
    RectTransform _root;
    Image _backdropImage;
    Image _sweepImage;
    Text _titleLabel;
    Text _subtitleLabel;
    Text _bodyLabel;
    Text _hintLabel;
    Text _previewLabel;
    Text _creditsLabel;
    TitleCreditsData _creditsData;
    Coroutine _autoReturnRoutine;

    // AfterSceneLoad only fires for the first scene of a session, so a LoadScene("CreditsScene")
    // from MainScene never reached Bootstrap. Hook sceneLoaded so every entry path configures the scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            Bootstrap();
    }

    static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !string.Equals(activeScene.name, "CreditsScene", System.StringComparison.Ordinal))
            return;

        CreditsSceneArrivalController controller = FindObjectOfType<CreditsSceneArrivalController>(true);
        if (controller != null && controller._sequenceStarted)
            return;
        if (controller == null)
        {
            GameObject runtimeRoot = new GameObject("CreditsSceneArrivalRuntime");
            controller = runtimeRoot.AddComponent<CreditsSceneArrivalController>();
        }

        controller.ConfigureRuntime(TutorialSceneTransitionState.ConsumeMainClearExit());
    }

    public void ConfigureRuntime(bool fromMainClear)
    {
        _fromMainClear = fromMainClear;
        // Direct entry (dev hotkey / editor) now shows the credits roll instead of an empty scene.
        _isDirectPreview = !_fromMainClear;
        if (_sequenceStarted)
            return;

        RuntimeMenuSceneStateUtility.PrepareForMenuScene();
        EnsureVisuals();
        ApplyRuntimeLabelState();
        StartCoroutine(CoPlayArrivalSequence());
    }

    void OnDisable()
    {
        StopAutoReturnCountdown();
    }

    void OnDestroy()
    {
        StopAutoReturnCountdown();
    }

    void Update()
    {
        if (!_allowReturnInput || _returnTriggered)
            return;

        if (!Input.anyKeyDown && !Input.GetKeyDown(fallbackReturnKey))
            return;

        _returnTriggered = true;
        StartCoroutine(CoReturnToTitle());
    }

    IEnumerator CoPlayArrivalSequence()
    {
        _sequenceStarted = true;
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();
        EnsureVisuals();
        if (_canvasGroup == null)
            yield break;

        _canvasGroup.alpha = 0f;
        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        float fadeDuration = Mathf.Max(0.05f, fadeInDuration);
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = 1f;

        if (_sweepImage != null)
            yield return CoPlayAccentSweep();

        if (_hintLabel != null)
            _hintLabel.canvasRenderer.SetAlpha(0f);

        if (hintFadeDelay > 0f)
            yield return new WaitForSecondsRealtime(hintFadeDelay);

        if (_hintLabel != null)
            _hintLabel.CrossFadeAlpha(1f, 0.24f, true);

        if (inputGateDelay > 0f)
            yield return new WaitForSecondsRealtime(inputGateDelay);

        _allowReturnInput = true;
        if (!_isDirectPreview)
            StartAutoReturnCountdown();
    }

    IEnumerator CoReturnToTitle()
    {
        _allowReturnInput = false;
        StopAutoReturnCountdown();
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();

        if (_canvasGroup != null)
        {
            float fadeDuration = Mathf.Max(0.05f, returnFadeDuration);
            float elapsed = 0f;
            float startAlpha = _canvasGroup.alpha;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }

            _canvasGroup.alpha = 0f;
        }

        TutorialSceneTransitionState.MarkCreditsToTitle();
        SceneFader sceneFader = RuntimeSceneFaderUtility.EnsureSceneFader();
        if (sceneFader != null)
            sceneFader.FadeOutAndLoadScene(titleSceneName);
        else
            SceneManager.LoadScene(titleSceneName);
    }

    void StartAutoReturnCountdown()
    {
        if (_isDirectPreview || autoReturnDelay <= 0f || _returnTriggered)
            return;

        StopAutoReturnCountdown();
        _autoReturnRoutine = StartCoroutine(CoAutoReturnCountdown());
    }

    void StopAutoReturnCountdown()
    {
        if (_autoReturnRoutine == null)
            return;

        StopCoroutine(_autoReturnRoutine);
        _autoReturnRoutine = null;
    }

    IEnumerator CoAutoReturnCountdown()
    {
        float endTime = Time.unscaledTime + autoReturnDelay;
        float refreshInterval = Mathf.Max(0.1f, countdownRefreshInterval);

        while (!_returnTriggered)
        {
            float remain = endTime - Time.unscaledTime;
            if (remain <= 0f)
                break;

            UpdateHintLabel(Mathf.CeilToInt(remain));
            yield return new WaitForSecondsRealtime(refreshInterval);
        }

        if (_returnTriggered)
            yield break;

        _returnTriggered = true;
        StartCoroutine(CoReturnToTitle());
    }

    void EnsureVisuals()
    {
        if (_canvas != null)
            return;

        GameObject canvasObject = new GameObject("CreditsArrivalCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 400;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        _backdropImage = CreateImage("Backdrop", canvasObject.transform, backdropColor);
        RectTransform backdropRect = _backdropImage.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        backdropRect.localScale = Vector3.one;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        _root = panelObject.GetComponent<RectTransform>();
        _root.anchorMin = new Vector2(0.5f, 0.5f);
        _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = panelSize;
        _root.anchoredPosition = panelAnchoredPosition;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = panelColor;
        panelImage.raycastTarget = false;

        Image accentImage = CreateImage("Accent", _root, accentColor);
        RectTransform accentRect = accentImage.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, 5f);

        _sweepImage = CreateImage("Sweep", _root, sweepColor);
        RectTransform sweepRect = _sweepImage.rectTransform;
        sweepRect.anchorMin = new Vector2(0f, 0f);
        sweepRect.anchorMax = new Vector2(0f, 1f);
        sweepRect.pivot = new Vector2(0.5f, 0.5f);
        sweepRect.sizeDelta = new Vector2(sweepWidth, 0f);
        sweepRect.anchoredPosition = new Vector2(-panelSize.x, 0f);
        sweepRect.localEulerAngles = new Vector3(0f, 0f, -10f);
        _sweepImage.gameObject.SetActive(false);

        float top = panelSize.y * 0.5f;
        float innerWidth = panelSize.x - 96f;

        _titleLabel = CreateText("Title", _root, titleText, 40, FontStyle.Bold, titleColor);
        ConfigureTextRect(_titleLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, top - 80f), innerWidth, 50f);
        _titleLabel.alignment = TextAnchor.MiddleLeft;

        _subtitleLabel = CreateText("Subtitle", _root, subtitleText, 15, FontStyle.Bold, accentColor);
        ConfigureTextRect(_subtitleLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, top - 122f), innerWidth, 22f);
        _subtitleLabel.alignment = TextAnchor.MiddleLeft;

        _bodyLabel = CreateText("Body", _root, bodyText, 18, FontStyle.Normal, bodyColor);
        ConfigureTextRect(_bodyLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, top - 154f), innerWidth, 28f);
        _bodyLabel.alignment = TextAnchor.MiddleLeft;

        Image divider = CreateImage("Divider", _root, new Color(1f, 1f, 1f, 0.14f));
        ConfigureTextRect(divider.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, top - 186f), innerWidth, 1f);

        _creditsLabel = CreateText("Credits", _root, BuildCreditsText(), 18, FontStyle.Normal, titleColor);
        ConfigureTextRect(_creditsLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, top - 206f - 230f), innerWidth, 460f);
        _creditsLabel.alignment = TextAnchor.UpperLeft;
        _creditsLabel.supportRichText = true;
        _creditsLabel.lineSpacing = 1.15f;

        _hintLabel = CreateText("Hint", _root, hintText, 14, FontStyle.Normal, bodyColor);
        ConfigureTextRect(_hintLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -top + 44f), innerWidth, 22f);
        _hintLabel.alignment = TextAnchor.MiddleLeft;
        _hintLabel.canvasRenderer.SetAlpha(0f);

        _previewLabel = CreateText("Preview", _root, previewBadgeText, 11, FontStyle.Bold, accentColor);
        ConfigureTextRect(_previewLabel.rectTransform, new Vector2(1f, 1f), new Vector2(-104f, -28f), 160f, 18f);
        _previewLabel.alignment = TextAnchor.UpperRight;
        _previewLabel.gameObject.SetActive(false);
    }

    void UpdateHintLabel(int remainSeconds)
    {
        if (_hintLabel == null)
            return;

        if (_isDirectPreview)
            _hintLabel.text = hintText;
        else if (autoReturnDelay > 0f)
            _hintLabel.text = $"{hintText} ({remainSeconds})";
        else
            _hintLabel.text = hintText;
    }

    void ApplyRuntimeLabelState()
    {
        if (_previewLabel != null)
            _previewLabel.gameObject.SetActive(_isDirectPreview && ShouldUseDirectPreview());

        TitleCreditsData data = LoadCreditsData();
        if (_titleLabel != null)
            _titleLabel.text = _isDirectPreview && data != null && !string.IsNullOrEmpty(data.title) ? data.title : titleText;

        if (_subtitleLabel != null)
            _subtitleLabel.text = _isDirectPreview ? "DEVELOPMENT CREDITS" : subtitleText;

        if (_bodyLabel != null)
            _bodyLabel.text = _isDirectPreview ? GetSubtitleTail(data) : bodyText;

        if (_hintLabel != null)
            _hintLabel.text = hintText;
    }

    bool ShouldUseDirectPreview()
    {
        return Application.isEditor && enableEditorDirectPreview;
    }

    TitleCreditsData LoadCreditsData()
    {
        if (_creditsData == null && !string.IsNullOrEmpty(creditsResourcePath))
            _creditsData = Resources.Load<TitleCreditsData>(creditsResourcePath);
        return _creditsData;
    }

    static string GetSubtitleTail(TitleCreditsData data)
    {
        if (data == null || string.IsNullOrEmpty(data.subtitle))
            return string.Empty;
        string[] lines = data.subtitle.Split('\n');
        return lines[lines.Length - 1].Trim();
    }

    string BuildCreditsText()
    {
        TitleCreditsData data = LoadCreditsData();
        if (data == null || data.sections == null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (TitleCreditsData.Section section in data.sections)
        {
            if (section == null)
                continue;
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append("<size=13><color=#E5261C>").Append(section.heading.ToUpperInvariant()).Append("</color></size>\n");
            sb.Append("<b>").Append(section.members != null ? string.Join("   ", section.members) : string.Empty).Append("</b>");
        }
        return sb.ToString();
    }

    IEnumerator CoPlayAccentSweep()
    {
        if (_sweepImage == null)
            yield break;

        RectTransform sweepRect = _sweepImage.rectTransform;
        float duration = Mathf.Max(0.1f, sweepDuration);
        float startX = -panelSize.x - (sweepWidth * 0.5f);
        float endX = panelSize.x + (sweepWidth * 0.5f);
        float elapsed = 0f;

        _sweepImage.gameObject.SetActive(true);
        Color baseColor = sweepColor;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            sweepRect.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, eased), 0f);
            _sweepImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(baseColor.a, 0f, t));
            yield return null;
        }

        _sweepImage.color = baseColor;
        _sweepImage.gameObject.SetActive(false);
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
