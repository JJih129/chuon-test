using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MainSceneDeathPresentationController : MonoBehaviour
{
    const float DefaultFixedDeltaTime = 0.02f;
    const int CanvasSortOrder = 6200;

    [Header("Flow")]
    [SerializeField] string titleSceneName = "TitleScene";
    [SerializeField] float titleRevealDelay = 0.45f;
    [SerializeField] float freezeDelay = 0.95f;
    [SerializeField] float buttonsRevealDelay = 1.35f;
    [SerializeField] float overlayFadeDuration = 0.60f;
    [SerializeField] float titleScalePunch = 1.035f;

    [Header("Theme")]
    [SerializeField] GameplayUiTheme themeOverride;
    [SerializeField] string themeResourcePath = GameplayUiTheme.DefaultResourcePath;

    PlayerHealth _boundHealth;
    PlayerHUD _boundHud;
    BossUIController _boundBossUi;
    PauseManager _pauseManager;
    GameplayUiTheme _theme;

    Canvas _canvas;
    CanvasScaler _scaler;
    GraphicRaycaster _raycaster;
    RectTransform _rootRect;
    CanvasGroup _overlayGroup;
    CanvasGroup _titleGroup;
    CanvasGroup _buttonGroup;
    RectTransform _bandRect;
    Text _titleText;
    Text _subtitleText;
    Text _detailText;
    Button _restartButton;
    Button _titleButton;
    Button _quitButton;

    Coroutine _sequenceRoutine;
    bool _sequenceStarted;
    bool _menuStateApplied;
    bool _transitioning;

    public void ConfigureRuntime(PlayerHUD hud, PlayerHealth playerHealth, BossUIController bossUi)
    {
        if (_boundHealth == playerHealth && _boundHud == hud && _boundBossUi == bossUi)
            return;

        Unbind();
        _boundHud = hud;
        _boundHealth = playerHealth;
        _boundBossUi = bossUi;
        _pauseManager = PauseManager.Instance;

        if (_boundHealth != null)
            _boundHealth.OnDied += HandlePlayerDied;

        EnsureOverlay();
    }

    void OnDestroy()
    {
        Unbind();
        KillTweens();
    }

    void Unbind()
    {
        if (_boundHealth != null)
            _boundHealth.OnDied -= HandlePlayerDied;

        _boundHealth = null;
        _boundHud = null;
        _boundBossUi = null;
    }

    void HandlePlayerDied()
    {
        if (_sequenceStarted)
            return;

        _sequenceStarted = true;
        EnsureOverlay();

        if (_sequenceRoutine != null)
            StopCoroutine(_sequenceRoutine);

        _sequenceRoutine = StartCoroutine(CoPresentDeathOverlay());
    }

    IEnumerator CoPresentDeathOverlay()
    {
        ResetOverlayState();
        _rootRect.gameObject.SetActive(true);

        yield return WaitRealtime(titleRevealDelay);

        KillTweens();
        _overlayGroup.DOFade(1f, overlayFadeDuration).SetUpdate(true);
        _titleGroup.DOFade(1f, overlayFadeDuration).SetUpdate(true);
        _titleGroup.transform.localScale = Vector3.one * titleScalePunch;
        _titleGroup.transform.DOScale(1f, overlayFadeDuration).SetEase(Ease.OutCubic).SetUpdate(true);

        float waitToFreeze = Mathf.Max(0f, freezeDelay - titleRevealDelay);
        if (waitToFreeze > 0f)
            yield return WaitRealtime(waitToFreeze);

        ApplyDeathMenuState();
        HideGameplayHud();

        float waitToButtons = Mathf.Max(0f, buttonsRevealDelay - freezeDelay);
        if (waitToButtons > 0f)
            yield return WaitRealtime(waitToButtons);

        _buttonGroup.DOFade(1f, 0.22f).SetUpdate(true);
        _buttonGroup.interactable = true;
        _buttonGroup.blocksRaycasts = true;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_restartButton != null ? _restartButton.gameObject : null);
    }

    void EnsureOverlay()
    {
        if (_canvas != null)
            return;

        _theme = themeOverride != null
            ? themeOverride
            : GameplayUiTheme.LoadOrCreate(themeResourcePath);

        GameObject canvasObject = new GameObject(
            "MissionFailedOverlayCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortOrder;
        _canvas.pixelPerfect = false;

        _scaler = canvasObject.GetComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _scaler.referenceResolution = new Vector2(1920f, 1080f);
        _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        _scaler.matchWidthOrHeight = 0.5f;

        _raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        _raycaster.enabled = true;

        _rootRect = canvasObject.GetComponent<RectTransform>();
        Stretch(_rootRect);

        Image overlayImage = canvasObject.AddComponent<Image>();
        overlayImage.sprite = _theme.ResolvePanelSprite();
        overlayImage.type = Image.Type.Sliced;
        overlayImage.color = _theme.overlayColor;
        overlayImage.raycastTarget = true;

        _overlayGroup = canvasObject.GetComponent<CanvasGroup>();
        _overlayGroup.alpha = 0f;
        _overlayGroup.blocksRaycasts = true;
        _overlayGroup.interactable = true;

        BuildVignette();
        BuildCenterBand();
        BuildActionButtons();

        _rootRect.gameObject.SetActive(false);
    }

    void BuildVignette()
    {
        CreatePanel("TopVignette", _rootRect, new Vector2(0f, 0.74f), new Vector2(1f, 1f), _theme.vignetteColor, false);
        CreatePanel("BottomVignette", _rootRect, new Vector2(0f, 0f), new Vector2(1f, 0.32f), _theme.vignetteColor, false);
    }

    void BuildCenterBand()
    {
        _bandRect = CreatePanel("FailureBand", _rootRect, new Vector2(0f, 0.31f), new Vector2(1f, 0.69f), _theme.bandColor, true).rectTransform;
        AddLine("BandTop", _bandRect, new Vector2(0.06f, 0.95f), new Vector2(0.94f, 0.965f), _theme.panelLineColor);
        AddLine("BandBottom", _bandRect, new Vector2(0.06f, 0.035f), new Vector2(0.94f, 0.05f), new Color(_theme.accentSecondaryColor.r, _theme.accentSecondaryColor.g, _theme.accentSecondaryColor.b, 0.42f));
        AddLine("BandAccent", _bandRect, new Vector2(0.38f, 0.95f), new Vector2(0.62f, 0.965f), _theme.accentColor);

        GameObject titleGroupObject = new GameObject("TitleGroup", typeof(RectTransform), typeof(CanvasGroup));
        titleGroupObject.transform.SetParent(_bandRect, false);
        _titleGroup = titleGroupObject.GetComponent<CanvasGroup>();
        _titleGroup.alpha = 0f;
        _titleGroup.transform.localScale = Vector3.one;

        RectTransform titleGroupRect = titleGroupObject.GetComponent<RectTransform>();
        Stretch(titleGroupRect);

        _titleText = CreateText(
            "MissionTitle",
            titleGroupRect,
            "MISSION FAILED",
            _theme.ResolveTitleFont(),
            _theme.deathTitleFontSize,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            _theme.titleColor,
            new Vector2(0.16f, 0.34f),
            new Vector2(0.84f, 0.82f));

        Shadow titleShadow = _titleText.gameObject.AddComponent<Shadow>();
        titleShadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
        titleShadow.effectDistance = new Vector2(4f, -4f);

        _subtitleText = CreateText(
            "MissionSubtitle",
            titleGroupRect,
            "\uC791\uC804 \uC2E4\uD328",
            _theme.ResolveBodyFont(),
            _theme.deathSubtitleFontSize,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            _theme.subtitleColor,
            new Vector2(0.22f, 0.22f),
            new Vector2(0.78f, 0.36f));

        _detailText = CreateText(
            "MissionDetail",
            titleGroupRect,
            "\uC804\uD22C \uAE30\uB85D\uC774 \uC885\uB8CC\uB418\uC5C8\uC2B5\uB2C8\uB2E4  /  RETRY PROTOCOL AVAILABLE",
            _theme.ResolveBodyFont(),
            _theme.detailFontSize,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            _theme.mutedColor,
            new Vector2(0.16f, 0.10f),
            new Vector2(0.84f, 0.22f));
    }

    void BuildActionButtons()
    {
        GameObject buttonsRootObject = new GameObject("ActionButtons", typeof(RectTransform), typeof(CanvasGroup));
        buttonsRootObject.transform.SetParent(_rootRect, false);
        _buttonGroup = buttonsRootObject.GetComponent<CanvasGroup>();
        _buttonGroup.alpha = 0f;
        _buttonGroup.interactable = false;
        _buttonGroup.blocksRaycasts = false;

        RectTransform buttonsRoot = buttonsRootObject.GetComponent<RectTransform>();
        buttonsRoot.anchorMin = new Vector2(0.27f, 0.09f);
        buttonsRoot.anchorMax = new Vector2(0.73f, 0.18f);
        buttonsRoot.offsetMin = Vector2.zero;
        buttonsRoot.offsetMax = Vector2.zero;
        buttonsRoot.localScale = Vector3.one;

        HorizontalLayoutGroup layout = buttonsRootObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        _restartButton = CreateActionButton(buttonsRoot, "\uC7AC\uC2DC\uC791");
        _restartButton.onClick.AddListener(RestartCurrentScene);

        _titleButton = CreateActionButton(buttonsRoot, "\uD0C0\uC774\uD2C0");
        _titleButton.onClick.AddListener(ReturnToTitle);

        _quitButton = CreateActionButton(buttonsRoot, "\uAC8C\uC784 \uC885\uB8CC");
        _quitButton.onClick.AddListener(QuitGame);
    }

    Button CreateActionButton(RectTransform parent, string label)
    {
        GameObject buttonObject = new GameObject("Button_" + label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;

        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredWidth = 0f;
        layout.preferredHeight = 82f;
        layout.flexibleWidth = 1f;

        Image image = buttonObject.GetComponent<Image>();
        image.sprite = _theme.ResolveButtonSprite();
        image.type = Image.Type.Sliced;
        image.color = _theme.buttonIdleColor;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = _theme.buttonIdleColor;
        colors.highlightedColor = _theme.buttonHoverColor;
        colors.pressedColor = _theme.buttonPressedColor;
        colors.selectedColor = _theme.buttonHoverColor;
        colors.disabledColor = new Color(_theme.buttonIdleColor.r, _theme.buttonIdleColor.g, _theme.buttonIdleColor.b, 0.38f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        AddLine("TopLine", rect, new Vector2(0.08f, 0.93f), new Vector2(0.92f, 0.97f), _theme.panelLineColor);
        AddLine("BottomLine", rect, new Vector2(0.08f, 0.03f), new Vector2(0.92f, 0.07f), new Color(_theme.accentSecondaryColor.r, _theme.accentSecondaryColor.g, _theme.accentSecondaryColor.b, 0.42f));
        AddLine("Accent", rect, new Vector2(0.08f, 0.03f), new Vector2(0.28f, 0.07f), _theme.accentColor);

        Text buttonText = CreateText(
            "Label",
            rect,
            label,
            _theme.ResolveBodyFont(),
            _theme.buttonFontSize,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            _theme.buttonTextColor,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f));

        Shadow buttonShadow = buttonText.gameObject.AddComponent<Shadow>();
        buttonShadow.effectColor = new Color(0f, 0f, 0f, 0.40f);
        buttonShadow.effectDistance = new Vector2(1f, -1f);

        return button;
    }

    void ResetOverlayState()
    {
        if (_overlayGroup == null)
            return;

        KillTweens();
        _overlayGroup.alpha = 0f;
        _titleGroup.alpha = 0f;
        _titleGroup.transform.localScale = Vector3.one * titleScalePunch;
        _buttonGroup.alpha = 0f;
        _buttonGroup.interactable = false;
        _buttonGroup.blocksRaycasts = false;
        _menuStateApplied = false;
        _transitioning = false;
    }

    void ApplyDeathMenuState()
    {
        if (_menuStateApplied)
            return;

        _menuStateApplied = true;

        if (_pauseManager == null)
            _pauseManager = PauseManager.Instance;

        if (_pauseManager != null)
            _pauseManager.enabled = false;

        Time.timeScale = 0f;
        Time.fixedDeltaTime = DefaultFixedDeltaTime;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void HideGameplayHud()
    {
        if (_boundHud != null)
            _boundHud.gameObject.SetActive(false);

        if (_boundBossUi != null)
            _boundBossUi.gameObject.SetActive(false);
    }

    void RestartCurrentScene()
    {
        if (_transitioning)
            return;

        _transitioning = true;
        RuntimeMenuSceneStateUtility.PrepareForGameplayScene();
        SceneFader sceneFader = RuntimeSceneFaderUtility.EnsureSceneFader();
        string activeSceneName = SceneManager.GetActiveScene().name;

        if (sceneFader != null)
            sceneFader.FadeOutAndLoadScene(activeSceneName);
        else
            SceneManager.LoadScene(activeSceneName);
    }

    void ReturnToTitle()
    {
        if (_transitioning)
            return;

        _transitioning = true;
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();
        SceneFader sceneFader = RuntimeSceneFaderUtility.EnsureSceneFader();

        if (sceneFader != null)
            sceneFader.FadeOutAndLoadScene(titleSceneName);
        else
            SceneManager.LoadScene(titleSceneName);
    }

    void QuitGame()
    {
        if (_transitioning)
            return;

        _transitioning = true;
        RuntimeMenuSceneStateUtility.PrepareForMenuScene();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator WaitRealtime(float seconds)
    {
        float endAt = Time.unscaledTime + seconds;
        while (Time.unscaledTime < endAt)
            yield return null;
    }

    void KillTweens()
    {
        if (_overlayGroup != null)
            DOTween.Kill(_overlayGroup);
        if (_titleGroup != null)
        {
            DOTween.Kill(_titleGroup);
            DOTween.Kill(_titleGroup.transform);
        }
        if (_buttonGroup != null)
            DOTween.Kill(_buttonGroup);
    }

    Image CreatePanel(string objectName, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color, bool sliced)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = panelObject.GetComponent<Image>();
        image.sprite = sliced ? _theme.ResolvePanelSprite() : null;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text CreateText(string objectName, RectTransform parent, string content, Font font, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void AddLine(string objectName, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject lineObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = lineObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = lineObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
