using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 레터박스 트랙 (선택).
/// - CinematicDirector 재생 중: 클립이 있는 구간만 자동 램프를 덮어쓴다 (예: 카드 구간 전체 검정 바).
/// - 다른 컨트롤러가 재생하는 타임라인(예: Type-D 리빌 v2 = MainSceneArrivalController): 이 트랙이 레터박스를 직접 그리고, 끝나면 0 으로 되돌린다.
/// 옵션에서 레터박스를 끄면 아무것도 그리지 않는다.
/// </summary>
[TrackColor(0.02f, 0.02f, 0.02f)]
[TrackClipType(typeof(CineLetterboxClip))]
public sealed class CineLetterboxTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        return ScriptPlayable<CineLetterboxMixer>.Create(graph, inputCount);
    }
}

public sealed class CineLetterboxMixer : PlayableBehaviour
{
    bool _ownsOverlay;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!Application.isPlaying || !CinematicSettings.LetterboxEnabled || !CinePlayables.IsDirectorGraph(playable))
            return;
        bool standalone = !CinematicDirector.IsPlaying;
        CinematicOverlay overlay = standalone ? CinematicOverlay.Ensure() : CinematicOverlay.Active;
        if (overlay == null)
            return;

        int count = playable.GetInputCount();
        float value = 0f;
        float weight = 0f;
        for (int i = 0; i < count; i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f)
                continue;
            var input = (ScriptPlayable<CineLetterboxBehaviour>)playable.GetInput(i);
            value += w * input.GetBehaviour().amount;
            weight += w;
        }

        if (standalone)
        {
            _ownsOverlay = true;
            overlay.SetLetterbox(weight > 0f ? value / weight * Mathf.Clamp01(weight) : 0f);
            return;
        }

        if (weight > 0f)
            overlay.SetLetterbox(Mathf.Lerp(overlay.Letterbox, value / weight, Mathf.Clamp01(weight)));
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (_ownsOverlay && !CinematicDirector.IsPlaying && CinematicOverlay.Active != null)
            CinematicOverlay.Active.SetLetterbox(0f);
        _ownsOverlay = false;
    }
}
