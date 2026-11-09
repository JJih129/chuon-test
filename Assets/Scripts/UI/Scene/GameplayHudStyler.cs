using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임플레이 HUD(플레이어/퀘스트/보스) 색을 GameplayUiTheme 한 곳에서 적용.
/// 시안으로 구워진 원본 스프라이트를 같은 이름의 *_white 스프라이트(Resources/UI/HUD)로 바꾼 뒤 테마 색을 곱한다.
/// Tutorial / Level_Facility 모두 같은 경로를 타므로 HUD 톤이 일관됨.
/// </summary>
public static class GameplayHudStyler
{
    const string HudResourceFolder = "UI/HUD/";
    const string WhiteSuffix = "_white";

    static GameplayUiTheme s_theme;
    static readonly Dictionary<string, Sprite> s_whiteCache = new Dictionary<string, Sprite>();

    public static GameplayUiTheme Theme
    {
        get
        {
            if (s_theme == null)
                s_theme = GameplayUiTheme.LoadOrCreate();
            return s_theme;
        }
    }

    /// <summary>원본 스프라이트의 *_white 버전(없으면 null).</summary>
    public static Sprite ResolveWhiteSprite(Sprite source)
    {
        if (source == null)
            return null;

        string name = source.name;
        if (name.EndsWith(WhiteSuffix))
            return source;

        if (s_whiteCache.TryGetValue(name, out Sprite cached))
            return cached;

        Sprite white = Resources.Load<Sprite>(HudResourceFolder + name + WhiteSuffix);
        s_whiteCache[name] = white;
        return white;
    }

    /// <summary>흰 스프라이트로 바꾸고 색을 적용. 흰 버전이 없으면 색만 적용하지 않는다(시안 위 곱셈은 탁해짐).</summary>
    public static bool ApplyWhite(Image image, Color color)
    {
        if (image == null)
            return false;

        Sprite white = ResolveWhiteSprite(image.sprite);
        if (white == null)
            return false;

        image.sprite = white;
        image.color = color;
        return true;
    }

    public static bool ApplyFrame(Image frame)
    {
        return ApplyWhite(frame, Theme.hudFrameColor);
    }

    /// <summary>얼티밋 링: 충전 중 화이트, 가득 차면 레드.</summary>
    public static Color ResolveUltimateColor(float normalized, bool ready)
    {
        GameplayUiTheme t = Theme;
        if (ready || normalized >= 0.999f)
            return t.hudAccentColor;

        // 막바지(80%~)에 레드 쪽으로 살짝 기울여 곧 찬다는 신호
        float k = Mathf.InverseLerp(0.8f, 1f, normalized) * 0.35f;
        return Color.Lerp(t.hudUltimateChargingColor, t.hudAccentColor, k);
    }

    public static Color ResolveHpColor(float normalized)
    {
        GameplayUiTheme t = Theme;
        return normalized <= t.hudHpLowThreshold ? t.hudAccentColor : t.hudHpFillColor;
    }
}
