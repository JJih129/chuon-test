using System;
using TMPro;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[Serializable]
public sealed class CineTextClip : PlayableAsset, ITimelineClipAsset
{
    [TextArea(1, 3)] public string text = "B1";
    public bool overrideColor;
    public Color color = new Color(1f, 0.16f, 0.12f, 1f);
    [Tooltip("0 보다 크면 이 주기(Hz)로 글자를 깜빡인다 (경고 문구).")]
    [Min(0f)] public float blinkHz;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CineTextBehaviour>.Create(graph);
        CineTextBehaviour b = playable.GetBehaviour();
        b.text = text;
        b.overrideColor = overrideColor;
        b.color = color;
        b.blinkHz = blinkHz;
        return playable;
    }
}

[Serializable]
public sealed class CineTextBehaviour : PlayableBehaviour
{
    public string text;
    public bool overrideColor;
    public Color color;
    public float blinkHz;

    TMP_Text _target;
    bool _applied;
    bool _visible = true;

    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        _applied = false;
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var target = playerData as TMP_Text;
        if (target == null)
            return;
        if (!_applied || target != _target)
        {
            _target = target;
            _applied = true;
            target.text = text;
            if (overrideColor)
                target.color = color;
            _visible = true;
            target.enabled = true;
        }

        if (blinkHz > 0f)
        {
            bool on = Mathf.Repeat((float)playable.GetTime() * blinkHz, 1f) < 0.62f;
            if (on != _visible)
            {
                _visible = on;
                target.enabled = on;
            }
        }
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (_target != null && !_visible)
            _target.enabled = true;
        _visible = true;
    }
}
