// 파일명: LoadingTipOverlay.cs
// 씬 전환(검은 화면) 동안 우하단에 짧은 팁 카드를 띄운다. 츠카사 HUD 톤(오프화이트 라인 + 레드 액센트).
// - SceneFader.FadeOutAndLoadScene: 페이더 알파를 따라 함께 나타나고, 다음 씬 FadeIn 에서 함께 사라진다.
// - SystemBootLoader: 부팅 로딩 화면 동안 표시.
// 입력을 막지 않는다(GraphicRaycaster 없음, blocksRaycasts=false). 실패해도 예외를 던지지 않는다.
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LoadingTipOverlay : MonoBehaviour
{
    public const string RootObjectName = "LoadingTipOverlay";
    const int BaseSortingOrder = 520; // RuntimeSceneFader(500) 위
    const float DirectFadeInDuration = 0.35f;
    const float DirectFadeOutDuration = 0.25f;
    // 페이더가 밝아질 때 팁이 먼저 사라지도록 (페이더 알파가 이 값 아래로 내려가면 팁 0)
    const float FollowHideThreshold = 0.45f;

    struct Tip
    {
        public string Tag;
        public string Body;

        public Tip(string tag, string body)
        {
            Tag = tag;
            Body = body;
        }
    }

    // 조작 + 전투 규칙 + 설정(Docs/Story_Reference.md) 혼합
    static readonly Tip[] Tips =
    {
        new Tip("CONTROL", "약공격은 좌클릭, 강공격은 우클릭. 콤보 루트 5종(A–E)은 일시정지 > 콤보(C 키)에서 확인할 수 있다."),
        new Tip("CONTROL", "L-SHIFT 로 회피. 연속 회피는 3번째부터 무적 시간이 짧아진다. 리듬을 끊어라."),
        new Tip("GUARD", "E 를 누르고 있으면 가드, 공격이 닿는 순간에 맞춰 누르면 패링. 패링은 반격의 시작이다."),
        new Tip("SIGNAL", "적의 발광 색을 읽어라. 금색 = 패링 가능, 빨강 = 회피, 자홍 = 가드·패링 모두 불가."),
        new Tip("CONTROL", "TAB 또는 휠 클릭으로 락온. 대상이 없을 때 TAB 은 카메라를 등 뒤로 되돌린다."),
        new Tip("AMPOULE", "Q 로 회복 앰플 사용, HP 35% 회복. 사용 동작 중 피격되면 회복은 취소되고 앰플은 소모된다."),
        new Tip("PHASE CORE", "R 로 궁극기. 페이즈 코어 5단이 모두 차야 발동하며, 보스전마다 한 번뿐이다."),
        new Tip("BREAK", "브레이크 게이지가 가득 차면 적은 5초간 그로기에 빠진다. 그 틈에 피니셔를 넣어라."),
        new Tip("FACILITY", "격납고행 엘리베이터가 내려가는 동안 앰플 4개가 충전되고 페이즈 코어가 30% 차오른다."),
        new Tip("ARCHIVE", "츠카사 코퍼레이션. 무기와 기체, 그리고 국가까지. 이 나라의 모든 것은 츠카사의 설계도 위에 있다."),
        new Tip("ARCHIVE", "SUBJECT 07 / 상태: DISPOSED. 불안정성 판정으로 폐기된 프로젝트 시리즈의 실패작."),
        new Tip("E.G.O", "E.G.O 는 츠카사가 폐기한 지능형 카타나. 07호가 아니라 '추온'이라는 이름으로 부르는 유일한 존재다."),
        new Tip("ARCHIVE", "Type-D. 같은 프로젝트 시리즈의 또 다른 산물. 본사 격납고에서 그가 길을 막고 있다."),
    };

    // 팁을 띄우지 않는 목적지 씬 (자체 연출/로딩 화면이 있음)
    static readonly string[] SkippedScenes = { "TeamLogoScene", "TitleScene", "BootScene", "CreditsScene" };

    enum Mode
    {
        Hidden,
        Showing,
        Hiding
    }

    static LoadingTipOverlay s_instance;
    static bool s_failed;
    static int s_lastTipIndex = -1;
    static string[] s_counterLabels;

    Canvas _canvas;
    CanvasGroup _group;
    Text _tagText;
    Text _bodyText;
    Text _counterText;
    Mode _mode = Mode.Hidden;
    CanvasGroup _follow;
    bool _followReachedBlack;
    float _alpha;

    public static bool IsShowing => s_instance != null && s_instance._mode == Mode.Showing;
    public static bool IsVisible => s_instance != null && s_instance._mode != Mode.Hidden;
    public static string CurrentTipText => s_instance != null && s_instance._bodyText != null ? s_instance._bodyText.text : string.Empty;

    // ------------------------------------------------------------------
    // 공개 API
    // ------------------------------------------------------------------

    /// <summary>목적지 씬에 팁을 띄워도 되는지 (로고/타이틀/부트/크레딧 제외).</summary>
    public static bool ShouldShowForScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return false;

        for (int i = 0; i < SkippedScenes.Length; i++)
        {
            if (string.Equals(SkippedScenes[i], sceneName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 무작위 팁 표시. followFader 를 주면 그 CanvasGroup(검은 페이더) 알파를 따라 나타난다.
    /// </summary>
    public static void ShowRandom(CanvasGroup followFader = null)
    {
        if (!Application.isPlaying || s_failed)
            return;

        try
        {
            LoadingTipOverlay overlay = EnsureInstance();
            if (overlay != null)
                overlay.BeginShow(followFader);
        }
        catch (Exception exception)
        {
            s_failed = true;
            Debug.LogWarning("[LoadingTipOverlay] Disabled after error: " + exception.Message);
        }
    }

    /// <summary>팁 숨김. followFader 를 주면 페이더가 밝아지는 속도에 맞춰(조금 먼저) 사라진다.</summary>
    public static void Hide(CanvasGroup followFader = null)
    {
        if (s_instance == null)
            return;

        try
        {
            s_instance.BeginHide(followFader);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[LoadingTipOverlay] Hide failed: " + exception.Message);
        }
    }

    /// <summary>즉시 숨김 (애니메이션 없음).</summary>
    public static void HideImmediate()
    {
        if (s_instance != null)
            s_instance.SetHidden();
    }

    // ------------------------------------------------------------------
    // 내부
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        s_failed = false;
        s_lastTipIndex = -1;
    }

    static LoadingTipOverlay EnsureInstance()
    {
        if (s_instance != null)
            return s_instance;

        GameObject root = new GameObject(RootObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        DontDestroyOnLoad(root);
        s_instance = root.AddComponent<LoadingTipOverlay>();
        s_instance.Build();
        return s_instance;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    // 새 씬이 올라오면 전환은 끝난 것 → 게임플레이 위에 남지 않도록 항상 숨김 시작
    void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (loadMode != LoadSceneMode.Single || _mode != Mode.Showing)
            return;

        SceneFader fader = SceneFader.Instance;
        BeginHide(fader != null ? fader.fadeCanvasGroup : null);
    }

    void BeginShow(CanvasGroup followFader)
    {
        PickTip();
        _follow = followFader;
        _followReachedBlack = false;

        int order = BaseSortingOrder;
        Canvas faderCanvas = followFader != null ? followFader.GetComponentInParent<Canvas>() : null;
        if (faderCanvas != null)
            order = Mathf.Max(order, faderCanvas.rootCanvas.sortingOrder + 1);
        _canvas.sortingOrder = order;

        if (_mode == Mode.Hidden)
            _alpha = 0f;

        _mode = Mode.Showing;
        _canvas.enabled = true;
        enabled = true;
        ApplyAlpha();
    }

    void BeginHide(CanvasGroup followFader)
    {
        if (_mode == Mode.Hidden)
            return;

        _follow = followFader;
        _mode = Mode.Hiding;
        enabled = true;
    }

    void SetHidden()
    {
        _mode = Mode.Hidden;
        _alpha = 0f;
        _follow = null;
        if (_group != null)
            _group.alpha = 0f;
        if (_canvas != null)
            _canvas.enabled = false;
        enabled = false;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool hasFollow = _follow != null && _follow.gameObject.activeInHierarchy;

        if (_mode == Mode.Showing)
        {
            if (hasFollow)
            {
                float faderAlpha = _follow.alpha;
                _alpha = Mathf.Max(_alpha, Mathf.SmoothStep(0f, 1f, faderAlpha));
                if (faderAlpha >= 0.95f)
                    _followReachedBlack = true;
                // 안전장치: 페이더가 다시 밝아졌는데 숨김 호출이 없으면 스스로 숨김
                else if (_followReachedBlack && faderAlpha <= 0.5f)
                    _mode = Mode.Hiding;
            }
            else
            {
                _alpha = Mathf.MoveTowards(_alpha, 1f, dt / DirectFadeInDuration);
            }
        }
        else if (_mode == Mode.Hiding)
        {
            float target = 0f;
            if (hasFollow)
                target = Mathf.Clamp01((_follow.alpha - FollowHideThreshold) / (1f - FollowHideThreshold));

            // 직접 페이드는 항상 진행, 페이더 추종은 그보다 늦지 않게
            _alpha = Mathf.Min(hasFollow ? target : 1f, Mathf.MoveTowards(_alpha, 0f, dt / (hasFollow ? 0.6f : DirectFadeOutDuration)));
            if (_alpha <= 0.001f)
            {
                SetHidden();
                return;
            }
        }
        else
        {
            enabled = false;
            return;
        }

        ApplyAlpha();
    }

    void ApplyAlpha()
    {
        if (_group != null && !Mathf.Approximately(_group.alpha, _alpha))
            _group.alpha = _alpha;
    }

    void PickTip()
    {
        int count = Tips.Length;
        if (count == 0)
            return;

        int index = UnityEngine.Random.Range(0, count);
        if (count > 1 && index == s_lastTipIndex)
            index = (index + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
        s_lastTipIndex = index;

        Tip tip = Tips[index];
        _tagText.text = "// " + tip.Tag;
        _bodyText.text = GlyphBody(tip.Body);
        _counterText.text = GetCounterLabel(index);
    }

    // 팁 문구의 키 표기를 현재 장치/리바인드 표기로 (키보드 기본 배치면 원문 그대로)
    static string GlyphBody(string body)
    {
        body = InputGlyphs.ReplaceKeyToken(body, ChuonAction.Dodge, "L-SHIFT");
        body = InputGlyphs.ReplaceKeyToken(body, ChuonAction.Guard, "E");
        body = InputGlyphs.ReplaceKeyToken(body, ChuonAction.LockOn, "TAB");
        body = InputGlyphs.ReplaceKeyToken(body, ChuonAction.Heal, "Q");
        body = InputGlyphs.ReplaceKeyToken(body, ChuonAction.Ultimate, "R");
        if (ChuonInput.IsGamepadScheme || ChuonInput.KeyboardOverridden(ChuonAction.AttackLight))
            body = body.Replace("좌클릭", InputGlyphs.Get(ChuonAction.AttackLight));
        if (ChuonInput.IsGamepadScheme || ChuonInput.KeyboardOverridden(ChuonAction.AttackHeavy))
            body = body.Replace("우클릭", InputGlyphs.Get(ChuonAction.AttackHeavy));
        return body;
    }

    static string GetCounterLabel(int index)
    {
        if (s_counterLabels == null)
        {
            s_counterLabels = new string[Tips.Length];
            for (int i = 0; i < Tips.Length; i++)
                s_counterLabels[i] = "NOTE " + (i + 1).ToString("00") + " / " + Tips.Length.ToString("00");
        }

        return s_counterLabels[Mathf.Clamp(index, 0, s_counterLabels.Length - 1)];
    }

    // ------------------------------------------------------------------
    // 구성 (1920x1080 기준, 우하단 카드)
    // ------------------------------------------------------------------

    void Build()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = BaseSortingOrder;
        _canvas.enabled = false;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        GameplayUiTheme theme = GameplayHudStyler.Theme;
        Color red = new Color(0.878f, 0.188f, 0.227f, 1f);
        Color white = theme.hudTextColor;
        Color line = new Color(0.93f, 0.94f, 0.95f, 0.55f);
        Color muted = new Color(0.80f, 0.82f, 0.85f, 0.72f);
        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();

        RectTransform card = CreateRect("TipCard", transform);
        card.anchorMin = new Vector2(1f, 0f);
        card.anchorMax = new Vector2(1f, 0f);
        card.pivot = new Vector2(1f, 0f);
        card.anchoredPosition = new Vector2(-72f, 48f);
        card.sizeDelta = new Vector2(700f, 148f);

        CreateImage("Panel", card, new Color(0.035f, 0.04f, 0.045f, 0.86f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        CreateImage("LineTop", card, line, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), Vector2.zero);
        CreateImage("LineBottom", card, new Color(line.r, line.g, line.b, 0.30f), new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));
        CreateImage("AccentBar", card, red, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f));
        CreateImage("AccentTick", card, red, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -3f), new Vector2(0f, 0f));
        CreateImage("HeaderRule", card, new Color(line.r, line.g, line.b, 0.25f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -42f), new Vector2(-20f, -41f));

        CreateText("Header", card, font, "TSUKASA CORP.  //  FIELD NOTE", 15, FontStyle.Bold, TextAnchor.MiddleLeft, muted,
            new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(24f, -40f), new Vector2(0f, -8f));
        _counterText = CreateText("Counter", card, font, string.Empty, 14, FontStyle.Bold, TextAnchor.MiddleRight, muted,
            new Vector2(0.62f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), new Vector2(-20f, -8f));
        _tagText = CreateText("Tag", card, font, string.Empty, 16, FontStyle.Bold, TextAnchor.UpperLeft, red,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -74f), new Vector2(-20f, -50f));
        _bodyText = CreateText("Body", card, font, string.Empty, 19, FontStyle.Normal, TextAnchor.UpperLeft, white,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 12f), new Vector2(-24f, -78f));
        _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _bodyText.lineSpacing = 1.1f;

        enabled = false;
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static Image CreateImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(string name, Transform parent, Font font, string content, int size, FontStyle style, TextAnchor anchor, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
