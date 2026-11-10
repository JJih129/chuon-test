using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>화면 페이드 트랙 (CinematicOverlay 전체 화면 색). 바인딩 없음. 클립 블렌드(이즈 인/아웃) 지원.</summary>
[TrackColor(0.1f, 0.1f, 0.12f)]
[TrackClipType(typeof(CineFadeClip))]
public sealed class CineFadeTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        return ScriptPlayable<CineFadeMixer>.Create(graph, inputCount);
    }
}

/// <summary>반복 14C: 디렉터가 재생하는 그래프에서만 오버레이를 건드린다. 프리웜용 수동 그래프(CombatVfxPrewarmer: PlayableGraph.Create + Evaluate(0))는 리졸버가 없어 제외 — 시작이 검정인 연출이 화면을 검게 남기던 버그.</summary>
public static class CinePlayables
{
    public static bool IsDirectorGraph(Playable playable) => playable.GetGraph().GetResolver() is PlayableDirector;
}

public sealed class CineFadeMixer : PlayableBehaviour
{
    float _last = -1f;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!Application.isPlaying || !CinePlayables.IsDirectorGraph(playable))
            return;
        CinematicOverlay overlay = CinematicOverlay.Active;
        if (overlay == null)
            return;

        int count = playable.GetInputCount();
        float alpha = 0f;
        float weightSum = 0f;
        Color color = Color.black;
        for (int i = 0; i < count; i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f)
                continue;
            var input = (ScriptPlayable<CineFadeBehaviour>)playable.GetInput(i);
            CineFadeBehaviour b = input.GetBehaviour();
            float t = (float)(input.GetTime() / System.Math.Max(0.0001d, input.GetDuration()));
            t = Mathf.Clamp01(t);
            t = t * t * (3f - 2f * t);
            alpha += w * Mathf.Lerp(b.fromAlpha, b.toAlpha, t);
            color = Color.Lerp(color, b.color, weightSum <= 0f ? 1f : w);
            weightSum += w;
        }

        if (weightSum <= 0f)
        {
            // 클립 사이 구간: 투명. (끝에 남은 검정은 CinematicDirector 의 복귀 페이드가 걷는다)
            if (_last != 0f)
            {
                _last = 0f;
                overlay.SetFade(color, 0f);
            }
            return;
        }

        if (Mathf.Abs(alpha - _last) > 0.0005f)
        {
            _last = alpha;
            overlay.SetFade(color, alpha);
        }
    }
}
