using System.Collections;
using UnityEngine;

/// <summary>
/// 레거시 UnityEngine.UI.Text(LegacyRuntime.ttf, 동적 폰트) 글리프 사전 캐시.
/// LegacyRuntime 에는 한글이 없어서 첫 한글 요청 때 OS 대체 폰트 탐색/로드 + 래스터화가 한 프레임에 몰린다
/// (빌드에서 Level_Facility 로드 첫 프레임 Font.CacheFontForText 290~1120ms).
/// BootScene(로딩 화면)에서 UI 에 쓰이는 한글/ASCII 를 실제 사용 크기별로 미리 요청해 둔다.
/// 동적 폰트 텍스처는 씬 전환 후에도 유지되므로 이후 씬 로드는 캐시 히트.
/// 글자 목록은 Assets/Scripts 문자열 리터럴 + 씬/프리팹 텍스트에서 추출 (UI 문구가 크게 바뀌면 갱신).
/// </summary>
public static class LegacyFontGlyphWarmup
{
    // UI 문구에서 추출한 한글 (424자)
    const string HangulChars =
        "가각간갇감값강같개거걱건검게겟겠격결겹경계곗고공곷과관괜교구군궁권그극근글금기길김깆까깎깚깥깨꺼꺽께꾧꾩끄끊끌끝끻나남납" +
        "났낮내낸너널넣네높놨뇼누눌는능늦니다닥단닫닿대댁댐덀더데델도독돌동돼됐되됩두둬뒤뒪드든들듿디따때떊또뚮뜻라락람랑래러런럼" +
        "렇레렌려력련렸로록롭룄료룹뤻류륨를름릇리릭림링마막만말망맞맵먯먼메면모목몰뫼무문뭻미밀밍및바박밖반받발밝방배밸버번범벗베" +
        "벨벽별볤보복본볼봐부붉붙브블비빈빌빠빨빺뺡뺣뻃뻴뻿뼕뼱뽤뽥쀾사살상생서섞선설성세셋션소속손솔숄수순스습승시신실심쎖쒻쓬씬" +
        "아안압앙앰앵야약어얼없었에엔엘여역연열영예오온와완요용우운울움워원위유율으은을음응의이인일읽임입있자작잔장재저적전젅젏점" +
        "접정제젝져졌조존졻종좋좌주준중즈즉지직진질짐집짧쩍쪽찍착찮창천첫체쳐최추출춤충춰측층치칠침카캐컃컓커컨켜콘콚콣콤크클키킼" +
        "타탑태탭터턴텀템토톤투퉻튜트튼틀틈팅파패퍼페펙편퐜표푺품프플피픽필핑하한할함합항해핵했행향허험혀현형호화확환활회효후훈휠" +
        "흐흔흘힘";

    const string AsciiChars =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
        "·•…–—→←↑↓▶◀▲▼■□●○◆◇★☆※×÷%°";

    // 레거시 Text 실사용 크기/스타일 (Level_Facility 런타임 조사: 18 Bold x33, 19 x14, 16 Bold x9, 18 x4 ...).
    // 한글 전체는 주요 4개 조합만, 나머지 크기는 ASCII 만 (전 조합 한글 = 8.7k 글리프, 빌드에서 0.5~1.3s 라 과함).
    static readonly (int size, FontStyle style)[] HangulCombos =
    {
        (18, FontStyle.Bold), (19, FontStyle.Normal), (16, FontStyle.Bold), (18, FontStyle.Normal)
    };
    static readonly int[] AsciiSizes = { 14, 16, 20, 22, 24, 26, 34, 42 };
    const int CharsPerFrame = 64; // 프레임당 요청 글자 수 (로딩 화면 프레임 유지)

    static bool s_done;
    public static bool IsDone => s_done;
    /// <summary>마지막 워밍업에 든 시간(ms, 요청 시간 합)과 가장 긴 한 프레임 요청(ms).</summary>
    public static float LastWarmupMs { get; private set; }
    public static float LastWarmupMaxFrameMs { get; private set; }

    /// <summary>여러 프레임에 나눠 요청 (로딩 화면 애니메이션이 멈추지 않게).</summary>
    public static IEnumerator WarmRoutine()
    {
        if (s_done)
            yield break;

        s_done = true;
        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();
        if (font == null || !font.dynamic)
            yield break;

        float total = 0f, maxFrame = 0f;
        string hangul = AsciiChars + HangulChars;
        for (int c = 0; c < HangulCombos.Length; c++)
        {
            for (int start = 0; start < hangul.Length; start += CharsPerFrame)
            {
                string chunk = hangul.Substring(start, Mathf.Min(CharsPerFrame, hangul.Length - start));
                float ms = Request(font, chunk, HangulCombos[c].size, HangulCombos[c].style);
                total += ms;
                maxFrame = Mathf.Max(maxFrame, ms);
                yield return null;
            }
        }

        for (int i = 0; i < AsciiSizes.Length; i++)
        {
            float ms = Request(font, AsciiChars, AsciiSizes[i], FontStyle.Bold) + Request(font, AsciiChars, AsciiSizes[i], FontStyle.Normal);
            total += ms;
            maxFrame = Mathf.Max(maxFrame, ms);
            yield return null;
        }

        LastWarmupMs = total;
        LastWarmupMaxFrameMs = maxFrame;
        if (Debug.isDebugBuild)
            Debug.Log($"[LegacyFontGlyphWarmup] {hangul.Length} chars x {HangulCombos.Length} combos + ASCII x {AsciiSizes.Length * 2}: {total:F0} ms total, worst frame {maxFrame:F0} ms, texture {DescribeTexture(font)}");
    }

    static float Request(Font font, string chars, int size, FontStyle style)
    {
        float start = Time.realtimeSinceStartup;
        font.RequestCharactersInTexture(chars, size, style);
        return (Time.realtimeSinceStartup - start) * 1000f;
    }

    static string DescribeTexture(Font font)
    {
        Texture tex = font.material != null ? font.material.mainTexture : null;
        return tex != null ? tex.width + "x" + tex.height : "?";
    }
}
