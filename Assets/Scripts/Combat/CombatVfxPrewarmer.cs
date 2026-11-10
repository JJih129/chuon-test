using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스전 VFX 워밍업 (Level_Facility 전용, 씬에 배치된 경우에만 동작).
/// 상층 → 하층 핸드오프(LobbyManager.LowerFloorReached: 엘리베이터 하강 암전 / 체크포인트 스킵) 시점에
/// 보스 검기·피격·소드웨이브·궁극기·플레이어 피격 VFX 를 TransientVfxPool 에 미리 생성하고,
/// 플레이어 시야 밖에서 전용 카메라로 1회 렌더해 첫 사용 시 Instantiate / 셰이더·텍스처 업로드 히치를 없앤다.
/// (드론 프리웜 LobbyManager.CoPrewarmLobbyDrones 와 같은 방식)
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatVfxPrewarmer : MonoBehaviour
{
    const string DefaultUltimateSequencePath = "Ultimate/UltimateSequence_Default";

    [SerializeField] bool prewarmOnLowerFloorReached = true;
    [Tooltip("프리팹당 풀에 미리 만들어 둘 인스턴스 수")]
    [SerializeField, Min(1)] int instancesPerPrefab = 2;
    [Tooltip("한 프레임에 처리할 프리팹 수 (히치 분산)")]
    [SerializeField, Min(1)] int prefabsPerFrame = 4;
    [SerializeField, Min(1)] int warmupRenderFrames = 2;
    [SerializeField] bool includeUltimateVfx = true;
    [SerializeField] GameObject[] extraPrefabs = System.Array.Empty<GameObject>();

    bool _done;

    public bool HasPrewarmed => _done;

    void OnEnable()
    {
        LobbyManager.LowerFloorReached += HandleLowerFloorReached;
    }

    void OnDisable()
    {
        LobbyManager.LowerFloorReached -= HandleLowerFloorReached;
    }

    void HandleLowerFloorReached(LobbyManager lobby)
    {
        if (prewarmOnLowerFloorReached)
            Prewarm();
    }

    public void Prewarm()
    {
        if (_done || !isActiveAndEnabled)
            return;

        _done = true;
        StartCoroutine(CoPrewarm());
    }

    List<GameObject> CollectPrefabs()
    {
        var list = new List<GameObject>(32);
        foreach (BossAttackVfxPresenter presenter in FindObjectsOfType<BossAttackVfxPresenter>(true))
            presenter.CollectPrewarmPrefabs(list);
        foreach (BossAttackFeedbackPresenter presenter in FindObjectsOfType<BossAttackFeedbackPresenter>(true))
            presenter.CollectPrewarmPrefabs(list);
        foreach (BossHitReaction reaction in FindObjectsOfType<BossHitReaction>(true))
        {
            list.Add(reaction.normalHitVfx);
            list.Add(reaction.heavyHitVfx);
        }
        foreach (PlayerHealth health in FindObjectsOfType<PlayerHealth>(true))
            list.Add(health.HitEffectPrefab);
        foreach (UltimateTargetSimple target in FindObjectsOfType<UltimateTargetSimple>(true))
            list.Add(target.hitVFXPrefab);

        if (includeUltimateVfx)
        {
            UltimateSequenceData data = Resources.Load<UltimateSequenceData>(DefaultUltimateSequencePath);
            if (data != null && data.Vfx != null)
            {
                UltimateSequenceData.VfxSettings vfx = data.Vfx;
                list.Add(vfx.introPoseVfxPrefab);
                list.Add(vfx.dashSlashVfxPrefab);
                list.Add(vfx.lightBeamVfxPrefab);
                list.Add(vfx.crackWorldVfxPrefab);
                list.Add(vfx.explosionVfxPrefab);
                list.Add(vfx.shardVfxPrefab);
                list.Add(vfx.walkoutVfxPrefab);
                list.Add(vfx.slashStormAoeVfxPrefab);
            }
        }

        if (extraPrefabs != null)
            list.AddRange(extraPrefabs);

        var unique = new HashSet<GameObject>();
        list.RemoveAll(prefab => prefab == null || !unique.Add(prefab));
        return list;
    }

    IEnumerator CoPrewarm()
    {
        yield return null; // 핸드오프 프레임은 건드리지 않는다

        List<GameObject> prefabs = CollectPrefabs();
        BossController boss = FindObjectOfType<BossController>();
        Vector3 origin = boss != null ? boss.transform.position : transform.position;
        Vector3 basePosition = origin + Vector3.up * 150f;

        var cameraObject = new GameObject("__CombatVfxWarmupCamera");
        cameraObject.transform.SetPositionAndRotation(basePosition + new Vector3(0f, 0.5f, -6f), Quaternion.identity);
        Camera warmupCamera = cameraObject.AddComponent<Camera>();
        RenderTexture warmupTarget = RenderTexture.GetTemporary(256, 256, 16);
        warmupCamera.targetTexture = warmupTarget;
        warmupCamera.depth = -100f;
        warmupCamera.nearClipPlane = 0.1f;
        warmupCamera.farClipPlane = 40f;
        warmupCamera.fieldOfView = 70f;
        // 아레나와 같은 셰이더 변형(_ADDITIONAL_LIGHTS)을 컴파일하도록 임시 포인트 라이트
        var lightObject = new GameObject("__CombatVfxWarmupLight");
        lightObject.transform.position = basePosition + new Vector3(0f, 2f, -2f);
        Light warmupLight = lightObject.AddComponent<Light>();
        warmupLight.type = LightType.Point;
        warmupLight.range = 12f;
        warmupLight.shadows = LightShadows.None;

        int processed = 0;
        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            TransientVfxPool.Prewarm(prefab, instancesPerPrefab);
            // 카메라 앞 격자에 1개씩 재생 → 렌더 1회 (셰이더/텍스처 업로드). 수명이 끝나면 풀로 자동 회수.
            Vector3 position = basePosition + new Vector3(((i % 5) - 2) * 1.4f, (i / 5 % 3 - 1) * 1.2f, 1.5f);
            GameObject spawned = TransientVfxPool.Spawn(prefab, position, Quaternion.identity, null, 0.5f);
            MuteAudio(spawned);

            if (++processed % prefabsPerFrame == 0)
                yield return null;
        }

        // 소드웨이브 투사체 (정적 풀 + Resources 비주얼): 무해한 설정으로 1발 생성 → 수명 후 풀로 복귀
        BossSwordWaveProjectile wave = BossSwordWaveProjectile.Spawn(basePosition + new Vector3(0f, -1.5f, 2f), Quaternion.identity, boss != null ? boss.gameObject.layer : gameObject.layer);
        if (wave != null)
            wave.Configure(null, 0f, 0.1f, 0.3f, 2f, 1f, 1f, false, false, false, false, false);

        // 보스 피해 균열 (HP 50%/25% 첫 진입 히치): 텍스처 2장 생성 + 오버레이 재질을 워밍업 카메라 앞 쿼드로 1회 렌더
        GameObject crackQuadA = null, crackQuadB = null;
        Material crackPrewarmMaterial = null;
        Material crackMaterial = BossDamageCracks.PrewarmShared(out Texture2D crackStage2);
        if (crackMaterial != null)
        {
            crackQuadA = CreateWarmupQuad(basePosition + new Vector3(-0.8f, 0.4f, 2.5f), crackMaterial);
            if (crackStage2 != null)
            {
                crackPrewarmMaterial = new Material(crackMaterial) { name = "_BossDamageCracks (prewarm stage2)", hideFlags = HideFlags.DontSave, mainTexture = crackStage2 };
                crackQuadB = CreateWarmupQuad(basePosition + new Vector3(0.8f, 0.4f, 2.5f), crackPrewarmMaterial);
            }
        }

        for (int frame = 0; frame < warmupRenderFrames; frame++)
            yield return null;

        if (crackQuadA != null) Destroy(crackQuadA);
        if (crackQuadB != null) Destroy(crackQuadB);
        if (crackPrewarmMaterial != null) Destroy(crackPrewarmMaterial);

        warmupCamera.targetTexture = null;
        RenderTexture.ReleaseTemporary(warmupTarget);
        Destroy(cameraObject);
        Destroy(lightObject);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[CombatVfxPrewarmer] Prewarmed {prefabs.Count} VFX prefabs (+ sword wave).", this);
#endif

        if (prewarmUltimateCode)
            yield return CoPrewarmUltimateCode();
    }

    // ------------------------------------------------------------------
    // 궁극기 첫 발동 히치(반복 12 프로파일: ~99 ms = Mono JIT ~60 ms + Timeline 그래프 첫 생성 ~33 ms)를
    // 하층 도착 암전 중에 미리 소진한다. 렌더/애니메이터 문제가 아니라 첫 호출 JIT 였다.
    // 1) 궁극기 관련 타입(중첩 코루틴 타입 포함) 메서드를 프레임당 1타입씩 PrepareMethod 로 JIT
    // 2) 씬의 PlayableDirector 타임라인을 바인딩 없는 임시 그래프로 1회 생성·평가 후 파괴 (Timeline/Cinemachine 코드 JIT)
    [Tooltip("궁극기 코드 JIT + 타임라인 그래프를 하층 도착 시 미리 돌린다 (첫 발동 히치 제거)")]
    [SerializeField] bool prewarmUltimateCode = true;

    static readonly string[] UltimateTypeNames =
    {
        "PlayerUltimateController", "UltimatePhaseSeveringBeats", "UltimateCinematicController", "UltimateSkillController",
        "UltimateVFXPresenter", "UltimateCameraDirector", "UltimateCameraSessionController", "UltimateScreenFX",
        "UltimateCinematicHudFrame", "UltimateSlashBurstSpawner", "UltimateSwordCoreChargeFx", "UltimateScreenCrackOverlay",
        "UltimateEnemyCinematicState", "UltimateTargetBinder", "UltimateSequencePlayer", "UltimateHitProcessor",
        "SlashSequenceClip", "SlashSequenceBehaviour", "ScreenFXSignalHooks", "TimeScaleController",
        "ScreenCut", "ActionColourDirector",
        "ActionColourDirector", "ScreenShatter", "PhaseCoreHUD", "MusicDirector",
        // 반복 14A: 튜토리얼 첫 퍼펙트 회피 슬로우(110 ms) / 튜토리얼 궁극기 런타임 구성 경로
        "PerfectDodgeController", "PerfectDodgeAfterImageEffect", "TutorialRuntimeBootstrap", "InfMobUltimateTarget", "UltimateTargetSimple"
    };

    /// <summary>
    /// 보스 아레나가 아닌 씬(튜토리얼 VR 부팅 등)에서 같은 예열을 실행한다. 씬에 없으면 host 에 붙인다.
    /// </summary>
    public static CombatVfxPrewarmer EnsureAndPrewarm(GameObject host)
    {
        CombatVfxPrewarmer prewarmer = FindObjectOfType<CombatVfxPrewarmer>();
        if (prewarmer == null && host != null)
            prewarmer = host.AddComponent<CombatVfxPrewarmer>();
        if (prewarmer != null)
            prewarmer.Prewarm();
        return prewarmer;
    }

    IEnumerator CoPrewarmUltimateCode()
    {
        float start = Time.realtimeSinceStartup;
        int methods = 0;
        System.Reflection.Assembly asm = typeof(CombatVfxPrewarmer).Assembly;

        // 대상 타입: 궁극기 목록 + 플레이어/보스 계층의 모든 게임 컴포넌트 타입 + 궁극기 이벤트 구독자 타입
        // (발동 프레임에 TryActivate 아래에서 처음 호출되는 코드가 이 범위에 몰려 있다)
        var types = new List<System.Type>(128);
        var seen = new HashSet<System.Type>();
        void AddType(System.Type t)
        {
            if (t == null || t.Assembly != asm || !seen.Add(t))
                return;
            types.Add(t);
            foreach (System.Type nested in t.GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                AddType(nested);
        }
        for (int i = 0; i < UltimateTypeNames.Length; i++)
            AddType(asm.GetType(UltimateTypeNames[i]));
        var components = new List<MonoBehaviour>(256);
        PlayerHealth playerHealth = FindObjectOfType<PlayerHealth>();
        BossController bossController = FindObjectOfType<BossController>();
        if (playerHealth != null)
            playerHealth.transform.root.GetComponentsInChildren(true, components);
        foreach (MonoBehaviour c in components)
            if (c != null) AddType(c.GetType());
        components.Clear();
        if (bossController != null)
            bossController.transform.root.GetComponentsInChildren(true, components);
        foreach (MonoBehaviour c in components)
            if (c != null) AddType(c.GetType());
        foreach (PlayerUltimateController ult in FindObjectsOfType<PlayerUltimateController>(true))
            AddDelegateTargets(ult, AddType);

        // Timeline / Cinemachine(타임라인 트랙·믹서·브레인 블렌드) 코드도 같은 예산으로 JIT
        // → 아래 임시 그래프 생성이 한 프레임에 몰아 JIT 하던 ~65 ms 를 분산
        void AddExternal(System.Type t)
        {
            if (t != null && !t.ContainsGenericParameters && seen.Add(t))
                types.Add(t);
        }
        foreach (System.Type t in typeof(UnityEngine.Timeline.TimelineAsset).Assembly.GetTypes())
            AddExternal(t);
        foreach (System.Type t in typeof(Unity.Cinemachine.CinemachineBrain).Assembly.GetTypes())
        {
            string n = t.Name;
            if (n.Contains("Brain") || n.Contains("Blend") || n.Contains("Track") || n.Contains("Shot") || n.Contains("Mixer") || n.Contains("Playable"))
                AddExternal(t);
        }

        // 프레임당 ~4 ms 예산으로 메서드 단위 JIT (도착 암전 중 큰 히치 방지)
        var budget = System.Diagnostics.Stopwatch.StartNew();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        for (int i = 0; i < types.Count; i++)
        {
            System.Type type = types[i];
            if (type.ContainsGenericParameters)
                continue;
            foreach (System.Reflection.MethodBase method in EnumerateMethods(type, flags))
            {
                if (TryPrepare(method))
                    methods++;
                if (budget.Elapsed.TotalMilliseconds > 4.0)
                {
                    yield return null;
                    budget.Restart();
                }
            }
        }
        yield return null;

        int graphs = 0;
        foreach (UnityEngine.Playables.PlayableDirector director in FindObjectsOfType<UnityEngine.Playables.PlayableDirector>(true))
        {
            if (director == null || director.playableAsset == null || director.state == UnityEngine.Playables.PlayState.Playing)
                continue;
            UnityEngine.Playables.PlayableGraph graph = UnityEngine.Playables.PlayableGraph.Create("__UltimatePrewarm");
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                director.playableAsset.CreatePlayable(graph, director.gameObject);
                graph.Evaluate(0f);
                graphs++;
            }
            catch (System.Exception e)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[CombatVfxPrewarmer] timeline prewarm skipped ({director.name}): {e.Message}", director);
#endif
            }
            finally
            {
                if (graph.IsValid())
                    graph.Destroy();
            }
            yield return null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[CombatVfxPrewarmer] Ultimate code prewarm: {methods} methods JIT, {graphs} timeline graphs, {(Time.realtimeSinceStartup - start) * 1000f:F0} ms over frames.", this);
#endif
    }

    static IEnumerable<System.Reflection.MethodBase> EnumerateMethods(System.Type type, System.Reflection.BindingFlags flags)
    {
        foreach (System.Reflection.MethodInfo method in type.GetMethods(flags))
            yield return method;
        foreach (System.Reflection.ConstructorInfo ctor in type.GetConstructors(flags))
            yield return ctor;
    }

    static bool TryPrepare(System.Reflection.MethodBase method)
    {
        if (method.IsAbstract || method.ContainsGenericParameters)
            return false;
        try
        {
            if (method.GetMethodBody() == null)
                return false;
            // Mono 의 PrepareMethod 는 사실상 no-op (반복 12 측정: JIT 31 ms 그대로) → GetFunctionPointer 가 mono_compile_method 로 실제 JIT
            method.MethodHandle.GetFunctionPointer();
            return true;
        }
        catch (System.Exception)
        {
            // 일부 메서드(extern/특수)는 미리 컴파일할 수 없다 → 첫 호출 때 JIT
            return false;
        }
    }

    // 궁극기 컨트롤러의 C# 이벤트(OnUltimateStarted 등) 구독자 타입도 발동 프레임에 처음 호출된다
    static void AddDelegateTargets(object owner, System.Action<System.Type> add)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        foreach (System.Reflection.FieldInfo field in owner.GetType().GetFields(flags))
        {
            if (!typeof(System.Delegate).IsAssignableFrom(field.FieldType))
                continue;
            if (!(field.GetValue(field.IsStatic ? null : owner) is System.Delegate d))
                continue;
            foreach (System.Delegate entry in d.GetInvocationList())
            {
                if (entry.Target != null)
                    add(entry.Target.GetType());
                if (entry.Method != null)
                    add(entry.Method.DeclaringType);
            }
        }
    }

    static GameObject CreateWarmupQuad(Vector3 position, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "__CombatVfxWarmupQuad";
        Collider collider = quad.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        quad.transform.SetPositionAndRotation(position, Quaternion.identity);
        quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        return quad;
    }

    static void MuteAudio(GameObject spawned)
    {
        if (spawned == null)
            return;

        AudioSource[] sources = spawned.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
            sources[i].Stop();
    }
}
