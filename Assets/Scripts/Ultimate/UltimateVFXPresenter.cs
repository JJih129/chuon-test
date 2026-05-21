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

        Vector3 rootPosition = _cachedVfxRoot != null ? _cachedVfxRoot.position : transform.position;
        SpawnWorldVfx(_data.Vfx.introPoseVfxPrefab, rootPosition, Quaternion.identity, _data.Vfx.introPoseLifetime);

        Transform swordCoreAnchor = ResolveIntroSwordAnchor();
        float effectDuration = Mathf.Max(_data.Timings.introPoseDuration, _data.Vfx.introPoseLifetime);
        swordCoreChargeFx?.Play(swordCoreAnchor, effectDuration, _cachedSequenceCameraTransform);
    }

    public void PlayDashSlash(Vector3 position, Vector3 lookTarget)
    {
        if (_data == null)
            return;

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

    public void PlayCrackBurst(Vector3 position)
    {
        if (_data == null)
            return;

        Vector3 forward = _binder != null && _binder.PlayerRoot != null ? _binder.PlayerRoot.forward : transform.forward;
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
        screenFX?.Flash(0.7f, 0.12f);
        _cameraDirector?.PlayShake(_data.Impact.crackShakeAmplitude, _data.Impact.crackShakeDuration);
    }

    public void PlayFinalExplosion(Vector3 position)
    {
        if (_data == null)
            return;

        Vector3 forward = _binder != null && _binder.PlayerRoot != null ? _binder.PlayerRoot.forward : transform.forward;
        slashBurstSpawner?.EmitConvergenceBurst(position, forward, _sequenceSeed ^ 15401, _data.Vfx.explosionLifetime, _data.Vfx, 1.2f, true);
        SpawnWorldVfx(_data.Vfx.explosionVfxPrefab, position, Quaternion.identity, _data.Vfx.explosionLifetime);
        for (int i = 0; i < _data.Vfx.shardSpawnCount; i++)
        {
            Vector3 jitter = Random.insideUnitSphere * 0.6f;
            jitter.y = Mathf.Abs(jitter.y) * 0.5f;
            SpawnWorldVfx(_data.Vfx.shardVfxPrefab, position + jitter, Random.rotation, _data.Vfx.explosionLifetime);
        }

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

    void SpawnWorldVfx(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (prefab == null)
            return;

        TransientVfxPool.Spawn(prefab, position, rotation, _cachedVfxRoot, lifetime);
        if (debugLog)
            Debug.Log($"[UltimateVFX] Spawn {prefab.name} at {position}", this);
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
