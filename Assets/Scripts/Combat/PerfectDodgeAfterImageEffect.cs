using System.Collections;
using System.Collections.Generic;
using JuiceBits;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class PerfectDodgeAfterImageEffect : MonoBehaviour
{
    sealed class RuntimeMaterialSet
    {
        public Material[] Materials;
        public Color BaseColor;
    }

    sealed class ActiveSnapshot
    {
        public GameObject Root;
        public List<Mesh> SpawnedMeshes;
        public List<MaterialState> MaterialStates;
        public float AlphaMultiplier;
        public float Lifetime;
        public float Elapsed;
    }

    [SerializeField] private PlayerReferences playerReferences;
    [SerializeField] private CharacterController characterController;

    [Header("Burst")]
    [SerializeField, Min(1)] private int imageCount = 7;
    [SerializeField, Min(0f)] private float spawnIntervalRealtime = 0.02f;
    [SerializeField, Min(0.01f)] private float imageLifetimeRealtime = 0.24f;
    [SerializeField] private bool useRealtime = true;
    [SerializeField, Min(0f)] private float minReplayIntervalRealtime = 0.24f;
    [SerializeField, Min(1)] private int maxConcurrentSnapshots = 8;
    [SerializeField] private bool adaptiveBurstCount = true;
    [SerializeField, Min(1)] private int minAdaptiveImageCount = 3;
    [SerializeField, Min(15f)] private float adaptiveFpsThreshold = 60f;
    [SerializeField, Min(10f)] private float criticalAdaptiveFpsThreshold = 45f;
    [SerializeField, Min(10f)] private float suspendBelowFpsThreshold = 30f;
    [SerializeField, Min(1)] private int criticalAdaptiveImageCount = 1;
    [SerializeField, Min(1)] private int criticalMaxSkinnedRenderers = 1;
    [SerializeField, Min(1)] private int maxNormalSkinnedRenderers = 2;
    [SerializeField, Min(0)] private int maxNormalStaticRenderers = 1;
    [SerializeField, Min(0)] private int maxAdaptiveStaticRenderers = 0;
    [SerializeField] private bool skipStaticSnapshotsWhenCritical = true;
    [SerializeField, Range(0.35f, 1f)] private float criticalLifetimeScale = 0.55f;
    [SerializeField, Range(1f, 2f)] private float criticalSpawnIntervalScale = 1.6f;

    [Header("Look")]
    [SerializeField] private Color afterImageTint = new Color(0.55f, 1f, 1f, 0.5f);
    [SerializeField, Range(0.5f, 2f)] private float colorBlend = 0.8f;
    [SerializeField, Range(1f, 1.2f)] private float startScaleMultiplier = 1.01f;
    [SerializeField, Range(1f, 1.3f)] private float endScaleMultiplier = 1.08f;
    [SerializeField] private EaseTypes fadeEase = EaseTypes.EaseOutCubic;

    [Header("Trail")]
    [SerializeField] private bool arrangeAlongMovement = true;
    [SerializeField, Min(0f)] private float trailLength = 2f;
    [SerializeField, Range(0.05f, 1f)] private float tailAlphaMultiplier = 0.16f;
    [SerializeField] private AnimationCurve trailDistribution = null;

    [Header("Render")]
    [SerializeField] private bool includeInactiveRenderers = false;
    [SerializeField] private bool disableShadows = true;
    [SerializeField] private int renderQueue = 3000;

    Transform _cachedVisualRoot;
    float _lastPlayRealtime = float.NegativeInfinity;
    float _smoothedRealtimeDelta = 1f / 60f;
    Transform _scheduledBurstVisualRoot;
    int _scheduledBurstCount;
    int _scheduledBurstSpawned;
    float _scheduledBurstFps;
    float _scheduledBurstNextAt;
    bool _scheduledBurstCriticalMode;

    readonly List<SkinnedMeshRenderer> _skinnedRenderers = new();
    readonly List<MeshRenderer> _meshRenderers = new();
    readonly Dictionary<Renderer, RuntimeMaterialSet> _runtimeMaterialSets = new();
    readonly List<Material> _ownedRuntimeMaterials = new();
    readonly List<ActiveSnapshot> _activeSnapshots = new();
    readonly Stack<List<Mesh>> _meshListPool = new();
    readonly Stack<List<MaterialState>> _materialStateListPool = new();
    readonly Stack<ActiveSnapshot> _snapshotPool = new();

    void Awake()
    {
        ResolveReferences();

        if (trailDistribution == null || trailDistribution.length == 0)
            trailDistribution = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        enabled = false;
    }

    void OnDestroy()
    {
        ClearActiveSnapshots();
        ClearRuntimeMaterialCache();
    }

    void LateUpdate()
    {
        if (_scheduledBurstCount > 0)
            UpdateScheduledBurst();

        if (_activeSnapshots.Count == 0)
        {
            if (_scheduledBurstCount == 0 && enabled)
                enabled = false;
            return;
        }

        float deltaTime = useRealtime ? Time.unscaledDeltaTime : Time.deltaTime;
        UpdateActiveSnapshots(Mathf.Max(0.0001f, deltaTime));
    }

    public void Play()
    {
        ResolveReferences();
        _smoothedRealtimeDelta = Mathf.Lerp(_smoothedRealtimeDelta, Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.12f);
        float smoothedFps = 1f / Mathf.Max(0.0001f, _smoothedRealtimeDelta);
        if (adaptiveBurstCount && smoothedFps < suspendBelowFpsThreshold)
            return;

        if (Time.realtimeSinceStartup - _lastPlayRealtime < minReplayIntervalRealtime)
            return;

        Transform visualRoot = GetVisualRoot();
        if (!PrepareSourceRenderers(visualRoot))
            return;

        enabled = true;
        _lastPlayRealtime = Time.realtimeSinceStartup;
        bool criticalMode = adaptiveBurstCount && smoothedFps < criticalAdaptiveFpsThreshold;
        ScheduleBurst(visualRoot, ResolveBurstImageCount(smoothedFps), smoothedFps, criticalMode);
        UpdateScheduledBurst();
    }

    void ResolveReferences()
    {
        if (playerReferences == null)
            playerReferences = GetComponent<PlayerReferences>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();
    }

    void ScheduleBurst(Transform visualRoot, int burstCount, float smoothedFps, bool criticalMode)
    {
        _scheduledBurstVisualRoot = visualRoot;
        _scheduledBurstCount = Mathf.Max(0, burstCount);
        _scheduledBurstSpawned = 0;
        _scheduledBurstFps = smoothedFps;
        _scheduledBurstCriticalMode = criticalMode;
        _scheduledBurstNextAt = GetBurstClockTime();
    }

    void UpdateScheduledBurst()
    {
        if (_scheduledBurstCount <= 0 || _scheduledBurstVisualRoot == null)
        {
            ClearScheduledBurst();
            return;
        }

        float effectiveInterval = ResolveEffectiveSpawnInterval(_scheduledBurstCriticalMode);
        float now = GetBurstClockTime();

        while (_scheduledBurstSpawned < _scheduledBurstCount)
        {
            if (now + 0.0001f < _scheduledBurstNextAt)
                break;

            SpawnSnapshot(
                _scheduledBurstVisualRoot,
                _scheduledBurstSpawned,
                _scheduledBurstCount,
                _scheduledBurstFps,
                _scheduledBurstCriticalMode);

            _scheduledBurstSpawned++;
            if (_scheduledBurstSpawned >= _scheduledBurstCount)
            {
                ClearScheduledBurst();
                break;
            }

            if (effectiveInterval <= 0f)
                continue;

            _scheduledBurstNextAt += effectiveInterval;
        }
    }

    float ResolveEffectiveSpawnInterval(bool criticalMode)
    {
        return criticalMode
            ? spawnIntervalRealtime * Mathf.Max(1f, criticalSpawnIntervalScale)
            : spawnIntervalRealtime;
    }

    float GetBurstClockTime()
    {
        return useRealtime ? Time.unscaledTime : Time.time;
    }

    void ClearScheduledBurst()
    {
        _scheduledBurstVisualRoot = null;
        _scheduledBurstCount = 0;
        _scheduledBurstSpawned = 0;
        _scheduledBurstFps = 0f;
        _scheduledBurstNextAt = 0f;
        _scheduledBurstCriticalMode = false;
    }

    void SpawnSnapshot(Transform visualRoot, int burstIndex, int burstCount, float smoothedFps, bool criticalMode)
    {
        if (visualRoot == null)
            return;

        if (_skinnedRenderers.Count == 0 && _meshRenderers.Count == 0)
            return;

        if (criticalMode && _activeSnapshots.Count >= Mathf.Max(1, maxConcurrentSnapshots / 2))
            return;

        TrimActiveSnapshots();

        GameObject snapshotRoot = new GameObject("PerfectDodgeAfterImage");
        snapshotRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        snapshotRoot.transform.localScale = Vector3.one;

        List<Mesh> spawnedMeshes = AcquireMeshList();
        List<MaterialState> materialStates = AcquireMaterialStateList();
        float trailProgress = burstCount <= 1 ? 0f : (float)burstIndex / (burstCount - 1);
        float distribution = trailDistribution != null ? Mathf.Clamp01(trailDistribution.Evaluate(trailProgress)) : trailProgress;
        Vector3 trailOffset = arrangeAlongMovement ? ResolveTrailOffset(distribution) : Vector3.zero;
        float alphaMultiplier = Mathf.Lerp(1f, tailAlphaMultiplier, distribution);
        int maxSkinnedRenderers = Mathf.Max(1, maxNormalSkinnedRenderers);
        if (criticalMode)
            maxSkinnedRenderers = Mathf.Min(maxSkinnedRenderers, Mathf.Max(1, criticalMaxSkinnedRenderers));
        else if (adaptiveBurstCount && smoothedFps < adaptiveFpsThreshold)
            maxSkinnedRenderers = Mathf.Max(1, maxSkinnedRenderers - 1);

        int skinnedCount = Mathf.Min(_skinnedRenderers.Count, maxSkinnedRenderers);

        for (int i = 0; i < skinnedCount; i++)
            CreateSkinnedSnapshot(_skinnedRenderers[i], snapshotRoot.transform, spawnedMeshes, materialStates, alphaMultiplier);

        bool allowStaticSnapshots = !(criticalMode && skipStaticSnapshotsWhenCritical);
        int maxStaticRenderers = maxNormalStaticRenderers;
        if (adaptiveBurstCount && smoothedFps < adaptiveFpsThreshold)
            maxStaticRenderers = maxAdaptiveStaticRenderers;

        if (allowStaticSnapshots && maxStaticRenderers > 0)
        {
            int staticCount = Mathf.Min(_meshRenderers.Count, maxStaticRenderers);
            for (int i = 0; i < staticCount; i++)
                CreateStaticSnapshot(_meshRenderers[i], snapshotRoot.transform, materialStates, alphaMultiplier);
        }

        if (materialStates.Count == 0)
        {
            CleanupSnapshot(snapshotRoot, spawnedMeshes, materialStates);
            return;
        }

        if (trailOffset != Vector3.zero)
            snapshotRoot.transform.position += trailOffset;

        float lifetime = criticalMode
            ? imageLifetimeRealtime * Mathf.Clamp(criticalLifetimeScale, 0.35f, 1f)
            : imageLifetimeRealtime;
        RegisterSnapshot(snapshotRoot, spawnedMeshes, materialStates, alphaMultiplier, lifetime);
    }

    bool PrepareSourceRenderers(Transform visualRoot)
    {
        if (visualRoot == null)
            return false;

        if (_cachedVisualRoot != visualRoot || (_skinnedRenderers.Count == 0 && _meshRenderers.Count == 0))
            RebuildSourceRenderers(visualRoot);

        return _skinnedRenderers.Count > 0 || _meshRenderers.Count > 0;
    }

    void RebuildSourceRenderers(Transform visualRoot)
    {
        _cachedVisualRoot = visualRoot;
        _skinnedRenderers.Clear();
        _meshRenderers.Clear();
        ClearRuntimeMaterialCache();

        SkinnedMeshRenderer[] skinned = visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactiveRenderers);
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] == null || !skinned[i].enabled || skinned[i].sharedMesh == null)
                continue;

            _skinnedRenderers.Add(skinned[i]);
        }

        MeshRenderer[] meshes = visualRoot.GetComponentsInChildren<MeshRenderer>(includeInactiveRenderers);
        for (int i = 0; i < meshes.Length; i++)
        {
            MeshRenderer meshRenderer = meshes[i];
            if (meshRenderer == null || !meshRenderer.enabled)
                continue;

            if (meshRenderer.GetComponent<SkinnedMeshRenderer>() != null)
                continue;

            if (meshRenderer.GetComponent<MeshFilter>() == null)
                continue;

            _meshRenderers.Add(meshRenderer);
        }
    }

    void CreateSkinnedSnapshot(
        SkinnedMeshRenderer source,
        Transform parent,
        List<Mesh> spawnedMeshes,
        List<MaterialState> materialStates,
        float alphaMultiplier)
    {
        Mesh bakedMesh = new Mesh
        {
            name = $"{source.name}_AfterImageMesh"
        };
        source.BakeMesh(bakedMesh, true);
        spawnedMeshes.Add(bakedMesh);

        GameObject child = new GameObject(source.name);
        child.transform.SetParent(parent, false);
        child.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        child.transform.localScale = Vector3.Scale(source.transform.lossyScale, Vector3.one * startScaleMultiplier);

        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = bakedMesh;

        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        ConfigureRendererSnapshot(renderer, source, materialStates, alphaMultiplier);
    }

    void CreateStaticSnapshot(
        MeshRenderer source,
        Transform parent,
        List<MaterialState> materialStates,
        float alphaMultiplier)
    {
        MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null)
            return;

        GameObject child = new GameObject(source.name);
        child.transform.SetParent(parent, false);
        child.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        child.transform.localScale = Vector3.Scale(source.transform.lossyScale, Vector3.one * startScaleMultiplier);

        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = sourceFilter.sharedMesh;

        MeshRenderer renderer = child.AddComponent<MeshRenderer>();
        ConfigureRendererSnapshot(renderer, source, materialStates, alphaMultiplier);
    }

    void ConfigureRendererSnapshot(
        MeshRenderer renderer,
        Renderer sourceRenderer,
        List<MaterialState> materialStates,
        float alphaMultiplier)
    {
        if (renderer == null)
            return;

        RuntimeMaterialSet materialSet = GetOrCreateMaterialSet(sourceRenderer);
        renderer.sharedMaterials = materialSet.Materials;
        if (disableShadows)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        ApplyRendererColor(renderer, block, ScaleAlpha(materialSet.BaseColor, alphaMultiplier));
        materialStates.Add(new MaterialState(renderer, block, materialSet.BaseColor));
    }

    void RegisterSnapshot(
        GameObject snapshotRoot,
        List<Mesh> spawnedMeshes,
        List<MaterialState> materialStates,
        float alphaMultiplier,
        float lifetime)
    {
        ActiveSnapshot snapshot = AcquireSnapshot();
        snapshot.Root = snapshotRoot;
        snapshot.SpawnedMeshes = spawnedMeshes;
        snapshot.MaterialStates = materialStates;
        snapshot.AlphaMultiplier = alphaMultiplier;
        snapshot.Lifetime = Mathf.Max(0.05f, lifetime);
        snapshot.Elapsed = 0f;
        _activeSnapshots.Add(snapshot);
        enabled = true;
    }

    void UpdateActiveSnapshots(float deltaTime)
    {
        for (int i = _activeSnapshots.Count - 1; i >= 0; i--)
        {
            ActiveSnapshot snapshot = _activeSnapshots[i];
            snapshot.Elapsed += deltaTime;

            float normalized = Mathf.Clamp01(snapshot.Elapsed / snapshot.Lifetime);
            float eased = Mathf.Clamp01(EaseManger.Easings(fadeEase, normalized));
            float alphaFactor = 1f - eased;
            float scaleMultiplier = Mathf.Lerp(startScaleMultiplier, endScaleMultiplier, eased);

            if (snapshot.Root != null)
                snapshot.Root.transform.localScale = Vector3.one * scaleMultiplier;

            List<MaterialState> materialStates = snapshot.MaterialStates;
            for (int j = 0; j < materialStates.Count; j++)
            {
                MaterialState state = materialStates[j];
                if (state.Renderer == null)
                    continue;

                Color color = ScaleAlpha(state.BaseColor, alphaFactor * snapshot.AlphaMultiplier);
                ApplyRendererColor(state.Renderer, state.Block, color);
            }

            if (snapshot.Elapsed >= snapshot.Lifetime)
                ReleaseSnapshotAt(i);
        }
    }

    void CleanupSnapshot(GameObject snapshotRoot, List<Mesh> spawnedMeshes, List<MaterialState> materialStates)
    {
        for (int i = 0; i < spawnedMeshes.Count; i++)
        {
            if (spawnedMeshes[i] != null)
                Destroy(spawnedMeshes[i]);
        }
        if (snapshotRoot != null)
            Destroy(snapshotRoot);

        ReturnMeshList(spawnedMeshes);
        ReturnMaterialStateList(materialStates);
    }

    Transform GetVisualRoot()
    {
        if (playerReferences == null)
            return transform;

        if (playerReferences.VisualRoot != null)
            return playerReferences.VisualRoot;

        if (playerReferences.PlayerRoot != null)
            return playerReferences.PlayerRoot;

        return transform;
    }

    Vector3 ResolveTrailOffset(float trailProgress)
    {
        Transform root = playerReferences != null && playerReferences.PlayerRoot != null
            ? playerReferences.PlayerRoot
            : transform;

        Vector3 direction = ResolveTrailDirection(root);
        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        return -direction * trailLength * Mathf.Clamp01(trailProgress);
    }

    Vector3 ResolveTrailDirection(Transform root)
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (characterController != null)
        {
            Vector3 velocity = characterController.velocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.0001f)
                return velocity.normalized;
        }

        if (root != null)
        {
            Vector3 forward = root.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                return forward.normalized;
        }

        return Vector3.forward;
    }

    RuntimeMaterialSet GetOrCreateMaterialSet(Renderer sourceRenderer)
    {
        if (sourceRenderer != null && _runtimeMaterialSets.TryGetValue(sourceRenderer, out RuntimeMaterialSet existing))
            return existing;

        Material[] sourceMaterials = sourceRenderer != null ? sourceRenderer.sharedMaterials : null;
        int materialCount = sourceMaterials != null && sourceMaterials.Length > 0 ? sourceMaterials.Length : 1;
        Material[] runtimeMaterials = new Material[materialCount];

        Color baseColor = afterImageTint;
        baseColor.a = afterImageTint.a;

        for (int i = 0; i < materialCount; i++)
        {
            Material sourceMaterial = sourceMaterials != null && i < sourceMaterials.Length
                ? sourceMaterials[i]
                : null;
            Material runtimeMaterial = CreateRuntimeMaterial(sourceMaterial);
            PrepareTransparentMaterial(runtimeMaterial);
            runtimeMaterials[i] = runtimeMaterial;
            _ownedRuntimeMaterials.Add(runtimeMaterial);

            if (i == 0)
            {
                Color resolvedBase = ResolveBaseColor(sourceMaterial, Color.white);
                baseColor = Color.Lerp(resolvedBase, afterImageTint, Mathf.Clamp01(colorBlend));
                baseColor.a = afterImageTint.a;
            }
        }

        RuntimeMaterialSet materialSet = new RuntimeMaterialSet
        {
            Materials = runtimeMaterials,
            BaseColor = baseColor
        };

        if (sourceRenderer != null)
            _runtimeMaterialSets[sourceRenderer] = materialSet;

        return materialSet;
    }

    void ClearRuntimeMaterialCache()
    {
        for (int i = 0; i < _ownedRuntimeMaterials.Count; i++)
        {
            if (_ownedRuntimeMaterials[i] != null)
                Destroy(_ownedRuntimeMaterials[i]);
        }

        _ownedRuntimeMaterials.Clear();
        _runtimeMaterialSets.Clear();
    }

    static Color ResolveBaseColor(Material material, Color fallback)
    {
        if (material == null)
            return fallback;

        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.color;

        return fallback;
    }

    void PrepareTransparentMaterial(Material material)
    {
        if (material == null)
            return;

        material.renderQueue = renderQueue;
        material.enableInstancing = true;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    static void ApplyRendererColor(Renderer renderer, MaterialPropertyBlock block, Color color)
    {
        if (renderer == null || block == null)
            return;

        block.Clear();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    static Color ScaleAlpha(Color color, float alphaMultiplier)
    {
        color.a *= Mathf.Clamp01(alphaMultiplier);
        return color;
    }

    static Material CreateRuntimeMaterial(Material source)
    {
        if (source != null)
            return new Material(source);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        return new Material(shader);
    }

    int ResolveBurstImageCount(float fps)
    {
        int resolved = Mathf.Max(1, imageCount);
        if (!adaptiveBurstCount)
            return resolved;

        if (fps >= adaptiveFpsThreshold)
            return resolved;

        if (fps < criticalAdaptiveFpsThreshold)
            return Mathf.Clamp(criticalAdaptiveImageCount, 1, resolved);

        float ratio = Mathf.Clamp01(fps / Mathf.Max(1f, adaptiveFpsThreshold));
        int adaptiveCount = Mathf.RoundToInt(Mathf.Lerp(minAdaptiveImageCount, resolved, ratio));
        return Mathf.Clamp(adaptiveCount, minAdaptiveImageCount, resolved);
    }

    void TrimActiveSnapshots()
    {
        int overflow = (_activeSnapshots.Count + 1) - Mathf.Max(1, maxConcurrentSnapshots);
        for (int i = 0; i < overflow; i++)
        {
            if (_activeSnapshots.Count == 0)
                break;

            ReleaseSnapshotAt(0);
        }
    }

    void ClearActiveSnapshots()
    {
        for (int i = _activeSnapshots.Count - 1; i >= 0; i--)
            ReleaseSnapshotAt(i);
    }

    void ReleaseSnapshotAt(int index)
    {
        ActiveSnapshot snapshot = _activeSnapshots[index];
        _activeSnapshots.RemoveAt(index);
        CleanupSnapshot(snapshot.Root, snapshot.SpawnedMeshes, snapshot.MaterialStates);
        snapshot.Root = null;
        snapshot.SpawnedMeshes = null;
        snapshot.MaterialStates = null;
        snapshot.AlphaMultiplier = 0f;
        snapshot.Lifetime = 0f;
        snapshot.Elapsed = 0f;
        _snapshotPool.Push(snapshot);
    }

    ActiveSnapshot AcquireSnapshot()
    {
        return _snapshotPool.Count > 0 ? _snapshotPool.Pop() : new ActiveSnapshot();
    }

    List<Mesh> AcquireMeshList()
    {
        List<Mesh> list = _meshListPool.Count > 0 ? _meshListPool.Pop() : new List<Mesh>(_skinnedRenderers.Count);
        list.Clear();
        return list;
    }

    void ReturnMeshList(List<Mesh> list)
    {
        if (list == null)
            return;

        list.Clear();
        _meshListPool.Push(list);
    }

    List<MaterialState> AcquireMaterialStateList()
    {
        List<MaterialState> list = _materialStateListPool.Count > 0
            ? _materialStateListPool.Pop()
            : new List<MaterialState>(_skinnedRenderers.Count + _meshRenderers.Count);
        list.Clear();
        return list;
    }

    void ReturnMaterialStateList(List<MaterialState> list)
    {
        if (list == null)
            return;

        list.Clear();
        _materialStateListPool.Push(list);
    }

    readonly struct MaterialState
    {
        public MaterialState(Renderer renderer, MaterialPropertyBlock block, Color baseColor)
        {
            Renderer = renderer;
            Block = block;
            BaseColor = baseColor;
        }

        public Renderer Renderer { get; }
        public MaterialPropertyBlock Block { get; }
        public Color BaseColor { get; }
    }
}
