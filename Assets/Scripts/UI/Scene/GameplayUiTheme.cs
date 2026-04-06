using UnityEngine;

[CreateAssetMenu(menuName = "UI/Gameplay UI Theme", fileName = "GameplayUiTheme")]
public sealed class GameplayUiTheme : ScriptableObject
{
    public const string DefaultResourcePath = "UI/Game/GameplayUiTheme_Default";
    static Sprite s_RuntimeFallbackSprite;

    [Header("Fonts")]
    public Font titleFont;
    public Font bodyFont;

    [Header("Sprites")]
    public Sprite frameSprite;
    public Sprite panelSprite;
    public Sprite buttonSprite;

    [Header("Death Overlay")]
    public Color overlayColor = new Color(0.01f, 0.01f, 0.02f, 0.94f);
    public Color vignetteColor = new Color(0f, 0f, 0f, 0.65f);
    public Color bandColor = new Color(0.08f, 0.02f, 0.02f, 0.84f);
    public Color panelColor = new Color(0.07f, 0.08f, 0.10f, 0.95f);
    public Color panelLineColor = new Color(0.76f, 0.14f, 0.16f, 0.96f);
    public Color accentColor = new Color(1f, 0.32f, 0.28f, 0.98f);
    public Color accentSecondaryColor = new Color(1f, 0.62f, 0.52f, 0.96f);
    public Color titleColor = new Color(0.92f, 0.16f, 0.18f, 1f);
    public Color subtitleColor = new Color(1f, 0.90f, 0.88f, 0.90f);
    public Color bodyColor = new Color(0.93f, 0.95f, 0.98f, 0.95f);
    public Color mutedColor = new Color(0.78f, 0.82f, 0.88f, 0.82f);
    public Color buttonIdleColor = new Color(0.12f, 0.13f, 0.16f, 0.96f);
    public Color buttonHoverColor = new Color(0.30f, 0.09f, 0.09f, 0.98f);
    public Color buttonPressedColor = new Color(0.38f, 0.10f, 0.10f, 1f);
    public Color buttonTextColor = new Color(0.98f, 0.98f, 0.99f, 1f);
    public Color buttonMutedTextColor = new Color(0.86f, 0.87f, 0.90f, 0.95f);

    [Header("Typography")]
    public int deathTitleFontSize = 110;
    public int deathSubtitleFontSize = 22;
    public int buttonFontSize = 24;
    public int detailFontSize = 14;

    public static GameplayUiTheme LoadOrCreate(string resourcePath = DefaultResourcePath)
    {
        GameplayUiTheme loaded = Resources.Load<GameplayUiTheme>(resourcePath);
        return loaded != null ? loaded : CreateRuntimeFallback();
    }

    public static GameplayUiTheme CreateRuntimeFallback()
    {
        GameplayUiTheme theme = CreateInstance<GameplayUiTheme>();
        theme.hideFlags = HideFlags.HideAndDontSave;
        return theme;
    }

    public Font ResolveTitleFont()
    {
        return titleFont != null ? titleFont : RuntimeBuiltInFontUtility.GetDefaultFont();
    }

    public Font ResolveBodyFont()
    {
        return bodyFont != null ? bodyFont : RuntimeBuiltInFontUtility.GetDefaultFont();
    }

    public Sprite ResolveFrameSprite()
    {
        return frameSprite != null ? frameSprite : GetRuntimeFallbackSprite();
    }

    public Sprite ResolvePanelSprite()
    {
        return panelSprite != null ? panelSprite : ResolveFrameSprite();
    }

    public Sprite ResolveButtonSprite()
    {
        return buttonSprite != null ? buttonSprite : ResolveFrameSprite();
    }

    static Sprite GetRuntimeFallbackSprite()
    {
        if (s_RuntimeFallbackSprite != null)
            return s_RuntimeFallbackSprite;

        Texture2D whiteTexture = Texture2D.whiteTexture;
        if (whiteTexture == null)
            return null;

        s_RuntimeFallbackSprite = Sprite.Create(
            whiteTexture,
            new Rect(0f, 0f, whiteTexture.width, whiteTexture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        s_RuntimeFallbackSprite.name = "GameplayUiTheme_RuntimeFallback";
        return s_RuntimeFallbackSprite;
    }
}
