using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateVFXPresenter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UltimateScreenFX screenFX;
    [SerializeField] private UltimateScreenCrackOverlay screenCrackOverlay;
    [SerializeField] private UltimateSlashBurstSpawner slashBurstSpawner;
    [SerializeField] private UltimateSwordCoreChargeFx swordCoreChargeFx;
    [SerializeField] private PlayerReferences playerReferences;
    [SerializeField] private Transform fallbackVfxRoot;
    [SerializeField] private bool debugLog;

    UltimateSequenceData _data;
    UltimateTargetBinder _binder;
    UltimateCameraDirector _cameraDirector;
    Transform _cachedVfxRoot;
    Transform _cachedSequenceCameraTransform;
    Transform _runtimeIntroSwordAnchor;
    readonly List<ParticleSystem> _speedParticles = new(16);
    readonly List<Animator> _speedAnimators = new(4);
    readonly List<Transform> _runtimeTransforms = new(32);
    readonly List<Renderer> _tintRenderers = new(16);
    readonly List<TrailRenderer> _tintTrails = new(8);
    MaterialPropertyBlock _tintBlock;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int MainColorId = Shader.PropertyToID("_MainColor");
    static readonly int InnerColorId = Shader.PropertyToID("_InnerColor");
    static readonly int OuterColorId = Shader.PropertyToID("_OuterColor");
    static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int AddColorId = Shader.PropertyToID("_AddColor");
    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int EdgeColorId = Shader.PropertyToID("_EdgeCol");
    static readonly int EmisColorId = Shader.PropertyToID("_EmisColor");
    int _cachedSlashCount;
    int _sequenceSeed;

    void Awake()
    {
        ResolveReferences();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences();
    }
#endif

    public void BeginSequence(UltimateSequenceData data, UltimateTargetBinder binder, UltimateCameraDirector cameraDirector)
    {
        if (playerReferences == null || screenFX == null || slashBurstSpawner == null)
            ResolveReferences();

        _data = data;
        _binder = binder;
        _cameraDirector = cameraDirector;
        _cachedVfxRoot = ResolveVfxRootInternal(binder);
        _cachedSequenceCameraTransform = cameraDirector != null && cameraDirector.ActiveCamera != null
            ? cameraDirector.ActiveCamera.transform
            : null;
        _cachedSlashCount = Mathf.Max(1, _data != null ? _data.SlashCount : 1);
        _sequenceSeed = (Time.frameCount * 397) ^ GetInstanceID();
        swordCoreChargeFx?.Prime(_cachedSequenceCameraTransform);

        if (screenCrackOverlay != null)
        {
            Camera activeCamera = _cachedSequenceCameraTransform != null
                ? _cachedSequenceCameraTransform.GetComponent<Camera>()
                : cameraDirector != null ? cameraDirector.ActiveCamera : null;
            screenCrackOverlay.BindCamera(activeCamera);
            screenCrackOverlay.ClearImmediate();
        }

        screenFX?.PlayChargeIn();
    }

    public void EndSequence()
    {
        swordCoreChargeFx?.StopImmediate();
        screenFX?.PlayChargeOut();
        screenCrackOverlay?.ClearImmediate();
        _data = null;
        _binder = null;
        _cameraDirector = null;
        _cachedVfxRoot = null;
        _cachedSequenceCameraTransform = null;
        _cachedSlashCount = 0;
    }

    public void PlayIntroPose()
    {
        if (_data == null)
            return;

        if (swordCoreChargeFx == null || !swordCoreChargeFx)
            ResolveReferences();

        if (!_data.Vfx.suppressIntroPoseParticles)
        {
            Vector3 rootPosition = _cachedVfxRoot != null ? _cachedVfxRoot.position : transform.position;
            SpawnWorldVfx(_data.Vfx.introPoseVfxPrefab, rootPosition, Quaternion.identity, _data.Vfx.introPoseLifetime);
        }

        Transform swordCoreAnchor = ResolveIntroSwordAnchor();
        float effectDuration = Mathf.Max(_data.Timings.introPoseDuration, _data.Vfx.introPoseLifetime);
        if (!_data.Vfx.suppressIntroSwordCoreCharge)
            swordCoreChargeFx?.Play(swordCoreAnchor, effectDuration, _cachedSequenceCameraTransform);
    }

    public void PlayDashSlash(Vector3 position, Vector3 lookTarget)
    {
        if (_data == null)
            return;

        if (ShouldSuppressLegacySlashVfx())
        {
            if (_data.Vfx.useDashScreenPulse)
                screenFX?.PulseMinor();
            _cameraDirector?.PlayShake(_data.Impact.dashShakeAmplitude, _data.Impact.dashShakeDuration);
            return;
        }

        Quaternion rotation = Quaternion.LookRotation((lookTarget - position).normalized, Vector3.up);
        SpawnWorldVfx(_data.Vfx.dashSlashVfxPrefab, position, rotation, _data.Vfx.dashSlashLifetime);
        EmitSlashBurst(0, _cachedSlashCount, position, lookTarget, _data.Vfx.dashSlashLifetime, 1.05f);
        if (_data.Vfx.useDashScreenPulse)
            screenFX?.PulseMinor();
        _cameraDirector?.PlayShake(_data.Impact.dashShakeAmplitude, _data.Impact.dashShakeDuration);
    }

    public void PlayMultiSlash(int slashIndex, int totalSlashes, UltimateSequenceData.SlashStepData step, Vector3 position, Vector3 lookTarget)
    {
        if (_data == null)
            return;

        if (ShouldSuppressLegacySlashVfx())
            return;

        Quaternion rotation = Quaternion.LookRotation((lookTarget - position).normalized, Vector3.up);
        if (_data.Vfx.useAfterImageStyleSlash)
            EmitSlashBurst(slashIndex, totalSlashes, position, lookTarget, Mathf.Max(0.05f, step.delay), step.accentCrackPulse ? 1.3f : 1f);
        else
            SpawnWorldVfx(_data.Vfx.dashSlashVfxPrefab, position, rotation, Mathf.Max(0.05f, step.delay));

        if (step.accentCrackPulse && _data.Vfx.useAccentSlashFlash)
        {
            screenFX?.Flash(_data.Vfx.accentSlashFlashStrength, _data.Vfx.accentSlashFlashDuration);
        }
        else if (_data.Vfx.useMultiSlashScreenPulse)
        {
            screenFX?.Flash(_data.Vfx.multiSlashPulseStrength, _data.Vfx.multiSlashPulseDuration);
        }

        screenCrackOverlay?.EmitSlashCrack(position, lookTarget - position, _data.Vfx, _sequenceSeed ^ (slashIndex * 1619));
        _cameraDirector?.PlayShake(_data.Impact.slashShakeAmplitude, _data.Impact.slashShakeDuration);
    }

    public bool PlaySlashStormAoe(Vector3 center, Quaternion frameRotation)
    {
        if (_data == null || _data.Vfx.slashStormAoeVfxPrefab == null)
            return false;

        Quaternion rotation = frameRotation * Quaternion.Euler(_data.Vfx.slashStormAoeLocalEuler);
        Vector3 position = center + frameRotation * _data.Vfx.slashStormAoeLocalOffset;
        float playbackSpeed = Mathf.Clamp(_data.Vfx.slashStormAoePlaybackSpeed, 0.05f, 2f);
        float lifetime = _data.Vfx.slashStormAoeLifetime / playbackSpeed;
        GameObject spawned = SpawnWorldVfx(_data.Vfx.slashStormAoeVfxPrefab, position, rotation, lifetime);
        RestoreSlashStormAoeRuntimeHierarchy(spawned);
        ApplyPlaybackSpeed(spawned, playbackSpeed);
        ApplySlashStormAoeTint(spawned);
        SuppressSlashStormAoeBlueParticles(spawned);
        if (_data.Vfx.useMultiSlashScreenPulse)
            screenFX?.Flash(_data.Vfx.multiSlashPulseStrength, _data.Vfx.multiSlashPulseDuration);
        _cameraDirector?.PlayShake(_data.Impact.slashShakeAmplitude, _data.Impact.slashShakeDuration);
        return true;
    }

    public void PlayCrackBurst(Vector3 position)
    {
        if (_data == null)
            return;

        if (!ShouldSuppressLegacySlashVfx())
        {
            Vector3 forward = ResolveSequenceForward();
            slashBurstSpawner?.EmitConvergenceBurst(position, forward, _sequenceSeed ^ 7919, _data.Vfx.crackLifetime, _data.Vfx, 1.05f, false);

            if (_data.Vfx.useScreenSpaceCrack)
            {
                if (_cachedSequenceCameraTransform != null)
                    SpawnScreenSpaceVfx(_data.Vfx.crackScreenVfxPrefab, _cachedSequenceCameraTransform, _data.Vfx.crackLifetime);
            }
            else
            {
                SpawnWorldVfx(_data.Vfx.crackWorldVfxPrefab, position, Quaternion.identity, _data.Vfx.crackLifetime);
            }

            screenCrackOverlay?.EmitBurstCrack(position, _data.Vfx, 1.08f, _sequenceSeed ^ 7919);
        }

        screenFX?.Flash(0.7f, 0.12f);
        _cameraDirector?.PlayShake(_data.Impact.crackShakeAmplitude, _data.Impact.crackShakeDuration);
    }

    public void PlayFinalExplosion(Vector3 position)
    {
        if (_data == null)
            return;

        if (!ShouldSuppressLegacySlashVfx())
        {
            Vector3 forward = ResolveSequenceForward();
            slashBurstSpawner?.EmitConvergenceBurst(position, forward, _sequenceSeed ^ 15401, _data.Vfx.explosionLifetime, _data.Vfx, 1.2f, true);
        }

        GameObject explosion = SpawnWorldVfx(_data.Vfx.explosionVfxPrefab, position, Quaternion.identity, _data.Vfx.explosionLifetime);
        ApplyUniformScale(explosion, _data.Vfx.explosionVfxScale);
        ApplyExplosionTint(explosion);
        for (int i = 0; i < _data.Vfx.shardSpawnCount; i++)
        {
            Vector3 jitter = Random.insideUnitSphere * 0.6f;
            jitter.y = Mathf.Abs(jitter.y) * 0.5f;
            SpawnWorldVfx(_data.Vfx.shardVfxPrefab, position + jitter, Random.rotation, _data.Vfx.explosionLifetime);
        }

        if (!ShouldSuppressLegacySlashVfx())
            screenCrackOverlay?.EmitBurstCrack(position, _data.Vfx, 1.35f, _sequenceSeed ^ 15401);
        screenFX?.PulseMajor();
        _cameraDirector?.PlayShake(_data.Impact.finalShakeAmplitude, _data.Impact.finalShakeDuration);
    }

    public void PlayWalkout(Vector3 position, Vector3 forward)
    {
        if (_data == null)
            return;

        Quaternion rotation = forward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;
        SpawnWorldVfx(_data.Vfx.walkoutVfxPrefab, position, rotation, _data.Vfx.walkoutLifetime);
    }

    void EmitSlashBurst(int index, int totalCount, Vector3 origin, Vector3 focusPoint, float life, float intensityScale)
    {
        if (slashBurstSpawner == null)
            return;

        slashBurstSpawner.EmitOneSlash(index, totalCount, _sequenceSeed, origin, focusPoint, life, _data != null ? _data.Vfx : null, intensityScale);
    }

    bool ShouldSuppressLegacySlashVfx()
    {
        return _data != null
            && _data.Vfx.replaceMultiSlashWithAoe
            && _data.Vfx.slashStormAoeVfxPrefab != null;
    }

    Vector3 ResolveSequenceForward()
    {
        if (_binder != null && _binder.HasCinematicFrame)
            return _binder.CinematicFrame.Forward;

        return _binder != null && _binder.PlayerRoot != null ? _binder.PlayerRoot.forward : transform.forward;
    }

    GameObject SpawnWorldVfx(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (prefab == null)
            return null;

        GameObject spawned = TransientVfxPool.Spawn(prefab, position, rotation, _cachedVfxRoot, lifetime);
        if (debugLog)
            Debug.Log($"[UltimateVFX] Spawn {prefab.name} at {position}", this);
        return spawned;
    }

    void ApplyPlaybackSpeed(GameObject root, float speed)
    {
        if (root == null)
            return;

        speed = Mathf.Clamp(speed, 0.05f, 2f);
        _speedParticles.Clear();
        root.GetComponentsInChildren(true, _speedParticles);
        for (int i = 0; i < _speedParticles.Count; i++)
        {
            ParticleSystem.MainModule main = _speedParticles[i].main;
            main.simulationSpeed = speed;
        }

        _speedAnimators.Clear();
        root.GetComponentsInChildren(true, _speedAnimators);
        for (int i = 0; i < _speedAnimators.Count; i++)
            _speedAnimators[i].speed = speed;
    }

    void ApplySlashStormAoeTint(GameObject root)
    {
        if (root == null || _data == null || !_data.Vfx.tintSlashStormAoe)
            return;

        Color primary = _data.Vfx.slashStormAoeTint;
        Color accent = _data.Vfx.slashStormAoeAccentTint;
        Color highlight = _data.Vfx.slashStormAoeHighlightTint;
        Color trail = _data.Vfx.slashStormAoeTrailTint;
        Color emission = _data.Vfx.slashStormAoeEmission;
        Gradient particleGradient = CreateSlashStormGradient(primary, accent, highlight);
        Gradient trailGradient = CreateTrailGradient(trail, accent, highlight);

        _speedParticles.Clear();
        root.GetComponentsInChildren(true, _speedParticles);
        for (int i = 0; i < _speedParticles.Count; i++)
        {
            ApplyParticlePalette(_speedParticles[i], primary, particleGradient, trailGradient);
        }

        _tintTrails.Clear();
        root.GetComponentsInChildren(true, _tintTrails);
        for (int i = 0; i < _tintTrails.Count; i++)
        {
            TrailRenderer trailRenderer = _tintTrails[i];
            trailRenderer.startColor = highlight;
            trailRenderer.endColor = new Color(trail.r, trail.g, trail.b, 0f);
            trailRenderer.colorGradient = trailGradient;
        }

        if (_tintBlock == null)
            _tintBlock = new MaterialPropertyBlock();

        _tintRenderers.Clear();
        root.GetComponentsInChildren(true, _tintRenderers);
        for (int i = 0; i < _tintRenderers.Count; i++)
        {
            Renderer targetRenderer = _tintRenderers[i];
            Material shared = targetRenderer.sharedMaterial;
            if (shared == null)
                continue;

            targetRenderer.GetPropertyBlock(_tintBlock);
            if (shared.HasProperty(BaseColorId))
                _tintBlock.SetColor(BaseColorId, primary);
            if (shared.HasProperty(ColorId))
                _tintBlock.SetColor(ColorId, primary);
            if (shared.HasProperty(TintColorId))
                _tintBlock.SetColor(TintColorId, primary);
            if (shared.HasProperty(EmissionColorId))
                _tintBlock.SetColor(EmissionColorId, emission);
            if (shared.HasProperty(MainColorId))
                _tintBlock.SetColor(MainColorId, primary);
            if (shared.HasProperty(InnerColorId))
                _tintBlock.SetColor(InnerColorId, highlight);
            if (shared.HasProperty(OuterColorId))
                _tintBlock.SetColor(OuterColorId, accent);
            if (shared.HasProperty(CoreColorId))
                _tintBlock.SetColor(CoreColorId, highlight);
            if (shared.HasProperty(RimColorId))
                _tintBlock.SetColor(RimColorId, accent);
            if (shared.HasProperty(AddColorId))
                _tintBlock.SetColor(AddColorId, accent);
            if (shared.HasProperty(GlowColorId))
                _tintBlock.SetColor(GlowColorId, emission);
            if (shared.HasProperty(EdgeColorId))
                _tintBlock.SetColor(EdgeColorId, highlight);
            if (shared.HasProperty(EmisColorId))
                _tintBlock.SetColor(EmisColorId, emission);
            targetRenderer.SetPropertyBlock(_tintBlock);
        }
    }

    void RestoreSlashStormAoeRuntimeHierarchy(GameObject root)
    {
        if (root == null)
            return;

        _runtimeTransforms.Clear();
        root.GetComponentsInChildren(true, _runtimeTransforms);
        for (int i = 0; i < _runtimeTransforms.Count; i++)
        {
            Transform child = _runtimeTransforms[i];
            if (child != null && !child.gameObject.activeSelf)
                child.gameObject.SetActive(true);
        }
    }

    void ApplyExplosionTint(GameObject root)
    {
        if (root == null || _data == null || !_data.Vfx.tintExplosionVfx)
            return;

        Color primary = _data.Vfx.explosionPrimaryTint;
        Color accent = _data.Vfx.explosionAccentTint;
        Color highlight = _data.Vfx.explosionHighlightTint;
        Color smoke = _data.Vfx.explosionSmokeTint;
        Color emission = _data.Vfx.explosionEmissionTint;
        Gradient fireGradient = CreateSlashStormGradient(primary, accent, highlight);
        Gradient smokeGradient = CreateTrailGradient(smoke, primary, accent);

        _speedParticles.Clear();
        root.GetComponentsInChildren(true, _speedParticles);
        for (int i = 0; i < _speedParticles.Count; i++)
        {
            ParticleSystem particle = _speedParticles[i];
            bool smokeLike = IsSmokeLike(particle.name);
            ApplyParticlePalette(particle, smokeLike ? smoke : primary, smokeLike ? smokeGradient : fireGradient, smokeLike ? smokeGradient : fireGradient);
        }

        _tintTrails.Clear();
        root.GetComponentsInChildren(true, _tintTrails);
        for (int i = 0; i < _tintTrails.Count; i++)
        {
            TrailRenderer trailRenderer = _tintTrails[i];
            trailRenderer.startColor = highlight;
            trailRenderer.endColor = new Color(primary.r, primary.g, primary.b, 0f);
            trailRenderer.colorGradient = fireGradient;
        }

        ApplyRendererPalette(root, primary, accent, highlight, emission, smoke);
    }

    void SuppressSlashStormAoeBlueParticles(GameObject root)
    {
        if (root == null || _data == null || !_data.Vfx.suppressSlashStormAoeBlueParticles)
            return;

        _speedParticles.Clear();
        root.GetComponentsInChildren(true, _speedParticles);
        for (int i = 0; i < _speedParticles.Count; i++)
        {
            ParticleSystem particle = _speedParticles[i];
            if (ShouldSuppressAoeParticle(particle.name))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.gameObject.SetActive(false);
            }
        }
    }

    static bool ShouldSuppressAoeParticle(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        return objectName.IndexOf("flash", System.StringComparison.OrdinalIgnoreCase) >= 0
            || objectName.IndexOf("start_particle", System.StringComparison.OrdinalIgnoreCase) >= 0
            || objectName.IndexOf("start particles", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void ApplyRendererPalette(GameObject root, Color primary, Color accent, Color highlight, Color emission, Color smoke)
    {
        if (_tintBlock == null)
            _tintBlock = new MaterialPropertyBlock();

        _tintRenderers.Clear();
        root.GetComponentsInChildren(true, _tintRenderers);
        for (int i = 0; i < _tintRenderers.Count; i++)
        {
            Renderer targetRenderer = _tintRenderers[i];
            Material shared = targetRenderer.sharedMaterial;
            if (shared == null)
                continue;

            bool smokeLike = IsSmokeLike(targetRenderer.name);
            Color baseTint = smokeLike ? smoke : primary;
            Color outerTint = smokeLike ? primary : accent;

            targetRenderer.GetPropertyBlock(_tintBlock);
            if (shared.HasProperty(BaseColorId))
                _tintBlock.SetColor(BaseColorId, baseTint);
            if (shared.HasProperty(ColorId))
                _tintBlock.SetColor(ColorId, baseTint);
            if (shared.HasProperty(TintColorId))
                _tintBlock.SetColor(TintColorId, baseTint);
            if (shared.HasProperty(EmissionColorId))
                _tintBlock.SetColor(EmissionColorId, emission);
            if (shared.HasProperty(MainColorId))
                _tintBlock.SetColor(MainColorId, baseTint);
            if (shared.HasProperty(InnerColorId))
                _tintBlock.SetColor(InnerColorId, highlight);
            if (shared.HasProperty(OuterColorId))
                _tintBlock.SetColor(OuterColorId, outerTint);
            if (shared.HasProperty(CoreColorId))
                _tintBlock.SetColor(CoreColorId, highlight);
            if (shared.HasProperty(RimColorId))
                _tintBlock.SetColor(RimColorId, accent);
            if (shared.HasProperty(AddColorId))
                _tintBlock.SetColor(AddColorId, accent);
            if (shared.HasProperty(GlowColorId))
                _tintBlock.SetColor(GlowColorId, emission);
            if (shared.HasProperty(EdgeColorId))
                _tintBlock.SetColor(EdgeColorId, highlight);
            if (shared.HasProperty(EmisColorId))
                _tintBlock.SetColor(EmisColorId, emission);
            targetRenderer.SetPropertyBlock(_tintBlock);
        }
    }

    static bool IsSmokeLike(string objectName)
    {
        return !string.IsNullOrEmpty(objectName)
            && objectName.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void ApplyParticlePalette(ParticleSystem particle, Color primary, Gradient particleGradient, Gradient trailGradient)
    {
        ParticleSystem.MainModule main = particle.main;
        main.startColor = new ParticleSystem.MinMaxGradient(primary);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particle.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(particleGradient);

        ParticleSystem.ColorBySpeedModule colorBySpeed = particle.colorBySpeed;
        if (colorBySpeed.enabled)
            colorBySpeed.color = new ParticleSystem.MinMaxGradient(particleGradient);

        ParticleSystem.TrailModule trails = particle.trails;
        if (trails.enabled)
        {
            trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(trailGradient);
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailGradient);
        }

        ParticleSystem.CustomDataModule customData = particle.customData;
        customData.enabled = true;
        customData.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Color);
        customData.SetColor(ParticleSystemCustomData.Custom1, new ParticleSystem.MinMaxGradient(particleGradient));
        customData.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Color);
        customData.SetColor(ParticleSystemCustomData.Custom2, new ParticleSystem.MinMaxGradient(trailGradient));
    }

    static Gradient CreateSlashStormGradient(Color primary, Color accent, Color highlight)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(highlight, 0f),
                new GradientColorKey(accent, 0.38f),
                new GradientColorKey(primary, 1f)
            },
            new[]
            {
                new GradientAlphaKey(highlight.a, 0f),
                new GradientAlphaKey(accent.a, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    static Gradient CreateTrailGradient(Color primary, Color accent, Color highlight)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(highlight, 0f),
                new GradientColorKey(accent, 0.25f),
                new GradientColorKey(primary, 1f)
            },
            new[]
            {
                new GradientAlphaKey(highlight.a, 0f),
                new GradientAlphaKey(accent.a * 0.85f, 0.32f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    static void ApplyUniformScale(GameObject root, float scale)
    {
        if (root == null)
            return;

        scale = Mathf.Max(0.1f, scale);
        root.transform.localScale *= scale;
    }

    void SpawnScreenSpaceVfx(GameObject prefab, Transform cameraTransform, float lifetime)
    {
        if (prefab == null || cameraTransform == null)
            return;

        Vector3 position = cameraTransform.position + cameraTransform.forward * 0.8f;
        TransientVfxPool.Spawn(prefab, position, cameraTransform.rotation, cameraTransform, lifetime);
    }

    Transform ResolveVfxRootInternal(UltimateTargetBinder binder)
    {
        if (binder != null && binder.HasCinematicFrame)
            return null;
        if (binder != null && binder.PlayerVfxRoot != null)
            return binder.PlayerVfxRoot;
        if (playerReferences != null && playerReferences.VFXRoot != null)
            return playerReferences.VFXRoot;
        return fallbackVfxRoot;
    }

    Transform ResolveIntroSwordAnchor()
    {
        EnsureRuntimeIntroSwordAnchor();

        Transform visualLayerSource = null;
        if (_binder != null)
        {
            Transform swordCoreAnchor = _binder.GetPlayerIntroSwordEffectAnchor();
            Vector3 swordCorePoint = swordCoreAnchor != null
                ? swordCoreAnchor.position
                : _binder.GetPlayerIntroSwordLookPoint(Vector3.zero);
            visualLayerSource = _binder.PlayerPresentationClone != null && _binder.PlayerPresentationClone.CloneRoot != null
                ? _binder.PlayerPresentationClone.CloneRoot
                : swordCoreAnchor;
            if (swordCoreAnchor != null || _binder.PlayerRoot != null)
            {
                _runtimeIntroSwordAnchor.position = swordCorePoint;
                _runtimeIntroSwordAnchor.rotation = swordCoreAnchor != null
                    ? swordCoreAnchor.rotation
                    : _binder.PlayerRoot.rotation;
                _runtimeIntroSwordAnchor.localScale = Vector3.one;
                _runtimeIntroSwordAnchor.gameObject.layer = visualLayerSource != null
                    ? visualLayerSource.gameObject.layer
                    : (_binder.PlayerRoot != null ? _binder.PlayerRoot.gameObject.layer : gameObject.layer);
                return _runtimeIntroSwordAnchor;
            }

            if (_binder.PlayerPresentationClone != null && _binder.PlayerPresentationClone.CloneRoot != null)
                return _runtimeIntroSwordAnchor;

            if (_binder.PlayerRoot != null)
            {
                _runtimeIntroSwordAnchor.position = _binder.PlayerRoot.position;
                _runtimeIntroSwordAnchor.rotation = _binder.PlayerRoot.rotation;
                _runtimeIntroSwordAnchor.gameObject.layer = _binder.PlayerRoot.gameObject.layer;
                return _runtimeIntroSwordAnchor;
            }
        }

        if (_cachedVfxRoot != null)
        {
            _runtimeIntroSwordAnchor.position = _cachedVfxRoot.position;
            _runtimeIntroSwordAnchor.rotation = _cachedVfxRoot.rotation;
            _runtimeIntroSwordAnchor.gameObject.layer = _cachedVfxRoot.gameObject.layer;
            return _runtimeIntroSwordAnchor;
        }

        _runtimeIntroSwordAnchor.position = transform.position;
        _runtimeIntroSwordAnchor.rotation = transform.rotation;
        _runtimeIntroSwordAnchor.gameObject.layer = gameObject.layer;
        return _runtimeIntroSwordAnchor;
    }

    void EnsureRuntimeIntroSwordAnchor()
    {
        if (_runtimeIntroSwordAnchor != null)
            return;

        GameObject anchorObject = new GameObject("__UltimateIntroSwordFxAnchor");
        anchorObject.hideFlags = HideFlags.DontSave;
        anchorObject.transform.SetParent(transform, false);
        _runtimeIntroSwordAnchor = anchorObject.transform;
    }

    void ResolveReferences()
    {
        if (playerReferences == null)
            playerReferences = GetComponent<PlayerReferences>();
        if (screenFX == null)
            screenFX = GetComponent<UltimateScreenFX>();
        if (screenCrackOverlay == null)
        {
            screenCrackOverlay = GetComponent<UltimateScreenCrackOverlay>();
            if (screenCrackOverlay == null && Application.isPlaying)
                screenCrackOverlay = gameObject.AddComponent<UltimateScreenCrackOverlay>();
        }
        if (slashBurstSpawner == null)
            slashBurstSpawner = GetComponent<UltimateSlashBurstSpawner>();
        if (swordCoreChargeFx == null)
            swordCoreChargeFx = GetComponent<UltimateSwordCoreChargeFx>();
        if (swordCoreChargeFx == null && Application.isPlaying)
            swordCoreChargeFx = gameObject.AddComponent<UltimateSwordCoreChargeFx>();
    }
}
