using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>클립 구간 동안 fromAlpha → toAlpha (스무스스텝). 검정 컷인/아웃, 흰 섬광(플래시 옵션 배율 적용).</summary>
[Serializable]
public sealed class CineFadeClip : PlayableAsset, ITimelineClipAsset
{
    public Color color = Color.black;
    [Range(0f, 1f)] public float fromAlpha = 1f;
    [Range(0f, 1f)] public float toAlpha = 0f;
    [Tooltip("흰 섬광 등 번쩍임: 접근성 '화면 번쩍임 강도' 배율을 곱한다.")]
    public bool isFlash;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CineFadeBehaviour>.Create(graph);
        CineFadeBehaviour b = playable.GetBehaviour();
        b.color = color;
        float scale = isFlash && Application.isPlaying ? AccessibilityOptionsRuntime.FlashIntensity : 1f;
        b.fromAlpha = fromAlpha * scale;
        b.toAlpha = toAlpha * scale;
        return playable;
    }
}

[Serializable]
public sealed class CineFadeBehaviour : PlayableBehaviour
{
    public Color color = Color.black;
    public float fromAlpha;
    public float toAlpha;
}
