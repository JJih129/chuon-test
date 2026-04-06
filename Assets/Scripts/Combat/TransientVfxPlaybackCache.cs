using UnityEngine;

[DisallowMultipleComponent]
public sealed class TransientVfxPlaybackCache : MonoBehaviour
{
    Animator[] _animators;
    ParticleSystem[] _particleSystems;
    Renderer _primaryRenderer;
    bool _captured;

    public Renderer PrimaryRenderer
    {
        get
        {
            EnsureCaptured();
            return _primaryRenderer;
        }
    }

    public void ApplyUnscaledTime()
    {
        EnsureCaptured();

        for (int i = 0; i < _animators.Length; i++)
            _animators[i].updateMode = AnimatorUpdateMode.UnscaledTime;

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            var main = _particleSystems[i].main;
            main.useUnscaledTime = true;
        }
    }

    void EnsureCaptured()
    {
        if (_captured)
            return;

        _animators = GetComponentsInChildren<Animator>(true);
        _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        _primaryRenderer = GetComponent<Renderer>();

        if (_primaryRenderer == null)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
                _primaryRenderer = renderers[0];
        }

        _captured = true;
    }
}
