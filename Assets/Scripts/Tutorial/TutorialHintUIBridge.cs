using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TutorialHintUIBridge : MonoBehaviour
{
    public event Action<string, string> HintChanged;

    [Header("Guide UI")]
    [SerializeField] private CanvasGroup dialogueGroup;
    [SerializeField] private TextMeshProUGUI speakerText;
    [SerializeField] private TextMeshProUGUI contentText;

    [Header("Hint UI")]
    [SerializeField] private CanvasGroup questPanelGroup;
    [SerializeField] private TextMeshProUGUI questTitleText;
    [SerializeField] private TextMeshProUGUI questDescriptionText;
    [SerializeField] private GameObject comboGuidePanel;
    [SerializeField] private TutorialComboGuideView comboGuideView;

    [Header("Runtime Layout")]
    [SerializeField] private Vector2 dialoguePanelAnchoredPosition = new Vector2(0f, 112f);
    [SerializeField] private Vector2 dialoguePanelSize = new Vector2(1280f, 180f);
    [SerializeField] private Vector2 objectivePanelAnchoredPosition = new Vector2(-28f, -28f);
    [SerializeField] private Vector2 objectivePanelSize = new Vector2(960f, 372f);
    [SerializeField] private Vector2 comboPanelAnchoredPosition = new Vector2(32f, -160f);
    [SerializeField] private Vector2 comboPanelSize = new Vector2(680f, 500f);

    [Header("Runtime Style")]
    [SerializeField] private Color dialoguePanelColor = new Color(0.03f, 0.07f, 0.10f, 0.88f);
    [SerializeField] private Color objectivePanelColor = new Color(0.03f, 0.07f, 0.10f, 0.88f);
    [SerializeField] private Color accentColor = new Color(0.22f, 0.86f, 1f, 0.96f);
    [SerializeField] private Color speakerColor = new Color(0.36f, 0.94f, 1f, 1f);
    [SerializeField] private Color guideBodyColor = new Color(0.90f, 0.97f, 1f, 0.96f);
    [SerializeField] private Color questTitleColor = new Color(0.88f, 0.97f, 1f, 0.98f);
    [SerializeField] private Color questBodyColor = new Color(0.82f, 0.92f, 0.98f, 0.96f);

    [Header("Behaviour")]
    [SerializeField] private string defaultSpeaker = "EGO";

    CanvasGroup _legacyDialogueGroup;
    CanvasGroup _legacyQuestPanelGroup;
    GameObject _legacyComboGuidePanel;
    Canvas _runtimeCanvas;
    RectTransform _runtimeHudRoot;
    CanvasGroup _runtimeHudGroup;
    TMP_FontAsset _preferredFontAsset;

    public CanvasGroup QuestPanelGroup => questPanelGroup;
    public TextMeshProUGUI QuestTitleText => questTitleText;
    public TextMeshProUGUI QuestDescriptionText => questDescriptionText;

    public void BindLegacy(TutorialManager legacyManager)
    {
        if (legacyManager == null)
            return;

        _legacyDialogueGroup = legacyManager.dialogueGroup;
        _legacyQuestPanelGroup = legacyManager.questPanelGroup;
        _legacyComboGuidePanel = legacyManager.comboGuidePanel;
        _preferredFontAsset = ResolvePreferredFontAsset(
            legacyManager.speakerText,
            legacyManager.contentText,
            legacyManager.questTitleText,
            legacyManager.questDescriptionText);

        dialogueGroup = _legacyDialogueGroup;
        speakerText = legacyManager.speakerText;
        contentText = legacyManager.contentText;
        questPanelGroup = _legacyQuestPanelGroup;
        questTitleText = legacyManager.questTitleText;
        questDescriptionText = legacyManager.questDescriptionText;
        comboGuidePanel = _legacyComboGuidePanel;

        HideLegacyUi();
        EnsureRuntimeHud();

        if (questPanelGroup != null)
        {
            questPanelGroup.interactable = false;
            questPanelGroup.blocksRaycasts = false;
            questPanelGroup.alpha = 1f;
        }

        if (comboGuidePanel != null)
        {
            comboGuideView = comboGuidePanel.GetComponent<TutorialComboGuideView>();
            if (comboGuideView == null)
                comboGuideView = comboGuidePanel.AddComponent<TutorialComboGuideView>();

            comboGuideView.ConfigureRuntime();
            comboGuidePanel.SetActive(false);
        }
    }

    public void SetOverlayVisible(bool visible)
    {
        if (_runtimeHudGroup == null)
            return;

        _runtimeHudGroup.alpha = visible ? 1f : 0f;
        _runtimeHudGroup.interactable = false;
        _runtimeHudGroup.blocksRaycasts = false;
    }

    public void ShowHint(string line1, string line2 = null)
    {
        EnsureRuntimeHud();

        if (questPanelGroup != null)
            questPanelGroup.alpha = 1f;

        string resolvedTitle = string.IsNullOrWhiteSpace(line1) ? "목표" : line1;
        string resolvedDescription = string.IsNullOrWhiteSpace(line2)
            ? (string.IsNullOrWhiteSpace(line1) ? string.Empty : line1)
            : line2;

        if (questTitleText != null)
            questTitleText.text = resolvedTitle;

        if (questDescriptionText != null)
            questDescriptionText.text = resolvedDescription;

        HintChanged?.Invoke(resolvedTitle, resolvedDescription);
    }

    public void ShowGuideText(string body, string speaker = null)
    {
        EnsureRuntimeHud();

        if (dialogueGroup != null)
            dialogueGroup.alpha = string.IsNullOrWhiteSpace(body) ? 0f : 1f;

        if (speakerText != null)
            speakerText.text = string.IsNullOrWhiteSpace(speaker) ? defaultSpeaker : speaker;

        if (contentText != null)
            contentText.text = body ?? string.Empty;
    }

    public void HideGuideText()
    {
        if (dialogueGroup != null)
            dialogueGroup.alpha = 0f;
    }

    public void ShowStepCompleted(string stepTitle)
    {
        if (questTitleText != null && !string.IsNullOrWhiteSpace(stepTitle))
            questTitleText.text = "완료 · " + stepTitle;

        HintChanged?.Invoke(
            questTitleText != null ? questTitleText.text : string.Empty,
            questDescriptionText != null ? questDescriptionText.text : string.Empty);
    }

    public void ShowComboGuide(string title, string body)
    {
        EnsureRuntimeHud();
        comboGuideView?.Show(title, body);
    }

    public void UpdateComboGuideStatus(string status)
    {
        comboGuideView?.SetStatus(status);
    }

    public void ClearComboGuideStatus()
    {
        comboGuideView?.SetStatus(string.Empty);
    }

    public void HideComboGuide()
    {
        comboGuideView?.Hide();
    }

    void HideLegacyUi()
    {
        if (_legacyDialogueGroup != null)
        {
            _legacyDialogueGroup.alpha = 0f;
            _legacyDialogueGroup.interactable = false;
            _legacyDialogueGroup.blocksRaycasts = false;
        }

        if (_legacyQuestPanelGroup != null)
        {
            _legacyQuestPanelGroup.alpha = 0f;
            _legacyQuestPanelGroup.interactable = false;
            _legacyQuestPanelGroup.blocksRaycasts = false;
        }

        if (_legacyComboGuidePanel != null)
            _legacyComboGuidePanel.SetActive(false);
    }

    void EnsureRuntimeHud()
    {
        if (_runtimeHudRoot != null)
            return;

        _runtimeCanvas = ResolveRuntimeCanvas();
        if (_runtimeCanvas == null)
            return;

        GameObject rootObject = new GameObject("TutorialRuntimeHUD", typeof(RectTransform), typeof(CanvasGroup));
        _runtimeHudRoot = rootObject.GetComponent<RectTransform>();
        _runtimeHudRoot.SetParent(_runtimeCanvas.transform, false);
        StretchToParent(_runtimeHudRoot);
        _runtimeHudRoot.SetAsLastSibling();

        _runtimeHudGroup = rootObject.GetComponent<CanvasGroup>();
        _runtimeHudGroup.alpha = 1f;
        _runtimeHudGroup.interactable = false;
        _runtimeHudGroup.blocksRaycasts = false;

        CreateDialoguePanel();
        CreateObjectivePanel();
        CreateComboPanel();
    }

    void CreateDialoguePanel()
    {
        CanvasGroup panelGroup = CreateCanvasGroup(
            "DialoguePanel",
            _runtimeHudRoot,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            dialoguePanelAnchoredPosition,
            dialoguePanelSize);
        panelGroup.alpha = 0f;

        dialogueGroup = panelGroup;
        speakerText = EnsureText(panelGroup.transform, "Speaker");
        ApplyTextStyle(speakerText, 24f, FontStyles.Bold, speakerColor, TextAlignmentOptions.Bottom);
        ConfigureRect(
            speakerText.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -8f),
            new Vector2(dialoguePanelSize.x, 28f));

        contentText = EnsureText(panelGroup.transform, "Content");
        ApplyTextStyle(contentText, 30f, FontStyles.Bold, guideBodyColor, TextAlignmentOptions.Top);
        contentText.enableWordWrapping = true;
        contentText.richText = true;
        ConfigureRect(
            contentText.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 20f),
            new Vector2(dialoguePanelSize.x, 112f));
    }

    void CreateObjectivePanel()
    {
        CanvasGroup panelGroup = CreatePanelGroup(
            "ObjectivePanel",
            _runtimeHudRoot,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            objectivePanelAnchoredPosition,
            objectivePanelSize,
            objectivePanelColor);
        panelGroup.alpha = 1f;

        EnsureAccent(panelGroup.transform, "Accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 8f));

        questPanelGroup = panelGroup;
        questTitleText = EnsureText(panelGroup.transform, "QuestTitle");
        ApplyTextStyle(questTitleText, 46f, FontStyles.Bold, questTitleColor, TextAlignmentOptions.TopLeft);
        ConfigureRect(questTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(32f, -92f), new Vector2(objectivePanelSize.x - 64f, 52f));

        questDescriptionText = EnsureText(panelGroup.transform, "QuestDescription");
        ApplyTextStyle(questDescriptionText, 30f, FontStyles.Normal, questBodyColor, TextAlignmentOptions.TopLeft);
        questDescriptionText.enableWordWrapping = true;
        ConfigureRect(questDescriptionText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(32f, -172f), new Vector2(objectivePanelSize.x - 64f, 176f));
    }

    void CreateComboPanel()
    {
        GameObject panelObject = new GameObject("ComboPanel", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.SetParent(_runtimeHudRoot, false);
        ConfigureRect(rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), comboPanelAnchoredPosition, comboPanelSize);

        CanvasGroup panelGroup = panelObject.GetComponent<CanvasGroup>();
        panelGroup.alpha = 1f;
        panelGroup.interactable = false;
        panelGroup.blocksRaycasts = false;

        comboGuidePanel = panelObject;
    }

    Canvas ResolveRuntimeCanvas()
    {
        if (_runtimeCanvas != null)
            return _runtimeCanvas;

        if (_legacyDialogueGroup != null)
            _runtimeCanvas = _legacyDialogueGroup.GetComponentInParent<Canvas>();

        if (_runtimeCanvas == null && _legacyQuestPanelGroup != null)
            _runtimeCanvas = _legacyQuestPanelGroup.GetComponentInParent<Canvas>();

        if (_runtimeCanvas == null)
            _runtimeCanvas = FindObjectOfType<Canvas>(true);

        return _runtimeCanvas;
    }

    TMP_FontAsset ResolvePreferredFontAsset(params TextMeshProUGUI[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null && candidates[i].font != null)
                return candidates[i].font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    CanvasGroup CreatePanelGroup(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        Color backgroundColor)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        ConfigureRect(rectTransform, anchorMin, anchorMax, pivot, anchoredPosition, size);

        Image background = panelObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;

        CanvasGroup group = panelObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    CanvasGroup CreateCanvasGroup(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        ConfigureRect(rectTransform, anchorMin, anchorMax, pivot, anchoredPosition, size);

        CanvasGroup group = panelObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    void EnsureAccent(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject accentObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rectTransform = accentObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        ConfigureRect(rectTransform, anchorMin, anchorMax, pivot, anchoredPosition, size);

        Image accentImage = accentObject.GetComponent<Image>();
        accentImage.color = accentColor;
        accentImage.raycastTarget = false;
    }

    TextMeshProUGUI EnsureText(Transform parent, string objectName)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        if (_preferredFontAsset != null)
            text.font = _preferredFontAsset;
        return text;
    }

    void ApplyTextStyle(TextMeshProUGUI text, float fontSize, FontStyles fontStyle, Color color, TextAlignmentOptions alignment)
    {
        if (text == null)
            return;

        if (_preferredFontAsset != null)
            text.font = _preferredFontAsset;

        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.richText = true;
    }

    static void ConfigureRect(RectTransform rectTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
        rectTransform.localScale = Vector3.one;
    }

    static void ConfigureStretchRect(RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
        rectTransform.localScale = Vector3.one;
    }

    static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }
}
