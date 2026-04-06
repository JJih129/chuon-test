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
        HideCue();
    }

    void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
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

        enabled = false;
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
