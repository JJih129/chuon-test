// 추온 화면 베기 API (반복 14S) — 화면 자체가 검에 베인다. 유리 파편 ScreenShatter 를 대체.
// 정지 프레임 캡처 → 직선 베기 2~3 개를 차례로 긋는다 → 정적(어둡게·채도 빠짐, 납도 '딸깍') → 2 프레임 반전 플래시와
// 함께 그 직선들로 화면이 큰 불투명 다각형 조각으로 갈라져 미끄러진다 (틈은 그래파이트 단색) → 2 프레임 만에 라이브로 스냅.
// 모두 실시간(unscaled) 기준.
// 접근성: '화면 번쩍임' 0 근처 → 번쩍임·빠른 이동 없이 베인 선에서 라이브 화면이 번지는 크로스페이드.
using UnityEngine;

/// <summary>베기 선 하나. 좌표는 뷰포트(0..1), 각도는 실제 화면 기준(0 = 오른쪽, 90 = 위)으로 그리는 방향.</summary>
public struct ScreenCutLine
{
    public Vector2 Through;
    public float AngleDeg;
    public float Start;        // 베기 시작 기준 그리기 시작 (s)
    public float Duration;     // 화면을 가로지르는 시간 (s)
    public float Slide;        // 분리 시 선 방향 미끄러짐 (화면 높이 = 2)
    public float Push;         // 분리 시 선 법선 방향 벌어짐
    public float RotationDeg;  // 분리 시 조각 회전
}

public struct ScreenCutPattern
{
    public int Count;
    public ScreenCutLine A, B, C;
    public float Hold;          // 마지막 선이 그려진 뒤 정적 (s)
    public float SplitDuration; // 조각이 갈라져 사라지는 시간 (s)
    public float Dim;           // 정지 프레임 어둡게 0..1
    public float SpeedLines;    // 0..1
    public float Streak;        // 0..1
    public float GapFlash;      // 0..1
    public bool Sfx;

    public ScreenCutLine Get(int i) => i == 0 ? A : i == 1 ? B : C;

    public float LinesEnd
    {
        get
        {
            float end = 0f;
            for (int i = 0; i < Mathf.Min(Count, ActionColourState.CutMaxLines); i++)
            {
                ScreenCutLine l = Get(i);
                end = Mathf.Max(end, l.Start + l.Duration);
            }
            return end;
        }
    }

    public float SplitAt => LinesEnd + Hold;
    public float Total => SplitAt + SplitDuration;
}

public static class ScreenCut
{
    public static bool IsPlaying => ActionColourDirector.CutPlaying;
    public static int PlayCount { get; private set; }

    /// <summary>재생. onRelease: 조각이 끝나 라이브 화면으로 스냅되는 순간 (임팩트·재생 재개 지점). 건너뛰기/중단 시에도 한 번 호출된다.</summary>
    public static void Play(in ScreenCutPattern pattern, System.Action onRelease = null)
    {
        PlayCount++;
        ActionColourDirector.StartCut(pattern, onRelease);
    }

    /// <summary>궁극기 '위상단절검' 마지막 일격: 세 줄 베기 → 정적 → 일곱 조각 분리 (약 0.9 s).</summary>
    public static void PlayUltimate(Vector2 impactViewport, System.Action onRelease = null)
    {
        Play(UltimatePattern(impactViewport), onRelease);
    }

    /// <summary>장면 전환(엔딩 등): 느린 한 줄 베기 → 두 조각 (약 1 s).</summary>
    public static void PlayTransition(Vector2 throughViewport, System.Action onRelease = null)
    {
        Play(TransitionPattern(throughViewport), onRelease);
    }

    public static void Skip()
    {
        ActionColourDirector.SkipCut();
    }

    public static ScreenCutPattern UltimatePattern(Vector2 impact)
    {
        // impact = 대상 몸통 중심. 세 선 모두 대각선(수평에서 24~48°)으로 몸통을 비스듬히 지나고,
        // 얼굴(몸통 위쪽)을 세로로 가르지 않게 한다. 한 점에 모이지 않게 살짝 어긋나 큰 다각형 조각이 된다.
        return new ScreenCutPattern
        {
            Count = 3,
            A = new ScreenCutLine { Through = impact + new Vector2(-0.02f, 0.02f), AngleDeg = -24f, Start = 0f, Duration = 0.075f, Slide = 0.075f, Push = 0.02f, RotationDeg = 0.5f },
            B = new ScreenCutLine { Through = impact + new Vector2(0.04f, -0.05f), AngleDeg = 206f, Start = 0.095f, Duration = 0.07f, Slide = 0.06f, Push = 0.018f, RotationDeg = -0.7f },
            C = new ScreenCutLine { Through = impact + new Vector2(-0.01f, -0.11f), AngleDeg = 132f, Start = 0.185f, Duration = 0.08f, Slide = 0.05f, Push = 0.024f, RotationDeg = 0.35f },
            Hold = 0.2f,
            SplitDuration = 0.42f,
            Dim = 0.22f,
            SpeedLines = 1f,
            Streak = 1f,
            GapFlash = 0.9f,
            Sfx = true
        };
    }

    /// <summary>
    /// 궁극기 나이트브링어 피니시 (반복 17): 걸린 절단선이 동시에 터진 정지 프레임을 대상 몸통을 지나는 한 줄 대각선으로 가른다
    /// → 짧은 정적 → 두 조각 (약 0.77 s). 효과음은 비트 구동기(UltimatePhaseSeveringBeats)가 폭발과 함께 낸다.
    /// </summary>
    public static ScreenCutPattern NightbringerPattern(Vector2 impact)
    {
        return new ScreenCutPattern
        {
            Count = 1,
            A = new ScreenCutLine { Through = impact + new Vector2(0f, 0.02f), AngleDeg = -33f, Start = 0f, Duration = 0.085f, Slide = 0.11f, Push = 0.03f, RotationDeg = 0.8f },
            Hold = 0.18f,
            SplitDuration = 0.5f,
            Dim = 0.18f,
            SpeedLines = 0.5f,
            Streak = 1f,
            GapFlash = 1f,
            Sfx = false
        };
    }

    public static ScreenCutPattern TransitionPattern(Vector2 through)
    {
        return new ScreenCutPattern
        {
            Count = 1,
            A = new ScreenCutLine { Through = through, AngleDeg = 172f, Start = 0f, Duration = 0.2f, Slide = 0.09f, Push = 0.035f, RotationDeg = 0.6f },
            Hold = 0.16f,
            SplitDuration = 0.62f,
            Dim = 0.12f,
            SpeedLines = 0f,
            Streak = 0.6f,
            GapFlash = 0.6f,
            Sfx = true
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PlayCount = 0;
    }
}
