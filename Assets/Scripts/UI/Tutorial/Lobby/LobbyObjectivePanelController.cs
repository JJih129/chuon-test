using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LobbyObjectivePanelController : MonoBehaviour
{
    [SerializeField] private LobbyManager lobbyManager;

    [Header("Layout")]
    [SerializeField] private Vector2 objectivePanelAnchoredPosition = new Vector2(-32f, -28f);
    [SerializeField] private Vector2 objectivePanelSize = new Vector2(660f, 312f);
    [SerializeField] private Vector2 supportPanelAnchoredPosition = new Vector2(32f, -124f);
    [SerializeField] private Vector2 supportPanelSize = new Vector2(720f, 228f);
    [SerializeField] private Vector2 iconAnchoredPosition = new Vector2(28f, -20f);
    [SerializeField] private Vector2 iconSize = new Vector2(40f, 40f);
    [SerializeField] private Vector2 categoryAnchoredPosition = new Vector2(28f, -20f);
    [SerializeField] private Vector2 counterAnchoredPosition = new Vector2(-28f, -20f);
    [SerializeField] private Vector2 badgeAnchoredPosition = new Vector2(-28f, -58f);
    [SerializeField] private Vector2 badgeSize = new Vector2(180f, 34f);
    [SerializeField] private Vector2 titleAnchoredPosition = new Vector2(22f, -68f);
    [SerializeField] private Vector2 titleSize = new Vector2(480f, 42f);
    [SerializeField] private Vector2 statusAnchoredPosition = new Vector2(22f, -98f);
    [SerializeField] private Vector2 statusSize = new Vector2(480f, 70f);
    [SerializeField] private Vector2 progressAnchoredMin = new Vector2(22f, 10f);
    [SerializeField] private Vector2 progressAnchoredMax = new Vector2(-22f, 28f);
    [SerializeField] private Vector2 cuePanelAnchoredPosition = new Vector2(24f, -24f);
    [SerializeField] private Vector2 cuePanelSize = new Vector2(520f, 128f);
    [SerializeField] private float accentHeight = 7f;
    [SerializeField] private float categoryFontSize = 22f;
    [SerializeField] private float counterFontSize = 20f;
    [SerializeField] private float badgeFontSize = 18f;
    [SerializeField] private float statusFontSize = 22f;
    [SerializeField] private float progressNodeSize = 14f;
    [SerializeField] private float progressCurrentScale = 1.35f;
    [SerializeField] private float progressCompletedScale = 1.10f;
    [SerializeField] private float progressLinkThickness = 4f;
    [SerializeField] private float sweepDuration = 0.24f;
    [SerializeField] private float iconPulseDuration = 0.16f;

    [Header("Colors")]
    [SerializeField] private Color arrivalColor = new Color(0.36f, 0.94f, 1f, 0.96f);
    [SerializeField] private Color combatColor = new Color(1f, 0.34f, 0.22f, 0.98f);
    [SerializeField] private Color routeColor = new Color(0.28f, 0.88f, 1f, 0.96f);
    [SerializeField] private Color boardColor = new Color(1f, 0.76f, 0.28f, 0.98f);
    [SerializeField] private Color elevatorColor = new Color(0.84f, 0.92f, 1f, 0.96f);
    [SerializeField] private Color objectivePanelColor = new Color(0.03f, 0.09f, 0.13f, 0.92f);
    [SerializeField] private Color supportPanelColor = new Color(0.03f, 0.08f, 0.12f, 0.90f);
    [SerializeField] private Color bodyTextColor = new Color(0.90f, 0.97f, 1f, 0.96f);
    [SerializeField] private Color pendingNodeColor = new Color(0.26f, 0.34f, 0.42f, 0.54f);
    [SerializeField] private Color pendingLinkColor = new Color(0.22f, 0.28f, 0.36f, 0.26f);
    [SerializeField] private Color cuePanelColor = new Color(0.12f, 0.03f, 0.03f, 0.84f);

    RectTransform _hudRoot;
    RectTransform _runtimeRoot;
    RawImage _runtimeBackground;
    RawImage _accentImage;
    RawImage _sweepImage;
    RectTransform _iconRoot;
    RawImage _iconBackplate;
    RawImage _iconPrimary;
    RawImage _iconSecondary;
    RawImage _iconTertiary;
    TextMeshProUGUI _categoryText;
    TextMeshProUGUI _counterText;
    RectTransform _badgeRoot;
    RawImage _badgeBackground;
    TextMeshProUGUI _badgeText;
    TextMeshProUGUI _titleText;
    TextMeshProUGUI _statusText;
    RectTransform _progressRoot;
    RawImage[] _progressNodes;
    RawImage[] _progressLinks;
    bool _subscribed;
    Coroutine _sweepRoutine;
    Coroutine _iconPulseRoutine;
    string _questTitle;
    string _questDescription;
    LobbyManager.LobbyFlowPhase _currentPhase = LobbyManager.LobbyFlowPhase.Arrival;
    Canvas _runtimeCanvas;
    RectTransform _cueRoot;
    CanvasGroup _cueGroup;
    RawImage _cueBackground;
    RawImage _cueAccent;
    TextMeshProUGUI _cueTitleText;
    TextMeshProUGUI _cueBodyText;
    bool _transientCueVisible;

    public void ConfigureRuntime(LobbyManager runtimeLobbyManager)
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
        {
            DisableRuntimePanel();
            return;
        }

        lobbyManager = runtimeLobbyManager;
        CacheQuestTexts();
        EnsureRuntimeVisuals();
        RefreshSubscriptions();
        ApplyPhase(lobbyManager != null ? lobbyManager.CurrentPhase : LobbyManager.LobbyFlowPhase.Arrival, false);
    }

    void OnEnable()
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
        {
            DisableRuntimePanel();
            return;
        }

        CacheQuestTexts();
        EnsureRuntimeVisuals();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        HideImmediate();
        HideTransientCue();
        if (_cueGroup != null)
            _cueGroup.alpha = 0f;
        SetLegacyQuestTextVisible(true);
        SetLegacyQuestPanelVisible(true);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (lobbyManager == null)
            lobbyManager = GetComponent<LobbyManager>();
        if (lobbyManager == null)
            return;

        lobbyManager.PhaseChanged += HandlePhaseChanged;
        lobbyManager.QuestUpdated += HandleQuestUpdated;
        lobbyManager.EnemyProgressUpdated += HandleEnemyProgressUpdated;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed || lobbyManager == null)
            return;

        lobbyManager.PhaseChanged -= HandlePhaseChanged;
        lobbyManager.QuestUpdated -= HandleQuestUpdated;
        lobbyManager.EnemyProgressUpdated -= HandleEnemyProgressUpdated;
        _subscribed = false;
    }

    void HandlePhaseChanged(LobbyManager.LobbyFlowPhase phase)
    {
        ApplyPhase(phase, true);
    }

    void HandleQuestUpdated(string title, string description)
    {
        EnsureRuntimeVisuals();
        _questTitle = title;
        _questDescription = description;
        ApplyQuestTexts();
        if (!_transientCueVisible)
            ApplySupportTexts();
    }

    void HandleEnemyProgressUpdated(int current, int total)
    {
        _questDescription = "\uc801 \uc81c\uac70 " + current + " / " + Mathf.Max(1, total);
        ApplyQuestTexts();
        if (!_transientCueVisible)
            ApplySupportTexts();
    }

    void ApplyPhase(LobbyManager.LobbyFlowPhase phase, bool animated)
    {
        EnsureRuntimeVisuals();
        _currentPhase = phase;

        Color color = GetPhaseColor(phase);
        int index = GetPhaseIndex(phase);
        int stepCount = 5;

        if (_runtimeBackground != null)
            _runtimeBackground.color = objectivePanelColor;

        if (_accentImage != null)
            _accentImage.color = color;

        ApplyIconStyle(phase, color, animated);

        if (_categoryText != null)
        {
            _categoryText.text = GetPhaseCategory(phase);
            _categoryText.color = color;
        }

        if (_counterText != null)
        {
            _counterText.text = (index + 1).ToString("00") + " / " + stepCount.ToString("00");
            _counterText.color = color;
        }

        if (_badgeBackground != null)
            _badgeBackground.color = Color.Lerp(color, Color.black, 0.55f);

        if (_badgeText != null)
        {
            _badgeText.text = GetPhaseBadge(phase);
            _badgeText.color = color;
        }

        if (_titleText != null)
            _titleText.color = bodyTextColor;

        if (_statusText != null)
            _statusText.color = bodyTextColor;

        ApplyQuestTexts();
        if (!_transientCueVisible)
            ApplySupportTexts();

        EnsureProgressVisuals();
        UpdateProgressVisuals(index, color);

        if (animated)
            PlaySweep(color, 0.16f);
    }

    void EnsureRuntimeVisuals()
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
            return;

        CanvasGroup questPanel = lobbyManager != null ? lobbyManager.questPanelGroup : null;
        if (questPanel == null)
            return;

        SetLegacyQuestTextVisible(false);
        SetLegacyQuestPanelVisible(false);
        EnsureRuntimeCanvas(questPanel);
        EnsureHudRoot();
        if (_hudRoot == null)
            return;

        if (_runtimeRoot == null)
        {
            Transform existing = _hudRoot.Find("LobbyObjectiveRuntime");
            if (existing != null)
                _runtimeRoot = existing as RectTransform;

            if (_runtimeRoot == null)
            {
                GameObject rootObject = new GameObject("LobbyObjectiveRuntime", typeof(RectTransform));
                _runtimeRoot = rootObject.GetComponent<RectTransform>();
                _runtimeRoot.SetParent(_hudRoot, false);
            }
        }

        _runtimeRoot.anchorMin = new Vector2(1f, 1f);
        _runtimeRoot.anchorMax = new Vector2(1f, 1f);
        _runtimeRoot.pivot = new Vector2(1f, 1f);
        _runtimeRoot.anchoredPosition = objectivePanelAnchoredPosition;
        _runtimeRoot.sizeDelta = objectivePanelSize;
        _runtimeRoot.localScale = Vector3.one;
        _runtimeRoot.localRotation = Quaternion.identity;
        _runtimeRoot.gameObject.SetActive(true);
        _runtimeRoot.SetAsLastSibling();

        _runtimeBackground = EnsurePanelImage(_runtimeRoot, "Background", out RectTransform backgroundRect);
        StretchToParent(backgroundRect);
        _runtimeBackground.color = objectivePanelColor;

        _accentImage = EnsurePanelImage(_runtimeRoot, "Accent", out RectTransform accentRect);
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0f, accentHeight);

        _sweepImage = EnsurePanelImage(_runtimeRoot, "Sweep", out RectTransform sweepRect);
        StretchToParent(sweepRect);
        _sweepImage.color = new Color(1f, 1f, 1f, 0f);

        EnsureIconVisuals();

        _categoryText = EnsureText(_runtimeRoot, "Category");
        RectTransform categoryRect = _categoryText.rectTransform;
        categoryRect.anchorMin = new Vector2(0f, 1f);
        categoryRect.anchorMax = new Vector2(0f, 1f);
        categoryRect.pivot = new Vector2(0f, 1f);
        categoryRect.anchoredPosition = categoryAnchoredPosition;
        categoryRect.sizeDelta = new Vector2(260f, 30f);
        _categoryText.fontSize = categoryFontSize;
        _categoryText.fontStyle = FontStyles.Bold;
        _categoryText.alignment = TextAlignmentOptions.Left;
        _categoryText.raycastTarget = false;

        _counterText = EnsureText(_runtimeRoot, "Counter");
        RectTransform counterRect = _counterText.rectTransform;
        counterRect.anchorMin = new Vector2(1f, 1f);
        counterRect.anchorMax = new Vector2(1f, 1f);
        counterRect.pivot = new Vector2(1f, 1f);
        counterRect.anchoredPosition = counterAnchoredPosition;
        counterRect.sizeDelta = new Vector2(140f, 30f);
        _counterText.fontSize = counterFontSize;
        _counterText.fontStyle = FontStyles.Bold;
        _counterText.alignment = TextAlignmentOptions.Right;
        _counterText.raycastTarget = false;

        if (_badgeRoot == null)
        {
            Transform existing = _runtimeRoot.Find("Badge");
            if (existing != null)
                _badgeRoot = existing as RectTransform;

            if (_badgeRoot == null)
            {
                GameObject badgeObject = new GameObject("Badge", typeof(RectTransform));
                _badgeRoot = badgeObject.GetComponent<RectTransform>();
                _badgeRoot.SetParent(_runtimeRoot, false);
            }
        }

        _badgeRoot.anchorMin = new Vector2(1f, 1f);
        _badgeRoot.anchorMax = new Vector2(1f, 1f);
        _badgeRoot.pivot = new Vector2(1f, 1f);
        _badgeRoot.anchoredPosition = badgeAnchoredPosition;
        _badgeRoot.sizeDelta = new Vector2(Mathf.Max(210f, badgeSize.x), badgeSize.y);

        _badgeBackground = EnsurePanelImage(_badgeRoot, "Background", out RectTransform badgeBackgroundRect);
        StretchToParent(badgeBackgroundRect);

        _badgeText = EnsureText(_badgeRoot, "Label");
        RectTransform badgeTextRect = _badgeText.rectTransform;
        StretchToParent(badgeTextRect);
        badgeTextRect.offsetMin = new Vector2(12f, 4f);
        badgeTextRect.offsetMax = new Vector2(-12f, -4f);
        _badgeText.fontSize = badgeFontSize;
        _badgeText.fontStyle = FontStyles.Bold;
        _badgeText.alignment = TextAlignmentOptions.Center;
        _badgeText.overflowMode = TextOverflowModes.Ellipsis;
        _badgeText.raycastTarget = false;

        _titleText = EnsureText(_runtimeRoot, "Title");
        RectTransform titleRect = _titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(28f, -146f);
        titleRect.offsetMax = new Vector2(-28f, -92f);
        _titleText.fontSize = Mathf.Max(30f, statusFontSize + 8f);
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.alignment = TextAlignmentOptions.TopLeft;
        _titleText.enableWordWrapping = true;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        _titleText.extraPadding = true;
        _titleText.lineSpacing = 2f;
        _titleText.raycastTarget = false;

        _statusText = EnsureText(_runtimeRoot, "Status");
        RectTransform statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.offsetMin = new Vector2(28f, -236f);
        statusRect.offsetMax = new Vector2(-28f, -152f);
        _statusText.fontSize = Mathf.Max(24f, statusFontSize + 2f);
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.alignment = TextAlignmentOptions.Left;
        _statusText.overflowMode = TextOverflowModes.Ellipsis;
        _statusText.enableWordWrapping = true;
        _statusText.extraPadding = true;
        _statusText.lineSpacing = 4f;
        _statusText.raycastTarget = false;

        EnsureProgressVisuals();
        EnsureTransientCueVisuals();
    }

    public void ShowTransientCue(string title, string description)
    {
        EnsureRuntimeVisuals();
        if (_cueGroup == null)
            return;

        Color phaseColor = GetPhaseColor(_currentPhase);
        _transientCueVisible = true;
        _cueGroup.alpha = 1f;

        if (_cueBackground != null)
            _cueBackground.color = cuePanelColor;

        if (_cueAccent != null)
            _cueAccent.color = phaseColor;

        if (_cueTitleText != null)
        {
            _cueTitleText.color = Color.Lerp(phaseColor, Color.white, 0.18f);
            _cueTitleText.text = string.IsNullOrWhiteSpace(title) ? GetSupportTitle(_currentPhase) : title;
        }

        if (_cueBodyText != null)
            _cueBodyText.text = string.IsNullOrWhiteSpace(description) ? GetPhaseStatus(_currentPhase) : description;
    }

    public void HideTransientCue()
    {
        _transientCueVisible = false;
        ApplySupportTexts();
    }

    void CacheQuestTexts()
    {
        if (lobbyManager == null)
            return;

        _questTitle = lobbyManager.questTitleText != null ? lobbyManager.questTitleText.text : string.Empty;
        _questDescription = lobbyManager.questDescriptionText != null ? lobbyManager.questDescriptionText.text : string.Empty;
        _currentPhase = lobbyManager.CurrentPhase;
    }

    void EnsureRuntimeCanvas(CanvasGroup questPanel)
    {
        if (_runtimeCanvas == null && questPanel != null)
            _runtimeCanvas = questPanel.GetComponentInParent<Canvas>();
    }

    void EnsureHudRoot()
    {
        if (_runtimeCanvas == null)
            return;

        if (_hudRoot == null)
        {
            Transform existing = _runtimeCanvas.transform.Find("LobbyRuntimeHUD");
            if (existing != null)
                _hudRoot = existing as RectTransform;

            if (_hudRoot == null)
            {
                GameObject rootObject = new GameObject("LobbyRuntimeHUD", typeof(RectTransform));
                _hudRoot = rootObject.GetComponent<RectTransform>();
                _hudRoot.SetParent(_runtimeCanvas.transform, false);
            }
        }

        StretchToParent(_hudRoot);
        _hudRoot.SetAsLastSibling();
    }

    void SetLegacyQuestTextVisible(bool visible)
    {
        if (lobbyManager == null)
            return;

        if (lobbyManager.questTitleText != null)
            lobbyManager.questTitleText.gameObject.SetActive(visible);

        if (lobbyManager.questDescriptionText != null)
            lobbyManager.questDescriptionText.gameObject.SetActive(visible);
    }

    void SetLegacyQuestPanelVisible(bool visible)
    {
        if (lobbyManager == null || lobbyManager.questPanelGroup == null)
            return;

        lobbyManager.questPanelGroup.alpha = visible ? 1f : 0f;
        lobbyManager.questPanelGroup.interactable = false;
        lobbyManager.questPanelGroup.blocksRaycasts = false;
    }

    void ApplyQuestTexts()
    {
        if (_badgeText != null)
            _badgeText.text = GetPhaseBadge(_currentPhase);

        if (_titleText != null)
            _titleText.text = string.IsNullOrWhiteSpace(_questTitle) ? GetPhaseCategory(_currentPhase) : _questTitle;

        if (_statusText != null)
        {
            string description = string.IsNullOrWhiteSpace(_questDescription)
                ? GetPhaseStatus(_currentPhase)
                : _questDescription;
            _statusText.text = description;
        }
    }

    void ApplySupportTexts()
    {
        EnsureTransientCueVisuals();
        if (_cueGroup == null)
            return;

        Color phaseColor = GetPhaseColor(_currentPhase);
        _cueGroup.alpha = 1f;

        if (_cueBackground != null)
            _cueBackground.color = supportPanelColor;

        if (_cueAccent != null)
            _cueAccent.color = phaseColor;

        if (_cueTitleText != null)
        {
            _cueTitleText.color = Color.Lerp(phaseColor, Color.white, 0.2f);
            _cueTitleText.text = GetSupportTitle(_currentPhase);
        }

        if (_cueBodyText != null)
        {
            string body = GetSupportBody(_currentPhase);
            _cueBodyText.color = bodyTextColor;
            _cueBodyText.text = body;
        }
    }

    void EnsureTransientCueVisuals()
    {
        if (_runtimeCanvas == null)
            return;

        if (_cueRoot == null)
        {
            Transform existing = _hudRoot != null ? _hudRoot.Find("LobbyTransientCueRuntime") : null;
            if (existing != null)
                _cueRoot = existing as RectTransform;

            if (_cueRoot == null)
            {
                GameObject rootObject = new GameObject("LobbyTransientCueRuntime", typeof(RectTransform), typeof(CanvasGroup));
                _cueRoot = rootObject.GetComponent<RectTransform>();
                _cueRoot.SetParent(_hudRoot != null ? _hudRoot : _runtimeCanvas.transform, false);
            }
        }

        _cueRoot.anchorMin = new Vector2(0f, 1f);
        _cueRoot.anchorMax = new Vector2(0f, 1f);
        _cueRoot.pivot = new Vector2(0f, 1f);
        _cueRoot.anchoredPosition = supportPanelAnchoredPosition;
        _cueRoot.sizeDelta = supportPanelSize;
        _cueRoot.localScale = Vector3.one;
        _cueRoot.localRotation = Quaternion.identity;
        _cueRoot.SetAsLastSibling();

        _cueGroup = _cueRoot.GetComponent<CanvasGroup>();
        _cueGroup.interactable = false;
        _cueGroup.blocksRaycasts = false;
        if (_cueGroup.alpha <= 0f)
            _cueGroup.alpha = 1f;

        _cueBackground = EnsurePanelImage(_cueRoot, "CueBackground", out RectTransform cueBackgroundRect);
        StretchToParent(cueBackgroundRect);
        _cueBackground.color = supportPanelColor;

        _cueAccent = EnsurePanelImage(_cueRoot, "CueAccent", out RectTransform cueAccentRect);
        cueAccentRect.anchorMin = new Vector2(0f, 0f);
        cueAccentRect.anchorMax = new Vector2(0f, 1f);
        cueAccentRect.pivot = new Vector2(0f, 0.5f);
        cueAccentRect.anchoredPosition = Vector2.zero;
        cueAccentRect.sizeDelta = new Vector2(8f, 0f);

        _cueTitleText = EnsureText(_cueRoot, "CueTitle");
        RectTransform cueTitleRect = _cueTitleText.rectTransform;
        cueTitleRect.anchorMin = new Vector2(0f, 1f);
        cueTitleRect.anchorMax = new Vector2(1f, 1f);
        cueTitleRect.pivot = new Vector2(0f, 1f);
        cueTitleRect.offsetMin = new Vector2(32f, -64f);
        cueTitleRect.offsetMax = new Vector2(-24f, -18f);
        _cueTitleText.fontSize = 30f;
        _cueTitleText.fontStyle = FontStyles.Bold;
        _cueTitleText.alignment = TextAlignmentOptions.TopLeft;
        _cueTitleText.enableWordWrapping = false;
        _cueTitleText.overflowMode = TextOverflowModes.Ellipsis;

        _cueBodyText = EnsureText(_cueRoot, "CueBody");
        RectTransform cueBodyRect = _cueBodyText.rectTransform;
        cueBodyRect.anchorMin = new Vector2(0f, 0f);
        cueBodyRect.anchorMax = new Vector2(1f, 1f);
        cueBodyRect.pivot = new Vector2(0f, 0f);
        cueBodyRect.offsetMin = new Vector2(32f, 26f);
        cueBodyRect.offsetMax = new Vector2(-24f, -72f);
        _cueBodyText.fontSize = 22f;
        _cueBodyText.fontStyle = FontStyles.Normal;
        _cueBodyText.alignment = TextAlignmentOptions.TopLeft;
        _cueBodyText.enableWordWrapping = true;
        _cueBodyText.overflowMode = TextOverflowModes.Ellipsis;
        _cueBodyText.color = bodyTextColor;
    }

    void EnsureIconVisuals()
    {
        if (_runtimeRoot == null)
            return;

        if (_iconRoot == null)
        {
            Transform existing = _runtimeRoot.Find("Icon");
            if (existing != null)
                _iconRoot = existing as RectTransform;

            if (_iconRoot == null)
            {
                GameObject rootObject = new GameObject("Icon", typeof(RectTransform));
                _iconRoot = rootObject.GetComponent<RectTransform>();
                _iconRoot.SetParent(_runtimeRoot, false);
            }
        }

        _iconRoot.anchorMin = new Vector2(0f, 1f);
        _iconRoot.anchorMax = new Vector2(0f, 1f);
        _iconRoot.pivot = new Vector2(0f, 1f);
        _iconRoot.anchoredPosition = iconAnchoredPosition;
        _iconRoot.sizeDelta = iconSize;
        _iconRoot.localScale = Vector3.one;

        _iconBackplate = EnsurePanelImage(_iconRoot, "Backplate", out RectTransform backplateRect);
        StretchToParent(backplateRect);

        _iconPrimary = EnsurePanelImage(_iconRoot, "Primary", out _);
        _iconSecondary = EnsurePanelImage(_iconRoot, "Secondary", out _);
        _iconTertiary = EnsurePanelImage(_iconRoot, "Tertiary", out _);
    }

    void EnsureProgressVisuals()
    {
        if (_runtimeRoot == null)
            return;

        if (_progressRoot == null)
        {
            Transform existing = _runtimeRoot.Find("Progress");
            if (existing != null)
                _progressRoot = existing as RectTransform;

            if (_progressRoot == null)
            {
                GameObject progressObject = new GameObject("Progress", typeof(RectTransform));
                _progressRoot = progressObject.GetComponent<RectTransform>();
                _progressRoot.SetParent(_runtimeRoot, false);
            }
        }

        _progressRoot.anchorMin = new Vector2(0f, 0f);
        _progressRoot.anchorMax = new Vector2(1f, 0f);
        _progressRoot.pivot = new Vector2(0.5f, 0f);
        _progressRoot.offsetMin = progressAnchoredMin;
        _progressRoot.offsetMax = progressAnchoredMax;

        const int nodeCount = 5;
        if (_progressNodes != null && _progressNodes.Length == nodeCount)
            return;

        for (int i = _progressRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = _progressRoot.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }

        _progressNodes = new RawImage[nodeCount];
        _progressLinks = new RawImage[nodeCount - 1];

        for (int i = 0; i < nodeCount; i++)
        {
            RawImage node = EnsureProgressImage("Node_" + i);
            _progressNodes[i] = node;
            RectTransform rect = node.rectTransform;
            rect.SetParent(_progressRoot, false);
            rect.anchorMin = new Vector2((float)i / (nodeCount - 1), 0.5f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(progressNodeSize, progressNodeSize);
        }

        for (int i = 0; i < _progressLinks.Length; i++)
        {
            RawImage link = EnsureProgressImage("Link_" + i);
            _progressLinks[i] = link;
            RectTransform rect = link.rectTransform;
            rect.SetParent(_progressRoot, false);
            rect.anchorMin = new Vector2((float)i / (nodeCount - 1), 0.5f);
            rect.anchorMax = new Vector2((float)(i + 1) / (nodeCount - 1), 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            float padding = progressNodeSize * 0.55f;
            rect.offsetMin = new Vector2(padding, -progressLinkThickness * 0.5f);
            rect.offsetMax = new Vector2(-padding, progressLinkThickness * 0.5f);
        }
    }

    void UpdateProgressVisuals(int activeIndex, Color activeColor)
    {
        if (_progressNodes == null || _progressLinks == null)
            return;

        for (int i = 0; i < _progressNodes.Length; i++)
        {
            RawImage node = _progressNodes[i];
            if (node == null)
                continue;

            bool isCurrent = i == activeIndex;
            bool isCompleted = i < activeIndex;
            node.color = isCurrent || isCompleted ? activeColor : pendingNodeColor;
            float scale = isCurrent ? progressCurrentScale : (isCompleted ? progressCompletedScale : 1f);
            node.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        for (int i = 0; i < _progressLinks.Length; i++)
        {
            RawImage link = _progressLinks[i];
            if (link == null)
                continue;

            link.color = i < activeIndex ? Color.Lerp(activeColor, Color.white, 0.12f) : pendingLinkColor;
        }
    }

    void PlaySweep(Color color, float maxAlpha)
    {
        if (_sweepImage == null)
            return;

        if (_sweepRoutine != null)
            StopCoroutine(_sweepRoutine);

        _sweepRoutine = StartCoroutine(CoPlaySweep(color, maxAlpha));
    }

    void ApplyIconStyle(LobbyManager.LobbyFlowPhase phase, Color color, bool animated)
    {
        EnsureIconVisuals();
        if (_iconRoot == null)
            return;

        _iconBackplate.color = new Color(color.r, color.g, color.b, 0.12f);
        ConfigureIconPart(_iconPrimary, color, Vector2.zero, Vector2.one, 0f, true);
        ConfigureIconPart(_iconSecondary, color, Vector2.zero, Vector2.one, 0f, true);
        ConfigureIconPart(_iconTertiary, color, Vector2.zero, Vector2.one, 0f, true);

        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Arrival:
                ConfigureIconPart(_iconPrimary, color, new Vector2(14f, 14f), new Vector2(12f, 12f), 45f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(16f, 5f), new Vector2(16f, 3f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(6f, 14f), new Vector2(4f, 4f), 0f, true);
                break;

            case LobbyManager.LobbyFlowPhase.Combat:
                ConfigureIconPart(_iconPrimary, color, new Vector2(15f, 15f), new Vector2(18f, 4f), 32f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(15f, 15f), new Vector2(18f, 4f), -32f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(15f, 15f), new Vector2(5f, 5f), 0f, true);
                break;

            case LobbyManager.LobbyFlowPhase.Route:
                ConfigureIconPart(_iconPrimary, color, new Vector2(18f, 14f), new Vector2(14f, 4f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(10f, 14f), new Vector2(10f, 10f), 45f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(8f, 14f), new Vector2(3f, 18f), 0f, true);
                break;

            case LobbyManager.LobbyFlowPhase.Board:
                ConfigureIconPart(_iconPrimary, color, new Vector2(11f, 14f), new Vector2(4f, 18f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(21f, 14f), new Vector2(4f, 18f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(16f, 14f), new Vector2(10f, 4f), 0f, true);
                break;

            case LobbyManager.LobbyFlowPhase.Elevator:
                ConfigureIconPart(_iconPrimary, color, new Vector2(16f, 16f), new Vector2(5f, 18f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(16f, 16f), new Vector2(18f, 5f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(16f, 16f), new Vector2(6f, 6f), 45f, true);
                break;
        }

        if (_iconPulseRoutine != null)
        {
            StopCoroutine(_iconPulseRoutine);
            _iconPulseRoutine = null;
        }

        _iconRoot.localScale = Vector3.one;
        if (animated)
            _iconPulseRoutine = StartCoroutine(CoPulseIcon());
    }

    void ConfigureIconPart(RawImage image, Color color, Vector2 position, Vector2 size, float rotationZ, bool visible)
    {
        if (image == null)
            return;

        image.enabled = visible;
        if (!visible)
            return;

        image.color = color;
        RectTransform rectTransform = image.rectTransform;
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(position.x, -position.y);
        rectTransform.sizeDelta = size;
        rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        rectTransform.localScale = Vector3.one;
    }

    IEnumerator CoPlaySweep(Color color, float maxAlpha)
    {
        float duration = Mathf.Max(0.05f, sweepDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float fade = 1f - EaseOutCubic(t);
            Color sweepColor = color;
            sweepColor.a = maxAlpha * fade;
            _sweepImage.color = sweepColor;
            yield return null;
        }

        _sweepImage.color = new Color(color.r, color.g, color.b, 0f);
        _sweepRoutine = null;
    }

    IEnumerator CoPulseIcon()
    {
        if (_iconRoot == null)
            yield break;

        float duration = Mathf.Max(0.05f, iconPulseDuration);
        float elapsed = 0f;
        Vector3 startScale = new Vector3(0.88f, 0.88f, 1f);
        Vector3 endScale = Vector3.one;
        _iconRoot.localScale = startScale;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _iconRoot.localScale = Vector3.LerpUnclamped(startScale, endScale, EaseOutCubic(t));
            yield return null;
        }

        _iconRoot.localScale = endScale;
        _iconPulseRoutine = null;
    }

    Color GetPhaseColor(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return combatColor;
            case LobbyManager.LobbyFlowPhase.Route:
                return routeColor;
            case LobbyManager.LobbyFlowPhase.Board:
                return boardColor;
            case LobbyManager.LobbyFlowPhase.Elevator:
                return elevatorColor;
            default:
                return arrivalColor;
        }
    }

    static int GetPhaseIndex(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return 1;
            case LobbyManager.LobbyFlowPhase.Route:
                return 2;
            case LobbyManager.LobbyFlowPhase.Board:
                return 3;
            case LobbyManager.LobbyFlowPhase.Elevator:
                return 4;
            default:
                return 0;
        }
    }

    static string GetPhaseCategory(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return "\uc2e4\uc804 \uad50\uc804 / COMBAT";
            case LobbyManager.LobbyFlowPhase.Route:
                return "\uc9c4\ub85c \ud655\ubcf4 / ROUTE";
            case LobbyManager.LobbyFlowPhase.Board:
                return "\ud0d1\uc2b9 \uc808\ucc28 / BOARD";
            case LobbyManager.LobbyFlowPhase.Elevator:
                return "\uce35\uac04 \uc5f0\ub3d9 / ACCESS";
            default:
                return "\uce68\ud22c \uc9c4\uc785 / BREACH";
        }
    }

    static string GetPhaseBadge(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return "ENGAGE";
            case LobbyManager.LobbyFlowPhase.Route:
                return "ADVANCE";
            case LobbyManager.LobbyFlowPhase.Board:
                return "BOARD";
            case LobbyManager.LobbyFlowPhase.Elevator:
                return "SYNC";
            default:
                return "LIVE";
        }
    }

    static string GetPhaseStatus(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return "\uc801 \uc2e0\ud638 \ucd94\uc801 \uc911";
            case LobbyManager.LobbyFlowPhase.Route:
                return "\uac1c\ubc29\ub41c \uacbd\ub85c\ub97c \ub530\ub77c \uc804\uc9c4";
            case LobbyManager.LobbyFlowPhase.Board:
                return "\uc2b9\uac15\uae30 \uc548\uc73c\ub85c \uc9c4\uc785";
            case LobbyManager.LobbyFlowPhase.Elevator:
                return "\ud328\ub110\uc744 \uc870\uc791\ud574 \ub2e4\uc74c \uce35\uc73c\ub85c \uc774\ub3d9";
            default:
                return "\uc2dc\ubbac\ub808\uc774\uc158 \ub9c1\ud06c \ud574\uc81c";
        }
    }

    static string GetSupportTitle(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return "\uc804\uc220 \ubcf4\uc870";
            case LobbyManager.LobbyFlowPhase.Route:
                return "\uacbd\ub85c \uc548\ub0b4";
            case LobbyManager.LobbyFlowPhase.Board:
                return "\ud0d1\uc2b9 \uc548\ub0b4";
            case LobbyManager.LobbyFlowPhase.Elevator:
                return "\uc2dc\uc2a4\ud15c \uc5f0\ub3d9";
            default:
                return "\ub9c1\ud06c \uc0c1\ud0dc";
        }
    }

    string GetSupportBody(LobbyManager.LobbyFlowPhase phase)
    {
        switch (phase)
        {
            case LobbyManager.LobbyFlowPhase.Combat:
                return "\uacbd\uace0\uc120\uacfc \ud53c\aca9 \ud53c\ub4dc\ubc31\uc744 \ud655\uc778\ud558\uace0 \uc0ac\uaca9 \uac01\uc744 \ub04a\uc5b4.";
            case LobbyManager.LobbyFlowPhase.Route:
                return "\uac1c\ubc29\ub41c \uacbd\ub85c\ub97c \ub530\ub77c \uc2b9\uac15\uae30\ub85c \uc804\uc9c4\ud574.";
            case LobbyManager.LobbyFlowPhase.Board:
                return "\uc2b9\uac15\uae30 \uc548\uc73c\ub85c \uc9c4\uc785\ud574 \ub2e4\uc74c \uce35 \ub9c1\ud06c\ub97c \uc5f4\uc5b4.";
            case LobbyManager.LobbyFlowPhase.Elevator:
                return string.IsNullOrWhiteSpace(_questDescription) ? GetPhaseStatus(phase) : _questDescription;
            default:
                return "\uc2dc\ubbac\ub808\uc774\uc158 \ub9c1\ud06c \ud574\uc81c \uc644\ub8cc. \uc804\ud22c \uc0c1\ud0dc\ub97c \uc900\ube44\ud574.";
        }
    }

    static RawImage EnsurePanelImage(Transform parent, string objectName, out RectTransform rectTransform)
    {
        Transform existing = parent.Find(objectName);
        RawImage image = existing != null ? existing.GetComponent<RawImage>() : null;
        if (image == null)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.transform.SetParent(parent, false);
            image = imageObject.GetComponent<RawImage>();
        }

        rectTransform = image.rectTransform;
        image.texture = Texture2D.whiteTexture;
        image.raycastTarget = false;
        return image;
    }

    static RawImage EnsureProgressImage(string objectName)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RawImage image = imageObject.GetComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    static TextMeshProUGUI EnsureText(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        TextMeshProUGUI text = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (text == null)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            text = textObject.GetComponent<TextMeshProUGUI>();
        }

        text.raycastTarget = false;
        text.enableWordWrapping = false;
        return text;
    }

    void HideImmediate()
    {
        if (_sweepRoutine != null)
        {
            StopCoroutine(_sweepRoutine);
            _sweepRoutine = null;
        }

        if (_iconPulseRoutine != null)
        {
            StopCoroutine(_iconPulseRoutine);
            _iconPulseRoutine = null;
        }

        if (_sweepImage != null)
            _sweepImage.color = new Color(1f, 1f, 1f, 0f);
        if (_iconRoot != null)
            _iconRoot.localScale = Vector3.one;
    }

    void DisableRuntimePanel()
    {
        ReleaseSubscriptions();
        HideImmediate();
        HideTransientCue();
        if (_runtimeRoot != null)
            _runtimeRoot.gameObject.SetActive(false);
        if (_cueGroup != null)
            _cueGroup.alpha = 0f;
        SetLegacyQuestTextVisible(true);
        SetLegacyQuestPanelVisible(true);
        enabled = false;
    }

    static void StretchToParent(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t -= 1f;
        return t * t * t + 1f;
    }
}
