using System.Collections.Generic;
using UnityEngine;

// Type-D 하드서피스 장갑 리그 (반복 10, 사용자 결정: 성별 모호한 실험 병기 외형)
// - 기존 리그/애니메이션은 그대로. Blender 로 만든 강체 장갑(흉갑·어깨·등 코어 하우징·척추판·전완·헬멧 바이저/핀·허리)을
//   휴머노이드 뼈에 붙여서 모든 보스 애니메이션을 그대로 따라간다.
// - 에너지 코어(가슴 렌즈 + 등 리액터)가 맥동하고, 페이즈 전환 때 밝게 치솟는다. 브레이크 중에는 불안정하게 깜빡이고, 사망 시 꺼진다.
// - 헬멧 바이저 발광선은 공격 전조(BossTelegraphVfxPresenter)가 타입 색으로 램프시킨다.
// - 몸 재질은 무광 고무 슈트 + 회로 발광 마스크 + 림라이트 버전으로 교체 (TypeDArmorSet.materialOverrides).
[DisallowMultipleComponent]
public sealed class TypeDArmorRig : MonoBehaviour
{
    [SerializeField] TypeDArmorSet armorSet;
    [SerializeField] bool installOnAwake = true;
    [SerializeField, Range(0.1f, 3f)] float corePulseHz = 0.85f;
    [SerializeField, Range(0f, 1f)] float corePulseDepth = 0.28f;
    [SerializeField, Range(1f, 6f)] float corePhaseBoost = 3.2f;
    [SerializeField, Range(0.2f, 6f)] float corePhaseBoostDecay = 2.4f;
    [SerializeField, Range(0f, 3f)] float coreBaseIntensity = 1.35f;
    [SerializeField, Range(0f, 2f)] float suitCircuitIntensity = 0.55f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    struct GlowSlot
    {
        public Renderer Renderer;
        public int Submesh;
        public Material Expected;
    }

    readonly List<GameObject> _spawned = new List<GameObject>();
    readonly List<GlowSlot> _coreSlots = new List<GlowSlot>();
    readonly List<GlowSlot> _visorSlots = new List<GlowSlot>();
    readonly List<GlowSlot> _accentSlots = new List<GlowSlot>();
    readonly List<GlowSlot> _suitSlots = new List<GlowSlot>();
    readonly List<Renderer> _armorRenderers = new List<Renderer>();

    struct BlendedPart
    {
        public Transform Part;
        public Transform Primary;
        public Transform Secondary;
        public Vector3 PrimaryPos;
        public Quaternion PrimaryRot;
        public Vector3 SecondaryPos;
        public Quaternion SecondaryRot;
        public float Weight;
        public Vector3 Pivot;
    }

    readonly List<BlendedPart> _blended = new List<BlendedPart>();
    MaterialPropertyBlock _block;
    Transform _visorAnchor;
    bool _installed;
    BossController _boss;
    BossHealth _health;
    BossBreakController _break;
    float _phaseBoost;
    float _deathFade = 1f;
    Color _cueColor;
    float _cueVisor;
    float _cueAccent;

    const string ArmorSetResource = "TypeD/TypeD_ArmorSet";

    /// <summary>
    /// BossController.Awake 에서 호출: 같은 Type-D 모델(검증 렌더러/정점 수 일치)이면 장갑 리그를 자동 설치한다.
    /// 씬 파일을 고치지 않고 Level_Facility / MainScene 보스 모두 같은 외형이 된다.
    /// </summary>
    public static TypeDArmorRig EnsureInstalled(Component boss)
    {
        if (boss == null)
            return null;
        TypeDArmorRig existing = boss.GetComponentInChildren<TypeDArmorRig>(true);
        if (existing != null)
            return existing;

        TypeDArmorSet set = Resources.Load<TypeDArmorSet>(ArmorSetResource);
        if (set == null || !set.MatchesModel(boss.transform))
            return null;

        TypeDArmorRig rig = boss.gameObject.AddComponent<TypeDArmorRig>();
        rig.armorSet = set;
        rig.Install();
        return rig;
    }

    // ---------------------------------------------------------------- 반복 16: 장갑 해제 / 코어 과열 / 바이저 파괴
    float _overheat;
    readonly List<GameObject> _purged = new List<GameObject>();

    /// <summary>설치된 파츠 GameObject (이름 = TypeDArmorSet.Part.name). 없으면 null.</summary>
    public GameObject FindPart(string partName)
    {
        string n = "TypeD_" + partName;
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null && _spawned[i].name == n)
                return _spawned[i];
        return null;
    }

    /// <summary>페이즈 3 과열: 코어가 백열 + TSUKASA 레드로 치솟는다 (0..1).</summary>
    public void SetCoreOverheat(float level01) => _overheat = Mathf.Clamp01(level01);
    public float CoreOverheat => _overheat;

    /// <summary>
    /// 장갑 판을 뼈에서 떼어 물리로 날린다 (페이즈 2 전환 컷). ignore 콜라이더와는 부딪히지 않는다.
    /// 반환: 떼어낸 개수. 조각은 lifetime 뒤 사라진다.
    /// </summary>
    public int PurgePlates(IList<string> partNames, Vector3 burstOrigin, float impulse, float lifetime, IList<Collider> ignore)
    {
        int count = 0;
        if (partNames == null)
            return 0;
        for (int i = 0; i < partNames.Count; i++)
        {
            GameObject go = FindPart(partNames[i]);
            if (go == null || _purged.Contains(go))
                continue;
            Transform t = go.transform;
            for (int b = _blended.Count - 1; b >= 0; b--)
                if (_blended[b].Part == t)
                    _blended.RemoveAt(b);
            t.SetParent(null, true);
            go.layer = 2; // Ignore Raycast: 카메라·락온 레이에 걸리지 않음
            MeshFilter mf = go.GetComponent<MeshFilter>();
            BoxCollider box = go.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds mb = mf.sharedMesh.bounds;
                box.center = mb.center;
                box.size = Vector3.Max(mb.size, Vector3.one * 0.06f);
            }
            if (ignore != null)
                for (int c = 0; c < ignore.Count; c++)
                    if (ignore[c] != null)
                        Physics.IgnoreCollision(box, ignore[c], true);
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Vector3 dir = (t.position - burstOrigin);
            dir.y = Mathf.Max(0.35f, dir.y + 0.6f);
            body.AddForce(dir.normalized * impulse * Random.Range(0.8f, 1.2f), ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * impulse * 1.6f, ForceMode.VelocityChange);
            _purged.Add(go);
            Destroy(go, Mathf.Max(1f, lifetime));
            count++;
        }
        return count;
    }

    /// <summary>바이저 파괴: 발광 띠를 끄고 숨긴다 (파편은 호출자가 스폰).</summary>
    public void BreakVisor()
    {
        for (int i = 0; i < _visorSlots.Count; i++)
            if (_visorSlots[i].Renderer != null)
                _visorSlots[i].Renderer.enabled = false;
        _visorSlots.Clear();
    }

    public Transform VisorAnchor => _visorAnchor;
    public bool HasVisorGlow => _visorSlots.Count > 0;
    public bool IsInstalled => _installed;
    public IReadOnlyList<Renderer> ArmorRenderers => _armorRenderers;
    /// <summary>QA: 현재 코어 밝기 배율 (페이즈 부스트 포함).</summary>
    public float CoreLevel { get; private set; }
    public int PartCount => _spawned.Count;

    void Awake()
    {
        if (installOnAwake)
            Install();
    }

    void OnEnable()
    {
        Bind();
    }

    void OnDisable()
    {
        if (_boss != null)
            _boss.OnBossPhaseChanged -= HandlePhaseChanged;
    }

    void OnDestroy()
    {
        for (int i = 0; i < _spawned.Count; i++)
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
    }

    void Bind()
    {
        if (_boss == null)
            _boss = GetComponentInParent<BossController>() ?? GetComponentInChildren<BossController>(true);
        if (_boss != null)
        {
            _boss.OnBossPhaseChanged -= HandlePhaseChanged;
            _boss.OnBossPhaseChanged += HandlePhaseChanged;
            if (_health == null)
                _health = _boss.GetComponentInChildren<BossHealth>(true) ?? _boss.GetComponentInParent<BossHealth>();
            if (_break == null)
                _break = _boss.GetComponentInChildren<BossBreakController>(true) ?? _boss.GetComponentInParent<BossBreakController>();
        }
    }

    void HandlePhaseChanged(int phase, float hpRatio)
    {
        PulsePhaseChange();
    }

    /// <summary>페이즈 전환 연출: 코어/회로가 순간적으로 밝게 치솟았다가 가라앉는다.</summary>
    public void PulsePhaseChange()
    {
        _phaseBoost = corePhaseBoost;
    }

    public void Install()
    {
        if (_installed || armorSet == null)
            return;

        Animator animator = ResolveHumanoid();
        if (animator == null)
        {
            Debug.LogWarning("[TypeDArmorRig] Humanoid animator not found; armor not installed.", this);
            return;
        }

        _block = new MaterialPropertyBlock();
        TypeDArmorSet.Part[] parts = armorSet.parts;
        for (int i = 0; i < parts.Length; i++)
        {
            TypeDArmorSet.Part part = parts[i];
            if (part == null || part.mesh == null)
                continue;
            Transform bone = animator.GetBoneTransform(part.bone);
            if (bone == null)
                continue;

            var go = new GameObject("TypeD_" + part.name);
            go.layer = bone.gameObject.layer;
            Transform t = go.transform;
            t.SetParent(bone, false);
            t.localPosition = part.localPosition;
            t.localRotation = part.localRotation;
            t.localScale = part.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = part.materials;
            mr.shadowCastingMode = part.role == TypeDArmorSet.PartRole.Plate
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            _spawned.Add(go);
            _armorRenderers.Add(mr);

            if (part.useBlendBone)
            {
                Transform secondary = animator.GetBoneTransform(part.blendBone);
                if (secondary != null)
                    _blended.Add(new BlendedPart
                    {
                        Part = t, Primary = bone, Secondary = secondary,
                        PrimaryPos = part.localPosition, PrimaryRot = part.localRotation,
                        SecondaryPos = part.blendLocalPosition, SecondaryRot = part.blendLocalRotation,
                        Weight = part.blendWeight,
                        Pivot = part.pivotLocal
                    });
            }

            switch (part.role)
            {
                case TypeDArmorSet.PartRole.Core:
                    AddSlot(_coreSlots, mr, 0);
                    break;
                case TypeDArmorSet.PartRole.Visor:
                    AddSlot(_visorSlots, mr, 0);
                    var anchor = new GameObject("TypeD_VisorAnchor").transform;
                    anchor.SetParent(t, false);
                    anchor.localPosition = part.anchorLocal;
                    _visorAnchor = anchor;
                    break;
                default:
                    if (part.glowSubmesh >= 0)
                        AddSlot(_accentSlots, mr, part.glowSubmesh);
                    break;
            }
        }

        ApplyMaterialOverrides();
        _installed = _spawned.Count > 0;
        Bind();
        Update();
    }

    void AddSlot(List<GlowSlot> list, Renderer r, int submesh)
    {
        Material[] mats = r.sharedMaterials;
        if (submesh < 0 || submesh >= mats.Length)
            return;
        list.Add(new GlowSlot { Renderer = r, Submesh = submesh, Expected = mats[submesh] });
    }

    void ApplyMaterialOverrides()
    {
        if (armorSet.materialOverrides == null)
            return;
        Transform root = _boss != null ? _boss.transform : transform.root;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < armorSet.materialOverrides.Length; i++)
        {
            TypeDArmorSet.MaterialOverride o = armorSet.materialOverrides[i];
            if (o == null || o.materials == null || o.materials.Length == 0)
                continue;
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null || renderers[r].name != o.rendererName)
                    continue;
                if (renderers[r].sharedMaterials.Length != o.materials.Length)
                    continue;
                renderers[r].sharedMaterials = o.materials;
                for (int m = 0; m < o.materials.Length; m++)
                    if (o.materials[m] != null && o.materials[m].IsKeywordEnabled("_EMISSION"))
                        AddSlot(_suitSlots, renderers[r], m);
            }
        }
    }

    Animator ResolveHumanoid()
    {
        Transform root = transform.root != null ? transform.root : transform;
        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator a = animators[i];
            if (a != null && a.isHuman && a.avatar != null && a.GetBoneTransform(HumanBodyBones.Head) != null)
                return a;
        }
        return null;
    }

    /// <summary>공격 전조: 바이저(visor01)와 장갑 발광선(accent01)을 큐 색으로 램프. 0 이면 평상시 색.</summary>
    public void SetTelegraphGlow(Color cueColor, float visor01, float accent01)
    {
        _cueColor = cueColor;
        _cueVisor = Mathf.Clamp01(visor01);
        _cueAccent = Mathf.Clamp01(accent01);
    }

    // 애니메이션 이후: 어깨 장갑을 위팔/쇄골 포즈 사이로 섞는다
    void LateUpdate()
    {
        for (int i = 0; i < _blended.Count; i++)
        {
            BlendedPart b = _blended[i];
            if (b.Part == null || b.Primary == null || b.Secondary == null)
                continue;
            // 관절(피벗) 기준으로 섞는다: 원점(발밑)끼리 섞으면 회전 차이만큼 장갑이 떠 버린다
            Quaternion ra = b.Primary.rotation * b.PrimaryRot;
            Quaternion rb = b.Secondary.rotation * b.SecondaryRot;
            Vector3 pivotA = b.Primary.TransformPoint(b.PrimaryPos) + ra * b.Pivot;
            Vector3 pivotB = b.Secondary.TransformPoint(b.SecondaryPos) + rb * b.Pivot;
            Quaternion r = Quaternion.Slerp(ra, rb, b.Weight);
            Vector3 pivot = Vector3.Lerp(pivotA, pivotB, b.Weight);
            b.Part.SetPositionAndRotation(pivot - r * b.Pivot, r);
        }
    }

    void Update()
    {
        if (!_installed || armorSet == null)
            return;

        float dt = Time.deltaTime;
        _phaseBoost = Mathf.MoveTowards(_phaseBoost, 0f, corePhaseBoostDecay * dt);

        bool dead = _health != null && _health.IsDead;
        _deathFade = Mathf.MoveTowards(_deathFade, dead ? 0.05f : 1f, dt * (dead ? 0.6f : 2f));

        float t = Time.time;
        float pulse = 1f + corePulseDepth * Mathf.Sin(t * corePulseHz * Mathf.PI * 2f);
        if (_break != null && _break.IsInBreak)
            pulse *= 0.45f + 0.55f * Mathf.PerlinNoise(t * 9f, 0.37f); // 브레이크: 불안정한 깜빡임
        float level = (coreBaseIntensity * pulse + _phaseBoost) * _deathFade;
        CoreLevel = level;

        Color core = armorSet.coreColor;
        if (_overheat > 0f)
        {
            // 과열: 백열(세라믹 화이트) 중심 + TSUKASA 레드 림, 빠른 맥동
            float hot = _overheat * (0.8f + 0.2f * Mathf.Sin(t * 7.5f));
            core = Color.Lerp(core, new Color(1.6f, 0.95f, 0.85f), hot);
            Apply(_coreSlots, core, Color.Lerp(core * 1.4f, new Color(2.4f, 0.12f, 0.08f), _overheat), level * (1f + 1.4f * _overheat));
        }
        else
            Apply(_coreSlots, core, core * 1.4f, level);

        Color accent = Color.Lerp(armorSet.glowColor * (0.8f + 0.2f * pulse + _phaseBoost * 0.3f), _cueColor, _cueAccent);
        Apply(_accentSlots, accent, accent * 1.2f, _deathFade * (1f + _cueAccent * 0.8f));

        // 바이저: 평상시 은은한 청백 → 전조 램프 시 타입 색으로 HDR 상승 (블룸)
        Color visor = Color.Lerp(armorSet.visorIdleColor * (0.9f + _phaseBoost * 0.2f), _cueColor, _cueVisor);
        Apply(_visorSlots, visor, visor * 1.5f, _deathFade * Mathf.Lerp(1f, 2.4f, _cueVisor));

        if (_suitSlots.Count > 0)
        {
            Color suit = armorSet.suitCircuitColor * (suitCircuitIntensity * (0.75f + 0.25f * pulse) + _phaseBoost * 0.35f) * _deathFade;
            for (int i = 0; i < _suitSlots.Count; i++)
            {
                GlowSlot s = _suitSlots[i];
                if (!SlotValid(s))
                    continue;
                s.Renderer.GetPropertyBlock(_block, s.Submesh);
                _block.SetColor(EmissionColorId, suit);
                s.Renderer.SetPropertyBlock(_block, s.Submesh);
            }
        }
    }

    void Apply(List<GlowSlot> slots, Color color, Color rim, float intensity)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            GlowSlot s = slots[i];
            if (!SlotValid(s))
                continue;
            _block.Clear();
            _block.SetColor(ColorId, color);
            _block.SetColor(RimColorId, rim);
            _block.SetFloat(IntensityId, intensity);
            s.Renderer.SetPropertyBlock(_block, s.Submesh);
        }
    }

    // 브레이크 홀로그램 등 다른 재질로 바뀐 동안에는 손대지 않는다
    static readonly List<Material> s_matBuffer = new List<Material>(8);

    bool SlotValid(GlowSlot s)
    {
        if (s.Renderer == null)
            return false;
        s.Renderer.GetSharedMaterials(s_matBuffer);
        if (s.Submesh >= s_matBuffer.Count || s_matBuffer[s.Submesh] != s.Expected)
        {
            if (s.Submesh < s_matBuffer.Count)
                s.Renderer.SetPropertyBlock(null, s.Submesh);
            return false;
        }
        return true;
    }
}
