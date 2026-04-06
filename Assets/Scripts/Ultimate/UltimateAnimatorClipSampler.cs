using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class UltimateAnimatorClipSampler : IDisposable
{
    readonly string _graphName;

    Animator _animator;
    AnimationClip _clip;
    PlayableGraph _graph;
    AnimationPlayableOutput _output;
    AnimationClipPlayable _clipPlayable;
    bool _graphCreated;
    double _lastSampleTime = double.NaN;

    public UltimateAnimatorClipSampler(string graphName)
    {
        _graphName = string.IsNullOrWhiteSpace(graphName) ? "UltimateClipSampler" : graphName;
    }

    public bool Begin(Animator animator, AnimationClip clip)
    {
        if (animator == null || clip == null)
            return false;

        if (_animator == animator && _clip == clip && _graphCreated)
            return true;

        StopPlayback();

        _graph = PlayableGraph.Create(_graphName);
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        _clipPlayable.SetApplyFootIK(false);
        _clipPlayable.SetApplyPlayableIK(false);
        _clipPlayable.SetSpeed(0d);

        _output = AnimationPlayableOutput.Create(_graph, $"{_graphName}_Output", animator);
        _output.SetSourcePlayable(_clipPlayable);
        _graph.Play();

        _animator = animator;
        _clip = clip;
        _graphCreated = true;
        _lastSampleTime = double.NaN;
        return true;
    }

    public void SampleNormalized(float normalizedTime)
    {
        if (!_graphCreated || !_clipPlayable.IsValid() || _clip == null)
            return;

        double targetTime = ResolveSampleTime(normalizedTime, 0f, 1f);
        if (double.IsNaN(targetTime))
            return;

        if (!double.IsNaN(_lastSampleTime) && Math.Abs(_lastSampleTime - targetTime) <= 0.0001d)
            return;

        _clipPlayable.SetTime(targetTime);
        _graph.Evaluate(0f);
        _lastSampleTime = targetTime;
    }

    public void SampleNormalizedRange(float normalizedTime, float clipStartNormalized, float clipEndNormalized)
    {
        if (!_graphCreated || !_clipPlayable.IsValid() || _clip == null)
            return;

        double targetTime = ResolveSampleTime(normalizedTime, clipStartNormalized, clipEndNormalized);
        if (double.IsNaN(targetTime))
            return;

        if (!double.IsNaN(_lastSampleTime) && Math.Abs(_lastSampleTime - targetTime) <= 0.0001d)
            return;

        _clipPlayable.SetTime(targetTime);
        _graph.Evaluate(0f);
        _lastSampleTime = targetTime;
    }

    double ResolveSampleTime(float normalizedTime, float clipStartNormalized, float clipEndNormalized)
    {
        if (_clip == null)
            return double.NaN;

        double duration = Math.Max(0.0001d, _clip.length);
        double rangeStart = Math.Max(0d, Math.Min(1d, clipStartNormalized));
        double rangeEnd = Math.Max(0d, Math.Min(1d, clipEndNormalized));
        if (rangeEnd < rangeStart)
        {
            double swap = rangeStart;
            rangeStart = rangeEnd;
            rangeEnd = swap;
        }

        double clampedNormalized = Math.Max(0d, Math.Min(1d, normalizedTime));
        double rangedNormalized = rangeStart + ((rangeEnd - rangeStart) * clampedNormalized);
        double targetTime = rangedNormalized * duration;
        if (targetTime >= duration)
            targetTime = Math.Max(0d, duration - 0.0001d);

        return targetTime;
    }

    public void StopPlayback()
    {
        if (_graphCreated && _graph.IsValid())
            _graph.Destroy();

        _graphCreated = false;
        _lastSampleTime = double.NaN;
        _animator = null;
        _clip = null;
        _clipPlayable = default;
        _output = default;
        _graph = default;
    }

    public void Dispose()
    {
        StopPlayback();
    }
}
