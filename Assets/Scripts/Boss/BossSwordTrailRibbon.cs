using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class BossSwordTrailRibbon : MonoBehaviour
{
    struct RibbonSample
    {
        public Vector3 basePosition;
        public Vector3 tipPosition;
        public float timestamp;
    }

    const int MaxSamples = 32;

    [SerializeField] float sampleLifetime = 0.12f;
    [SerializeField] float minSampleDistance = 0.03f;
    [SerializeField, Min(1f / 120f)] float meshRefreshInterval = 1f / 45f;
    [SerializeField] float startWidthScale = 1.08f;
    [SerializeField] float endWidthScale = 0.28f;
    [SerializeField] Color startColor = new Color(0.3f, 1f, 1f, 0.95f);
    [SerializeField] Color endColor = new Color(0.15f, 0.7f, 1f, 0f);

    readonly RibbonSample[] _samples = new RibbonSample[MaxSamples];
    readonly Vector3[] _vertices = new Vector3[MaxSamples * 2];
    readonly Vector2[] _uv = new Vector2[MaxSamples * 2];
    readonly Color[] _colors = new Color[MaxSamples * 2];
    readonly int[] _triangles = new int[(MaxSamples - 1) * 6];

    int _sampleCount;
    bool _emitting;
    bool _dirty;
    Transform _baseAnchor;
    Transform _tipAnchor;
    Mesh _mesh;
    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;
    float _nextMeshRebuildAt;

    public void Configure(
        Transform baseAnchor,
        Transform tipAnchor,
        Material material,
        float lifetime,
        float minDistance,
        float bladeStartWidthScale,
        float bladeEndWidthScale,
        Color freshColor,
        Color fadedColor)
    {
        _baseAnchor = baseAnchor;
        _tipAnchor = tipAnchor;
        sampleLifetime = Mathf.Max(0.02f, lifetime);
        minSampleDistance = Mathf.Max(0.001f, minDistance);
        startWidthScale = Mathf.Max(1f, bladeStartWidthScale);
        endWidthScale = Mathf.Clamp(bladeEndWidthScale, 0.02f, startWidthScale);
        startColor = freshColor;
        endColor = fadedColor;

        EnsureComponents();
        if (material != null)
            _meshRenderer.sharedMaterial = material;
    }

    public void Begin()
    {
        if (_baseAnchor == null || _tipAnchor == null)
            return;

        EnsureComponents();
        DetachToWorldSpace();
        _sampleCount = 0;
        _emitting = true;
        _nextMeshRebuildAt = 0f;
        gameObject.SetActive(true);
        PushSample(force: true);
        RebuildMesh(Time.time);
    }

    public void Stop()
    {
        _emitting = false;
    }

    void LateUpdate()
    {
        if (_baseAnchor == null || _tipAnchor == null)
            return;

        float now = Time.time;
        if (_emitting)
            PushSample(force: false);

        TrimExpired(now);

        if (_sampleCount == 0)
        {
            if (_mesh != null)
                _mesh.Clear(false);
            _nextMeshRebuildAt = 0f;
            gameObject.SetActive(false);
            return;
        }

        if (!_dirty && now < _nextMeshRebuildAt)
            return;

        RebuildMesh(now);
        _nextMeshRebuildAt = now + Mathf.Max(1f / 90f, meshRefreshInterval);
    }

    void EnsureComponents()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();
        if (_meshRenderer == null)
            _meshRenderer = GetComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "BossSwordTrailRibbon" };
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;
        }

        _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    void DetachToWorldSpace()
    {
        if (transform.parent != null)
            transform.SetParent(null, false);

        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
    }

    void PushSample(bool force)
    {
        Vector3 basePos = _baseAnchor.position;
        Vector3 tipPos = _tipAnchor.position;

        if (!force && _sampleCount > 0)
        {
            Vector3 lastBase = _samples[_sampleCount - 1].basePosition;
            Vector3 lastTip = _samples[_sampleCount - 1].tipPosition;
            float moved = Mathf.Max(
                Vector3.Distance(lastBase, basePos),
                Vector3.Distance(lastTip, tipPos));

            if (moved < minSampleDistance)
                return;
        }

        if (_sampleCount >= MaxSamples)
        {
            for (int i = 1; i < _sampleCount; i++)
                _samples[i - 1] = _samples[i];
            _sampleCount = MaxSamples - 1;
        }

        _samples[_sampleCount++] = new RibbonSample
        {
            basePosition = basePos,
            tipPosition = tipPos,
            timestamp = Time.time
        };
        _dirty = true;
    }

    void TrimExpired(float now)
    {
        int expired = 0;
        while (expired < _sampleCount && now - _samples[expired].timestamp > sampleLifetime)
            expired++;

        if (expired <= 0)
            return;

        for (int i = expired; i < _sampleCount; i++)
            _samples[i - expired] = _samples[i];

        _sampleCount -= expired;
        _dirty = true;
    }

    void RebuildMesh(float now)
    {
        if (_mesh == null)
            return;

        if (_sampleCount < 2)
        {
            _mesh.Clear(false);
            _dirty = false;
            return;
        }

        int vertexCount = _sampleCount * 2;
        int triangleCount = (_sampleCount - 1) * 6;
        Bounds localBounds = default;
        bool hasBounds = false;

        for (int i = 0; i < _sampleCount; i++)
        {
            RibbonSample sample = _samples[i];
            Vector3 center = (sample.basePosition + sample.tipPosition) * 0.5f;
            float age01 = Mathf.Clamp01((now - sample.timestamp) / sampleLifetime);
            float currentWidthScale = Mathf.Lerp(startWidthScale, endWidthScale, age01);
            Vector3 basePos = center + (sample.basePosition - center) * currentWidthScale;
            Vector3 tipPos = center + (sample.tipPosition - center) * currentWidthScale;

            int vertexIndex = i * 2;
            _vertices[vertexIndex] = basePos;
            _vertices[vertexIndex + 1] = tipPos;
            if (!hasBounds)
            {
                localBounds = new Bounds(_vertices[vertexIndex], Vector3.zero);
                localBounds.Encapsulate(_vertices[vertexIndex + 1]);
                hasBounds = true;
            }
            else
            {
                localBounds.Encapsulate(_vertices[vertexIndex]);
                localBounds.Encapsulate(_vertices[vertexIndex + 1]);
            }

            float t = _sampleCount <= 1 ? 0f : (float)i / (_sampleCount - 1);
            _uv[vertexIndex] = new Vector2(0f, t);
            _uv[vertexIndex + 1] = new Vector2(1f, t);

            Color color = Color.Lerp(startColor, endColor, age01);
            _colors[vertexIndex] = color;
            _colors[vertexIndex + 1] = color;
        }

        int triangleIndex = 0;
        for (int i = 0; i < _sampleCount - 1; i++)
        {
            int root = i * 2;
            _triangles[triangleIndex++] = root;
            _triangles[triangleIndex++] = root + 1;
            _triangles[triangleIndex++] = root + 2;
            _triangles[triangleIndex++] = root + 2;
            _triangles[triangleIndex++] = root + 1;
            _triangles[triangleIndex++] = root + 3;
        }

        _mesh.Clear(false);
        _mesh.SetVertices(_vertices, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
        _mesh.SetUVs(0, _uv, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
        _mesh.SetColors(_colors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
        _mesh.SetTriangles(_triangles, 0, triangleCount, 0, false, 0);
        if (hasBounds)
        {
            localBounds.Expand(Vector3.one * 0.05f);
            _mesh.bounds = localBounds;
        }
        _dirty = false;
    }
}
