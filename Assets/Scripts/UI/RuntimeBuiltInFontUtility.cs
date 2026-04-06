using UnityEngine;

public static class RuntimeBuiltInFontUtility
{
    static Font s_defaultFont;

    public static Font GetDefaultFont()
    {
        if (s_defaultFont == null)
            s_defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return s_defaultFont;
    }
}
