using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossPatternHandTelegraph : MonoBehaviour
{
    const string PivotName = "PatternPivot";
    const float PivotLookupCooldown = 2f;
    float _nextPivotLookupAt;
    const string MaterialPath = "Assets/Eric VFX Studio/Resource/Materials/DungeonRingGuid.mat";
    const string TexturePath = "Assets/Eric VFX Studio/Resource/Textures/DungeonRingGuid.png";
    const string MaterialResourcePath = "Boss/DungeonRingGuid";
    const string TextureResourcePath = "Boss/DungeonRingGuid";
    const string OverlayShaderName = "ChuOn/AlwaysOnTopTransparent";

    [SerializeField] Transform patternPivot;
    [SerializeField] Material telegraphMaterial;
    [SerializeField, Min(0.05f)] float baseScale = 1.45f;
    [SerializeField, Min(0f)] float verticalOffset = 0.03f;
    [SerializeField, Min(0f)] float cameraPullForward = 0.58f;
    [SerializeField, Min(0f)] float pulseAmount = 0.26f;
    [SerializeField, Min(0f)] float pulseSpeed = 12f;
    [SerializeField, Min(0f)] float rotateSpeed = 185f;
    [SerializeField] Color fallbackColor = new Color(1f, 0.96f, 0.02f, 1f);

    GameObject _quad;
    Transform _quadTransform;
    Renderer _renderer;
    Material _runtimeMaterial;
    Camera _mainCamera;
    float _hideAt;
    float _rotation;
    float _scaleMultiplier = 1f;

    void Awake()
    {
        ResolvePivot();
        EnsureQuad();
        Hide();
    }

    void OnDestroy()
    {
        if (_quad != null)
            Destroy(_quad);
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }

    void LateUpdate()
    {
        if (_quad == null || !_quad.activeSelf)
            return;

        if (_hideAt > 0f && Time.time >= _hideAt)
        {
            Hide();
            return;
        }

        if (patternPivot == null)
            ResolvePivot();
        if (patternPivot == null)
            return;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        Vector3 cameraDirection = _mainCamera != null
            ? (_mainCamera.transform.position - patternPivot.position).normalized
            : Vector3.back;
        _quadTransform.position = patternPivot.position + Vector3.up * verticalOffset + cameraDirection * cameraPullForward;

        _rotation += rotateSpeed * Time.deltaTime;
        if (_mainCamera != null)
            _quadTransform.rotation = _mainCamera.transform.rotation * Quaternion.Euler(0f, 0f, _rotation);

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        _quadTransform.localScale = Vector3.one * (baseScale * _scaleMultiplier * Mathf.Max(0.05f, pulse));
    }

    public void Play(float duration)
    {
        Play(duration, fallbackColor, 1f);
    }

    // 다단 후속타용(BossController.StringHitCues): 텔레그래프 색 언어의 색과 작은 크기로 짧게 표시.
    // Play(duration) 은 항상 기본 색/크기로 되돌리므로 오프너 표시는 그대로다.
    public void Play(float duration, Color tint, float scaleMultiplier)
    {
        if (duration <= 0.01f)
            return;

        _scaleMultiplier = Mathf.Max(0.05f, scaleMultiplier);

        if (patternPivot == null)
            ResolvePivot();
        EnsureQuad();
        if (_quad == null || patternPivot == null)
            return;

        ApplyTint(tint);
        _hideAt = Time.time + duration;
        _quad.SetActive(true);
        LateUpdate();
    }

    void ApplyTint(Color tint)
    {
        if (_runtimeMaterial == null)
            return;

        float max = tint.maxColorComponent;
        Color ldr = max > 1f ? tint / max : tint; // HDR 팔레트 색을 LDR 로
        ldr.a = 1f;
        if (_runtimeMaterial.HasProperty("_Color"))
            _runtimeMaterial.SetColor("_Color", ldr);
        if (_runtimeMaterial.HasProperty("_BaseColor"))
            _runtimeMaterial.SetColor("_BaseColor", ldr);
        if (_runtimeMaterial.HasProperty("_EmissionColor"))
            _runtimeMaterial.SetColor("_EmissionColor", ldr * 2.8f);
    }

    public void Hide()
    {
        _hideAt = 0f;
        if (_quad != null)
            _quad.SetActive(false);
    }

    void ResolvePivot()
    {
        if (patternPivot != null)
            return;

        // 피벗 탐색 실패 시 매 Play 마다 전체 씬 스캔하지 않도록 쿨다운
        if (Time.unscaledTime < _nextPivotLookupAt)
            return;
        _nextPivotLookupAt = Time.unscaledTime + PivotLookupCooldown;

        Transform[] children = transform.root != null
            ? transform.root.GetComponentsInChildren<Transform>(true)
            : GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == PivotName)
            {
                patternPivot = children[i];
                return;
            }
        }

        Transform best = null;
        float bestSqrDistance = float.PositiveInfinity;
        Transform[] sceneTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform candidate = sceneTransforms[i];
            if (candidate == null || candidate.name != PivotName)
                continue;

            float sqrDistance = (candidate.position - transform.position).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            best = candidate;
            bestSqrDistance = sqrDistance;
        }

        patternPivot = best;
    }

    void EnsureQuad()
    {
        if (_quad != null)
            return;

        _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quad.name = "BossPatternHandTelegraph_Runtime";
        _quadTransform = _quad.transform;
        _quadTransform.SetParent(null, false);

        Collider collider = _quad.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        _renderer = _quad.GetComponent<Renderer>();
        if (_renderer != null)
        {
            _renderer.sharedMaterial = CreateRuntimeMaterial(ResolveMaterial());
            _renderer.sortingOrder = short.MaxValue;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }
    }

    Material ResolveMaterial()
    {
        if (telegraphMaterial != null)
            return telegraphMaterial;

        telegraphMaterial = Resources.Load<Material>(MaterialResourcePath);
        if (telegraphMaterial != null)
            return telegraphMaterial;

#if UNITY_EDITOR
        telegraphMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (telegraphMaterial != null)
            return telegraphMaterial;
#endif

        Shader shader = Shader.Find(OverlayShaderName) ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        Material material = new Material(shader)
        {
            name = "BossPatternHandTelegraph_Fallback",
            color = fallbackColor,
            renderQueue = 5000
        };
        return material;
    }

    Material CreateRuntimeMaterial(Material source)
    {
        if (_runtimeMaterial != null)
            return _runtimeMaterial;

        Shader overlayShader = Shader.Find(OverlayShaderName);
        _runtimeMaterial = overlayShader != null ? new Material(overlayShader) : (source != null ? new Material(source) : ResolveMaterial());
        _runtimeMaterial.name = "BossPatternHandTelegraph_RuntimeMat";
        _runtimeMaterial.renderQueue = 5000;

        Texture texture = Resources.Load<Texture>(TextureResourcePath);
#if UNITY_EDITOR
        if (texture == null)
            texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(TexturePath);
#endif
        if (texture != null && _runtimeMaterial.HasProperty("_MainTex"))
            _runtimeMaterial.SetTexture("_MainTex", texture);

        if (_runtimeMaterial.HasProperty("_Color"))
            _runtimeMaterial.SetColor("_Color", fallbackColor);
        if (_runtimeMaterial.HasProperty("_BaseColor"))
            _runtimeMaterial.SetColor("_BaseColor", fallbackColor);
        if (_runtimeMaterial.HasProperty("_EmissionColor"))
            _runtimeMaterial.SetColor("_EmissionColor", fallbackColor * 2.8f);

        return _runtimeMaterial;
    }
}
