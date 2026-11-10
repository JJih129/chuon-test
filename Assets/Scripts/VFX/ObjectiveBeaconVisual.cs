using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 목표 지점 홀로그램 표식 (반복 16, 플레이어 리뷰 #1). 츠카사 HUD 문법: 바닥의 얇은 앰버 링 + 가는 빛기둥 + 작은 쉐브론.
/// - 거리/각도 페이드: 카메라가 가까우면 기둥·쉐브론이 사라지고(얼굴 앞을 막지 않게), 링은 낮은 시선(스치는 각)에서 옅어진다.
///   화면 중앙을 정면으로 볼 때는 기둥을 조금 낮춰 화면을 지배하지 않게 하고, 먼 거리에서도 쉐브론 크기를 유지해 읽히게 한다.
/// - 연출 재생 중에는 숨는다 (종료 연출에서 표식이 공중에 떠 보이던 문제).
/// - 충돌체 없음, 그림자 없음, 프레임당 할당 없음 (MPB 값은 바뀔 때만 갱신).
/// 사용: TutorialWorldMarker 가 이 컴포넌트에 그리기를 위임한다. 단독으로 붙여도 동작.
/// </summary>
[DisallowMultipleComponent]
public sealed class ObjectiveBeaconVisual : MonoBehaviour
{
    const string MaterialResourcePath = "VFX/ChuonObjectiveBeacon";

    [SerializeField] Color color = new Color(1f, 0.62f, 0.16f, 1f);
    [Tooltip("코어 HDR 배율 (블룸 임계 1.1 근처, 네온 금지).")]
    [SerializeField, Range(1f, 3f)] float hdrIntensity = 1.5f;
    [SerializeField, Range(0.25f, 1.2f)] float radius = 0.55f;
    [SerializeField, Range(1.2f, 4f)] float beamHeight = 2.3f;
    [SerializeField, Range(0.1f, 0.8f)] float beamWidth = 0.34f;
    [Tooltip("카메라가 이 거리(m) 안이면 기둥·쉐브론이 사라진다.")]
    [SerializeField] Vector2 beamNearFade = new Vector2(2.2f, 5f);
    [Tooltip("링: 카메라가 이 거리 안이면 옅어진다.")]
    [SerializeField] Vector2 ringNearFade = new Vector2(0.8f, 2.0f);
    [Tooltip("먼 거리 감쇠 시작/끝 (m) 과 최소 배율.")]
    [SerializeField] Vector2 farFade = new Vector2(30f, 60f);
    [SerializeField, Range(0f, 1f)] float farMinimum = 0.55f;
    [Tooltip("표식 기준점(예: 높이 2.5 m 의 출구 패널) 아래 바닥에 링·기둥을 붙인다 (공중에 뜬 표식 방지).")]
    [SerializeField] bool snapToGround = true;
    [SerializeField, Min(0.5f)] float groundProbeDepth = 4f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int ModeId = Shader.PropertyToID("_Mode");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static Material s_material;
    static Mesh s_flatQuad;
    static Mesh s_uprightQuad;

    Transform _ring;
    Transform _beam;
    Transform _chevron;
    MeshRenderer _ringRenderer;
    MeshRenderer _beamRenderer;
    MeshRenderer _chevronRenderer;
    MaterialPropertyBlock _block;
    Camera _camera;
    float _cinematicFade = 1f;
    float _lastRingFade = -1f;
    float _lastBeamFade = -1f;
    bool _colorDirty = true;
    float _groundOffset;
    float _nextGroundProbeAt;
    Vector3 _lastProbeOrigin = new Vector3(float.NaN, 0f, 0f);
    static readonly RaycastHit[] s_groundHits = new RaycastHit[8];

    public void Configure(Color markerColor, float ringRadius, float height)
    {
        color = markerColor;
        radius = Mathf.Clamp(ringRadius, 0.25f, 1.2f);
        beamHeight = Mathf.Clamp(height, 1.2f, 4f);
        EnsureBuilt();
        ApplyLayout();
        _colorDirty = true;
    }

    public void SetColor(Color markerColor)
    {
        color = markerColor;
        _colorDirty = true;
    }

    void Awake()
    {
        EnsureBuilt();
        ApplyLayout();
    }

    void OnEnable()
    {
        _lastRingFade = _lastBeamFade = -1f;
        _colorDirty = true;
        _cinematicFade = CinematicDirector.IsPlaying ? 0f : 1f;
        _nextGroundProbeAt = 0f;
    }

    /// <summary>0.5 s 마다(또는 기준점이 움직였을 때) 아래 바닥까지의 거리. 캐릭터·트리거는 무시.</summary>
    void UpdateGround()
    {
        if (!snapToGround)
        {
            _groundOffset = 0f;
            return;
        }
        Vector3 origin = transform.position;
        if (Time.unscaledTime < _nextGroundProbeAt && (origin - _lastProbeOrigin).sqrMagnitude < 0.0025f)
            return;
        _nextGroundProbeAt = Time.unscaledTime + 0.5f;
        _lastProbeOrigin = origin;
        float best = -1f;
        int n = Physics.RaycastNonAlloc(origin + Vector3.up * 0.2f, Vector3.down, s_groundHits, groundProbeDepth + 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = s_groundHits[i].collider;
            if (c == null || SoulsCameraRig.IsCharacterCollider(c))
                continue;
            float d = s_groundHits[i].distance - 0.2f;
            if (best < 0f || d < best)
                best = d;
        }
        _groundOffset = best > 0.08f ? best : 0f;
    }

    void EnsureBuilt()
    {
        _block ??= new MaterialPropertyBlock();
        if (_ring == null)
            _ring = BuildPart("BeaconRing", FlatQuad, out _ringRenderer);
        if (_beam == null)
            _beam = BuildPart("BeaconBeam", UprightQuad, out _beamRenderer);
        if (_chevron == null)
            _chevron = BuildPart("BeaconChevron", UprightQuad, out _chevronRenderer);
    }

    Transform BuildPart(string partName, Mesh mesh, out MeshRenderer meshRenderer)
    {
        Transform existing = transform.Find(partName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(partName);
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        MeshFilter filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        meshRenderer = go.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = ResolveMaterial();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return go.transform;
    }

    void ApplyLayout()
    {
        if (_ring != null)
        {
            _ring.localPosition = new Vector3(0f, 0.02f, 0f);
            _ring.localRotation = Quaternion.identity;
            _ring.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
        }
        if (_beam != null)
        {
            _beam.localPosition = Vector3.zero;
            _beam.localScale = new Vector3(beamWidth, beamHeight, 1f);
        }
    }

    void LateUpdate()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = Camera.main;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        _cinematicFade = Mathf.MoveTowards(_cinematicFade, CinematicDirector.IsPlaying ? 0f : 1f, dt / 0.2f);

        float ringFade = _cinematicFade;
        float beamFade = _cinematicFade;
        float chevronScale = 0.26f;
        UpdateGround();
        Vector3 basePos = transform.position + Vector3.down * _groundOffset;
        if (_ring != null)
            _ring.SetPositionAndRotation(basePos + Vector3.up * 0.02f, Quaternion.identity);
        if (_beam != null)
            _beam.position = basePos;
        if (_camera != null)
        {
            Transform cam = _camera.transform;
            Vector3 toMarker = basePos + Vector3.up * (beamHeight * 0.4f) - cam.position;
            float distance = toMarker.magnitude;
            Vector3 dir = distance > 0.001f ? toMarker / distance : cam.forward;

            float far = Mathf.Lerp(1f, farMinimum, Mathf.InverseLerp(farFade.x, farFade.y, distance));
            // 링: 가까우면 옅게, 스치는 각(카메라가 거의 수평으로 볼 때)에서는 납작한 띠로 보이므로 옅게
            Vector3 toRing = basePos - cam.position;
            float grazing = toRing.sqrMagnitude > 0.0001f ? Mathf.Abs(toRing.normalized.y) : 1f;
            ringFade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ringNearFade.x, ringNearFade.y, toRing.magnitude))
                        * Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(0.06f, 0.3f, grazing)) * far;
            // 기둥·쉐브론: 가까우면 사라지고, 화면 정중앙을 정면으로 볼 때는 70 %
            float center = Mathf.InverseLerp(0.94f, 0.995f, Vector3.Dot(cam.forward, dir));
            beamFade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(beamNearFade.x, beamNearFade.y, distance))
                        * Mathf.Lerp(1f, 0.7f, center) * far;
            // 쉐브론은 화면상 크기가 거의 일정하게 (가까이 0.26 m → 멀리 최대 0.9 m)
            chevronScale = Mathf.Clamp(distance * 0.028f, 0.26f, 0.9f);

            // 기둥: Y 축 빌보드, 쉐브론: 완전 빌보드
            Vector3 flat = new Vector3(cam.position.x - basePos.x, 0f, cam.position.z - basePos.z);
            if (_beam != null && flat.sqrMagnitude > 0.0001f)
                _beam.rotation = Quaternion.LookRotation(-flat.normalized, Vector3.up);
            if (_chevron != null)
            {
                float bob = Mathf.Sin(Time.unscaledTime * 2.2f) * 0.06f;
                Vector3 p = basePos + Vector3.up * (beamHeight * 0.82f + bob);
                Vector3 look = p - cam.position;
                _chevron.position = p;
                _chevron.rotation = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
            }
        }
        if (_chevron != null)
            _chevron.localScale = new Vector3(chevronScale, chevronScale, 1f);

        bool colorDirty = _colorDirty;
        _colorDirty = false;
        if (colorDirty || Mathf.Abs(ringFade - _lastRingFade) > 0.004f)
        {
            _lastRingFade = ringFade;
            Apply(_ringRenderer, 0f, ringFade);
        }
        if (colorDirty || Mathf.Abs(beamFade - _lastBeamFade) > 0.004f)
        {
            _lastBeamFade = beamFade;
            Apply(_beamRenderer, 1f, beamFade);
            Apply(_chevronRenderer, 2f, beamFade);
        }
    }

    void Apply(Renderer target, float mode, float fade)
    {
        if (target == null)
            return;
        bool visible = fade > 0.003f;
        if (target.enabled != visible)
            target.enabled = visible;
        if (!visible)
            return;
        Color hdr = new Color(color.r * hdrIntensity, color.g * hdrIntensity, color.b * hdrIntensity, 1f);
        _block.Clear();
        _block.SetColor(ColorId, hdr);
        _block.SetFloat(ModeId, mode);
        _block.SetFloat(FadeId, Mathf.Clamp01(fade));
        target.SetPropertyBlock(_block);
    }

    static Material ResolveMaterial()
    {
        if (s_material != null)
            return s_material;
        s_material = Resources.Load<Material>(MaterialResourcePath);
        if (s_material == null)
        {
            Shader shader = Shader.Find("Chuon/ObjectiveBeacon") ?? Shader.Find("Sprites/Default");
            s_material = new Material(shader) { hideFlags = HideFlags.DontSave, renderQueue = (int)RenderQueue.Transparent };
        }
        return s_material;
    }

    /// <summary>바닥에 눕힌 1 m 쿼드 (중심 원점, +Y 법선).</summary>
    static Mesh FlatQuad
    {
        get
        {
            if (s_flatQuad != null)
                return s_flatQuad;
            s_flatQuad = new Mesh { name = "_BeaconFlatQuad", hideFlags = HideFlags.DontSave };
            s_flatQuad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            s_flatQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_flatQuad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            s_flatQuad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            s_flatQuad.RecalculateBounds();
            return s_flatQuad;
        }
    }

    /// <summary>세운 1 m 쿼드 (아래 가운데 원점 = 기둥, 쉐브론은 중심이 약간 위).</summary>
    static Mesh UprightQuad
    {
        get
        {
            if (s_uprightQuad != null)
                return s_uprightQuad;
            s_uprightQuad = new Mesh { name = "_BeaconUprightQuad", hideFlags = HideFlags.DontSave };
            s_uprightQuad.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
            s_uprightQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_uprightQuad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            s_uprightQuad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            s_uprightQuad.RecalculateBounds();
            return s_uprightQuad;
        }
    }
}
