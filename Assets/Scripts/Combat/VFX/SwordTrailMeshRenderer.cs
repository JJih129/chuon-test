using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Generates a mesh ribbon from the real sword motion.
/// Add Animation Events only for StartTrail and StopTrail timing; direction is inferred
/// automatically from SwordTrailPoints.TrailBase and SwordTrailPoints.TrailTip movement.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class SwordTrailMeshRenderer : MonoBehaviour
{
    const int MinimumSampleCount = 2;
    const int MaximumSampleCount = 128;

    struct TrailSample
    {
        public Vector3 BasePosition;
        public Vector3 TipPosition;
        public float Time;
    }

    [Tooltip("Component that provides the sword grip-side and tip-side trail points.")]
    [SerializeField] SwordTrailPoints trailPoints;

    [Tooltip("If enabled, trail sampling starts automatically when this object is enabled.")]
    [SerializeField] bool playOnEnable;

    [Tooltip("Minimum world-space movement required before adding a new trail sample.")]
    [SerializeField, Min(0.001f)] float minSampleDistance = 0.025f;

    [Tooltip("How long each trail sample remains visible after it is created.")]
    [SerializeField, Min(0.01f)] float trailLifeTime = 0.18f;

    [Tooltip("Maximum number of samples kept in the ring buffer. Higher values look smoother but cost more.")]
    [SerializeField, Range(MinimumSampleCount, MaximumSampleCount)] int maxSamples = 32;

    [Tooltip("If enabled, StopTrail clears the mesh immediately instead of letting it fade by lifetime.")]
    [SerializeField] bool clearOnStop;

    [Tooltip("If enabled, trail timing ignores Time.timeScale.")]
    [SerializeField] bool useUnscaledTime;

    [Tooltip("If enabled, triangles are generated for both sides so the trail is visible with backface-culling materials.")]
    [SerializeField] bool generateDoubleSided = true;

    [Tooltip("If enabled, the MeshRenderer is disabled whenever there is no visible trail mesh.")]
    [SerializeField] bool disableRendererWhenEmpty = true;

    [Tooltip("If enabled, selected-object gizmos show the currently buffered trail samples.")]
    [SerializeField] bool drawDebugGizmos = true;

    [Tooltip("Visual multiplier applied to the TrailBase/TrailTip span. Increase this if the slash reads too thin.")]
    [SerializeField, Min(0.1f)] float visualWidthMultiplier = 1.35f;

    [Tooltip("Minimum rendered world-space span between the visual trail edges.")]
    [SerializeField, Min(0.01f)] float minimumVisualWidth = 0.75f;

    [Tooltip("How much the rendered trail edge direction is blended toward the camera plane to avoid edge-on one-pixel lines.")]
    [SerializeField, Range(0f, 1f)] float cameraFacingBlend = 0.65f;

    [Tooltip("Material used by the generated sword trail mesh. Usually extracted from an authored Free Slash VFX prefab.")]
    [SerializeField] Material trailMaterial;

    [Tooltip("Tint applied through vertex colors and common shader color properties.")]
    [SerializeField] Color trailTint = Color.white;

    [Tooltip("If enabled, common shader color properties are updated with the trail tint through a reused MaterialPropertyBlock.")]
    [SerializeField] bool applyTintPropertyBlock = true;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int Color1Id = Shader.PropertyToID("_Color1");
    static readonly int Color2Id = Shader.PropertyToID("_Color2");

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Mesh trailMesh;
    MaterialPropertyBlock materialPropertyBlock;
    TrailSample[] samples;
    Vector3[] vertices;
    Vector2[] uvs;
    Color32[] colors;
    int[] triangles;
    int sampleStartIndex;
    int sampleCount;
    int lastVertexCount;
    int lastTriangleCount;
    bool isEmitting;
    bool warnedMissingPoints;
    Camera cachedCamera;

    public bool IsEmitting => isEmitting;

    public void Configure(
        SwordTrailPoints points,
        Material material,
        Color tint,
        float sampleDistance,
        float lifeTime,
        int sampleLimit,
        bool clearImmediatelyOnStop,
        bool unscaledTime)
    {
        trailPoints = points;
        trailMaterial = material;
        trailTint = tint;
        minSampleDistance = Mathf.Max(0.001f, sampleDistance);
        trailLifeTime = Mathf.Max(0.01f, lifeTime);
        maxSamples = Mathf.Clamp(sampleLimit, MinimumSampleCount, MaximumSampleCount);
        clearOnStop = clearImmediatelyOnStop;
        useUnscaledTime = unscaledTime;

        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();

        CreateMesh();
        EnsureBuffers();
        ApplyRendererSettings();
    }

    void Reset()
    {
        trailPoints = GetComponentInParent<SwordTrailPoints>();
    }

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        CreateMesh();
        EnsureBuffers();
        ApplyRendererSettings();
        SetRendererVisible(false);
    }

    void OnEnable()
    {
        if (playOnEnable)
            StartTrail();
    }

    void OnDisable()
    {
        isEmitting = false;
        ClearTrail();
    }

    void LateUpdate()
    {
        EnsureBuffers();

        float now = CurrentTime;
        bool meshDirty = ExpireOldSamples(now);

        if (isEmitting)
            meshDirty |= TryAddCurrentSample(now, sampleCount == 0);

        if (meshDirty || sampleCount != lastVertexCount / 2)
            RebuildMesh(now);
    }

    public void StartTrail()
    {
        if (!HasTrailPoints())
            return;

        ClearTrail();
        isEmitting = true;
        TryAddCurrentSample(CurrentTime, true);
        RebuildMesh(CurrentTime);
    }

    public void StopTrail()
    {
        isEmitting = false;

        if (clearOnStop)
            ClearTrail();
    }

    public void ClearTrail()
    {
        sampleStartIndex = 0;
        sampleCount = 0;
        lastVertexCount = 0;
        lastTriangleCount = 0;

        if (trailMesh != null)
            trailMesh.Clear(false);

        SetRendererVisible(false);
    }

    bool HasTrailPoints()
    {
        if (trailPoints != null && trailPoints.HasValidPoints)
            return true;

        if (!warnedMissingPoints)
        {
            warnedMissingPoints = true;
            Debug.LogWarning("[SwordTrailMeshRenderer] Assign SwordTrailPoints with valid TrailBase and TrailTip.", this);
        }

        return false;
    }

    bool TryAddCurrentSample(float now, bool force)
    {
        if (!HasTrailPoints())
            return false;

        Vector3 basePosition = trailPoints.TrailBase.position;
        Vector3 tipPosition = trailPoints.TrailTip.position;

        if (!force && sampleCount > 0)
        {
            TrailSample lastSample = samples[PhysicalIndex(sampleCount - 1)];
            float minDistanceSqr = minSampleDistance * minSampleDistance;
            bool baseMoved = (basePosition - lastSample.BasePosition).sqrMagnitude >= minDistanceSqr;
            bool tipMoved = (tipPosition - lastSample.TipPosition).sqrMagnitude >= minDistanceSqr;

            if (!baseMoved && !tipMoved)
                return false;
        }

        AddSample(basePosition, tipPosition, now);
        return true;
    }

    void AddSample(Vector3 basePosition, Vector3 tipPosition, float now)
    {
        if (sampleCount < maxSamples)
        {
            int writeIndex = PhysicalIndex(sampleCount);
            samples[writeIndex] = new TrailSample { BasePosition = basePosition, TipPosition = tipPosition, Time = now };
            sampleCount++;
            return;
        }

        samples[sampleStartIndex] = new TrailSample { BasePosition = basePosition, TipPosition = tipPosition, Time = now };
        sampleStartIndex = (sampleStartIndex + 1) % maxSamples;
    }

    bool ExpireOldSamples(float now)
    {
        if (sampleCount == 0 || trailLifeTime <= 0f)
            return false;

        float oldestVisibleTime = now - trailLifeTime;
        bool removedAny = false;

        while (sampleCount > 0 && samples[sampleStartIndex].Time < oldestVisibleTime)
        {
            sampleStartIndex = (sampleStartIndex + 1) % maxSamples;
            sampleCount--;
            removedAny = true;
        }

        if (sampleCount == 0)
            sampleStartIndex = 0;

        return removedAny;
    }

    void RebuildMesh(float now)
    {
        if (trailMesh == null)
            return;

        if (sampleCount < 2)
        {
            trailMesh.Clear(false);
            lastVertexCount = 0;
            lastTriangleCount = 0;
            SetRendererVisible(false);
            return;
        }

        int vertexCount = sampleCount * 2;
        int triangleCount = (sampleCount - 1) * 6 * (generateDoubleSided ? 2 : 1);

        for (int i = 0; i < sampleCount; i++)
        {
            TrailSample sample = samples[PhysicalIndex(i)];
            int vertexIndex = i * 2;
            float alongTrail = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
            byte alpha = CalculateSampleAlpha(sample.Time, now);

            ResolveVisualEdge(sample.BasePosition, sample.TipPosition, out Vector3 visualBase, out Vector3 visualTip);
            vertices[vertexIndex] = transform.InverseTransformPoint(visualBase);
            vertices[vertexIndex + 1] = transform.InverseTransformPoint(visualTip);
            uvs[vertexIndex] = new Vector2(alongTrail, 0f);
            uvs[vertexIndex + 1] = new Vector2(alongTrail, 1f);
            colors[vertexIndex] = BuildVertexColor(alpha);
            colors[vertexIndex + 1] = BuildVertexColor(alpha);
        }

        int triangleWriteIndex = 0;
        for (int i = 0; i < sampleCount - 1; i++)
        {
            int currentBase = i * 2;
            int currentTip = currentBase + 1;
            int nextBase = currentBase + 2;
            int nextTip = currentBase + 3;

            triangles[triangleWriteIndex++] = currentBase;
            triangles[triangleWriteIndex++] = currentTip;
            triangles[triangleWriteIndex++] = nextBase;
            triangles[triangleWriteIndex++] = currentTip;
            triangles[triangleWriteIndex++] = nextTip;
            triangles[triangleWriteIndex++] = nextBase;

            if (!generateDoubleSided)
                continue;

            triangles[triangleWriteIndex++] = nextBase;
            triangles[triangleWriteIndex++] = currentTip;
            triangles[triangleWriteIndex++] = currentBase;
            triangles[triangleWriteIndex++] = nextBase;
            triangles[triangleWriteIndex++] = nextTip;
            triangles[triangleWriteIndex++] = currentTip;
        }

        trailMesh.Clear(false);
        trailMesh.SetVertices(vertices, 0, vertexCount);
        trailMesh.SetUVs(0, uvs, 0, vertexCount);
        trailMesh.SetColors(colors, 0, vertexCount);
        trailMesh.SetTriangles(triangles, 0, triangleCount, 0, true);
        trailMesh.RecalculateBounds();

        lastVertexCount = vertexCount;
        lastTriangleCount = triangleCount;
        SetRendererVisible(true);
    }

    byte CalculateSampleAlpha(float sampleTime, float now)
    {
        if (trailLifeTime <= 0f)
            return 255;

        float normalizedLife = Mathf.Clamp01((sampleTime + trailLifeTime - now) / trailLifeTime);
        return (byte)Mathf.RoundToInt(normalizedLife * 255f);
    }

    Color32 BuildVertexColor(byte alpha)
    {
        Color tint = trailTint;
        tint.a = Mathf.Clamp01(tint.a) * (alpha / 255f);
        return tint;
    }

    void ResolveVisualEdge(Vector3 basePosition, Vector3 tipPosition, out Vector3 visualBase, out Vector3 visualTip)
    {
        Vector3 center = (basePosition + tipPosition) * 0.5f;
        Vector3 edge = tipPosition - basePosition;
        float rawWidth = edge.magnitude;

        if (rawWidth <= 0.0001f)
        {
            visualBase = basePosition;
            visualTip = tipPosition;
            return;
        }

        Vector3 edgeDirection = edge / rawWidth;
        if (cameraFacingBlend > 0.001f)
        {
            Camera camera = ResolveCamera();
            if (camera != null)
            {
                Vector3 cameraPlaneDirection = Vector3.ProjectOnPlane(edgeDirection, camera.transform.forward);
                if (cameraPlaneDirection.sqrMagnitude <= 0.0001f)
                    cameraPlaneDirection = camera.transform.up;

                edgeDirection = Vector3.Slerp(edgeDirection, cameraPlaneDirection.normalized, cameraFacingBlend).normalized;
            }
        }

        float visualWidth = Mathf.Max(minimumVisualWidth, rawWidth * visualWidthMultiplier);
        Vector3 halfEdge = edgeDirection * (visualWidth * 0.5f);
        visualBase = center - halfEdge;
        visualTip = center + halfEdge;
    }

    Camera ResolveCamera()
    {
        if (cachedCamera != null && cachedCamera.isActiveAndEnabled)
            return cachedCamera;

        cachedCamera = Camera.main;
        return cachedCamera;
    }

    int PhysicalIndex(int logicalIndex)
    {
        return (sampleStartIndex + logicalIndex) % maxSamples;
    }

    void CreateMesh()
    {
        if (trailMesh != null)
            return;

        trailMesh = new Mesh { name = "Sword Trail Mesh" };
        trailMesh.MarkDynamic();
        meshFilter.sharedMesh = trailMesh;
    }

    void EnsureBuffers()
    {
        maxSamples = Mathf.Clamp(maxSamples, MinimumSampleCount, MaximumSampleCount);

        if (samples != null && samples.Length == maxSamples)
            return;

        samples = new TrailSample[maxSamples];
        vertices = new Vector3[maxSamples * 2];
        uvs = new Vector2[maxSamples * 2];
        colors = new Color32[maxSamples * 2];
        triangles = new int[(maxSamples - 1) * 6 * 2];
        ClearTrail();
    }

    void SetRendererVisible(bool visible)
    {
        if (meshRenderer != null && disableRendererWhenEmpty)
            meshRenderer.enabled = visible;
    }

    void ApplyRendererSettings()
    {
        if (meshRenderer == null)
            return;

        if (trailMaterial != null)
            meshRenderer.sharedMaterial = trailMaterial;

        if (!applyTintPropertyBlock)
            return;

        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();

        Color visibleTint = trailTint;
        materialPropertyBlock.Clear();
        materialPropertyBlock.SetColor(ColorId, visibleTint);
        materialPropertyBlock.SetColor(BaseColorId, visibleTint);
        materialPropertyBlock.SetColor(TintColorId, visibleTint);
        materialPropertyBlock.SetColor(EmissionColorId, visibleTint);
        materialPropertyBlock.SetColor(Color1Id, visibleTint);
        visibleTint.a = 0f;
        materialPropertyBlock.SetColor(Color2Id, visibleTint);
        meshRenderer.SetPropertyBlock(materialPropertyBlock);
    }

    float CurrentTime => useUnscaledTime ? Time.unscaledTime : Time.time;

    void OnValidate()
    {
        minSampleDistance = Mathf.Max(0.001f, minSampleDistance);
        trailLifeTime = Mathf.Max(0.01f, trailLifeTime);
        maxSamples = Mathf.Clamp(maxSamples, MinimumSampleCount, MaximumSampleCount);
        visualWidthMultiplier = Mathf.Max(0.1f, visualWidthMultiplier);
        minimumVisualWidth = Mathf.Max(0.01f, minimumVisualWidth);
        trailTint.a = Mathf.Clamp01(trailTint.a);
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
        ApplyRendererSettings();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos || samples == null || sampleCount == 0)
            return;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.65f);
        for (int i = 0; i < sampleCount; i++)
        {
            TrailSample sample = samples[PhysicalIndex(i)];
            Gizmos.DrawLine(sample.BasePosition, sample.TipPosition);
        }
    }
#endif
}

/// <summary>
/// Resolves authored Free Slash VFX materials without spawning the prefab at runtime.
/// Assign a prefab manually for builds; in the editor, default Free Slash VFX prefab paths are used as fallback.
/// </summary>
public static class SwordTrailVfxMaterialSource
{
    public enum Palette
    {
        Player,
        Boss
    }

    const string PlayerFallbackPrefabPath = "Assets/Free Slash VFX/Prefabs/Slash Fire VFX.prefab";
    const string BossFallbackPrefabPath = "Assets/Free Slash VFX/Prefabs/Slash Eletric VFX.prefab";
    const string NeutralFallbackPrefabPath = "Assets/Free Slash VFX/Prefabs/Slash VFX.prefab";
    const string PlayerFallbackMaterialPath = "Assets/Free Slash VFX/Materials/Fire Version/Slash World Fire.mat";
    const string BossFallbackMaterialPath = "Assets/Free Slash VFX/Materials/Slash World.mat";

    public static Material ResolveMaterial(GameObject preferredPrefab, Palette palette)
    {
        Material material = ExtractPreferredSharedMaterial(preferredPrefab, palette);
        if (material != null)
            return material;

#if UNITY_EDITOR
        string materialPath = palette == Palette.Player ? PlayerFallbackMaterialPath : BossFallbackMaterialPath;
        material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material != null)
            return material;

        string primaryPath = palette == Palette.Player ? PlayerFallbackPrefabPath : BossFallbackPrefabPath;
        material = ExtractPreferredSharedMaterial(AssetDatabase.LoadAssetAtPath<GameObject>(primaryPath), palette);
        if (material != null)
            return material;

        return ExtractPreferredSharedMaterial(AssetDatabase.LoadAssetAtPath<GameObject>(NeutralFallbackPrefabPath), palette);
#else
        return null;
#endif
    }

    static Material ExtractPreferredSharedMaterial(GameObject prefab, Palette palette)
    {
        if (prefab == null)
            return null;

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        Material firstUsable = null;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null)
                    continue;

                if (firstUsable == null && IsUsableSlashMaterial(material))
                    firstUsable = material;

                if (IsPreferredSlashMaterial(material, palette))
                    return material;
            }
        }

        return firstUsable;
    }

    static bool IsPreferredSlashMaterial(Material material, Palette palette)
    {
        string materialName = material.name.ToLowerInvariant();
        if (!materialName.Contains("slash world"))
            return false;

        return palette != Palette.Player || materialName.Contains("fire");
    }

    static bool IsUsableSlashMaterial(Material material)
    {
        string materialName = material.name.ToLowerInvariant();
        return materialName.Contains("slash world") || materialName == "slash";
    }
}
