using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PatternVisuals : MonoBehaviour
{
    struct RendererBinding
    {
        public Renderer Renderer;
        public int ColorPropertyId;
        public Color IdleColor;
        public bool UsesOriginalBaseColor;
    }

    [Header("Renderer")]
    [Tooltip("대표 렌더러. 비우면 자식에서 자동 탐색합니다.")]
    public Renderer visualPartRenderer;
    [Tooltip("직접 지정할 추가 렌더러들. 비우면 자동 수집만 사용합니다.")]
    public Renderer[] additionalRenderers;
    [SerializeField] bool autoCollectChildRenderers = true;
    [SerializeField] bool includeInactiveRenderers = true;
    [SerializeField] bool ignorePlaneNamedRenderers = true;
    [SerializeField] bool ignoreParticleRenderers = true;

    [Header("Material Property")]
    [Tooltip("우선 적용할 색상 프로퍼티 이름")]
    public string emissionColorName = "_EmissionColor";
    [Tooltip("Emission이 없을 때 사용할 기본 색상 프로퍼티 이름")]
    public string baseColorName = "_BaseColor";
    [Tooltip("구형 셰이더용 기본 색상 프로퍼티 이름")]
    public string legacyColorName = "_Color";
    [Range(0.1f, 1f)] public float baseColorCueBlend = 0.55f;

    [Header("Telegraph Colors")]
    [ColorUsage(true, true)] public Color parryColor = new Color(1.00f, 0.66f, 0.08f) * 5f;
    [ColorUsage(true, true)] public Color guardColor = new Color(0.22f, 0.78f, 1.00f) * 4.5f;
    [ColorUsage(true, true)] public Color dodgeColor = new Color(1.00f, 0.18f, 0.18f) * 5f;
    [ColorUsage(true, true)] public Color dangerColor = new Color(1.00f, 0.12f, 0.78f) * 6f;
    [ColorUsage(true, true)] public Color punishColor = new Color(0.36f, 1.00f, 0.78f) * 4.5f;
    [ColorUsage(true, true)] public Color idleColor = Color.black;

    [Header("Timing")]
    [Min(0.06f)] public float flashDuration = 0.45f;
    [Range(1, 4)] public int pulseCount = 2;
    [Range(0.1f, 1f)] public float punishHoldIntensity = 0.42f;

    MaterialPropertyBlock _propertyBlock;
    RendererBinding[] _rendererBindings = Array.Empty<RendererBinding>();
    int _emissionColorId;
    int _baseColorId;
    int _legacyColorId;
    Color _lastCueColor = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
    bool _telegraphCueActive;
    AttackTelegraphType _telegraphCueType;
    float _telegraphCueStartTime;
    float _telegraphCueDuration;
    bool _punishCueActive;
    float _punishCueStartTime;
    float _punishCueDuration;

    void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        _emissionColorId = Shader.PropertyToID(emissionColorName);
        _baseColorId = Shader.PropertyToID(baseColorName);
        _legacyColorId = Shader.PropertyToID(legacyColorName);

        ResolveRendererBindings();
        if (_rendererBindings.Length == 0)
        {
            Debug.LogError("[PatternVisuals] Target renderer is missing.", this);
            enabled = false;
            return;
        }

        SetCueColor(idleColor);
        enabled = false;
    }

    void Update()
    {
        UpdateCue();
    }

    void OnDisable()
    {
        StopCue();
        SetCueColor(idleColor);
    }

    public void SetParryable(bool isParryable)
    {
        StartVisualCue(isParryable ? AttackTelegraphType.Parry : AttackTelegraphType.Dodge, flashDuration);
    }

    public void StartVisualCue(bool isParryable)
    {
        StartVisualCue(isParryable ? AttackTelegraphType.Parry : AttackTelegraphType.Dodge, flashDuration);
    }

    public void StartVisualCue(AttackTelegraphType telegraphType, float duration)
    {
        if (!gameObject.activeInHierarchy)
            return;

        EnsureRendererBindings();
        if (_rendererBindings.Length == 0)
            return;

        enabled = true;
        StopCue();
        _telegraphCueActive = true;
        _telegraphCueType = telegraphType;
        _telegraphCueStartTime = Time.time;
        _telegraphCueDuration = Mathf.Max(0.06f, duration);
        SetCueColor(idleColor);
    }

    public void ResetToIdle()
    {
        StopCue();
        SetCueColor(idleColor);
        enabled = false;
    }

    public void StartPunishCue(float duration)
    {
        if (!gameObject.activeInHierarchy)
            return;

        EnsureRendererBindings();
        if (_rendererBindings.Length == 0)
            return;

        enabled = true;
        StopCue();
        _punishCueActive = true;
        _punishCueStartTime = Time.unscaledTime;
        _punishCueDuration = Mathf.Max(0.08f, duration);
        SetCueColor(idleColor);
    }

    void EnsureRendererBindings()
    {
        for (int i = 0; i < _rendererBindings.Length; i++)
        {
            if (_rendererBindings[i].Renderer != null)
                return;
        }

        ResolveRendererBindings();
    }

    void ResolveRendererBindings()
    {
        if (visualPartRenderer == null)
            visualPartRenderer = FindFirstEligibleRenderer();

        List<RendererBinding> bindings = new List<RendererBinding>(8);
        HashSet<Renderer> seen = new HashSet<Renderer>();

        TryRegisterRenderer(visualPartRenderer, bindings, seen);

        if (additionalRenderers != null)
        {
            for (int i = 0; i < additionalRenderers.Length; i++)
                TryRegisterRenderer(additionalRenderers[i], bindings, seen);
        }

        if (autoCollectChildRenderers)
        {
            Renderer[] childRenderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
            for (int i = 0; i < childRenderers.Length; i++)
                TryRegisterRenderer(childRenderers[i], bindings, seen);
        }

        _rendererBindings = bindings.ToArray();
        if (visualPartRenderer == null && _rendererBindings.Length > 0)
            visualPartRenderer = _rendererBindings[0].Renderer;
    }

    Renderer FindFirstEligibleRenderer()
    {
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        for (int i = 0; i < childRenderers.Length; i++)
        {
            if (IsEligibleRenderer(childRenderers[i]))
                return childRenderers[i];
        }

        return null;
    }

    void TryRegisterRenderer(Renderer candidate, List<RendererBinding> bindings, HashSet<Renderer> seen)
    {
        if (!IsEligibleRenderer(candidate) || !seen.Add(candidate))
            return;

        if (!TryResolveColorBinding(candidate, out RendererBinding binding))
            return;

        bindings.Add(binding);
    }

    bool IsEligibleRenderer(Renderer candidate)
    {
        if (candidate == null)
            return false;

        if (ignoreParticleRenderers && candidate is ParticleSystemRenderer)
            return false;

        string candidateName = candidate.name ?? string.Empty;
        if (ignorePlaneNamedRenderers && candidateName.StartsWith("Plane", StringComparison.OrdinalIgnoreCase))
            return false;

        if (candidateName.IndexOf("Particle View", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return true;
    }

    bool TryResolveColorBinding(Renderer candidate, out RendererBinding binding)
    {
        binding = default;
        if (candidate == null)
            return false;

        Material[] materials = candidate.sharedMaterials;
        Color resolvedBaseColor = Color.white;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
                continue;

            if (material.HasProperty(_emissionColorId))
            {
                binding = new RendererBinding
                {
                    Renderer = candidate,
                    ColorPropertyId = _emissionColorId,
                    IdleColor = idleColor,
                    UsesOriginalBaseColor = false
                };
                return true;
            }

            if (material.HasProperty(_baseColorId))
            {
                resolvedBaseColor = material.GetColor(_baseColorId);
                binding = new RendererBinding
                {
                    Renderer = candidate,
                    ColorPropertyId = _baseColorId,
                    IdleColor = resolvedBaseColor,
                    UsesOriginalBaseColor = true
                };
                return true;
            }

            if (material.HasProperty(_legacyColorId))
            {
                resolvedBaseColor = material.GetColor(_legacyColorId);
                binding = new RendererBinding
                {
                    Renderer = candidate,
                    ColorPropertyId = _legacyColorId,
                    IdleColor = resolvedBaseColor,
                    UsesOriginalBaseColor = true
                };
                return true;
            }
        }

        return false;
    }

    void StopCue()
    {
        _telegraphCueActive = false;
        _punishCueActive = false;
    }

    Color ResolveColor(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return parryColor;
            case AttackTelegraphType.Guard:
                return guardColor;
            case AttackTelegraphType.Danger:
                return dangerColor;
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                return dodgeColor;
        }
    }

    void SetCueColor(Color color)
    {
        if (_rendererBindings.Length == 0 || _lastCueColor.Equals(color))
            return;

        for (int i = 0; i < _rendererBindings.Length; i++)
        {
            RendererBinding binding = _rendererBindings[i];
            if (binding.Renderer == null || binding.ColorPropertyId == 0)
                continue;

            _propertyBlock.Clear();
            Color resolvedColor = binding.UsesOriginalBaseColor
                ? ResolveBaseColorCue(binding.IdleColor, color)
                : color;
            _propertyBlock.SetColor(binding.ColorPropertyId, resolvedColor);
            binding.Renderer.SetPropertyBlock(_propertyBlock);
        }

        _lastCueColor = color;
    }

    void UpdateCue()
    {
        if (_telegraphCueActive)
        {
            UpdateTelegraphCue();
            return;
        }

        if (_punishCueActive)
        {
            UpdatePunishCue();
            return;
        }

        enabled = false;
    }

    void UpdatePunishCue()
    {
        float duration = Mathf.Max(0.08f, _punishCueDuration);
        float pulseIn = Mathf.Min(0.10f, duration * 0.22f);
        float pulseOut = Mathf.Min(0.12f, duration * 0.24f);
        float settle = 0.08f;
        float hold = Mathf.Max(0f, duration - pulseIn - pulseOut - settle);
        Color holdColor = Color.Lerp(idleColor, punishColor, Mathf.Clamp01(punishHoldIntensity));
        float elapsed = Time.unscaledTime - _punishCueStartTime;

        if (elapsed >= duration)
        {
            _punishCueActive = false;
            SetCueColor(idleColor);
            enabled = false;
            return;
        }

        if (elapsed <= pulseIn)
        {
            SetCueColor(Color.Lerp(idleColor, punishColor, Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, pulseIn))));
            return;
        }

        elapsed -= pulseIn;
        if (elapsed <= pulseOut)
        {
            SetCueColor(Color.Lerp(punishColor, holdColor, Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, pulseOut))));
            return;
        }

        elapsed -= pulseOut;
        if (elapsed <= hold)
        {
            SetCueColor(holdColor);
            return;
        }

        elapsed -= hold;
        SetCueColor(Color.Lerp(holdColor, idleColor, Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, settle))));
    }

    void UpdateTelegraphCue()
    {
        float duration = Mathf.Max(0.06f, _telegraphCueDuration);
        float elapsed = Time.time - _telegraphCueStartTime;
        if (elapsed >= duration)
        {
            _telegraphCueActive = false;
            SetCueColor(idleColor);
            enabled = false;
            return;
        }

        Color targetColor = ResolveColor(_telegraphCueType);
        int totalPulses = Mathf.Max(1, _telegraphCueType == AttackTelegraphType.Danger ? pulseCount + 1 : pulseCount);
        float pulseDuration = Mathf.Max(0.0001f, duration / totalPulses);
        float pulseElapsed = Mathf.Repeat(elapsed, pulseDuration);
        float pulseT = pulseElapsed / pulseDuration;
        float fadeInPortion = _telegraphCueType == AttackTelegraphType.Danger ? 0.22f : 0.32f;

        if (pulseT <= fadeInPortion)
        {
            float t = pulseT / Mathf.Max(0.0001f, fadeInPortion);
            SetCueColor(Color.Lerp(idleColor, targetColor, t));
            return;
        }

        float fadeOutT = (pulseT - fadeInPortion) / Mathf.Max(0.0001f, 1f - fadeInPortion);
        SetCueColor(Color.Lerp(targetColor, idleColor, fadeOutT));
    }

    Color ResolveBaseColorCue(Color baseColor, Color cueColor)
    {
        if (cueColor.Equals(idleColor))
            return baseColor;

        Color clampedCue = new Color(
            Mathf.Clamp01(cueColor.r),
            Mathf.Clamp01(cueColor.g),
            Mathf.Clamp01(cueColor.b),
            Mathf.Max(baseColor.a, cueColor.a));
        return Color.Lerp(baseColor, clampedCue, Mathf.Clamp01(baseColorCueBlend));
    }

#if UNITY_EDITOR
    [ContextMenu("Test Parry Telegraph")]
    void TestParryTelegraph() => StartVisualCue(AttackTelegraphType.Parry, flashDuration);

    [ContextMenu("Test Guard Telegraph")]
    void TestGuardTelegraph() => StartVisualCue(AttackTelegraphType.Guard, flashDuration);

    [ContextMenu("Test Dodge Telegraph")]
    void TestDodgeTelegraph() => StartVisualCue(AttackTelegraphType.Dodge, flashDuration);

    [ContextMenu("Test Danger Telegraph")]
    void TestDangerTelegraph() => StartVisualCue(AttackTelegraphType.Danger, flashDuration);

    [ContextMenu("Reset To Idle")]
    void TestResetIdle() => ResetToIdle();
#endif
}
