using UnityEngine;

public static class RuntimeBuiltInFontUtility
{
    // 반복 16 (E8): Arial(LegacyRuntime) 대신 Noto Sans KR Medium (한글을 OS 대체 글꼴에 맡기지 않는다). 없으면 LegacyRuntime.
    public static Font GetDefaultFont()
    {
        return ChuonUiFonts.Legacy;
    }
}
