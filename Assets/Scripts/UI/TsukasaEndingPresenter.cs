using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 엔딩 (Docs/Story_Reference.md): Type-D 가 쓰러진 직후 과학자 츠카사가 홀로그램으로 등장해 한 마디를 남긴다.
/// 화면 오버레이(실루엣 홀로 패널 + 자막)라 씬 배치가 필요 없고, F / Space / Enter / 클릭으로 건너뛸 수 있다.
/// 전부 실시간(unscaled) 기준이라 슬로모션·일시정지와 무관하게 끝나고, 출구(크레딧) 진행을 막지 않는다.
/// </summary>
public sealed class TsukasaEndingPresenter : MonoBehaviour
{
    public const string SpeakerName = "츠카사"; // 츠카사
    public const string CanonLine = "결국 일어나 버렸나.. 하지만 넌 아직 내가 설계한 궤도 위를 걷고 있을 뿐이다.";

    /// <summary>QA: 마지막으로 엔딩이 끝난(또는 건너뛴) 시각 (realtime). 재생 전 -1.</summary>
    public static float LastFinishedAt { get; private set; } = -1f;
    public static int PlayCount { get; private set; }
    public static bool IsPlaying => s_active != null;
    public static event Action Finished;

    static TsukasaEndingPresenter s_active;
    static Texture2D s_silhouette;

    Action _onDone;
    bool _skip;
    CanvasGroup _group;
    RectTransform _panel;
    RawImage _figure;
    Text _lineText;
    Text _speakerText;
    Image _subBg;
    Image _dim;

    // 대사 테이블 키 (Resources/Dialogue/DialogueTable_KO). 정사 대사는 테이블이 없어도 CanonLine 그대로.
    const string CanonLineKey = "ending.tsukasa";
    const string ReplyPrefix = "ending.reply";

    public static TsukasaEndingPresenter Play(Action onDone)
    {
        if (s_active != null)
        {
            s_active._onDone += onDone;
            return s_active;
        }

        var go = new GameObject("TsukasaEndingHologram", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        var presenter = go.AddComponent<TsukasaEndingPresenter>();
        presenter._onDone = onDone;
        presenter.Build();
        // 반복 14S: 결전의 화면이 느린 한 줄 베기로 갈라지며 홀로그램 통신으로 전환 (궁극기 베기와 같은 언어)
        ScreenCut.PlayTransition(new Vector2(0.5f, 0.52f));
        s_active = presenter;
        PlayCount++;
        presenter.StartCoroutine(presenter.CoPlay());
        return presenter;
    }

    void OnDestroy()
    {
        if (s_active == this)
            s_active = null;
    }

    void Build()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 860;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();
        var red = new Color(1f, 0.12f, 0.1f, 0.95f);
        var white = new Color(0.93f, 0.96f, 1f, 0.96f);

        _dim = NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.5f));
        Stretch(_dim.rectTransform);

        // 홀로 패널 (오른쪽 중앙): 그래파이트 바탕 + 얇은 레드 라인 + 흰 코너 틱
        _panel = NewRect("HoloPanel", transform);
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(480f, 600f);
        _panel.anchoredPosition = new Vector2(-580f, 90f);
        Image bg = NewImage("Bg", _panel, new Color(0.03f, 0.03f, 0.04f, 0.78f));
        Stretch(bg.rectTransform);
        Bar(_panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 3f), red);
        Bar(_panel, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 3f), red);
        Bar(_panel, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Color(1f, 1f, 1f, 0.25f));
        Bar(_panel, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Color(1f, 1f, 1f, 0.25f));

        // 반복 16: 보조 문구 15 px·흰 60 % → 18 px·흰 80 % (그래파이트 위 대비 ≥ 4.5:1)
        Text tag = NewText("Tag", _panel, font, 18, ChuonUiPalette.WithAlpha(ChuonUiPalette.MutedWhite, 0.9f), TextAnchor.UpperLeft);
        tag.text = "HOLO-LINK  //  PRIORITY CHANNEL  //  B4 HANGAR";
        Place(tag.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(460f, 22f), new Vector2(0f, 1f));

        _figure = new GameObject("Silhouette", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        _figure.transform.SetParent(_panel, false);
        _figure.texture = GetSilhouette();
        _figure.color = new Color(0.9f, 0.95f, 1f, 0.9f);
        _figure.raycastTarget = false;
        Place(_figure.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(210f, 420f), new Vector2(0.5f, 0.5f));

        Text nameText = NewText("Name", _panel, font, 46, white, TextAnchor.LowerLeft);
        nameText.fontStyle = FontStyle.Bold;
        nameText.text = "TSUKASA";
        Place(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(24f, 58f), new Vector2(460f, 56f), new Vector2(0f, 0f));
        Text title = NewText("Title", _panel, font, 18, ChuonUiPalette.TsukasaRedText, TextAnchor.LowerLeft);
        title.text = "TSUKASA CORPORATION  //  FOUNDER";
        Place(title.rectTransform, new Vector2(0f, 0f), new Vector2(26f, 30f), new Vector2(460f, 24f), new Vector2(0f, 0f));

        // 자막 (하단 중앙)
        RectTransform sub = NewRect("Subtitle", transform);
        Place(sub, new Vector2(0.5f, 0f), new Vector2(0f, 175f), new Vector2(1280f, 120f), new Vector2(0.5f, 0f));
        Image subBg = NewImage("Bg", sub, new Color(0f, 0f, 0f, 0.62f));
        Stretch(subBg.rectTransform);
        _subBg = subBg;
        Bar(sub, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f), red);
        Text speaker = NewText("Speaker", sub, font, 22, ChuonUiPalette.TsukasaRedText, TextAnchor.UpperLeft);
        speaker.fontStyle = FontStyle.Bold;
        speaker.text = DialogueTable.Speaker(CanonLineKey, SpeakerName);
        _speakerText = speaker;
        Place(speaker.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -12f), new Vector2(400f, 30f), new Vector2(0f, 1f));
        _lineText = NewText("Line", sub, font, 30, white, TextAnchor.UpperLeft);
        _lineText.horizontalOverflow = HorizontalWrapMode.Wrap;
        Place(_lineText.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -46f), new Vector2(1224f, 70f), new Vector2(0f, 1f));

        // 반복 16 (E2): 16 px 흰 55 % 글자 → 키 글리프 상자 + 24 px 설명
        RectTransform hint = ChuonKeyHint.Create(transform, "SkipHint", ChuonKeyHint.SkipKeys(), "건너뛰기", 24);
        hint.anchorMin = hint.anchorMax = new Vector2(1f, 0f);
        hint.pivot = new Vector2(1f, 0f);
        hint.anchoredPosition = new Vector2(-40f, 36f);

        // 옵션 메뉴의 자막 크기/배경 설정 반영
        float scale = Mathf.Clamp(OptionsManagerAdvanced.CurrentSubtitleSize, 0.6f, 1.8f);
        _lineText.fontSize = Mathf.RoundToInt(30 * scale);
        speaker.fontSize = Mathf.RoundToInt(22 * scale);
        _subBg.enabled = OptionsManagerAdvanced.CurrentSubtitleBackground;
    }

    IEnumerator CoPlay()
    {
        // 글리치 인 (패널이 세로로 펼쳐지며 깜빡임)
        float t = 0f;
        const float glitchIn = 0.4f;
        while (t < glitchIn && !_skip)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / glitchIn);
            _group.alpha = Mathf.Lerp(0f, 1f, k) * (UnityEngine.Random.value < 0.25f ? 0.4f : 1f);
            _panel.localScale = new Vector3(1f, Mathf.SmoothStep(0.02f, 1f, k), 1f);
            PollSkip();
            yield return null;
        }
        _group.alpha = 1f;
        _panel.localScale = Vector3.one;

        // 타이핑 (츠카사 정사 대사)
        yield return CoTypeLine(DialogueTable.Text(CanonLineKey, CanonLine), 2.8f);

        // 짧은 응수 (추온 / EGO) — 테이블에 있을 때만. 건너뛰기는 전부 건너뛴다.
        var replies = DialogueTable.GetSequence(ReplyPrefix);
        for (int i = 0; i < replies.Count && !_skip; i++)
        {
            DialogueTable.Entry reply = replies[i];
            _speakerText.text = string.IsNullOrEmpty(reply.speaker) ? "EGO" : reply.speaker;
            _speakerText.color = ChuonUiPalette.Speaker(_speakerText.text); // 반복 16 (E7): EGO 시안 → 스틸 화이트
            _lineText.text = string.Empty;
            yield return CoTypeLine(reply.text, reply.duration > 0f ? reply.duration : 1.8f);
        }

        // 글리치 아웃
        t = 0f;
        const float glitchOut = 0.35f;
        while (t < glitchOut)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / glitchOut);
            _group.alpha = (1f - k) * (UnityEngine.Random.value < 0.3f ? 0.3f : 1f);
            _panel.localScale = new Vector3(1f + k * 0.08f, Mathf.Lerp(1f, 0.02f, k * k), 1f);
            yield return null;
        }

        LastFinishedAt = Time.realtimeSinceStartup;
        Action done = _onDone;
        _onDone = null;
        s_active = null;
        try { done?.Invoke(); Finished?.Invoke(); }
        finally { Destroy(gameObject); }
    }

    IEnumerator CoTypeLine(string line, float holdSeconds)
    {
        line = line ?? string.Empty;
        float typeTime = 0f;
        const float charsPerSecond = 22f;
        while (!_skip)
        {
            typeTime += Time.unscaledDeltaTime;
            int n = Mathf.Min(line.Length, Mathf.FloorToInt(typeTime * charsPerSecond));
            _lineText.text = line.Substring(0, n);
            Flicker();
            PollSkip();
            if (n >= line.Length)
                break;
            yield return null;
        }
        _lineText.text = line;

        // 여운
        float hold = 0f;
        while (hold < holdSeconds && !_skip)
        {
            hold += Time.unscaledDeltaTime;
            Flicker();
            PollSkip();
            yield return null;
        }
    }

    void Flicker()
    {
        if (_figure == null)
            return;
        float n = Mathf.PerlinNoise(Time.unscaledTime * 9f, 0.3f);
        Color c = _figure.color;
        c.a = n < 0.12f ? 0.35f : 0.82f + 0.1f * n;
        _figure.color = c;
        Vector2 p = _figure.rectTransform.anchoredPosition;
        p.x = n > 0.93f ? 6f : 0f;
        _figure.rectTransform.anchoredPosition = p;
    }

    void PollSkip()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;
        if (kb != null && (kb.fKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
            || mouse != null && mouse.leftButton.wasPressedThisFrame
            || pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
            _skip = true;
    }

    /// <summary>QA/외부에서 강제 건너뛰기.</summary>
    public static void SkipActive()
    {
        if (s_active != null)
            s_active._skip = true;
    }

    // ------------------------------------------------------------------ UI helpers

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var img = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Text NewText(string name, Transform parent, Font font, int size, Color color, TextAnchor anchor)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    static void Bar(RectTransform parent, Vector2 min, Vector2 max, Vector2 thickness, Color color)
    {
        Image bar = NewImage("Line", parent, color);
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = thickness;
    }

    // 키 큰 인물(롱코트) 실루엣: 흰색 + 스캔라인 + 가장자리 글로우. 얼굴은 그리지 않는다.
    static Texture2D GetSilhouette()
    {
        if (s_silhouette != null)
            return s_silhouette;

        const int w = 128, h = 256;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "TsukasaSilhouette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float d = SilhouetteDistance(x + 0.5f, y + 0.5f);
                float fill = Mathf.Clamp01(0.5f - d);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d) / 3.5f) * 0.9f;
                float scan = (y % 4) < 1 ? 0.45f : 1f;
                float a = Mathf.Max(fill * 0.62f * scan, rim);
                float fade = Mathf.Clamp01(y / 40f); // 발치는 투사광처럼 흐리게
                a *= Mathf.Lerp(0.25f, 1f, fade);
                byte g = (byte)(rim > fill * 0.62f ? 255 : 225);
                px[y * w + x] = new Color32(g, g, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s_silhouette = tex;
        return tex;
    }

    // 부호 거리(픽셀): 음수 = 내부. 롱코트 차림의 키 큰 인물 (머리·목·어깨 경사·팔 틈·코트 밑단·다리)
    static float SilhouetteDistance(float x, float y)
    {
        const float cx = 64f;
        float dx = Mathf.Abs(x - cx);
        float head = new Vector2(dx / 12f, (y - 228f) / 15f).magnitude * 13f - 13f;
        float neck = Box(dx, y - 207f, 5f, 7f);
        // 어깨 경사 + 몸통/코트: 높이별 반폭
        float half;
        if (y > 200f) half = -1f;
        else if (y > 190f) half = Mathf.Lerp(36f, 12f, (y - 190f) / 10f);
        else if (y > 150f) half = Mathf.Lerp(31f, 36f, (y - 150f) / 40f);
        else if (y > 108f) half = Mathf.Lerp(30f, 31f, (y - 108f) / 42f);
        else half = Mathf.Lerp(37f, 30f, Mathf.Clamp01((y - 38f) / 70f));
        float body = Mathf.Max(dx - half, Mathf.Max(y - 200f, 38f - y));
        // 팔과 몸통 사이 틈 (팔 윤곽)
        float armGap = -Box(dx - 24f, y - 150f, 0.9f, 38f);
        body = Mathf.Max(body, armGap);
        // 코트 앞 여밈 틈
        body = Mathf.Max(body, -Box(dx, y - 62f, 0.8f, 26f));
        // 다리 (코트 아래)
        float legs = Box(dx - 9f, y - 22f, 6f, 22f);
        return Mathf.Min(Mathf.Min(head, neck), Mathf.Min(body, legs));
    }

    static float Box(float dx, float dy, float hx, float hy)
    {
        return Mathf.Max(Mathf.Abs(dx) - hx, Mathf.Abs(dy) - hy);
    }
}
