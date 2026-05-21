using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

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
    [SerializeField] private Vector2 objectivePanelAnchoredPosition = new Vector2(-350f, 360f);
    [SerializeField] private Vector2 objectivePanelSize = new Vector2(620f, 190f);
    [SerializeField] private Vector2 comboPanelAnchoredPosition = new Vector2(32f, -160f);
    [SerializeField] private Vector2 comboPanelSize = new Vector2(680f, 500f);
    [SerializeField] private Vector2 timingCuePanelAnchoredPosition = Vector2.zero;
    [SerializeField] private Vector2 timingCuePanelSize = new Vector2(280f, 280f);
    [SerializeField] private Vector2 keyCueSize = new Vector2(220f, 220f);

    [Header("Runtime Style")]
    [SerializeField] private Color dialoguePanelColor = new Color(0.03f, 0.07f, 0.10f, 0.88f);
    [SerializeField] private Color objectivePanelColor = new Color(0.03f, 0.07f, 0.10f, 0.88f);
    [SerializeField] private Color timingCuePanelColor = new Color(0.02f, 0.05f, 0.07f, 0.92f);
    [SerializeField] private Color timingCueTextColor = new Color(0.96f, 1f, 1f, 1f);
    [SerializeField] private Color accentColor = new Color(0.22f, 0.86f, 1f, 0.96f);
    [SerializeField] private Color speakerColor = new Color(0.36f, 0.94f, 1f, 1f);
    [SerializeField] private Color guideBodyColor = new Color(0.90f, 0.97f, 1f, 0.96f);
    [SerializeField] private Color questTitleColor = new Color(0.88f, 0.97f, 1f, 0.98f);
    [SerializeField] private Color questBodyColor = new Color(0.82f, 0.92f, 0.98f, 0.96f);
    [SerializeField] private Color keyCueGlowColor = new Color(0.22f, 0.92f, 1f, 0.42f);

    [Header("Behaviour")]
    [SerializeField] private string defaultSpeaker = "EGO";
    [SerializeField] private Sprite parryKeySprite;
    [SerializeField] private Sprite dodgeKeySprite;
    [SerializeField] private Sprite healKeySprite;
    [SerializeField] private Sprite questWindowSprite;
    [SerializeField] private Sprite questGaugeSprite;

    CanvasGroup _legacyDialogueGroup;
    CanvasGroup _legacyQuestPanelGroup;
    GameObject _legacyComboGuidePanel;
    Canvas _runtimeCanvas;
    RectTransform _runtimeHudRoot;
    CanvasGroup _runtimeHudGroup;
    TMP_FontAsset _preferredFontAsset;
    Coroutine _timingCueRoutine;
    CanvasGroup _timingCueGroup;
    RectTransform _timingCueRect;
    TextMeshProUGUI _timingCueText;
    Image _timingCueBackground;
    Image _timingCueFill;
    Image _timingCueFillBack;
    Image _timingCueKeyImage;
    Image _timingCueKeyGlow;
    string _currentGuideBody;
    string _currentGuideSpeaker;
    bool _guideTextVisible;

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
        if (!ExhibitionPrototypePresentationPolicy.DialogueEnabled)
        {
            HideGuideText();
            return;
        }

        EnsureRuntimeHud();
        _currentGuideBody = body;
        _currentGuideSpeaker = speaker;
        _guideTextVisible = !string.IsNullOrWhiteSpace(body);

        if (dialogueGroup != null)
            dialogueGroup.alpha = string.IsNullOrWhiteSpace(body) ? 0f : 1f;

        if (speakerText != null)
            speakerText.text = string.IsNullOrWhiteSpace(speaker) ? defaultSpeaker : speaker;

        if (contentText != null)
            contentText.text = body ?? string.Empty;
    }

    public void HideGuideText()
    {
        _currentGuideBody = string.Empty;
        _currentGuideSpeaker = null;
        _guideTextVisible = false;

        if (dialogueGroup != null)
            dialogueGroup.alpha = 0f;
    }

    public void ShowTimingCue(string body, string speaker, float duration)
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeTutorialKeyCueEnabled
            && !ExhibitionPrototypePresentationPolicy.DialogueEnabled)
            return;

        EnsureRuntimeHud();

        if (_timingCueRoutine != null)
            StopCoroutine(_timingCueRoutine);

        _timingCueRoutine = StartCoroutine(CoTimingCue(body, speaker, duration));
    }

    public void HideTimingCue()
    {
        if (_timingCueRoutine != null)
        {
            StopCoroutine(_timingCueRoutine);
            _timingCueRoutine = null;
        }

        if (_timingCueGroup != null)
            _timingCueGroup.alpha = 0f;
        if (_timingCueRect != null)
            _timingCueRect.localScale = Vector3.one;
    }

    IEnumerator CoTimingCue(string body, string speaker, float duration)
    {
        Sprite keyCueSprite = ExhibitionPrototypePresentationPolicy.RuntimeTutorialKeyCueEnabled
            ? ResolveKeyCueSprite(body, speaker)
            : null;
        if (keyCueSprite != null)
        {
            yield return CoKeySpriteCue(duration, keyCueSprite);
            _timingCueRoutine = null;
            yield break;
        }

        if (!ExhibitionPrototypePresentationPolicy.DialogueEnabled)
        {
            HideGuideText();
            _timingCueRoutine = null;
            yield break;
        }

        if (_timingCueGroup == null || _timingCueText == null)
        {
            ShowGuideText(body, speaker);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, duration));
            HideGuideText();
            _timingCueRoutine = null;
            yield break;
        }

        float total = Mathf.Max(0.05f, duration);
        float fadeIn = Mathf.Min(0.08f, total * 0.25f);
        float fadeOut = Mathf.Min(0.12f, total * 0.35f);
        string resolvedSpeaker = string.IsNullOrWhiteSpace(speaker) ? defaultSpeaker : speaker;

        _timingCueText.text = $"<size=72%>{resolvedSpeaker}</size>\n{body}";
        SetTimingTextMode(true);
        _timingCueGroup.alpha = 0f;
        _timingCueGroup.gameObject.SetActive(true);
        if (_timingCueFill != null)
            _timingCueFill.fillAmount = 1f;

        float elapsed = 0f;
        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / total);
            float alpha = 1f;
            if (elapsed < fadeIn)
                alpha = Mathf.Clamp01(elapsed / fadeIn);
            else if (elapsed > total - fadeOut)
                alpha = Mathf.Clamp01((total - elapsed) / fadeOut);

            _timingCueGroup.alpha = alpha;
            if (_timingCueRect != null)
            {
                float pulse = 1f + Mathf.Sin(normalized * Mathf.PI) * 0.045f;
                _timingCueRect.localScale = new Vector3(pulse, pulse, 1f);
            }

            if (_timingCueFill != null)
                _timingCueFill.fillAmount = 1f - normalized;

            yield return null;
        }

        _timingCueGroup.alpha = 0f;
        if (_timingCueRect != null)
            _timingCueRect.localScale = Vector3.one;

        _timingCueRoutine = null;
    }

    IEnumerator CoKeySpriteCue(float duration, Sprite keySprite)
    {
        if (_timingCueGroup == null || _timingCueKeyImage == null)
            yield break;

        if (keySprite == null)
            yield break;

        SetTimingTextMode(false);
        _timingCueKeyImage.sprite = keySprite;
        if (_timingCueKeyGlow != null)
            _timingCueKeyGlow.sprite = keySprite;
        _timingCueGroup.alpha = 0f;
        _timingCueGroup.gameObject.SetActive(true);

        float total = Mathf.Max(0.05f, duration);
        float fadeIn = Mathf.Min(0.08f, total * 0.25f);
        float elapsed = 0f;
        while (elapsed < total)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = elapsed < fadeIn ? Mathf.Clamp01(elapsed / fadeIn) : 1f;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 14f) * 0.14f;
            float glowPulse = 1.24f + Mathf.Sin(Time.unscaledTime * 14f) * 0.18f;

            _timingCueGroup.alpha = alpha;
            _timingCueKeyImage.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            if (_timingCueKeyGlow != null)
            {
                _timingCueKeyGlow.rectTransform.localScale = new Vector3(glowPulse, glowPulse, 1f);
                _timingCueKeyGlow.color = new Color(keyCueGlowColor.r, keyCueGlowColor.g, keyCueGlowColor.b, keyCueGlowColor.a * alpha);
            }

            yield return null;
        }

        _timingCueGroup.alpha = 0f;
        _timingCueKeyImage.rectTransform.localScale = Vector3.one;
        if (_timingCueKeyGlow != null)
            _timingCueKeyGlow.rectTransform.localScale = Vector3.one;
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
        CreateTimingCuePanel();
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
        if (questPanelGroup != null)
            return;

        CanvasGroup panelGroup = CreatePanelGroup(
            "QuastBG",
            _runtimeHudRoot,
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f),
            objectivePanelAnchoredPosition,
            objectivePanelSize,
            objectivePanelColor);
        panelGroup.alpha = 1f;

        ApplyQuestWindowSprite(panelGroup.GetComponent<Image>());

        questPanelGroup = panelGroup;
        questTitleText = EnsureText(panelGroup.transform, "QuestTitle");
        ApplyTextStyle(questTitleText, 24f, FontStyles.Bold, questTitleColor, TextAlignmentOptions.TopLeft);
        ConfigureRect(questTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(52f, -48f), new Vector2(-104f, 34f));

        questDescriptionText = EnsureText(panelGroup.transform, "QuestDescription");
        ApplyTextStyle(questDescriptionText, 18f, FontStyles.Bold, questBodyColor, TextAlignmentOptions.TopLeft);
        questDescriptionText.enableWordWrapping = true;
        ConfigureRect(questDescriptionText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(52f, -82f), new Vector2(-104f, 68f));

        Image gauge = CreateImage(panelGroup.transform, "QuestGauge", Color.white);
        ApplyQuestGaugeSprite(gauge);
        ConfigureRect(gauge.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(-56f, 24f));
    }

    void ApplyQuestWindowSprite(Image background)
    {
        if (background == null)
            return;

        if (questWindowSprite == null)
            questWindowSprite = Resources.Load<Sprite>("UI/HUD/chuon_hud_quest");

        if (questWindowSprite == null)
            return;

        background.sprite = questWindowSprite;
        background.type = Image.Type.Simple;
        background.preserveAspect = false;
        background.color = Color.white;
    }

    void ApplyQuestGaugeSprite(Image gauge)
    {
        if (gauge == null)
            return;

        if (questGaugeSprite == null)
            questGaugeSprite = Resources.Load<Sprite>("UI/HUD/chuon_hud_quest_gauge");

        if (questGaugeSprite == null)
            return;

        gauge.sprite = questGaugeSprite;
        gauge.type = Image.Type.Simple;
        gauge.preserveAspect = false;
        gauge.raycastTarget = false;
        gauge.color = Color.white;
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

    void CreateTimingCuePanel()
    {
        CanvasGroup panelGroup = CreatePanelGroup(
            "TimingCuePanel",
            _runtimeHudRoot,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            timingCuePanelAnchoredPosition,
            timingCuePanelSize,
            timingCuePanelColor);
        panelGroup.alpha = 0f;

        _timingCueGroup = panelGroup;
        _timingCueRect = panelGroup.GetComponent<RectTransform>();
        _timingCueBackground = panelGroup.GetComponent<Image>();
        _timingCueGroup.gameObject.SetActive(true);

        EnsureAccent(panelGroup.transform, "TopAccent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 6f));

        _timingCueFillBack = CreateImage(panelGroup.transform, "TimingFillBack", new Color(0.12f, 0.18f, 0.22f, 0.84f));
        ConfigureRect(_timingCueFillBack.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-56f, 8f));

        _timingCueFill = CreateImage(panelGroup.transform, "TimingFill", accentColor);
        _timingCueFill.type = Image.Type.Filled;
        _timingCueFill.fillMethod = Image.FillMethod.Horizontal;
        _timingCueFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        ConfigureRect(_timingCueFill.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-56f, 8f));

        _timingCueText = EnsureText(panelGroup.transform, "TimingText");
        ApplyTextStyle(_timingCueText, 30f, FontStyles.Bold, timingCueTextColor, TextAlignmentOptions.Center);
        _timingCueText.enableWordWrapping = false;
        ConfigureRect(_timingCueText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(-48f, -24f));

        _timingCueKeyGlow = CreateImage(panelGroup.transform, "ParryKeyGlow", keyCueGlowColor);
        _timingCueKeyGlow.preserveAspect = true;
        ConfigureRect(_timingCueKeyGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, keyCueSize * 1.28f);

        _timingCueKeyImage = CreateImage(panelGroup.transform, "ParryKeyIcon", Color.white);
        _timingCueKeyImage.preserveAspect = true;
        ConfigureRect(_timingCueKeyImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, keyCueSize);

        SetTimingTextMode(true);
    }

    void SetTimingTextMode(bool textMode)
    {
        SetGraphicEnabled(_timingCueBackground, textMode);
        SetGraphicEnabled(_timingCueFillBack, textMode);
        SetGraphicEnabled(_timingCueFill, textMode);
        if (_timingCueText != null)
            _timingCueText.enabled = textMode;
        SetGraphicEnabled(_timingCueKeyGlow, !textMode);
        SetGraphicEnabled(_timingCueKeyImage, !textMode);
    }

    bool ShouldUseParryKeyCue(string body, string speaker)
    {
        return ContainsIgnoreCase(body, "Parry") ||
               ContainsIgnoreCase(speaker, "PARRY") ||
               ContainsIgnoreCase(body, "패링");
    }

    bool ShouldUseDodgeKeyCue(string body, string speaker)
    {
        return ContainsIgnoreCase(body, "Dodge") ||
               ContainsIgnoreCase(body, "Shift") ||
               ContainsIgnoreCase(speaker, "DODGE") ||
               ContainsIgnoreCase(body, "회피");
    }

    bool ShouldUseHealKeyCue(string body, string speaker)
    {
        return ContainsIgnoreCase(body, "Q") ||
               ContainsIgnoreCase(body, "\uC570\uD50C") ||
               ContainsIgnoreCase(body, "\uD68C\uBCF5") ||
               ContainsIgnoreCase(speaker, "HEAL");
    }

    static bool ContainsIgnoreCase(string value, string token)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    Sprite ResolveKeyCueSprite(string body, string speaker)
    {
        if (ShouldUseParryKeyCue(body, speaker))
            return ResolveKeySprite(ref parryKeySprite, "E");

        if (ShouldUseDodgeKeyCue(body, speaker))
            return ResolveKeySprite(ref dodgeKeySprite, "Shift");

        if (ShouldUseHealKeyCue(body, speaker))
            return ResolveKeySprite(ref healKeySprite, "Q");

        return null;
    }

    Sprite ResolveKeySprite(ref Sprite keySprite, string filePrefix)
    {
        if (keySprite != null)
            return keySprite;

        keySprite = Resources.Load<Sprite>("TutorialUI/" + filePrefix);
        if (keySprite != null)
            return keySprite;

        string localizedName = filePrefix == "E"
            ? "E\uD0A4"
            : (filePrefix == "Shift" ? "Shift\uD0A4" : string.Empty);
        if (!string.IsNullOrEmpty(localizedName))
        {
            keySprite = Resources.Load<Sprite>("TutorialUI/" + localizedName);
            if (keySprite != null)
                return keySprite;
        }

        if (filePrefix == "Q")
        {
            keySprite = Resources.Load<Sprite>("TutorialUI/q-Photoroom");
            if (keySprite != null)
                return keySprite;
        }

#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/UIAsset" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || !Path.GetFileNameWithoutExtension(path).StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            keySprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (keySprite != null)
                return keySprite;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                keySprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                return keySprite;
            }
        }
#endif
        return null;
    }

    static void SetGraphicEnabled(Graphic graphic, bool enabled)
    {
        if (graphic != null)
            graphic.enabled = enabled;
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

    Image CreateImage(Transform parent, string objectName, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
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
