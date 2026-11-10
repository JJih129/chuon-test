// 파일명: KioskUi.cs
// 키오스크 오버레이 공용 UI 빌더. 츠카사 HUD 톤 (LoadingTipOverlay 와 같은 팔레트):
// 짙은 패널 + 오프화이트 라인 + 레드 액센트 + "TSUKASA CORP.  //  ..." 헤더.
using UnityEngine;
using UnityEngine.UI;

public static class KioskUi
{
    public static readonly Color Red = new Color(0.878f, 0.188f, 0.227f, 1f);
    public static readonly Color White = new Color(0.97f, 0.97f, 0.98f, 1f);
    public static readonly Color Line = new Color(0.93f, 0.94f, 0.95f, 0.55f);
    public static readonly Color Muted = new Color(0.80f, 0.82f, 0.85f, 0.72f);
    public static readonly Color Panel = new Color(0.035f, 0.04f, 0.045f, 0.92f);
    public static readonly Color Dim = new Color(0f, 0f, 0f, 0.62f);

    public static Font Font => RuntimeBuiltInFontUtility.GetDefaultFont();

    /// <summary>DontDestroyOnLoad 부모 아래 스크린 오버레이 캔버스 (1920×1080 기준, 입력 차단 없음).</summary>
    public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder, out CanvasGroup group)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvas.enabled = false;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        return canvas;
    }

    public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    /// <summary>중앙 기준 고정 크기 카드.</summary>
    public static RectTransform CreateCard(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static Image CreateImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
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

    public static Image CreateFullscreen(string name, Transform parent, Color color)
        => CreateImage(name, parent, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

    public static Text CreateText(string name, Transform parent, string content, int size, FontStyle style, TextAnchor anchor, Color color,
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
        text.font = Font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        return text;
    }

    /// <summary>츠카사 카드 프레임: 패널 + 상/하단 라인 + 왼쪽 레드 바 + 우상단 틱 + 헤더.</summary>
    public static Text DecorateCard(RectTransform card, string header, Color? panelColor = null)
    {
        CreateImage("Panel", card, panelColor ?? Panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        CreateImage("LineTop", card, Line, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), Vector2.zero);
        CreateImage("LineBottom", card, new Color(Line.r, Line.g, Line.b, 0.30f), new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));
        CreateImage("AccentBar", card, Red, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f));
        CreateImage("AccentTick", card, Red, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-64f, -3f), Vector2.zero);
        CreateImage("HeaderRule", card, new Color(Line.r, Line.g, Line.b, 0.25f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -46f), new Vector2(-24f, -45f));
        return CreateText("Header", card, header, 15, FontStyle.Bold, TextAnchor.MiddleLeft, Muted,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -44f), new Vector2(-24f, -10f));
    }
}
