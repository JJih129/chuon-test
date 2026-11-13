using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Blade-sweep ribbon built from the real sword motion (iteration 10 rewrite).
/// Every LateUpdate (after animation) the TrailBase/TrailTip transforms on the runtime sword are sampled.
/// The mesh is rebuilt every frame from those samples with Catmull-Rom sub-frame interpolation, so a
/// fast swing (30~50 degrees per frame) still produces a smooth arc that lies exactly on the blade path.
/// No camera-facing bending / re-centering: the ribbon edge IS the blade (hilt → tip), optionally
/// extended past the tip by the strike weight. Visibility is driven by StartTrail/StopTrail (attack
/// hit window) and the remaining samples fade out over the trail lifetime.
/// UV.x = age (0 = blade now, 1 = oldest), UV.y = 0 hilt side .. 1 tip (see ChuOn/BladeTrail shader).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class SwordTrailMeshRenderer : MonoBehaviour
{
    const int MinimumSampleCount = 2;
    const int MaximumSampleCount = 128;
    const int HistoryLength = 8;
    const string DefaultMaterialResource = "VFX/ChuonBladeTrail";

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

    [Tooltip("Maximum number of raw (per-frame) samples kept in the ring buffer.")]
    [SerializeField, Range(MinimumSampleCount, MaximumSampleCount)] int maxSamples = 32;

    [Tooltip("If enabled, StopTrail clears the mesh immediately instead of letting it fade by lifetime.")]
    [SerializeField] bool clearOnStop;

    [Tooltip("If enabled, trail timing ignores Time.timeScale.")]
    [SerializeField] bool useUnscaledTime;

    [Tooltip("Legacy (pre-ribbon mode) option. The ribbon shader renders both faces.")]
    [SerializeField] bool generateDoubleSided = true;

    [Tooltip("If enabled, the MeshRenderer is disabled whenever there is no visible trail mesh.")]
    [SerializeField] bool disableRendererWhenEmpty = true;

    [Tooltip("If enabled, selected-object gizmos show the currently buffered trail samples.")]
    [SerializeField] bool drawDebugGizmos = true;

    [Tooltip("Legacy (only when Blade Ribbon Mode is off): span multiplier around the blade centre.")]
    [SerializeField, Min(0.1f)] float visualWidthMultiplier = 1.35f;

    [Tooltip("Legacy (only when Blade Ribbon Mode is off): minimum rendered span.")]
    [SerializeField, Min(0.01f)] float minimumVisualWidth = 0.75f;

    [Tooltip("Legacy (only when Blade Ribbon Mode is off): bends the edge toward the camera plane. This made the trail disagree with the real blade.")]
    [SerializeField, Range(0f, 1f)] float cameraFacingBlend = 0.65f;

    [Tooltip("Material used by the generated sword trail mesh. Empty = Resources/VFX/ChuonBladeTrail.")]
    [SerializeField] Material trailMaterial;

    [Tooltip("Edge tint (the core stays white-hot). Alpha scales the whole ribbon.")]
    [SerializeField] Color trailTint = Color.white;

    [Tooltip("If enabled, shader colour properties are updated through a reused MaterialPropertyBlock.")]
    [SerializeField] bool applyTintPropertyBlock = true;

    [Header("Blade Ribbon (iteration 10)")]
    [Tooltip("True: the ribbon spans exactly the blade (hilt→tip) every frame. False: legacy camera-bent ribbon.")]
    [SerializeField] bool bladeRibbonMode = true;
    [Tooltip("Max interpolated slices inserted between two frames (Catmull-Rom).")]
    [SerializeField, Range(1, 12)] int maxSubdivisions = 8;
    [Tooltip("Target tip arc length per interpolated slice (m).")]
    [SerializeField, Min(0.005f)] float subdivisionSpacing = 0.045f;
    [Tooltip("Blade motion from just before StartTrail that is included so the ribbon does not pop in.")]
    [SerializeField, Range(0f, 0.12f)] float preRollSeconds = 0.04f;
    [Tooltip("Ignore new samples whose tip moved less than this (m).")]
    [SerializeField, Min(0.0005f)] float ribbonMinTipMove = 0.003f;
    [Tooltip("Prefer the ChuOn/BladeTrail material (Resources) over an authored slash material.")]
    [SerializeField] bool preferChuonBladeMaterial = true;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int Color1Id = Shader.PropertyToID("_Color1");
    static readonly int Color2Id = Shader.PropertyToID("_Color2");
    static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int HiltFadeId = Shader.PropertyToID("_HiltFade");
    static readonly int CoreWidthId = Shader.PropertyToID("_CoreWidth");
    static readonly int StreakSeedId = Shader.PropertyToID("_StreakSeed");

    static Material s_defaultRibbonMaterial;

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

    // 직전 프레임 칼날 기록 (StartTrail 프리롤용, 방출 중이 아니어도 기록)
    readonly TrailSample[] history = new TrailSample[HistoryLength];
    int historyWrite;
    int historyCount;

    // 공격 무게별 스타일 (SetStrikeWeight)
    float strikeWeight = 0.4f;
    float ribbonHiltFraction = 0.22f;
    float ribbonTipExtension = 0.05f;
    float ribbonIntensity = 1f;
    float ribbonLifeTime = 0.16f;
    float ribbonHiltFade = 0.55f;
    float streakSeed;
    bool styleOverridden;

    public bool IsEmitting => isEmitting;
    public float StrikeWeight => strikeWeight;
    public int SampleCount => sampleCount;

    /// <summary>QA: newest ribbon edge in world space (should equal the blade hilt/tip).</summary>
    public bool TryGetNewestEdge(out Vector3 basePosition, out Vector3 tipPosition)
    {
        basePosition = tipPosition = Vector3.zero;
        if (sampleCount == 0 || samples == null)
            return false;
        TrailSample s = samples[PhysicalIndex(sampleCount - 1)];
        ResolveRibbonEdge(s.BasePosition, s.TipPosition, out basePosition, out tipPosition);
        return true;
    }

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

    /// <summary>
    /// Strike weight 0 (light jab) .. 1 (heavy finisher): heavier = wider span (starts nearer the hilt,
    /// extends past the tip), brighter core and a slightly longer fade.
    /// </summary>
    public void SetStrikeWeight(float weight01)
    {
        strikeWeight = Mathf.Clamp01(weight01);
        styleOverridden = true;
        ribbonHiltFraction = Mathf.Lerp(0.32f, 0.04f, strikeWeight);
        ribbonTipExtension = Mathf.Lerp(0.02f, 0.16f, strikeWeight);
        ribbonIntensity = Mathf.Lerp(0.85f, 1.65f, strikeWeight);
        ribbonLifeTime = Mathf.Lerp(0.12f, 0.22f, strikeWeight);
        ribbonHiltFade = Mathf.Lerp(0.62f, 0.38f, strikeWeight);
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
        historyCount = 0;
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        if (playOnEnable)
            StartTrail();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        isEmitting = false;
        ClearTrail();
    }

    int lastTickFrame = -1;

    // 리본 모드: 렌더 직전에 샘플링 (무기 소켓/IK 등 다른 LateUpdate 가 검을 옮긴 뒤의 '화면에 그려질' 칼날 포즈)
    void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!bladeRibbonMode || !Application.isPlaying)
            return;
        Tick();
    }

    void LateUpdate()
    {
        // SRP 가 없으면(이론상) LateUpdate 에서, 리본 모드면 렌더 직전 콜백에서 처리
        if (bladeRibbonMode && GraphicsSettings.currentRenderPipeline != null)
            return;
        Tick();
    }

    void Tick()
    {
        if (lastTickFrame == Time.frameCount)
            return;
        lastTickFrame = Time.frameCount;
        EnsureBuffers();

        float now = CurrentTime;
        bool hasPoints = trailPoints != null && trailPoints.HasValidPoints;
        if (hasPoints)
            RecordHistory(trailPoints.TrailBase.position, trailPoints.TrailTip.position, now);

        bool meshDirty = ExpireOldSamples(now);

        if (isEmitting)
            meshDirty |= TryAddCurrentSample(now, sampleCount == 0);

        // 리본 모드: 나이(uv.x)가 매 프레임 바뀌므로 보이는 동안 매 프레임 재구성
        if (bladeRibbonMode ? sampleCount > 0 || lastVertexCount > 0 : meshDirty || sampleCount != lastVertexCount / 2)
            RebuildMesh(now);
    }

    public void StartTrail()
    {
        if (!HasTrailPoints())
            return;

        ClearTrail();
        isEmitting = true;
        float now = CurrentTime;

        if (bladeRibbonMode && preRollSeconds > 0f)
        {
            // 프리롤: 히트 윈도우 직전 칼날 경로를 포함 (첫 프레임 리본이 비지 않도록)
            for (int i = 0; i < historyCount; i++)
            {
                int index = (historyWrite - historyCount + i + HistoryLength) % HistoryLength;
                TrailSample h = history[index];
                if (now - h.Time <= preRollSeconds && now - h.Time > 0.0001f)
                    AddSample(h.BasePosition, h.TipPosition, h.Time);
            }
        }

        TryAddCurrentSample(now, true);
        RebuildMesh(now);
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

    void RecordHistory(Vector3 basePosition, Vector3 tipPosition, float now)
    {
        if (historyCount > 0)
        {
            int last = (historyWrite - 1 + HistoryLength) % HistoryLength;
            if (Mathf.Approximately(history[last].Time, now))
                return;
        }

        history[historyWrite] = new TrailSample { BasePosition = basePosition, TipPosition = tipPosition, Time = now };
        historyWrite = (historyWrite + 1) % HistoryLength;
        historyCount = Mathf.Min(historyCount + 1, HistoryLength);
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
            if (bladeRibbonMode)
            {
                // 같은 시각(같은 프레임, 또는 히트스톱으로 시간이 멈춘 동안)이면 머리 샘플을 현재 칼날로 갱신
                if (Mathf.Approximately(lastSample.Time, now))
                {
                    samples[PhysicalIndex(sampleCount - 1)] = new TrailSample { BasePosition = basePosition, TipPosition = tipPosition, Time = now };
                    return true;
                }
                float minTip = ribbonMinTipMove * ribbonMinTipMove;
                if ((tipPosition - lastSample.TipPosition).sqrMagnitude < minTip &&
                    (basePosition - lastSample.BasePosition).sqrMagnitude < minTip)
                    return false;
            }
            else
            {
                float minDistanceSqr = minSampleDistance * minSampleDistance;
                bool baseMoved = (basePosition - lastSample.BasePosition).sqrMagnitude >= minDistanceSqr;
                bool tipMoved = (tipPosition - lastSample.TipPosition).sqrMagnitude >= minDistanceSqr;

                if (!baseMoved && !tipMoved)
                    return false;
            }
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

    float EffectiveLifeTime => bladeRibbonMode && styleOverridden ? ribbonLifeTime : trailLifeTime;

    bool ExpireOldSamples(float now)
    {
        float life = EffectiveLifeTime;
        if (sampleCount == 0 || life <= 0f)
            return false;

        float oldestVisibleTime = now - life;
        bool removedAny = false;

        // 리본 모드에서는 마지막(현재) 샘플까지 사라지면 끝 (방출 중이면 머리 샘플은 유지)
        int keep = isEmitting ? 1 : 0;
        while (sampleCount > keep && samples[sampleStartIndex].Time < oldestVisibleTime)
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

        if (bladeRibbonMode)
        {
            RebuildRibbon(now);
            return;
        }

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

        UploadMesh(vertexCount, triangleCount);
    }

    // ───────── 리본 모드: Catmull-Rom 보간 슬라이스 ─────────
    void RebuildRibbon(float now)
    {
        float life = Mathf.Max(0.01f, EffectiveLifeTime);
        if (sampleCount < 2)
        {
            if (lastVertexCount > 0 || sampleCount == 0)
            {
                trailMesh.Clear(false);
                lastVertexCount = 0;
                lastTriangleCount = 0;
                SetRendererVisible(false);
            }
            return;
        }

        int maxSlices = vertices.Length / 2;
        int slice = 0;
        Color32 white = BuildVertexColor(255);

        for (int i = 0; i < sampleCount - 1 && slice < maxSlices; i++)
        {
            TrailSample p0 = samples[PhysicalIndex(Mathf.Max(0, i - 1))];
            TrailSample p1 = samples[PhysicalIndex(i)];
            TrailSample p2 = samples[PhysicalIndex(i + 1)];
            TrailSample p3 = samples[PhysicalIndex(Mathf.Min(sampleCount - 1, i + 2))];

            float tipTravel = Vector3.Distance(p1.TipPosition, p2.TipPosition);
            int steps = Mathf.Clamp(Mathf.CeilToInt(tipTravel / Mathf.Max(0.005f, subdivisionSpacing)), 1, Mathf.Max(1, maxSubdivisions));
            float len1 = Vector3.Distance(p1.BasePosition, p1.TipPosition);
            float len2 = Vector3.Distance(p2.BasePosition, p2.TipPosition);

            for (int s = 0; s < steps && slice < maxSlices; s++)
            {
                float t = s / (float)steps;
                Vector3 b = s == 0 ? p1.BasePosition : CatmullRom(p0.BasePosition, p1.BasePosition, p2.BasePosition, p3.BasePosition, t);
                Vector3 tip = s == 0 ? p1.TipPosition : CatmullRom(p0.TipPosition, p1.TipPosition, p2.TipPosition, p3.TipPosition, t);
                if (s > 0)
                {
                    // 보간된 칼날 길이 유지 (현 보간은 칼날을 짧게 만든다)
                    Vector3 axis = tip - b;
                    float len = Mathf.Lerp(len1, len2, t);
                    if (axis.sqrMagnitude > 0.000001f)
                        tip = b + axis.normalized * len;
                }

                float sampleTime = Mathf.Lerp(p1.Time, p2.Time, t);
                WriteSlice(slice++, b, tip, sampleTime, now, life, white);
            }
        }

        if (slice < maxSlices)
        {
            TrailSample last = samples[PhysicalIndex(sampleCount - 1)];
            WriteSlice(slice++, last.BasePosition, last.TipPosition, last.Time, now, life, white);
        }

        // 가장 오래된 슬라이스가 이미 수명을 넘었으면 표시할 것이 없다
        if (slice < 2)
        {
            trailMesh.Clear(false);
            lastVertexCount = 0;
            lastTriangleCount = 0;
            SetRendererVisible(false);
            return;
        }

        int triangleWriteIndex = 0;
        for (int i = 0; i < slice - 1; i++)
        {
            int a = i * 2;
            triangles[triangleWriteIndex++] = a;
            triangles[triangleWriteIndex++] = a + 1;
            triangles[triangleWriteIndex++] = a + 2;
            triangles[triangleWriteIndex++] = a + 1;
            triangles[triangleWriteIndex++] = a + 3;
            triangles[triangleWriteIndex++] = a + 2;
        }

        UploadMesh(slice * 2, triangleWriteIndex);
    }

    void WriteSlice(int slice, Vector3 basePosition, Vector3 tipPosition, float sampleTime, float now, float life, Color32 color)
    {
        ResolveRibbonEdge(basePosition, tipPosition, out Vector3 edgeBase, out Vector3 edgeTip);
        int v = slice * 2;
        // 최신 = 0, 수명 끝 = 1 (셰이더가 페이드)
        float age = Mathf.Clamp01((now - sampleTime) / life);
        vertices[v] = transform.InverseTransformPoint(edgeBase);
        vertices[v + 1] = transform.InverseTransformPoint(edgeTip);
        uvs[v] = new Vector2(age, 0f);
        uvs[v + 1] = new Vector2(age, 1f);
        colors[v] = color;
        colors[v + 1] = color;
    }

    void ResolveRibbonEdge(Vector3 basePosition, Vector3 tipPosition, out Vector3 edgeBase, out Vector3 edgeTip)
    {
        if (!bladeRibbonMode)
        {
            ResolveVisualEdge(basePosition, tipPosition, out edgeBase, out edgeTip);
            return;
        }

        Vector3 blade = tipPosition - basePosition;
        float hilt = styleOverridden ? ribbonHiltFraction : 0.22f;
        float ext = styleOverridden ? ribbonTipExtension : 0.05f;
        edgeBase = basePosition + blade * hilt;
        edgeTip = tipPosition + blade * ext;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void UploadMesh(int vertexCount, int triangleCount)
    {
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
        if (bladeRibbonMode)
        {
            // 리본 셰이더는 색을 재질(코어/엣지)에서 받고 정점색은 전체 배율로만 쓴다
            Color white = Color.white;
            white.a = Mathf.Clamp01(trailTint.a) * (alpha / 255f);
            return white;
        }

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
        // 리본 모드는 프레임 사이에 최대 maxSubdivisions 슬라이스를 넣는다
        int slices = maxSamples * Mathf.Max(1, maxSubdivisions) + 1;
        vertices = new Vector3[slices * 2];
        uvs = new Vector2[slices * 2];
        colors = new Color32[slices * 2];
        triangles = new int[Mathf.Max((maxSamples - 1) * 6 * 2, (slices - 1) * 6)];
        ClearTrail();
    }

    void SetRendererVisible(bool visible)
    {
        if (meshRenderer != null && disableRendererWhenEmpty)
            meshRenderer.enabled = visible;
    }

    Material ResolveRibbonMaterial()
    {
        if (!bladeRibbonMode || !preferChuonBladeMaterial)
            return trailMaterial;

        if (trailMaterial != null && trailMaterial.shader != null && trailMaterial.shader.name == "ChuOn/BladeTrail")
            return trailMaterial;

        if (s_defaultRibbonMaterial == null)
            s_defaultRibbonMaterial = Resources.Load<Material>(DefaultMaterialResource);
        return s_defaultRibbonMaterial != null ? s_defaultRibbonMaterial : trailMaterial;
    }

    void ApplyRendererSettings()
    {
        if (meshRenderer == null)
            return;

        Material material = ResolveRibbonMaterial();
        if (material != null)
            meshRenderer.sharedMaterial = material;

        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        if (!applyTintPropertyBlock)
            return;

        if (materialPropertyBlock == null)
            materialPropertyBlock = new MaterialPropertyBlock();

        Color visibleTint = trailTint;
        materialPropertyBlock.Clear();
        if (bladeRibbonMode && material != null && material.HasProperty(EdgeColorId))
        {
            // 엣지 = 틴트(HDR 2.2배), 코어는 재질의 백열색. 무게에 따라 밝기/손잡이 페이드/코어 폭
            Color edge = new Color(visibleTint.r, visibleTint.g, visibleTint.b, 1f) * 1.4f;
            materialPropertyBlock.SetColor(EdgeColorId, edge);
            materialPropertyBlock.SetFloat(IntensityId, styleOverridden ? ribbonIntensity : 1f);
            materialPropertyBlock.SetFloat(HiltFadeId, styleOverridden ? ribbonHiltFade : 0.55f);
            materialPropertyBlock.SetFloat(CoreWidthId, Mathf.Lerp(0.38f, 0.55f, strikeWeight));
            streakSeed = (streakSeed + 0.6180339f) % 1f;
            materialPropertyBlock.SetFloat(StreakSeedId, streakSeed * 10f);
            meshRenderer.SetPropertyBlock(materialPropertyBlock);
            return;
        }

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
        if (Application.isPlaying)
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

    const string PlayerFallbackPrefabPath = "Assets/ThirdParty/Free Slash VFX/Prefabs/Slash Fire VFX.prefab";
    const string BossFallbackPrefabPath = "Assets/ThirdParty/Free Slash VFX/Prefabs/Slash Eletric VFX.prefab";
    const string NeutralFallbackPrefabPath = "Assets/ThirdParty/Free Slash VFX/Prefabs/Slash VFX.prefab";
    const string PlayerFallbackMaterialPath = "Assets/ThirdParty/Free Slash VFX/Materials/Fire Version/Slash World Fire.mat";
    const string BossFallbackMaterialPath = "Assets/ThirdParty/Free Slash VFX/Materials/Slash World.mat";

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
