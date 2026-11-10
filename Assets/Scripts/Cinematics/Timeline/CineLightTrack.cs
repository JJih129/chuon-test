using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 조명 강도·점멸 트랙 (엘리베이터 조명 끊김, 경보 회전등, 홀로그램 투사광). 바인딩: Light.
/// 그래프 시작 때의 강도를 기준(1.0)으로 배율을 적용하고, 그래프가 끝나면 원래 값으로 되돌린다.
/// 점멸은 결정적 노이즈(Perlin)라 매 재생이 같다.
/// </summary>
[TrackColor(1f, 0.85f, 0.6f)]
[TrackBindingType(typeof(Light))]
[TrackClipType(typeof(CineLightClip))]
public sealed class CineLightTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        return ScriptPlayable<CineLightMixer>.Create(graph, inputCount);
    }
}

public sealed class CineLightMixer : PlayableBehaviour
{
    Light _light;
    float _baseIntensity;
    Color _baseColor;
    bool _captured;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var light = playerData as Light;
        if (light == null)
            return;
        if (!_captured || light != _light)
        {
            _light = light;
            _baseIntensity = light.intensity;
            _baseColor = light.color;
            _captured = true;
        }

        int count = playable.GetInputCount();
        float mult = 0f;
        float weight = 0f;
        Color color = Color.black;
        for (int i = 0; i < count; i++)
        {
            float w = playable.GetInputWeight(i);
            if (w <= 0f)
                continue;
            var input = (ScriptPlayable<CineLightBehaviour>)playable.GetInput(i);
            CineLightBehaviour b = input.GetBehaviour();
            float t = (float)input.GetTime();
            float m = b.intensity;
            if (b.flicker > 0f)
            {
                float n = Mathf.PerlinNoise(t * b.flickerHz, b.seed);
                // 0.45 아래로 떨어지면 끊김(거의 꺼짐), 위면 미세 흔들림
                float drop = n < 0.45f ? 1f : (1f - n) * 0.35f;
                m *= 1f - b.flicker * drop;
            }
            mult += w * m;
            color += w * (b.overrideColor ? b.color : _baseColor);
            weight += w;
        }

        if (weight <= 0f)
        {
            light.intensity = _baseIntensity;
            light.color = _baseColor;
            return;
        }

        float inv = 1f - Mathf.Clamp01(weight);
        light.intensity = _baseIntensity * (mult + inv);
        light.color = color + _baseColor * inv;
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        if (_captured && _light != null)
        {
            _light.intensity = _baseIntensity;
            _light.color = _baseColor;
        }
        _captured = false;
    }
}
