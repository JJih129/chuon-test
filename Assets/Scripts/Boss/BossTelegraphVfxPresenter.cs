using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossTelegraphVfxPresenter : MonoBehaviour
{
    struct RendererBinding
    {
        public Renderer Renderer;
        public int ColorPropertyId;
        public Color IdleColor;
        public bool IsEmission;
    }

    [Header("Targets")]
    [SerializeField] private Renderer[] weaponRenderers;
    [SerializeField] private Renderer[] bodyRenderers;
    [SerializeField] private bool autoCollectWhenEmpty = true;
    [SerializeField] private bool includeInactiveRenderers = true;
    [SerializeField] private bool ignoreParticleRenderers = true;

    [Header("Material Properties")]
    [SerializeField] private string emissionColorName = "_EmissionColor";
    [SerializeField] private string baseColorName = "_BaseColor";
    [SerializeField] private string legacyColorName = "_Color";
    [SerializeField, Range(0.1f, 1f)] private float baseColorCueBlend = 0.55f;

    [Header("Cue Colors")]
    [SerializeField, ColorUsage(true, true)] private Color normalColor = new Color(0.75f, 0.95f, 1f) * 3.5f;
    [SerializeField, ColorUsage(true, true)] private Color parryableColor = new Color(1f, 0.62f, 0.08f) * 5f;
    [SerializeField, ColorUsage(true, true)] private Color dodgeOnlyColor = new Color(1f, 0.16f, 0.16f) * 5f;
    [SerializeField, ColorUsage(true, true)] private Color unblockableColor = new Color(1f, 0.08f, 0.72f) * 6f;
    [SerializeField, ColorUsage(true, true)] private Color heavyColor = new Color(1f, 0.34f, 0.04f) * 5.5f;
    [SerializeField, ColorUsage(true, true)] private Color rangedColor = new Color(0.25f, 0.82f, 1f) * 4.5f;

    [Header("Pulse")]
    [SerializeField, Min(0.04f)] private float minimumDuration = 0.08f;
    [SerializeField, Range(1, 5)] private int pulseCount = 2;

    private MaterialPropertyBlock _propertyBlock;
    private RendererBinding[] _weaponBindings = Array.Empty<RendererBinding>();
    private RendererBinding[] _bodyBindings = Array.Empty<RendererBinding>();
    private int _emissionColorId;
    private int _baseColorId;
    private int _legacyColorId;
    private Color _cueColor;
    private float _cueStartTime;
    private float _cueDuration;
    private bool _weaponActive;
    private bool _bodyActive;
    private bool _initialized;

    void Awake()
    {
        Initialize();
        enabled = false;
    }

    void OnDisable()
    {
        ResetCue();
    }

    void OnValidate()
    {
        _initialized = false;
        _weaponBindings = Array.Empty<RendererBinding>();
        _bodyBindings = Array.Empty<RendererBinding>();
    }

    void Update()
    {
        float elapsed = Time.time - _cueStartTime;
        if (elapsed >= _cueDuration)
        {
            ResetCue();
            return;
        }

        float normalized = _cueDuration > 0f ? Mathf.Clamp01(elapsed / _cueDuration) : 1f;
        float pulse = Mathf.PingPong(normalized * Mathf.Max(1, pulseCount), 1f);
        float intensity = Mathf.SmoothStep(0.2f, 1f, pulse) * (1f - normalized);
        Color color = _cueColor * intensity;

        if (_weaponActive)
            ApplyColor(_weaponBindings, color);
        if (_bodyActive)
            ApplyColor(_bodyBindings, color);
    }

    public bool PlayCue(BossAttackTelegraphType telegraphType, float duration, BossAttackTimingCueFlags cueFlags)
    {
        bool wantsWeapon = (cueFlags & BossAttackTimingCueFlags.WeaponFlash) != 0;
        bool wantsBody = (cueFlags & BossAttackTimingCueFlags.BodyFlash) != 0;
        if (!wantsWeapon && !wantsBody)
            return false;

        Initialize();
        RestoreActiveCue();

        _weaponActive = wantsWeapon && _weaponBindings.Length > 0;
        _bodyActive = wantsBody && _bodyBindings.Length > 0;
        if (!_weaponActive && !_bodyActive)
            return false;

        _cueColor = ResolveCueColor(telegraphType);
        _cueStartTime = Time.time;
        _cueDuration = Mathf.Max(minimumDuration, duration);
        enabled = true;
        return true;
    }

    public void ResetCue()
    {
        RestoreActiveCue();
        enabled = false;
    }

    void RestoreActiveCue()
    {
        if (_weaponActive)
            RestoreIdle(_weaponBindings);
        if (_bodyActive)
            RestoreIdle(_bodyBindings);

        _weaponActive = false;
        _bodyActive = false;
    }

    void Initialize()
    {
        if (_initialized)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        _emissionColorId = Shader.PropertyToID(emissionColorName);
        _baseColorId = Shader.PropertyToID(baseColorName);
        _legacyColorId = Shader.PropertyToID(legacyColorName);

        if (autoCollectWhenEmpty && IsNullOrEmpty(weaponRenderers) && IsNullOrEmpty(bodyRenderers))
            AutoCollectRenderers();

        if (_weaponBindings.Length == 0)
            _weaponBindings = BuildBindings(weaponRenderers);
        if (_bodyBindings.Length == 0)
            _bodyBindings = BuildBindings(bodyRenderers);

        _initialized = true;
    }

    void AutoCollectRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        int weaponCount = 0;
        int bodyCount = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEligible(renderer))
                continue;

            if (LooksLikeWeapon(renderer))
                weaponCount++;
            else
                bodyCount++;
        }

        weaponRenderers = weaponCount > 0 ? new Renderer[weaponCount] : Array.Empty<Renderer>();
        bodyRenderers = bodyCount > 0 ? new Renderer[bodyCount] : Array.Empty<Renderer>();

        int weaponIndex = 0;
        int bodyIndex = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEligible(renderer))
                continue;

            if (LooksLikeWeapon(renderer))
                weaponRenderers[weaponIndex++] = renderer;
            else
                bodyRenderers[bodyIndex++] = renderer;
        }
    }

    RendererBinding[] BuildBindings(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
            return Array.Empty<RendererBinding>();

        RendererBinding[] temp = new RendererBinding[renderers.Length];
        int count = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEligible(renderer))
                continue;

            if (!TryBuildBinding(renderer, out RendererBinding binding))
                continue;

            bool duplicate = false;
            for (int j = 0; j < count; j++)
            {
                if (temp[j].Renderer == renderer)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                temp[count++] = binding;
        }

        if (count == temp.Length)
            return temp;

        RendererBinding[] result = new RendererBinding[count];
        Array.Copy(temp, result, count);
        return result;
    }

    bool TryBuildBinding(Renderer renderer, out RendererBinding binding)
    {
        binding = default;

        Material material = renderer.sharedMaterial;
        if (material == null)
            return false;

        int propertyId;
        Color idleColor;
        if (material.HasProperty(_emissionColorId))
        {
            propertyId = _emissionColorId;
            idleColor = material.GetColor(_emissionColorId);
            binding.IsEmission = true;
        }
        else if (material.HasProperty(_baseColorId))
        {
            propertyId = _baseColorId;
            idleColor = material.GetColor(_baseColorId);
        }
        else if (material.HasProperty(_legacyColorId))
        {
            propertyId = _legacyColorId;
            idleColor = material.GetColor(_legacyColorId);
        }
        else
        {
            return false;
        }

        binding.Renderer = renderer;
        binding.ColorPropertyId = propertyId;
        binding.IdleColor = idleColor;
        return true;
    }

    bool IsEligible(Renderer renderer)
    {
        if (renderer == null)
            return false;

        if (ignoreParticleRenderers && renderer is ParticleSystemRenderer)
            return false;

        return renderer.sharedMaterial != null;
    }

    bool LooksLikeWeapon(Renderer renderer)
    {
        string name = renderer != null ? renderer.name : string.Empty;
        return ContainsIgnoreCase(name, "weapon") ||
               ContainsIgnoreCase(name, "sword") ||
               ContainsIgnoreCase(name, "katana") ||
               ContainsIgnoreCase(name, "blade") ||
               ContainsIgnoreCase(name, "object012") ||
               ContainsIgnoreCase(name, "object002");
    }

    static bool ContainsIgnoreCase(string source, string value)
    {
        return !string.IsNullOrEmpty(source) &&
               source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsNullOrEmpty(Renderer[] renderers)
    {
        return renderers == null || renderers.Length == 0;
    }

    Color ResolveCueColor(BossAttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return parryableColor;
            case BossAttackTelegraphType.DodgeOnly:
                return dodgeOnlyColor;
            case BossAttackTelegraphType.Unblockable:
                return unblockableColor;
            case BossAttackTelegraphType.Heavy:
                return heavyColor;
            case BossAttackTelegraphType.Ranged:
                return rangedColor;
            case BossAttackTelegraphType.Normal:
                return normalColor;
            default:
                return normalColor;
        }
    }

    void ApplyColor(RendererBinding[] bindings, Color color)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            Renderer renderer = bindings[i].Renderer;
            if (renderer == null)
                continue;

            _propertyBlock.Clear();
            renderer.GetPropertyBlock(_propertyBlock);
            Color resolvedColor = bindings[i].IsEmission
                ? color
                : Color.Lerp(bindings[i].IdleColor, color, baseColorCueBlend);
            _propertyBlock.SetColor(bindings[i].ColorPropertyId, resolvedColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    void RestoreIdle(RendererBinding[] bindings)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            Renderer renderer = bindings[i].Renderer;
            if (renderer == null)
                continue;

            _propertyBlock.Clear();
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(bindings[i].ColorPropertyId, bindings[i].IdleColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
