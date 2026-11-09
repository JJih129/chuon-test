using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class BossGroundTelegraph : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Color parryColor = new Color(1.00f, 0.66f, 0.08f, 0.26f);
    [SerializeField] private Color guardColor = new Color(0.22f, 0.78f, 1.00f, 0.24f);
    [SerializeField] private Color dodgeColor = new Color(1.00f, 0.18f, 0.18f, 0.22f);
    [SerializeField] private Color dangerColor = new Color(1.00f, 0.12f, 0.78f, 0.30f);
    [SerializeField, Min(0.25f)] private float minimumWidth = 0.9f;
    [SerializeField, Min(0.25f)] private float minimumLength = 0.9f;
    [SerializeField, Min(0f)] private float defaultPadding = 0.28f;
    [SerializeField, Min(0f)] private float dangerExtraPadding = 0.2f;
    [SerializeField, Min(0f)] private float yOffset = 0.03f;
    [SerializeField, Min(1f / 120f)] private float transformRefreshInterval = 1f / 20f;

    Transform _visualTransform;
    MeshRenderer _visualRenderer;
    Material _runtimeMaterial;
    MaterialPropertyBlock _propertyBlock;
    Collider _trackedCollider;
    Transform _ownerRoot;
    bool _active;
    float _hideAtTime;
    float _nextTransformRefreshAt;
    AttackTelegraphType _currentTelegraphType = AttackTelegraphType.Auto;

    void Awake()
    {
        EnsureVisual();
        HideCue();
    }

    void LateUpdate()
    {
        if (_circleActive)
            UpdateCircle();

        if (!_active)
            return;

        if (_trackedCollider == null || Time.time >= _hideAtTime)
        {
            HideCue();
            return;
        }

        if (Time.time >= _nextTransformRefreshAt)
            UpdateVisualTransform();
    }

    void OnDisable()
    {
        HideCircle();
        HideCue();
    }

    void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
        if (_circleMaterial != null)
            Destroy(_circleMaterial);
    }

    // ==================== 원형 텔레그래프 (충격파 / 도약 그림자) ====================
    // 테두리 링 + 안쪽에서 바깥으로 차오르는 채움(= 남은 시간). 채움이 테두리에 닿는 순간이 피격 시점 (정직한 큐).
    // shadowStyle: 도약 내려찍기용. 어두운 그림자 원판이 follow 위치(플레이어)를 따라가다 LockCircle() 후 고정.
    [Header("Circle Telegraph")]
    [SerializeField, Range(0.5f, 0.99f)] private float circleRimInner = 0.93f;
    [SerializeField] private Color shadowFillColor = new Color(0.02f, 0.02f, 0.03f, 0.62f);

    Transform _circleRoot;
    MeshRenderer _circleRimRenderer;
    MeshRenderer _circleFillRenderer;
    MeshRenderer _circleShadowRenderer;
    Material _circleMaterial;
    MaterialPropertyBlock _circleBlock;
    bool _circleActive;
    float _circleStartTime;
    float _circleEndTime;
    float _circleRadius;
    float _circleGroundY;
    Color _circleColor;
    Vector3 _circleCenter;
    System.Func<Vector3> _circleFollow;
    bool _circleShadow;
    bool _circleLocked;
    const float CircleFollowSharpness = 9f;
    static Mesh s_discMesh;
    static Mesh s_ringMesh;

    public bool IsCircleActive => _circleActive;
    public Vector3 CircleCenter => _circleCenter;
    public float CircleRadius => _circleRadius;

    public void ShowCircle(Vector3 center, float radius, AttackTelegraphType telegraphType, float duration, System.Func<Vector3> follow = null, bool shadowStyle = false)
    {
        EnsureCircleVisual();
        _circleActive = true;
        _circleStartTime = Time.time;
        _circleEndTime = Time.time + Mathf.Max(0.06f, duration);
        _circleRadius = Mathf.Max(0.3f, radius);
        _circleGroundY = center.y + yOffset + 0.01f;
        _circleCenter = new Vector3(center.x, _circleGroundY, center.z);
        _circleFollow = follow;
        _circleShadow = shadowStyle;
        _circleLocked = follow == null;
        Color c = ResolveColor(telegraphType);
        _circleColor = new Color(c.r, c.g, c.b, 1f);
        _circleRoot.gameObject.SetActive(true);
        _circleShadowRenderer.gameObject.SetActive(shadowStyle);
        enabled = true;
        UpdateCircle();
    }

    /// <summary>추적을 멈추고 현재 위치에 고정 (도약 낙하 지점 확정).</summary>
    public Vector3 LockCircle()
    {
        _circleLocked = true;
        _circleFollow = null;
        return _circleCenter;
    }

    public void HideCircle()
    {
        _circleActive = false;
        _circleFollow = null;
        if (_circleRoot != null)
            _circleRoot.gameObject.SetActive(false);
        if (!_active)
            enabled = false;
    }

    void UpdateCircle()
    {
        if (_circleRoot == null)
            return;

        if (Time.time >= _circleEndTime + 0.12f)
        {
            HideCircle();
            return;
        }

        if (!_circleLocked && _circleFollow != null)
        {
            Vector3 target = _circleFollow();
            target.y = _circleGroundY;
            float k = 1f - Mathf.Exp(-CircleFollowSharpness * Time.deltaTime);
            _circleCenter = Vector3.Lerp(_circleCenter, target, k);
        }

        float duration = Mathf.Max(0.01f, _circleEndTime - _circleStartTime);
        float progress = Mathf.Clamp01((Time.time - _circleStartTime) / duration);
        _circleRoot.SetPositionAndRotation(_circleCenter, Quaternion.identity);

        _circleRimRenderer.transform.localScale = new Vector3(_circleRadius, 1f, _circleRadius);
        float fillScale = Mathf.Max(0.02f, _circleRadius * progress);
        _circleFillRenderer.transform.localScale = new Vector3(fillScale, 1f, fillScale);

        // 마지막 0.4초: 테두리가 빠르게 깜빡이며 밝아진다 (임박)
        float remaining = _circleEndTime - Time.time;
        float urgent = remaining < 0.4f ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 26f)) : 0f;
        SetCircleColor(_circleRimRenderer, _circleColor, Mathf.Lerp(0.55f, 0.95f, Mathf.Max(progress * 0.6f, urgent)));
        SetCircleColor(_circleFillRenderer, _circleColor, Mathf.Lerp(0.14f, 0.30f, progress));
        if (_circleShadow)
        {
            float shadowScale = _circleRadius * Mathf.Lerp(0.55f, 0.8f, progress);
            _circleShadowRenderer.transform.localScale = new Vector3(shadowScale, 1f, shadowScale);
            SetCircleColor(_circleShadowRenderer, shadowFillColor, Mathf.Lerp(shadowFillColor.a * 0.5f, shadowFillColor.a, progress));
        }
    }

    void SetCircleColor(MeshRenderer target, Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        _circleBlock.Clear();
        _circleBlock.SetColor("_BaseColor", color);
        _circleBlock.SetColor("_Color", color);
        target.SetPropertyBlock(_circleBlock);
    }

    void EnsureCircleVisual()
    {
        if (_circleRoot != null)
            return;

        if (s_discMesh == null) s_discMesh = BuildAnnulus(0f, 1f, 64, "_TelegraphDisc");
        if (s_ringMesh == null) s_ringMesh = BuildAnnulus(circleRimInner, 1f, 96, "_TelegraphRing");

        var root = new GameObject("GroundTelegraphCircle");
        root.hideFlags = HideFlags.HideAndDontSave;
        _circleRoot = root.transform;
        _circleRoot.SetParent(transform, false);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            _circleMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _circleMaterial.renderQueue = (int)RenderQueue.Transparent + 1;
        }

        _circleShadowRenderer = NewCirclePart("Shadow", s_discMesh, 0.000f);
        _circleFillRenderer = NewCirclePart("Fill", s_discMesh, 0.004f);
        _circleRimRenderer = NewCirclePart("Rim", s_ringMesh, 0.008f);
        _circleBlock = new MaterialPropertyBlock();
        root.SetActive(false);
    }

    MeshRenderer NewCirclePart(string partName, Mesh mesh, float lift)
    {
        var go = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetParent(_circleRoot, false);
        go.transform.localPosition = new Vector3(0f, lift, 0f);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = _circleMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    static Mesh BuildAnnulus(float inner, float outer, int segments, string meshName)
    {
        var mesh = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
        var verts = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[verts.Length];
        var tris = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = d * inner;
            verts[i * 2 + 1] = d * outer;
            uvs[i * 2] = new Vector2(i / (float)segments, 0f);
            uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
        }
        for (int i = 0; i < segments; i++)
        {
            int v = i * 2;
            int t = i * 6;
            tris[t] = v; tris[t + 1] = v + 3; tris[t + 2] = v + 1;
            tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public void ShowCue(Collider trackedCollider, AttackTelegraphType telegraphType, float duration, Transform ownerRoot = null)
    {
        if (trackedCollider == null)
            return;

        EnsureVisual();
        _trackedCollider = trackedCollider;
        _ownerRoot = ownerRoot != null ? ownerRoot : trackedCollider.transform.root;
        _hideAtTime = Time.time + Mathf.Max(0.06f, duration);
        _active = true;
        _nextTransformRefreshAt = 0f;
        _currentTelegraphType = telegraphType;

        SetVisualColor(ResolveColor(_currentTelegraphType));
        UpdateVisualTransform();
        _visualTransform.gameObject.SetActive(true);
        enabled = true;
    }

    public void HideCue()
    {
        _active = false;
        _trackedCollider = null;
        _ownerRoot = null;
        _nextTransformRefreshAt = 0f;
        _currentTelegraphType = AttackTelegraphType.Auto;

        if (_visualTransform != null)
            _visualTransform.gameObject.SetActive(false);

        enabled = _circleActive;
    }

    void EnsureVisual()
    {
        if (_visualTransform != null)
            return;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        quad.name = "GroundTelegraphVisual";
        quad.hideFlags = HideFlags.HideAndDontSave;
        quad.transform.SetParent(transform, false);

        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
            Destroy(quadCollider);

        _visualTransform = quad.transform;
        _visualRenderer = quad.GetComponent<MeshRenderer>();
        _visualTransform.localRotation = Quaternion.identity;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _runtimeMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _runtimeMaterial.renderQueue = (int)RenderQueue.Transparent;
            if (_runtimeMaterial.HasProperty("_Surface"))
                _runtimeMaterial.SetFloat("_Surface", 1f);
            if (_runtimeMaterial.HasProperty("_Blend"))
                _runtimeMaterial.SetFloat("_Blend", 0f);
            if (_runtimeMaterial.HasProperty("_Cull"))
                _runtimeMaterial.SetFloat("_Cull", 0f);
            if (_runtimeMaterial.HasProperty("_ZWrite"))
                _runtimeMaterial.SetFloat("_ZWrite", 0f);
            _visualRenderer.sharedMaterial = _runtimeMaterial;
        }

        _visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _visualRenderer.receiveShadows = false;
        _visualRenderer.lightProbeUsage = LightProbeUsage.Off;
        _visualRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _propertyBlock = new MaterialPropertyBlock();
    }

    void SetVisualColor(Color color)
    {
        if (_visualRenderer == null)
            return;

        _propertyBlock.Clear();
        _propertyBlock.SetColor("_BaseColor", color);
        _propertyBlock.SetColor("_Color", color);
        _visualRenderer.SetPropertyBlock(_propertyBlock);
    }

    void UpdateVisualTransform()
    {
        if (_visualTransform == null || _trackedCollider == null)
            return;

        if (!TryResolveFootprint(_trackedCollider, out Vector3 center, out Vector2 size))
            return;

        float padding = defaultPadding;
        if (_currentTelegraphType == AttackTelegraphType.Danger)
            padding += dangerExtraPadding;

        size.x = Mathf.Max(minimumWidth, size.x + padding);
        size.y = Mathf.Max(minimumLength, size.y + padding);

        float groundY = _ownerRoot != null ? _ownerRoot.position.y + yOffset : center.y + yOffset;
        _visualTransform.SetPositionAndRotation(
            new Vector3(center.x, groundY, center.z),
            Quaternion.Euler(0f, ResolveYaw(), 0f));
        _visualTransform.localScale = new Vector3(size.x, 0.03f, size.y);
        _nextTransformRefreshAt = Time.time + Mathf.Max(1f / 60f, transformRefreshInterval);
    }

    bool TryResolveFootprint(Collider sourceCollider, out Vector3 center, out Vector2 size)
    {
        center = Vector3.zero;
        size = Vector2.zero;

        if (sourceCollider == null)
            return false;

        switch (sourceCollider)
        {
            case BoxCollider boxCollider:
                return TryResolveBoundsFootprint(
                    boxCollider.transform,
                    boxCollider.center,
                    boxCollider.size * 0.5f,
                    out center,
                    out size);

            case SphereCollider sphereCollider:
            {
                Vector3 absScale = AbsVector(sphereCollider.transform.lossyScale);
                Vector3 worldCenter = sphereCollider.transform.TransformPoint(sphereCollider.center);
                float radius = sphereCollider.radius * Mathf.Max(absScale.x, absScale.z);
                center = worldCenter;
                size = new Vector2(radius * 2f, radius * 2f);
                return true;
            }

            case CapsuleCollider capsuleCollider:
            {
                Vector3 absScale = AbsVector(capsuleCollider.transform.lossyScale);
                int axis = Mathf.Clamp(capsuleCollider.direction, 0, 2);
                float axisScale = GetAxis(absScale, axis);
                float radiusScale = axis == 0
                    ? Mathf.Max(absScale.y, absScale.z)
                    : axis == 1
                        ? Mathf.Max(absScale.x, absScale.z)
                        : Mathf.Max(absScale.x, absScale.y);

                float radius = capsuleCollider.radius * radiusScale;
                float height = Mathf.Max(capsuleCollider.height * axisScale, radius * 2f);
                float halfSegment = Mathf.Max(0f, (height * 0.5f) - radius);
                Vector3 worldCenter = capsuleCollider.transform.TransformPoint(capsuleCollider.center);
                Vector3 axisDir = capsuleCollider.transform.TransformDirection(GetAxisVector(axis)).normalized;
                Vector3 pointA = worldCenter + axisDir * halfSegment;
                Vector3 pointB = worldCenter - axisDir * halfSegment;

                float minX = Mathf.Min(pointA.x, pointB.x) - radius;
                float maxX = Mathf.Max(pointA.x, pointB.x) + radius;
                float minZ = Mathf.Min(pointA.z, pointB.z) - radius;
                float maxZ = Mathf.Max(pointA.z, pointB.z) + radius;
                center = new Vector3((minX + maxX) * 0.5f, worldCenter.y, (minZ + maxZ) * 0.5f);
                size = new Vector2(maxX - minX, maxZ - minZ);
                return true;
            }

            case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                return TryResolveBoundsFootprint(
                    meshCollider.transform,
                    meshCollider.sharedMesh.bounds.center,
                    meshCollider.sharedMesh.bounds.extents,
                    out center,
                    out size);

            default:
            {
                Bounds bounds = sourceCollider.bounds;
                center = bounds.center;
                size = new Vector2(bounds.size.x, bounds.size.z);
                return true;
            }
        }
    }

    bool TryResolveBoundsFootprint(Transform targetTransform, Vector3 localCenter, Vector3 localExtents, out Vector3 center, out Vector2 size)
    {
        center = Vector3.zero;
        size = Vector2.zero;

        if (targetTransform == null)
            return false;

        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < 8; i++)
        {
            Vector3 localCorner = new Vector3(
                (i & 1) == 0 ? -localExtents.x : localExtents.x,
                (i & 2) == 0 ? -localExtents.y : localExtents.y,
                (i & 4) == 0 ? -localExtents.z : localExtents.z);
            Vector3 worldCorner = targetTransform.TransformPoint(localCenter + localCorner);
            min = Vector3.Min(min, worldCorner);
            max = Vector3.Max(max, worldCorner);
        }

        center = (min + max) * 0.5f;
        size = new Vector2(max.x - min.x, max.z - min.z);
        return true;
    }

    float ResolveYaw()
    {
        if (_ownerRoot == null)
            return 0f;

        Vector3 euler = _ownerRoot.rotation.eulerAngles;
        return euler.y;
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

    static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    static Vector3 GetAxisVector(int axis)
    {
        switch (axis)
        {
            case 0:
                return Vector3.right;
            case 1:
                return Vector3.up;
            default:
                return Vector3.forward;
        }
    }
}
