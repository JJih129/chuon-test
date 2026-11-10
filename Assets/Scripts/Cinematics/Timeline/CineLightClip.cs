using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[Serializable]
public sealed class CineLightClip : PlayableAsset, ITimelineClipAsset
{
    [Tooltip("기준 강도 배율 (0 = 꺼짐, 1 = 원래)")]
    [Min(0f)] public float intensity = 1f;
    [Range(0f, 1f)] public float flicker;
    [Min(0.1f)] public float flickerHz = 9f;
    public float seed = 0.37f;
    public bool overrideColor;
    public Color color = Color.white;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CineLightBehaviour>.Create(graph);
        CineLightBehaviour b = playable.GetBehaviour();
        b.intensity = intensity;
        b.flicker = flicker;
        b.flickerHz = flickerHz;
        b.seed = seed;
        b.overrideColor = overrideColor;
        b.color = color;
        return playable;
    }
}

[Serializable]
public sealed class CineLightBehaviour : PlayableBehaviour
{
    public float intensity = 1f;
    public float flicker;
    public float flickerHz = 9f;
    public float seed;
    public bool overrideColor;
    public Color color = Color.white;
}
