using UnityEngine;

[DisallowMultipleComponent]
public sealed class RuntimeVfxTintCache : MonoBehaviour
{
    static readonly int[] ColorPropertyIds =
    {
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_EmissionColor")
    };

    ParticleSystem[] _particles;
    ParticleSystem.MinMaxGradient[] _particleStartColors;
    Renderer[] _renderers;
    MaterialPropertyBlock _propertyBlock;
    bool _cached;

    void Awake() => Cache();

    public void Apply(bool tinted, Color tint)
    {
        Cache();

        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem particle = _particles[i];
            if (particle == null)
                continue;

            ParticleSystem.MainModule main = particle.main;
            main.startColor = tinted ? new ParticleSystem.MinMaxGradient(tint) : _particleStartColors[i];
        }

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
                continue;

            if (!tinted)
            {
                renderer.SetPropertyBlock(null);
                continue;
            }

            _propertyBlock.Clear();
            for (int p = 0; p < ColorPropertyIds.Length; p++)
                _propertyBlock.SetColor(ColorPropertyIds[p], tint);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    void Cache()
    {
        if (_cached)
            return;

        _particles = GetComponentsInChildren<ParticleSystem>(true);
        _particleStartColors = new ParticleSystem.MinMaxGradient[_particles.Length];
        for (int i = 0; i < _particles.Length; i++)
            _particleStartColors[i] = _particles[i].main.startColor;

        _renderers = GetComponentsInChildren<Renderer>(true);
        _cached = true;
    }
}
