using UnityEngine;

public class UltimateSlashBurstSpawner : MonoBehaviour
{
    [Header("Anchors")]
    [SerializeField] PlayerReferences playerReferences;
    [SerializeField] Transform player;
    [SerializeField] Transform spawnRoot;

    [Header("Prefab & Look")]
    [SerializeField] GameObject slashPrefab;
    [SerializeField] float forwardOffset = 3.0f;
    [SerializeField] float heightOffset = 1.4f;
    [SerializeField] float length = 6.0f;
    [SerializeField] float thickness = 0.08f;
    [SerializeField] float coneAngle = 25f;
    [SerializeField] bool alignSlashRollToObject002 = true;
    [SerializeField, Range(0f, 1f)] float object002BladeRollWeight = 0.85f;

    [Header("Material props (optional)")]
    [SerializeField] string propIntensity = "_Intensity";
    [SerializeField] string propScroll = "_ScrollSpeed";
    [SerializeField] string propAlpha = "_Alpha";
    [SerializeField] float baseIntensity = 1.2f;
    [SerializeField] float baseScroll = 2.0f;
    [SerializeField] Transform presentationPlayerOverride;
    [SerializeField] Transform presentationSpawnRootOverride;

    const float Golden = 137.507764f;
    bool _warnedMissingSlashPrefab;
    MaterialPropertyBlock _cachedPropertyBlock;
    int _propIntensityId = -1;
    int _propScrollId = -1;
    int _propAlphaId = -1;
    Transform _cachedWeaponRoot;
    Transform _cachedWeaponTransform;

    public void PushPresentationOverride(Transform playerOverride, Transform spawnRootOverride)
    {
        presentationPlayerOverride = playerOverride;
        presentationSpawnRootOverride = spawnRootOverride;
    }

    public void ClearPresentationOverride()
    {
        presentationPlayerOverride = null;
        presentationSpawnRootOverride = null;
    }

    void EnsureSpawnRoot()
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!player && playerReferences != null) player = playerReferences.PlayerRoot;
        if (!spawnRoot && playerReferences != null) spawnRoot = playerReferences.UltimateSpawnRoot;
        if (!player) player = transform;
        if (spawnRoot) return;
        var go = new GameObject("UltimateSpawnRoot");
        spawnRoot = go.transform;
        spawnRoot.SetParent(player, false);
        spawnRoot.localPosition = new Vector3(0f, heightOffset, forwardOffset);
        spawnRoot.localRotation = Quaternion.identity;
    }

    public void EmitOneSlash(int index, int totalCount, int patternSeed, Vector3 origin, Vector3 focusPoint, float life = 0.7f, UltimateSequenceData.VfxSettings style = null, float intensityScale = 1f)
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!player) player = playerReferences != null ? playerReferences.PlayerRoot : transform;
        EnsureSpawnRoot();

        Transform activePlayer = presentationPlayerOverride != null ? presentationPlayerOverride : player;
        Transform activeSpawnRoot = presentationSpawnRootOverride != null ? presentationSpawnRootOverride : spawnRoot;
        if (activePlayer == null)
            activePlayer = transform;
        if (activeSpawnRoot == null)
            activeSpawnRoot = activePlayer;

        GameObject mainSlashPrefab = ResolveMainSlashPrefab(style);
        if (!mainSlashPrefab)
        {
            if (!_warnedMissingSlashPrefab)
            {
                Debug.LogWarning("[Ultimate] UltimateSlashBurstSpawner.slashPrefab is not assigned. Slash burst VFX will be skipped.", this);
                _warnedMissingSlashPrefab = true;
            }
            return;
        }

        CacheShaderPropertyIds();

        Vector3 forwardToFocus = focusPoint - origin;
        forwardToFocus.y = 0f;
        if (forwardToFocus.sqrMagnitude <= 0.0001f)
            forwardToFocus = activePlayer.forward;

        Vector3 slashForward = forwardToFocus.normalized;
        Quaternion faceFwd = Quaternion.LookRotation(slashForward, ResolveSlashUpFromWeapon(activePlayer, slashForward));
        float yaw = index * Golden + Mathf.Lerp(-5f, 5f, Hash01(patternSeed, index, 11));
        float pitch = Mathf.Lerp(-coneAngle, coneAngle, Hash01(patternSeed, index, 29));
        Quaternion rot = faceFwd
                       * Quaternion.AngleAxis(yaw, Vector3.up)
                       * Quaternion.AngleAxis(pitch, Vector3.right);
        Vector3 pos = origin;

        float progress = totalCount > 1 ? index / Mathf.Max(1f, totalCount - 1f) : 0f;
        SpawnSlashInstance(mainSlashPrefab, pos, rot, life, 1f, 1f, intensityScale * Mathf.Lerp(1f, 1.15f, progress), 1f);

        if (style == null)
            return;

        EmitSupportSlashes(style, mainSlashPrefab, pos, faceFwd, yaw, pitch, index, patternSeed, life, intensityScale, progress);
        EmitLightBeams(style, ResolveLightBeamPrefab(style), pos, focusPoint, faceFwd, yaw, pitch, index, patternSeed, life, intensityScale, progress);
    }

    public void EmitOneSlash(int index, int totalCount, int patternSeed, float life = 0.7f, UltimateSequenceData.VfxSettings style = null, float intensityScale = 1f)
    {
        if (!playerReferences) playerReferences = GetComponent<PlayerReferences>();
        if (!player) player = playerReferences != null ? playerReferences.PlayerRoot : transform;
        EnsureSpawnRoot();

        Transform activePlayer = presentationPlayerOverride != null ? presentationPlayerOverride : player;
        Transform activeSpawnRoot = presentationSpawnRootOverride != null ? presentationSpawnRootOverride : spawnRoot;
        if (activePlayer == null)
            activePlayer = transform;
        if (activeSpawnRoot == null)
            activeSpawnRoot = activePlayer;

        Vector3 origin = activeSpawnRoot.position;
        Vector3 focusPoint = origin + activePlayer.forward * Mathf.Max(1.25f, forwardOffset);
        EmitOneSlash(index, totalCount, patternSeed, origin, focusPoint, life, style, intensityScale);
    }

    public void EmitConvergenceBurst(Vector3 center, Vector3 forward, int patternSeed, float life, UltimateSequenceData.VfxSettings style, float intensityScale = 1f, bool finalBurst = false)
    {
        if (style == null)
            return;

        GameObject beamPrefab = ResolveLightBeamPrefab(style);
        if (beamPrefab == null)
            return;

        CacheShaderPropertyIds();

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = transform.forward;
        flatForward.Normalize();

        int count = Mathf.Max(3, finalBurst ? style.finalConvergenceBeamCount : style.crackBeamCount);
        float angleStep = 360f / count;
        float burstIntensity = finalBurst ? style.finalBurstIntensityMultiplier : 1f;
        float lengthScale = finalBurst ? style.beamLengthScale * 1.28f : style.beamLengthScale * 1.05f;
        float thicknessScale = finalBurst ? style.beamThicknessScale * 1.15f : style.beamThicknessScale * 0.92f;
        float radiusMin = Mathf.Max(0.15f, style.beamSpawnRadiusMin * (finalBurst ? 1.08f : 0.92f));
        float radiusMax = Mathf.Max(radiusMin + 0.05f, style.beamSpawnRadiusMax * (finalBurst ? 1.22f : 1f));
        float focusHeight = style.beamFocusHeightOffset;

        for (int i = 0; i < count; i++)
        {
            float yaw = i * angleStep + Mathf.Lerp(-8f, 8f, Hash01(patternSeed, i, 71));
            float radius = Mathf.Lerp(radiusMin, radiusMax, Hash01(patternSeed, i, 73));
            float height = Mathf.Lerp(style.beamSpawnHeightMin, style.beamSpawnHeightMax, Hash01(patternSeed, i, 79));
            Vector3 radial = Quaternion.AngleAxis(yaw, Vector3.up) * flatForward;
            Vector3 spawnPos = center + radial * radius + Vector3.up * height;
            Vector3 focusPoint = center + Vector3.up * focusHeight;
            Quaternion inwardBasis = Quaternion.LookRotation((focusPoint - spawnPos).normalized, ResolveSlashUpFromWeapon(activePlayer: null, forward: focusPoint - spawnPos));
            float pitch = Mathf.Lerp(-style.beamPitchSpread, style.beamPitchSpread, Hash01(patternSeed, i, 83));
            Quaternion rotation = inwardBasis * Quaternion.AngleAxis(pitch, Vector3.right);
            SpawnSlashInstance(beamPrefab, spawnPos, rotation, life, thicknessScale, lengthScale, intensityScale * burstIntensity, style.beamAlpha);
        }
    }

    void EmitSupportSlashes(UltimateSequenceData.VfxSettings style, GameObject prefab, Vector3 pos, Quaternion faceFwd, float yaw, float pitch, int index, int patternSeed, float life, float intensityScale, float progress)
    {
        int count = Mathf.Max(0, style.supportSlashCount);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / Mathf.Max(1f, count - 1f);
            float offsetYaw = Mathf.Lerp(-style.supportYawSpread, style.supportYawSpread, t);
            float offsetPitch = Mathf.Lerp(-style.supportPitchSpread, style.supportPitchSpread, Hash01(patternSeed, i + index, 37));
            Quaternion rotation = faceFwd
                * Quaternion.AngleAxis(yaw + offsetYaw, Vector3.up)
                * Quaternion.AngleAxis(pitch + offsetPitch, Vector3.right);
            SpawnSlashInstance(prefab, pos, rotation, life, style.supportThicknessScale, style.supportLengthScale, intensityScale * Mathf.Lerp(0.85f, 1.05f, progress), style.supportAlpha);
        }
    }

    void EmitLightBeams(UltimateSequenceData.VfxSettings style, GameObject prefab, Vector3 pos, Vector3 focusPoint, Quaternion faceFwd, float yaw, float pitch, int index, int patternSeed, float life, float intensityScale, float progress)
    {
        int count = Mathf.Max(0, style.lightBeamCount);
        Vector3 center = focusPoint;
        Vector3 focus = focusPoint + Vector3.up * style.beamFocusHeightOffset;
        Vector3 baseForward = faceFwd * Vector3.forward;
        baseForward.y = 0f;
        if (baseForward.sqrMagnitude <= 0.0001f)
            baseForward = Vector3.forward;
        baseForward.Normalize();

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / Mathf.Max(1f, count - 1f);
            float beamYaw = Mathf.Lerp(-style.beamYawSpread, style.beamYawSpread, t);
            float ringYaw = (360f / Mathf.Max(1, count)) * i + beamYaw + Mathf.Lerp(-12f, 12f, Hash01(patternSeed, i + index, 131));
            float radius = Mathf.Lerp(style.beamSpawnRadiusMin, style.beamSpawnRadiusMax, Hash01(patternSeed, i + index, 149));
            float height = Mathf.Lerp(style.beamSpawnHeightMin, style.beamSpawnHeightMax, Hash01(patternSeed, i + index, 151));
            Vector3 radial = Quaternion.AngleAxis(ringYaw, Vector3.up) * baseForward;
            Vector3 spawnPos = center + radial * radius + Vector3.up * height;
            Quaternion inwardBasis = Quaternion.LookRotation((focus - spawnPos).normalized, Vector3.up);
            float beamPitch = Mathf.Lerp(-style.beamPitchSpread, style.beamPitchSpread, Hash01(patternSeed, i + index, 53));
            Quaternion rotation = inwardBasis * Quaternion.AngleAxis(beamPitch, Vector3.right);
            SpawnSlashInstance(prefab, spawnPos, rotation, life, style.beamThicknessScale, style.beamLengthScale * Mathf.Lerp(1f, 1.18f, progress), intensityScale * Mathf.Lerp(0.7f, 1.15f, progress), style.beamAlpha);
        }
    }

    GameObject ResolveMainSlashPrefab(UltimateSequenceData.VfxSettings style)
    {
        if (style != null && style.dashSlashVfxPrefab != null)
            return style.dashSlashVfxPrefab;
        return slashPrefab;
    }

    Vector3 ResolveSlashUpFromWeapon(Transform activePlayer, Vector3 forward)
    {
        if (!alignSlashRollToObject002)
            return Vector3.up;

        if (forward.sqrMagnitude <= 0.0001f)
            return Vector3.up;

        forward.Normalize();
        Transform weapon = ResolveWeaponTransform(activePlayer);
        Transform grip = activePlayer != null ? activePlayer : player;
        if (weapon == null || !PlayerWeaponVisualUtility.TryGetBladeWorldPose(weapon, grip, out Vector3 bladeBase, out Vector3 bladeTip, out _))
            return Vector3.up;

        Vector3 bladeAxis = Vector3.ProjectOnPlane(bladeTip - bladeBase, forward);
        if (bladeAxis.sqrMagnitude <= 0.0001f)
            return Vector3.up;

        Vector3 bladeUp = bladeAxis.normalized;
        return Vector3.Slerp(Vector3.up, bladeUp, Mathf.Clamp01(object002BladeRollWeight)).normalized;
    }

    Transform ResolveWeaponTransform(Transform activePlayer)
    {
        Transform root = activePlayer != null ? activePlayer : player;
        if (_cachedWeaponTransform != null && _cachedWeaponRoot == root)
            return _cachedWeaponTransform;

        _cachedWeaponRoot = root;
        _cachedWeaponTransform = PlayerWeaponVisualUtility.FindSwordVisualTransform(root);
        if (_cachedWeaponTransform == null && playerReferences != null && playerReferences.VisualRoot != null)
            _cachedWeaponTransform = PlayerWeaponVisualUtility.FindSwordVisualTransform(playerReferences.VisualRoot);

        return _cachedWeaponTransform;
    }

    GameObject ResolveLightBeamPrefab(UltimateSequenceData.VfxSettings style)
    {
        if (style != null)
        {
            if (style.lightBeamVfxPrefab != null)
                return style.lightBeamVfxPrefab;
            if (style.dashSlashVfxPrefab != null)
                return style.dashSlashVfxPrefab;
        }
        return slashPrefab;
    }

    void SpawnSlashInstance(GameObject prefab, Vector3 position, Quaternion rotation, float life, float thicknessScale, float lengthScale, float intensityScale, float alpha)
    {
        var go = TransientVfxPool.Spawn(prefab, position, rotation, null, life);
        if (go == null)
            return;

        go.transform.localScale = new Vector3(thickness * thicknessScale, thickness * thicknessScale, length * lengthScale);

        var playbackCache = go.GetComponent<TransientVfxPlaybackCache>();
        if (playbackCache == null)
            playbackCache = go.AddComponent<TransientVfxPlaybackCache>();
        playbackCache.ApplyUnscaledTime();

        var renderer = playbackCache.PrimaryRenderer;
        if (renderer == null)
            return;

        if (_cachedPropertyBlock == null)
            _cachedPropertyBlock = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(_cachedPropertyBlock);
        if (_propIntensityId != -1) _cachedPropertyBlock.SetFloat(_propIntensityId, baseIntensity * intensityScale);
        if (_propScrollId != -1) _cachedPropertyBlock.SetFloat(_propScrollId, baseScroll);
        if (_propAlphaId != -1) _cachedPropertyBlock.SetFloat(_propAlphaId, alpha);
        renderer.SetPropertyBlock(_cachedPropertyBlock);
    }

    void CacheShaderPropertyIds()
    {
        if (_propIntensityId == -1 && !string.IsNullOrEmpty(propIntensity))
            _propIntensityId = Shader.PropertyToID(propIntensity);
        if (_propScrollId == -1 && !string.IsNullOrEmpty(propScroll))
            _propScrollId = Shader.PropertyToID(propScroll);
        if (_propAlphaId == -1 && !string.IsNullOrEmpty(propAlpha))
            _propAlphaId = Shader.PropertyToID(propAlpha);
    }

    static float Hash01(int seed, int index, int salt)
    {
        uint value = (uint)seed;
        value ^= (uint)(index * 374761393);
        value ^= (uint)(salt * 668265263);
        value = (value ^ (value >> 13)) * 1274126177u;
        value ^= value >> 16;

        return (value & 0x00FFFFFFu) / 16777215f;
    }
}
