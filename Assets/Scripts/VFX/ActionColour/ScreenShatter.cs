// 추온 화면 갈라짐 — 호환 래퍼 (반복 14S)
// 반복 11 의 보로노이 유리 파편은 '직선 화면 베기'(ScreenCut)로 대체됐다. 남은 호출은 느린 한 줄 베기 전환으로 보낸다.
using UnityEngine;

public static class ScreenShatter
{
    public static bool IsPlaying => ScreenCut.IsPlaying;
    public static int PlayCount => ScreenCut.PlayCount;

    /// <summary>impactViewport 를 지나는 한 줄 베기 전환. crack/burst 길이는 더 이상 쓰지 않는다 (패턴 고정).</summary>
    public static void Play(Vector2 impactViewport, float crackDuration = 0.2f, float burstDuration = 0.6f, System.Action onBurst = null)
    {
        ScreenCut.PlayTransition(impactViewport, onBurst);
    }

    public static void Skip()
    {
        ScreenCut.Skip();
    }
}
