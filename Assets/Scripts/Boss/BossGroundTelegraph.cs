using UnityEngine;
using UnityEngine.Rendering;

// 보스 바닥 텔레그래프 (반복 10 재구현)
// 기존: Sprites/Default 큐브 + 평면 원판 (원시 도형처럼 보임).
// 현재: 바닥에 붙인 평면 메시 + ChuOn/TelegraphGround 셰이더 (소프트 엣지, HDR 테두리, 진행 방향 쉐브론,
//       채움이 피격 순간에 끝에 닿는 '정직한' 진행 표시). 원형은 같은 셰이더의 원형 모드(충격파/도약 그림자).
// 색 언어: 패링 가능 = 금/주황, 회피 전용 = 빨강, 가드·패링 불가 = 자홍, 일반(가드) = 청색. 색각 보정 팔레트 지원.
[DisallowMultipleComponent]
public sealed class BossGroundTelegraph : MonoBehaviour
{
    const string MaterialResourcePath = "VFX/ChuonTelegraphGround";

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
    [Tooltip("HDR 배율 (블룸이 테두리/파면을 잡도록)")]
    [SerializeField, Min(0.5f)] private float hdrIntensity = 2.4f;
    [Tooltip("마지막 몇 초 동안 테두리가 '임박' 상태로 밝아진다")]
    [SerializeField, Min(0.05f)] private float urgencyWindow = 0.4f;
    [SerializeField] private Material groundMaterial;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int ShapeId = Shader.PropertyToID("_Shape");
    static readonly int AspectId = Shader.PropertyToID("_Aspect");
    static readonly int UrgencyId = Shader.PropertyToID("_Urgency");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int ShadowId = Shader.PropertyToID("_Shadow");

    static Mesh s_quadMesh;

    Transform _visualTransform;
    MeshRenderer _visualRenderer;
    Material _runtimeMaterial;
    MaterialPropertyBlock _propertyBlock;
    Collider _trackedCollider;
    Transform _ownerRoot;
    bool _active;
    float _showTime;
    float _hideAtTime;
    float _nextTransformRefreshAt;
    float _rectAspect = 1f;
    Color _rectColor;
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

        UpdateRectAnimation();
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
        if (_visualTransform != null)
            Destroy(_visualTransform.gameObject);
        if (_circleRoot != null)
            Destroy(_circleRoot.gameObject);
    }

    // ==================== 원형 텔레그래프 (충격파 / 도약 그림자) ====================
    // 테두리 링 + 안쪽에서 바깥으로 차오르는 채움(= 남은 시간). 채움이 테두리에 닿는 순간이 피격 시점 (정직한 큐).
    // shadowStyle: 도약 내려찍기용. 어두운 그림자 원판이 follow 위치(플레이어)를 따라가다 LockCircle() 후 고정.
    [Header("Circle Telegraph")]
    [SerializeField, Range(0.5f, 0.99f)] private float circleRimInner = 0.93f;
    [SerializeField] private Color shadowFillColor = new Color(0.02f, 0.02f, 0.03f, 0.62f);

    Transform _circleRoot;
    MeshRenderer _circleRenderer;
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
        _circleColor = ResolveHdrColor(telegraphType);
        _circleRoot.gameObject.SetActive(true);
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

        // 피격 후 0.12초 동안 테두리가 밝게 터지며 사라진다
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
        float remaining = _circleEndTime - Time.time;
        float urgency = remaining < urgencyWindow ? 1f - Mathf.Clamp01(remaining / urgencyWindow) : 0f;
        float after = Mathf.Clamp01((Time.time - _circleEndTime) / 0.12f);

        float diameter = _circleRadius * 2f;
        _circleRoot.SetPositionAndRotation(_circleCenter, Quaternion.identity);
        _circleRoot.localScale = new Vector3(diameter, 1f, diameter);

        _circleBlock.Clear();
        _circleBlock.SetColor(ColorId, _circleColor * (1f + urgency * 0.6f + after * 1.5f));
        _circleBlock.SetFloat(FillId, progress);
        _circleBlock.SetFloat(ShapeId, 1f);
        _circleBlock.SetFloat(AspectId, 1f);
        _circleBlock.SetFloat(UrgencyId, Mathf.Max(urgency, after));
        _circleBlock.SetFloat(OpacityId, Mathf.Clamp01(Mathf.Min(1f, (Time.time - _circleStartTime) / 0.08f)) * (1f - after));
        _circleBlock.SetFloat(ShadowId, _circleShadow ? 1f : 0f);
        _circleRenderer.SetPropertyBlock(_circleBlock);
    }

    void EnsureCircleVisual()
    {
        if (_circleRoot != null)
            return;

        var go = new GameObject("GroundTelegraphCircle", typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = HideFlags.DontSave;
        _circleRoot = go.transform;
        _circleRoot.SetParent(null, false);
        go.GetComponent<MeshFilter>().sharedMesh = QuadMesh;
        _circleRenderer = go.GetComponent<MeshRenderer>();
        ConfigureRenderer(_circleRenderer);
        _circleBlock = new MaterialPropertyBlock();
        go.SetActive(false);
    }

    public void ShowCue(Collider trackedCollider, AttackTelegraphType telegraphType, float duration, Transform ownerRoot = null)
    {
        if (trackedCollider == null)
            return;

        EnsureVisual();
        _trackedCollider = trackedCollider;
        _ownerRoot = ownerRoot != null ? ownerRoot : trackedCollider.transform.root;
        _showTime = Time.time;
        _hideAtTime = Time.time + Mathf.Max(0.06f, duration);
        _active = true;
        _nextTransformRefreshAt = 0f;
        _currentTelegraphType = telegraphType;
        _rectColor = ResolveHdrColor(_currentTelegraphType);

        UpdateVisualTransform();
        UpdateRectAnimation();
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

        var go = new GameObject("GroundTelegraphVisual", typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(null, false);
        go.GetComponent<MeshFilter>().sharedMesh = QuadMesh;
        _visualTransform = go.transform;
        _visualRenderer = go.GetComponent<MeshRenderer>();
        ConfigureRenderer(_visualRenderer);
        _propertyBlock = new MaterialPropertyBlock();
        go.SetActive(false);
    }

    void ConfigureRenderer(MeshRenderer target)
    {
        target.sharedMaterial = ResolveMaterial();
        target.shadowCastingMode = ShadowCastingMode.Off;
        target.receiveShadows = false;
        target.lightProbeUsage = LightProbeUsage.Off;
        target.reflectionProbeUsage = ReflectionProbeUsage.Off;
        target.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    Material ResolveMaterial()
    {
        if (groundMaterial != null)
            return groundMaterial;
        if (_runtimeMaterial != null)
            return _runtimeMaterial;

        Material source = Resources.Load<Material>(MaterialResourcePath);
        if (source != null)
            return groundMaterial = source;

        // 폴백 (셰이더가 빌드에 없을 때만)
        Shader shader = Shader.Find("ChuOn/TelegraphGround") ?? Shader.Find("Sprites/Default");
        _runtimeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave, renderQueue = (int)RenderQueue.Transparent };
        return _runtimeMaterial;
    }

    // 1x1 평면 (XZ), uv.y = +Z 방향 (보스 쪽 0 → 먼 끝 1)
    static Mesh QuadMesh
    {
        get
        {
            if (s_quadMesh != null)
                return s_quadMesh;
            s_quadMesh = new Mesh { name = "_TelegraphGroundQuad", hideFlags = HideFlags.DontSave };
            s_quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f)
            };
            s_quadMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_quadMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            s_quadMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            s_quadMesh.RecalculateBounds();
            return s_quadMesh;
        }
    }

    void UpdateRectAnimation()
    {
        if (_visualRenderer == null)
            return;

        float duration = Mathf.Max(0.01f, _hideAtTime - _showTime);
        float progress = Mathf.Clamp01((Time.time - _showTime) / duration);
        float remaining = _hideAtTime - Time.time;
        float urgency = remaining < urgencyWindow ? 1f - Mathf.Clamp01(remaining / urgencyWindow) : 0f;

        _propertyBlock.Clear();
        _propertyBlock.SetColor(ColorId, _rectColor * (1f + urgency * 0.5f));
        _propertyBlock.SetFloat(FillId, progress);
        _propertyBlock.SetFloat(ShapeId, 0f);
        _propertyBlock.SetFloat(AspectId, _rectAspect);
        _propertyBlock.SetFloat(UrgencyId, urgency);
        _propertyBlock.SetFloat(OpacityId, Mathf.Clamp01((Time.time - _showTime) / 0.08f));
        _propertyBlock.SetFloat(ShadowId, 0f);
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
        _rectAspect = size.y / Mathf.Max(0.05f, size.x);

        float groundY = _ownerRoot != null ? _ownerRoot.position.y + yOffset : center.y + yOffset;
        _visualTransform.SetPositionAndRotation(
            new Vector3(center.x, groundY, center.z),
            Quaternion.Euler(0f, ResolveYaw(), 0f));
        _visualTransform.localScale = new Vector3(size.x, 1f, size.y);
        _nextTransformRefreshAt = Time.time + Mathf.Max(1f / 60f, transformRefreshInterval);
    }

    bool TryResolveFootprint(Collider sourceCollider, out Vector3 center, out Vector2 size)
    {
        center = Vector3.zero;
        size = Vector2.zero;

        if (sourceCollider == null)
            return false;

        // 발자국은 보스 기준 축(요)으로 잰다 (월드 AABB 는 비스듬한 보스에서 커진다)
        Quaternion inv = Quaternion.Inverse(Quaternion.Euler(0f, ResolveYaw(), 0f));

        switch (sourceCollider)
        {
            case BoxCollider boxCollider:
                return TryResolveBoundsFootprint(boxCollider.transform, boxCollider.center, boxCollider.size * 0.5f, inv, out center, out size);

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
                Vector3 a = inv * (worldCenter + axisDir * halfSegment);
                Vector3 b = inv * (worldCenter - axisDir * halfSegment);

                float minX = Mathf.Min(a.x, b.x) - radius;
                float maxX = Mathf.Max(a.x, b.x) + radius;
                float minZ = Mathf.Min(a.z, b.z) - radius;
                float maxZ = Mathf.Max(a.z, b.z) + radius;
                Vector3 localCenter = new Vector3((minX + maxX) * 0.5f, (a.y + b.y) * 0.5f, (minZ + maxZ) * 0.5f);
                center = Quaternion.Inverse(inv) * localCenter;
                size = new Vector2(maxX - minX, maxZ - minZ);
                return true;
            }

            case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                return TryResolveBoundsFootprint(meshCollider.transform, meshCollider.sharedMesh.bounds.center, meshCollider.sharedMesh.bounds.extents, inv, out center, out size);

            default:
            {
                Bounds bounds = sourceCollider.bounds;
                center = bounds.center;
                size = new Vector2(bounds.size.x, bounds.size.z);
                return true;
            }
        }
    }

    static bool TryResolveBoundsFootprint(Transform targetTransform, Vector3 localCenter, Vector3 localExtents, Quaternion toFrame, out Vector3 center, out Vector2 size)
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
            Vector3 corner = toFrame * targetTransform.TransformPoint(localCenter + localCorner);
            min = Vector3.Min(min, corner);
            max = Vector3.Max(max, corner);
        }

        center = Quaternion.Inverse(toFrame) * ((min + max) * 0.5f);
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

    Color ResolveHdrColor(AttackTelegraphType telegraphType)
    {
        Color c = BossTelegraphVfxPresenter.UseColorblindSafePalette
            ? ResolveColorblindColor(telegraphType)
            : ResolveColor(telegraphType);
        c.a = 1f;
        return c * hdrIntensity;
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

    // BossTelegraphVfxPresenter 의 Okabe-Ito 팔레트와 동일
    static Color ResolveColorblindColor(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return new Color(0.94f, 0.89f, 0.26f);
            case AttackTelegraphType.Guard:
                return new Color(0.34f, 0.71f, 0.91f);
            case AttackTelegraphType.Danger:
                return new Color(0.8f, 0.47f, 0.65f);
            default:
                return new Color(0.84f, 0.37f, 0f);
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
