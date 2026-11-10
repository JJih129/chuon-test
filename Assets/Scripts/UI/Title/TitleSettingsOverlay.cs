using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TitleSettingsOverlay : MonoBehaviour
{
    enum SettingsTab
    {
        Controls,
        Audio,
        Video,
        Accessibility,
        Game,
        Combo
    }

    enum SliderDisplayMode
    {
        Percent,
        Multiplier,
        Degrees
    }

    sealed class KeybindRow
    {
        public Button SourceButton;
        public Image ValueBoxImage;
        public Text ValueText;
        public Image ActionButtonImage;
    }

    sealed class SliderRow
    {
        public Slider Source;
        public Slider Mirror;
        public Text ValueText;
        public SliderDisplayMode DisplayMode;
    }

    sealed class ToggleRow
    {
        public Toggle Source;
        public Image ValueBoxImage;
        public Text ValueText;
    }

    sealed class CycleRow
    {
        public Dropdown Source;
        public Text ValueText;
    }

    sealed class CustomCycleRow
    {
        public Text ValueText;
        public Func<string> GetValueText;
    }

    // 반복 13: 실제 리바인드 행 (키보드·마우스 칸 + 게임패드 칸, Input System 인터랙티브 리바인딩)
    sealed class ActionBindingRow
    {
        public ChuonAction Action;
        public int KeyboardIndex;
        public bool PadEnabled;
        public Button KeyboardButton;
        public Text KeyboardText;
        public Image KeyboardImage;
        public Button PadButton;
        public Text PadText;
        public Image PadImage;
    }

    const float FadeDuration = 0.16f;
    const float SliderSyncThreshold = 0.001f;
    const string TargetFrameRateKey = "opt_target_fps";
    const int DefaultTargetFrameRate = 120;

    static readonly int[] TargetFrameRateOptions = { 60, 90, 120, 144, 165, 240 };
    static readonly Color OverlayColor = new Color(0.01f, 0.015f, 0.03f, 0.94f);
    static readonly Color FrameColor = new Color(0.035f, 0.045f, 0.07f, 0.985f);
    static readonly Color TopBarColor = new Color(0.06f, 0.25f, 0.28f, 0.72f);
    static readonly Color RailColor = new Color(0.045f, 0.055f, 0.08f, 0.985f);
    static readonly Color PanelColor = new Color(0.055f, 0.075f, 0.11f, 0.985f);
    static readonly Color SoftPanelColor = new Color(0.075f, 0.095f, 0.135f, 0.985f);
    static readonly Color RowEvenColor = new Color(0.070f, 0.092f, 0.132f, 0.985f);
    static readonly Color RowOddColor = new Color(0.082f, 0.105f, 0.145f, 0.985f);
    static readonly Color ControlColor = new Color(0.09f, 0.125f, 0.18f, 0.985f);
    static readonly Color ControlHighlightColor = new Color(0.145f, 0.205f, 0.275f, 0.985f);
    static readonly Color TextColor = new Color(0.96f, 0.98f, 1f, 0.98f);
    static readonly Color MutedTextColor = new Color(0.71f, 0.82f, 0.90f, 0.96f);
    static readonly Color CyanLineColor = new Color(0.18f, 0.90f, 0.98f, 0.78f);
    static readonly Color AmberColor = new Color(1f, 0.78f, 0.36f, 0.98f);
    static readonly Color TabIdleColor = new Color(0.08f, 0.10f, 0.14f, 0.98f);
    static readonly Color TabActiveColor = new Color(0.25f, 0.18f, 0.07f, 0.98f);
    static readonly Color SliderTrackColor = new Color(0.09f, 0.11f, 0.15f, 1f);
    static readonly Color SliderFillColor = new Color(0.22f, 0.88f, 0.97f, 0.96f);
    // 츠카사 HUD 톤 (콤보 칩 / 난이도 강조)
    static readonly Color TsukasaRedColor = new Color(0.878f, 0.188f, 0.227f, 1f);
    static readonly Color TsukasaWhiteColor = new Color(0.93f, 0.94f, 0.95f, 0.98f);
    static readonly Color ChipDarkTextColor = new Color(0.06f, 0.07f, 0.09f, 1f);

    readonly Dictionary<SettingsTab, Button> _tabButtons = new Dictionary<SettingsTab, Button>(4);
    readonly Dictionary<SettingsTab, RectTransform> _tabViews = new Dictionary<SettingsTab, RectTransform>(4);
    readonly List<KeybindRow> _keybindRows = new List<KeybindRow>(8);
    readonly List<SliderRow> _sliderRows = new List<SliderRow>(12);
    readonly List<ToggleRow> _toggleRows = new List<ToggleRow>(8);
    readonly List<CycleRow> _cycleRows = new List<CycleRow>(4);
    readonly List<CustomCycleRow> _customCycleRows = new List<CustomCycleRow>(2);
    readonly List<ActionBindingRow> _actionRows = new List<ActionBindingRow>(16);
    readonly List<SettingsTab> _tabOrder = new List<SettingsTab>(6);
    Text _rebindStatusText;
    string _rebindStatus = string.Empty;

    CanvasGroup _canvasGroup;
    RectTransform _rootRect;
    RectTransform _frameRect;
    RectTransform _tabsRect;
    RectTransform _contentRoot;
    Button _backButton;
    GameObject _settingsInstance;
    OptionsManagerAdvanced _optionsManager;
    Coroutine _fadeRoutine;
    SettingsTab _activeTab = SettingsTab.Game;
    bool _structureBuilt;
    bool _isVisible;

    public bool IsVisible => _isVisible;

    /// <summary>설정창을 열고 '콤보' 탭으로 이동 (일시정지 메뉴 C 키 / 외부 호출용).</summary>
    public void ShowComboTab()
    {
        ShowWithTab(SettingsTab.Combo);
    }

    /// <summary>설정창을 열고 '게임'(난이도) 탭으로 이동.</summary>
    public void ShowGameTab()
    {
        ShowWithTab(SettingsTab.Game);
    }

    void ShowWithTab(SettingsTab tab)
    {
        if (!_structureBuilt)
            return;

        _activeTab = tab;
        if (!_isVisible)
            Show();
        else
            ApplyTab(tab);
    }

    public bool Initialize(Transform parent, GameObject settingsPrefab)
    {
        if (settingsPrefab == null)
            return false;

        if (_structureBuilt)
            return true;

        transform.SetParent(parent, false);
        BuildStructure(settingsPrefab);
        _structureBuilt = _optionsManager != null;
        gameObject.SetActive(false);
        return _structureBuilt;
    }

    public void Show()
    {
        if (!_structureBuilt)
            return;

        gameObject.SetActive(true);
        _isVisible = true;
        RefreshAllRows();
        ApplyTab(_activeTab);
        FadeTo(1f, true);
        FocusActiveTabButton();
    }

    public void Hide()
    {
        if (!_structureBuilt || !_isVisible)
            return;

        CancelPendingRebind();
        if (ChuonInputRebinding.IsRebinding)
            ChuonInputRebinding.Cancel();
        _isVisible = false;
        FadeTo(0f, false);
        OptionsManagerAdvanced.FlushPrefs(); // 닫을 때 설정을 디스크에 기록
    }

    void Update()
    {
        if (!_isVisible || _optionsManager == null)
            return;

        SyncKeybindRows();
        SyncSliderRows();
        SyncToggleRows();
        SyncCycleRows();
        SyncActionRows();

        // 패드 LB / RB = 탭 전환
        int tabDelta = ChuonInput.PadTabDelta();
        if (tabDelta != 0 && _tabOrder.Count > 0)
        {
            int index = Mathf.Max(0, _tabOrder.IndexOf(_activeTab));
            index = (index + tabDelta + _tabOrder.Count) % _tabOrder.Count;
            ApplyTab(_tabOrder[index]);
            FocusActiveTabButton();
        }

        KeepSelectionVisible();
    }

    void OnDestroy()
    {
        if (_settingsInstance != null)
            Destroy(_settingsInstance);
    }

    void BuildStructure(GameObject settingsPrefab)
    {
        _rootRect = GetOrAdd<RectTransform>(gameObject);
        Stretch(_rootRect);

        Image overlay = GetOrAdd<Image>(gameObject);
        overlay.color = OverlayColor;
        overlay.raycastTarget = true;

        Button rootButton = GetComponent<Button>();
        if (rootButton != null)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.enabled = false;
        }

        _canvasGroup = GetOrAdd<CanvasGroup>(gameObject);
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        BuildShell();
        InstantiateBackend(settingsPrefab);
        if (_optionsManager == null)
            return;

        BuildTabs();
        BuildBackButton();
        BuildViews();
        ApplyTab(_activeTab);
    }

    void BuildShell()
    {
        _frameRect = CreateRect("Frame", transform);
        SetRect(_frameRect, new Vector2(0.012f, 0.02f), new Vector2(0.988f, 0.98f));
        _frameRect.gameObject.AddComponent<Image>().color = FrameColor;

        AddLine("FrameTop", _frameRect, new Vector2(0f, 0.995f), new Vector2(1f, 1f), CyanLineColor);
        AddLine("FrameBottom", _frameRect, new Vector2(0f, 0f), new Vector2(1f, 0.005f), new Color(CyanLineColor.r, CyanLineColor.g, CyanLineColor.b, 0.52f));
        AddLine("FrameLeft", _frameRect, new Vector2(0f, 0f), new Vector2(0.0022f, 1f), CyanLineColor);
        AddLine("FrameRight", _frameRect, new Vector2(0.9978f, 0f), new Vector2(1f, 1f), CyanLineColor);

        RectTransform topBar = CreateRect("TopBar", _frameRect);
        SetRect(topBar, new Vector2(0f, 0.90f), new Vector2(1f, 1f));
        topBar.gameObject.AddComponent<Image>().color = TopBarColor;
        CreateText("TopCaption", topBar, "시스템 설정 / 프로젝트 추온", 12, FontStyle.Bold, TextAnchor.MiddleLeft, MutedTextColor, new Vector2(0.03f, 0.12f), new Vector2(0.70f, 0.92f));

        RectTransform leftRail = CreateRect("LeftRail", _frameRect);
        SetRect(leftRail, new Vector2(0.018f, 0.04f), new Vector2(0.255f, 0.90f));
        leftRail.gameObject.AddComponent<Image>().color = RailColor;
        AddLine("RailTop", leftRail, new Vector2(0.05f, 0.94f), new Vector2(0.95f, 0.95f), CyanLineColor);
        AddLine("RailBottom", leftRail, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.06f), new Color(CyanLineColor.r, CyanLineColor.g, CyanLineColor.b, 0.45f));
        AddLine("RailGlow", leftRail, new Vector2(0.05f, 0.89f), new Vector2(0.33f, 0.90f), AmberColor);

        CreateText("RailTitle", leftRail, "설정", 32, FontStyle.Bold, TextAnchor.UpperLeft, TextColor, new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.94f));
        CreateText("RailSubtitle", leftRail, "소울라이크 설정\n프로젝트 추온", 12, FontStyle.Bold, TextAnchor.UpperLeft, MutedTextColor, new Vector2(0.08f, 0.64f), new Vector2(0.92f, 0.76f));
        CreateText("RailDesc", leftRail, "전투 가독성, 프레임, 사운드,\n자막과 카메라 편의를\n타이틀에서 바로 조정합니다.", 14, FontStyle.Normal, TextAnchor.UpperLeft, MutedTextColor, new Vector2(0.08f, 0.42f), new Vector2(0.92f, 0.56f));
        CreateText("RailHint", leftRail, "ESC 또는 뒤로 버튼으로 닫습니다.\n모든 값은 즉시 반영됩니다.", 12, FontStyle.Normal, TextAnchor.LowerLeft, MutedTextColor, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.16f));

        _tabsRect = CreateRect("Tabs", _frameRect);
        SetRect(_tabsRect, new Vector2(0.275f, 0.915f), new Vector2(0.80f, 0.975f));
        HorizontalLayoutGroup tabsLayout = _tabsRect.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 14f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;
        tabsLayout.childForceExpandHeight = true;

        RectTransform contentFrame = CreateRect("ContentFrame", _frameRect);
        SetRect(contentFrame, new Vector2(0.275f, 0.04f), new Vector2(0.985f, 0.88f));
        contentFrame.gameObject.AddComponent<Image>().color = PanelColor;
        AddLine("ContentTop", contentFrame, new Vector2(0.015f, 0.985f), new Vector2(0.985f, 0.99f), CyanLineColor);
        AddLine("ContentAccent", contentFrame, new Vector2(0.015f, 0.985f), new Vector2(0.18f, 0.99f), AmberColor);
        AddLine("ContentBottom", contentFrame, new Vector2(0.015f, 0.01f), new Vector2(0.985f, 0.015f), new Color(CyanLineColor.r, CyanLineColor.g, CyanLineColor.b, 0.35f));

        _contentRoot = CreateRect("ContentRoot", contentFrame);
        SetRect(_contentRoot, new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.97f));
    }

    void InstantiateBackend(GameObject settingsPrefab)
    {
        RectTransform hiddenRoot = CreateRect("RuntimeSettingsBackend", _frameRect);
        hiddenRoot.anchorMin = Vector2.zero;
        hiddenRoot.anchorMax = Vector2.zero;
        hiddenRoot.pivot = Vector2.zero;
        hiddenRoot.sizeDelta = new Vector2(16f, 16f);
        hiddenRoot.anchoredPosition = new Vector2(-10000f, -10000f);

        _settingsInstance = Instantiate(settingsPrefab, hiddenRoot, false);
        _optionsManager = _settingsInstance.GetComponentInChildren<OptionsManagerAdvanced>(true);
        if (_optionsManager == null)
        {
            Debug.LogError("[TitleSettingsOverlay] OptionsManagerAdvanced is missing.", this);
            return;
        }

        PauseSettingsOverlayStyler legacyStyler = _settingsInstance.GetComponent<PauseSettingsOverlayStyler>();
        if (legacyStyler != null)
            legacyStyler.enabled = false;

        CanvasGroup backendCanvasGroup = GetOrAdd<CanvasGroup>(_settingsInstance);
        backendCanvasGroup.alpha = 0f;
        backendCanvasGroup.blocksRaycasts = false;
        backendCanvasGroup.interactable = false;

        string[] hiddenNames =
        {
            "Panel_Back","Panel_Controls","Panel_Audio","Panel_Video","Panel_Bottom",
            "Button_Save","Button_Cancel","_OverlayTabs","_TabControls","_TabAudio","_TabVideo",
            "_OverlayTopGlow","_OverlayBottomGlow","_OverlayLeftBar","_OverlayRightBar",
            "_OverlayHeaderLine","_OverlayCornerTL","_OverlayCornerTR","_OverlayCornerBL",
            "_OverlayCornerBR","_RuntimeDropdownBlocker","_RuntimeDropdownPopup","_OptionTemplate",
            "_RebindHintPanel","_FooterHint"
        };

        for (int i = 0; i < hiddenNames.Length; i++)
        {
            Transform child = FindChildRecursive(_settingsInstance.transform, hiddenNames[i]);
            if (child != null)
                child.gameObject.SetActive(false);
        }

        HideBackendVisuals(_settingsInstance);
    }

    void BuildTabs()
    {
        CreateTabButton("게임", SettingsTab.Game);
        CreateTabButton("조작", SettingsTab.Controls);
        CreateTabButton("사운드", SettingsTab.Audio);
        CreateTabButton("화면", SettingsTab.Video);
        CreateTabButton("접근성", SettingsTab.Accessibility);
        Button comboTab = CreateTabButton("콤보 / COMBO", SettingsTab.Combo);
        LayoutElement comboLayout = comboTab.GetComponent<LayoutElement>();
        if (comboLayout != null)
            comboLayout.preferredWidth = 156f;
    }

    void BuildBackButton()
    {
        _backButton = CreateButton("BackButton", _frameRect, "뒤로", 16);
        SetRect(_backButton.GetComponent<RectTransform>(), new Vector2(0.865f, 0.925f), new Vector2(0.975f, 0.975f));
        _backButton.onClick.AddListener(Hide);
    }

    void BuildViews()
    {
        BuildControlsView();
        BuildAudioView();
        BuildVideoView();
        BuildAccessibilityView();
        BuildGameView();
        BuildComboView();
    }

    void BuildControlsView()
    {
        RectTransform content = CreateTabView(SettingsTab.Controls, "조작", "핵심 이동과 공격 입력을 재배치하고,\n현재 빌드 기준 전투 기본 조작을 확인합니다.");

        int rowIndex = 0;
        CreateSectionHeader(content, "키 설정  ·  키보드·마우스 | 게임패드", "칸을 선택해 새 키를 누르면 바로 바뀝니다. 겹치는 키는 서로 교체됩니다. ESC / START = 취소.");
        CreateRebindStatusRow(content);
        CreateActionBindingRow(content, "이동 (앞)", ChuonAction.Move, ChuonInput.MoveUpIndex, rowIndex++);
        CreateActionBindingRow(content, "이동 (뒤)", ChuonAction.Move, ChuonInput.MoveDownIndex, rowIndex++);
        CreateActionBindingRow(content, "이동 (왼쪽)", ChuonAction.Move, ChuonInput.MoveLeftIndex, rowIndex++);
        CreateActionBindingRow(content, "이동 (오른쪽)", ChuonAction.Move, ChuonInput.MoveRightIndex, rowIndex++);
        CreateActionBindingRow(content, "기본공격", ChuonAction.AttackLight, 0, rowIndex++);
        CreateActionBindingRow(content, "강공격", ChuonAction.AttackHeavy, 0, rowIndex++);
        CreateActionBindingRow(content, "가드 / 패링", ChuonAction.Guard, 0, rowIndex++);
        CreateActionBindingRow(content, "회피", ChuonAction.Dodge, 0, rowIndex++);
        CreateActionBindingRow(content, "락온", ChuonAction.LockOn, 0, rowIndex++);
        CreateActionBindingRow(content, "락온 (보조)", ChuonAction.LockOn, 1, rowIndex++);
        CreateActionBindingRow(content, "상호작용", ChuonAction.Interact, 0, rowIndex++);
        CreateActionBindingRow(content, "궁극기", ChuonAction.Ultimate, 0, rowIndex++);
        CreateActionBindingRow(content, "회복 앰플", ChuonAction.Heal, 0, rowIndex++);
        CreateResetBindingsRow(content, rowIndex++);

        CreateSectionHeader(content, "고정 키", "전투와 디버그 관련 빠른 전환 키입니다 (변경 불가).");
        CreateStaticBindingRow(content, "일시정지", "ESC  |  START", "일시정지 메뉴 · 메뉴에서 B = 뒤로, LB / RB = 탭 전환", rowIndex++);
        CreateStaticBindingRow(content, "카메라", "마우스  |  R 스틱", "락온 중 스틱을 튕기면 대상 전환", rowIndex++);
        CreateStaticBindingRow(content, "콤보 표", "C  |  Y", "콤보 루트 A–E 입력표 (일시정지 중)", rowIndex++);
        CreateStaticBindingRow(content, "성능 HUD", "F11", "좌상단 프레임 / ms 표시 토글", rowIndex++);
        CreateStaticBindingRow(content, "전투 디버그 HUD", "F12", "우상단 전투 상태 디버그 토글", rowIndex++);

        CreateSectionHeader(content, "카메라", "시점 감도와 방향을 조정합니다. 변경 즉시 적용됩니다.");
        CreateSliderRow(content, "마우스 감도", _optionsManager.lookSensitivityMouseSlider, rowIndex++, SliderDisplayMode.Multiplier);
        CreateSliderRow(content, "패드 감도", _optionsManager.lookSensitivityPadSlider, rowIndex++, SliderDisplayMode.Multiplier);
        CreateToggleRow(content, "상하 반전", _optionsManager.invertYToggle, rowIndex++);
        CreateToggleRow(content, "카메라 자동 정렬", _optionsManager.autoRecenterToggle, rowIndex++);
        CreateStaticBindingRow(content, "카메라 리셋", "TAB (타깃 없음)", "락온 대상이 없을 때 탭하면 등 뒤로", rowIndex++);
    }

    void BuildAudioView()
    {
        RectTransform content = CreateTabView(SettingsTab.Audio, "사운드", "전투 타격감과 환경음, 보이스 밸런스를\n개별 항목으로 조정합니다.");

        int rowIndex = 0;
        CreateSectionHeader(content, "메인 믹스", "전체 볼륨과 메인 밸런스를 조정합니다.");
        CreateSliderRow(content, "마스터 볼륨", _optionsManager.masterSlider, rowIndex++, SliderDisplayMode.Percent);

        CreateSectionHeader(content, "전투 / 환경", "전투 사운드와 배경 사운드를 분리 조정합니다.");
        CreateSliderRow(content, "배경음 볼륨", _optionsManager.musicSlider, rowIndex++, SliderDisplayMode.Percent);
        CreateSliderRow(content, "효과음 볼륨", _optionsManager.sfxSlider, rowIndex++, SliderDisplayMode.Percent);
        CreateSliderRow(content, "보이스 볼륨", _optionsManager.voiceSlider, rowIndex++, SliderDisplayMode.Percent);
    }

    void BuildVideoView()
    {
        RectTransform content = CreateTabView(SettingsTab.Video, "화면", "프레임 우선 소울라이크 전투 기준으로\n디스플레이와 렌더링을 조정합니다.");

        int rowIndex = 0;
        CreateSectionHeader(content, "디스플레이", "모니터 환경에 맞는 기본 화면 설정입니다.");
        CreateCycleRow(content, "해상도", _optionsManager.resolutionDropdown, rowIndex++);
        CreateToggleRow(content, "전체 화면", _optionsManager.fullscreenToggle, rowIndex++);
        CreateToggleRow(content, "수직 동기화", _optionsManager.vSyncToggle, rowIndex++);

        CreateSectionHeader(content, "프레임 / 가독성", "반응성과 전투 가독성에 직접 영향을 주는 항목입니다.");
        CreateTargetFrameRateRow(content, rowIndex++);
        CreateSliderRow(content, "밝기", _optionsManager.brightnessSlider, rowIndex++, SliderDisplayMode.Percent);
        CreateSliderRow(content, "시야각 (FOV)", _optionsManager.cameraFovSlider, rowIndex++, SliderDisplayMode.Degrees);

        CreateSectionHeader(content, "렌더링", "그래픽 품질과 잔상 표현을 조정합니다.");
        CreateCycleRow(content, "그래픽 품질", _optionsManager.qualityDropdown, rowIndex++);
        CreateToggleRow(content, "모션 블러", _optionsManager.motionBlurToggle, rowIndex++);
    }

    void BuildAccessibilityView()
    {
        RectTransform content = CreateTabView(SettingsTab.Accessibility, "접근성", "자막, 카메라 흔들림, 화면 읽기성을\n전투 기준으로 조정합니다.");

        int rowIndex = 0;
        CreateSectionHeader(content, "자막", "컷신과 이벤트 텍스트 가독성을 조정합니다.");
        CreateSliderRow(content, "자막 크기", _optionsManager.subtitleSizeSlider, rowIndex++, SliderDisplayMode.Multiplier);
        CreateToggleRow(content, "자막 배경", _optionsManager.subtitleBackgroundToggle, rowIndex++);

        CreateSectionHeader(content, "카메라 편의", "강한 연출과 화면 흔들림을 제어합니다.");
        CreateSliderRow(content, "카메라 흔들림 강도", _optionsManager.cameraShakeSlider, rowIndex++, SliderDisplayMode.Percent);
        CreateSliderRow(content, "화면 번쩍임 강도", _optionsManager.flashIntensitySlider, rowIndex++, SliderDisplayMode.Percent);
        CreateToggleRow(content, "흑백 흡수 연출", _optionsManager.bwAbsorbToggle, rowIndex++);
        CreateStaticBindingRow(content, "권장 프리셋", "흔들림 40% 이하", "보스전 가독성 중심 권장값", rowIndex++);

        CreateSectionHeader(content, "연출", "컷신 레터박스와 재도전 시 생략을 설정합니다. 길게 눌러 언제든 건너뛸 수 있습니다.");
        CreateToggleRow(content, "레터박스", _optionsManager.cinematicLetterboxToggle, rowIndex++);
        CreateToggleRow(content, "본 연출 다시 보지 않기", _optionsManager.cinematicSkipSeenToggle, rowIndex++);
    }

    // ---- 게임 탭: 난이도 선택 (GameDifficulty, 즉시 반영) ----
    static string[] s_difficultyValueLabels;

    void BuildGameView()
    {
        RectTransform content = CreateTabView(SettingsTab.Game, "게임 / GAME", "난이도와 전투 기본 규칙을 확인합니다.\n난이도는 즉시 반영되며 보스전 중에도 바꿀 수 있습니다.");

        int rowIndex = 0;
        CreateSectionHeader(content, "난이도 / DIFFICULTY", "Type-D 보스전의 공격 속도·피해·패링 판정에 반영됩니다. 기본값은 보통입니다.");
        CreateDifficultyRow(content, rowIndex++);

        CreateSectionHeader(content, "전투 규칙 / FIELD RULES", "츠카사 경비 체계와 싸우기 위한 기본 규칙입니다.");
        CreateStaticBindingRow(content, "발광 색", "금 / 빨강 / 자홍", "금색 = 패링 가능 · 빨강 = 회피 · 자홍 = 가드·패링 불가", rowIndex++);
        CreateStaticBindingRow(content, "페이즈 코어", "5단 충전", "코어가 5단까지 차면 R 로 궁극기. 보스전마다 1회만 사용", rowIndex++);
        CreateStaticBindingRow(content, "브레이크", "게이지 최대", "적이 5초간 그로기 상태가 되고 피니셔를 넣을 수 있다", rowIndex++);
        CreateStaticBindingRow(content, "연속 회피", "3회째부터", "연속 회피는 3번째부터 무적 시간이 짧아진다", rowIndex++);
    }

    void CreateDifficultyRow(RectTransform parent, int rowIndex)
    {
        RectTransform holder = CreateRow(parent, "난이도", rowIndex);

        Button previousButton = CreateButton("PrevButton", holder, "<", 16);
        SetRect(previousButton.GetComponent<RectTransform>(), new Vector2(0.00f, 0f), new Vector2(0.13f, 1f));
        previousButton.onClick.AddListener(() => StepDifficulty(-1));

        Image valueBox = CreatePanel("ValueBox", holder, ControlColor);
        SetRect(valueBox.rectTransform, new Vector2(0.17f, 0f), new Vector2(0.83f, 1f));
        AddLine("DifficultyAccent", valueBox.rectTransform, new Vector2(0.02f, 0.06f), new Vector2(0.98f, 0.10f), TsukasaRedColor);
        Text valueText = CreateText("ValueText", valueBox.rectTransform, GetDifficultyValueLabel(), 16, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));

        Button nextButton = CreateButton("NextButton", holder, ">", 16);
        SetRect(nextButton.GetComponent<RectTransform>(), new Vector2(0.87f, 0f), new Vector2(1f, 1f));
        nextButton.onClick.AddListener(() => StepDifficulty(1));

        _customCycleRows.Add(new CustomCycleRow
        {
            ValueText = valueText,
            GetValueText = GetDifficultyValueLabel
        });

        // 설명 줄 (선택값 바로 아래)
        RectTransform descRow = CreateRect("Row_DifficultyDescription", parent);
        FitHeight(descRow, 54f);
        descRow.gameObject.AddComponent<Image>().color = rowIndex % 2 == 0 ? RowEvenColor : RowOddColor;
        AddLine("DescAccent", descRow, new Vector2(0.01f, 0.12f), new Vector2(0.013f, 0.88f), TsukasaRedColor);
        CreateText("Tag", descRow, "MODE NOTE", 11, FontStyle.Bold, TextAnchor.MiddleLeft, MutedTextColor, new Vector2(0.03f, 0.10f), new Vector2(0.30f, 0.90f));
        Text descText = CreateText("DescText", descRow, GetDifficultyDescription(), 14, FontStyle.Normal, TextAnchor.MiddleLeft, TextColor, new Vector2(0.33f, 0.08f), new Vector2(0.97f, 0.92f));

        _customCycleRows.Add(new CustomCycleRow
        {
            ValueText = descText,
            GetValueText = GetDifficultyDescription
        });
    }

    void StepDifficulty(int direction)
    {
        GameDifficulty.Step(direction);
        SyncCycleRows();
    }

    static string GetDifficultyValueLabel()
    {
        BossDifficultyTier[] tiers = GameDifficulty.Selectable;
        if (s_difficultyValueLabels == null)
        {
            s_difficultyValueLabels = new string[tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
                s_difficultyValueLabels[i] = GameDifficulty.Label(tiers[i]) + "   //   " + tiers[i].ToString().ToUpperInvariant();
        }

        int index = Array.IndexOf(tiers, GameDifficulty.Current);
        if (index < 0 || index >= s_difficultyValueLabels.Length)
            return GameDifficulty.CurrentLabel;

        return s_difficultyValueLabels[index];
    }

    static string GetDifficultyDescription()
    {
        return GameDifficulty.Description(GameDifficulty.Current);
    }

    // ---- 콤보 탭: ComboRouteCatalog (튜토리얼 공격 배치표와 같은 데이터) ----
    void BuildComboView()
    {
        RectTransform content = CreateTabView(SettingsTab.Combo, "콤보 / COMBO", "튜토리얼 공격 배치표와 같은 콤보 루트 5종입니다.\n입력 간격을 짧게 유지하면 다음 타로 이어집니다.");

        int rowIndex = 0;
        CreateSectionHeader(content, "입력 / INPUT", "약공과 강공 두 버튼의 순서로 루트가 갈립니다.");
        CreateStaticBindingRow(content, ComboRouteCatalog.LightLabel, ComboRouteCatalog.LightKey + "  ·  좌클릭", "빠른 연결 · 대부분의 루트 시작", rowIndex++);
        CreateStaticBindingRow(content, ComboRouteCatalog.HeavyLabel, ComboRouteCatalog.HeavyKey + "  ·  우클릭", "무거운 일격 · 연계가 깊을수록 피해 증가", rowIndex++);

        CreateSectionHeader(content, "콤보 루트 A–E / ROUTES", "히트 이후에는 회피로 언제든 연계를 끊을 수 있습니다.");
        ComboRoute[] routes = ComboRouteCatalog.Routes;
        for (int i = 0; i < routes.Length; i++)
            CreateComboRouteRow(content, routes[i], rowIndex++);
    }

    void CreateComboRouteRow(RectTransform parent, ComboRoute route, int rowIndex)
    {
        if (route == null)
            return;

        RectTransform row = CreateRect("Row_Combo_" + route.Id, parent);
        FitHeight(row, 96f);
        row.gameObject.AddComponent<Image>().color = rowIndex % 2 == 0 ? RowEvenColor : RowOddColor;
        AddLine("RowAccent", row, new Vector2(0.01f, 0.08f), new Vector2(0.013f, 0.92f), TsukasaRedColor);

        CreateText("RouteId", row, route.Id, 34, FontStyle.Bold, TextAnchor.MiddleCenter, TsukasaRedColor, new Vector2(0.02f, 0.10f), new Vector2(0.08f, 0.90f));
        CreateText("RouteName", row, route.Name, 17, FontStyle.Bold, TextAnchor.LowerLeft, TextColor, new Vector2(0.09f, 0.50f), new Vector2(0.25f, 0.86f));
        CreateText("RouteMeta", row, "ROUTE " + route.Id + "  ·  " + route.Length + " HIT", 11, FontStyle.Bold, TextAnchor.UpperLeft, MutedTextColor, new Vector2(0.09f, 0.14f), new Vector2(0.25f, 0.46f));

        RectTransform chips = CreateRect("Chips", row);
        SetRect(chips, new Vector2(0.26f, 0.12f), new Vector2(0.66f, 0.88f));

        const float chipWidth = 50f;
        const float arrowWidth = 20f;
        float x = 0f;
        for (int i = 0; i < route.Inputs.Length; i++)
        {
            if (i > 0)
            {
                Text arrow = CreateText("Arrow" + i, chips, ">", 16, FontStyle.Bold, TextAnchor.MiddleCenter, TsukasaRedColor, new Vector2(0f, 0f), new Vector2(0f, 1f));
                PlaceLeft(arrow.rectTransform, x, arrowWidth);
                x += arrowWidth;
            }

            CreateComboChip(chips, route.Inputs[i], i, x, chipWidth);
            x += chipWidth;
        }

        CreateText("RouteNote", row, route.Note, 13, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor, new Vector2(0.67f, 0.10f), new Vector2(0.98f, 0.90f));
    }

    static void CreateComboChip(RectTransform parent, ComboInputKind input, int index, float x, float width)
    {
        bool heavy = input == ComboInputKind.Heavy;
        Image chip = CreatePanel("Chip" + index, parent, heavy ? TsukasaRedColor : TsukasaWhiteColor);
        chip.raycastTarget = false;
        PlaceLeft(chip.rectTransform, x, width);

        Color textColor = heavy ? TsukasaWhiteColor : ChipDarkTextColor;
        CreateText("Input", chip.rectTransform, ComboRouteCatalog.ShortLabelOf(input), 18, FontStyle.Bold, TextAnchor.MiddleCenter, textColor, new Vector2(0f, 0.38f), new Vector2(1f, 1f));
        CreateText("Key", chip.rectTransform, ComboRouteCatalog.KeyOf(input), 11, FontStyle.Bold, TextAnchor.MiddleCenter, textColor, new Vector2(0f, 0.02f), new Vector2(1f, 0.40f));
    }

    static void PlaceLeft(RectTransform rect, float x, float width)
    {
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(x, 0f);
        rect.offsetMax = new Vector2(x + width, 0f);
    }

    RectTransform CreateTabView(SettingsTab tab, string title, string description)
    {
        RectTransform view = CreateRect(tab + "View", _contentRoot);
        Stretch(view);
        view.gameObject.SetActive(false);

        RectTransform viewport = CreateRect("Viewport", view);
        Stretch(viewport);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        ScrollRect scrollRect = view.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 34f;
        scrollRect.viewport = viewport;

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(-12f, 0f);
        scrollRect.content = content;

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 16f;
        layout.padding = new RectOffset(20, 20, 18, 26);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = CreateRect("Header", content);
        FitHeight(header, 118f);
        header.gameObject.AddComponent<Image>().color = SoftPanelColor;
        AddLine("HeaderTop", header, new Vector2(0.01f, 0.97f), new Vector2(0.99f, 0.99f), CyanLineColor);
        AddLine("HeaderAccent", header, new Vector2(0.01f, 0.97f), new Vector2(0.22f, 0.99f), AmberColor);
        CreateText("Title", header, title, 24, FontStyle.Bold, TextAnchor.UpperLeft, CyanLineColor, new Vector2(0.03f, 0.56f), new Vector2(0.97f, 0.88f));
        CreateText("Description", header, description, 13, FontStyle.Normal, TextAnchor.UpperLeft, MutedTextColor, new Vector2(0.03f, 0.16f), new Vector2(0.97f, 0.50f));

        _tabViews[tab] = view;
        return content;
    }

    Button CreateTabButton(string label, SettingsTab tab)
    {
        Button button = CreateButton(tab + "TabButton", _tabsRect, label, 15);
        button.onClick.AddListener(() => ApplyTab(tab));

        LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = 128f;

        _tabButtons[tab] = button;
        if (!_tabOrder.Contains(tab))
            _tabOrder.Add(tab);
        return button;
    }

    void CreateKeybindRow(RectTransform parent, string label, Button sourceButton, int rowIndex)
    {
        RectTransform holder = CreateRow(parent, label, rowIndex);

        Image valueBox = CreatePanel("ValueBox", holder, ControlColor);
        SetRect(valueBox.rectTransform, new Vector2(0.00f, 0f), new Vector2(0.56f, 1f));
        Text valueText = CreateText("ValueText", valueBox.rectTransform, ReadBindingValue(sourceButton), 16, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, 0f), new Vector2(1f, 1f));

        Button actionButton = CreateButton("ActionButton", holder, "리바인드", 15);
        SetRect(actionButton.GetComponent<RectTransform>(), new Vector2(0.62f, 0f), new Vector2(1f, 1f));
        actionButton.onClick.AddListener(() =>
        {
            if (sourceButton == null || _optionsManager == null || _optionsManager.IsWaitingForRebind)
                return;

            sourceButton.onClick.Invoke();
            SyncKeybindRows();
        });

        _keybindRows.Add(new KeybindRow
        {
            SourceButton = sourceButton,
            ValueBoxImage = valueBox,
            ValueText = valueText,
            ActionButtonImage = actionButton.GetComponent<Image>()
        });
    }

    void CreateSliderRow(RectTransform parent, string label, Slider sourceSlider, int rowIndex, SliderDisplayMode displayMode)
    {
        if (sourceSlider == null)
            return;

        RectTransform holder = CreateRow(parent, label, rowIndex);

        Slider mirror = CreateStyledSlider(holder);
        SetRect(mirror.GetComponent<RectTransform>(), new Vector2(0.00f, 0.18f), new Vector2(0.74f, 0.82f));
        mirror.minValue = sourceSlider.minValue;
        mirror.maxValue = sourceSlider.maxValue;
        mirror.wholeNumbers = sourceSlider.wholeNumbers;
        mirror.SetValueWithoutNotify(sourceSlider.value);
        mirror.onValueChanged.AddListener(value =>
        {
            sourceSlider.value = value;
            RefreshSliderRow(sourceSlider, displayMode);
        });

        Image valueBox = CreatePanel("ValueBox", holder, ControlColor);
        SetRect(valueBox.rectTransform, new Vector2(0.79f, 0f), new Vector2(1f, 1f));
        Text valueText = CreateText("ValueText", valueBox.rectTransform, FormatSliderValue(sourceSlider.value, displayMode), 16, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, 0f), new Vector2(1f, 1f));

        _sliderRows.Add(new SliderRow
        {
            Source = sourceSlider,
            Mirror = mirror,
            ValueText = valueText,
            DisplayMode = displayMode
        });
    }

    void CreateToggleRow(RectTransform parent, string label, Toggle sourceToggle, int rowIndex)
    {
        if (sourceToggle == null)
            return;

        RectTransform holder = CreateRow(parent, label, rowIndex);
        Image valueBox = CreatePanel("ToggleBox", holder, ControlColor);
        SetRect(valueBox.rectTransform, new Vector2(0.48f, 0f), new Vector2(1f, 1f));

        Button valueButton = valueBox.gameObject.AddComponent<Button>();
        valueButton.targetGraphic = valueBox;
        valueButton.onClick.AddListener(() =>
        {
            sourceToggle.isOn = !sourceToggle.isOn;
            RefreshToggleRow(sourceToggle);
        });

        Text valueText = CreateText("ValueText", valueBox.rectTransform, "꺼짐", 16, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, 0f), new Vector2(1f, 1f));

        _toggleRows.Add(new ToggleRow
        {
            Source = sourceToggle,
            ValueBoxImage = valueBox,
            ValueText = valueText
        });
    }

    void CreateCycleRow(RectTransform parent, string label, Dropdown sourceDropdown, int rowIndex)
    {
        if (sourceDropdown == null)
            return;

        RectTransform holder = CreateRow(parent, label, rowIndex);

        Button previousButton = CreateButton("PrevButton", holder, "<", 16);
        SetRect(previousButton.GetComponent<RectTransform>(), new Vector2(0.00f, 0f), new Vector2(0.13f, 1f));
        previousButton.onClick.AddListener(() => CycleDropdown(sourceDropdown, -1));

        Image valueBox = CreatePanel("ValueBox", holder, ControlColor);
        SetRect(valueBox.rectTransform, new Vector2(0.17f, 0f), new Vector2(0.83f, 1f));
        Text valueText = CreateText("ValueText", valueBox.rectTransform, ReadDropdownValue(sourceDropdown), 15, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));

        Button nextButton = CreateButton("NextButton", holder, ">", 16);
        SetRect(nextButton.GetComponent<RectTransform>(), new Vector2(0.87f, 0f), new Vector2(1f, 1f));
        nextButton.onClick.AddListener(() => CycleDropdown(sourceDropdown, 1));

        _cycleRows.Add(new CycleRow
        {
            Source = sourceDropdown,
            ValueText = valueText
        });
    }

    void CreateTargetFrameRateRow(RectTransform parent, int rowIndex)
    {
        RectTransform holder = CreateRow(parent, "목표 프레임", rowIndex);

        Button previousButton = CreateButton("PrevButton", holder, "<", 16);
        SetRect(previousButton.GetComponent<RectTransform>(), new Vector2(0.00f, 0f), new Vector2(0.13f, 1f));
        previousButton.onClick.AddListener(() => CycleTargetFrameRate(-1));

        Image valueBox = CreatePanel("ValueBox", holder, ControlColor);
        SetRect(valueBox.rectTransform, new Vector2(0.17f, 0f), new Vector2(0.83f, 1f));
        Text valueText = CreateText("ValueText", valueBox.rectTransform, GetTargetFrameRateLabel(), 15, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));

        Button nextButton = CreateButton("NextButton", holder, ">", 16);
        SetRect(nextButton.GetComponent<RectTransform>(), new Vector2(0.87f, 0f), new Vector2(1f, 1f));
        nextButton.onClick.AddListener(() => CycleTargetFrameRate(1));

        _customCycleRows.Add(new CustomCycleRow
        {
            ValueText = valueText,
            GetValueText = GetTargetFrameRateLabel
        });
    }

    void CreateSectionHeader(RectTransform parent, string title, string description)
    {
        RectTransform section = CreateRect("Section_" + title.Replace(" ", "_"), parent);
        FitHeight(section, 72f);
        section.gameObject.AddComponent<Image>().color = SoftPanelColor;
        AddLine("SectionLine", section, new Vector2(0.015f, 0.90f), new Vector2(0.985f, 0.94f), new Color(CyanLineColor.r, CyanLineColor.g, CyanLineColor.b, 0.55f));
        AddLine("SectionAccent", section, new Vector2(0.015f, 0.90f), new Vector2(0.22f, 0.94f), AmberColor);
        CreateText("Title", section, title, 15, FontStyle.Bold, TextAnchor.UpperLeft, AmberColor, new Vector2(0.03f, 0.44f), new Vector2(0.97f, 0.84f));
        CreateText("Description", section, description, 12, FontStyle.Normal, TextAnchor.LowerLeft, MutedTextColor, new Vector2(0.03f, 0.10f), new Vector2(0.97f, 0.42f));
    }

    void CreateStaticBindingRow(RectTransform parent, string label, string binding, string description, int rowIndex)
    {
        RectTransform holder = CreateRow(parent, label, rowIndex);

        Image valueBox = CreatePanel("ValueBox", holder, ControlHighlightColor);
        SetRect(valueBox.rectTransform, new Vector2(0.00f, 0f), new Vector2(0.35f, 1f));
        CreateText("ValueText", valueBox.rectTransform, binding, 15, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, 0f), new Vector2(1f, 1f));

        Image descBox = CreatePanel("DescBox", holder, ControlColor);
        SetRect(descBox.rectTransform, new Vector2(0.40f, 0f), new Vector2(1f, 1f));
        CreateText("DescText", descBox.rectTransform, description, 12, FontStyle.Normal, TextAnchor.MiddleLeft, MutedTextColor, new Vector2(0.05f, 0f), new Vector2(0.95f, 1f));
    }

    RectTransform CreateRow(RectTransform parent, string label, int rowIndex)
    {
        RectTransform row = CreateRect("Row_" + label.Replace(" ", "_"), parent);
        FitHeight(row, 84f);
        row.gameObject.AddComponent<Image>().color = rowIndex % 2 == 0 ? RowEvenColor : RowOddColor;
        AddLine("RowAccent", row, new Vector2(0.01f, 0.08f), new Vector2(0.013f, 0.92f), rowIndex % 2 == 0 ? CyanLineColor : AmberColor);

        CreateText("Label", row, label, 16, FontStyle.Bold, TextAnchor.MiddleLeft, TextColor, new Vector2(0.03f, 0.16f), new Vector2(0.30f, 0.84f));

        RectTransform holder = CreateRect("Holder", row);
        SetRect(holder, new Vector2(0.33f, 0.16f), new Vector2(0.97f, 0.84f));
        return holder;
    }

    Slider CreateStyledSlider(Transform parent)
    {
        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        sliderRect.SetParent(parent, false);
        sliderRect.localScale = Vector3.one;

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;

        Image track = CreatePanel("Track", sliderRect, SliderTrackColor);
        Stretch(track.rectTransform);

        RectTransform fillArea = CreateRect("FillArea", sliderRect);
        SetRect(fillArea, new Vector2(0f, 0f), new Vector2(1f, 1f));
        fillArea.offsetMin = new Vector2(10f, 8f);
        fillArea.offsetMax = new Vector2(-10f, -8f);

        Image fill = CreatePanel("Fill", fillArea, SliderFillColor);
        Stretch(fill.rectTransform);
        slider.fillRect = fill.rectTransform;

        RectTransform handleArea = CreateRect("HandleArea", sliderRect);
        SetRect(handleArea, new Vector2(0f, 0f), new Vector2(1f, 1f));
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);

        Image handle = CreatePanel("Handle", handleArea, AmberColor);
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(16f, 28f);

        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        return slider;
    }

    void ApplyTab(SettingsTab tab)
    {
        _activeTab = tab;

        foreach (KeyValuePair<SettingsTab, RectTransform> pair in _tabViews)
            pair.Value.gameObject.SetActive(pair.Key == tab);

        foreach (KeyValuePair<SettingsTab, Button> pair in _tabButtons)
        {
            bool isActive = pair.Key == tab;
            Image image = pair.Value.GetComponent<Image>();
            if (image != null)
                image.color = isActive ? TabActiveColor : TabIdleColor;

            Text label = pair.Value.GetComponentInChildren<Text>(true);
            if (label != null)
                label.color = isActive ? AmberColor : TextColor;
        }
    }

    void RefreshAllRows()
    {
        SyncKeybindRows();
        SyncSliderRows();
        SyncToggleRows();
        SyncCycleRows();
    }

    void SyncKeybindRows()
    {
        bool isWaiting = _optionsManager != null && _optionsManager.IsWaitingForRebind;
        Button currentButton = _optionsManager != null ? _optionsManager.CurrentRebindButton : null;

        for (int i = 0; i < _keybindRows.Count; i++)
        {
            KeybindRow row = _keybindRows[i];
            if (row == null)
                continue;

            bool waitingRow = isWaiting && row.SourceButton == currentButton;
            row.ValueText.text = waitingRow ? "아무 키나 입력" : ReadBindingValue(row.SourceButton);
            row.ValueBoxImage.color = waitingRow ? ControlHighlightColor : ControlColor;
            row.ActionButtonImage.color = waitingRow ? AmberColor : ControlColor;
        }
    }

    void SyncSliderRows()
    {
        for (int i = 0; i < _sliderRows.Count; i++)
        {
            SliderRow row = _sliderRows[i];
            if (row == null || row.Source == null || row.Mirror == null)
                continue;

            if (Mathf.Abs(row.Source.value - row.Mirror.value) > SliderSyncThreshold)
                row.Mirror.SetValueWithoutNotify(row.Source.value);

            row.ValueText.text = FormatSliderValue(row.Source.value, row.DisplayMode);
        }
    }

    void SyncToggleRows()
    {
        for (int i = 0; i < _toggleRows.Count; i++)
            RefreshToggleRow(_toggleRows[i].Source);
    }

    void SyncCycleRows()
    {
        for (int i = 0; i < _cycleRows.Count; i++)
        {
            CycleRow row = _cycleRows[i];
            if (row == null || row.Source == null)
                continue;

            row.ValueText.text = ReadDropdownValue(row.Source);
        }

        for (int i = 0; i < _customCycleRows.Count; i++)
        {
            CustomCycleRow row = _customCycleRows[i];
            if (row == null || row.ValueText == null || row.GetValueText == null)
                continue;

            row.ValueText.text = row.GetValueText();
        }
    }

    void RefreshToggleRow(Toggle sourceToggle)
    {
        if (sourceToggle == null)
            return;

        for (int i = 0; i < _toggleRows.Count; i++)
        {
            ToggleRow row = _toggleRows[i];
            if (row == null || row.Source != sourceToggle)
                continue;

            bool isOn = sourceToggle.isOn;
            row.ValueText.text = isOn ? "켜짐" : "꺼짐";
            row.ValueText.color = isOn ? TextColor : MutedTextColor;
            row.ValueBoxImage.color = isOn ? ControlHighlightColor : ControlColor;
            return;
        }
    }

    void RefreshSliderRow(Slider sourceSlider, SliderDisplayMode displayMode)
    {
        if (sourceSlider == null)
            return;

        for (int i = 0; i < _sliderRows.Count; i++)
        {
            SliderRow row = _sliderRows[i];
            if (row == null || row.Source != sourceSlider)
                continue;

            row.Mirror.SetValueWithoutNotify(sourceSlider.value);
            row.ValueText.text = FormatSliderValue(sourceSlider.value, displayMode);
            return;
        }
    }

    void CycleDropdown(Dropdown sourceDropdown, int direction)
    {
        if (sourceDropdown == null || sourceDropdown.options == null || sourceDropdown.options.Count == 0)
            return;

        int nextValue = sourceDropdown.value + direction;
        if (nextValue < 0)
            nextValue = sourceDropdown.options.Count - 1;
        else if (nextValue >= sourceDropdown.options.Count)
            nextValue = 0;

        sourceDropdown.value = nextValue;
        sourceDropdown.RefreshShownValue();
        SyncCycleRows();
    }

    void CycleTargetFrameRate(int direction)
    {
        int currentIndex = GetTargetFrameRateIndex();
        int nextIndex = currentIndex + direction;
        if (nextIndex < 0)
            nextIndex = TargetFrameRateOptions.Length - 1;
        else if (nextIndex >= TargetFrameRateOptions.Length)
            nextIndex = 0;

        PlayerPrefs.SetInt(TargetFrameRateKey, TargetFrameRateOptions[nextIndex]);
        FramePacingRuntime.RefreshFromPrefs();
        SyncCycleRows();
    }

    int GetTargetFrameRateIndex()
    {
        int current = Mathf.Clamp(PlayerPrefs.GetInt(TargetFrameRateKey, DefaultTargetFrameRate), 30, 240);
        int bestIndex = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < TargetFrameRateOptions.Length; i++)
        {
            int distance = Mathf.Abs(TargetFrameRateOptions[i] - current);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    string GetTargetFrameRateLabel()
    {
        return TargetFrameRateOptions[GetTargetFrameRateIndex()] + " FPS";
    }

    void CancelPendingRebind()
    {
        if (_optionsManager != null)
            _optionsManager.CancelRebind();
    }

    void FadeTo(float targetAlpha, bool showing)
    {
        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);

        _fadeRoutine = StartCoroutine(CoFade(targetAlpha, showing));
    }

    IEnumerator CoFade(float targetAlpha, bool showing)
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.interactable = true;

        while (elapsed < FadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / FadeDuration));
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;
        _fadeRoutine = null;

        if (showing)
            yield break;

        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }

    Button FindKeybindButton(string buttonName)
    {
        if (_optionsManager == null || _optionsManager.keybindButtons == null)
            return null;

        for (int i = 0; i < _optionsManager.keybindButtons.Count; i++)
        {
            Button button = _optionsManager.keybindButtons[i];
            if (button != null && button.name == buttonName)
                return button;
        }

        return null;
    }

    static string ReadBindingValue(Button sourceButton)
    {
        if (sourceButton == null)
            return "---";

        Text label = sourceButton.GetComponentInChildren<Text>(true);
        if (label == null || string.IsNullOrWhiteSpace(label.text))
            return "---";

        return label.text.ToUpperInvariant();
    }

    static string ReadDropdownValue(Dropdown sourceDropdown)
    {
        if (sourceDropdown == null || sourceDropdown.options == null || sourceDropdown.options.Count == 0)
            return "-";

        int index = Mathf.Clamp(sourceDropdown.value, 0, sourceDropdown.options.Count - 1);
        return sourceDropdown.options[index].text;
    }

    static string FormatSliderValue(float value, SliderDisplayMode mode)
    {
        if (mode == SliderDisplayMode.Multiplier)
            return value.ToString("0.0x");

        if (mode == SliderDisplayMode.Degrees)
            return Mathf.RoundToInt(value) + "°";

        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }

    // ------------------------------------------------------------------ 리바인드 행 (반복 13)

    void CreateRebindStatusRow(RectTransform parent)
    {
        RectTransform row = CreateRect("Row_RebindStatus", parent);
        FitHeight(row, 40f);
        row.gameObject.AddComponent<Image>().color = SoftPanelColor;
        _rebindStatusText = CreateText("Status", row, string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleLeft, AmberColor, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
    }

    void CreateActionBindingRow(RectTransform parent, string label, ChuonAction action, int keyboardIndex, int rowIndex)
    {
        RectTransform holder = CreateRow(parent, label, rowIndex);
        var row = new ActionBindingRow
        {
            Action = action,
            KeyboardIndex = keyboardIndex,
            // 패드 칸: 이동은 L 스틱 고정, 락온 보조(마우스 휠클릭)는 키보드 전용
            PadEnabled = ChuonInputRebinding.CanRebind(action, true) && !(action == ChuonAction.LockOn && keyboardIndex > 0)
        };

        row.KeyboardButton = CreateButton("KeyboardBinding", holder, "-", 15);
        SetRect(row.KeyboardButton.GetComponent<RectTransform>(), new Vector2(0.00f, 0f), new Vector2(0.48f, 1f));
        row.KeyboardText = row.KeyboardButton.GetComponentInChildren<Text>(true);
        row.KeyboardImage = row.KeyboardButton.GetComponent<Image>();
        row.KeyboardButton.onClick.AddListener(() => BeginRowRebind(row, false));

        row.PadButton = CreateButton("GamepadBinding", holder, "-", 15);
        SetRect(row.PadButton.GetComponent<RectTransform>(), new Vector2(0.52f, 0f), new Vector2(1f, 1f));
        row.PadText = row.PadButton.GetComponentInChildren<Text>(true);
        row.PadImage = row.PadButton.GetComponent<Image>();
        row.PadButton.interactable = row.PadEnabled;
        if (row.PadEnabled)
            row.PadButton.onClick.AddListener(() => BeginRowRebind(row, true));

        _actionRows.Add(row);
        SyncActionRow(row);
    }

    void CreateResetBindingsRow(RectTransform parent, int rowIndex)
    {
        RectTransform holder = CreateRow(parent, "기본값 복원", rowIndex);
        Button reset = CreateButton("ResetBindings", holder, "모든 키 기본값으로", 15);
        SetRect(reset.GetComponent<RectTransform>(), new Vector2(0.00f, 0f), new Vector2(1f, 1f));
        reset.onClick.AddListener(() =>
        {
            if (ChuonInputRebinding.IsRebinding)
                return;
            ChuonInputRebinding.ResetAll();
            _rebindStatus = "모든 키를 기본값으로 되돌렸습니다.";
            SyncActionRows();
        });
    }

    void BeginRowRebind(ActionBindingRow row, bool gamepad)
    {
        if (row == null || ChuonInputRebinding.IsRebinding)
            return;

        Button source = gamepad ? row.PadButton : row.KeyboardButton;
        bool started = ChuonInputRebinding.Start(row.Action, gamepad, gamepad ? 0 : row.KeyboardIndex, result =>
        {
            if (!result.completed)
                _rebindStatus = "변경을 취소했습니다.";
            else if (!string.IsNullOrEmpty(result.swappedWith))
                _rebindStatus = "'" + result.swappedWith + "' 와(과) 겹쳐 두 키를 서로 교체했습니다.";
            else
                _rebindStatus = ChuonInput.DisplayName(result.action) + " → " + InputGlyphs.PathLabel(result.newPath, gamepad ? PadLabelScheme() : ChuonInputScheme.KeyboardMouse);
            SyncActionRows();
            if (source != null)
                ChuonInputRuntime.SelectForGamepad(source.gameObject);
        });

        if (started)
            _rebindStatus = gamepad
                ? "새 버튼을 누르세요  ·  START = 취소  (" + Mathf.RoundToInt(ChuonInputRebinding.TimeoutSeconds) + "초)"
                : "새 키 / 마우스 버튼을 누르세요  ·  ESC = 취소  (" + Mathf.RoundToInt(ChuonInputRebinding.TimeoutSeconds) + "초)";
        SyncActionRows();
    }

    static ChuonInputScheme PadLabelScheme()
    {
        return ChuonInput.Scheme == ChuonInputScheme.PlayStation ? ChuonInputScheme.PlayStation : ChuonInputScheme.Xbox;
    }

    void SyncActionRows()
    {
        for (int i = 0; i < _actionRows.Count; i++)
            SyncActionRow(_actionRows[i]);
        if (_rebindStatusText != null && _rebindStatusText.text != _rebindStatus)
            _rebindStatusText.text = _rebindStatus;
    }

    void SyncActionRow(ActionBindingRow row)
    {
        if (row == null)
            return;
        bool rebinding = ChuonInputRebinding.IsRebinding && ChuonInputRebinding.CurrentAction == row.Action && ChuonInputRebinding.CurrentBindingIndex >= 0;
        bool waitingKb = rebinding && !ChuonInputRebinding.CurrentIsGamepad && ChuonInputRebinding.CurrentBindingIndex == row.KeyboardIndex;
        bool waitingPad = rebinding && ChuonInputRebinding.CurrentIsGamepad && row.PadEnabled;

        string kb = waitingKb ? "입력 대기..." : InputGlyphs.BindingLabel(ChuonInput.KeyboardAction(row.Action), row.KeyboardIndex, ChuonInputScheme.KeyboardMouse);
        string pad;
        if (waitingPad)
            pad = "입력 대기...";
        else if (row.Action == ChuonAction.Move)
            pad = "L 스틱";
        else if (!row.PadEnabled)
            pad = "-";
        else
            pad = InputGlyphs.BindingLabel(ChuonInput.GamepadAction(row.Action), 0, PadLabelScheme());

        if (row.KeyboardText != null && row.KeyboardText.text != kb)
            row.KeyboardText.text = kb;
        if (row.PadText != null && row.PadText.text != pad)
            row.PadText.text = pad;
        if (row.KeyboardImage != null)
            row.KeyboardImage.color = waitingKb ? ControlHighlightColor : ControlColor;
        if (row.PadImage != null)
            row.PadImage.color = waitingPad ? ControlHighlightColor : ControlColor;
        if (row.KeyboardText != null)
            row.KeyboardText.color = waitingKb ? AmberColor : TextColor;
        if (row.PadText != null)
            row.PadText.color = waitingPad ? AmberColor : (row.PadEnabled ? TextColor : MutedTextColor);
    }

    void FocusActiveTabButton()
    {
        if (_tabButtons.TryGetValue(_activeTab, out Button tabButton) && tabButton != null)
            ChuonInputRuntime.SelectForGamepad(tabButton.gameObject);
    }

    // 패드로 선택이 스크롤 영역 밖으로 나가면 보이도록 스크롤한다 (마우스 휠 스크롤은 그대로).
    void KeepSelectionVisible()
    {
        if (!ChuonInput.IsGamepadScheme || UnityEngine.EventSystems.EventSystem.current == null)
            return;
        GameObject selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
        if (selected == null || !_tabViews.TryGetValue(_activeTab, out RectTransform view) || view == null)
            return;
        if (!selected.transform.IsChildOf(view))
            return;
        ScrollRect scroll = view.GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null)
            return;

        Transform rowT = selected.transform;
        while (rowT != null && rowT.parent != scroll.content)
            rowT = rowT.parent;
        RectTransform row = rowT as RectTransform;
        if (row == null)
            return;

        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, row);
        Rect vp = scroll.viewport.rect;
        float shift = 0f;
        if (b.max.y > vp.yMax)
            shift = b.max.y - vp.yMax + 12f;
        else if (b.min.y < vp.yMin)
            shift = b.min.y - vp.yMin - 12f;
        if (Mathf.Abs(shift) < 0.5f)
            return;
        Vector2 pos = scroll.content.anchoredPosition;
        float maxY = Mathf.Max(0f, scroll.content.rect.height - vp.height);
        pos.y = Mathf.Clamp(pos.y - shift, 0f, maxY);
        scroll.content.anchoredPosition = pos;
        scroll.velocity = Vector2.zero;
    }

    static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    static Image CreatePanel(string objectName, Transform parent, Color color)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    static Text CreateText(string objectName, Transform parent, string content, int fontSize, FontStyle fontStyle, TextAnchor anchor, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        SetRect(rect, anchorMin, anchorMax);

        Text text = go.GetComponent<Text>();
        text.font = RuntimeBuiltInFontUtility.GetDefaultFont();
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = anchor;
        text.color = color;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static Button CreateButton(string objectName, Transform parent, string label, int fontSize)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;

        Image image = go.GetComponent<Image>();
        image.color = ControlColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;

        AddLine("ButtonAccentTop", rect, new Vector2(0.06f, 0.94f), new Vector2(0.94f, 0.98f), AmberColor);
        AddLine("ButtonAccentBottom", rect, new Vector2(0.06f, 0.02f), new Vector2(0.94f, 0.06f), new Color(CyanLineColor.r, CyanLineColor.g, CyanLineColor.b, 0.45f));
        CreateText("Label", rect, label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, 0f), new Vector2(1f, 1f));
        return button;
    }

    static void AddLine(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        Image line = CreatePanel(objectName, parent, color);
        line.raycastTarget = false;
        SetRect(line.rectTransform, anchorMin, anchorMax);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static void FitHeight(RectTransform rect, float height)
    {
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    static Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform nested = FindChildRecursive(child, targetName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    static void HideBackendVisuals(GameObject root)
    {
        if (root == null)
            return;

        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null)
                continue;
            graphics[i].enabled = false;
            graphics[i].raycastTarget = false;
        }

        Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null)
                continue;
            canvases[i].enabled = false;
        }

        GraphicRaycaster[] raycasters = root.GetComponentsInChildren<GraphicRaycaster>(true);
        for (int i = 0; i < raycasters.Length; i++)
        {
            if (raycasters[i] == null)
                continue;
            raycasters[i].enabled = false;
        }
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }
}
