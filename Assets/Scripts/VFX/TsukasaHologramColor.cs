// 반복 16: 홀로그램 색 규칙 (시안 금지, TSUKASA 화이트·레드). 씬에 직렬화된 예전 시안 값은 런타임에 바꿔 쓴다.
using UnityEngine;

public static class TsukasaHologramColor
{
    /// <summary>세라믹 화이트에 TSUKASA 레드가 스민 홀로그램 색 (알파는 유지).</summary>
    public static Color Resolve(Color serialized)
    {
        bool cyanish = serialized.b > serialized.r + 0.3f && serialized.g > serialized.r + 0.2f;
        if (!cyanish)
            return serialized;
        return new Color(1f, 0.52f, 0.46f, serialized.a);
    }
}
