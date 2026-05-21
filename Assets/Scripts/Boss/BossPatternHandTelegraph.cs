using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossPatternHandTelegraph : MonoBehaviour
{
    const string PivotName = "PatternPivot";
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
        _quadTransform.localScale = Vector3.one * (baseScale * Mathf.Max(0.05f, pulse));
    }

    public void Play(float duration)
    {
        if (duration <= 0.01f)
            return;

        if (patternPivot == null)
            ResolvePivot();
        EnsureQuad();
        if (_quad == null || patternPivot == null)
            return;

        _hideAt = Time.time + duration;
        _quad.SetActive(true);
        LateUpdate();
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
