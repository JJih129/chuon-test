using UnityEngine;

[DisallowMultipleComponent]
public sealed class RuntimeVfxTintCache : MonoBehaviour
{
    static readonly int[] ColorPropertyIds =
    {
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_EmissionColor"),
        Shader.PropertyToID("_MainColor"),
        Shader.PropertyToID("_InnerColor"),
        Shader.PropertyToID("_OuterColor"),
        Shader.PropertyToID("_CoreColor"),
        Shader.PropertyToID("_RimColor"),
        Shader.PropertyToID("_AddColor"),
        Shader.PropertyToID("_GlowColor"),
        Shader.PropertyToID("_EdgeColor"),
        Shader.PropertyToID("_EmisColor")
    };

    ParticleSystem[] _particles;
    ParticleSystem.MinMaxGradient[] _particleStartColors;
    bool[] _particleColorOverLifetimeEnabled;
    ParticleSystem.MinMaxGradient[] _particleColorOverLifetimeColors;
    bool[] _particleColorBySpeedEnabled;
    ParticleSystem.MinMaxGradient[] _particleColorBySpeedColors;
    bool[] _particleTrailsEnabled;
    ParticleSystem.MinMaxGradient[] _particleTrailLifetimeColors;
    ParticleSystem.MinMaxGradient[] _particleTrailOverTrailColors;
    TrailRenderer[] _trails;
    Gradient[] _trailColorGradients;
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

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particle.colorOverLifetime;
            colorOverLifetime.enabled = _particleColorOverLifetimeEnabled[i];
            if (_particleColorOverLifetimeEnabled[i])
                colorOverLifetime.color = tinted ? new ParticleSystem.MinMaxGradient(BuildRedGradient(tint)) : _particleColorOverLifetimeColors[i];

            ParticleSystem.ColorBySpeedModule colorBySpeed = particle.colorBySpeed;
            if (_particleColorBySpeedEnabled[i])
            {
                colorBySpeed.enabled = true;
                colorBySpeed.color = tinted ? new ParticleSystem.MinMaxGradient(BuildRedGradient(tint)) : _particleColorBySpeedColors[i];
            }

            ParticleSystem.TrailModule particleTrails = particle.trails;
            if (_particleTrailsEnabled[i])
            {
                particleTrails.enabled = true;
                particleTrails.colorOverLifetime = tinted ? new ParticleSystem.MinMaxGradient(BuildRedTrailGradient(tint)) : _particleTrailLifetimeColors[i];
                particleTrails.colorOverTrail = tinted ? new ParticleSystem.MinMaxGradient(BuildRedTrailGradient(tint)) : _particleTrailOverTrailColors[i];
            }
        }

        for (int i = 0; i < _trails.Length; i++)
        {
            TrailRenderer trail = _trails[i];
            if (trail == null)
                continue;

            trail.colorGradient = tinted ? BuildRedTrailGradient(tint) : _trailColorGradients[i];
            if (tinted)
            {
                trail.startColor = tint;
                trail.endColor = new Color(tint.r, tint.g * 0.35f, tint.b * 0.25f, 0f);
            }
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
        _particleColorOverLifetimeEnabled = new bool[_particles.Length];
        _particleColorOverLifetimeColors = new ParticleSystem.MinMaxGradient[_particles.Length];
        _particleColorBySpeedEnabled = new bool[_particles.Length];
        _particleColorBySpeedColors = new ParticleSystem.MinMaxGradient[_particles.Length];
        _particleTrailsEnabled = new bool[_particles.Length];
        _particleTrailLifetimeColors = new ParticleSystem.MinMaxGradient[_particles.Length];
        _particleTrailOverTrailColors = new ParticleSystem.MinMaxGradient[_particles.Length];
        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem particle = _particles[i];
            _particleStartColors[i] = particle.main.startColor;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particle.colorOverLifetime;
            _particleColorOverLifetimeEnabled[i] = colorOverLifetime.enabled;
            _particleColorOverLifetimeColors[i] = colorOverLifetime.color;

            ParticleSystem.ColorBySpeedModule colorBySpeed = particle.colorBySpeed;
            _particleColorBySpeedEnabled[i] = colorBySpeed.enabled;
            _particleColorBySpeedColors[i] = colorBySpeed.color;

            ParticleSystem.TrailModule particleTrails = particle.trails;
            _particleTrailsEnabled[i] = particleTrails.enabled;
            _particleTrailLifetimeColors[i] = particleTrails.colorOverLifetime;
            _particleTrailOverTrailColors[i] = particleTrails.colorOverTrail;
        }

        _trails = GetComponentsInChildren<TrailRenderer>(true);
        _trailColorGradients = new Gradient[_trails.Length];
        for (int i = 0; i < _trails.Length; i++)
            _trailColorGradients[i] = _trails[i] != null ? _trails[i].colorGradient : null;

        _renderers = GetComponentsInChildren<Renderer>(true);
        _cached = true;
    }

    static Gradient BuildRedGradient(Color primary)
    {
        Color highlight = new Color(1f, 0.78f, 0.68f, primary.a);
        Color accent = new Color(1f, 0.24f, 0.08f, primary.a);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(highlight, 0f),
                new GradientColorKey(primary, 0.38f),
                new GradientColorKey(accent, 1f)
            },
            new[]
            {
                new GradientAlphaKey(primary.a, 0f),
                new GradientAlphaKey(primary.a, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    static Gradient BuildRedTrailGradient(Color primary)
    {
        Color core = new Color(1f, 0.82f, 0.72f, primary.a);
        Color edge = new Color(0.95f, 0.05f, 0.02f, primary.a);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(core, 0f),
                new GradientColorKey(primary, 0.28f),
                new GradientColorKey(edge, 1f)
            },
            new[]
            {
                new GradientAlphaKey(primary.a, 0f),
                new GradientAlphaKey(primary.a * 0.9f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }
}
