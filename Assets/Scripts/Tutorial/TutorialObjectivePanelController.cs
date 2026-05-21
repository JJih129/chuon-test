using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TutorialObjectivePanelController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private TutorialHintUIBridge hintBridge;

    [Header("Style")]
    [SerializeField] private Vector2 iconAnchoredPosition = new Vector2(20f, -18f);
    [SerializeField] private Vector2 iconSize = new Vector2(32f, 32f);
    [SerializeField] private Vector2 categoryAnchoredPosition = new Vector2(62f, -16f);
    [SerializeField] private Vector2 counterAnchoredPosition = new Vector2(-24f, -16f);
    [SerializeField] private Vector2 badgeAnchoredPosition = new Vector2(-150f, -14f);
    [SerializeField] private Vector2 badgeSize = new Vector2(112f, 26f);
    [SerializeField] private Vector2 titleAnchoredPosition = new Vector2(62f, -52f);
    [SerializeField] private Vector2 titleSize = new Vector2(500f, 42f);
    [SerializeField] private Vector2 statusAnchoredPosition = new Vector2(62f, -100f);
    [SerializeField] private Vector2 statusSize = new Vector2(500f, 70f);
    [SerializeField] private Vector2 progressAnchoredMin = new Vector2(24f, 14f);
    [SerializeField] private Vector2 progressAnchoredMax = new Vector2(-24f, 34f);
    [SerializeField] private float categoryFontSize = 20f;
    [SerializeField] private float titleFontSize = 30f;
    [SerializeField] private float counterFontSize = 20f;
    [SerializeField] private float statusFontSize = 23f;
    [SerializeField] private float badgeFontSize = 15f;
    [SerializeField] private float accentHeight = 8f;
    [SerializeField] private float progressNodeSize = 14f;
    [SerializeField] private float progressCurrentNodeScale = 1.35f;
    [SerializeField] private float progressCompletedNodeScale = 1.10f;
    [SerializeField] private float progressLinkThickness = 4f;
    [SerializeField] private float sweepDuration = 0.26f;
    [SerializeField] private float iconPulseDuration = 0.18f;

    [Header("Colors")]
    [SerializeField] private Color movementColor = new Color(0.22f, 0.86f, 1f, 0.96f);
    [SerializeField] private Color focusColor = new Color(0.44f, 1f, 1f, 0.96f);
    [SerializeField] private Color attackColor = new Color(1f, 0.78f, 0.24f, 0.96f);
    [SerializeField] private Color defenseColor = new Color(1f, 0.42f, 0.18f, 0.98f);
    [SerializeField] private Color supportColor = new Color(0.42f, 1f, 0.62f, 0.96f);
    [SerializeField] private Color exitColor = new Color(0.86f, 0.92f, 1f, 0.96f);
    [SerializeField] private Color bodyTextColor = new Color(0.90f, 0.97f, 1f, 0.96f);
    [SerializeField] private Color pendingNodeColor = new Color(0.30f, 0.40f, 0.48f, 0.58f);
    [SerializeField] private Color pendingLinkColor = new Color(0.22f, 0.30f, 0.38f, 0.32f);

    RectTransform _runtimeRoot;
    RawImage _accentImage;
    RawImage _sweepImage;
    RectTransform _iconRoot;
    RawImage _iconBackplate;
    RawImage _iconPrimary;
    RawImage _iconSecondary;
    RawImage _iconTertiary;
    TextMeshProUGUI _categoryText;
    TextMeshProUGUI _titleText;
    TextMeshProUGUI _counterText;
    RectTransform _badgeRoot;
    RawImage _badgeBackground;
    TextMeshProUGUI _badgeText;
    TextMeshProUGUI _statusText;
    RectTransform _progressRoot;
    RawImage[] _progressNodes;
    RawImage[] _progressLinks;
    Coroutine _sweepRoutine;
    Coroutine _iconPulseRoutine;
    bool _subscribed;
    string _bridgeHintTitle = string.Empty;
    string _bridgeHintBody = string.Empty;

    public void ConfigureRuntime(TutorialFlowController runtimeFlowController, TutorialHintUIBridge runtimeHintBridge)
    {
        flowController = runtimeFlowController;
        hintBridge = runtimeHintBridge;

        EnsureRuntimeVisuals();
        RefreshSubscriptions();
        ApplyStep(flowController != null ? flowController.CurrentStep : null, false);
    }

    void OnEnable()
    {
        EnsureRuntimeVisuals();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        HideImmediate();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        SyncBridgeHintTextVisibility(true);
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();
        if (flowController == null)
        {
            if (hintBridge != null)
            {
                hintBridge.HintChanged += HandleHintChanged;
                _subscribed = true;
            }
            return;
        }

        flowController.StepStarted += HandleStepStarted;
        flowController.StepCompleted += HandleStepCompleted;
        flowController.StepProgressUpdated += HandleStepProgressUpdated;
        if (hintBridge != null)
            hintBridge.HintChanged += HandleHintChanged;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed)
            return;

        if (flowController != null)
        {
            flowController.StepStarted -= HandleStepStarted;
            flowController.StepCompleted -= HandleStepCompleted;
            flowController.StepProgressUpdated -= HandleStepProgressUpdated;
        }

        if (hintBridge != null)
            hintBridge.HintChanged -= HandleHintChanged;

        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        ApplyStep(step, true);
    }

    void HandleHintChanged(string title, string body)
    {
        _bridgeHintTitle = title ?? string.Empty;
        _bridgeHintBody = body ?? string.Empty;

        TutorialStepDefinition currentStep = flowController != null ? flowController.CurrentStep : null;
        if (currentStep == null)
            return;

        if (_titleText != null)
            _titleText.text = ResolveHintTitle(currentStep);

        if (_statusText != null)
            _statusText.text = ResolveHintBody(currentStep);
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null)
            return;

        EnsureRuntimeVisuals();
        if (_counterText != null)
            _counterText.text = "\uc644\ub8cc";

        if (_titleText != null)
            _titleText.text = ResolveHintTitle(step);

        if (_statusText != null)
        {
            _statusText.text = "\ud604\uc7ac \ub2e8\uacc4 \uc644\ub8cc";
            _statusText.color = Color.Lerp(bodyTextColor, GetStepColor(step.stepType), 0.42f);
        }

        SetBadgeText(step.stepType, true, GetStepColor(step.stepType));
        UpdateProgressVisuals(flowController != null ? flowController.CurrentStepIndex : -1, true, GetStepColor(step.stepType));
        PlaySweep(GetStepColor(step.stepType), 0.16f);
    }

    void HandleStepProgressUpdated(TutorialStepDefinition step, string progressText)
    {
        if (step == null)
            return;

        EnsureRuntimeVisuals();
        if (_statusText == null)
            return;

        _statusText.text = string.IsNullOrWhiteSpace(progressText) ? ResolveHintBody(step) : progressText;
        _statusText.color = Color.Lerp(bodyTextColor, GetStepColor(step.stepType), 0.42f);
    }

    void ApplyStep(TutorialStepDefinition step, bool playSweep)
    {
        EnsureRuntimeVisuals();
        if (step == null || hintBridge == null)
            return;

        Color color = GetStepColor(step.stepType);
        string category = GetStepCategory(step.stepType);
        int currentStepDisplay = Mathf.Max(1, flowController != null ? flowController.CurrentStepIndex + 1 : 1);
        int stepCount = Mathf.Max(1, flowController != null ? flowController.StepCount : 1);

        if (_categoryText != null)
        {
            _categoryText.text = category;
            _categoryText.color = color;
        }

        if (_titleText != null)
        {
            _titleText.text = ResolveHintTitle(step);
            _titleText.color = bodyTextColor;
        }

        if (_counterText != null)
        {
            _counterText.text = currentStepDisplay.ToString("00") + " / " + stepCount.ToString("00");
            _counterText.color = color;
        }

        if (_accentImage != null)
            _accentImage.color = color;

        ApplyIconStyle(step.stepType, color);
        SetBadgeText(step.stepType, false, color);

        if (_statusText != null)
        {
            _statusText.text = ResolveHintBody(step);
            _statusText.color = bodyTextColor;
        }

        EnsureProgressVisuals();
        UpdateProgressVisuals(currentStepDisplay - 1, false, color);

        if (playSweep)
            PlaySweep(color, 0.22f);
    }

    void EnsureRuntimeVisuals()
    {
        CanvasGroup questPanel = hintBridge != null ? hintBridge.QuestPanelGroup : null;
        if (questPanel == null)
            return;

        if (hintBridge != null)
        {
            _bridgeHintTitle = hintBridge.QuestTitleText != null ? hintBridge.QuestTitleText.text : _bridgeHintTitle;
            _bridgeHintBody = hintBridge.QuestDescriptionText != null ? hintBridge.QuestDescriptionText.text : _bridgeHintBody;
        }

        if (_runtimeRoot == null)
        {
            Transform existing = questPanel.transform.Find("TutorialObjectiveRuntime");
            if (existing != null)
                _runtimeRoot = existing as RectTransform;

            if (_runtimeRoot == null)
            {
                GameObject rootObject = new GameObject("TutorialObjectiveRuntime", typeof(RectTransform));
                _runtimeRoot = rootObject.GetComponent<RectTransform>();
                _runtimeRoot.SetParent(questPanel.transform, false);
            }

            StretchToParent(_runtimeRoot);
            _runtimeRoot.SetAsLastSibling();
        }

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
        categoryRect.sizeDelta = new Vector2(300f, 30f);
        _categoryText.alignment = TextAlignmentOptions.Left;
        _categoryText.fontSize = categoryFontSize;
        _categoryText.fontStyle = FontStyles.Bold;
        _categoryText.raycastTarget = false;

        _titleText = EnsureText(_runtimeRoot, "Title");
        RectTransform titleRect = _titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = titleAnchoredPosition;
        titleRect.sizeDelta = titleSize;
        _titleText.alignment = TextAlignmentOptions.Left;
        _titleText.fontSize = titleFontSize;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.enableWordWrapping = true;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;
        _titleText.extraPadding = true;
        _titleText.lineSpacing = 2f;
        _titleText.raycastTarget = false;

        _counterText = EnsureText(_runtimeRoot, "Counter");
        RectTransform counterRect = _counterText.rectTransform;
        counterRect.anchorMin = new Vector2(1f, 1f);
        counterRect.anchorMax = new Vector2(1f, 1f);
        counterRect.pivot = new Vector2(1f, 1f);
        counterRect.anchoredPosition = counterAnchoredPosition;
        counterRect.sizeDelta = new Vector2(150f, 30f);
        _counterText.alignment = TextAlignmentOptions.Right;
        _counterText.fontSize = counterFontSize;
        _counterText.fontStyle = FontStyles.Bold;
        _counterText.raycastTarget = false;

        EnsureBadgeVisuals();

        _statusText = EnsureText(_runtimeRoot, "Status");
        RectTransform statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(0f, 1f);
        statusRect.pivot = new Vector2(0f, 1f);
        statusRect.anchoredPosition = statusAnchoredPosition;
        statusRect.sizeDelta = statusSize;
        _statusText.alignment = TextAlignmentOptions.Left;
        _statusText.fontSize = statusFontSize;
        _statusText.fontStyle = FontStyles.Normal;
        _statusText.enableWordWrapping = true;
        _statusText.overflowMode = TextOverflowModes.Ellipsis;
        _statusText.extraPadding = true;
        _statusText.lineSpacing = 4f;
        _statusText.raycastTarget = false;

        EnsureProgressVisuals();
        SyncBridgeHintTextVisibility(false);
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

    void EnsureBadgeVisuals()
    {
        if (_runtimeRoot == null)
            return;

        if (_badgeRoot == null)
        {
            Transform existing = _runtimeRoot.Find("Badge");
            if (existing != null)
                _badgeRoot = existing as RectTransform;

            if (_badgeRoot == null)
            {
                GameObject rootObject = new GameObject("Badge", typeof(RectTransform));
                _badgeRoot = rootObject.GetComponent<RectTransform>();
                _badgeRoot.SetParent(_runtimeRoot, false);
            }
        }

        _badgeRoot.anchorMin = new Vector2(1f, 1f);
        _badgeRoot.anchorMax = new Vector2(1f, 1f);
        _badgeRoot.pivot = new Vector2(1f, 1f);
        _badgeRoot.anchoredPosition = badgeAnchoredPosition;
        _badgeRoot.sizeDelta = badgeSize;
        _badgeRoot.localScale = Vector3.one;

        _badgeBackground = EnsurePanelImage(_badgeRoot, "Background", out RectTransform badgeRect);
        StretchToParent(badgeRect);

        _badgeText = EnsureText(_badgeRoot, "Label");
        RectTransform badgeTextRect = _badgeText.rectTransform;
        StretchToParent(badgeTextRect);
        badgeTextRect.offsetMin = new Vector2(8f, 2f);
        badgeTextRect.offsetMax = new Vector2(-8f, -2f);
        _badgeText.alignment = TextAlignmentOptions.Center;
        _badgeText.fontSize = badgeFontSize;
        _badgeText.fontStyle = FontStyles.Bold;
        _badgeText.raycastTarget = false;
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
                GameObject rootObject = new GameObject("Progress", typeof(RectTransform));
                _progressRoot = rootObject.GetComponent<RectTransform>();
                _progressRoot.SetParent(_runtimeRoot, false);
            }
        }

        _progressRoot.anchorMin = new Vector2(0f, 0f);
        _progressRoot.anchorMax = new Vector2(1f, 0f);
        _progressRoot.pivot = new Vector2(0.5f, 0f);
        _progressRoot.offsetMin = progressAnchoredMin;
        _progressRoot.offsetMax = progressAnchoredMax;
        _progressRoot.localScale = Vector3.one;

        int requiredNodeCount = Mathf.Max(1, flowController != null ? flowController.StepCount : 1);
        if (_progressNodes != null && _progressNodes.Length == requiredNodeCount && _progressLinks != null && _progressLinks.Length == Mathf.Max(0, requiredNodeCount - 1))
            return;

        RebuildProgressVisuals(requiredNodeCount);
    }

    void RebuildProgressVisuals(int nodeCount)
    {
        if (_progressRoot == null)
            return;

        for (int i = _progressRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = _progressRoot.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }

        _progressNodes = new RawImage[nodeCount];
        _progressLinks = new RawImage[Mathf.Max(0, nodeCount - 1)];

        for (int i = 0; i < nodeCount; i++)
        {
            _progressNodes[i] = EnsureProgressImage("Node_" + i);
            _progressNodes[i].rectTransform.SetParent(_progressRoot, false);
            _progressNodes[i].rectTransform.anchorMin = new Vector2(nodeCount <= 1 ? 0.5f : (float)i / (nodeCount - 1), 0.5f);
            _progressNodes[i].rectTransform.anchorMax = _progressNodes[i].rectTransform.anchorMin;
            _progressNodes[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _progressNodes[i].rectTransform.anchoredPosition = Vector2.zero;
            _progressNodes[i].rectTransform.sizeDelta = new Vector2(progressNodeSize, progressNodeSize);
        }

        for (int i = 0; i < _progressLinks.Length; i++)
        {
            RawImage link = EnsureProgressImage("Link_" + i);
            _progressLinks[i] = link;
            RectTransform linkRect = link.rectTransform;
            linkRect.SetParent(_progressRoot, false);
            float startAnchor = nodeCount <= 1 ? 0.5f : (float)i / (nodeCount - 1);
            float endAnchor = nodeCount <= 1 ? 0.5f : (float)(i + 1) / (nodeCount - 1);
            linkRect.anchorMin = new Vector2(startAnchor, 0.5f);
            linkRect.anchorMax = new Vector2(endAnchor, 0.5f);
            linkRect.pivot = new Vector2(0.5f, 0.5f);
            float horizontalPadding = progressNodeSize * 0.55f;
            linkRect.offsetMin = new Vector2(horizontalPadding, -progressLinkThickness * 0.5f);
            linkRect.offsetMax = new Vector2(-horizontalPadding, progressLinkThickness * 0.5f);
        }
    }

    void UpdateProgressVisuals(int activeIndex, bool treatActiveAsCompleted, Color activeColor)
    {
        if (_progressNodes == null || _progressNodes.Length == 0)
            return;

        int clampedIndex = Mathf.Clamp(activeIndex, 0, _progressNodes.Length - 1);
        for (int i = 0; i < _progressNodes.Length; i++)
        {
            RawImage node = _progressNodes[i];
            if (node == null)
                continue;

            bool isCompleted = i < clampedIndex || (treatActiveAsCompleted && i == clampedIndex);
            bool isCurrent = !treatActiveAsCompleted && i == clampedIndex;
            Color nodeColor = isCompleted || isCurrent ? activeColor : pendingNodeColor;
            float nodeScale = isCurrent
                ? progressCurrentNodeScale
                : (isCompleted ? progressCompletedNodeScale : 1f);

            node.color = nodeColor;
            node.rectTransform.localScale = new Vector3(nodeScale, nodeScale, 1f);
        }

        for (int i = 0; i < _progressLinks.Length; i++)
        {
            RawImage link = _progressLinks[i];
            if (link == null)
                continue;

            bool isCompletedLink = i < clampedIndex || (treatActiveAsCompleted && i == clampedIndex);
            link.color = isCompletedLink ? new Color(activeColor.r, activeColor.g, activeColor.b, activeColor.a * 0.72f) : pendingLinkColor;
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

    void ApplyIconStyle(TutorialStepType stepType, Color color)
    {
        EnsureIconVisuals();
        if (_iconBackplate == null || _iconPrimary == null || _iconSecondary == null || _iconTertiary == null)
            return;

        _iconBackplate.color = new Color(color.r, color.g, color.b, 0.12f);
        ConfigureIconPart(_iconPrimary, color, Vector2.zero, Vector2.one, 0f, true);
        ConfigureIconPart(_iconSecondary, color, Vector2.zero, Vector2.one, 0f, true);
        ConfigureIconPart(_iconTertiary, color, Vector2.zero, Vector2.one, 0f, true);

        switch (stepType)
        {
            case TutorialStepType.Movement:
                ConfigureIconPart(_iconPrimary, color, new Vector2(18f, 4f), new Vector2(13f, 14f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(7f, 7f), new Vector2(3f, 14f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(6f, 6f), new Vector2(4f, 4f), 45f, true);
                break;

            case TutorialStepType.CameraFocus:
            case TutorialStepType.LockOn:
                ConfigureIconPart(_iconPrimary, color, new Vector2(16f, 16f), new Vector2(12f, 12f), 45f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(16f, 16f), new Vector2(4f, 4f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(16f, 4f), new Vector2(14f, 2f), 0f, true);
                break;

            case TutorialStepType.BasicAttack:
            case TutorialStepType.Combo:
                ConfigureIconPart(_iconPrimary, color, new Vector2(16f, 16f), new Vector2(18f, 4f), 28f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(16f, 16f), new Vector2(18f, 4f), -28f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(16f, 16f), new Vector2(5f, 5f), 0f, true);
                break;

            case TutorialStepType.Guard:
            case TutorialStepType.Parry:
            case TutorialStepType.Dodge:
            case TutorialStepType.PerfectDodge:
                ConfigureIconPart(_iconPrimary, color, new Vector2(16f, 14f), new Vector2(6f, 18f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(16f, 8f), new Vector2(16f, 4f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(16f, 22f), new Vector2(10f, 4f), 0f, true);
                break;

            case TutorialStepType.Heal:
            case TutorialStepType.Ultimate:
                ConfigureIconPart(_iconPrimary, color, new Vector2(16f, 16f), new Vector2(5f, 18f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(16f, 16f), new Vector2(18f, 5f), 0f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(16f, 16f), new Vector2(6f, 6f), 45f, true);
                break;

            case TutorialStepType.Exit:
                ConfigureIconPart(_iconPrimary, color, new Vector2(20f, 10f), new Vector2(12f, 4f), 32f, true);
                ConfigureIconPart(_iconSecondary, color, new Vector2(20f, 18f), new Vector2(12f, 4f), -32f, true);
                ConfigureIconPart(_iconTertiary, color, new Vector2(8f, 14f), new Vector2(10f, 4f), 0f, true);
                break;

            default:
                ConfigureIconPart(_iconPrimary, color, new Vector2(16f, 16f), new Vector2(10f, 10f), 0f, true);
                ConfigureIconPart(_iconSecondary, color, Vector2.zero, Vector2.zero, 0f, false);
                ConfigureIconPart(_iconTertiary, color, Vector2.zero, Vector2.zero, 0f, false);
                break;
        }

        if (_iconPulseRoutine != null)
            StopCoroutine(_iconPulseRoutine);
        _iconPulseRoutine = StartCoroutine(CoPulseIcon());
    }

    void SetBadgeText(TutorialStepType stepType, bool completed, Color color)
    {
        EnsureBadgeVisuals();
        if (_badgeBackground == null || _badgeText == null)
            return;

        string badgeText;
        if (completed)
            badgeText = "SYNCED";
        else if (stepType == TutorialStepType.Exit)
            badgeText = "RETURN";
        else
            badgeText = "VR SIM";

        _badgeText.text = badgeText;
        _badgeText.color = color;
        _badgeBackground.color = new Color(color.r, color.g, color.b, completed ? 0.18f : 0.12f);
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
        if (_statusText != null)
            _statusText.text = string.Empty;
        SyncBridgeHintTextVisibility(true);
    }

    string ResolveHintTitle(TutorialStepDefinition step)
    {
        if (step == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(_bridgeHintTitle))
            return _bridgeHintTitle;

        if (!string.IsNullOrWhiteSpace(step.hintLine1))
            return step.hintLine1;

        return GetStepCategory(step.stepType);
    }

    string ResolveHintBody(TutorialStepDefinition step)
    {
        if (step == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(_bridgeHintBody))
            return _bridgeHintBody;

        string currentProgressText = flowController != null ? flowController.CurrentStepProgressText : string.Empty;
        if (!string.IsNullOrWhiteSpace(step.hintLine2))
        {
            if (!string.IsNullOrWhiteSpace(currentProgressText) && currentProgressText != "\uc644\ub8cc")
                return $"{step.hintLine2}\n{currentProgressText}";

            return step.hintLine2;
        }

        if (!string.IsNullOrWhiteSpace(currentProgressText) && currentProgressText != "\uc644\ub8cc")
            return currentProgressText;

        return string.Empty;
    }

    void SyncBridgeHintTextVisibility(bool visible)
    {
        if (hintBridge == null)
            return;

        TextMeshProUGUI questTitle = hintBridge.QuestTitleText;
        if (questTitle != null)
            questTitle.enabled = visible;

        TextMeshProUGUI questDescription = hintBridge.QuestDescriptionText;
        if (questDescription != null)
            questDescription.enabled = visible;
    }

    Color GetStepColor(TutorialStepType stepType)
    {
        switch (stepType)
        {
            case TutorialStepType.Movement:
                return movementColor;

            case TutorialStepType.CameraFocus:
            case TutorialStepType.LockOn:
                return focusColor;

            case TutorialStepType.BasicAttack:
            case TutorialStepType.Combo:
                return attackColor;

            case TutorialStepType.Guard:
            case TutorialStepType.Parry:
            case TutorialStepType.Dodge:
            case TutorialStepType.PerfectDodge:
                return defenseColor;

            case TutorialStepType.Heal:
            case TutorialStepType.Ultimate:
                return supportColor;

            case TutorialStepType.Exit:
                return exitColor;

            default:
                return movementColor;
        }
    }

    string GetStepCategory(TutorialStepType stepType)
    {
        switch (stepType)
        {
            case TutorialStepType.Movement:
                return "\uae30\ub3d9 \ud6c8\ub828";

            case TutorialStepType.CameraFocus:
            case TutorialStepType.LockOn:
                return "\uc2dc\uc57c \uc778\uc2dd";

            case TutorialStepType.BasicAttack:
            case TutorialStepType.Combo:
                return "\uacf5\uaca9 \ud6c8\ub828";

            case TutorialStepType.Guard:
            case TutorialStepType.Parry:
            case TutorialStepType.Dodge:
            case TutorialStepType.PerfectDodge:
                return "\ubc29\uc5b4 \ud6c8\ub828";

            case TutorialStepType.Heal:
            case TutorialStepType.Ultimate:
                return "\uc9c0\uc6d0 \uc6b4\uc6a9";

            case TutorialStepType.Exit:
                return "\ubcf5\uadc0 \uc808\ucc28";

            default:
                return "\ud6c8\ub828";
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
        image.texture = Texture2D.whiteTexture;
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

        return text;
    }

    static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t -= 1f;
        return t * t * t + 1f;
    }
}
