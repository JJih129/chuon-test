using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class BossBreakController : MonoBehaviour
{
    public enum BreakSource
    {
        Generic = 0,
        Parry = 1
    }

    [Header("01. Break Gauge")]
    [Tooltip("Maximum break gauge value.")]
    [Min(1f)] public float maxBreak = 100f;

    [Tooltip("Break damage applied by a regular successful player hit.")]
    [Min(0f)] public float basicAttackBreakDamage = 1f;

    [Tooltip("Break damage applied by a successful parry.")]
    [Min(0f)] public float baseBreakPerHit = 12.5f;

    [Tooltip("How long the boss stays in break state.")]
    [Min(0f)] public float breakDuration = 8f;

    [Tooltip("Legacy recovery value. Kept for inspector compatibility.")]
    [Min(0f)] public float recoveryPerSecond = 15f;

    [Header("02. References")]
    [Tooltip("Boss health/stagger controller.")]
    [SerializeField] private BossHealth bossHealth;

    [Tooltip("Boss animator that uses the break bool.")]
    [SerializeField] private Animator bossAnimator;

    [Tooltip("Animator bool parameter name for break state.")]
    [SerializeField] private string breakBoolName = "IsBreak";

    [Header("03. Break Animation Hold")]
    [SerializeField] private string breakStateName = "Break";
    [SerializeField, Range(0.05f, 1f)] private float breakHoldNormalizedTime = 0.4f;

    [Header("04. Break Hologram")]
    [SerializeField] private bool useBreakHologram = true;
    [SerializeField] private Transform hologramRoot;
    [SerializeField] private Material breakHologramMaterial;
    [SerializeField] private string breakHologramResourcePath = "Tutorial/TutorialHologramSpawn";
    [SerializeField] private Color breakHologramColor = new Color(0f, 0.72f, 1f, 0.42f);

    [Header("05. Events")]
    [Tooltip("Invoked when break state starts.")]
    public UnityEvent OnBreakEnter = new UnityEvent();

    [Tooltip("Invoked when break state ends.")]
    public UnityEvent OnBreakExit = new UnityEvent();

    public event Action<float, float> OnBreakChanged;

    [Header("06. Debug")]
    [Tooltip("Enable debug logging for break flow.")]
    public bool logDebug = false;

    private float _currentBreak;
    private float _breakTimer;
    private bool _isInBreak;
    private int _breakBoolHash;
    private int _breakStateHash;
    private bool _breakAnimationHeld;
    private float _cachedAnimatorSpeed = 1f;
    private Renderer[] _hologramRenderers;
    private Material[][] _hologramOriginalMaterials;
    private Material _runtimeHologramMaterial;
    private PlayerUltimateController _ultimateController;
    private bool _hasUltimatePresentationSnapshot;
    private float _ultimatePresentationBreakSnapshot;
    private float _ultimatePresentationBreakTimerSnapshot;
    private bool _ultimatePresentationWasInBreak;

    public float NormalizedBreak => Mathf.Clamp01(maxBreak > 0f ? _currentBreak / maxBreak : 0f);
    public float CurrentBreak => _currentBreak;

    public float Get01() => NormalizedBreak;

    public bool IsInBreak => _isInBreak;
    public float RemainingBreakTime => _breakTimer;

    static readonly int HologramColorId = Shader.PropertyToID("_Hologram_Color");
    static readonly int TextureTintColorId = Shader.PropertyToID("_Texture_Tint_Color");

    private void Reset()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator)
        {
            BossReferences bossReferences = GetComponent<BossReferences>();
            if (bossReferences != null && bossReferences.MainAnimator != null)
                bossAnimator = bossReferences.MainAnimator;
            if (!bossAnimator) bossAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        }
    }

    private void Awake()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator)
        {
            BossReferences bossReferences = GetComponent<BossReferences>();
            if (bossReferences != null && bossReferences.MainAnimator != null)
                bossAnimator = bossReferences.MainAnimator;
            if (!bossAnimator) bossAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        }

        if (bossHealth != null)
            bossHealth.OnDied += HandleBossDied;

        _ultimateController = GameplaySceneCache.ResolvePlayerUltimateController();
        if (_ultimateController != null)
        {
            _ultimateController.OnUltimateStarted += HandleUltimateStarted;
            _ultimateController.OnUltimateEnded += HandleUltimateEnded;
        }

        _breakBoolHash = Animator.StringToHash(breakBoolName);
        _breakStateHash = Animator.StringToHash(breakStateName);
        ResetGauge();
        enabled = false;
    }

    private void OnDestroy()
    {
        ReleaseBreakAnimationHold();
        RestoreBreakHologram();

        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;

        if (_ultimateController != null)
        {
            _ultimateController.OnUltimateStarted -= HandleUltimateStarted;
            _ultimateController.OnUltimateEnded -= HandleUltimateEnded;
        }
    }

    private void Update()
    {
        if (!_isInBreak)
            return;

        UpdateBreakAnimationHold();

        _breakTimer -= Time.deltaTime;
        if (_breakTimer <= 0f)
            ForceExitBreak();
    }

    public void NotifyBossDamaged(int damage)
    {
        if (damage <= 0) return;
        if (_isInBreak) return;

        float remove = basicAttackBreakDamage;
        _currentBreak = Mathf.Max(0f, _currentBreak - remove);
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] Damage={damage}, -{remove} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak <= 0f)
            ForceEnterBreak();
    }

    public void AddBreak(float amount, BreakSource source)
    {
        if (_isInBreak) return;

        float remove = amount > 0f
            ? amount
            : source == BreakSource.Parry
                ? baseBreakPerHit
                : basicAttackBreakDamage;
        if (remove <= 0f) return;

        _currentBreak = Mathf.Max(0f, _currentBreak - remove);
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] Source={source}, -{remove} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak <= 0f)
            ForceEnterBreak();
    }

    public void ForceEnterBreak()
    {
        if (_isInBreak) return;

        _currentBreak = 0f;
        RaiseBreakChanged();
        _isInBreak = true;
        _breakTimer = breakDuration;
        enabled = true;
        ReleaseBreakAnimationHold();
        ApplyBreakHologram();

        if (bossHealth != null)
            bossHealth.SetStaggered(true);

        if (bossAnimator != null && _breakBoolHash != 0)
            bossAnimator.SetBool(_breakBoolHash, true);

        OnBreakEnter?.Invoke();

        if (logDebug)
            Debug.Log("[Break] ENTER", this);
    }

    public void ForceExitBreak()
    {
        if (!_isInBreak) return;

        _isInBreak = false;
        _breakTimer = 0f;
        ReleaseBreakAnimationHold();
        RestoreBreakHologram();

        if (bossHealth != null)
            bossHealth.SetStaggered(false);

        if (bossAnimator != null && _breakBoolHash != 0)
            bossAnimator.SetBool(_breakBoolHash, false);

        _currentBreak = maxBreak;
        RaiseBreakChanged();
        OnBreakExit?.Invoke();
        enabled = false;

        if (logDebug)
            Debug.Log("[Break] EXIT", this);
    }

    public void RestoreStateAfterPresentation(float preservedBreak, bool preservedInBreak, float preservedBreakTimer)
    {
        if (bossHealth != null && bossHealth.IsDead)
            return;

        if (preservedInBreak)
        {
            bool wasInBreak = _isInBreak;
            _currentBreak = 0f;
            _isInBreak = true;
            _breakTimer = Mathf.Max(_breakTimer, Mathf.Max(0f, preservedBreakTimer));
            enabled = true;
            ReleaseBreakAnimationHold();
            ApplyBreakHologram();

            if (bossHealth != null)
                bossHealth.SetStaggered(true);

            if (bossAnimator != null && _breakBoolHash != 0)
                bossAnimator.SetBool(_breakBoolHash, true);

            RaiseBreakChanged();

            if (!wasInBreak)
                OnBreakEnter?.Invoke();

            return;
        }

        if (_isInBreak)
            return;

        float clampedBreak = Mathf.Clamp(preservedBreak, 0f, maxBreak);
        if (_currentBreak <= clampedBreak + 0.001f)
            return;

        _currentBreak = clampedBreak;
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] RestoreAfterPresentation -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);
    }

    public void ResetGauge()
    {
        _currentBreak = maxBreak;
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log("[Break] ResetGauge()", this);
    }

    private void HandleBossDied()
    {
        ReleaseBreakAnimationHold();
        RestoreBreakHologram();
        ForceExitBreak();
        ResetGauge();

        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;
    }

    private void HandleUltimateStarted()
    {
        _ultimatePresentationBreakSnapshot = _currentBreak;
        _ultimatePresentationBreakTimerSnapshot = _breakTimer;
        _ultimatePresentationWasInBreak = _isInBreak;
        _hasUltimatePresentationSnapshot = true;

        if (logDebug)
            Debug.Log($"[Break] SnapshotForUltimate -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00}) inBreak={_isInBreak}", this);
    }

    private void HandleUltimateEnded()
    {
        if (!_hasUltimatePresentationSnapshot)
            return;

        _hasUltimatePresentationSnapshot = false;
        RestoreStateAfterPresentation(
            _ultimatePresentationBreakSnapshot,
            _ultimatePresentationWasInBreak,
            _ultimatePresentationBreakTimerSnapshot);
    }

    void RaiseBreakChanged()
    {
        OnBreakChanged?.Invoke(NormalizedBreak, _currentBreak);
    }

    void UpdateBreakAnimationHold()
    {
        if (_breakAnimationHeld || bossAnimator == null || _breakStateHash == 0)
            return;

        AnimatorStateInfo state = bossAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != _breakStateHash)
            return;

        if (state.normalizedTime < Mathf.Clamp01(breakHoldNormalizedTime))
            return;

        _cachedAnimatorSpeed = bossAnimator.speed;
        bossAnimator.speed = 0f;
        _breakAnimationHeld = true;
    }

    void ReleaseBreakAnimationHold()
    {
        if (!_breakAnimationHeld)
            return;

        _breakAnimationHeld = false;
        if (bossAnimator != null)
            bossAnimator.speed = _cachedAnimatorSpeed;
    }

    void ApplyBreakHologram()
    {
        if (!useBreakHologram || _runtimeHologramMaterial != null)
            return;

        Material sourceMaterial = ResolveBreakHologramMaterial();
        if (sourceMaterial == null)
            return;

        List<Renderer> validRenderers = CollectBreakHologramRenderers();
        if (validRenderers.Count == 0)
            return;

        var originalMaterials = new List<Material[]>(validRenderers.Count);
        for (int i = 0; i < validRenderers.Count; i++)
            originalMaterials.Add(validRenderers[i].sharedMaterials);

        _runtimeHologramMaterial = new Material(sourceMaterial) { name = name + "_BreakHologram" };
        SetBreakHologramColor();

        _hologramRenderers = validRenderers.ToArray();
        _hologramOriginalMaterials = originalMaterials.ToArray();
        for (int i = 0; i < _hologramRenderers.Length; i++)
        {
            Renderer renderer = _hologramRenderers[i];
            Material[] original = _hologramOriginalMaterials[i];
            if (renderer == null || original == null)
                continue;

            Material[] hologramMaterials = new Material[original.Length];
            for (int slot = 0; slot < hologramMaterials.Length; slot++)
                hologramMaterials[slot] = _runtimeHologramMaterial;
            renderer.sharedMaterials = hologramMaterials;
        }
    }

    List<Renderer> CollectBreakHologramRenderers()
    {
        var validRenderers = new List<Renderer>(32);
        var seen = new HashSet<Renderer>();

        AddBreakHologramRenderers(hologramRoot, validRenderers, seen);
        BossReferences bossReferences = GetComponent<BossReferences>() ?? GetComponentInParent<BossReferences>();
        if (bossReferences != null)
            AddBreakHologramRenderers(bossReferences.VisualRoot, validRenderers, seen);
        if (bossAnimator != null)
            AddBreakHologramRenderers(bossAnimator.transform, validRenderers, seen);
        AddBreakHologramRenderers(transform, validRenderers, seen);

        return validRenderers;
    }

    static void AddBreakHologramRenderers(Transform root, List<Renderer> validRenderers, HashSet<Renderer> seen)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer || !seen.Add(renderer))
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            validRenderers.Add(renderer);
        }
    }

    Material ResolveBreakHologramMaterial()
    {
        if (breakHologramMaterial != null)
            return breakHologramMaterial;

        if (!string.IsNullOrWhiteSpace(breakHologramResourcePath))
            breakHologramMaterial = Resources.Load<Material>(breakHologramResourcePath);

#if UNITY_EDITOR
        if (breakHologramMaterial == null)
            breakHologramMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Holograms/Materials/Examples/Basic/Scanline_Hologram_Empty.mat");
#endif

        return breakHologramMaterial;
    }

    void SetBreakHologramColor()
    {
        if (_runtimeHologramMaterial == null)
            return;

        if (_runtimeHologramMaterial.HasProperty(HologramColorId))
            _runtimeHologramMaterial.SetColor(HologramColorId, breakHologramColor);

        if (_runtimeHologramMaterial.HasProperty(TextureTintColorId))
        {
            Color tint = breakHologramColor;
            tint.a *= 0.75f;
            _runtimeHologramMaterial.SetColor(TextureTintColorId, tint);
        }
    }

    void RestoreBreakHologram()
    {
        if (_hologramRenderers != null && _hologramOriginalMaterials != null)
        {
            int count = Mathf.Min(_hologramRenderers.Length, _hologramOriginalMaterials.Length);
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = _hologramRenderers[i];
                Material[] materials = _hologramOriginalMaterials[i];
                if (renderer != null && materials != null)
                    renderer.sharedMaterials = materials;
            }
        }

        _hologramRenderers = null;
        _hologramOriginalMaterials = null;

        if (_runtimeHologramMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeHologramMaterial);
            else
                DestroyImmediate(_runtimeHologramMaterial);
        }
        _runtimeHologramMaterial = null;
    }

    private void OnValidate()
    {
        maxBreak = Mathf.Max(1f, maxBreak);
        basicAttackBreakDamage = Mathf.Max(0f, basicAttackBreakDamage);
        baseBreakPerHit = Mathf.Max(0f, baseBreakPerHit);
        breakDuration = Mathf.Max(0f, breakDuration);
        recoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
        breakHoldNormalizedTime = Mathf.Clamp(breakHoldNormalizedTime, 0.05f, 1f);
    }
}
