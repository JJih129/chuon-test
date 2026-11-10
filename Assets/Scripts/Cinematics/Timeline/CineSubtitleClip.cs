using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 자막 한 줄(Line) 또는 화면 중앙 타이틀 카드(Card). 키가 있으면 대사 테이블 문구를 쓰고, 없으면 저장된 폴백.
/// Line: 클립 시작에 한 번 표시, 유지 시간 = 클립 길이 − 0.25 s(페이드 아웃). Card: 0.4 s 페이드 인/아웃.
/// </summary>
[Serializable]
public sealed class CineSubtitleClip : PlayableAsset, ITimelineClipAsset
{
    public enum Mode : byte { Line = 0, Card = 1 }

    public Mode mode = Mode.Line;
    [Tooltip("대사 테이블 키 (예: cine.open.3). 비우면 아래 문구 그대로.")]
    public string key;
    public string speaker = "EGO";
    [TextArea(1, 3)] public string text;
    [Tooltip("Card 모드 보조 문구 (작은 글씨)")]
    public string cardSub;
    [Tooltip("Card 배치: Center = 검정 위 장소 카드, LowerThird = 인물 클로즈업 위 하단 1/3 (얼굴을 가리지 않게, 반복 16)")]
    public CinematicOverlay.CardPlacement cardPlacement = CinematicOverlay.CardPlacement.Center;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CineSubtitleBehaviour>.Create(graph);
        CineSubtitleBehaviour b = playable.GetBehaviour();
        b.mode = mode;
        b.key = key;
        b.speaker = speaker;
        b.text = text;
        b.cardSub = cardSub;
        b.cardPlacement = cardPlacement;
        return playable;
    }
}

[Serializable]
public sealed class CineSubtitleBehaviour : PlayableBehaviour
{
    public CineSubtitleClip.Mode mode;
    public string key;
    public string speaker;
    public string text;
    public string cardSub;
    public CinematicOverlay.CardPlacement cardPlacement;

    bool _shown;
    float _lastCard = -1f;

    public override void OnGraphStart(Playable playable)
    {
        _shown = false;
        _lastCard = -1f;
    }

    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        if (!Application.isPlaying || _shown || !CinePlayables.IsDirectorGraph(playable))
            return;
        CinematicOverlay overlay = CinematicOverlay.Active;
        if (overlay == null)
            return;
        _shown = true;

        string s = speaker;
        string t = text;
        CineDialogue.Resolve(key, ref s, ref t);
        if (mode == CineSubtitleClip.Mode.Card)
        {
            overlay.ShowCard(t, cardSub, cardPlacement);
            return;
        }

        float hold = Mathf.Max(0.6f, (float)playable.GetDuration() - 0.25f);
        overlay.ShowLine(s, t, hold);
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (mode != CineSubtitleClip.Mode.Card || !Application.isPlaying || !CinePlayables.IsDirectorGraph(playable))
            return;
        CinematicOverlay overlay = CinematicOverlay.Active;
        if (overlay == null)
            return;
        float time = (float)playable.GetTime();
        float dur = (float)playable.GetDuration();
        float a = Mathf.Min(Mathf.Clamp01(time / 0.4f), Mathf.Clamp01((dur - time) / 0.4f));
        if (Mathf.Abs(a - _lastCard) > 0.002f)
        {
            _lastCard = a;
            overlay.SetCard(a);
        }
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (mode != CineSubtitleClip.Mode.Card || !Application.isPlaying)
            return;
        // 클립 끝(또는 일시정지): 카드 숨김. 메뉴 일시정지에서 재개되면 ProcessFrame 이 다시 올린다.
        CinematicOverlay overlay = CinematicOverlay.Active;
        if (overlay != null && info.effectivePlayState == PlayState.Paused)
        {
            overlay.SetCard(0f);
            _lastCard = 0f;
        }
    }
}
