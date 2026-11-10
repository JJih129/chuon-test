using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>레터박스 높이 (0 = 없음, 1 = 기본 2.35:1, 4.5 ≈ 화면 전체).</summary>
[Serializable]
public sealed class CineLetterboxClip : PlayableAsset, ITimelineClipAsset
{
    [Range(0f, 4.6f)] public float amount = 1f;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CineLetterboxBehaviour>.Create(graph);
        playable.GetBehaviour().amount = amount;
        return playable;
    }
}

[Serializable]
public sealed class CineLetterboxBehaviour : PlayableBehaviour
{
    public float amount = 1f;
}
