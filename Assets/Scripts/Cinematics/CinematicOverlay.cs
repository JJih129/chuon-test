using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시네마틱 화면 오버레이: 레터박스 바, 페이드, 타이틀 카드, 길게 눌러 건너뛰기 프롬프트, 자막 호스트.
/// 자막은 기존 IntroDialoguePresenter 를 이 캔버스 안에 하나 두고 쓴다 (자막 크기·배경 옵션, 화자 색, 음악 더킹이 그대로 적용).
/// 정렬 순서 870: HUD(기본) 위, 츠카사 엔딩 패널(860) 위, VR 전환(900) 아래.
/// 모든 갱신은 값이 바뀔 때만 하고, 문자열은 표시 시점에만 만든다 (프레임당 할당 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class CinematicOverlay : MonoBehaviour
{
    public const int SortingOrder = 870;
    const float BarHeightRatio = 0.11f; // 16:9 → 약 2.35:1

    static CinematicOverlay s_instance;

    Canvas _canvas;
    RectTransform _top;
    RectTransform _bottom;
    Image _fade;
    CanvasGroup _skipGroup;
    RectTransform _skipFill;
    Text _skipText;
    CanvasGroup _cardGroup;
    Text _cardTitle;
    Text _cardSub;
    Image _cardLine;
    IntroDialoguePresenter _subtitles;

    float _letterbox = -1f;
    float _fadeAlpha = -1f;
    Color _fadeColor = Color.black;
    float _skipAlpha = -1f;
    float _skipProgress = -1f;
    float _cardAlpha = -1f;
    bool _skipTextDirty = true;
    bool _glyphHooked;

    public static CinematicOverlay Active => s_instance;
    public float Letterbox => Mathf.Max(0f, _letterbox);
    public float FadeAlpha => Mathf.Max(0f, _fadeAlpha);
    public Color FadeColor => _fadeColor;

    public static CinematicOverlay Ensure()
    {
        if (s_instance != null)
            return s_instance;

        var go = new GameObject("CinematicOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        s_instance = go.AddComponent<CinematicOverlay>();
        s_instance.Build();
        return s_instance;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        if (_glyphHooked)
            InputGlyphs.Changed -= HandleGlyphsChanged;
    }

    void Build()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Color bar = new Color(0.008f, 0.008f, 0.012f, 1f);
        _top = NewImage("LetterboxTop", transform, bar).rectTransform;
        _top.anchorMin = new Vector2(0f, 1f); _top.anchorMax = new Vector2(1f, 1f); _top.pivot = new Vector2(0.5f, 1f);
        _bottom = NewImage("LetterboxBottom", transform, bar).rectTransform;
        _bottom.anchorMin = new Vector2(0f, 0f); _bottom.anchorMax = new Vector2(1f, 0f); _bottom.pivot = new Vector2(0.5f, 0f);

        _fade = NewImage("Fade", transform, Color.black);
        Stretch(_fade.rectTransform);

        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();

        // 타이틀 카드 (장소·시간 자막, "SIMULATION TERMINATED" 등)
        var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup));
        card.transform.SetParent(transform, false);
        _cardGroup = card.GetComponent<CanvasGroup>();
        _cardGroup.blocksRaycasts = false;
        _cardGroup.interactable = false;
        RectTransform cardRect = (RectTransform)card.transform;
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(1400f, 200f);
        _cardTitle = NewText("Title", cardRect, font, 44, new Color(0.94f, 0.95f, 0.97f, 1f), new Vector2(0f, 22f), new Vector2(1400f, 64f));
        _cardTitle.fontStyle = FontStyle.Bold;
        _cardLine = NewImage("Rule", cardRect, new Color(1f, 0.12f, 0.1f, 0.95f));
        RectTransform rule = _cardLine.rectTransform;
        rule.anchorMin = rule.anchorMax = new Vector2(0.5f, 0.5f);
        rule.sizeDelta = new Vector2(220f, 2f);
        rule.anchoredPosition = new Vector2(0f, -16f);
        _cardSub = NewText("Sub", cardRect, font, 20, new Color(1f, 1f, 1f, 0.62f), new Vector2(0f, -44f), new Vector2(1400f, 32f));

        // 길게 눌러 건너뛰기 (오른쪽 아래, 레터박스 바 위)
        var skip = new GameObject("SkipPrompt", typeof(RectTransform), typeof(CanvasGroup));
        skip.transform.SetParent(transform, false);
        _skipGroup = skip.GetComponent<CanvasGroup>();
        _skipGroup.blocksRaycasts = false;
        _skipGroup.interactable = false;
        RectTransform skipRect = (RectTransform)skip.transform;
        skipRect.anchorMin = skipRect.anchorMax = new Vector2(1f, 0f);
        skipRect.pivot = new Vector2(1f, 0f);
        skipRect.sizeDelta = new Vector2(380f, 40f);
        skipRect.anchoredPosition = new Vector2(-40f, 34f);
        _skipText = NewText("Label", skipRect, font, 18, new Color(1f, 1f, 1f, 0.82f), new Vector2(0f, 8f), new Vector2(380f, 26f));
        _skipText.alignment = TextAnchor.MiddleRight;
        Image track = NewImage("Track", skipRect, new Color(1f, 1f, 1f, 0.18f));
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f); trackRect.anchorMax = new Vector2(1f, 0f); trackRect.pivot = new Vector2(1f, 0f);
        trackRect.sizeDelta = new Vector2(-150f, 2f);
        trackRect.anchoredPosition = Vector2.zero;
        Image fill = NewImage("Fill", trackRect, new Color(1f, 0.16f, 0.12f, 1f));
        _skipFill = fill.rectTransform;
        _skipFill.anchorMin = Vector2.zero; _skipFill.anchorMax = Vector2.one; _skipFill.pivot = new Vector2(1f, 0.5f);
        _skipFill.offsetMin = _skipFill.offsetMax = Vector2.zero;

        // 자막: 기존 대사 표시기 (옵션 크기/배경, 화자 색). 레터박스 바 위쪽에 오도록 위치는 presenter 기본값(하단 18%).
        var subtitleHost = new GameObject("Subtitles", typeof(RectTransform));
        subtitleHost.transform.SetParent(transform, false);
        Stretch((RectTransform)subtitleHost.transform);
        _subtitles = subtitleHost.AddComponent<IntroDialoguePresenter>();

        SetLetterbox(0f);
        SetFade(Color.black, 0f);
        SetSkipPrompt(0f, 0f);
        SetCard(0f);
    }

    // ------------------------------------------------------------------ API

    public void SetLetterbox(float t)
    {
        t = Mathf.Clamp(t, 0f, 4.6f); // 1 = 기본 2.35:1, 4.55 ≈ 화면 전체
        if (Mathf.Abs(t - _letterbox) < 0.0005f)
            return;
        _letterbox = t;
        float h = 1080f * BarHeightRatio * t;
        _top.sizeDelta = new Vector2(0f, h);
        _bottom.sizeDelta = new Vector2(0f, h);
        bool on = t > 0.0005f;
        if (_top.gameObject.activeSelf != on)
        {
            _top.gameObject.SetActive(on);
            _bottom.gameObject.SetActive(on);
        }
    }

    public void SetFade(Color color, float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (Mathf.Abs(alpha - _fadeAlpha) < 0.0005f && color == _fadeColor)
            return;
        _fadeAlpha = alpha;
        _fadeColor = color;
        color.a = alpha;
        _fade.color = color;
        bool on = alpha > 0.0005f;
        if (_fade.enabled != on)
            _fade.enabled = on;
    }

    /// <summary>alpha 0 이면 숨김. progress 0~1 = 길게 누르기 진행.</summary>
    public void SetSkipPrompt(float alpha, float progress)
    {
        alpha = Mathf.Clamp01(alpha);
        progress = Mathf.Clamp01(progress);
        if (alpha > 0f && _skipTextDirty)
            RefreshSkipText();
        if (Mathf.Abs(alpha - _skipAlpha) >= 0.002f)
        {
            _skipAlpha = alpha;
            _skipGroup.alpha = alpha;
        }
        if (Mathf.Abs(progress - _skipProgress) >= 0.002f)
        {
            _skipProgress = progress;
            _skipFill.localScale = new Vector3(progress, 1f, 1f);
        }
    }

    public void ShowCard(string title, string sub)
    {
        _cardTitle.text = title ?? string.Empty;
        _cardSub.text = sub ?? string.Empty;
        _cardLine.enabled = !string.IsNullOrEmpty(title);
    }

    public void SetCard(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (Mathf.Abs(alpha - _cardAlpha) < 0.002f)
            return;
        _cardAlpha = alpha;
        _cardGroup.alpha = alpha;
    }

    /// <summary>자막 한 줄 (조작을 막지 않는 오버레이 줄, 실시간). 대사 옵션이 꺼져 있으면 무시.</summary>
    public bool ShowLine(string speaker, string text, float hold)
    {
        if (_subtitles == null || !CinematicSettings.SubtitlesEnabled)
            return false;
        return _subtitles.ShowOverlayLine(_canvas, speaker, text, hold);
    }

    public void HideLine()
    {
        if (_subtitles != null)
            _subtitles.Hide();
    }

    public void ResetAll()
    {
        SetLetterbox(0f);
        SetFade(Color.black, 0f);
        SetSkipPrompt(0f, 0f);
        SetCard(0f);
        HideLine();
    }

    void RefreshSkipText()
    {
        if (!_glyphHooked)
        {
            _glyphHooked = true;
            InputGlyphs.Changed += HandleGlyphsChanged;
        }
        _skipTextDirty = false;
        _skipText.text = "[" + InputGlyphs.Get(ChuonAction.Interact) + "]  길게 눌러 건너뛰기";
    }

    void HandleGlyphsChanged() => _skipTextDirty = true;

    // ------------------------------------------------------------------ ui helpers

    static Image NewImage(string name, Transform parent, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Text NewText(string name, RectTransform parent, Font font, int size, Color color, Vector2 pos, Vector2 box)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = box;
        rt.anchoredPosition = pos;
        return text;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
