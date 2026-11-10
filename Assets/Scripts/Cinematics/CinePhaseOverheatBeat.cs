using UnityEngine;

/// <summary>
/// 장면 7 "페이즈 전환: Type-D 과열·균열" (Docs/Cinematics_Shotlist.md §7).
/// 전투 중에는 카메라·조작을 뺏지 않는다(소울라이크 원칙). 대신 1.6 s 동안 세계 안에서 "짧은 컷"처럼 읽히는 연출을 겹친다:
///   증기 분출(등 배기구) + 붉은 열광 점광원 + 갈라진 균열 발광 링 + 저역 임팩트 + 카메라 리그 흔들림(옵션 배율 적용).
/// TypeDArmorRig 의 코어 펄스(기존)와 Type-D 시스템 음성 bark(기존 phase2.typed/phase3.typed)는 그대로 쓴다.
/// 보스 오브젝트(또는 아무 곳)에 붙이면 BossController.OnBossPhaseChanged 를 구독한다. 파티클은 Awake 에 한 번 만든다(재생 중 생성 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class CinePhaseOverheatBeat : MonoBehaviour
{
    [SerializeField] BossController boss;
    [SerializeField, Min(2)] int minPhase = 2;
    [SerializeField, Min(0.2f)] float duration = 1.6f;
    [SerializeField] Color heatColor = new Color(1f, 0.22f, 0.12f, 1f);
    [SerializeField, Min(0f)] float heatLightIntensity = 9f;
    [SerializeField, Min(0f)] float shakeAmplitude = 0.05f;
    [SerializeField] AudioClip impactClip;
    [SerializeField, Range(0f, 1f)] float impactVolume = 0.8f;
    [Tooltip("비우면 URP 파티클 언릿(또는 Sprites/Default)으로 런타임 생성")]
    [SerializeField] Material steamMaterial;
    [SerializeField] Material emberMaterial;

    Transform _anchor;
    ParticleSystem _steam;
    ParticleSystem _embers;
    Light _heat;
    AudioSource _audio;
    float _startedAt = -100f;
    bool _running;
    int _lastPhase = 1;

    public static int PlayCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PlayCount = 0;

    void Awake()
    {
        if (boss == null)
            boss = GetComponentInParent<BossController>();
        if (boss == null)
            boss = GameplaySceneCache.ResolveBossController();
        Build();
    }

    void OnEnable()
    {
        if (boss != null)
            boss.OnBossPhaseChanged += HandlePhaseChanged;
    }

    void OnDisable()
    {
        if (boss != null)
            boss.OnBossPhaseChanged -= HandlePhaseChanged;
        StopBeat();
    }

    void HandlePhaseChanged(int phase, float hp)
    {
        if (phase < minPhase || phase <= _lastPhase)
            return;
        _lastPhase = phase;
        Play(phase);
    }

    /// <summary>테스트/통합용 직접 호출.</summary>
    public void Play(int phase)
    {
        ResolveAnchor();
        if (_anchor == null)
            return;
        PlayCount++;
        _startedAt = Time.unscaledTime;
        _running = true;

        Transform root = _steam.transform.parent;
        root.SetPositionAndRotation(_anchor.position, _anchor.rotation);

        float scale = phase >= 3 ? 1.35f : 1f;
        ParticleSystem.MainModule main = _steam.main;
        main.startSizeMultiplier = 0.55f * scale;
        _steam.Clear(true);
        _steam.Play(true);
        _embers.Clear(true);
        _embers.Play(true);
        _heat.enabled = true;

        if (shakeAmplitude > 0f)
            SoulsCameraRig.TryRouteShake(shakeAmplitude * scale * CameraShake.GlobalStrength, 0.35f);
        if (impactClip != null && _audio != null)
            _audio.PlayOneShot(impactClip, impactVolume);
        GameLog.Info($"[CinePhaseOverheat] phase {phase} beat", this);
    }

    void Update()
    {
        if (!_running)
            return;
        float t = (Time.unscaledTime - _startedAt) / duration;
        if (t >= 1f)
        {
            StopBeat();
            return;
        }

        if (_anchor != null)
            _steam.transform.parent.position = _anchor.position;
        // 열광: 0.12 s 치솟고 천천히 식는다 + 불규칙 맥동
        float env = t < 0.08f ? t / 0.08f : Mathf.Pow(1f - (t - 0.08f) / 0.92f, 1.6f);
        float pulse = 0.75f + 0.25f * Mathf.PerlinNoise(Time.unscaledTime * 14f, 0.5f);
        _heat.intensity = heatLightIntensity * env * pulse * Mathf.Lerp(0.4f, 1f, AccessibilityOptionsRuntime.FlashIntensity);
    }

    void StopBeat()
    {
        _running = false;
        if (_heat != null)
            _heat.enabled = false;
        if (_steam != null)
            _steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (_embers != null)
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void ResolveAnchor()
    {
        if (_anchor != null)
            return;
        if (boss == null)
            boss = GameplaySceneCache.ResolveBossController();
        if (boss == null)
            return;
        Animator animator = boss.GetComponentInChildren<Animator>();
        Transform chest = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.UpperChest) : null;
        if (chest == null && animator != null && animator.isHuman)
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        _anchor = chest != null ? chest : boss.transform;
    }

    void Build()
    {
        var root = new GameObject("PhaseOverheatFx").transform;
        root.SetParent(transform, false);

        _steam = CreateSystem("Steam", root, new Color(0.86f, 0.86f, 0.88f, 0.55f), 2.4f, 1.1f, 46f, 0.55f, -0.15f, 0.9f, steamMaterial);
        // 반복 15: 흰 네모 블록 → 부드러운 퍼프 텍스처(M_CINE_Steam) + 그래파이트~화이트 색 분산 + 회전 + 감속(뭉게뭉게 퍼짐)
        ParticleSystem.MainModule steamMain = _steam.main;
        steamMain.startColor = new ParticleSystem.MinMaxGradient(new Color(0.34f, 0.35f, 0.38f, 0.5f), new Color(0.9f, 0.9f, 0.92f, 0.55f));
        steamMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        ParticleSystem.RotationOverLifetimeModule steamRot = _steam.rotationOverLifetime;
        steamRot.enabled = true;
        steamRot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        ParticleSystem.LimitVelocityOverLifetimeModule steamDrag = _steam.limitVelocityOverLifetime;
        steamDrag.enabled = true;
        steamDrag.limit = 0.6f;
        steamDrag.dampen = 0.12f;
        ParticleSystem.ShapeModule shape = _steam.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.12f;
        // 등 배기구: 뒤쪽 위로 두 줄기
        _steam.transform.localRotation = Quaternion.Euler(-60f, 180f, 0f);
        _steam.transform.localPosition = new Vector3(0f, 0.05f, -0.18f);

        _embers = CreateSystem("Embers", root, heatColor, 4.5f, 0.55f, 70f, 0.035f, 0.35f, 0.6f, emberMaterial);
        ParticleSystem.ShapeModule eshape = _embers.shape;
        eshape.shapeType = ParticleSystemShapeType.Sphere;
        eshape.radius = 0.35f;
        // 불티: 속도 방향으로 늘린 작은 점(스파크), 레드~앰버
        ParticleSystem.MainModule emberMain = _embers.main;
        emberMain.startColor = new ParticleSystem.MinMaxGradient(heatColor, new Color(1f, 0.55f, 0.22f, 1f));
        var emberRenderer = _embers.GetComponent<ParticleSystemRenderer>();
        emberRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        emberRenderer.velocityScale = 0.04f;
        emberRenderer.lengthScale = 1.6f;

        var lightGo = new GameObject("HeatLight");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.1f, 0.35f);
        _heat = lightGo.AddComponent<Light>();
        _heat.type = LightType.Point;
        _heat.color = heatColor;
        _heat.range = 5.5f;
        _heat.shadows = LightShadows.None;
        _heat.enabled = false;

        _audio = gameObject.GetComponent<AudioSource>();
        if (_audio == null)
            _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0.6f;
        ChuonAudioMix.Route(_audio, ChuonAudioMix.Bus.Sfx);
    }

    static ParticleSystem CreateSystem(string name, Transform parent, Color color, float speed, float life, float burst, float size, float gravity, float sizeGrow, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.duration = 0.6f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles = 160;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(burst * 0.6f)), new ParticleSystem.Burst(0.18f, (short)(burst * 0.4f)) });
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1f + sizeGrow * 2f));
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader shader = material != null ? null : (Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"));
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }
        else if (shader != null)
        {
            var mat = new Material(shader) { name = "M_CinePhase_" + name };
            Texture2D soft = SoftParticleTexture;
            mat.SetTexture("_BaseMap", soft);
            mat.SetTexture("_MainTex", soft);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", name == "Embers" ? 2f : 0f); // Embers: additive, Steam: alpha
            mat.SetColor("_BaseColor", Color.white);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", name == "Embers" ? (int)UnityEngine.Rendering.BlendMode.One : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            renderer.sharedMaterial = mat;
        }
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // 재질이 없을 때의 폴백 텍스처: 원형 소프트 폴오프 (텍스처 없는 파티클 재질 = 흰 네모)
    static Texture2D s_soft;
    static Texture2D SoftParticleTexture
    {
        get
        {
            if (s_soft != null)
                return s_soft;
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.6f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            s_soft = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "_CinePhaseSoft", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            s_soft.SetPixels32(px);
            s_soft.Apply(true, true);
            return s_soft;
        }
    }
}
