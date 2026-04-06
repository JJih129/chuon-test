using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class UltimateSwordCoreChargeFx : MonoBehaviour
{
    const int RingSegments = 25;
    const int BoltSegments = 6;

    [Header("Visual")]
    [SerializeField] private Color coreColor = new Color(0.38f, 0.95f, 1f, 1f);
    [SerializeField] private Color boltColor = new Color(0.82f, 0.97f, 1f, 0.95f);
    [SerializeField] private Color accentColor = new Color(0.72f, 0.48f, 1f, 0.95f);
    [SerializeField] private Color shadowColor = new Color(0.03f, 0.09f, 0.22f, 0.22f);
    [SerializeField] private Color hotCoreColor = new Color(1f, 0.84f, 0.98f, 0.96f);
    [SerializeField] [Range(2, 6)] private int boltCount = 3;
    [SerializeField] [Min(0.02f)] private float baseRadius = 0.085f;
    [SerializeField] [Min(0.01f)] private float innerRadiusScale = 0.58f;
    [SerializeField] [Min(0.005f)] private float ringWidth = 0.014f;
    [SerializeField] [Min(0.005f)] private float boltWidth = 0.018f;
    [SerializeField] [Min(0f)] private float pulseLightIntensity = 3.6f;
    [SerializeField] [Min(0f)] private float pulseLightRange = 1.05f;
    [SerializeField] [Min(0.02f)] private float coreGlowScale = 0.06f;
    [SerializeField] [Min(0.005f)] private float flareWidth = 0.018f;
    [SerializeField] [Min(0.02f)] private float backdropScale = 0.085f;
    [SerializeField] [Min(0f)] private float cameraFacingOffset = 0.025f;

    Transform _runtimeRoot;
    Transform _ownerRoot;
    Transform _followTarget;
    Transform _viewCamera;
    LineRenderer _outerRing;
    LineRenderer _innerRing;
    LineRenderer _horizontalFlare;
    LineRenderer _verticalFlare;
    LineRenderer[] _boltRenderers;
    Vector3[] _outerRingPositions;
    Vector3[] _innerRingPositions;
    Vector3[][] _boltPositions;
    Vector3[] _horizontalFlarePositions;
    Vector3[] _verticalFlarePositions;
    Light _coreLight;
    Transform _coreGlow;
    MeshRenderer _coreGlowRenderer;
    Transform _coreBackdrop;
    MeshRenderer _coreBackdropRenderer;
    Material _sharedLineMaterial;
    Material _sharedGlowMaterial;
    Material _sharedBackdropMaterial;
    Texture2D _sharedRadialTexture;
    float _elapsed;
    float _duration;
    bool _isPlaying;

    void Awake()
    {
        _ownerRoot = transform;
        EnsureBuilt();
        SetVisible(false);
        enabled = false;
    }

    void OnDisable()
    {
        if (_isPlaying)
            StopImmediate();
    }

    void OnDestroy()
    {
        ReleaseRuntimeVisuals();

        if (_sharedLineMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_sharedLineMaterial);
            else
                DestroyImmediate(_sharedLineMaterial);
        }

        if (_sharedGlowMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_sharedGlowMaterial);
            else
                DestroyImmediate(_sharedGlowMaterial);
        }

        if (_sharedBackdropMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_sharedBackdropMaterial);
            else
                DestroyImmediate(_sharedBackdropMaterial);
        }

        if (_sharedRadialTexture != null)
        {
            if (Application.isPlaying)
                Destroy(_sharedRadialTexture);
            else
                DestroyImmediate(_sharedRadialTexture);
        }
    }

    public void Play(Transform followTarget, float duration, Transform viewCamera = null)
    {
        if (followTarget == null)
        {
            StopImmediate();
            return;
        }

        if (_isPlaying)
            StopImmediate();

        RebuildVisualTree();
        EnsureBuilt();

        _followTarget = followTarget;
        _viewCamera = viewCamera;
        _duration = Mathf.Max(0.05f, duration);
        _elapsed = _duration * 0.18f;
        _isPlaying = true;

        _runtimeRoot.SetParent(_ownerRoot, false);
        _runtimeRoot.position = ResolveFollowPosition();
        _runtimeRoot.rotation = Quaternion.identity;
        _runtimeRoot.localScale = Vector3.one;
        SetLayerRecursively(_runtimeRoot, followTarget.gameObject.layer);
        SetVisible(true);
        Sample(Mathf.Clamp01(_elapsed / _duration));
        enabled = true;
    }

    public void Prime(Transform viewCamera = null)
    {
        _viewCamera = viewCamera;
        EnsureBuilt();
        SetVisible(false);
    }

    public void StopImmediate()
    {
        _isPlaying = false;
        _elapsed = 0f;
        _followTarget = null;
        _viewCamera = null;
        enabled = false;

        if (_runtimeRoot != null)
        {
            _runtimeRoot.SetParent(_ownerRoot, false);
            _runtimeRoot.localPosition = Vector3.zero;
            _runtimeRoot.localRotation = Quaternion.identity;
            _runtimeRoot.localScale = Vector3.one;
        }

        SetVisible(false);
    }

    void Update()
    {
        if (!_isPlaying || _runtimeRoot == null || _followTarget == null || !_followTarget)
        {
            StopImmediate();
            return;
        }

        if (!HasValidVisuals())
        {
            RebuildVisualTree();
            EnsureBuilt();
        }

        _runtimeRoot.position = ResolveFollowPosition();
        _elapsed += Time.unscaledDeltaTime;
        float normalized = Mathf.Clamp01(_elapsed / _duration);
        Sample(normalized);

        if (_elapsed >= _duration)
            StopImmediate();
    }

    void EnsureBuilt()
    {
        if (_runtimeRoot == null || !_runtimeRoot)
        {
            GameObject rootObject = new GameObject("__UltimateSwordCoreFx");
            rootObject.hideFlags = HideFlags.DontSave;
            _runtimeRoot = rootObject.transform;
            _runtimeRoot.SetParent(_ownerRoot, false);
        }

        if (_sharedLineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Particles/Additive");

            _sharedLineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };
            _sharedLineMaterial.renderQueue = 3500;
        }

        if (_sharedGlowMaterial == null)
        {
            Shader glowShader = Shader.Find("Sprites/Default");
            if (glowShader == null)
                glowShader = Shader.Find("Unlit/Color");
            if (glowShader == null)
                glowShader = Shader.Find("Legacy Shaders/Particles/Additive");

            _sharedGlowMaterial = new Material(glowShader)
            {
                hideFlags = HideFlags.DontSave
            };
            _sharedGlowMaterial.renderQueue = 3501;
        }

        if (_sharedBackdropMaterial == null)
        {
            Shader backdropShader = Shader.Find("Sprites/Default");
            if (backdropShader == null)
                backdropShader = Shader.Find("Unlit/Color");
            if (backdropShader == null)
                backdropShader = Shader.Find("Legacy Shaders/Particles/Additive");

            _sharedBackdropMaterial = new Material(backdropShader)
            {
                hideFlags = HideFlags.DontSave
            };
            _sharedBackdropMaterial.renderQueue = 3498;
        }

        if (_sharedRadialTexture == null)
        {
            _sharedRadialTexture = CreateRadialTexture(64);
            _sharedGlowMaterial.mainTexture = _sharedRadialTexture;
            _sharedBackdropMaterial.mainTexture = _sharedRadialTexture;
        }

        if (_outerRing == null || !_outerRing)
            _outerRing = CreateLineRenderer("OuterRing", RingSegments, ringWidth);
        if (_innerRing == null || !_innerRing)
            _innerRing = CreateLineRenderer("InnerRing", RingSegments, ringWidth * 0.82f);
        if (_horizontalFlare == null || !_horizontalFlare)
            _horizontalFlare = CreateLineRenderer("HorizontalFlare", 2, flareWidth);
        if (_verticalFlare == null || !_verticalFlare)
            _verticalFlare = CreateLineRenderer("VerticalFlare", 2, flareWidth * 0.9f);

        int clampedBoltCount = Mathf.Clamp(boltCount, 2, 6);
        bool needsBoltRebuild = _boltRenderers == null || _boltRenderers.Length != clampedBoltCount || _boltPositions == null || _boltPositions.Length != clampedBoltCount;
        if (!needsBoltRebuild)
        {
            for (int i = 0; i < _boltRenderers.Length; i++)
            {
                if (_boltRenderers[i] == null || !_boltRenderers[i] || _boltPositions[i] == null || _boltPositions[i].Length != BoltSegments)
                {
                    needsBoltRebuild = true;
                    break;
                }
            }
        }

        if (needsBoltRebuild)
        {
            ClearBoltRenderers();
            _boltRenderers = new LineRenderer[clampedBoltCount];
            _boltPositions = new Vector3[clampedBoltCount][];
            for (int i = 0; i < clampedBoltCount; i++)
            {
                _boltRenderers[i] = CreateLineRenderer($"Bolt_{i}", BoltSegments, boltWidth);
                _boltRenderers[i].textureMode = LineTextureMode.Stretch;
                _boltPositions[i] = new Vector3[BoltSegments];
            }
        }

        if (_outerRingPositions == null || _outerRingPositions.Length != RingSegments)
            _outerRingPositions = new Vector3[RingSegments];
        if (_innerRingPositions == null || _innerRingPositions.Length != RingSegments)
            _innerRingPositions = new Vector3[RingSegments];
        if (_horizontalFlarePositions == null || _horizontalFlarePositions.Length != 2)
            _horizontalFlarePositions = new Vector3[2];
        if (_verticalFlarePositions == null || _verticalFlarePositions.Length != 2)
            _verticalFlarePositions = new Vector3[2];

        if (_coreLight == null || !_coreLight)
        {
            GameObject lightObject = new GameObject("CoreLight");
            lightObject.hideFlags = HideFlags.DontSave;
            lightObject.transform.SetParent(_runtimeRoot, false);
            _coreLight = lightObject.AddComponent<Light>();
            _coreLight.type = LightType.Point;
            _coreLight.shadows = LightShadows.None;
            _coreLight.renderMode = LightRenderMode.ForcePixel;
            _coreLight.color = coreColor;
            _coreLight.intensity = 0f;
            _coreLight.range = pulseLightRange;
        }

        if (_coreGlow == null || !_coreGlow || _coreGlowRenderer == null || !_coreGlowRenderer)
            EnsureCoreGlow();
        if (_coreBackdrop == null || !_coreBackdrop || _coreBackdropRenderer == null || !_coreBackdropRenderer)
            EnsureCoreBackdrop();
    }

    void Sample(float normalized)
    {
        if (!HasValidVisuals())
            return;

        AlignCoreVisualsToCamera();

        float charge = Mathf.SmoothStep(0f, 1f, normalized);
        float pulse = 0.7f + 0.3f * Mathf.Sin((_elapsed * 16f + 0.35f) * Mathf.PI * 2f);
        float radius = Mathf.Lerp(baseRadius * 1.15f, baseRadius * 0.62f, charge);
        float innerRadius = radius * innerRadiusScale;
        float currentRingWidth = Mathf.Lerp(ringWidth * 0.92f, ringWidth * 1.18f, charge) * pulse;
        float currentBoltWidth = Mathf.Lerp(boltWidth * 0.96f, boltWidth * 1.2f, charge) * pulse;
        float currentFlareWidth = Mathf.Lerp(flareWidth * 0.82f, flareWidth * 1.06f, charge) * pulse;

        Color outerRingColor = Color.Lerp(coreColor, accentColor, 0.36f) * new Color(1f, 1f, 1f, 0.96f);
        Color innerRingColor = Color.Lerp(boltColor, hotCoreColor, 0.32f) * new Color(1f, 1f, 1f, 0.9f);
        UpdateRing(_outerRing, _outerRingPositions, radius, 0f, currentRingWidth, outerRingColor);
        UpdateRing(_innerRing, _innerRingPositions, innerRadius, 90f, currentRingWidth * 0.92f, innerRingColor);
        UpdateFlare(_horizontalFlare, _horizontalFlarePositions, new Vector3(-radius * 0.68f, 0f, 0f), new Vector3(radius * 0.68f, 0f, 0f), currentFlareWidth, Color.Lerp(hotCoreColor, Color.white, 0.38f) * new Color(1f, 1f, 1f, 0.82f));
        UpdateFlare(_verticalFlare, _verticalFlarePositions, new Vector3(0f, -radius * 0.45f, 0f), new Vector3(0f, radius * 0.45f, 0f), currentFlareWidth * 0.85f, Color.Lerp(coreColor, accentColor, 0.58f) * new Color(1f, 1f, 1f, 0.76f));

        for (int i = 0; i < _boltRenderers.Length; i++)
        {
            LineRenderer bolt = _boltRenderers[i];
            Vector3[] positions = _boltPositions[i];
            if (bolt == null || !bolt || positions == null)
                continue;
            UpdateBolt(i, positions, radius, charge, pulse);
            bolt.widthMultiplier = currentBoltWidth;
            bolt.startColor = Color.Lerp(accentColor, hotCoreColor, 0.5f) * new Color(1f, 1f, 1f, 0.7f + 0.2f * charge);
            bolt.endColor = Color.Lerp(coreColor, Color.white, 0.42f) * new Color(1f, 1f, 1f, 1f);
            bolt.SetPositions(positions);
        }

        if (_coreBackdrop != null && _coreBackdrop)
        {
            float backdropPulse = Mathf.Lerp(backdropScale * 0.94f, backdropScale * 1.2f, charge) * (0.95f + 0.12f * pulse);
            _coreBackdrop.localScale = new Vector3(backdropPulse, backdropPulse, backdropPulse);
            if (_coreBackdropRenderer != null && _coreBackdropRenderer)
            {
                Color backdropColor = Color.Lerp(shadowColor, accentColor * 0.16f, charge * 0.2f);
                backdropColor.a = Mathf.Lerp(0.14f, 0.24f, charge);
                _coreBackdropRenderer.sharedMaterial.color = backdropColor;
            }
        }

        if (_coreGlow != null && _coreGlow)
        {
            float glowScale = Mathf.Lerp(coreGlowScale * 0.88f, coreGlowScale * 1.58f, charge) * pulse;
            _coreGlow.localScale = new Vector3(glowScale, glowScale, glowScale);
            if (_coreGlowRenderer != null && _coreGlowRenderer)
            {
                Color glowColor = Color.Lerp(Color.Lerp(accentColor, boltColor, 0.42f), hotCoreColor, 0.4f);
                glowColor.a = 0.72f + 0.12f * charge;
                _coreGlowRenderer.sharedMaterial.color = glowColor;
            }
        }

        if (_coreLight != null)
        {
            _coreLight.color = Color.Lerp(Color.Lerp(accentColor, hotCoreColor, 0.36f), coreColor, 0.62f);
            _coreLight.intensity = Mathf.Lerp(0.7f, pulseLightIntensity, charge) * pulse;
            _coreLight.range = Mathf.Lerp(pulseLightRange * 0.82f, pulseLightRange, charge);
        }
    }

    void UpdateRing(LineRenderer renderer, Vector3[] positions, float radius, float angleOffsetDeg, float width, Color color)
    {
        if (renderer == null || positions == null)
            return;

        float angleOffset = angleOffsetDeg * Mathf.Deg2Rad;
        for (int i = 0; i < RingSegments; i++)
        {
            float t = i / (float)(RingSegments - 1);
            float angle = t * Mathf.PI * 2f + angleOffset;
            positions[i] = angleOffsetDeg < 45f
                ? new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.72f, 0f)
                : new Vector3(0f, Mathf.Sin(angle) * radius * 0.8f, Mathf.Cos(angle) * radius);
        }

        renderer.widthMultiplier = width;
        renderer.startColor = color;
        renderer.endColor = color;
        renderer.SetPositions(positions);
    }

    void UpdateBolt(int boltIndex, Vector3[] positions, float radius, float charge, float pulse)
    {
        float phase = boltIndex * (Mathf.PI * 2f / Mathf.Max(1, _boltRenderers.Length));
        float spin = _elapsed * (5.2f + boltIndex * 0.37f) + phase;
        Vector3 orbit = new Vector3(
            Mathf.Cos(spin) * radius,
            Mathf.Sin(spin * 1.31f) * radius * 0.56f,
            Mathf.Sin(spin + phase * 0.35f) * radius * 0.82f);

        Vector3 orbitDir = orbit.sqrMagnitude > 0.0001f ? orbit.normalized : Vector3.forward;
        Vector3 tangent = Vector3.Cross(Vector3.up, orbitDir);
        if (tangent.sqrMagnitude <= 0.0001f)
            tangent = Vector3.right;
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(orbitDir, tangent).normalized;

        float jitterBase = radius * Mathf.Lerp(0.7f, 0.2f, charge) * pulse;
        for (int i = 0; i < BoltSegments; i++)
        {
            float t = i / (float)(BoltSegments - 1);
            Vector3 inward = Vector3.Lerp(orbit, Vector3.zero, t);
            float wiggle = (1f - t) * jitterBase;
            float waveA = Mathf.Sin((_elapsed * 22f) + phase + t * 5.7f);
            float waveB = Mathf.Cos((_elapsed * 19f) + phase * 0.7f + t * 6.4f);
            positions[i] = inward + (tangent * waveA + bitangent * waveB) * wiggle * 0.16f;
        }
    }

    void UpdateFlare(LineRenderer renderer, Vector3[] positions, Vector3 start, Vector3 end, float width, Color color)
    {
        if (renderer == null || !renderer || positions == null || positions.Length < 2)
            return;

        positions[0] = start;
        positions[1] = end;
        renderer.widthMultiplier = width;
        renderer.startColor = color;
        renderer.endColor = color;
        renderer.SetPositions(positions);
    }

    LineRenderer CreateLineRenderer(string nodeName, int positionCount, float width)
    {
        Transform existing = _runtimeRoot.Find(nodeName);
        GameObject lineObject = existing != null ? existing.gameObject : new GameObject(nodeName);
        lineObject.hideFlags = HideFlags.DontSave;
        lineObject.transform.SetParent(_runtimeRoot, false);

        LineRenderer renderer = lineObject.GetComponent<LineRenderer>();
        if (renderer == null)
            renderer = lineObject.AddComponent<LineRenderer>();

        renderer.sharedMaterial = _sharedLineMaterial;
        renderer.loop = positionCount == RingSegments;
        renderer.useWorldSpace = false;
        renderer.positionCount = positionCount;
        renderer.widthMultiplier = width;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = LineAlignment.View;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.numCornerVertices = 2;
        renderer.numCapVertices = 2;
        renderer.generateLightingData = false;
        renderer.sortingOrder = 45;
        return renderer;
    }

    void EnsureCoreGlow()
    {
        _coreGlow = CreateQuad("CoreGlow", coreGlowScale);
        _coreGlowRenderer = _coreGlow != null ? _coreGlow.GetComponent<MeshRenderer>() : null;
        if (_coreGlowRenderer != null)
        {
            _coreGlowRenderer.sharedMaterial = _sharedGlowMaterial;
            _coreGlowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _coreGlowRenderer.receiveShadows = false;
            _coreGlowRenderer.sharedMaterial.color = coreColor;
            _coreGlowRenderer.sortingOrder = 48;
        }
    }

    void EnsureCoreBackdrop()
    {
        _coreBackdrop = CreateQuad("CoreBackdrop", backdropScale);
        _coreBackdropRenderer = _coreBackdrop != null ? _coreBackdrop.GetComponent<MeshRenderer>() : null;
        if (_coreBackdropRenderer != null)
        {
            _coreBackdropRenderer.sharedMaterial = _sharedBackdropMaterial;
            _coreBackdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _coreBackdropRenderer.receiveShadows = false;
            _coreBackdropRenderer.sharedMaterial.color = shadowColor;
            _coreBackdropRenderer.sortingOrder = 44;
        }

        if (_coreBackdrop != null)
            _coreBackdrop.localPosition = new Vector3(0f, 0f, 0.01f);
    }

    Transform CreateQuad(string nodeName, float scale)
    {
        GameObject quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadObject.name = nodeName;
        quadObject.hideFlags = HideFlags.DontSave;
        quadObject.transform.SetParent(_runtimeRoot, false);
        quadObject.transform.localPosition = Vector3.zero;
        quadObject.transform.localRotation = Quaternion.identity;
        quadObject.transform.localScale = Vector3.one * scale;

        Collider quadCollider = quadObject.GetComponent<Collider>();
        if (quadCollider != null)
        {
            if (Application.isPlaying)
                Destroy(quadCollider);
            else
                DestroyImmediate(quadCollider);
        }

        return quadObject.transform;
    }

    Texture2D CreateRadialTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = "UltimateSwordCoreRadial",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color32[] pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;
                float ny = (y - half) / half;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Clamp01(1f - radius);
                alpha = alpha * alpha * (3f - 2f * alpha);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    void ClearBoltRenderers()
    {
        if (_boltRenderers == null)
            return;

        for (int i = 0; i < _boltRenderers.Length; i++)
        {
            if (_boltRenderers[i] == null)
                continue;

            if (Application.isPlaying)
                Destroy(_boltRenderers[i].gameObject);
            else
                DestroyImmediate(_boltRenderers[i].gameObject);
        }

        _boltRenderers = null;
        _boltPositions = null;
    }

    void RebuildVisualTree()
    {
        ReleaseRuntimeVisuals();
        ClearVisualRefs();
    }

    void ReleaseRuntimeVisuals()
    {
        if (_runtimeRoot == null || !_runtimeRoot)
            return;

        if (Application.isPlaying)
            Destroy(_runtimeRoot.gameObject);
        else
            DestroyImmediate(_runtimeRoot.gameObject);
    }

    void ClearVisualRefs()
    {
        _runtimeRoot = null;
        _outerRing = null;
        _innerRing = null;
        _horizontalFlare = null;
        _verticalFlare = null;
        _boltRenderers = null;
        _boltPositions = null;
        _outerRingPositions = null;
        _innerRingPositions = null;
        _horizontalFlarePositions = null;
        _verticalFlarePositions = null;
        _coreLight = null;
        _coreGlow = null;
        _coreGlowRenderer = null;
        _coreBackdrop = null;
        _coreBackdropRenderer = null;
    }

    void AlignCoreVisualsToCamera()
    {
        Transform cameraTransform = ResolveViewCamera();
        if (cameraTransform == null)
            return;

        Quaternion billboardRotation = cameraTransform.rotation;
        if (_coreGlow != null && _coreGlow)
            _coreGlow.rotation = billboardRotation;
        if (_coreBackdrop != null && _coreBackdrop)
            _coreBackdrop.rotation = billboardRotation;
    }

    Transform ResolveViewCamera()
    {
        if (_viewCamera != null && _viewCamera)
            return _viewCamera;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    Vector3 ResolveFollowPosition()
    {
        if (_followTarget == null || !_followTarget)
            return _runtimeRoot != null ? _runtimeRoot.position : transform.position;

        Vector3 position = _followTarget.position;
        Transform cameraTransform = ResolveViewCamera();
        if (cameraTransform != null)
        {
            Vector3 toCamera = cameraTransform.position - position;
            float sqrMagnitude = toCamera.sqrMagnitude;
            if (sqrMagnitude > 0.0001f)
                position += toCamera / Mathf.Sqrt(sqrMagnitude) * cameraFacingOffset;
        }

        return position;
    }

    void SetVisible(bool visible)
    {
        if (_runtimeRoot != null)
            _runtimeRoot.gameObject.SetActive(visible);
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    bool HasValidVisuals()
    {
        if (_runtimeRoot == null || !_runtimeRoot)
            return false;
        if (_outerRing == null || !_outerRing)
            return false;
        if (_innerRing == null || !_innerRing)
            return false;
        if (_horizontalFlare == null || !_horizontalFlare)
            return false;
        if (_verticalFlare == null || !_verticalFlare)
            return false;
        if (_boltRenderers == null || _boltPositions == null || _boltRenderers.Length == 0 || _boltRenderers.Length != _boltPositions.Length)
            return false;
        for (int i = 0; i < _boltRenderers.Length; i++)
        {
            if (_boltRenderers[i] == null || !_boltRenderers[i] || _boltPositions[i] == null || _boltPositions[i].Length != BoltSegments)
                return false;
        }

        if (_coreGlow == null || !_coreGlow || _coreGlowRenderer == null || !_coreGlowRenderer)
            return false;
        if (_coreBackdrop == null || !_coreBackdrop || _coreBackdropRenderer == null || !_coreBackdropRenderer)
            return false;

        return _coreLight != null && _coreLight;
    }
}
