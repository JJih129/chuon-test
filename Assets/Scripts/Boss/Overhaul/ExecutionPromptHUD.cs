// 반복 16: 처형 표식 + 프롬프트. 보스가 브레이크(처형 가능)면 가슴 위에 TSUKASA 레드 다이아몬드 「처형」,
// 처형 거리 안이면 화면 하단 `[공격 글리프] 처형` / `암살` (InputGlyphs: 키보드·패드 전환 반영).
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ExecutionPromptHUD : MonoBehaviour
{
    static readonly Color Red = new Color(0.92f, 0.12f, 0.1f, 1f);
    static readonly Color White = new Color(0.96f, 0.95f, 0.93f, 1f);

    Canvas _canvas;
    TextMeshProUGUI _prompt;
    TextMeshProUGUI _marker;
    Image _promptBack;
    Image _diamond;
    Transform _player;
    float _nextResolveAt;
    float _promptAlpha;
    float _markerAlpha;

    public static string LastPrompt { get; private set; } = "";
    public static bool MarkerVisible { get; private set; }

    void Awake()
    {
        var go = new GameObject("OH_ExecutionPromptCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 70;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var back = new GameObject("PromptBack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        back.transform.SetParent(go.transform, false);
        _promptBack = back.GetComponent<Image>();
        _promptBack.color = new Color(0.06f, 0.06f, 0.07f, 0.72f);
        _promptBack.raycastTarget = false;
        RectTransform br = _promptBack.rectTransform;
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f);
        br.pivot = new Vector2(0.5f, 0f);
        br.anchoredPosition = new Vector2(0f, 168f);
        br.sizeDelta = new Vector2(340f, 54f);

        _prompt = MakeText(go.transform, "Prompt", 30f, White);
        RectTransform pr = _prompt.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
        pr.pivot = new Vector2(0.5f, 0f);
        pr.anchoredPosition = new Vector2(0f, 174f);
        pr.sizeDelta = new Vector2(600f, 44f);

        _marker = MakeText(go.transform, "Marker", 26f, Red);
        RectTransform mr = _marker.rectTransform;
        mr.anchorMin = mr.anchorMax = new Vector2(0f, 0f);
        mr.pivot = new Vector2(0.5f, 0.5f);
        mr.sizeDelta = new Vector2(220f, 40f);

        // 다이아몬드 (폰트 글리프 대신 회전한 사각형: 누락 글리프 경고 없음)
        var dia = new GameObject("Diamond", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dia.transform.SetParent(_marker.transform, false);
        _diamond = dia.GetComponent<Image>();
        _diamond.color = Red;
        _diamond.raycastTarget = false;
        RectTransform dr = _diamond.rectTransform;
        dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 1f);
        dr.pivot = new Vector2(0.5f, 0.5f);
        dr.sizeDelta = new Vector2(20f, 20f);
        dr.anchoredPosition = new Vector2(0f, 16f);
        dr.localRotation = Quaternion.Euler(0f, 0f, 45f);
        SetAlpha(0f, 0f);
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = ResolveKoreanFont();
        if (font != null)
            t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 4f;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    static TMP_FontAsset s_font;

    static TMP_FontAsset ResolveKoreanFont()
    {
        if (s_font != null)
            return s_font;
        if (ChuonUiFonts.Tmp != null) // 반복 16 (E8): Noto Sans KR Medium
            return s_font = ChuonUiFonts.Tmp;
        TMP_Text[] texts = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_FontAsset f = texts[i] != null ? texts[i].font : null;
            if (f != null && f.name.Contains("NotoSansKR"))
                return s_font = f;
        }
        return s_font = TMP_Settings.defaultFontAsset;
    }

    void SetAlpha(float prompt, float marker)
    {
        _promptAlpha = prompt;
        _markerAlpha = marker;
        Color c = _prompt.color; c.a = prompt; _prompt.color = c;
        Color b = _promptBack.color; b.a = 0.72f * prompt; _promptBack.color = b;
        Color m = _marker.color; m.a = marker; _marker.color = m;
        if (_diamond != null) { Color d = _diamond.color; d.a = marker; _diamond.color = d; }
        _canvas.enabled = prompt > 0.001f || marker > 0.001f;
    }

    void LateUpdate()
    {
        if (_player == null && Time.unscaledTime >= _nextResolveAt)
        {
            _nextResolveAt = Time.unscaledTime + 1f;
            PlayerReferences refs = FindObjectOfType<PlayerReferences>();
            _player = refs != null ? refs.PlayerRoot : null;
        }

        float dt = Time.unscaledDeltaTime;
        bool hidden = CinematicDirector.IsPlaying || ExecutionDirector.IsExecuting || GameTimeScale.IsPaused;
        string text = "";
        if (!hidden && _player != null && ExecutionDirector.TryGetPrompt(_player, out ExecutionKind kind, out _))
        {
            string glyph = InputGlyphs.Get(ChuonAction.AttackLight);
            text = $"<color=#E8231A>[{glyph}]</color>  " + (kind == ExecutionKind.Takedown ? "암살" : "처형");
        }
        if (text.Length > 0 && _prompt.text != text)
            _prompt.text = text;
        LastPrompt = text;

        // 월드 표식: 처형 가능한 보스 가슴 위
        TypeDOverhaul marked = hidden ? null : ExecutionDirector.MarkedBoss;
        bool markerOn = false;
        Camera cam = Camera.main;
        if (marked != null && cam != null)
        {
            Vector3 anchor = marked.MarkerAnchorPosition;
            Vector3 vp = cam.WorldToViewportPoint(anchor);
            if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f)
            {
                markerOn = true;
                RectTransform canvasRect = (RectTransform)_canvas.transform;
                Vector2 size = canvasRect.rect.size;
                _marker.rectTransform.anchoredPosition = new Vector2(vp.x * size.x, vp.y * size.y);
                float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 7f);
                _marker.rectTransform.localScale = Vector3.one * pulse;
                string m = marked.IsFinalExecutionPending ? "최종 처형" : "처형";
                if (_marker.text != m)
                    _marker.text = m;
            }
        }
        MarkerVisible = markerOn;

        float pa = Mathf.MoveTowards(_promptAlpha, text.Length > 0 ? 1f : 0f, dt * 8f);
        float ma = Mathf.MoveTowards(_markerAlpha, markerOn ? 1f : 0f, dt * 6f);
        if (!Mathf.Approximately(pa, _promptAlpha) || !Mathf.Approximately(ma, _markerAlpha))
            SetAlpha(pa, ma);
    }
}
