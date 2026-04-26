using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class BossAttackVfxPresenter : MonoBehaviour
{
    [Serializable]
    public struct PatternSlashVfxEntry
    {
        public string patternName;
        public string animTriggerName;
        public GameObject vfxPrefab;
        public Vector3 localPositionOffset;
        public Vector3 localEulerOffset;
        public Vector3 localScale;
        public float fallbackLifetime;
    }

    [Header("참조")]
    [SerializeField] BossController bossController;
    [SerializeField] BossReferences bossReferences;
    [SerializeField] Transform spawnAnchor;
    [SerializeField] Transform swordTrailAnchorOverride;

    [Header("기본 효과")]
    [SerializeField] GameObject fallbackVfxPrefab;
    [SerializeField] bool useAnchorRotation = true;
    [SerializeField] bool parentToAnchor = false;
    [SerializeField] float defaultFallbackLifetime = 0.9f;
    [SerializeField] bool useProceduralSwordTrail = true;
    [SerializeField] Material drakkarTrailMaterial;
    [SerializeField] Texture2D drakkarMeleeTrailTexture;
    [SerializeField] Texture2D drakkarWideTrailTexture;

    [Header("패턴별 검기")]
    [SerializeField] PatternSlashVfxEntry[] patternSlashVfx = Array.Empty<PatternSlashVfxEntry>();

    [Header("프로시저럴 검 궤적")]
    [SerializeField] float trailTime = 0.09f;
    [SerializeField] float trailStartWidth = 0.14f;
    [SerializeField] float trailEndWidth = 0.02f;
    [SerializeField] float trailMinVertexDistance = 0.065f;
    [SerializeField] Color trailStartColor = new Color(0.3f, 1f, 1f, 0.95f);
    [SerializeField] Color trailEndColor = new Color(0.15f, 0.7f, 1f, 0f);
    [SerializeField] float swordTipForwardPadding = 0.04f;
    [SerializeField] bool autoSizeTrailFromSword = true;
    [SerializeField] float trailWidthMultiplier = 1.35f;
    [SerializeField] float minimumAutoTrailWidth = 0.32f;
    [SerializeField] float autoTrailEndWidthRatio = 0.24f;

    [Header("디버그")]
    [SerializeField] bool debugLog = false;

    DrakkarSwordTrailDriver _proceduralTrailDriver;
    Material _proceduralTrailMaterial;
    Material _proceduralMeleeTrailMaterial;
    Material _proceduralWideTrailMaterial;
    Transform _resolvedSwordTrailAnchor;
    Transform _resolvedSwordTrailBaseAnchor;
    readonly Dictionary<string, PatternSlashVfxEntry> _entryByPatternName = new Dictionary<string, PatternSlashVfxEntry>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, PatternSlashVfxEntry> _entryByTriggerName = new Dictionary<string, PatternSlashVfxEntry>(StringComparer.OrdinalIgnoreCase);
    bool _patternLookupDirty = true;
    float _cachedStartWidthScale = -1f;
    float _cachedEndWidthScale = -1f;

    public void PlayCurrentPatternSlash()
    {
        Transform swordBaseAnchor = ResolveSwordTrailBaseAnchor();
        Transform swordTipAnchor = ResolveSwordTrailAnchor();
        if (swordBaseAnchor != null && swordTipAnchor != null && useProceduralSwordTrail)
        {
            EnableProceduralTrail(swordBaseAnchor, swordTipAnchor);
            if (debugLog)
                Debug.Log($"[BossAttackVfxPresenter] ribbon on: {swordBaseAnchor.name} -> {swordTipAnchor.name}", this);
            return;
        }

        Transform anchor = ResolveSpawnAnchor();
        if (anchor == null)
            return;
        string patternName = bossController != null ? bossController.CurrentPatternName : string.Empty;
        string triggerName = bossController != null ? bossController.CurrentPatternTriggerName : string.Empty;
        PatternSlashVfxEntry entry = ResolveEntry(patternName, triggerName);
        GameObject prefab = entry.vfxPrefab != null ? entry.vfxPrefab : fallbackVfxPrefab;
        if (prefab == null)
            return;

        Vector3 localScale = IsUsableScale(entry.localScale) ? entry.localScale : Vector3.one;
        Vector3 spawnPosition = anchor.position + anchor.TransformVector(entry.localPositionOffset);
        Quaternion spawnRotation = useAnchorRotation
            ? anchor.rotation * Quaternion.Euler(entry.localEulerOffset)
            : Quaternion.Euler(entry.localEulerOffset);

        GameObject spawned = TransientVfxPool.Spawn(
            prefab,
            spawnPosition,
            spawnRotation,
            parentToAnchor ? anchor : null,
            entry.fallbackLifetime > 0.01f ? entry.fallbackLifetime : defaultFallbackLifetime);

        if (spawned != null)
            spawned.transform.localScale = localScale;

        if (debugLog)
            Debug.Log($"[BossAttackVfxPresenter] pattern={patternName}, trigger={triggerName}, prefab={(prefab != null ? prefab.name : "null")}", this);
    }

    public void StopCurrentPatternSlash()
    {
        if (_proceduralTrailDriver != null)
            _proceduralTrailDriver.End();
    }

    void Awake()
    {
        AutoWire();
        RebuildPatternLookupIfNeeded();
    }

    void OnDestroy()
    {
        if (_proceduralTrailMaterial != null)
            Destroy(_proceduralTrailMaterial);
        if (_proceduralMeleeTrailMaterial != null)
            Destroy(_proceduralMeleeTrailMaterial);
        if (_proceduralWideTrailMaterial != null)
            Destroy(_proceduralWideTrailMaterial);
    }

#if UNITY_EDITOR
    void Reset()
    {
        AutoWire();
        EnsureDefaultMappings(force: true);
        MarkLookupDirty();
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        AutoWire();
        EnsureDefaultMappings(force: false);
        MarkLookupDirty();
    }
#endif

    void AutoWire()
    {
        if (bossController == null)
            bossController = GetComponent<BossController>() ?? GetComponentInParent<BossController>();

        if (bossReferences == null)
            bossReferences = GetComponent<BossReferences>() ?? GetComponentInParent<BossReferences>();

        if (spawnAnchor == null && bossReferences != null)
            spawnAnchor = bossReferences.AttackHitboxSocket != null ? bossReferences.AttackHitboxSocket : bossReferences.VfxPivot;

        if (spawnAnchor == null && bossController != null && bossController.attackHitbox != null)
            spawnAnchor = bossController.attackHitbox.transform;

        if (spawnAnchor == null)
            spawnAnchor = transform;
    }

    Transform ResolveSpawnAnchor()
    {
        if (spawnAnchor != null)
            return spawnAnchor;

        AutoWire();
        return spawnAnchor;
    }

    Transform ResolveSwordTrailAnchor()
    {
        if (swordTrailAnchorOverride != null)
            return swordTrailAnchorOverride;

        if (_resolvedSwordTrailAnchor != null)
            return _resolvedSwordTrailAnchor;

        Transform swordVisual = FindBestSwordVisualTransform();
        if (swordVisual == null)
            swordVisual = ResolveSpawnAnchor();
        if (swordVisual == null)
            return null;

        _resolvedSwordTrailAnchor = CreateSwordTipAnchor(swordVisual);
        return _resolvedSwordTrailAnchor;
    }

    Transform ResolveSwordTrailBaseAnchor()
    {
        if (_resolvedSwordTrailBaseAnchor != null)
            return _resolvedSwordTrailBaseAnchor;

        Transform swordVisual = FindBestSwordVisualTransform();
        if (swordVisual == null)
            swordVisual = ResolveSpawnAnchor();
        if (swordVisual == null)
            return null;

        _resolvedSwordTrailBaseAnchor = CreateSwordBaseAnchor(swordVisual);
        return _resolvedSwordTrailBaseAnchor;
    }

    Transform FindBestSwordVisualTransform()
    {
        Transform socket = bossReferences != null ? bossReferences.AttackHitboxSocket : null;
        Transform searchRoot = bossReferences != null && bossReferences.VisualRoot != null
            ? bossReferences.VisualRoot
            : transform;

        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        Transform best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Transform candidate = renderer.transform;
            if (!TryGetLocalMeshBounds(candidate, out Bounds localBounds))
                continue;

            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("body") || lowerName.Contains("clothes") || lowerName.Contains("face") || lowerName.Contains("hair"))
                continue;

            Vector3 size = localBounds.size;
            float maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float minAxis = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (maxAxis <= 0.05f || minAxis <= 0.0001f)
                continue;

            float slenderness = maxAxis / minAxis;
            if (slenderness < 3f)
                continue;

            float score = slenderness * 10f - size.sqrMagnitude;
            if (socket != null)
                score -= Vector3.Distance(candidate.position, socket.position) * 4f;

            if (lowerName.Contains("sword") || lowerName.Contains("katana") || lowerName.Contains("blade") || lowerName.Contains("weapon"))
                score += 50f;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best != null)
            return best;

        return bossReferences != null ? bossReferences.AttackHitboxSourceVisual : null;
    }

    Transform CreateSwordTipAnchor(Transform swordVisual)
    {
        const string anchorName = "RuntimeBossSwordTrailTip";
        Transform existing = swordVisual.Find(anchorName);
        if (existing != null)
            return existing;

        if (!TryGetLocalMeshBounds(swordVisual, out Bounds localBounds))
            return swordVisual;

        Vector3 size = localBounds.size;
        int axis = 0;
        if (size.y > size.x && size.y >= size.z)
            axis = 1;
        else if (size.z > size.x && size.z >= size.y)
            axis = 2;

        Vector3 localPosition = localBounds.center;
        switch (axis)
        {
            case 1:
                localPosition.y = localBounds.max.y + swordTipForwardPadding;
                break;
            case 2:
                localPosition.z = localBounds.max.z + swordTipForwardPadding;
                break;
            default:
                localPosition.x = localBounds.max.x + swordTipForwardPadding;
                break;
        }

        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(swordVisual, false);
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    Transform CreateSwordBaseAnchor(Transform swordVisual)
    {
        const string anchorName = "RuntimeBossSwordTrailBase";
        Transform existing = swordVisual.Find(anchorName);
        if (existing != null)
            return existing;

        if (!TryGetLocalMeshBounds(swordVisual, out Bounds localBounds))
            return swordVisual;

        Vector3 size = localBounds.size;
        int axis = 0;
        if (size.y > size.x && size.y >= size.z)
            axis = 1;
        else if (size.z > size.x && size.z >= size.y)
            axis = 2;

        Vector3 localPosition = localBounds.center;
        switch (axis)
        {
            case 1:
                localPosition.y = localBounds.min.y - swordTipForwardPadding;
                break;
            case 2:
                localPosition.z = localBounds.min.z - swordTipForwardPadding;
                break;
            default:
                localPosition.x = localBounds.min.x - swordTipForwardPadding;
                break;
        }

        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(swordVisual, false);
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    void EnableProceduralTrail(Transform baseAnchor, Transform tipAnchor)
    {
        EnsureProceduralTrail(baseAnchor);
        if (_proceduralTrailDriver == null)
            return;

        Material trailMaterial = ResolveDrakkarTrailMaterial();
        if (trailMaterial == null)
            return;

        _proceduralTrailDriver.Configure(
            baseAnchor,
            tipAnchor,
            trailMaterial,
            gameObject.layer);
        _proceduralTrailDriver.Begin();
    }

    void ResolveDynamicTrailWidthScales(Transform trailAnchor, out float startWidthScale, out float endWidthScale)
    {
        float baseScale = Mathf.Max(1f, trailStartWidth / Mathf.Max(0.01f, minimumAutoTrailWidth));
        float endScaleFromBase = Mathf.Clamp(baseScale * Mathf.Clamp01(autoTrailEndWidthRatio), 0.02f, baseScale);
        Transform swordTransform = trailAnchor != null && trailAnchor.parent != null ? trailAnchor.parent : trailAnchor;
        if (swordTransform != null && TryGetLocalMeshBounds(swordTransform, out Bounds swordBounds))
        {
            Vector3 scaledSize = Vector3.Scale(swordBounds.size, Abs(swordTransform.lossyScale));
            float swordWorldLength = Mathf.Max(scaledSize.x, Mathf.Max(scaledSize.y, scaledSize.z));
            if (autoSizeTrailFromSword && swordWorldLength > 0.001f)
            {
                float autoWidth = Mathf.Max(minimumAutoTrailWidth, swordWorldLength * trailWidthMultiplier);
                startWidthScale = Mathf.Max(baseScale, autoWidth / Mathf.Max(0.01f, swordWorldLength));
                float worldEndWidth = Mathf.Max(trailEndWidth, autoWidth * Mathf.Clamp01(autoTrailEndWidthRatio));
                endWidthScale = Mathf.Clamp(worldEndWidth / Mathf.Max(0.01f, swordWorldLength), 0.02f, startWidthScale);
                return;
            }
        }

        if (bossController != null && bossController.attackHitbox != null && bossController.attackHitbox.Collider != null)
        {
            Bounds bounds = bossController.attackHitbox.Collider.bounds;
            float hitboxLength = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (hitboxLength > 0.001f)
            {
                float autoWidth = Mathf.Max(minimumAutoTrailWidth, hitboxLength * trailWidthMultiplier);
                startWidthScale = Mathf.Max(baseScale, autoWidth / Mathf.Max(0.01f, hitboxLength));
                float worldEndWidth = Mathf.Max(trailEndWidth, autoWidth * Mathf.Clamp01(autoTrailEndWidthRatio));
                endWidthScale = Mathf.Clamp(worldEndWidth / Mathf.Max(0.01f, hitboxLength), 0.02f, startWidthScale);
                return;
            }
        }

        startWidthScale = baseScale;
        endWidthScale = endScaleFromBase;
    }

    static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    void EnsureProceduralTrail(Transform trailAnchor)
    {
        if (_proceduralTrailDriver != null || trailAnchor == null)
            return;

        GameObject trailObject = new GameObject("RuntimeBossAttackRibbon");
        trailObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        trailObject.transform.localScale = Vector3.one;

        _proceduralTrailDriver = trailObject.AddComponent<DrakkarSwordTrailDriver>();
    }

    Material ResolveDrakkarTrailMaterial()
    {
        if (drakkarTrailMaterial != null)
            return drakkarTrailMaterial;

#if UNITY_EDITOR
        string triggerName = bossController != null ? bossController.CurrentPatternTriggerName : string.Empty;
        string patternName = bossController != null ? bossController.CurrentPatternName : string.Empty;
        bool useWideMaterial = string.Equals(triggerName, "Attack_F", StringComparison.OrdinalIgnoreCase)
            || string.Equals(patternName, "SwordWave", StringComparison.OrdinalIgnoreCase);

        string materialPath = useWideMaterial
            ? "Assets/Effects/Trail/MAT_BossTrail_Wide.mat"
            : "Assets/Effects/Trail/MAT_BossTrail_Melee.mat";
        Material authoredMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (authoredMaterial != null)
            return authoredMaterial;
#endif

        EnsureDefaultDrakkarTextures();

        string runtimeTriggerName = bossController != null ? bossController.CurrentPatternTriggerName : string.Empty;
        string runtimePatternName = bossController != null ? bossController.CurrentPatternName : string.Empty;
        bool useWide = string.Equals(runtimeTriggerName, "Attack_F", StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtimePatternName, "SwordWave", StringComparison.OrdinalIgnoreCase);

        if (useWide && drakkarWideTrailTexture != null)
        {
            _proceduralWideTrailMaterial = GetOrCreateDrakkarTrailMaterial(
                _proceduralWideTrailMaterial,
                drakkarWideTrailTexture,
                "BossAttackTrailWideRuntime");
            return _proceduralWideTrailMaterial;
        }

        if (drakkarMeleeTrailTexture != null)
        {
            _proceduralMeleeTrailMaterial = GetOrCreateDrakkarTrailMaterial(
                _proceduralMeleeTrailMaterial,
                drakkarMeleeTrailTexture,
                "BossAttackTrailMeleeRuntime");
            return _proceduralMeleeTrailMaterial;
        }

#if UNITY_EDITOR
        _proceduralTrailMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Drakkar/GameUtils/VISUALS/Trails/DrakkarTrails Examples/Assets/Trail Blue Alpha.mat");
#endif

        if (_proceduralTrailMaterial == null)
        {
            Shader shader = Shader.Find("Trail Shader Alpha") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _proceduralTrailMaterial = new Material(shader);
            _proceduralTrailMaterial.name = "BossAttackTrailRuntime";
            _proceduralTrailMaterial.hideFlags = HideFlags.HideAndDontSave;

            ConfigureDrakkarTrailMaterial(_proceduralTrailMaterial, Texture2D.whiteTexture);
        }

        return _proceduralTrailMaterial;
    }

    Material GetOrCreateDrakkarTrailMaterial(Material current, Texture2D texture, string materialName)
    {
        if (current != null)
            return current;

        Shader shader = Shader.Find("Trail Shader Alpha") ?? Shader.Find("Sprites/Default");
        if (shader == null || texture == null)
            return null;

        current = new Material(shader);
        current.name = materialName;
        current.hideFlags = HideFlags.HideAndDontSave;
        ConfigureDrakkarTrailMaterial(current, texture);
        return current;
    }

    void ConfigureDrakkarTrailMaterial(Material material, Texture texture)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Color1"))
            material.SetColor("_Color1", ForceVisibleTrailAlpha(trailStartColor));
        if (material.HasProperty("_Color2"))
            material.SetColor("_Color2", ForceVisibleTrailAlpha(trailEndColor));
        if (material.HasProperty("_Texture"))
            material.SetTexture("_Texture", texture);
        if (material.HasProperty("_Power"))
            material.SetFloat("_Power", 8f);
    }

    static Color ForceVisibleTrailAlpha(Color color)
    {
        color.a = 1f;
        return color;
    }

    void EnsureDefaultDrakkarTextures()
    {
#if UNITY_EDITOR
        if (drakkarMeleeTrailTexture == null)
            drakkarMeleeTrailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Effects/Trail/trail_player_heavy.png");
        if (drakkarWideTrailTexture == null)
            drakkarWideTrailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Effects/Trail/trail_ultimate_wide.png");
#endif
    }

    static bool TryGetLocalMeshBounds(Transform target, out Bounds bounds)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    PatternSlashVfxEntry ResolveEntry(string patternName, string triggerName)
    {
        RebuildPatternLookupIfNeeded();

        if (_entryByPatternName.Count == 0 && _entryByTriggerName.Count == 0)
            return default;

        if (!string.IsNullOrWhiteSpace(patternName) && _entryByPatternName.TryGetValue(patternName, out PatternSlashVfxEntry byPattern))
            return byPattern;

        if (!string.IsNullOrWhiteSpace(triggerName) && _entryByTriggerName.TryGetValue(triggerName, out PatternSlashVfxEntry byTrigger))
            return byTrigger;

        return default;
    }

    void MarkLookupDirty()
    {
        _patternLookupDirty = true;
        _cachedStartWidthScale = -1f;
        _cachedEndWidthScale = -1f;
    }

    void RebuildPatternLookupIfNeeded()
    {
        if (!_patternLookupDirty)
            return;

        _entryByPatternName.Clear();
        _entryByTriggerName.Clear();

        if (patternSlashVfx != null)
        {
            for (int i = 0; i < patternSlashVfx.Length; i++)
            {
                PatternSlashVfxEntry entry = patternSlashVfx[i];

                if (!string.IsNullOrWhiteSpace(entry.patternName) && !_entryByPatternName.ContainsKey(entry.patternName))
                    _entryByPatternName.Add(entry.patternName, entry);

                if (!string.IsNullOrWhiteSpace(entry.animTriggerName) && !_entryByTriggerName.ContainsKey(entry.animTriggerName))
                    _entryByTriggerName.Add(entry.animTriggerName, entry);
            }
        }

        _patternLookupDirty = false;
    }

    static bool IsUsableScale(Vector3 scale)
    {
        return scale.x > 0.001f && scale.y > 0.001f && scale.z > 0.001f;
    }

#if UNITY_EDITOR
    void EnsureDefaultMappings(bool force)
    {
        if (!force && patternSlashVfx != null && patternSlashVfx.Length > 0)
            return;

        fallbackVfxPrefab = null;

        patternSlashVfx = new[]
        {
            CreateDefaultEntry("a", "Attack_A", null, 1.00f, 0.75f),
            CreateDefaultEntry("b", "Attack_B", null, 1.05f, 0.80f),
            CreateDefaultEntry("c", "Attack_C", null, 1.18f, 0.95f),
            CreateDefaultEntry("d", "Attack_D", null, 1.12f, 0.90f),
            CreateDefaultEntry("e", "Attack_E", null, 1.00f, 0.80f),
            CreateDefaultEntry("f", "Attack_F", null, 1.16f, 0.92f),
        };

        EditorUtility.SetDirty(this);
    }

    static PatternSlashVfxEntry CreateDefaultEntry(string patternName, string triggerName, GameObject prefab, float scale, float lifetime)
    {
        return new PatternSlashVfxEntry
        {
            patternName = patternName,
            animTriggerName = triggerName,
            vfxPrefab = prefab,
            localPositionOffset = new Vector3(0f, 0.05f, 0f),
            localEulerOffset = Vector3.zero,
            localScale = new Vector3(scale, scale, scale),
            fallbackLifetime = lifetime
        };
    }
#endif
}
