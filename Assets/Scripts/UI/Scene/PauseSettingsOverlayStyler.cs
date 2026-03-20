using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PauseSettingsOverlayStyler : MonoBehaviour
{
    const string HeaderLineName = "_OverlayHeaderLine";
    const string TabsRootName = "_OverlayTabs";
    const string TabControlsName = "_TabControls";
    const string TabAudioName = "_TabAudio";
    const string TabVideoName = "_TabVideo";
    const string ControlsColumnsRootName = "_ControlsColumns";
    const string StandardColumnsRootName = "_StandardColumns";
    static string s_lastActiveTab = TabControlsName;

    static class ThemePreset
    {
        public static readonly Color Overlay = new Color(0.03f, 0.07f, 0.12f, 0.78f);
        public static readonly Color Panel = new Color(0.06f, 0.10f, 0.16f, 0.90f);
        public static readonly Color PanelBorder = new Color(0.18f, 0.82f, 0.96f, 0.40f);
        public static readonly Color Accent = new Color(0.20f, 0.90f, 0.98f, 0.95f);
        public static readonly Color Text = new Color(0.94f, 0.98f, 1.00f, 0.96f);
        public static readonly Color MutedText = new Color(0.72f, 0.84f, 0.90f, 0.96f);
        public static readonly Color Button = new Color(0.08f, 0.14f, 0.20f, 0.94f);
        public static readonly Color ButtonHighlight = new Color(0.10f, 0.20f, 0.28f, 0.96f);
        public static readonly Color SliderTrack = new Color(0.14f, 0.22f, 0.28f, 0.98f);
        public static readonly Color SliderFill = new Color(0.18f, 0.82f, 0.96f, 0.90f);
        public static readonly Color Row = new Color(0.08f, 0.12f, 0.16f, 0.36f);
        public static readonly Color RowFocus = new Color(0.10f, 0.17f, 0.22f, 0.68f);
        public static readonly Color ToggleBox = new Color(0.08f, 0.14f, 0.20f, 0.98f);
        public static readonly Color TabIdle = new Color(0.05f, 0.10f, 0.15f, 0.78f);
        public static readonly Color TabActive = new Color(0.10f, 0.24f, 0.32f, 0.96f);
        public static readonly Color TabTextIdle = new Color(0.70f, 0.82f, 0.90f, 0.95f);
        public static readonly Color ValueBox = new Color(0.07f, 0.13f, 0.18f, 0.98f);
        public static readonly Color ValueBoxFocus = new Color(0.10f, 0.20f, 0.26f, 0.98f);
        public static readonly Color TabIndicator = new Color(0.20f, 0.90f, 0.98f, 0.95f);
        public static readonly Color Decoration = new Color(0.18f, 0.82f, 0.96f, 0.32f);
        public static readonly Color DecorationSoft = new Color(0.14f, 0.56f, 0.72f, 0.12f);
        public static readonly Color HeaderLine = new Color(0.20f, 0.90f, 0.98f, 0.45f);
        public static readonly Color BottomPanel = new Color(0.05f, 0.10f, 0.15f, 0.90f);
        public static readonly Color RebindHintPanel = new Color(0.05f, 0.11f, 0.16f, 0.96f);
        public static readonly Color InlineButton = new Color(0.09f, 0.15f, 0.21f, 0.92f);
        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
        public static readonly Color TransparentBlocker = new Color(0f, 0f, 0f, 0.001f);
        public static readonly Color PopupBackground = new Color(0.05f, 0.10f, 0.15f, 0.98f);
        public static readonly Color PopupViewport = new Color(1f, 1f, 1f, 0.01f);
        public static readonly Color PopupScrollbar = new Color(1f, 1f, 1f, 0.04f);
        public static readonly Color DropdownAltRow = new Color(0.07f, 0.13f, 0.18f, 0.96f);
        public static readonly Color RebindPulseStart = new Color(0.14f, 0.24f, 0.30f, 0.98f);
        public static readonly Color RebindValuePulseStart = new Color(0.10f, 0.18f, 0.24f, 0.98f);
        public static readonly Color WaitingButton = new Color(0.08f, 0.12f, 0.16f, 0.55f);
        public static readonly Color WaitingValueBox = new Color(0.07f, 0.13f, 0.18f, 0.65f);
    }

    static class TypographyPreset
    {
        public const int Title = 34;
        public const int Tab = 18;
        public const int PanelTitle = 26;
        public const int RowLabel = 19;
        public const int Button = 18;
        public const int InlineButton = 16;
        public const int ValueBox = 17;
        public const int KeyValue = 18;
        public const int RebindHint = 18;
        public const int ColumnHeader = 15;
        public const int Footer = 14;
    }

    static readonly Color OverlayColor = ThemePreset.Overlay;
    static readonly Color PanelColor = ThemePreset.Panel;
    static readonly Color PanelBorderColor = ThemePreset.PanelBorder;
    static readonly Color AccentColor = ThemePreset.Accent;
    static readonly Color TextColor = ThemePreset.Text;
    static readonly Color MutedTextColor = ThemePreset.MutedText;
    static readonly Color ButtonColor = ThemePreset.Button;
    static readonly Color ButtonHighlightColor = ThemePreset.ButtonHighlight;
    static readonly Color SliderTrackColor = ThemePreset.SliderTrack;
    static readonly Color SliderFillColor = ThemePreset.SliderFill;
    static readonly Color RowColor = ThemePreset.Row;
    static readonly Color RowFocusColor = ThemePreset.RowFocus;
    static readonly Color ToggleBoxColor = ThemePreset.ToggleBox;
    static readonly Color TabIdleColor = ThemePreset.TabIdle;
    static readonly Color TabActiveColor = ThemePreset.TabActive;
    static readonly Color TabTextIdleColor = ThemePreset.TabTextIdle;
    static readonly Color ValueBoxColor = ThemePreset.ValueBox;
    static readonly Color ValueBoxFocusColor = ThemePreset.ValueBoxFocus;
    static readonly Color TabIndicatorColor = ThemePreset.TabIndicator;
    static readonly Color DecorationColor = ThemePreset.Decoration;
    static readonly Color DecorationSoftColor = ThemePreset.DecorationSoft;

    static class LayoutPreset
    {
        public static readonly Vector2 ControlsPanelSize = new Vector2(920f, 500f);
        public static readonly Vector2 StandardPanelSize = new Vector2(860f, 480f);
        public static readonly Vector2 PanelCenterOffset = new Vector2(0f, -8f);
        public static readonly Vector2 BottomPanelSize = new Vector2(420f, 72f);
        public static readonly Vector2 RebindHintSize = new Vector2(420f, 56f);
        public static readonly Vector2 ButtonSize = new Vector2(172f, 46f);
        public static readonly Vector2 RuntimeValueBoxSize = new Vector2(120f, 36f);
        public static readonly Vector2 RuntimeToggleSize = new Vector2(34f, 34f);
        public static readonly Vector2 RuntimeSliderSize = new Vector2(244f, 24f);
        public static readonly Vector2 RuntimeRebindKeySize = new Vector2(210f, 44f);
        public static readonly Vector2 RuntimeRebindButtonSize = new Vector2(146f, 44f);
        public static readonly Vector2 DropdownOptionSize = new Vector2(0f, 38f);
        public static readonly Vector2 DropdownHandleSize = new Vector2(0f, 40f);
        public static readonly Vector2 SliderHandleSize = new Vector2(18f, 18f);
        public static readonly Vector2 ToggleCheckSize = new Vector2(14f, 14f);
        public static readonly Vector2 DropdownMarkerSize = new Vector2(10f, 10f);
        public static readonly Vector2 HeaderLabelSize = new Vector2(0f, 36f);
        public static readonly Vector2 ColumnsSize = new Vector2(0f, 30f);
        public static readonly Vector2 HeaderLabelOffset = new Vector2(0f, -20f);
        public static readonly Vector2 ColumnsOffset = new Vector2(0f, -66f);
        public static readonly Vector2 FooterAnchorMin = new Vector2(0.58f, 0.06f);
        public static readonly Vector2 FooterAnchorMax = new Vector2(0.96f, 0.10f);
        public static readonly Vector2 HeaderLineAnchorMin = new Vector2(0.04f, 0.88f);
        public static readonly Vector2 HeaderLineAnchorMax = new Vector2(0.96f, 0.88f);
        public static readonly Vector2 HeaderLineOffsetMin = new Vector2(0f, -1f);
        public static readonly Vector2 HeaderLineOffsetMax = new Vector2(0f, 1f);
        public static readonly Vector2 TitleAnchorMin = new Vector2(0.04f, 0.90f);
        public static readonly Vector2 TitleAnchorMax = new Vector2(0.40f, 0.98f);
        public static readonly Vector2 TabsAnchorMin = new Vector2(0.30f, 0.89f);
        public static readonly Vector2 TabsAnchorMax = new Vector2(0.96f, 0.97f);
        public static readonly Vector2 TabButtonOffsetMin = new Vector2(10f, 6f);
        public static readonly Vector2 TabButtonOffsetMax = new Vector2(-10f, -6f);
        public static readonly Vector2 ButtonAnchor = new Vector2(1f, 0.5f);
        public static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
        public static readonly Vector2 BottomCenterAnchor = new Vector2(0.5f, 0.10f);
        public static readonly Vector2 RebindHintAnchor = new Vector2(0.5f, 0.18f);
        public static readonly Vector2 RuntimeControlAnchor = new Vector2(1f, 0.5f);
        public static readonly Vector2 HeaderLabelAnchorMin = new Vector2(0.06f, 1f);
        public static readonly Vector2 HeaderLabelAnchorMax = new Vector2(0.94f, 1f);
        public static readonly Vector2 HeaderLabelPivot = new Vector2(0f, 1f);
        public static readonly Vector2 RowAnchorMin = new Vector2(0.08f, 1f);
        public static readonly Vector2 RowAnchorMax = new Vector2(0.92f, 1f);
        public static readonly Vector2 RowPivot = new Vector2(0.5f, 1f);
        public static readonly Vector2 LabelAnchorMin = new Vector2(0f, 0f);
        public static readonly Vector2 LabelAnchorMax = new Vector2(0f, 1f);
        public static readonly Vector2 LabelPivot = new Vector2(0f, 0.5f);
        public static readonly Vector2 LabelOffset = new Vector2(22f, 0f);
        public static readonly Vector2 ContentColumnsAnchorMin = new Vector2(0f, 1f);
        public static readonly Vector2 ContentColumnsAnchorMax = new Vector2(1f, 1f);
        public static readonly Vector2 ContentColumnsPivot = new Vector2(0.5f, 1f);
        public static readonly Vector2 ControlsActionAnchorMin = new Vector2(0.08f, 1f);
        public static readonly Vector2 ControlsActionAnchorMax = new Vector2(0.42f, 1f);
        public static readonly Vector2 ControlsCurrentAnchorMin = new Vector2(0.58f, 1f);
        public static readonly Vector2 ControlsCurrentAnchorMax = new Vector2(0.79f, 1f);
        public static readonly Vector2 ControlsChangeAnchorMin = new Vector2(0.82f, 1f);
        public static readonly Vector2 ControlsChangeAnchorMax = new Vector2(0.96f, 1f);
        public static readonly Vector2 StandardOptionAnchorMin = new Vector2(0.08f, 1f);
        public static readonly Vector2 StandardOptionAnchorMax = new Vector2(0.42f, 1f);
        public static readonly Vector2 StandardValueAnchorMin = new Vector2(0.57f, 1f);
        public static readonly Vector2 StandardValueAnchorMax = new Vector2(0.74f, 1f);
        public static readonly Vector2 StandardControlAnchorMin = new Vector2(0.78f, 1f);
        public static readonly Vector2 StandardControlAnchorMax = new Vector2(0.96f, 1f);
        public static readonly Vector2 OverlayTopGlowAnchorMin = new Vector2(0.04f, 0.84f);
        public static readonly Vector2 OverlayTopGlowAnchorMax = new Vector2(0.96f, 0.84f);
        public static readonly Vector2 OverlayTopGlowOffsetMin = new Vector2(0f, -2f);
        public static readonly Vector2 OverlayTopGlowOffsetMax = new Vector2(0f, 2f);
        public static readonly Vector2 OverlayBottomGlowAnchorMin = new Vector2(0.08f, 0.06f);
        public static readonly Vector2 OverlayBottomGlowAnchorMax = new Vector2(0.92f, 0.06f);
        public static readonly Vector2 OverlayBottomGlowOffsetMin = new Vector2(0f, -1f);
        public static readonly Vector2 OverlayBottomGlowOffsetMax = new Vector2(0f, 1f);
        public static readonly Vector2 OverlayLeftBarAnchorMin = new Vector2(0.04f, 0.18f);
        public static readonly Vector2 OverlayLeftBarAnchorMax = new Vector2(0.04f, 0.82f);
        public static readonly Vector2 OverlayLeftBarOffsetMin = new Vector2(0f, 0f);
        public static readonly Vector2 OverlayLeftBarOffsetMax = new Vector2(2f, 0f);
        public static readonly Vector2 OverlayRightBarAnchorMin = new Vector2(0.96f, 0.18f);
        public static readonly Vector2 OverlayRightBarAnchorMax = new Vector2(0.96f, 0.82f);
        public static readonly Vector2 OverlayRightBarOffsetMin = new Vector2(-2f, 0f);
        public static readonly Vector2 OverlayRightBarOffsetMax = new Vector2(0f, 0f);
        public static readonly Vector2 OverlayCornerTopLeft = new Vector2(0.04f, 0.88f);
        public static readonly Vector2 OverlayCornerTopRight = new Vector2(0.96f, 0.88f);
        public static readonly Vector2 OverlayCornerBottomLeft = new Vector2(0.04f, 0.06f);
        public static readonly Vector2 OverlayCornerBottomRight = new Vector2(0.96f, 0.06f);
        public const float LabelWidth = 300f;
        public const float ControlsFirstRowY = -118f;
        public const float StandardFirstRowY = -78f;
        public const float RowHeight = 52f;
        public const float RowGap = 16f;
        public const float SliderValueBoxWidth = 120f;
        public const float SliderValueBoxX = -286f;
        public const float ToggleValueBoxWidth = 120f;
        public const float ToggleValueBoxX = -118f;
        public const float DropdownValueBoxWidth = 170f;
        public const float DropdownValueBoxX = -186f;
        public const float ControlRightX = -14f;
        public const float SourceButtonX = -6f;
        public const float RebindKeyValueX = -176f;
        public const float RebindButtonX = -14f;
        public const float SaveButtonX = -216f;
        public const float CloseButtonX = -24f;
        public const float DropdownArrowX = -8f;
        public const float DropdownMarkerX = -10f;
        public const float DropdownPopupMinWidth = 260f;
        public const float DropdownPopupPaddingHeight = 12f;
        public const float DropdownItemHeight = 38f;
    }

    RectTransform _hostPanel;
    RectTransform _root;
    RectTransform _panelAudio;
    RectTransform _panelControls;
    RectTransform _panelVideo;
    RectTransform _controlsColumnsRoot;
    Button _saveButton;
    Button _cancelButton;
    Button _tabControls;
    Button _tabAudio;
    Button _tabVideo;
    Image _tabControlsIndicator;
    Image _tabAudioIndicator;
    Image _tabVideoIndicator;
    string _activeTab = TabControlsName;
    RectTransform _dropdownBlocker;
    RectTransform _dropdownPopup;
    RectTransform _dropdownViewport;
    RectTransform _dropdownContent;
    RectTransform _dropdownOptionTemplate;
    Scrollbar _dropdownScrollbar;
    Dropdown _dropdownPopupOwner;
    OptionsManagerAdvanced _optionsManager;
    RectTransform _rebindHintPanel;
    Text _rebindHintText;
    Text _footerHintText;
    float _rebindPulse;
    readonly Dictionary<Button, Text> _runtimeKeyValueTexts = new Dictionary<Button, Text>();
    readonly Dictionary<Button, Button> _runtimeProxyButtons = new Dictionary<Button, Button>();
    readonly Dictionary<Slider, Text> _runtimeSliderValueTexts = new Dictionary<Slider, Text>();
    readonly Dictionary<Toggle, Text> _runtimeToggleValueTexts = new Dictionary<Toggle, Text>();
    readonly Dictionary<Dropdown, Text> _runtimeDropdownValueTexts = new Dictionary<Dropdown, Text>();
    bool _structurePrepared;

    public void Apply(RectTransform hostPanel)
    {
        bool hostChanged = _hostPanel != hostPanel;
        _hostPanel = hostPanel;
        if (!_structurePrepared || hostChanged || _root == null)
        {
            ApplyNow();
            return;
        }

        RefreshRuntimeState();
    }

    void OnEnable()
    {
        if (_hostPanel != null)
            Apply(_hostPanel);
    }

    void Update()
    {
        RefreshRuntimeState();
    }

    public void RefreshRuntimeState()
    {
        if (_hostPanel == null || _root == null)
            return;

        HandleTabHotkeys();
        RefreshTabIndicators();
        RefreshRebindState();
        RefreshOptionValueState();
        RefreshRowFocusState();
    }

    void ApplyNow()
    {
        if (_hostPanel == null)
            return;

        StretchFull(_hostPanel);

        _root = ResolveRootRect();
        if (_root == null)
            return;

        StretchFull(_root);
        if (!string.IsNullOrEmpty(s_lastActiveTab))
            _activeTab = s_lastActiveTab;

        var panelBack = FindRect("Panel_Back");
        _panelAudio = FindRect("Panel_Audio");
        _panelControls = FindRect("Panel_Controls");
        _panelVideo = FindRect("Panel_Video");
        var panelBottom = FindRect("Panel_Bottom");
        var title = FindText("Text_Title");
        var labelAudio = FindText("Label_Audio");
        var labelControls = FindText("Label_Controls");
        var labelVideo = FindText("Label_Video");
        _saveButton = FindButton("Button_Save");
        _cancelButton = FindButton("Button_Cancel");
        _optionsManager = GetComponentInChildren<OptionsManagerAdvanced>(true);

        ConfigureBackdrop(panelBack);
        ConfigureHeaderLine(_root);
        ConfigureOverlayDecorations(_root);
        ConfigureTitle(title);
        ConfigureTabs(_root);

        ConfigureContentPanel(_panelControls, labelControls, "CONTROLS");
        ConfigureContentPanel(_panelAudio, labelAudio, "AUDIO");
        ConfigureContentPanel(_panelVideo, labelVideo, "VIDEO");
        ConfigureBottomPanel(panelBottom);
        ConfigureButton(_saveButton, "APPLY", LayoutPreset.SaveButtonX);
        ConfigureButton(_cancelButton, "CLOSE", LayoutPreset.CloseButtonX);
        ConfigureFooterHint(_root);

        StyleTexts(_root, title, labelAudio, labelControls, labelVideo);
        StyleSliders(_root);
        StyleToggles(_root);
        StyleDropdowns(_root);
        StyleKeybindButtons(_root, _saveButton, _cancelButton);
        ConfigureRebindHint(_root);
        ApplyActiveTab(_activeTab);
        RefreshRebindState();
        RefreshOptionValueState();
        _structurePrepared = true;
    }

    RectTransform ResolveRootRect()
    {
        var backdrop = FindRect("Panel_Back");
        if (backdrop != null && backdrop.parent is RectTransform backdropParent)
            return backdropParent;

        for (int i = 0; i < transform.childCount; i++)
        {
            var rect = transform.GetChild(i) as RectTransform;
            if (rect != null)
                return rect;
        }

        return _hostPanel;
    }

    void ConfigureBackdrop(RectTransform backdrop)
    {
        if (backdrop == null)
            return;

        StretchFull(backdrop);

        var image = backdrop.GetComponent<Image>();
        if (image != null)
        {
            image.color = OverlayColor;
            image.raycastTarget = false;
        }
    }

    void ConfigureHeaderLine(RectTransform root)
    {
        if (root == null)
            return;

        var headerLine = FindRect(HeaderLineName);
        if (headerLine == null)
        {
            var go = new GameObject(HeaderLineName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            headerLine = go.GetComponent<RectTransform>();
        }

        headerLine.SetAsLastSibling();
        headerLine.anchorMin = LayoutPreset.HeaderLineAnchorMin;
        headerLine.anchorMax = LayoutPreset.HeaderLineAnchorMax;
        headerLine.offsetMin = LayoutPreset.HeaderLineOffsetMin;
        headerLine.offsetMax = LayoutPreset.HeaderLineOffsetMax;

        var image = headerLine.GetComponent<Image>();
        if (image != null)
        {
            image.color = ThemePreset.HeaderLine;
            image.raycastTarget = false;
        }
    }

    void ConfigureTitle(Text title)
    {
        if (title == null)
            return;

        var rect = title.rectTransform;
        rect.anchorMin = LayoutPreset.TitleAnchorMin;
        rect.anchorMax = LayoutPreset.TitleAnchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        title.text = "SETTINGS";
        title.alignment = TextAnchor.MiddleLeft;
        title.fontSize = TypographyPreset.Title;
        title.fontStyle = FontStyle.Bold;
        title.color = TextColor;
    }

    void ConfigureTabs(RectTransform root)
    {
        if (root == null)
            return;

        var tabsRoot = FindRect(TabsRootName);
        if (tabsRoot == null)
        {
            var go = new GameObject(TabsRootName, typeof(RectTransform));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            tabsRoot = go.GetComponent<RectTransform>();
        }

        tabsRoot.anchorMin = LayoutPreset.TabsAnchorMin;
        tabsRoot.anchorMax = LayoutPreset.TabsAnchorMax;
        tabsRoot.offsetMin = Vector2.zero;
        tabsRoot.offsetMax = Vector2.zero;

        _tabControls = ConfigureTabButton(tabsRoot, TabControlsName, "CONTROLS", 0);
        _tabAudio = ConfigureTabButton(tabsRoot, TabAudioName, "AUDIO", 1);
        _tabVideo = ConfigureTabButton(tabsRoot, TabVideoName, "VIDEO", 2);
        _tabControlsIndicator = EnsureTabIndicator(_tabControls);
        _tabAudioIndicator = EnsureTabIndicator(_tabAudio);
        _tabVideoIndicator = EnsureTabIndicator(_tabVideo);
    }

    Button ConfigureTabButton(RectTransform tabsRoot, string buttonName, string label, int index)
    {
        var existing = tabsRoot.Find(buttonName);
        Button button;
        Text text;

        if (existing == null)
        {
            var go = new GameObject(buttonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.layer = tabsRoot.gameObject.layer;
            go.transform.SetParent(tabsRoot, false);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.layer = tabsRoot.gameObject.layer;
            textGo.transform.SetParent(go.transform, false);

            button = go.GetComponent<Button>();
            text = textGo.GetComponent<Text>();
            text.font = GetBuiltinFont();
        }
        else
        {
            button = existing.GetComponent<Button>();
            text = existing.GetComponentInChildren<Text>(true);
        }

        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(index / 3f, 0f);
        rect.anchorMax = new Vector2((index + 1) / 3f, 1f);
        rect.offsetMin = LayoutPreset.TabButtonOffsetMin;
        rect.offsetMax = LayoutPreset.TabButtonOffsetMax;

        if (text != null)
        {
            StretchFull(text.rectTransform);
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = TypographyPreset.Tab;
            text.fontStyle = FontStyle.Bold;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => ApplyActiveTab(buttonName));
        ConfigureOutline(button.gameObject);
        return button;
    }

    void ConfigureContentPanel(RectTransform panel, Text label, string labelText)
    {
        if (panel == null)
            return;

        panel.anchorMin = LayoutPreset.CenterAnchor;
        panel.anchorMax = LayoutPreset.CenterAnchor;
        panel.pivot = LayoutPreset.CenterAnchor;
        panel.anchoredPosition = LayoutPreset.PanelCenterOffset;
        panel.sizeDelta = panel == _panelControls
            ? LayoutPreset.ControlsPanelSize
            : LayoutPreset.StandardPanelSize;

        var image = panel.GetComponent<Image>();
        if (image != null)
        {
            image.color = PanelColor;
            image.raycastTarget = false;
        }

        ConfigureOutline(panel.gameObject);
        ConfigurePanelCorners(panel);

        if (label != null)
        {
            label.text = labelText;
            label.alignment = TextAnchor.UpperLeft;
            label.fontSize = TypographyPreset.PanelTitle;
            label.fontStyle = FontStyle.Bold;
            label.color = AccentColor;
        }

        ConfigurePanelHeaders(panel);
        LayoutPanelRows(panel, label);
    }

    void ConfigurePanelHeaders(RectTransform panel)
    {
        if (panel == null)
            return;

        if (panel == _panelControls)
        {
            ConfigureControlsColumns(panel);
            var standardColumns = panel.Find(StandardColumnsRootName);
            if (standardColumns != null)
                standardColumns.gameObject.SetActive(false);
            return;
        }

        if (_controlsColumnsRoot != null)
            _controlsColumnsRoot.gameObject.SetActive(false);

        ConfigureStandardColumns(panel);
    }

    void ApplyActiveTab(string tabName)
    {
        _activeTab = tabName;
        s_lastActiveTab = tabName;
        CloseDropdownPopup();

        SetPanelVisible(_panelControls, tabName == TabControlsName);
        SetPanelVisible(_panelAudio, tabName == TabAudioName);
        SetPanelVisible(_panelVideo, tabName == TabVideoName);

        UpdateTabVisual(_tabControls, tabName == TabControlsName);
        UpdateTabVisual(_tabAudio, tabName == TabAudioName);
        UpdateTabVisual(_tabVideo, tabName == TabVideoName);
        ConfigureNavigationForCurrentState();
        FocusFirstSelectableInActiveTab();
    }

    void SetPanelVisible(RectTransform panel, bool isVisible)
    {
        if (panel == null)
            return;

        panel.gameObject.SetActive(isVisible);
    }

    void UpdateTabVisual(Button button, bool isActive)
    {
        if (button == null)
            return;

        var image = button.GetComponent<Image>();
        if (image != null)
            image.color = isActive ? TabActiveColor : TabIdleColor;

        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
            text.color = isActive ? TextColor : TabTextIdleColor;
    }

    void RefreshTabIndicators()
    {
        RefreshSingleTabIndicator(_tabControlsIndicator, _activeTab == TabControlsName);
        RefreshSingleTabIndicator(_tabAudioIndicator, _activeTab == TabAudioName);
        RefreshSingleTabIndicator(_tabVideoIndicator, _activeTab == TabVideoName);
    }

    void RefreshSingleTabIndicator(Image indicator, bool isActive)
    {
        if (indicator == null)
            return;

        indicator.gameObject.SetActive(isActive);
        if (!isActive)
            return;

        float alpha = 0.72f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.3f)) * 0.20f;
        indicator.color = new Color(TabIndicatorColor.r, TabIndicatorColor.g, TabIndicatorColor.b, alpha);
    }

    void LayoutPanelRows(RectTransform panel, Text headerLabel)
    {
        if (panel == null)
            return;

        if (headerLabel != null)
        {
            var headerRect = headerLabel.rectTransform;
            headerRect.anchorMin = LayoutPreset.HeaderLabelAnchorMin;
            headerRect.anchorMax = LayoutPreset.HeaderLabelAnchorMax;
            headerRect.pivot = LayoutPreset.HeaderLabelPivot;
            headerRect.anchoredPosition = LayoutPreset.HeaderLabelOffset;
            headerRect.sizeDelta = LayoutPreset.HeaderLabelSize;
        }

        float currentY = panel == _panelControls ? LayoutPreset.ControlsFirstRowY : LayoutPreset.StandardFirstRowY;
        float rowHeight = LayoutPreset.RowHeight;
        float rowGap = LayoutPreset.RowGap;

        for (int i = 0; i < panel.childCount; i++)
        {
            var child = panel.GetChild(i) as RectTransform;
            if (child == null || !ShouldLayoutRow(child, headerLabel))
                continue;

            child.anchorMin = LayoutPreset.RowAnchorMin;
            child.anchorMax = LayoutPreset.RowAnchorMax;
            child.pivot = LayoutPreset.RowPivot;
            child.anchoredPosition = new Vector2(0f, currentY);
            child.sizeDelta = new Vector2(0f, rowHeight);

            LayoutRowContents(child, rowHeight);
            currentY -= rowHeight + rowGap;
        }
    }

    void LayoutRowContents(RectTransform row, float rowHeight)
    {
        if (row == null)
            return;

        var rowImage = row.GetComponent<Image>();
        if (rowImage == null)
            rowImage = row.gameObject.AddComponent<Image>();

        rowImage.color = RowColor;
        rowImage.raycastTarget = false;

        var label = GetDirectTextChild(row);
        var button = GetDirectButtonChild(row);
        var slider = GetDirectSliderChild(row);
        var toggle = GetDirectToggleChild(row);
        var dropdown = GetDirectDropdownChild(row);

        if (label != null)
        {
            var labelRect = label.rectTransform;
            labelRect.anchorMin = LayoutPreset.LabelAnchorMin;
            labelRect.anchorMax = LayoutPreset.LabelAnchorMax;
            labelRect.pivot = LayoutPreset.LabelPivot;
            labelRect.anchoredPosition = LayoutPreset.LabelOffset;
            labelRect.sizeDelta = new Vector2(LayoutPreset.LabelWidth, 0f);
            label.alignment = TextAnchor.MiddleLeft;
            label.color = TextColor;
            label.fontStyle = FontStyle.Normal;
            label.fontSize = TypographyPreset.RowLabel;
        }

        if (button != null)
        {
            LayoutRebindRow(row, button, rowHeight);
        }

        if (slider != null)
        {
            var valueText = ConfigureRuntimeValueBox(row, "_RuntimeSliderValueBox", LayoutPreset.SliderValueBoxWidth, LayoutPreset.SliderValueBoxX);
            var sliderRect = slider.GetComponent<RectTransform>();
            sliderRect.anchorMin = LayoutPreset.RuntimeControlAnchor;
            sliderRect.anchorMax = LayoutPreset.RuntimeControlAnchor;
            sliderRect.pivot = LayoutPreset.RuntimeControlAnchor;
            sliderRect.anchoredPosition = new Vector2(LayoutPreset.ControlRightX, 0f);
            sliderRect.sizeDelta = LayoutPreset.RuntimeSliderSize;
            EnsureSliderVisuals(slider);
            _runtimeSliderValueTexts[slider] = valueText;
        }

        if (toggle != null)
        {
            var valueText = ConfigureRuntimeValueBox(row, "_RuntimeToggleValueBox", LayoutPreset.ToggleValueBoxWidth, LayoutPreset.ToggleValueBoxX);
            var toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = LayoutPreset.RuntimeControlAnchor;
            toggleRect.anchorMax = LayoutPreset.RuntimeControlAnchor;
            toggleRect.pivot = LayoutPreset.RuntimeControlAnchor;
            toggleRect.anchoredPosition = new Vector2(LayoutPreset.ControlRightX, 0f);
            toggleRect.sizeDelta = LayoutPreset.RuntimeToggleSize;
            EnsureToggleVisuals(toggle);
            _runtimeToggleValueTexts[toggle] = valueText;
        }

        if (dropdown != null)
        {
            var valueText = ConfigureRuntimeValueBox(row, "_RuntimeDropdownValueBox", LayoutPreset.DropdownValueBoxWidth, LayoutPreset.DropdownValueBoxX);
            var dropdownRect = dropdown.GetComponent<RectTransform>();
            dropdownRect.anchorMin = LayoutPreset.RuntimeControlAnchor;
            dropdownRect.anchorMax = LayoutPreset.RuntimeControlAnchor;
            dropdownRect.pivot = LayoutPreset.RuntimeControlAnchor;
            dropdownRect.anchoredPosition = new Vector2(LayoutPreset.ControlRightX, 0f);
            dropdownRect.sizeDelta = new Vector2(LayoutPreset.DropdownValueBoxWidth, rowHeight - 8f);
            EnsureDropdownVisuals(dropdown);
            _runtimeDropdownValueTexts[dropdown] = valueText;
        }
    }

    bool ShouldLayoutRow(RectTransform child, Text headerLabel)
    {
        if (child == null)
            return false;

        if (headerLabel != null && child == headerLabel.rectTransform)
            return false;

        if (child.name.StartsWith("_"))
            return false;

        if (child.name.Contains("Border"))
            return false;

        if (child.name == "Panel_Back")
            return false;

        return child.name.EndsWith("_Row")
            || child.name.StartsWith("Row_")
            || child.name.StartsWith("Key_")
            || child.name.StartsWith("Mute")
            || child.name.StartsWith("Fullscreen")
            || child.name.StartsWith("VSync")
            || child.name.StartsWith("Brightness");
    }

    void ConfigureBottomPanel(RectTransform panel)
    {
        if (panel == null)
            return;

        panel.anchorMin = LayoutPreset.BottomCenterAnchor;
        panel.anchorMax = LayoutPreset.BottomCenterAnchor;
        panel.pivot = LayoutPreset.CenterAnchor;
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = LayoutPreset.BottomPanelSize;

        var image = panel.GetComponent<Image>();
        if (image != null)
        {
            image.color = ThemePreset.BottomPanel;
            image.raycastTarget = false;
        }

        ConfigureOutline(panel.gameObject);
        ConfigurePanelCorners(panel);
    }

    void ConfigureOverlayDecorations(RectTransform root)
    {
        if (root == null)
            return;

        ConfigureDecorationLine(root, "_OverlayTopGlow", LayoutPreset.OverlayTopGlowAnchorMin, LayoutPreset.OverlayTopGlowAnchorMax, LayoutPreset.OverlayTopGlowOffsetMin, LayoutPreset.OverlayTopGlowOffsetMax, DecorationSoftColor);
        ConfigureDecorationLine(root, "_OverlayBottomGlow", LayoutPreset.OverlayBottomGlowAnchorMin, LayoutPreset.OverlayBottomGlowAnchorMax, LayoutPreset.OverlayBottomGlowOffsetMin, LayoutPreset.OverlayBottomGlowOffsetMax, new Color(DecorationSoftColor.r, DecorationSoftColor.g, DecorationSoftColor.b, 0.20f));
        ConfigureDecorationLine(root, "_OverlayLeftBar", LayoutPreset.OverlayLeftBarAnchorMin, LayoutPreset.OverlayLeftBarAnchorMax, LayoutPreset.OverlayLeftBarOffsetMin, LayoutPreset.OverlayLeftBarOffsetMax, new Color(DecorationSoftColor.r, DecorationSoftColor.g, DecorationSoftColor.b, 0.28f));
        ConfigureDecorationLine(root, "_OverlayRightBar", LayoutPreset.OverlayRightBarAnchorMin, LayoutPreset.OverlayRightBarAnchorMax, LayoutPreset.OverlayRightBarOffsetMin, LayoutPreset.OverlayRightBarOffsetMax, new Color(DecorationSoftColor.r, DecorationSoftColor.g, DecorationSoftColor.b, 0.28f));
        ConfigureCornerBracket(root, "_OverlayCornerTL", LayoutPreset.OverlayCornerTopLeft, false, false);
        ConfigureCornerBracket(root, "_OverlayCornerTR", LayoutPreset.OverlayCornerTopRight, true, false);
        ConfigureCornerBracket(root, "_OverlayCornerBL", LayoutPreset.OverlayCornerBottomLeft, false, true);
        ConfigureCornerBracket(root, "_OverlayCornerBR", LayoutPreset.OverlayCornerBottomRight, true, true);
    }

    void ConfigureRebindHint(RectTransform root)
    {
        if (root == null)
            return;

        if (_rebindHintPanel == null)
        {
            _rebindHintPanel = FindOrCreateRectChild(root, "_RebindHintPanel");
            var image = _rebindHintPanel.GetComponent<Image>();
            if (image == null)
                image = _rebindHintPanel.gameObject.AddComponent<Image>();
            image.color = ThemePreset.RebindHintPanel;
            ConfigureOutline(_rebindHintPanel.gameObject);

            _rebindHintText = FindOrCreateTextChild(_rebindHintPanel, "_RebindHintText");
            _rebindHintText.alignment = TextAnchor.MiddleCenter;
            _rebindHintText.fontSize = TypographyPreset.RebindHint;
            _rebindHintText.fontStyle = FontStyle.Bold;
            _rebindHintText.color = TextColor;
            StretchFull(_rebindHintText.rectTransform);
            _rebindHintText.rectTransform.offsetMin = new Vector2(16f, 0f);
            _rebindHintText.rectTransform.offsetMax = new Vector2(-16f, 0f);
        }

        _rebindHintPanel.anchorMin = LayoutPreset.RebindHintAnchor;
        _rebindHintPanel.anchorMax = LayoutPreset.RebindHintAnchor;
        _rebindHintPanel.pivot = LayoutPreset.CenterAnchor;
        _rebindHintPanel.sizeDelta = LayoutPreset.RebindHintSize;
        _rebindHintPanel.anchoredPosition = new Vector2(0f, 0f);
        _rebindHintPanel.gameObject.SetActive(false);
    }

    void ConfigureButton(Button button, string labelText, float anchoredX)
    {
        if (button == null)
            return;

        var rect = button.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = LayoutPreset.ButtonAnchor;
            rect.anchorMax = LayoutPreset.ButtonAnchor;
            rect.pivot = LayoutPreset.ButtonAnchor;
            rect.sizeDelta = LayoutPreset.ButtonSize;
            rect.anchoredPosition = new Vector2(anchoredX, 0f);
        }

        var image = button.GetComponent<Image>();
        if (image != null)
            image.color = ButtonColor;

        var colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = ButtonHighlightColor;
        colors.pressedColor = AccentColor;
        colors.selectedColor = ButtonHighlightColor;
        colors.disabledColor = new Color(ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.45f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        ConfigureOutline(button.gameObject);

        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = labelText;
            text.color = TextColor;
            text.fontSize = TypographyPreset.Button;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
        }
    }

    void StyleTexts(RectTransform root, params Text[] emphasizedTexts)
    {
        if (root == null)
            return;

        var texts = root.GetComponentsInChildren<Text>(true);
        foreach (var text in texts)
        {
            if (text == null)
                continue;

            if (IsEmphasized(text, emphasizedTexts))
                continue;

            if (text.transform.parent != null && text.transform.parent.name.StartsWith("Button_"))
                continue;

            text.color = MutedTextColor;
            text.resizeTextForBestFit = false;
            if (text.fontSize < 18)
            text.fontSize = TypographyPreset.Button;
            if (text.font == null)
                text.font = GetBuiltinFont();
        }
    }

    bool IsEmphasized(Text target, Text[] emphasizedTexts)
    {
        foreach (var emphasized in emphasizedTexts)
        {
            if (target == emphasized)
                return true;
        }

        return false;
    }

    void StyleSliders(RectTransform root)
    {
        if (root == null)
            return;

        var sliders = root.GetComponentsInChildren<Slider>(true);
        foreach (var slider in sliders)
        {
            if (slider == null)
                continue;

            EnsureSliderVisuals(slider);
        }
    }

    void StyleToggles(RectTransform root)
    {
        if (root == null)
            return;

        var toggles = root.GetComponentsInChildren<Toggle>(true);
        foreach (var toggle in toggles)
        {
            if (toggle == null)
                continue;

            EnsureToggleVisuals(toggle);
        }
    }

    void StyleDropdowns(RectTransform root)
    {
        if (root == null)
            return;

        var dropdowns = root.GetComponentsInChildren<Dropdown>(true);
        foreach (var dropdown in dropdowns)
        {
            if (dropdown == null)
                continue;

            EnsureDropdownVisuals(dropdown);
        }
    }

    void StyleKeybindButtons(RectTransform root, Button saveButton, Button cancelButton)
    {
        if (root == null)
            return;

        var buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button == null || button == saveButton || button == cancelButton)
                continue;

            if (button.name.StartsWith("_Tab"))
                continue;

            StyleInlineButton(button, 16);
        }
    }

    void StyleInlineButton(Button button, int fontSize)
    {
        if (button == null)
            return;

        var image = button.GetComponent<Image>();
        if (image == null)
            image = button.gameObject.AddComponent<Image>();

        image.color = ThemePreset.InlineButton;
        ApplySelectableColors(button, image.color, ButtonHighlightColor, AccentColor);

        ConfigureOutline(button.gameObject);

        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.color = TextColor;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            if (text.font == null)
                text.font = GetBuiltinFont();
        }
    }

    void ApplySelectableColors(Selectable selectable, Color normal, Color highlighted, Color pressed)
    {
        if (selectable == null)
            return;

        var colors = selectable.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = highlighted;
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        selectable.colors = colors;
    }

    void LayoutRebindRow(RectTransform row, Button sourceButton, float rowHeight)
    {
        if (row == null || sourceButton == null)
            return;

        var sourceRect = sourceButton.GetComponent<RectTransform>();
        if (sourceRect == null)
            return;

        sourceRect.anchorMin = LayoutPreset.RuntimeControlAnchor;
        sourceRect.anchorMax = LayoutPreset.RuntimeControlAnchor;
        sourceRect.pivot = LayoutPreset.RuntimeControlAnchor;
        sourceRect.anchoredPosition = new Vector2(LayoutPreset.SourceButtonX, 0f);
        sourceRect.sizeDelta = new Vector2(1f, 1f);

        var sourceImage = sourceButton.GetComponent<Image>();
        if (sourceImage != null)
        sourceImage.color = ThemePreset.Transparent;

        var sourceText = sourceButton.GetComponentInChildren<Text>(true);
        if (sourceText != null)
            sourceText.color = ThemePreset.Transparent;

        var keyValueRoot = FindOrCreateRectChild(row, "_RuntimeKeyValue");
        keyValueRoot.anchorMin = LayoutPreset.RuntimeControlAnchor;
        keyValueRoot.anchorMax = LayoutPreset.RuntimeControlAnchor;
        keyValueRoot.pivot = LayoutPreset.RuntimeControlAnchor;
        keyValueRoot.anchoredPosition = new Vector2(LayoutPreset.RebindKeyValueX, 0f);
        keyValueRoot.sizeDelta = new Vector2(LayoutPreset.RuntimeRebindKeySize.x, rowHeight - 8f);

        var keyValueImage = keyValueRoot.GetComponent<Image>();
        if (keyValueImage == null)
            keyValueImage = keyValueRoot.gameObject.AddComponent<Image>();
        keyValueImage.color = ThemePreset.ValueBox;
        ConfigureOutline(keyValueRoot.gameObject);

        var keyValueText = FindOrCreateTextChild(keyValueRoot, "_RuntimeKeyValueText");
        StretchFull(keyValueText.rectTransform);
        keyValueText.rectTransform.offsetMin = new Vector2(12f, 0f);
        keyValueText.rectTransform.offsetMax = new Vector2(-12f, 0f);
        keyValueText.alignment = TextAnchor.MiddleCenter;
        keyValueText.fontSize = TypographyPreset.KeyValue;
        keyValueText.fontStyle = FontStyle.Bold;
        keyValueText.color = TextColor;
        _runtimeKeyValueTexts[sourceButton] = keyValueText;

        var proxyButtonRect = FindOrCreateRectChild(row, "_RuntimeRebindButton");
        proxyButtonRect.anchorMin = LayoutPreset.RuntimeControlAnchor;
        proxyButtonRect.anchorMax = LayoutPreset.RuntimeControlAnchor;
        proxyButtonRect.pivot = LayoutPreset.RuntimeControlAnchor;
        proxyButtonRect.anchoredPosition = new Vector2(LayoutPreset.RebindButtonX, 0f);
        proxyButtonRect.sizeDelta = new Vector2(LayoutPreset.RuntimeRebindButtonSize.x, rowHeight - 8f);

        var proxyImage = proxyButtonRect.GetComponent<Image>();
        if (proxyImage == null)
            proxyImage = proxyButtonRect.gameObject.AddComponent<Image>();

        var proxyButton = proxyButtonRect.GetComponent<Button>();
        if (proxyButton == null)
            proxyButton = proxyButtonRect.gameObject.AddComponent<Button>();

        var proxyText = FindOrCreateTextChild(proxyButtonRect, "_RuntimeRebindButtonText");
        StretchFull(proxyText.rectTransform);
        proxyText.alignment = TextAnchor.MiddleCenter;
        proxyText.fontSize = TypographyPreset.InlineButton;
        proxyText.fontStyle = FontStyle.Bold;

        proxyButton.onClick.RemoveAllListeners();
        proxyButton.onClick.AddListener(() => sourceButton.onClick.Invoke());
        _runtimeProxyButtons[sourceButton] = proxyButton;

        StyleInlineButton(proxyButton, 16);
        proxyText.text = "REBIND";
    }

    void ConfigureControlsColumns(RectTransform panel)
    {
        if (panel == null)
            return;

        if (_controlsColumnsRoot == null)
        {
            _controlsColumnsRoot = FindOrCreateRectChild(panel, ControlsColumnsRootName);
            CreateControlsColumnLabel(_controlsColumnsRoot, "_ActionColumn", "ACTION", LayoutPreset.ControlsActionAnchorMin, LayoutPreset.ControlsActionAnchorMax, TextAnchor.MiddleLeft);
            CreateControlsColumnLabel(_controlsColumnsRoot, "_CurrentColumn", "CURRENT KEY", LayoutPreset.ControlsCurrentAnchorMin, LayoutPreset.ControlsCurrentAnchorMax, TextAnchor.MiddleCenter);
            CreateControlsColumnLabel(_controlsColumnsRoot, "_ChangeColumn", "CHANGE", LayoutPreset.ControlsChangeAnchorMin, LayoutPreset.ControlsChangeAnchorMax, TextAnchor.MiddleCenter);
        }

        _controlsColumnsRoot.gameObject.SetActive(true);
        _controlsColumnsRoot.anchorMin = LayoutPreset.ContentColumnsAnchorMin;
        _controlsColumnsRoot.anchorMax = LayoutPreset.ContentColumnsAnchorMax;
        _controlsColumnsRoot.pivot = LayoutPreset.ContentColumnsPivot;
        _controlsColumnsRoot.anchoredPosition = LayoutPreset.ColumnsOffset;
        _controlsColumnsRoot.sizeDelta = LayoutPreset.ColumnsSize;
    }

    void ConfigureStandardColumns(RectTransform panel)
    {
        if (panel == null)
            return;

        var columnsRoot = FindOrCreateRectChild(panel, StandardColumnsRootName);
        columnsRoot.gameObject.SetActive(true);
        columnsRoot.anchorMin = LayoutPreset.ContentColumnsAnchorMin;
        columnsRoot.anchorMax = LayoutPreset.ContentColumnsAnchorMax;
        columnsRoot.pivot = LayoutPreset.ContentColumnsPivot;
        columnsRoot.anchoredPosition = LayoutPreset.ColumnsOffset;
        columnsRoot.sizeDelta = LayoutPreset.ColumnsSize;

        CreateControlsColumnLabel(columnsRoot, "_OptionColumn", "OPTION", LayoutPreset.StandardOptionAnchorMin, LayoutPreset.StandardOptionAnchorMax, TextAnchor.MiddleLeft);
        CreateControlsColumnLabel(columnsRoot, "_ValueColumn", "VALUE", LayoutPreset.StandardValueAnchorMin, LayoutPreset.StandardValueAnchorMax, TextAnchor.MiddleCenter);
        CreateControlsColumnLabel(columnsRoot, "_ControlColumn", "CONTROL", LayoutPreset.StandardControlAnchorMin, LayoutPreset.StandardControlAnchorMax, TextAnchor.MiddleCenter);
    }

    void CreateControlsColumnLabel(RectTransform parent, string name, string textValue, Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment)
    {
        var label = FindOrCreateTextChild(parent, name);
        label.text = textValue;
        label.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.92f);
        label.fontSize = TypographyPreset.ColumnHeader;
        label.fontStyle = FontStyle.Bold;
        label.alignment = alignment;
        label.raycastTarget = false;

        var rect = label.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void ConfigureFooterHint(RectTransform root)
    {
        if (root == null)
            return;

        if (_footerHintText == null)
        {
            _footerHintText = FindOrCreateTextChild(root, "_FooterHint");
            _footerHintText.fontSize = TypographyPreset.Footer;
            _footerHintText.fontStyle = FontStyle.Bold;
            _footerHintText.alignment = TextAnchor.MiddleRight;
            _footerHintText.color = MutedTextColor;
            _footerHintText.raycastTarget = false;
        }

        _footerHintText.text = "TAB / Q,E SWITCH  -  ESC CLOSE";
        var rect = _footerHintText.rectTransform;
        rect.anchorMin = LayoutPreset.FooterAnchorMin;
        rect.anchorMax = LayoutPreset.FooterAnchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void RefreshRebindState()
    {
        if (_panelControls == null)
            return;

        bool waiting = _optionsManager != null && _optionsManager.IsWaitingForRebind;
        var activeButton = waiting ? _optionsManager.CurrentRebindButton : null;
        _rebindPulse += Time.unscaledDeltaTime * 3.5f;
        float pulse = 0.65f + Mathf.Abs(Mathf.Sin(_rebindPulse)) * 0.35f;

        var buttons = _panelControls.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button == null || button.name.StartsWith("_Tab"))
                continue;

            if (!button.name.StartsWith("Button_"))
                continue;

            if (!_runtimeKeyValueTexts.TryGetValue(button, out var keyValueText))
                continue;

            var image = _runtimeProxyButtons.TryGetValue(button, out var proxyButton)
                ? proxyButton.GetComponent<Image>()
                : null;
            var text = proxyButton != null ? proxyButton.GetComponentInChildren<Text>(true) : null;
            var sourceText = button.GetComponentInChildren<Text>(true);
            bool isActiveRebind = waiting && button == activeButton;

            if (sourceText != null)
                keyValueText.text = sourceText.text;

            if (isActiveRebind)
            {
                if (image != null)
                    image.color = Color.Lerp(ThemePreset.RebindPulseStart, AccentColor, pulse * 0.45f);
                var keyValueImage = keyValueText.transform.parent.GetComponent<Image>();
                if (keyValueImage != null)
                    keyValueImage.color = Color.Lerp(ThemePreset.RebindValuePulseStart, AccentColor, pulse * 0.30f);

                keyValueText.text = "PRESS KEY";
                keyValueText.color = TextColor;

                if (text != null)
                {
                    text.color = TextColor;
                    text.fontStyle = FontStyle.Bold;
                    text.text = "LISTENING";
                }
            }
            else
            {
                if (image != null)
                {
                    image.color = waiting
                        ? ThemePreset.WaitingButton
                        : ThemePreset.InlineButton;
                }

                var keyValueImage = keyValueText.transform.parent.GetComponent<Image>();
                if (keyValueImage != null)
                    keyValueImage.color = waiting
                        ? ThemePreset.WaitingValueBox
                        : ThemePreset.ValueBox;

                if (text != null)
                {
                    text.color = waiting ? new Color(TextColor.r, TextColor.g, TextColor.b, 0.70f) : TextColor;
                    text.fontStyle = FontStyle.Bold;
                    text.text = "REBIND";
                }

                keyValueText.color = waiting
                    ? new Color(TextColor.r, TextColor.g, TextColor.b, 0.72f)
                    : TextColor;
            }
        }

        if (_rebindHintPanel != null)
        {
            bool showHint = waiting && _activeTab == TabControlsName;
            _rebindHintPanel.gameObject.SetActive(showHint);

            if (showHint && _rebindHintText != null)
            {
                string actionName = ResolveRebindActionName(activeButton);
                _rebindHintText.text = string.IsNullOrEmpty(actionName)
                    ? "PRESS ANY KEY TO REBIND"
                    : "PRESS ANY KEY FOR " + actionName.ToUpperInvariant();
            }
        }
    }

    void RefreshOptionValueState()
    {
        foreach (var entry in _runtimeSliderValueTexts)
        {
            var slider = entry.Key;
            var text = entry.Value;
            if (slider == null || text == null)
                continue;

            text.text = Mathf.RoundToInt(slider.value * 100f) + "%";
            var box = text.transform.parent.GetComponent<Image>();
            if (box != null)
                box.color = slider.interactable ? ValueBoxColor : new Color(ValueBoxColor.r, ValueBoxColor.g, ValueBoxColor.b, 0.45f);
        }

        foreach (var entry in _runtimeToggleValueTexts)
        {
            var toggle = entry.Key;
            var text = entry.Value;
            if (toggle == null || text == null)
                continue;

            text.text = toggle.isOn ? "ON" : "OFF";
            text.color = toggle.isOn ? AccentColor : TextColor;
            var box = text.transform.parent.GetComponent<Image>();
            if (box != null)
                box.color = toggle.isOn ? ValueBoxFocusColor : ValueBoxColor;
        }

        foreach (var entry in _runtimeDropdownValueTexts)
        {
            var dropdown = entry.Key;
            var text = entry.Value;
            if (dropdown == null || text == null)
                continue;

            text.text = GetDropdownOptionText(dropdown);
            var box = text.transform.parent.GetComponent<Image>();
            if (box != null)
                box.color = dropdown.interactable ? ValueBoxColor : new Color(ValueBoxColor.r, ValueBoxColor.g, ValueBoxColor.b, 0.45f);
        }
    }

    void RefreshRowFocusState()
    {
        RefreshPanelRowFocus(_panelControls);
        RefreshPanelRowFocus(_panelAudio);
        RefreshPanelRowFocus(_panelVideo);
    }

    void RefreshPanelRowFocus(RectTransform panel)
    {
        if (panel == null || !panel.gameObject.activeInHierarchy)
            return;

        for (int i = 0; i < panel.childCount; i++)
        {
            var child = panel.GetChild(i) as RectTransform;
            if (child == null || !ShouldLayoutRow(child, null))
                continue;

            var rowImage = child.GetComponent<Image>();
            if (rowImage == null)
                continue;

            rowImage.color = RowHasFocusedControl(child) ? RowFocusColor : RowColor;
        }
    }

    bool RowHasFocusedControl(RectTransform row)
    {
        if (row == null || EventSystem.current == null)
            return false;

        var selected = EventSystem.current.currentSelectedGameObject;
        return selected != null && selected.transform.IsChildOf(row);
    }

    void HandleTabHotkeys()
    {
        if (!isActiveAndEnabled)
            return;

        if (_optionsManager != null && _optionsManager.IsWaitingForRebind)
            return;

        if (_dropdownPopup != null && _dropdownPopup.gameObject.activeSelf)
            return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CycleTab(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1);
            return;
        }

        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            CycleTab(-1);
            return;
        }

        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow))
            CycleTab(1);
    }

    void CycleTab(int direction)
    {
        string[] tabs = { TabControlsName, TabAudioName, TabVideoName };
        int currentIndex = 0;
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == _activeTab)
            {
                currentIndex = i;
                break;
            }
        }

        currentIndex = (currentIndex + direction + tabs.Length) % tabs.Length;
        ApplyActiveTab(tabs[currentIndex]);
    }

    void FocusFirstSelectableInActiveTab()
    {
        if (EventSystem.current == null)
            return;

        var panel = _activeTab == TabControlsName
            ? _panelControls
            : (_activeTab == TabAudioName ? _panelAudio : _panelVideo);

        if (panel == null)
            return;

        var selectables = panel.GetComponentsInChildren<Selectable>(true);
        foreach (var selectable in selectables)
        {
            if (!IsSelectableNavigable(selectable))
                continue;

            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            return;
        }
    }

    void ConfigureNavigationForCurrentState()
    {
        ConfigureTabNavigation();

        var panel = GetActivePanel();
        if (panel == null)
            return;

        var orderedSelectables = GetOrderedPanelSelectables(panel);
        ConfigurePanelNavigation(orderedSelectables);
        ConfigureBottomNavigation(orderedSelectables);
    }

    void ConfigureTabNavigation()
    {
        ConfigureTabNavigationButton(_tabControls, _tabVideo, _tabAudio, GetFirstSelectableForPanel(_panelControls));
        ConfigureTabNavigationButton(_tabAudio, _tabControls, _tabVideo, GetFirstSelectableForPanel(_panelAudio));
        ConfigureTabNavigationButton(_tabVideo, _tabAudio, _tabControls, GetFirstSelectableForPanel(_panelVideo));
    }

    void ConfigureTabNavigationButton(Button button, Selectable left, Selectable right, Selectable down)
    {
        if (button == null)
            return;

        var nav = button.navigation;
        nav.mode = Navigation.Mode.Explicit;
        nav.selectOnLeft = left;
        nav.selectOnRight = right;
        nav.selectOnDown = down;
        nav.selectOnUp = null;
        button.navigation = nav;
    }

    void ConfigurePanelNavigation(List<Selectable> orderedSelectables)
    {
        for (int i = 0; i < orderedSelectables.Count; i++)
        {
            var selectable = orderedSelectables[i];
            if (selectable == null)
                continue;

            var nav = selectable.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = i > 0 ? orderedSelectables[i - 1] : GetActiveTabButton();
            nav.selectOnDown = i < orderedSelectables.Count - 1 ? orderedSelectables[i + 1] : _saveButton;

            if (selectable == _saveButton)
            {
                nav.selectOnLeft = _cancelButton;
                nav.selectOnRight = _cancelButton;
            }
            else if (selectable == _cancelButton)
            {
                nav.selectOnLeft = _saveButton;
                nav.selectOnRight = _saveButton;
            }

            selectable.navigation = nav;
        }
    }

    void ConfigureBottomNavigation(List<Selectable> orderedSelectables)
    {
        var lastSelectable = orderedSelectables.Count > 0 ? orderedSelectables[orderedSelectables.Count - 1] : GetActiveTabButton();

        if (_saveButton != null)
        {
            var nav = _saveButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = lastSelectable;
            nav.selectOnDown = GetActiveTabButton();
            nav.selectOnLeft = _cancelButton;
            nav.selectOnRight = _cancelButton;
            _saveButton.navigation = nav;
        }

        if (_cancelButton != null)
        {
            var nav = _cancelButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = lastSelectable;
            nav.selectOnDown = GetActiveTabButton();
            nav.selectOnLeft = _saveButton;
            nav.selectOnRight = _saveButton;
            _cancelButton.navigation = nav;
        }
    }

    RectTransform GetActivePanel()
    {
        return _activeTab == TabControlsName
            ? _panelControls
            : (_activeTab == TabAudioName ? _panelAudio : _panelVideo);
    }

    Button GetActiveTabButton()
    {
        return _activeTab == TabControlsName
            ? _tabControls
            : (_activeTab == TabAudioName ? _tabAudio : _tabVideo);
    }

    Selectable GetFirstSelectableForPanel(RectTransform panel)
    {
        var ordered = GetOrderedPanelSelectables(panel);
        return ordered.Count > 0 ? ordered[0] : null;
    }

    List<Selectable> GetOrderedPanelSelectables(RectTransform panel)
    {
        var ordered = new List<Selectable>();
        if (panel == null)
            return ordered;

        for (int i = 0; i < panel.childCount; i++)
        {
            var row = panel.GetChild(i) as RectTransform;
            if (row == null || !ShouldLayoutRow(row, null))
                continue;

            var selectable = GetPreferredSelectableInRow(row);
            if (IsSelectableNavigable(selectable))
                ordered.Add(selectable);
        }

        return ordered;
    }

    Selectable GetPreferredSelectableInRow(RectTransform row)
    {
        if (row == null)
            return null;

        var runtimeRebind = row.Find("_RuntimeRebindButton");
        if (runtimeRebind != null)
        {
            var button = runtimeRebind.GetComponent<Button>();
            if (button != null)
                return button;
        }

        var dropdown = GetDirectDropdownChild(row);
        if (dropdown != null)
            return dropdown;

        var slider = GetDirectSliderChild(row);
        if (slider != null)
            return slider;

        var toggle = GetDirectToggleChild(row);
        if (toggle != null)
            return toggle;

        var buttonChild = GetDirectButtonChild(row);
        if (buttonChild != null)
            return buttonChild;

        return null;
    }

    bool IsSelectableNavigable(Selectable selectable)
    {
        if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable())
            return false;

        var rect = selectable.transform as RectTransform;
        if (rect == null)
            return false;

        if (rect.rect.width < 20f || rect.rect.height < 20f)
            return false;

        return true;
    }

    string ResolveRebindActionName(Button button)
    {
        if (button == null)
            return string.Empty;

        var row = button.transform.parent as RectTransform;
        if (row == null)
            return button.name.Replace("Button_", string.Empty);

        var label = GetDirectTextChild(row);
        if (label == null || string.IsNullOrWhiteSpace(label.text))
            return button.name.Replace("Button_", string.Empty);

        return label.text.Trim();
    }

    Text ConfigureRuntimeValueBox(RectTransform row, string rootName, float width, float anchoredX)
    {
        var valueRoot = FindOrCreateRectChild(row, rootName);
        valueRoot.anchorMin = new Vector2(1f, 0.5f);
        valueRoot.anchorMax = new Vector2(1f, 0.5f);
        valueRoot.pivot = new Vector2(1f, 0.5f);
        valueRoot.anchoredPosition = new Vector2(anchoredX, 0f);
        valueRoot.sizeDelta = new Vector2(width, 36f);

        var image = valueRoot.GetComponent<Image>();
        if (image == null)
            image = valueRoot.gameObject.AddComponent<Image>();
        image.color = ValueBoxColor;
        image.raycastTarget = false;
        ConfigureOutline(valueRoot.gameObject);

        var text = FindOrCreateTextChild(valueRoot, "_RuntimeValueText");
        StretchFull(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(10f, 0f);
        text.rectTransform.offsetMax = new Vector2(-10f, 0f);
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = TypographyPreset.ValueBox;
        text.fontStyle = FontStyle.Bold;
        text.color = TextColor;
        text.raycastTarget = false;
        return text;
    }

    void ConfigurePanelCorners(RectTransform panel)
    {
        if (panel == null)
            return;

        ConfigureCornerBracket(panel, "_CornerTL", new Vector2(0f, 1f), false, false, 28f, 3f, AccentColor);
        ConfigureCornerBracket(panel, "_CornerTR", new Vector2(1f, 1f), true, false, 28f, 3f, AccentColor);
        ConfigureCornerBracket(panel, "_CornerBL", new Vector2(0f, 0f), false, true, 28f, 3f, AccentColor);
        ConfigureCornerBracket(panel, "_CornerBR", new Vector2(1f, 0f), true, true, 28f, 3f, AccentColor);
    }

    void ConfigureCornerBracket(RectTransform parent, string rootName, Vector2 anchor, bool flipX, bool flipY, float length = 24f, float thickness = 2f, Color? colorOverride = null)
    {
        if (parent == null)
            return;

        var root = FindOrCreateRectChild(parent, rootName);
        root.anchorMin = anchor;
        root.anchorMax = anchor;
        root.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(length, length);
        root.SetAsLastSibling();

        var horizontal = FindOrCreateImageChild(root, "_H");
        horizontal.color = colorOverride ?? DecorationColor;
        horizontal.raycastTarget = false;
        horizontal.rectTransform.anchorMin = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        horizontal.rectTransform.anchorMax = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        horizontal.rectTransform.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        horizontal.rectTransform.anchoredPosition = Vector2.zero;
        horizontal.rectTransform.sizeDelta = new Vector2(length, thickness);

        var vertical = FindOrCreateImageChild(root, "_V");
        vertical.color = colorOverride ?? DecorationColor;
        vertical.raycastTarget = false;
        vertical.rectTransform.anchorMin = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        vertical.rectTransform.anchorMax = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        vertical.rectTransform.pivot = new Vector2(flipX ? 1f : 0f, flipY ? 0f : 1f);
        vertical.rectTransform.anchoredPosition = Vector2.zero;
        vertical.rectTransform.sizeDelta = new Vector2(thickness, length);
    }

    void ConfigureDecorationLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        if (parent == null)
            return;

        var line = FindOrCreateImageChild(parent, name);
        line.color = color;
        line.raycastTarget = false;
        line.rectTransform.anchorMin = anchorMin;
        line.rectTransform.anchorMax = anchorMax;
        line.rectTransform.offsetMin = offsetMin;
        line.rectTransform.offsetMax = offsetMax;
        line.rectTransform.SetAsFirstSibling();
    }

    Image EnsureTabIndicator(Button button)
    {
        if (button == null)
            return null;

        var indicatorRect = FindOrCreateRectChild(button.GetComponent<RectTransform>(), "_Indicator");
        indicatorRect.anchorMin = new Vector2(0.08f, 0f);
        indicatorRect.anchorMax = new Vector2(0.92f, 0f);
        indicatorRect.pivot = new Vector2(0.5f, 0f);
        indicatorRect.anchoredPosition = new Vector2(0f, 2f);
        indicatorRect.sizeDelta = new Vector2(0f, 4f);
        indicatorRect.SetAsLastSibling();

        var image = indicatorRect.GetComponent<Image>();
        if (image == null)
            image = indicatorRect.gameObject.AddComponent<Image>();
        image.color = TabIndicatorColor;
        image.raycastTarget = false;
        return image;
    }

    void EnsureSliderVisuals(Slider slider)
    {
        if (slider == null)
            return;

        var rootRect = slider.transform as RectTransform;
        if (rootRect == null)
            return;

        var background = FindOrCreateImageChild(rootRect, "_RuntimeSliderTrack");
        background.color = SliderTrackColor;
        background.raycastTarget = false;
        background.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        background.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        background.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        background.rectTransform.sizeDelta = new Vector2(0f, 8f);
        background.rectTransform.anchoredPosition = Vector2.zero;

        var fillArea = FindOrCreateRectChild(rootRect, "_RuntimeFillArea");
        fillArea.anchorMin = new Vector2(0f, 0f);
        fillArea.anchorMax = new Vector2(1f, 1f);
        fillArea.offsetMin = new Vector2(10f, 0f);
        fillArea.offsetMax = new Vector2(-10f, 0f);

        var fill = FindOrCreateImageChild(fillArea, "_RuntimeFill");
        fill.color = SliderFillColor;
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        fill.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        fill.rectTransform.sizeDelta = new Vector2(0f, 8f);
        fill.rectTransform.anchoredPosition = Vector2.zero;

        var handleArea = FindOrCreateRectChild(rootRect, "_RuntimeHandleArea");
        handleArea.anchorMin = new Vector2(0f, 0f);
        handleArea.anchorMax = new Vector2(1f, 1f);
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);

        var handle = FindOrCreateImageChild(handleArea, "_RuntimeHandle");
        handle.color = TextColor;
        handle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        handle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        handle.rectTransform.sizeDelta = LayoutPreset.SliderHandleSize;
        handle.rectTransform.anchoredPosition = Vector2.zero;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
    }

    void EnsureToggleVisuals(Toggle toggle)
    {
        if (toggle == null)
            return;

        var rootRect = toggle.transform as RectTransform;
        if (rootRect == null)
            return;

        var background = FindOrCreateImageChild(rootRect, "_RuntimeToggleBackground");
        background.color = ToggleBoxColor;
        background.rectTransform.anchorMin = Vector2.zero;
        background.rectTransform.anchorMax = Vector2.one;
        background.rectTransform.offsetMin = Vector2.zero;
        background.rectTransform.offsetMax = Vector2.zero;
        ConfigureOutline(background.gameObject);

        var checkmark = FindOrCreateImageChild(rootRect, "_RuntimeToggleCheck");
        checkmark.color = AccentColor;
        checkmark.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        checkmark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        checkmark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        checkmark.rectTransform.sizeDelta = LayoutPreset.ToggleCheckSize;
        checkmark.rectTransform.anchoredPosition = Vector2.zero;

        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        ApplySelectableColors(toggle, ToggleBoxColor, ValueBoxFocusColor, AccentColor);
    }

    void EnsureDropdownVisuals(Dropdown dropdown)
    {
        if (dropdown == null)
            return;

        var rootRect = dropdown.transform as RectTransform;
        if (rootRect == null)
            return;

        var image = dropdown.GetComponent<Image>();
        if (image == null)
            image = dropdown.gameObject.AddComponent<Image>();

        image.color = ButtonColor;
        ConfigureOutline(dropdown.gameObject);
        ApplySelectableColors(dropdown, ButtonColor, ButtonHighlightColor, AccentColor);

        var caption = dropdown.captionText;
        if (caption == null)
        {
            caption = FindOrCreateTextChild(rootRect, "_RuntimeCaption");
            dropdown.captionText = caption;
        }

        caption.rectTransform.anchorMin = new Vector2(0f, 0f);
        caption.rectTransform.anchorMax = new Vector2(1f, 1f);
        caption.rectTransform.offsetMin = new Vector2(14f, 0f);
        caption.rectTransform.offsetMax = new Vector2(-34f, 0f);
        caption.alignment = TextAnchor.MiddleLeft;
        caption.fontSize = TypographyPreset.Button;
        caption.color = TextColor;
        caption.fontStyle = FontStyle.Normal;

        var arrow = FindOrCreateTextChild(rootRect, "_RuntimeArrow");
        arrow.text = "v";
        arrow.alignment = TextAnchor.MiddleCenter;
        arrow.fontSize = TypographyPreset.Button;
        arrow.color = AccentColor;
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.anchoredPosition = new Vector2(LayoutPreset.DropdownArrowX, 0f);
        arrow.rectTransform.sizeDelta = new Vector2(24f, 0f);

        caption.text = GetDropdownOptionText(dropdown);

        var trigger = dropdown.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = dropdown.gameObject.AddComponent<EventTrigger>();

        if (trigger.triggers == null)
            trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

        trigger.triggers.Clear();

        var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick, callback = new EventTrigger.TriggerEvent() };
        clickEntry.callback.AddListener(_ => ToggleDropdownPopup(dropdown));
        trigger.triggers.Add(clickEntry);

        var submitEntry = new EventTrigger.Entry { eventID = EventTriggerType.Submit, callback = new EventTrigger.TriggerEvent() };
        submitEntry.callback.AddListener(_ => ToggleDropdownPopup(dropdown));
        trigger.triggers.Add(submitEntry);
    }

    string GetDropdownOptionText(Dropdown dropdown)
    {
        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
            return "-";

        var index = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
        return dropdown.options[index] != null ? dropdown.options[index].text : "-";
    }

    void ToggleDropdownPopup(Dropdown dropdown)
    {
        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
            return;

        if (_dropdownPopupOwner == dropdown && _dropdownPopup != null && _dropdownPopup.gameObject.activeSelf)
        {
            CloseDropdownPopup();
            return;
        }

        ShowDropdownPopup(dropdown);
    }

    void ShowDropdownPopup(Dropdown dropdown)
    {
        EnsureDropdownPopup();
        if (_dropdownPopup == null || _dropdownContent == null || _root == null)
            return;

        _dropdownPopupOwner = dropdown;
        if (_dropdownBlocker != null)
            _dropdownBlocker.gameObject.SetActive(true);
        _dropdownPopup.gameObject.SetActive(true);
        ClearDropdownPopupItems();

        int optionCount = dropdown.options != null ? dropdown.options.Count : 0;
        int visibleCount = Mathf.Clamp(optionCount, 1, 6);
        float itemHeight = LayoutPreset.DropdownItemHeight;

        var sourceRect = dropdown.transform as RectTransform;
        float popupWidth = sourceRect != null ? Mathf.Max(LayoutPreset.DropdownPopupMinWidth, sourceRect.rect.width) : LayoutPreset.DropdownPopupMinWidth;
        _dropdownPopup.sizeDelta = new Vector2(popupWidth, visibleCount * itemHeight + LayoutPreset.DropdownPopupPaddingHeight);
        PositionDropdownPopup(dropdown);

        for (int i = 0; i < optionCount; i++)
        {
            CreateDropdownOptionButton(dropdown, i, dropdown.options[i] != null ? dropdown.options[i].text : "-");
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_dropdownContent);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_dropdownPopup);
    }

    void PositionDropdownPopup(Dropdown dropdown)
    {
        if (_dropdownPopup == null || _root == null)
            return;

        var sourceRect = dropdown.transform as RectTransform;
        if (sourceRect == null)
            return;

        var corners = new Vector3[4];
        sourceRect.GetWorldCorners(corners);

        Vector2 localBottomLeft;
        Vector2 localTopLeft;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, corners[0], null, out localBottomLeft);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, corners[1], null, out localTopLeft);

        float popupWidth = _dropdownPopup.rect.width;
        float popupHeight = _dropdownPopup.rect.height;
        float rootHalfWidth = _root.rect.width * 0.5f;
        float rootHalfHeight = _root.rect.height * 0.5f;

        float x = Mathf.Clamp(localBottomLeft.x, -rootHalfWidth + 20f, rootHalfWidth - popupWidth - 20f);
        float y = localTopLeft.y - 4f;

        if (y - popupHeight < -rootHalfHeight + 20f)
            y = localBottomLeft.y + popupHeight + 4f;

        y = Mathf.Clamp(y, -rootHalfHeight + popupHeight + 20f, rootHalfHeight - 20f);

        _dropdownPopup.anchorMin = new Vector2(0.5f, 0.5f);
        _dropdownPopup.anchorMax = new Vector2(0.5f, 0.5f);
        _dropdownPopup.pivot = new Vector2(0f, 1f);
        _dropdownPopup.anchoredPosition = new Vector2(x, y);
        _dropdownPopup.SetAsLastSibling();
    }

    void EnsureDropdownPopup()
    {
        if (_root == null)
            return;

        if (_dropdownBlocker == null)
        {
            _dropdownBlocker = FindOrCreateRectChild(_root, "_RuntimeDropdownBlocker");
            StretchFull(_dropdownBlocker);

            var blockerImage = _dropdownBlocker.GetComponent<Image>();
            if (blockerImage == null)
                blockerImage = _dropdownBlocker.gameObject.AddComponent<Image>();
            blockerImage.color = ThemePreset.TransparentBlocker;

            var blockerButton = _dropdownBlocker.GetComponent<Button>();
            if (blockerButton == null)
                blockerButton = _dropdownBlocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.RemoveAllListeners();
            blockerButton.onClick.AddListener(CloseDropdownPopup);
        }

        if (_dropdownPopup == null)
        {
            _dropdownPopup = FindOrCreateRectChild(_dropdownBlocker, "_RuntimeDropdownPopup");
            var popupImage = _dropdownPopup.GetComponent<Image>();
            if (popupImage == null)
                popupImage = _dropdownPopup.gameObject.AddComponent<Image>();
            popupImage.color = ThemePreset.PopupBackground;
            ConfigureOutline(_dropdownPopup.gameObject);

            var scrollRect = _dropdownPopup.GetComponent<ScrollRect>();
            if (scrollRect == null)
                scrollRect = _dropdownPopup.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 18f;

            _dropdownViewport = FindOrCreateRectChild(_dropdownPopup, "_Viewport");
            var viewportImage = _dropdownViewport.GetComponent<Image>();
            if (viewportImage == null)
                viewportImage = _dropdownViewport.gameObject.AddComponent<Image>();
            viewportImage.color = ThemePreset.PopupViewport;

            var mask = _dropdownViewport.GetComponent<Mask>();
            if (mask == null)
                mask = _dropdownViewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            _dropdownContent = FindOrCreateRectChild(_dropdownViewport, "_Content");

            var layout = _dropdownContent.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = _dropdownContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = _dropdownContent.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = _dropdownContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbarRect = FindOrCreateRectChild(_dropdownPopup, "_Scrollbar");
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-14f, 6f);
            scrollbarRect.offsetMax = new Vector2(-4f, -6f);

            var scrollbarImage = scrollbarRect.GetComponent<Image>();
            if (scrollbarImage == null)
                scrollbarImage = scrollbarRect.gameObject.AddComponent<Image>();
            scrollbarImage.color = ThemePreset.PopupScrollbar;

            _dropdownScrollbar = scrollbarRect.GetComponent<Scrollbar>();
            if (_dropdownScrollbar == null)
                _dropdownScrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();

            var slidingArea = FindOrCreateRectChild(scrollbarRect, "_SlidingArea");
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.offsetMin = new Vector2(2f, 2f);
            slidingArea.offsetMax = new Vector2(-2f, -2f);

            var handle = FindOrCreateImageChild(slidingArea, "_Handle");
            handle.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.90f);
            handle.rectTransform.anchorMin = new Vector2(0f, 1f);
            handle.rectTransform.anchorMax = new Vector2(1f, 1f);
            handle.rectTransform.pivot = new Vector2(0.5f, 1f);
            handle.rectTransform.sizeDelta = LayoutPreset.DropdownHandleSize;
            handle.rectTransform.anchoredPosition = Vector2.zero;

            _dropdownScrollbar.direction = Scrollbar.Direction.BottomToTop;
            _dropdownScrollbar.handleRect = handle.rectTransform;
            _dropdownScrollbar.targetGraphic = handle;

            StretchFull(_dropdownViewport);
            _dropdownViewport.offsetMax = new Vector2(-18f, 0f);

            _dropdownContent.anchorMin = new Vector2(0f, 1f);
            _dropdownContent.anchorMax = new Vector2(1f, 1f);
            _dropdownContent.pivot = new Vector2(0.5f, 1f);
            _dropdownContent.anchoredPosition = Vector2.zero;
            _dropdownContent.sizeDelta = Vector2.zero;

            scrollRect.viewport = _dropdownViewport;
            scrollRect.content = _dropdownContent;
            scrollRect.verticalScrollbar = _dropdownScrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        if (_dropdownOptionTemplate == null)
        {
            _dropdownOptionTemplate = FindOrCreateRectChild(_dropdownPopup, "_OptionTemplate");

            var templateImage = _dropdownOptionTemplate.GetComponent<Image>();
            if (templateImage == null)
                templateImage = _dropdownOptionTemplate.gameObject.AddComponent<Image>();

            var templateButton = _dropdownOptionTemplate.GetComponent<Button>();
            if (templateButton == null)
                templateButton = _dropdownOptionTemplate.gameObject.AddComponent<Button>();

            var optionText = FindOrCreateTextChild(_dropdownOptionTemplate, "Text");
            optionText.font = GetBuiltinFont();

            var marker = FindOrCreateImageChild(_dropdownOptionTemplate, "SelectedMarker");
            marker.raycastTarget = false;
        }

        ConfigureDropdownOptionTemplate();

        if (_dropdownBlocker != null)
            _dropdownBlocker.gameObject.SetActive(false);

        if (_dropdownPopup != null)
            _dropdownPopup.gameObject.SetActive(false);
    }

    void ClearDropdownPopupItems()
    {
        if (_dropdownContent == null)
            return;

        for (int i = _dropdownContent.childCount - 1; i >= 0; i--)
        {
            var child = _dropdownContent.GetChild(i) as RectTransform;
            if (child == null)
                continue;

            if (!child.name.StartsWith("Option_"))
                continue;

            var button = child.GetComponent<Button>();
            if (button != null)
                button.onClick.RemoveAllListeners();

            child.gameObject.SetActive(false);
        }
    }

    void CreateDropdownOptionButton(Dropdown dropdown, int index, string optionText)
    {
        if (_dropdownContent == null)
            return;

        GameObject go;
        RectTransform rect;
        Image image;
        Button button;
        Text text;
        Image markerImage;

        var existing = _dropdownContent.Find("Option_" + index) as RectTransform;
        if (existing != null)
        {
            go = existing.gameObject;
            go.SetActive(true);
            rect = go.GetComponent<RectTransform>();
            image = go.GetComponent<Image>();
            button = go.GetComponent<Button>();
            text = go.transform.Find("Text")?.GetComponent<Text>();
            markerImage = go.transform.Find("SelectedMarker")?.GetComponent<Image>();
        }
        else
        {
            var optionTemplate = GetDropdownOptionTemplate();
            if (optionTemplate != null)
            {
                go = Instantiate(optionTemplate.gameObject, _dropdownContent);
                go.SetActive(true);
                rect = go.GetComponent<RectTransform>();
                image = go.GetComponent<Image>();
                button = go.GetComponent<Button>();
                text = go.transform.Find("Text")?.GetComponent<Text>();
                markerImage = go.transform.Find("SelectedMarker")?.GetComponent<Image>();
            }
            else
            {
                go = new GameObject("Option_" + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.layer = _dropdownContent.gameObject.layer;
                go.transform.SetParent(_dropdownContent, false);
                rect = go.GetComponent<RectTransform>();
                image = go.GetComponent<Image>();
                button = go.GetComponent<Button>();

                var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textGo.layer = go.layer;
                textGo.transform.SetParent(go.transform, false);
                text = textGo.GetComponent<Text>();
                text.font = GetBuiltinFont();

                var markerGo = new GameObject("SelectedMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                markerGo.layer = go.layer;
                markerGo.transform.SetParent(go.transform, false);
                markerImage = markerGo.GetComponent<Image>();
            }
        }

        go.name = "Option_" + index;
        rect.sizeDelta = LayoutPreset.DropdownOptionSize;
        image.color = index == dropdown.value
            ? ButtonHighlightColor
            : (index % 2 == 0 ? ButtonColor : ThemePreset.DropdownAltRow);

        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = ButtonHighlightColor;
        colors.pressedColor = AccentColor;
        colors.selectedColor = ButtonHighlightColor;
        colors.disabledColor = new Color(ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.45f);
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.onClick.RemoveAllListeners();
        text.text = optionText;
        text.color = TextColor;
        text.fontSize = TypographyPreset.InlineButton;
        text.alignment = TextAnchor.MiddleLeft;

        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 0f);
        textRect.offsetMax = new Vector2(-34f, 0f);

        if (markerImage != null)
        {
            var markerRect = markerImage.rectTransform;
            markerRect.anchorMin = new Vector2(1f, 0.5f);
            markerRect.anchorMax = new Vector2(1f, 0.5f);
            markerRect.pivot = new Vector2(1f, 0.5f);
            markerRect.anchoredPosition = new Vector2(LayoutPreset.DropdownMarkerX, 0f);
            markerRect.sizeDelta = LayoutPreset.DropdownMarkerSize;
            markerImage.color = AccentColor;
            markerImage.raycastTarget = false;
            markerImage.gameObject.SetActive(index == dropdown.value);
        }

        button.onClick.AddListener(() =>
        {
            dropdown.value = index;
            if (dropdown.captionText != null)
                dropdown.captionText.text = GetDropdownOptionText(dropdown);
            CloseDropdownPopup();
        });
    }

    RectTransform GetDropdownOptionTemplate()
    {
        if (_dropdownOptionTemplate == null && _dropdownPopup != null)
            _dropdownOptionTemplate = _dropdownPopup.Find("_OptionTemplate") as RectTransform;

        return _dropdownOptionTemplate;
    }

    void ConfigureDropdownOptionTemplate()
    {
        if (_dropdownOptionTemplate == null)
            return;

        _dropdownOptionTemplate.gameObject.SetActive(false);
        _dropdownOptionTemplate.SetAsLastSibling();
        _dropdownOptionTemplate.sizeDelta = LayoutPreset.DropdownOptionSize;

        var image = _dropdownOptionTemplate.GetComponent<Image>();
        if (image != null)
        {
            image.color = ButtonColor;
            image.raycastTarget = true;
        }

        var button = _dropdownOptionTemplate.GetComponent<Button>();
        if (button != null)
        {
            var colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = ButtonHighlightColor;
            colors.pressedColor = AccentColor;
            colors.selectedColor = ButtonHighlightColor;
            colors.disabledColor = new Color(ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.45f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        var text = _dropdownOptionTemplate.Find("Text")?.GetComponent<Text>();
        if (text != null)
        {
            text.font = GetBuiltinFont();
            text.color = TextColor;
            text.fontSize = TypographyPreset.InlineButton;
            text.alignment = TextAnchor.MiddleLeft;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 0f);
            textRect.offsetMax = new Vector2(-34f, 0f);
        }

        var marker = _dropdownOptionTemplate.Find("SelectedMarker")?.GetComponent<Image>();
        if (marker != null)
        {
            marker.color = AccentColor;
            marker.raycastTarget = false;
            var markerRect = marker.rectTransform;
            markerRect.anchorMin = new Vector2(1f, 0.5f);
            markerRect.anchorMax = new Vector2(1f, 0.5f);
            markerRect.pivot = new Vector2(1f, 0.5f);
            markerRect.anchoredPosition = new Vector2(LayoutPreset.DropdownMarkerX, 0f);
            markerRect.sizeDelta = LayoutPreset.DropdownMarkerSize;
            marker.gameObject.SetActive(false);
        }
    }

    void CloseDropdownPopup()
    {
        if (_dropdownPopup != null)
            _dropdownPopup.gameObject.SetActive(false);

        if (_dropdownBlocker != null)
            _dropdownBlocker.gameObject.SetActive(false);

        _dropdownPopupOwner = null;
    }

    void ConfigureOutline(GameObject target)
    {
        if (target == null)
            return;

        var rect = target.transform as RectTransform;
        if (rect == null)
            return;

        ConfigureBorderLine(rect, "_BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero);
        ConfigureBorderLine(rect, "_BorderBottom", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
        ConfigureBorderLine(rect, "_BorderLeft", Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(2f, 0f));
        ConfigureBorderLine(rect, "_BorderRight", new Vector2(1f, 0f), Vector2.one, new Vector2(-2f, 0f), Vector2.zero);
    }

    void ConfigureBorderLine(RectTransform parent, string lineName, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var line = parent.Find(lineName) as RectTransform;
        if (line == null)
        {
            var go = new GameObject(lineName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            line = go.GetComponent<RectTransform>();
        }

        line.anchorMin = anchorMin;
        line.anchorMax = anchorMax;
        line.offsetMin = offsetMin;
        line.offsetMax = offsetMax;
        line.SetAsFirstSibling();

        var image = line.GetComponent<Image>();
        if (image != null)
        {
            image.color = PanelBorderColor;
            image.raycastTarget = false;
        }
    }

    Text GetDirectTextChild(RectTransform row)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i);
            if (child.name.StartsWith("_Runtime"))
                continue;
            var text = child.GetComponent<Text>();
            if (text != null)
                return text;
        }

        return null;
    }

    Button GetDirectButtonChild(RectTransform row)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i);
            if (child.name.StartsWith("_Runtime"))
                continue;
            var button = child.GetComponent<Button>();
            if (button != null)
                return button;
        }

        return null;
    }

    Slider GetDirectSliderChild(RectTransform row)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i);
            if (child.name.StartsWith("_Runtime"))
                continue;
            var slider = child.GetComponent<Slider>();
            if (slider != null)
                return slider;
        }

        return null;
    }

    Toggle GetDirectToggleChild(RectTransform row)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i);
            if (child.name.StartsWith("_Runtime"))
                continue;
            var toggle = child.GetComponent<Toggle>();
            if (toggle != null)
                return toggle;
        }

        return null;
    }

    Dropdown GetDirectDropdownChild(RectTransform row)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            var child = row.GetChild(i);
            if (child.name.StartsWith("_Runtime"))
                continue;
            var dropdown = child.GetComponent<Dropdown>();
            if (dropdown != null)
                return dropdown;
        }

        return null;
    }

    RectTransform FindRect(string name)
    {
        var target = FindDeepChild(name);
        return target as RectTransform;
    }

    Text FindText(string name)
    {
        var target = FindDeepChild(name);
        return target != null ? target.GetComponent<Text>() : null;
    }

    Button FindButton(string name)
    {
        var target = FindDeepChild(name);
        return target != null ? target.GetComponent<Button>() : null;
    }

    Transform FindDeepChild(string name)
    {
        var children = GetComponentsInChildren<Transform>(true);
        foreach (var child in children)
        {
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    RectTransform FindOrCreateRectChild(RectTransform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null)
            return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    Image FindOrCreateImageChild(RectTransform parent, string name)
    {
        var rect = FindOrCreateRectChild(parent, name);
        var image = rect.GetComponent<Image>();
        if (image == null)
            image = rect.gameObject.AddComponent<Image>();
        return image;
    }

    Text FindOrCreateTextChild(RectTransform parent, string name)
    {
        var rect = FindOrCreateRectChild(parent, name);
        var text = rect.GetComponent<Text>();
        if (text == null)
            text = rect.gameObject.AddComponent<Text>();
        if (text.font == null)
            text.font = GetBuiltinFont();
        return text;
    }

    Font GetBuiltinFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void StretchFull(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
