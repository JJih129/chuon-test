// 반복 16: 보스 HUD 보강 (TSUKASA HUD 톤: 그래파이트·오프화이트·레드).
//  - HP 바 페이즈 눈금 5개 (70·66·40·30·15 %), 지나간 눈금은 레드
//  - HP 바 아래 균형 게이지에 「균형」 라벨, 과열(×2) 중 레드 테두리 라벨 「균형 ×2」
//  - 페이즈 배너(0.9–1.6 s), 기믹 목표 줄(「전원 노드 파괴 1/3」)
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BossOverhaulHUD : MonoBehaviour
{
    static readonly Color TickIdle = new Color(0.96f, 0.95f, 0.93f, 0.85f);
    static readonly Color TickPassed = new Color(0.9f, 0.12f, 0.1f, 0.95f);
    static readonly Color Ink = new Color(0.96f, 0.95f, 0.93f, 1f);
    static readonly Color Red = new Color(0.92f, 0.13f, 0.1f, 1f);

    TypeDOverhaul _overhaul;
    BossUIController _ui;
    Image _hpFill;
    Image _breakFill;
    readonly Image[] _ticks = new Image[5];
    TextMeshProUGUI _balanceLabel;
    Canvas _canvas;
    TextMeshProUGUI _banner;
    TextMeshProUGUI _objective;
    float _bannerUntil;
    float _bannerStart;
    float _objectivePulseAt = -10f;
    float _nextResolveAt;

    public string LastBanner { get; private set; } = "";
    public string Objective => _objective != null ? _objective.text : "";

    public void Bind(TypeDOverhaul overhaul) => _overhaul = overhaul;

    void Start()
    {
        var go = new GameObject("OH_BossBannerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 65;
        CanvasScaler sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        _banner = MakeText(go.transform, "Banner", 44f, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(900f, 70f));
        _banner.characterSpacing = 18f;
        _objective = MakeText(go.transform, "Objective", 24f, new Vector2(0.5f, 1f), new Vector2(0f, -262f), new Vector2(700f, 34f));
        _objective.text = "";
        _banner.text = "";
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, float size, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = ResolveFont();
        if (font != null)
            t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.color = Ink;
        t.raycastTarget = false;
        RectTransform r = t.rectTransform;
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = sizeDelta;
        return t;
    }

    static TMP_FontAsset s_font;

    static TMP_FontAsset ResolveFont()
    {
        if (s_font != null)
            return s_font;
        if (ChuonUiFonts.Tmp != null) // 반복 16 (E8): Noto Sans KR Medium (씬 Noto SDF 는 가변 글꼴 Thin)
            return s_font = ChuonUiFonts.Tmp;
        foreach (TMP_Text t in FindObjectsOfType<TMP_Text>(true))
            if (t != null && t.font != null && t.font.name.Contains("NotoSansKR"))
                return s_font = t.font;
        return s_font = TMP_Settings.defaultFontAsset;
    }

    public void ShowBanner(string text, float seconds)
    {
        if (_banner == null)
            return;
        LastBanner = text;
        _banner.text = text;
        _bannerStart = Time.unscaledTime;
        _bannerUntil = Time.unscaledTime + Mathf.Max(0.4f, seconds);
    }

    public void SetObjective(string text)
    {
        if (_objective != null)
            _objective.text = text ?? "";
    }

    public void PulseObjective() => _objectivePulseAt = Time.unscaledTime;

    public void RefreshTicks() => _ticksDirty = true;

    bool _ticksDirty = true;

    void LateUpdate()
    {
        if (_overhaul == null)
            return;
        if ((_hpFill == null || _breakFill == null) && Time.unscaledTime >= _nextResolveAt)
        {
            _nextResolveAt = Time.unscaledTime + 1f;
            ResolveBars();
        }
        if (_ticksDirty)
            UpdateTicks();

        // 배너: 0.15 s 페이드 인, 끝 0.3 s 페이드 아웃, 레드 → 흰 글자
        if (_banner != null)
        {
            float now = Time.unscaledTime;
            float a = 0f;
            if (now < _bannerUntil)
                a = Mathf.Min(Mathf.Clamp01((now - _bannerStart) / 0.15f), Mathf.Clamp01((_bannerUntil - now) / 0.3f));
            Color c = Color.Lerp(Red, Ink, Mathf.Clamp01((now - _bannerStart) / 0.6f));
            c.a = a;
            _banner.color = c;
        }
        if (_objective != null)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - _objectivePulseAt) / 0.35f);
            _objective.color = Color.Lerp(Red, Ink, k);
            _objective.rectTransform.localScale = Vector3.one * (1f + 0.08f * (1f - k));
        }

        bool hide = CinematicDirector.IsPlaying || ExecutionDirector.IsExecuting;
        if (_canvas != null && _canvas.enabled == hide)
            _canvas.enabled = !hide;

        if (_balanceLabel != null)
        {
            bool doubled = _overhaul.BalanceMultiplier > 1.01f;
            string want = doubled ? "균형 x" +_overhaul.BalanceMultiplier.ToString("0.#") : "균형";
            if (_balanceLabel.text != want)
                _balanceLabel.text = want;
            _balanceLabel.color = doubled || (_overhaul.Break != null && _overhaul.Break.IsInBreak) ? ChuonUiPalette.TsukasaRedText : new Color(0.96f, 0.95f, 0.93f, 0.92f); // 반복 16: 텍스트용 레드 (대비 ≥ 4.5:1)
        }

        if ((_frame++ & 15) == 0)
            _ticksDirty = true;
    }

    int _frame;

    void ResolveBars()
    {
        _ui = GameplaySceneCache.ResolveBossUIController();
        if (_ui == null)
            return;
        if (_ui.hpHud != null)
            _hpFill = _ui.hpHud.hpFillImage != null ? _ui.hpHud.hpFillImage : _ui.hpHud.GetComponent<Image>();
        if (_ui.breakHud != null)
        {
            _breakFill = _ui.breakHud.breakFillImage;
            _ui.breakHud.hideOnBreakExit = false;
        }
        if (_hpFill != null && _ticks[0] == null)
            BuildTicks();
        if (_breakFill != null && _balanceLabel == null)
            BuildBalanceLabel();
    }

    void BuildTicks()
    {
        RectTransform src = _hpFill.rectTransform;
        for (int i = 0; i < _ticks.Length; i++)
        {
            var go = new GameObject("OH_PhaseTick" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = src.gameObject.layer;
            go.transform.SetParent(src, false);
            Image img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = TickIdle;
            RectTransform r = img.rectTransform;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(3f, 0f);
            _ticks[i] = img;
        }
        _ticksDirty = true;
    }

    void UpdateTicks()
    {
        _ticksDirty = false;
        if (_hpFill == null || _ticks[0] == null)
            return;
        float[] at = _overhaul.HudTicks;
        float hp = _overhaul.Boss != null ? _overhaul.Boss.OverhaulHpNormalized : 1f;
        bool reverse = _hpFill.type == Image.Type.Filled && _hpFill.fillOrigin == 1 && _hpFill.fillMethod == Image.FillMethod.Horizontal;
        for (int i = 0; i < _ticks.Length && i < at.Length; i++)
        {
            Image img = _ticks[i];
            if (img == null)
                continue;
            float x = reverse ? 1f - at[i] : at[i];
            RectTransform r = img.rectTransform;
            r.anchorMin = new Vector2(x, -0.15f);
            r.anchorMax = new Vector2(x, 1.15f);
            r.anchoredPosition = Vector2.zero;
            img.color = hp <= at[i] + 0.0005f ? TickPassed : TickIdle;
        }
    }

    void BuildBalanceLabel()
    {
        RectTransform src = _breakFill.rectTransform;
        var go = new GameObject("OH_BalanceLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = src.gameObject.layer;
        go.transform.SetParent(src.parent, false);
        _balanceLabel = go.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = ResolveFont();
        if (font != null)
            _balanceLabel.font = font;
        _balanceLabel.fontSize = 20f; // 반복 16 (E9): 16 → 20 (1080p 기준)
        _balanceLabel.fontStyle = FontStyles.Bold;
        _balanceLabel.characterSpacing = 3f;
        _balanceLabel.alignment = TextAlignmentOptions.MidlineRight;
        _balanceLabel.enableWordWrapping = false;
        _balanceLabel.raycastTarget = false;
        _balanceLabel.text = "균형";
        RectTransform r = _balanceLabel.rectTransform;
        r.anchorMin = src.anchorMin;
        r.anchorMax = src.anchorMin;
        r.pivot = new Vector2(1f, 0.5f);
        r.sizeDelta = new Vector2(150f, 28f);
        Vector2 left = src.anchoredPosition - new Vector2(src.rect.width * src.pivot.x, 0f);
        r.anchoredPosition = left + new Vector2(-10f, 0f);
    }

    void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        for (int i = 0; i < _ticks.Length; i++)
            if (_ticks[i] != null)
                Destroy(_ticks[i].gameObject);
        if (_balanceLabel != null)
            Destroy(_balanceLabel.gameObject);
    }
}
