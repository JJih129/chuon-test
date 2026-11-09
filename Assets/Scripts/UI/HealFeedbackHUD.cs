// 파일명: HealFeedbackHUD.cs
// 회복 앰플 결과 피드백 (츠카사 화이트/레드 HUD 스타일). 성공: "AMPOULE // 회복 +35%", 취소: "회복 실패 · 앰플 손실".
// 씬 로컬 런타임 캔버스 (필요할 때 생성). 입력을 막지 않는다.
using UnityEngine;
using UnityEngine.UI;

public sealed class HealFeedbackHUD : MonoBehaviour
{
    static HealFeedbackHUD s_instance;

    CanvasGroup _group;
    Text _header;
    Text _body;
    Image _accent;
    Image _flash;
    float _shownAt = -10f;
    float _hold = 1.1f;

    public static string LastMessage { get; private set; }

    public static void ShowApplied(float ratio)
    {
        Show("AMPOULE // RESTORE", "회복 +" + Mathf.RoundToInt(ratio * 100f) + "%", new Color(1f, 1f, 1f, 1f), new Color(0.88f, 0.19f, 0.23f, 1f), true);
    }

    public static void ShowCancelled()
    {
        Show("AMPOULE // INTERRUPTED", "회복 실패 · 앰플 손실", new Color(1f, 0.42f, 0.42f, 1f), new Color(0.88f, 0.19f, 0.23f, 1f), false);
    }

    public static void ShowNotice(string header, string body)
    {
        Show(header, body, new Color(1f, 1f, 1f, 1f), new Color(0.88f, 0.19f, 0.23f, 1f), false, 2.2f);
    }

    static void Show(string header, string body, Color bodyColor, Color accent, bool flash, float hold = 1.1f)
    {
        LastMessage = body;
        HealFeedbackHUD hud = Ensure();
        if (hud == null)
            return;
        hud._header.text = header;
        hud._body.text = body;
        hud._body.color = bodyColor;
        hud._accent.color = accent;
        hud._flash.color = flash ? new Color(1f, 1f, 1f, 0.16f) : new Color(0.9f, 0.1f, 0.15f, header.Contains("INTERRUPT") ? 0.14f : 0f);
        hud._shownAt = Time.unscaledTime;
        hud._hold = hold;
        hud.gameObject.SetActive(true);
    }

    static HealFeedbackHUD Ensure()
    {
        if (s_instance != null)
            return s_instance;

        var root = new GameObject("_HealFeedbackHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var hud = root.AddComponent<HealFeedbackHUD>();
        hud._group = root.GetComponent<CanvasGroup>();
        hud._group.blocksRaycasts = false;
        hud._group.interactable = false;

        // 화면 가장자리 플래시 (아주 옅게)
        hud._flash = NewImage("Flash", root.transform, new Color(1f, 1f, 1f, 0f));
        Stretch(hud._flash.rectTransform);

        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(root.transform, false);
        var cardRect = (RectTransform)card.transform;
        // 좌하단 플레이어 HUD(체력/페이즈 코어) 바로 위 — 캐릭터를 가리지 않게
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0f, 0f);
        cardRect.pivot = new Vector2(0f, 0f);
        cardRect.anchoredPosition = new Vector2(40f, 196f); // 자막 박스(하단 중앙)와 겹치지 않는 왼쪽
        cardRect.sizeDelta = new Vector2(330f, 64f);

        Image bg = NewImage("BG", card.transform, new Color(0.04f, 0.04f, 0.06f, 0.62f));
        Stretch(bg.rectTransform);
        hud._accent = NewImage("Accent", card.transform, new Color(0.88f, 0.19f, 0.23f, 1f));
        var ar = hud._accent.rectTransform;
        ar.anchorMin = new Vector2(0f, 0f); ar.anchorMax = new Vector2(0f, 1f); ar.pivot = new Vector2(0f, 0.5f);
        ar.sizeDelta = new Vector2(5f, 0f); ar.anchoredPosition = Vector2.zero;
        Image line = NewImage("Line", card.transform, new Color(1f, 1f, 1f, 0.55f));
        var lr = line.rectTransform;
        lr.anchorMin = new Vector2(0f, 1f); lr.anchorMax = new Vector2(1f, 1f); lr.pivot = new Vector2(0.5f, 1f);
        lr.sizeDelta = new Vector2(0f, 1.5f); lr.anchoredPosition = Vector2.zero;

        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();
        hud._header = NewText("Header", card.transform, font, 15, new Color(1f, 1f, 1f, 0.75f), FontStyle.Bold);
        var hr = hud._header.rectTransform;
        hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f); hr.pivot = new Vector2(0f, 1f);
        hr.offsetMin = new Vector2(18f, -26f); hr.offsetMax = new Vector2(-10f, -6f);
        hud._body = NewText("Body", card.transform, font, 26, Color.white, FontStyle.Bold);
        var br = hud._body.rectTransform;
        br.anchorMin = new Vector2(0f, 0f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(0f, 0f);
        br.offsetMin = new Vector2(18f, 4f); br.offsetMax = new Vector2(-10f, -24f);

        s_instance = hud;
        return hud;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text NewText(string name, Transform parent, Font font, int size, Color color, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    void Update()
    {
        float t = Time.unscaledTime - _shownAt;
        float alpha = t < 0.08f ? t / 0.08f : t < _hold ? 1f : Mathf.Clamp01(1f - (t - _hold) / 0.35f);
        _group.alpha = alpha;
        _flash.canvasRenderer.SetAlpha(Mathf.Clamp01(1f - t / 0.25f));
        if (alpha <= 0f && t > _hold)
            gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }
}
