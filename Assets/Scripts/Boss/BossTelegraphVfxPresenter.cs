using System;
using UnityEngine;

// 보스 공격 전조 표현 (반복 10 재구현)
// 기획(보스 기획서 v1.2): 공격 0.4초 전 헬멧 발광 (주황 = 패링 가능 / 빨강 = 불가), 타입별 사운드.
// 우리 확장 색 언어: 패링 가능 금/주황, 회피 전용 빨강, 가드·패링 불가 자홍, 강공 호박 (+ 원거리 청색), 색각 보정 팔레트.
// 레이어:
//  (a) 예비동작: 헬멧 바이저 발광 램프(피격 0.4초 전부터 피격 순간 최고점) + 무기 날 발광 스윕(손잡이→칼끝, 피격 순간 도달).
//      Type-D 장갑 리그(TypeDArmorRig)가 있으면 바이저/장갑 발광선을 쓰고, 없으면 기존 몸 틴트로 폴백.
//  (b) 형태: 바닥 데칼은 BossGroundTelegraph (이 컴포넌트와 같은 큐 시간).
//  (c) 타이밍 핑: 큐 순간 타입별 효과음 (Resources/Audio/TelegraphCue_*, SFX 볼륨 반영) + 헬멧 앞 섬광(BossPatternHandTelegraph).
// 큐 시간은 BossController.HonestCues 가 실제 판정 시각에 맞춰 넘겨준 값 그대로다 (duration = 피격까지 남은 시간).
[DisallowMultipleComponent]
public sealed class BossTelegraphVfxPresenter : MonoBehaviour
{
    struct RendererBinding
    {
        public Renderer Renderer;
        public int ColorPropertyId;
        public Color IdleColor;
        public bool IsEmission;
    }

    struct WeaponOverlay
    {
        public Renderer Renderer;
        public Material[] Original;
        public Material[] WithOverlay;
        public Vector4 Axis;
        public Vector4 Range;
    }

    [Header("Targets")]
    [SerializeField] private Renderer[] weaponRenderers;
    [SerializeField] private Renderer[] bodyRenderers;
    [SerializeField] private bool autoCollectWhenEmpty = true;
    [SerializeField] private bool includeInactiveRenderers = true;
    [SerializeField] private bool ignoreParticleRenderers = true;

    [Header("Material Properties")]
    [SerializeField] private string emissionColorName = "_EmissionColor";
    [SerializeField] private string baseColorName = "_BaseColor";
    [SerializeField] private string legacyColorName = "_Color";
    [SerializeField, Range(0.1f, 1f)] private float baseColorCueBlend = 0.55f;

    [Header("Cue Colors")]
    // HDR/Bloom 활성화 기준으로 강도 ~2–3 유지. 카테고리별 색상(hue)을 분리.
    [SerializeField, ColorUsage(true, true)] private Color normalColor = new Color(0.75f, 0.95f, 1f) * 1.8f;
    [SerializeField, ColorUsage(true, true)] private Color parryableColor = new Color(1f, 0.78f, 0.22f) * 2.6f;   // warm gold
    [SerializeField, ColorUsage(true, true)] private Color dodgeOnlyColor = new Color(1f, 0.1f, 0.08f) * 2.6f;    // red
    [SerializeField, ColorUsage(true, true)] private Color unblockableColor = new Color(1f, 0.12f, 0.85f) * 3f;   // magenta
    [SerializeField, ColorUsage(true, true)] private Color heavyColor = new Color(1f, 0.6f, 0.04f) * 2.6f;       // amber (red=회피 전용과 구분)
    [SerializeField, ColorUsage(true, true)] private Color rangedColor = new Color(0.25f, 0.82f, 1f) * 2.2f;

    [Header("Colorblind-Safe Palette")]
    [Tooltip("색각 이상 대응 팔레트 사용 (Okabe-Ito 기반). 런타임 전역 설정: UseColorblindSafePalette")]
    [SerializeField] private bool useColorblindSafePalette;
    public static bool UseColorblindSafePalette;

    [Header("Pulse (legacy body tint fallback)")]
    [SerializeField, Min(0.04f)] private float minimumDuration = 0.08f;
    [SerializeField, Range(1, 5)] private int pulseCount = 2;
    [SerializeField] private bool varyMotionPerCategory = true;

    [Header("Anticipation (iteration 10)")]
    [Tooltip("헬멧/무기 발광이 피격 몇 초 전부터 차오르는가 (기획: 0.4초)")]
    [SerializeField, Range(0.1f, 1f)] private float anticipationLead = 0.4f;
    [Tooltip("피격 순간 이후 발광 유지/감쇠")]
    [SerializeField, Range(0f, 0.3f)] private float peakHold = 0.06f;
    [SerializeField, Range(0.02f, 0.6f)] private float decayTime = 0.16f;
    [SerializeField, Range(0.5f, 4f)] private float visorPeakIntensity = 2.2f;
    [SerializeField] private bool useWeaponSweep = true;
    [SerializeField] private Material weaponOverlayMaterial;

    [Header("Timing Ping SFX (iteration 10)")]
    [SerializeField] private bool playCueSfx = true;
    [SerializeField] private AudioClip parryableCueClip;
    [SerializeField] private AudioClip dodgeOnlyCueClip;
    [SerializeField] private AudioClip unblockableCueClip;
    [SerializeField] private AudioClip heavyCueClip;
    [SerializeField] private AudioClip rangedCueClip;
    [SerializeField, Range(0f, 1f)] private float cueSfxVolume = 0.8f;
    [Tooltip("다단 후속타(무기 플래시만) 큐 효과음 배율")]
    [SerializeField, Range(0f, 1f)] private float followUpCueVolumeScale = 0.45f;
    [SerializeField, Min(0f)] private float cueSfxMinInterval = 0.06f;

    /// <summary>새 타이밍 핑 효과음이 있으면 HUD 의 합성 위험음은 생략 (같은 순간 이중 재생 방지).</summary>
    public static bool CueSfxActive { get; private set; }

    static readonly int OverlayColorId = Shader.PropertyToID("_Color");
    static readonly int OverlaySweepId = Shader.PropertyToID("_Sweep");
    static readonly int OverlayChargeId = Shader.PropertyToID("_Charge");
    static readonly int OverlayGlowId = Shader.PropertyToID("_Glow");
    static readonly int OverlayAxisId = Shader.PropertyToID("_AxisOS");
    static readonly int OverlayRangeId = Shader.PropertyToID("_AxisRange");

    private MaterialPropertyBlock _propertyBlock;
    private RendererBinding[] _weaponBindings = Array.Empty<RendererBinding>();
    private RendererBinding[] _bodyBindings = Array.Empty<RendererBinding>();
    private WeaponOverlay[] _weaponOverlays = Array.Empty<WeaponOverlay>();
    private int _emissionColorId;
    private int _baseColorId;
    private int _legacyColorId;
    private Color _cueColor;
    private BossAttackTelegraphType _cueType;
    private float _cueStartTime;
    private float _cueHitTime;
    private float _cueDuration;
    private int _cuePulseCount = 2;
    private float _cuePulseSharpness = 1f;
    private bool _cueSustained;
    private bool _weaponActive;
    private bool _bodyActive;
    private bool _overlayApplied;
    private bool _initialized;
    private TypeDArmorRig _armor;
    private AudioSource _cueAudio;
    private float _lastCueSfxTime = float.NegativeInfinity;

    /// <summary>QA: 마지막 큐 (타입, 시작, 피격 예정 시각, 효과음).</summary>
    public BossAttackTelegraphType LastCueType => _cueType;
    public float LastCueStartTime => _cueStartTime;
    public float LastCueHitTime => _cueHitTime;
    public string LastCueSfx { get; private set; } = "";
    public int CueCount { get; private set; }
    /// <summary>QA: 현재 헬멧 발광 0..1 (램프 확인용).</summary>
    public float CurrentVisorLevel { get; private set; }

    void Awake()
    {
        Initialize();
        enabled = false;
    }

    void OnDisable()
    {
        ResetCue();
    }

    void OnDestroy()
    {
        RemoveWeaponOverlay();
    }

    void OnValidate()
    {
        _initialized = false;
        _weaponBindings = Array.Empty<RendererBinding>();
        _bodyBindings = Array.Empty<RendererBinding>();
    }

    void Update()
    {
        float now = Time.time;
        float end = _cueHitTime + peakHold + decayTime;
        if (now >= end && now - _cueStartTime >= _cueDuration)
        {
            ResetCue();
            return;
        }

        // (a) 예비동작 램프: 피격 anticipationLead 초 전부터 0→1, 피격 후 유지·감쇠
        float level = ResolveAnticipationLevel(now);
        CurrentVisorLevel = level;

        if (_armor != null)
            _armor.SetTelegraphGlow(_cueColor, level, _bodyActive ? level : 0f);

        if (_overlayApplied)
            UpdateWeaponOverlay(now, level);

        // 장갑 리그가 없을 때만 기존 몸 틴트 폴백
        if (_armor == null && (_bodyActive || (_weaponActive && !_overlayApplied)))
        {
            float elapsed = now - _cueStartTime;
            float normalized = _cueDuration > 0f ? Mathf.Clamp01(elapsed / _cueDuration) : 1f;
            float pulse = Mathf.PingPong(normalized * Mathf.Max(1, _cuePulseCount), 1f);
            if (_cuePulseSharpness != 1f)
                pulse = Mathf.Pow(pulse, _cuePulseSharpness);
            float decay = _cueSustained ? 1f - normalized * normalized : 1f - normalized;
            Color color = _cueColor * (Mathf.SmoothStep(0.2f, 1f, pulse) * decay);
            if (_weaponActive && !_overlayApplied)
                ApplyColor(_weaponBindings, color);
            if (_bodyActive)
                ApplyColor(_bodyBindings, color);
        }
    }

    float ResolveAnticipationLevel(float now)
    {
        float rampStart = Mathf.Max(_cueStartTime, _cueHitTime - anticipationLead);
        if (now < rampStart)
            return 0f;
        if (now <= _cueHitTime)
        {
            float span = Mathf.Max(0.02f, _cueHitTime - rampStart);
            float t = Mathf.Clamp01((now - rampStart) / span);
            // 처음 빠르게 켜지고(인지) 끝에서 최고점 (타이밍)
            return Mathf.Lerp(0.35f, 1f, t * t);
        }
        float after = now - _cueHitTime;
        if (after <= peakHold)
            return 1f;
        return Mathf.Clamp01(1f - (after - peakHold) / Mathf.Max(0.01f, decayTime));
    }

    public bool PlayCue(BossAttackTelegraphType telegraphType, float duration, BossAttackTimingCueFlags cueFlags)
    {
        return PlayCue(telegraphType, duration, cueFlags, false);
    }

    /// <param name="followUp">다단 후속타 큐 (BossController.StringHitCues): 효과음을 작게.</param>
    public bool PlayCue(BossAttackTelegraphType telegraphType, float duration, BossAttackTimingCueFlags cueFlags, bool followUp)
    {
        bool wantsWeapon = (cueFlags & BossAttackTimingCueFlags.WeaponFlash) != 0;
        bool wantsBody = (cueFlags & BossAttackTimingCueFlags.BodyFlash) != 0;
        if (!wantsWeapon && !wantsBody)
            return false;

        Initialize();
        RestoreActiveCue();

        _weaponActive = wantsWeapon && (_weaponBindings.Length > 0 || _weaponOverlays.Length > 0);
        _bodyActive = wantsBody && (_bodyBindings.Length > 0 || _armor != null);
        bool hasVisor = _armor != null && _armor.HasVisorGlow;
        if (!_weaponActive && !_bodyActive && !hasVisor)
            return false;

        _cueType = telegraphType;
        _cueColor = ResolveCueColor(telegraphType);
        ResolveCueMotion(telegraphType);
        _cueStartTime = Time.time;
        _cueDuration = Mathf.Max(minimumDuration, duration);
        _cueHitTime = Time.time + Mathf.Max(0f, duration);
        CueCount++;

        if (_weaponActive && useWeaponSweep)
            ApplyWeaponOverlay();

        // (c) 타이밍 핑: 큐 순간 타입별 효과음. 후속타(무기 플래시만)는 작게.
        PlayCueSfx(telegraphType, followUp);

        enabled = true;
        Update();
        return true;
    }

    public void ResetCue()
    {
        RestoreActiveCue();
        if (_armor != null)
            _armor.SetTelegraphGlow(Color.black, 0f, 0f);
        CurrentVisorLevel = 0f;
        enabled = false;
    }

    void RestoreActiveCue()
    {
        if (_weaponActive && !_overlayApplied)
            RestoreIdle(_weaponBindings);
        if (_bodyActive && _armor == null)
            RestoreIdle(_bodyBindings);
        RemoveWeaponOverlay();

        _weaponActive = false;
        _bodyActive = false;
    }

    void Initialize()
    {
        if (_initialized)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        _emissionColorId = Shader.PropertyToID(emissionColorName);
        _baseColorId = Shader.PropertyToID(baseColorName);
        _legacyColorId = Shader.PropertyToID(legacyColorName);

        if (autoCollectWhenEmpty && IsNullOrEmpty(weaponRenderers) && IsNullOrEmpty(bodyRenderers))
            AutoCollectRenderers();

        if (_weaponBindings.Length == 0)
            _weaponBindings = BuildBindings(weaponRenderers);
        if (_bodyBindings.Length == 0)
            _bodyBindings = BuildBindings(bodyRenderers);

        Transform root = transform.root != null ? transform.root : transform;
        _armor = root.GetComponentInChildren<TypeDArmorRig>(true);
        BuildWeaponOverlays();
        EnsureCueAudio();

        _initialized = true;
    }

    void AutoCollectRenderers()
    {
        // 무기/몸 렌더러는 보스 루트 전체에서 찾는다 (이 컴포넌트가 PatternVisuals 같은 하위 오브젝트에 붙을 수 있다)
        Transform root = transform.root != null ? transform.root : transform;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactiveRenderers);
        int weaponCount = 0;
        int bodyCount = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEligible(renderer))
                continue;

            if (LooksLikeWeapon(renderer))
                weaponCount++;
            else
                bodyCount++;
        }

        weaponRenderers = weaponCount > 0 ? new Renderer[weaponCount] : Array.Empty<Renderer>();
        bodyRenderers = bodyCount > 0 ? new Renderer[bodyCount] : Array.Empty<Renderer>();

        int weaponIndex = 0;
        int bodyIndex = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEligible(renderer))
                continue;

            if (LooksLikeWeapon(renderer))
                weaponRenderers[weaponIndex++] = renderer;
            else
                bodyRenderers[bodyIndex++] = renderer;
        }
    }

    RendererBinding[] BuildBindings(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
            return Array.Empty<RendererBinding>();

        RendererBinding[] temp = new RendererBinding[renderers.Length];
        int count = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!IsEligible(renderer))
                continue;

            if (!TryBuildBinding(renderer, out RendererBinding binding))
                continue;

            bool duplicate = false;
            for (int j = 0; j < count; j++)
            {
                if (temp[j].Renderer == renderer)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                temp[count++] = binding;
        }

        if (count == temp.Length)
            return temp;

        RendererBinding[] result = new RendererBinding[count];
        Array.Copy(temp, result, count);
        return result;
    }

    bool TryBuildBinding(Renderer renderer, out RendererBinding binding)
    {
        binding = default;

        Material material = renderer.sharedMaterial;
        if (material == null)
            return false;

        int propertyId;
        Color idleColor;
        if (material.HasProperty(_emissionColorId))
        {
            propertyId = _emissionColorId;
            idleColor = material.GetColor(_emissionColorId);
            binding.IsEmission = true;
        }
        else if (material.HasProperty(_baseColorId))
        {
            propertyId = _baseColorId;
            idleColor = material.GetColor(_baseColorId);
        }
        else if (material.HasProperty(_legacyColorId))
        {
            propertyId = _legacyColorId;
            idleColor = material.GetColor(_legacyColorId);
        }
        else
        {
            return false;
        }

        binding.Renderer = renderer;
        binding.ColorPropertyId = propertyId;
        binding.IdleColor = idleColor;
        return true;
    }

    bool IsEligible(Renderer renderer)
    {
        if (renderer == null)
            return false;

        if (ignoreParticleRenderers && renderer is ParticleSystemRenderer)
            return false;

        // 런타임 VFX (트레일/텔레그래프/장갑 발광) 는 대상이 아니다
        string n = renderer.name;
        if (ContainsIgnoreCase(n, "trail") || ContainsIgnoreCase(n, "telegraph") || ContainsIgnoreCase(n, "TypeD_"))
            return false;

        return renderer.sharedMaterial != null;
    }

    bool LooksLikeWeapon(Renderer renderer)
    {
        string name = renderer != null ? renderer.name : string.Empty;
        return ContainsIgnoreCase(name, "weapon") ||
               ContainsIgnoreCase(name, "sword") ||
               ContainsIgnoreCase(name, "katana") ||
               ContainsIgnoreCase(name, "blade") ||
               ContainsIgnoreCase(name, "object002");
    }

    static bool ContainsIgnoreCase(string source, string value)
    {
        return !string.IsNullOrEmpty(source) &&
               source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsNullOrEmpty(Renderer[] renderers)
    {
        return renderers == null || renderers.Length == 0;
    }

    // ───────── 무기 날 발광 스윕 ─────────
    void BuildWeaponOverlays()
    {
        Material overlay = weaponOverlayMaterial != null ? weaponOverlayMaterial : Resources.Load<Material>("VFX/ChuonWeaponTelegraph");
        if (overlay == null || weaponRenderers == null)
        {
            _weaponOverlays = Array.Empty<WeaponOverlay>();
            return;
        }

        var list = new System.Collections.Generic.List<WeaponOverlay>();
        for (int i = 0; i < weaponRenderers.Length; i++)
        {
            Renderer r = weaponRenderers[i];
            if (r == null || !(r is MeshRenderer))
                continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;

            Bounds b = mf.sharedMesh.bounds;
            int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            Vector3 axisVec = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            float min = Vector3.Dot(b.min, axisVec);
            float max = Vector3.Dot(b.max, axisVec);
            // 칼끝 = 손(부모)에서 먼 끝
            Transform hand = r.transform.parent != null ? r.transform.parent : r.transform;
            Vector3 minWorld = r.transform.TransformPoint(b.center + axisVec * (min - Vector3.Dot(b.center, axisVec)));
            Vector3 maxWorld = r.transform.TransformPoint(b.center + axisVec * (max - Vector3.Dot(b.center, axisVec)));
            if ((minWorld - hand.position).sqrMagnitude > (maxWorld - hand.position).sqrMagnitude)
            {
                axisVec = -axisVec;
                float t = min;
                min = -max;
                max = -t;
            }

            Material[] original = r.sharedMaterials;
            Material[] with = new Material[original.Length + 1];
            original.CopyTo(with, 0);
            with[with.Length - 1] = overlay;
            list.Add(new WeaponOverlay
            {
                Renderer = r,
                Original = original,
                WithOverlay = with,
                Axis = new Vector4(axisVec.x, axisVec.y, axisVec.z, 0f),
                Range = new Vector4(min, max, 0f, 0f)
            });
        }

        _weaponOverlays = list.ToArray();
    }

    void ApplyWeaponOverlay()
    {
        if (_weaponOverlays.Length == 0)
            return;
        for (int i = 0; i < _weaponOverlays.Length; i++)
        {
            if (_weaponOverlays[i].Renderer != null)
                _weaponOverlays[i].Renderer.sharedMaterials = _weaponOverlays[i].WithOverlay;
        }
        _overlayApplied = true;
    }

    void RemoveWeaponOverlay()
    {
        if (!_overlayApplied)
            return;
        for (int i = 0; i < _weaponOverlays.Length; i++)
        {
            Renderer r = _weaponOverlays[i].Renderer;
            if (r == null)
                continue;
            r.sharedMaterials = _weaponOverlays[i].Original;
            _propertyBlock.Clear();
            r.SetPropertyBlock(_propertyBlock);
        }
        _overlayApplied = false;
    }

    void UpdateWeaponOverlay(float now, float level)
    {
        float rampStart = Mathf.Max(_cueStartTime, _cueHitTime - anticipationLead);
        float sweep = _cueHitTime > rampStart + 0.001f ? Mathf.Clamp01((now - rampStart) / (_cueHitTime - rampStart)) : 1f;
        sweep = now < rampStart ? -0.2f : sweep * 1.15f; // 칼끝을 살짝 지나 피격 순간 끝에 닿는다
        float charge = now >= _cueHitTime ? level : sweep * 0.6f;
        for (int i = 0; i < _weaponOverlays.Length; i++)
        {
            Renderer r = _weaponOverlays[i].Renderer;
            if (r == null)
                continue;
            _propertyBlock.Clear();
            _propertyBlock.SetColor(OverlayColorId, _cueColor);
            _propertyBlock.SetFloat(OverlaySweepId, sweep);
            _propertyBlock.SetFloat(OverlayChargeId, charge);
            _propertyBlock.SetFloat(OverlayGlowId, Mathf.Max(0.25f, level));
            _propertyBlock.SetVector(OverlayAxisId, _weaponOverlays[i].Axis);
            _propertyBlock.SetVector(OverlayRangeId, _weaponOverlays[i].Range);
            r.SetPropertyBlock(_propertyBlock);
        }
    }

    // ───────── 타이밍 핑 효과음 ─────────
    void EnsureCueAudio()
    {
        if (_cueAudio != null)
            return;
        LoadDefaultCueClips();
        CueSfxActive = playCueSfx && parryableCueClip != null;

        var go = new GameObject("BossTelegraphCueAudio");
        go.transform.SetParent(transform, false);
        _cueAudio = go.AddComponent<AudioSource>();
        _cueAudio.playOnAwake = false;
        _cueAudio.spatialBlend = 0.25f; // 대부분 2D: 거리와 무관하게 확실히 들려야 하는 경고음
        _cueAudio.dopplerLevel = 0f;
        _cueAudio.priority = 20;

        // 보스 오디오와 같은 믹서 그룹이면 SFX 볼륨은 믹서가 처리
        AudioSource bossAudio = GetComponentInParent<AudioSource>();
        if (bossAudio != null && bossAudio.outputAudioMixerGroup != null)
            _cueAudio.outputAudioMixerGroup = bossAudio.outputAudioMixerGroup;
    }

    void LoadDefaultCueClips()
    {
        if (parryableCueClip == null) parryableCueClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Parryable");
        if (dodgeOnlyCueClip == null) dodgeOnlyCueClip = Resources.Load<AudioClip>("Audio/TelegraphCue_DodgeOnly");
        if (unblockableCueClip == null) unblockableCueClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Unblockable");
        if (heavyCueClip == null) heavyCueClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Heavy");
        if (rangedCueClip == null) rangedCueClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Ranged");
    }

    AudioClip ResolveCueClip(BossAttackTelegraphType type)
    {
        switch (type)
        {
            case BossAttackTelegraphType.Parryable: return parryableCueClip;
            case BossAttackTelegraphType.DodgeOnly: return dodgeOnlyCueClip;
            case BossAttackTelegraphType.Unblockable: return unblockableCueClip;
            case BossAttackTelegraphType.Heavy: return heavyCueClip;
            case BossAttackTelegraphType.Ranged: return rangedCueClip;
            default: return null;
        }
    }

    void PlayCueSfx(BossAttackTelegraphType type, bool followUp)
    {
        if (!playCueSfx || _cueAudio == null)
            return;
        AudioClip clip = ResolveCueClip(type);
        if (clip == null)
            return;
        float now = Time.unscaledTime;
        if (now - _lastCueSfxTime < cueSfxMinInterval)
            return;
        _lastCueSfxTime = now;

        float volume = cueSfxVolume * (followUp ? followUpCueVolumeScale : 1f);
        if (_cueAudio.outputAudioMixerGroup == null)
            volume = AudioOptionsRuntime.ScaleSfx(volume);
        _cueAudio.pitch = followUp ? 1.08f : 1f;
        _cueAudio.PlayOneShot(clip, volume);
        LastCueSfx = clip.name + (followUp ? " (follow-up)" : "");
        BossController.NoteQaCueAudioPlayed(); // QA 텔레그래프 감사: 청각 신호 실제 재생
    }

    void ResolveCueMotion(BossAttackTelegraphType telegraphType)
    {
        _cuePulseCount = Mathf.Max(1, pulseCount);
        _cuePulseSharpness = 1f;
        _cueSustained = false;
        if (!varyMotionPerCategory)
            return;

        switch (telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                _cuePulseCount = 1;          // 한 번의 날카로운 글린트
                _cuePulseSharpness = 2.2f;
                break;
            case BossAttackTelegraphType.DodgeOnly:
                _cuePulseCount = 3;          // 빠른 경고 깜빡임
                break;
            case BossAttackTelegraphType.Unblockable:
                _cuePulseCount = 2;          // 오래 유지되는 강한 맥동
                _cuePulseSharpness = 0.6f;
                _cueSustained = true;
                break;
            case BossAttackTelegraphType.Heavy:
                _cuePulseCount = 2;          // 묵직한 2회 맥동
                _cueSustained = true;
                break;
        }
    }

    /// <summary>The telegraph colour language (parryable gold / dodge-only red / unblockable magenta ...), colourblind setting included.</summary>
    public Color GetCueColor(BossAttackTelegraphType telegraphType) => ResolveCueColor(telegraphType);

    Color ResolveCueColor(BossAttackTelegraphType telegraphType)
    {
        if (useColorblindSafePalette || UseColorblindSafePalette)
            return ResolveColorblindSafeColor(telegraphType);

        switch (telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return parryableColor;
            case BossAttackTelegraphType.DodgeOnly:
                return dodgeOnlyColor;
            case BossAttackTelegraphType.Unblockable:
                return unblockableColor;
            case BossAttackTelegraphType.Heavy:
                return heavyColor;
            case BossAttackTelegraphType.Ranged:
                return rangedColor;
            case BossAttackTelegraphType.Normal:
                return normalColor;
            default:
                return normalColor;
        }
    }

    // Okabe-Ito 계열: 적/녹 구분 없이 명도·색상으로 식별 가능
    Color ResolveColorblindSafeColor(BossAttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return new Color(0.94f, 0.89f, 0.26f) * 2.4f;  // yellow
            case BossAttackTelegraphType.DodgeOnly:
                return new Color(0.84f, 0.37f, 0f) * 2.6f;     // vermillion
            case BossAttackTelegraphType.Unblockable:
                return new Color(0.8f, 0.47f, 0.65f) * 3f;     // reddish purple
            case BossAttackTelegraphType.Heavy:
                return new Color(0f, 0.45f, 0.7f) * 3f;        // blue
            case BossAttackTelegraphType.Ranged:
                return new Color(0.34f, 0.71f, 0.91f) * 2.2f;  // sky blue
            default:
                return normalColor;
        }
    }

    void ApplyColor(RendererBinding[] bindings, Color color)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            Renderer renderer = bindings[i].Renderer;
            if (renderer == null)
                continue;

            _propertyBlock.Clear();
            renderer.GetPropertyBlock(_propertyBlock);
            Color resolvedColor = bindings[i].IsEmission
                ? color
                : Color.Lerp(bindings[i].IdleColor, color, baseColorCueBlend);
            _propertyBlock.SetColor(bindings[i].ColorPropertyId, resolvedColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    void RestoreIdle(RendererBinding[] bindings)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            Renderer renderer = bindings[i].Renderer;
            if (renderer == null)
                continue;

            _propertyBlock.Clear();
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(bindings[i].ColorPropertyId, bindings[i].IdleColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
