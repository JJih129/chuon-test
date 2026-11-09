using UnityEngine;

// 보스 큐 '타이밍 핑' (반복 10 재구현)
// 기존: 손 피벗 위에 항상-위 노란 링 쿼드가 공지 시간 내내 회전 (원시 도형처럼 보이고, 타입과 무관하게 금색).
// 현재: 정직한 큐 순간에 헬멧(바이저) 앞에서 0.2초 동안 선명한 별빛 섬광 (ChuOn/TelegraphFlare, HDR → 블룸).
//       색은 텔레그래프 색 언어(패링 가능 금/주황, 회피 전용 빨강, 가드 불가 자홍, 강공 호박)를 그대로 쓴다.
//       '얼마나 남았나'는 헬멧/무기 발광 램프와 바닥 데칼 채움이 보여준다 (BossTelegraphVfxPresenter / BossGroundTelegraph).
[DisallowMultipleComponent]
public sealed class BossPatternHandTelegraph : MonoBehaviour
{
    const string PivotName = "PatternPivot";
    const float PivotLookupCooldown = 2f;
    const string MaterialResourcePath = "VFX/ChuonTelegraphFlare";
    float _nextPivotLookupAt;

    [SerializeField] Transform patternPivot;
    [SerializeField] Material telegraphMaterial;
    // (이전 링 쿼드용 baseScale/pulseAmount/pulseSpeed/rotateSpeed 는 섬광 구현에서 쓰이지 않아 제거 — CS0414)
    [SerializeField, Min(0f)] float verticalOffset = 0.03f;
    [Tooltip("헬멧에서 카메라 쪽으로 당기는 거리 (머리에 가려지지 않게)")]
    [SerializeField, Min(0f)] float cameraPullForward = 0.58f;
    [SerializeField] Color fallbackColor = new Color(1f, 0.96f, 0.02f, 1f);
    [Header("Flash (iteration 10)")]
    [SerializeField, Range(0.08f, 0.6f)] float flashDuration = 0.22f;
    [SerializeField, Range(0.2f, 3f)] float flashWorldSize = 0.9f;
    [SerializeField, Range(0.5f, 6f)] float flashHdrIntensity = 3.2f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int RotationId = Shader.PropertyToID("_Rotation");

    static Mesh s_quad;

    GameObject _quad;
    Transform _quadTransform;
    Renderer _renderer;
    Material _runtimeMaterial;
    MaterialPropertyBlock _block;
    Camera _mainCamera;
    Transform _headAnchor;
    float _startTime;
    float _hideAt;
    float _rotation;
    float _scaleMultiplier = 1f;
    Color _color;

    /// <summary>QA: 마지막 섬광 시각/색.</summary>
    public float LastFlashTime { get; private set; } = -1f;
    public Color LastFlashColor => _color;
    public int FlashCount { get; private set; }

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

        Transform anchor = ResolveAnchor();
        if (anchor == null)
            return;

        if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
            _mainCamera = Camera.main;

        Vector3 cameraDirection = _mainCamera != null
            ? (_mainCamera.transform.position - anchor.position).normalized
            : Vector3.back;
        _quadTransform.position = anchor.position + Vector3.up * verticalOffset + cameraDirection * cameraPullForward;
        if (_mainCamera != null)
            _quadTransform.rotation = _mainCamera.transform.rotation;

        float progress = Mathf.Clamp01((Time.time - _startTime) / Mathf.Max(0.01f, _hideAt - _startTime));
        float size = flashWorldSize * _scaleMultiplier * Mathf.Lerp(0.85f, 1.15f, progress);
        _quadTransform.localScale = new Vector3(size, size, size);

        _block.Clear();
        _block.SetColor(ColorId, _color);
        _block.SetFloat(ProgressId, progress);
        _block.SetFloat(RotationId, _rotation);
        _renderer.SetPropertyBlock(_block);
    }

    public void Play(float duration)
    {
        Play(duration, fallbackColor, 1f);
    }

    // 다단 후속타용(BossController.StringHitCues): 텔레그래프 색 언어의 색과 작은 크기로 짧게 표시.
    public void Play(float duration, Color tint, float scaleMultiplier)
    {
        if (duration <= 0.01f)
            return;

        _scaleMultiplier = Mathf.Max(0.05f, scaleMultiplier);
        EnsureQuad();
        if (_quad == null || ResolveAnchor() == null)
            return;

        float max = tint.maxColorComponent;
        Color ldr = max > 1f ? tint / max : tint;
        ldr.a = 1f;
        _color = ldr * flashHdrIntensity * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(_scaleMultiplier));
        _startTime = Time.time;
        // 섬광은 큐 순간에 짧게 (공지 시간이 더 짧으면 그 안에 끝낸다)
        _hideAt = Time.time + Mathf.Min(flashDuration, Mathf.Max(0.08f, duration + 0.06f));
        _rotation = Random.Range(-0.25f, 0.25f);
        LastFlashTime = Time.time;
        FlashCount++;
        _quad.SetActive(true);
        LateUpdate();
    }

    public void Hide()
    {
        _hideAt = 0f;
        if (_quad != null)
            _quad.SetActive(false);
    }

    Transform ResolveAnchor()
    {
        if (_headAnchor != null)
            return _headAnchor;

        // Type-D 장갑 리그의 바이저 앵커 → 애니메이터 머리 뼈 → PatternPivot
        TypeDArmorRig armor = GetComponentInChildren<TypeDArmorRig>(true) ?? GetComponentInParent<TypeDArmorRig>();
        if (armor != null && armor.VisorAnchor != null)
            return _headAnchor = armor.VisorAnchor;

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null || !animator.isHuman)
                continue;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
                return _headAnchor = head;
        }

        if (patternPivot == null)
            ResolvePivot();
        return patternPivot;
    }

    void ResolvePivot()
    {
        if (patternPivot != null)
            return;

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
    }

    void EnsureQuad()
    {
        if (_quad != null)
            return;

        _quad = new GameObject("BossCueFlare_Runtime", typeof(MeshFilter), typeof(MeshRenderer));
        _quad.hideFlags = HideFlags.DontSave;
        _quadTransform = _quad.transform;
        _quadTransform.SetParent(null, false);
        _quad.GetComponent<MeshFilter>().sharedMesh = Quad;

        _renderer = _quad.GetComponent<Renderer>();
        _renderer.sharedMaterial = ResolveMaterial();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _block = new MaterialPropertyBlock();
    }

    Material ResolveMaterial()
    {
        if (telegraphMaterial != null && telegraphMaterial.shader != null && telegraphMaterial.shader.name == "ChuOn/TelegraphFlare")
            return telegraphMaterial;

        Material loaded = Resources.Load<Material>(MaterialResourcePath);
        if (loaded != null)
            return loaded;

        Shader shader = Shader.Find("ChuOn/TelegraphFlare") ?? Shader.Find("Sprites/Default");
        _runtimeMaterial = new Material(shader) { name = "BossCueFlare_Fallback", hideFlags = HideFlags.DontSave };
        return _runtimeMaterial;
    }

    static Mesh Quad
    {
        get
        {
            if (s_quad != null)
                return s_quad;
            s_quad = new Mesh { name = "_CueFlareQuad", hideFlags = HideFlags.DontSave };
            s_quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            s_quad.RecalculateBounds();
            return s_quad;
        }
    }
}
