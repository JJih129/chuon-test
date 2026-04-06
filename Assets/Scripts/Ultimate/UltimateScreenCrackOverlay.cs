using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class UltimateScreenCrackOverlay : MonoBehaviour
{
    const int DefaultPoolSize = 24;

    struct CrackInstance
    {
        public Transform transform;
        public LineRenderer line;
        public float elapsed;
        public float lifetime;
        public float startAlpha;
        public bool active;
    }

    [Header("Overlay")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private int poolSize = DefaultPoolSize;
    [SerializeField] private float overlayDistance = 0.85f;
    [SerializeField] private Color crackColor = new Color(0.92f, 0.97f, 1f, 1f);

    static Material s_sharedMaterial;
    static AnimationCurve s_widthCurve;

    Transform _overlayRoot;
    CrackInstance[] _instances;
    int _nextIndex;

    void Awake()
    {
        EnsureResources();
        ClearImmediate();
    }

    void LateUpdate()
    {
        if (_instances == null)
            return;

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        for (int i = 0; i < _instances.Length; i++)
        {
            if (!_instances[i].active)
                continue;

            CrackInstance instance = _instances[i];
            instance.elapsed += deltaTime;
            float normalized = Mathf.Clamp01(instance.elapsed / Mathf.Max(0.0001f, instance.lifetime));
            float alpha = instance.startAlpha * (1f - normalized);

            if (alpha <= 0.001f)
            {
                instance.active = false;
                if (instance.transform != null)
                    instance.transform.gameObject.SetActive(false);
            }
            else
            {
                ApplyColor(instance.line, alpha);
            }

            _instances[i] = instance;
        }
    }

    public void BindCamera(Camera camera)
    {
        targetCamera = camera;
        EnsureResources();

        if (_overlayRoot == null || targetCamera == null)
            return;

        _overlayRoot.SetParent(targetCamera.transform, false);
        _overlayRoot.localPosition = Vector3.zero;
        _overlayRoot.localRotation = Quaternion.identity;
    }

    public void ClearImmediate()
    {
        if (_instances == null)
            return;

        for (int i = 0; i < _instances.Length; i++)
        {
            CrackInstance instance = _instances[i];
            instance.active = false;
            instance.elapsed = 0f;
            if (instance.transform != null)
                instance.transform.gameObject.SetActive(false);
            _instances[i] = instance;
        }
    }

    public void EmitSlashCrack(Vector3 worldAnchor, Vector3 worldDirection, UltimateSequenceData.VfxSettings settings, int seed)
    {
        if (settings == null || !settings.useSlashScreenCrackOverlay)
            return;

        Camera camera = ResolveCamera();
        if (camera == null)
            return;

        Vector3 viewport = camera.WorldToViewportPoint(worldAnchor);
        if (viewport.z <= 0.01f)
            return;

        Vector3 directionEnd = worldAnchor + (worldDirection.sqrMagnitude > 0.0001f ? worldDirection.normalized : Vector3.right) * 1.4f;
        Vector3 viewportEnd = camera.WorldToViewportPoint(directionEnd);
        Vector2 screenDirection = new Vector2(viewportEnd.x - viewport.x, viewportEnd.y - viewport.y);
        if (screenDirection.sqrMagnitude <= 0.00001f)
            screenDirection = Vector2.right;
        screenDirection.Normalize();

        float angle = Mathf.Atan2(screenDirection.y, screenDirection.x) * Mathf.Rad2Deg;
        float lifetime = Mathf.Max(0.05f, settings.screenCrackLifetime);
        float width = Mathf.Max(0.001f, settings.screenCrackWidth);
        float length = Mathf.Max(0.02f, settings.screenCrackLength);
        float alpha = Mathf.Clamp01(settings.screenCrackAlpha);

        SpawnCrack(viewport, angle, width, length, alpha, lifetime);

        Vector2 perpendicular = new Vector2(-screenDirection.y, screenDirection.x);
        int branchCount = Mathf.Max(0, settings.slashCrackBranchCount);
        for (int i = 0; i < branchCount; i++)
        {
            float t = branchCount <= 1 ? 0.5f : i / (float)(branchCount - 1);
            float branchAngle = Mathf.Lerp(-settings.screenCrackBranchAngle, settings.screenCrackBranchAngle, t);
            float offsetSign = Hash01(seed, i + 17) >= 0.5f ? 1f : -1f;
            float forwardOffset = Mathf.Lerp(0.01f, 0.04f, Hash01(seed, i + 53));
            float lateralOffset = Mathf.Lerp(0.004f, 0.02f, Hash01(seed, i + 91)) * offsetSign;
            Vector2 branchViewport = new Vector2(
                viewport.x + screenDirection.x * forwardOffset + perpendicular.x * lateralOffset,
                viewport.y + screenDirection.y * forwardOffset + perpendicular.y * lateralOffset);

            SpawnCrack(
                branchViewport,
                angle + branchAngle,
                width * 0.72f,
                length * Mathf.Clamp(settings.screenCrackBranchScale, 0.1f, 1f),
                alpha * 0.72f,
                lifetime);
        }
    }

    public void EmitBurstCrack(Vector3 worldAnchor, UltimateSequenceData.VfxSettings settings, float intensityScale, int seed)
    {
        if (settings == null || !settings.useBurstScreenCrackOverlay)
            return;

        Camera camera = ResolveCamera();
        if (camera == null)
            return;

        Vector3 viewport = camera.WorldToViewportPoint(worldAnchor);
        if (viewport.z <= 0.01f)
            return;

        int lineCount = Mathf.Max(1, settings.burstCrackLineCount);
        float width = Mathf.Max(0.001f, settings.screenCrackWidth * Mathf.Lerp(1f, 1.2f, Mathf.Clamp01(intensityScale - 1f)));
        float length = Mathf.Max(0.02f, settings.screenCrackLength * Mathf.Lerp(0.9f, 1.15f, Mathf.Clamp01(intensityScale - 1f)));
        float lifetime = Mathf.Max(0.05f, settings.screenCrackLifetime * 1.15f);
        float alpha = Mathf.Clamp01(settings.screenCrackAlpha * 0.95f);

        for (int i = 0; i < lineCount; i++)
        {
            float angle = (360f / lineCount) * i + Mathf.Lerp(-8f, 8f, Hash01(seed, i + 137));
            float radialOffset = Mathf.Lerp(0.0f, 0.025f, Hash01(seed, i + 173));
            float rad = angle * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radialOffset;
            SpawnCrack(
                new Vector2(viewport.x + offset.x, viewport.y + offset.y),
                angle,
                width * Mathf.Lerp(0.8f, 1.12f, Hash01(seed, i + 211)),
                length * Mathf.Lerp(0.72f, 1.08f, Hash01(seed, i + 257)),
                alpha,
                lifetime);
        }
    }

    void SpawnCrack(Vector2 viewportPoint, float angle, float widthNormalized, float lengthNormalized, float alpha, float lifetime)
    {
        EnsureResources();
        if (_instances == null || _instances.Length == 0)
            return;

        viewportPoint.x = Mathf.Clamp01(viewportPoint.x);
        viewportPoint.y = Mathf.Clamp01(viewportPoint.y);

        int instanceIndex = _nextIndex;
        CrackInstance instance = _instances[instanceIndex];
        _nextIndex = (_nextIndex + 1) % _instances.Length;

        if (instance.transform == null || instance.line == null)
            return;

        Camera camera = ResolveCamera();
        if (camera == null)
            return;

        Vector3 center = ViewportToLocalPosition(viewportPoint, overlayDistance);
        float visibleHeight = 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * overlayDistance;
        float visibleWidth = visibleHeight * camera.aspect;
        float width = Mathf.Max(0.0025f, visibleWidth * widthNormalized);
        float length = Mathf.Max(0.06f, visibleHeight * lengthNormalized);

        float angleRad = angle * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0f);
        Vector3 halfSpan = direction * (length * 0.5f);

        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        instance.line.widthMultiplier = width;
        instance.line.SetPosition(0, center - halfSpan);
        instance.line.SetPosition(1, center + halfSpan);
        instance.elapsed = 0f;
        instance.lifetime = lifetime;
        instance.startAlpha = alpha;
        instance.active = true;
        instance.transform.gameObject.SetActive(true);
        ApplyColor(instance.line, alpha);

        _instances[instanceIndex] = instance;
    }

    void EnsureResources()
    {
        if (s_sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            s_sharedMaterial = new Material(shader)
            {
                name = "UltimateScreenCrackOverlay_LineMat"
            };
            s_sharedMaterial.renderQueue = 3100;
        }

        if (s_widthCurve == null)
        {
            s_widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.88f, 1f),
                new Keyframe(1f, 0f));
        }

        if (_overlayRoot == null)
        {
            GameObject root = new GameObject("UltimateScreenCrackOverlayRoot");
            _overlayRoot = root.transform;
            if (targetCamera != null)
                _overlayRoot.SetParent(targetCamera.transform, false);
            else
                _overlayRoot.SetParent(transform, false);
            _overlayRoot.localPosition = Vector3.zero;
            _overlayRoot.localRotation = Quaternion.identity;
        }

        int desiredPoolSize = Mathf.Max(4, poolSize);
        if (_instances != null && _instances.Length == desiredPoolSize)
            return;

        _instances = new CrackInstance[desiredPoolSize];
        for (int i = 0; i < desiredPoolSize; i++)
        {
            GameObject go = new GameObject($"CrackOverlay_{i:00}");
            go.SetActive(false);
            go.transform.SetParent(_overlayRoot, false);

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 4;
            line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.allowOcclusionWhenDynamic = false;
            line.widthCurve = s_widthCurve;
            line.sharedMaterial = s_sharedMaterial;

            _instances[i] = new CrackInstance
            {
                transform = go.transform,
                line = line,
                active = false
            };
        }
    }

    Camera ResolveCamera()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        return targetCamera;
    }

    Vector3 ViewportToLocalPosition(Vector2 viewportPoint, float depth)
    {
        Camera camera = ResolveCamera();
        if (camera == null)
            return new Vector3(0f, 0f, depth);

        float halfHeight = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * depth;
        float halfWidth = halfHeight * camera.aspect;
        float x = (viewportPoint.x - 0.5f) * 2f * halfWidth;
        float y = (viewportPoint.y - 0.5f) * 2f * halfHeight;
        return new Vector3(x, y, depth);
    }

    void ApplyColor(LineRenderer line, float alpha)
    {
        if (line == null)
            return;

        Color color = crackColor;
        color.a *= Mathf.Clamp01(alpha);
        line.startColor = color;
        line.endColor = color;
    }

    static float Hash01(int seed, int salt)
    {
        unchecked
        {
            uint value = (uint)(seed * 73856093) ^ (uint)(salt * 19349663) ^ 0x9E3779B9u;
            value ^= value >> 16;
            value *= 2246822519u;
            value ^= value >> 13;
            value *= 3266489917u;
            value ^= value >> 16;
            return (value & 0x00FFFFFF) / 16777215f;
        }
    }
}
