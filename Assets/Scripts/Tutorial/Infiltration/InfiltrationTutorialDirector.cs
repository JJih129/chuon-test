using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

// 반복 13: 침투 경로 VR 시뮬레이션 = 숨겨진 튜토리얼 (Docs/LevelDesign_Infiltration.md).
// 거리 → 외곽 검문소 → 하역장 → 측면 출입구 → 본사 로비 문 앞에서 시뮬레이션 종료 → Level_Facility.
// 기존 시스템 재사용: TutorialFlowController(단계), TutorialConditionChecker 계열(조건), EGOGuideController/TutorialHintUIBridge(대사·목표),
// 대사 테이블 키 "tut.inf.<단계>.intro|success|remind.N", TutorialExitInteractableBridge(출구), VrTransitionOverlay(부팅/종료).
// 씬에 TutorialFlowController 가 있으므로 TutorialRuntimeBootstrap(구 사격장 프로토타입)은 생성되지 않는다.
[DefaultExecutionOrder(-5000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(TutorialFlowController))]
public sealed class InfiltrationTutorialDirector : MonoBehaviour
{
    public static InfiltrationTutorialDirector Active { get; private set; }

    [Header("Player")]
    [SerializeField] Transform playerStart;

    [Header("Zones (TutorialZoneTrigger)")]
    [SerializeField] TutorialZoneTrigger zoneMove;
    [SerializeField] TutorialZoneTrigger zoneScanPassed;
    [SerializeField] TutorialZoneTrigger zoneDockYard;
    [SerializeField] TutorialZoneTrigger zoneSideYard;
    [SerializeField] TutorialZoneTrigger zoneExit;

    [Header("Targets")]
    [SerializeField] Transform lookTarget;
    [SerializeField] Transform gateGoal;
    [SerializeField] Transform passageGoal;
    [SerializeField] Transform rampGoal;

    [Header("Checkpoints (VR 재동기화 지점)")]
    [SerializeField] Transform cpStreet;
    [SerializeField] Transform cpPlaza;
    [SerializeField] Transform cpDock;
    [SerializeField] Transform cpPlatform;
    [SerializeField] Transform cpSide;

    [Header("Mobs (Assets/Prefabs/Mobs)")]
    [SerializeField] GameObject androidPrefab;
    [SerializeField] GameObject enforcerPrefab;
    [SerializeField] GameObject wallTurretPrefab;
    [SerializeField] GameObject floorTurretPrefab;

    [Header("Spawns")]
    [SerializeField] Transform androidPatrolSpawn;
    [SerializeField] Transform[] androidPatrolPoints = Array.Empty<Transform>();
    [SerializeField] Transform boothSpawn;
    [SerializeField] Transform wallTurretMount;
    [SerializeField] Transform platformTurretSpawn;
    [SerializeField] Transform enforcerSpawn;
    [SerializeField] Transform sideTurretSpawn;

    [Header("Gates / hazards")]
    [SerializeField] InfGate checkpointGate;
    [SerializeField] InfGate passageBarrier;
    [SerializeField] InfScannerCurtain scanner;

    [Header("Exit")]
    [SerializeField] SceneLoadInteractable exitInteractable;

    [Header("FX")]
    [SerializeField] Material simFxMaterial;
    [SerializeField] AudioClip simSpawnSfx;

    [Header("Soft failure")]
    [SerializeField, Range(0.05f, 0.6f)] float resyncHealthRatio = 0.25f;
    [SerializeField, Range(0.1f, 0.6f)] float lessonMobHpFloor = 0.25f;
    [Tooltip("반복 14A: 콤보 레슨 경비 체력 하한 (강공 한 방에 하한을 뚫고 쓰러지지 않도록 일반 하한보다 높게)")]
    [SerializeField, Range(0.1f, 0.8f)] float comboHpFloor = 0.45f;
    [Tooltip("반복 14A: 콤보 레슨 경비가 맞을 때마다 다음 공격을 미루는 시간 (연타 중 반격 없음)")]
    [SerializeField, Min(0f)] float comboHitAttackDelay = 1.4f;
    int _comboHitsSeen = -1;

    // ---------------------------------------------------------------- runtime
    TutorialFlowController _flow;
    TutorialHintUIBridge _hint;
    EGOGuideController _ego;
    TutorialPlayerRuntimeBridge _bridge;
    PlayerLockOn _lockOn;
    GameObject _player;
    PlayerHealth _playerHealth;
    Camera _camera;
    Canvas _canvas;
    TutorialWorldMarker _marker;
    TutorialScreenTargetIndicator _indicator;
    Transform _goal;

    InfMobHitChecker _comboChecker;
    InfMobsDefeatedChecker _clearChecker;
    InfRedCueSurviveChecker _redChecker;
    InfGuardBreakChecker _guardBreakChecker;
    TutorialPerfectDodgeConditionChecker _pdChecker;

    EnemyBrain _android1, _android2, _wallTurret, _platformTurret, _enforcer, _sideTurret;
    EnemyBrain _takedownTarget; // 반복 16: 잠입 암살 레슨 (경계 전 경비 등 뒤에서 처형)
    InfMobsDefeatedChecker _takedownChecker;
    Transform _takedownSpawn;
    readonly List<EnemyBrain> _spawned = new List<EnemyBrain>();
    readonly HashSet<EnemyBrain> _hpFloor = new HashSet<EnemyBrain>();
    bool _resyncing;
    bool _ultimateConfigured;
    bool _ultFired;
    int _pdFailures;
    bool _pdAssist;
    float _respawnAt = -1f;
    static int s_resumeStep = -1;
    /// <summary>키오스크 리셋 등: 튜토리얼 재개 단계 정리 (다음 방문객은 처음부터).</summary>
    public static void ClearCheckpointState() => s_resumeStep = -1;

    // ---------------------------------------------------------------- QA (dev route bot / reports)
    public string CurrentStepId => _flow != null && _flow.CurrentStep != null ? _flow.CurrentStep.stepId : null;
    public int CurrentStepIndex => _flow != null ? _flow.CurrentStepIndex : -1;
    public int StepCount => _flow != null ? _flow.StepCount : 0;
    public Transform CurrentGoal => _goal;
    public Transform LookTarget => lookTarget;
    public InfScannerCurtain Scanner => scanner;
    public SceneLoadInteractable ExitInteractable => exitInteractable;
    public TutorialFlowController Flow => _flow;
    public int ResyncCount { get; private set; }
    public int LessonRespawns { get; private set; }
    public int PerfectDodgeAssists { get; private set; }
    public bool IsResyncing => _resyncing;
    public float StepStartedAt { get; private set; }
    public readonly List<string> StepLog = new List<string>();
    public event Action<TutorialStepDefinition> StepChanged;

    // ---------------------------------------------------------------- lines (단일 출처: 빌더가 대사 테이블에 같은 키로 기록)
    public struct Line
    {
        public string key, speaker, text;
        public Line(string k, string s, string t) { key = k; speaker = s; text = t; }
    }

    const string EGO = "EGO";
    const string CHUON = "추온"; // 추온

    public static readonly Line[] DefaultLines =
    {
        new Line("tut.inf.move.intro.0", EGO, "레지스탕스가 짠 내일 침투 경로야.\n거리에서 본사 로비 문 앞까지."),
        new Line("tut.inf.move.intro.1", EGO, "{move}로 가로등 아래 표식까지.\n내일은 이 길을 진짜로 걷는 거야."),
        new Line("tut.inf.move.success.0", EGO, "다리는 멀쩡하네. 다음."),
        new Line("tut.inf.look.intro.0", EGO, "{look}로 시야를 돌려 봐.\n거리 끝, 붉은 엠블럼."),
        new Line("tut.inf.look.remind.0", EGO, "고개 들어, 추온.\n거리 끝에서 제일 큰 빌딩."),
        new Line("tut.inf.look.success.0", CHUON, "츠카사 본사…"),
        new Line("tut.inf.look.success.1", EGO, "내일의 목적지. 오늘은\n로비 문 앞까지만 가 보자."),
        new Line("tut.inf.dodge.intro.0", EGO, "{dodge}로 회피. 두 번 굴러 봐.\n구르는 순간엔 아무것도 안 닿아."),
        new Line("tut.inf.dodge.success.0", EGO, "좋아. 그 무적 프레임이\n오늘의 첫 번째 열쇠야."),
        new Line("tut.inf.scan.intro.0", EGO, "앞에 보안 스캐너 빔. 걸어가면 걸려.\n빔 바로 앞에서 굴러서 통과해."),
        new Line("tut.inf.scan.remind.0", EGO, "걸어가면 감지돼.\n빔 바로 앞에서 {dodge}."),
        new Line("tut.inf.scan.success.0", EGO, "스캐너 통과. 기록엔\n고양이 한 마리 정도로 남았을걸."),
        new Line("tut.inf.lockon.intro.0", EGO, "검문소 경비 안드로이드. 등 돌렸지?\n{lockon}으로 락온해."),
        new Line("tut.inf.lockon.remind.0", EGO, "놈을 화면에 두고 {lockon}.\n시선이 딱 붙을 거야."),
        new Line("tut.inf.lockon.success.0", EGO, "고정 완료. 이제 벨 일만 남았어."),
        new Line("tut.inf.combo.intro.0", EGO, "{light} 약공, {heavy} 강공.\n약공 세 번, 강공 한 번 넣고 정리해."),
        new Line("tut.inf.combo.remind.0", EGO, "약공은 {light}, 강공은 {heavy}.\n끊지 말고 이어."),
        new Line("tut.inf.combo.success.0", EGO, "깔끔해. 시뮬레이션 경비라도\n살짝 미안해질 정도네."),
        new Line("tut.inf.guard.intro.0", EGO, "부스에서 하나 더 나온다.\n{guard}를 누르고 있으면 막아."),
        new Line("tut.inf.guard.remind.0", EGO, "놈이 휘두를 때 {guard}.\n일단 버티는 법부터."),
        new Line("tut.inf.guard.success.0", EGO, "좋아, 방어 리듬 잡혔어."),
        new Line("tut.inf.parry.intro.0", EGO, "세 번째 공격 전에 헬멧이 금색으로\n번쩍여. 그게 패링 신호야."),
        new Line("tut.inf.parry.intro.1", EGO, "맞기 직전에 {guard}.\n막는 게 아니라 끊는 거야."),
        new Line("tut.inf.parry.remind.0", EGO, "금색 섬광이 보이면\n한 박자 기다렸다가 {guard}."),
        new Line("tut.inf.parry.success.0", EGO, "그거야! 튕겨낸 순간이\n제일 크게 벨 기회야."),
        new Line("tut.inf.clear.intro.0", EGO, "남은 경비 정리하면\n차단기가 열려."),
        new Line("tut.inf.clear.success.0", EGO, "검문소 통과. 내일 진짜 경비는\n이렇게 순순히 안 열어 줄 거야."),
        new Line("tut.inf.to_dock.intro.0", EGO, "정문은 봉쇄야.\n서비스 골목 지나 하역장으로."),
        new Line("tut.inf.to_dock.success.0", CHUON, "하역장이군."),
        new Line("tut.inf.red.intro.0", EGO, "하역장 벽에 터렛. 빨간 빛은\n막을 수도, 패링할 수도 없어."),
        new Line("tut.inf.red.intro.1", EGO, "조준선이 고정되면 옆으로 비켜.\n컨테이너를 엄폐물로 써."),
        new Line("tut.inf.red.remind.0", EGO, "빨강은 받지 마.\n조준선 밖으로 한 걸음."),
        new Line("tut.inf.red.success.0", EGO, "터렛 잠깐 멈췄어.\n빨강 = 피한다. 잊지 마."),
        new Line("tut.inf.heal.intro.0", EGO, "긁혔네. {heal}로 앰플 투여.\n숨 돌릴 틈에 쓰는 거야."),
        new Line("tut.inf.heal.success.0", EGO, "회복 완료. 앰플은 간식 아니야,\n추온. 아껴 먹어."),
        new Line("tut.inf.pd.intro.0", EGO, "플랫폼은 좁아서 비킬 데가 없어.\n이번엔 빨강을 정면으로 넘는다."),
        new Line("tut.inf.pd.intro.1", EGO, "탄이 닿기 직전, 한 박자 늦게\n{dodge}. 그게 퍼펙트 회피야."),
        new Line("tut.inf.pd.remind.0", EGO, "너무 빨라. 조준선이 멈추고\n한 박자 뒤에 굴러."),
        new Line("tut.inf.pd.assist.0", EGO, "판정 보조 켰어. 시뮬레이션이니까\n이번만 봐준다."),
        new Line("tut.inf.pd.success.0", EGO, "그거야. 방금 세상이\n멈춘 것 같았지?"),
        new Line("tut.inf.to_side.intro.0", EGO, "셔터 통로 너머가 측면 출입구.\n로비 바로 앞이야."),
        new Line("tut.inf.takedown.intro.0", EGO, "저 경비, 아직 우릴 못 봤어.\n등 뒤로 조용히 붙어."),
        new Line("tut.inf.takedown.intro.1", EGO, "바로 뒤에서 {light}. 소리 없이 끝내는 거야."),
        new Line("tut.inf.takedown.remind.0", EGO, "정면으로 가면 들켜.\n돌아서 등 뒤로."),
        new Line("tut.inf.takedown.success.0", EGO, "깔끔해. 경보도 안 울렸어."),
        new Line("tut.inf.enforcer.intro.0", EGO, "방패 집행관. 정면 약공은\n다 막혀. 시간 낭비야."),
        new Line("tut.inf.enforcer.intro.1", EGO, "{heavy} 강공으로 방패를 깨거나\n옆으로 돌아. 뒤 터렛 조준선도 조심."),
        new Line("tut.inf.enforcer.remind.0", EGO, "정면 약공은 튕겨.\n강공으로 깨, 추온."),
        new Line("tut.inf.enforcer.success.0", EGO, "방패 붕괴! 지금 끝내자."),
        new Line("tut.inf.ultimate.intro.0", EGO, "시뮬레이션이 페이즈 게이지를\n채워 뒀어. {ult}로 궁극기."),
        new Line("tut.inf.ultimate.success.0", EGO, "봤지? 우리 둘이 제대로\n맞물리면 이 정도야."),
        new Line("tut.inf.exit.intro.0", EGO, "훈련 종료. 로비 문 패널에서 {interact}.\n이제 현실로 돌아간다."),
        new Line("tut.inf.exit.success.0", CHUON, "준비는 끝났어. 진짜로 간다."),
        new Line("tut.inf.resync.0", EGO, "동기화 끊겼어. 체크포인트로\n되감을게. 다시 가 보자, 추온."),
        new Line("tut.inf.respawn.0", EGO, "레슨 다시. 시뮬레이션이\n하나 더 띄워 줄게."),
    };

    // ---------------------------------------------------------------- lifecycle
    void Awake()
    {
        Active = this;
        ExecutionDirector.MobFilter = AllowMobExecution; // 반복 16: 레슨 경비를 처형·암살로 건너뛰지 않게
        _flow = GetComponent<TutorialFlowController>();
        _player = GameObject.FindGameObjectWithTag("Player");
        _camera = Camera.main;
        if (_player == null || _camera == null)
        {
            Debug.LogWarning("[Infiltration] Player or camera missing — tutorial disabled.", this);
            enabled = false;
            return;
        }

        _playerHealth = _player.GetComponent<PlayerHealth>();
        TutorialManager legacy = FindFirstObjectByType<TutorialManager>();
        if (legacy != null)
        {
            TutorialManager.Instance = null;
            legacy.enabled = false;
            if (legacy.comboGuidePanel != null)
                legacy.comboGuidePanel.SetActive(false);
        }

        _hint = gameObject.AddComponent<TutorialHintUIBridge>();
        _hint.BindLegacy(legacy);
        _ego = gameObject.AddComponent<EGOGuideController>();
        _ego.ConfigureRuntime(_hint);

        _lockOn = _player.GetComponent<PlayerLockOn>();
        if (_lockOn == null)
            _lockOn = _player.AddComponent<PlayerLockOn>();
        ExistingLockOnAdapter adapter = _player.GetComponent<ExistingLockOnAdapter>();
        if (adapter == null)
            adapter = _player.AddComponent<ExistingLockOnAdapter>();
        adapter.ConfigureRuntime(_lockOn);

        _bridge = gameObject.AddComponent<TutorialPlayerRuntimeBridge>();
        _bridge.ConfigureRuntime(_player, _camera, adapter);

        _canvas = ResolveCanvas(legacy);
        gameObject.AddComponent<TutorialPresentationController>().ConfigureRuntime(_flow, _bridge, _canvas);
        if (ExhibitionPrototypePresentationPolicy.RuntimeTutorialObjectiveControllerEnabled)
            gameObject.AddComponent<TutorialObjectivePanelController>().ConfigureRuntime(_flow, _hint);

        BuildGuidance();

        TutorialZoneTrigger[] zones = { zoneMove, zoneScanPassed, zoneDockYard, zoneSideYard, zoneExit };
        foreach (TutorialZoneTrigger z in zones)
            if (z != null) z.ConfigureRuntime(_player.transform, "Player", true);

        _flow.ConfigureRuntime(_bridge, _ego, _hint, BuildSteps(), Array.Empty<TrainingDummyController>());
        _flow.StepStarted += HandleStepStarted;
        _flow.StepCompleted += HandleStepCompleted;

        if (exitInteractable != null)
        {
            gameObject.AddComponent<TutorialExitInteractableBridge>().ConfigureRuntime(_flow, _ego, _hint, zoneExit, exitInteractable);
            exitInteractable.BeforeSceneLoad += HandleExit;
            exitInteractable.BeforeSceneLoadRoutine = ExitCinematicRoutine;
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged += HandleHealthChanged;
            _playerHealth.OnDied += HandlePlayerDied;
        }
        _bridge.DodgeStarted += HandleDodgeStartedForAssist;
        _bridge.UltimateStarted += HandleUltimateStarted;
        InputGlyphs.Changed += HandleGlyphsChanged;
    }

    void HandleUltimateStarted() => _ultFired = true;

    void Start()
    {
        if (!enabled)
            return;
        PlacePlayer(playerStart);
        int resumeStep = s_resumeStep;
        bool bootCine = resumeStep <= 0 && CinematicDirector.Has(BootCinematicId);
        // 반복 15: 부팅 연출이 있으면 밴드 유지 시간을 줄이고(0.8 → 0.15 s) 연출을 바로 뒤에 시작한다 —
        // 예전엔 연출 첫 ~3 s(도시 크레인 샷)가 오버레이·씬 로드 뒤에 가려졌다. VFX/JIT 예열은 연출이 끝난 뒤로 미룬다(첫 프레임 히치).
        VrTransitionOverlay.Play("TSUKASA HQ  //  INFILTRATION ROUTE", "VR SIMULATION  //  가상현실 부팅", bootCine ? BootOverlayHold : 0.8f);
        // 반복 14A: VR 부팅 동안 Level_Facility 와 같은 예열(VFX 풀 + 궁극기/퍼펙트 회피 코드 JIT + 타임라인 그래프)
        // → 튜토리얼 첫 궁극기·첫 퍼펙트 회피 슬로우 히치(98~110 ms) 제거
        if (!bootCine)
            CombatVfxPrewarmer.EnsureAndPrewarm(gameObject);
        PrewarmDialogueGlyphs();
        int resume = s_resumeStep;
        s_resumeStep = -1;
        if (resume > 0)
        {
            PlacePlayer(CheckpointFor(StepIdAt(resume)));
            _flow.SkipToStep(resume);
        }
        else
        {
            // 반복 14: VR 부팅 연출(6.5 s, 기존 밴드 와이프와 동시). 미설치면 기존 그대로.
            if (bootCine)
                StartCoroutine(CoPlayBootCinematic());
            _flow.StartFlow();
        }
    }

    // 반복 14A: 튜토리얼 대사·목표 문구의 글자를 VR 부팅 동안 TMP 아틀라스에 미리 넣는다.
    // (페이싱 수정으로 성공 대사가 실제로 표시되면서 단계 전환 때 처음 보는 글자 래스터화 + 아틀라스 업로드 26~81 ms 히치가 생김)
    // 4096² 동적 아틀라스는 글자를 추가할 때마다 텍스처 전체 업로드(~60 ms)라, 씬 첫 프레임(로딩 프레임)에 폰트당 한 번에 넣는다.
    void PrewarmDialogueGlyphs()
    {
        var fonts = new List<TMPro.TMP_FontAsset>(4);
        if (_hint != null)
            _hint.CollectGuideFonts(fonts);
        if (fonts.Count == 0)
            return;

        var text = new System.Text.StringBuilder(4096);
        var prefixes = new HashSet<string> { "tut.open" };
        foreach (Line l in DefaultLines)
        {
            text.Append(Glyphs(l.text)).Append(l.speaker);
            int dot = l.key.LastIndexOf('.');
            if (dot > 0) prefixes.Add(l.key.Substring(0, dot));
        }
        foreach (string prefix in prefixes)
            foreach (TutorialGuideLine g in Lines(prefix, EGOGuideMessageType.Briefing))
                text.Append(g.text).Append(g.speaker);
        if (_steps != null)
            foreach (TutorialStepDefinition st in _steps)
                text.Append(st.hintLine1).Append(st.hintLine2);
        text.Append("0123456789/ ·완료");

        var unique = new HashSet<char>();
        var chars = new System.Text.StringBuilder(512);
        foreach (char c in text.ToString())
            if (!char.IsControl(c) && unique.Add(c))
                chars.Append(c);
        string all = chars.ToString();

        float start = Time.realtimeSinceStartup;
        int added = 0;
        foreach (TMPro.TMP_FontAsset font in fonts)
        {
            if (font == null || font.atlasPopulationMode != TMPro.AtlasPopulationMode.Dynamic)
                continue;
            var missing = new System.Text.StringBuilder();
            foreach (char c in all)
                if (!font.HasCharacter(c, false, false)) missing.Append(c);
            if (missing.Length == 0)
                continue;
            font.TryAddCharacters(missing.ToString(), out string notFound);
            added += missing.Length - (notFound != null ? notFound.Length : 0);
            if (!string.IsNullOrEmpty(notFound) && font.fallbackFontAssetTable != null)
                foreach (TMPro.TMP_FontAsset fb in font.fallbackFontAssetTable)
                    if (fb != null && fb.atlasPopulationMode == TMPro.AtlasPopulationMode.Dynamic && !string.IsNullOrEmpty(notFound))
                        fb.TryAddCharacters(notFound, out notFound);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Infiltration] dialogue glyph prewarm: {all.Length} chars, {added} added to {fonts.Count} font(s), {(Time.realtimeSinceStartup - start) * 1000f:F0} ms (load frame)");
#endif
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
        if (ExecutionDirector.MobFilter == (Func<EnemyBrain, ExecutionKind, bool>)AllowMobExecution)
            ExecutionDirector.MobFilter = null;
        if (_flow != null)
        {
            _flow.StepStarted -= HandleStepStarted;
            _flow.StepCompleted -= HandleStepCompleted;
        }
        if (exitInteractable != null)
        {
            exitInteractable.BeforeSceneLoad -= HandleExit;
            exitInteractable.BeforeSceneLoadRoutine = null;
        }
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= HandleHealthChanged;
            _playerHealth.OnDied -= HandlePlayerDied;
        }
        if (_bridge != null)
        {
            _bridge.DodgeStarted -= HandleDodgeStartedForAssist;
            _bridge.UltimateStarted -= HandleUltimateStarted;
        }
        InputGlyphs.Changed -= HandleGlyphsChanged;
        if (_indicator != null)
            Destroy(_indicator.gameObject);
    }

    void Update()
    {
        // 레슨 적 체력 하한: 가르치는 동안엔 먼저 쓰러지지 않게 (레슨 완료 시 해제)
        foreach (EnemyBrain b in _hpFloor)
        {
            if (b == null || b.IsDead) continue;
            EnemyHealth h = b.GetComponent<EnemyHealth>();
            if (h != null && h.CurrentHP > 0 && h.CurrentHP < h.MaxHP * lessonMobHpFloor)
                h.Heal(Mathf.CeilToInt(h.MaxHP * (lessonMobHpFloor + 0.15f)) - h.CurrentHP);
        }

        // 반복 14A: 궁극기 레슨 대상(집행관)은 궁극기를 쓰기 전엔 쓰러지지 않는다 (평타로 먼저 잡혀 재생성되던 문제)
        if (CurrentStepId == "ultimate" && !_ultFired && _enforcer != null && !_enforcer.IsDead)
        {
            EnemyHealth h = _enforcer.GetComponent<EnemyHealth>();
            if (h != null && h.CurrentHP > 0 && h.CurrentHP < h.MaxHP * comboHpFloor)
                h.Heal(Mathf.CeilToInt(h.MaxHP * (comboHpFloor + 0.15f)) - h.CurrentHP);
        }

        // 반복 14A: 콤보 레슨 경비는 약공 3 · 강공 1 이 다 들어갈 때까지 쓰러지지 않고(체력 하한),
        // 맞는 동안엔 반격하지 않는다(연타 중 피격 → 재동기화 방지). 조건을 채우면 하한 해제 → 처치로 완료.
        if (CurrentStepId == "combo" && _android1 != null && !_android1.IsDead && _comboChecker != null)
        {
            int hits = _android1.HitsReceived;
            if (hits != _comboHitsSeen)
            {
                _comboHitsSeen = hits;
                _android1.DelayNextAttack(comboHitAttackDelay);
            }
            if (!_comboChecker.HitsSatisfied)
            {
                EnemyHealth h = _android1.GetComponent<EnemyHealth>();
                if (h != null && h.CurrentHP > 0 && h.CurrentHP < h.MaxHP * comboHpFloor)
                    h.Heal(Mathf.CeilToInt(h.MaxHP * (comboHpFloor + 0.15f)) - h.CurrentHP);
            }
        }

        // 터렛은 플레이어가 사계(射界)에 들어올 때 깨어난다 (멀리서 기습 사격 없음 → 엄폐 배치를 먼저 읽을 시간)
        WakeWhenNear(_wallTurret, 24f);
        WakeWhenNear(_platformTurret, 17f);
        WakeWhenNear(_sideTurret, 20f);

        if (_respawnAt > 0f && Time.time >= _respawnAt)
        {
            _respawnAt = -1f;
            RespawnLessonMob();
        }
        else if (_respawnAt < 0f)
        {
            CheckLessonMobLost();
        }
    }

    void WakeWhenNear(EnemyBrain turret, float range)
    {
        if (turret == null || turret.IsDead || !turret.PerceptionSuppressed || _player == null)
            return;
        if ((turret.transform.position - _player.transform.position).sqrMagnitude <= range * range)
            turret.PerceptionSuppressed = false;
    }

    // ---------------------------------------------------------------- steps
    TutorialStepDefinition[] BuildSteps()
    {
        GameObject host = new GameObject("InfCheckers");
        host.transform.SetParent(transform, false);
        var steps = new List<TutorialStepDefinition>();

        // 검사기는 [DisallowMultipleComponent] 라 단계마다 자식 오브젝트 하나씩
        T Add<T>() where T : TutorialConditionChecker
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(host.transform, false);
            return go.AddComponent<T>();
        }

        var move = Add<TutorialZoneConditionChecker>(); move.ConfigureRuntime(zoneMove);
        var look = Add<TutorialLookAtConditionChecker>(); look.ConfigureRuntime(_camera, lookTarget);
        Remind(look, "look", 5f);
        var dodge = Add<InfDodgeCountChecker>(); dodge.ConfigureRuntime(_bridge, 2);
        var scan = Add<TutorialZoneConditionChecker>(); scan.ConfigureRuntime(zoneScanPassed);
        Remind(scan, "scan", 9f);
        var lockon = Add<TutorialLockOnConditionChecker>(); lockon.ConfigureRuntime(_bridge, null);
        Remind(lockon, "lockon", 7f);
        _comboChecker = Add<InfMobHitChecker>(); _comboChecker.ConfigureRuntime(_bridge, 3, 1, true);
        Remind(_comboChecker, "combo", 10f);
        var guard = Add<TutorialGuardConditionChecker>(); guard.ConfigureRuntime(_bridge, 1);
        Remind(guard, "guard", 8f);
        var parry = Add<TutorialParryConditionChecker>(); parry.ConfigureRuntime(_bridge, 1);
        Remind(parry, "parry", 9f);
        _clearChecker = Add<InfMobsDefeatedChecker>(); _clearChecker.ConfigureRuntime("경비");
        var toDock = Add<TutorialZoneConditionChecker>(); toDock.ConfigureRuntime(zoneDockYard);
        _redChecker = Add<InfRedCueSurviveChecker>(); _redChecker.ConfigureRuntime(_bridge, 2);
        Remind(_redChecker, "red", 9f);
        var heal = Add<TutorialHealConditionChecker>(); heal.ConfigureRuntime(_bridge, 0.42f, 1.2f);
        _pdChecker = Add<TutorialPerfectDodgeConditionChecker>(); _pdChecker.ConfigureRuntime(_bridge, 1);
        Remind(_pdChecker, "pd", 10f);
        var toSide = Add<TutorialZoneConditionChecker>(); toSide.ConfigureRuntime(zoneSideYard);
        _takedownChecker = Add<InfMobsDefeatedChecker>(); _takedownChecker.ConfigureRuntime("암살");
        Remind(_takedownChecker, "takedown", 12f);
        _guardBreakChecker = Add<InfGuardBreakChecker>(); _guardBreakChecker.ConfigureRuntime(1);
        Remind(_guardBreakChecker, "enforcer", 10f);
        var ult = Add<TutorialUltimateConditionChecker>(); ult.ConfigureRuntime(_bridge, null);
        var exit = Add<TutorialZoneConditionChecker>(); exit.ConfigureRuntime(zoneExit);

        steps.Add(Step("move", TutorialStepType.Movement, "이동", "{move} — 가로등 아래 표식까지", move));
        steps.Add(Step("look", TutorialStepType.CameraFocus, "시야", "{look} — 거리 끝 츠카사 엠블럼을 화면 중앙에", look));
        steps.Add(Step("dodge", TutorialStepType.Dodge, "회피", "{dodge} — 두 번 구르기", dodge));
        steps.Add(Step("scan", TutorialStepType.Dodge, "스캐너 통과", "빔 바로 앞에서 {dodge}로 굴러서 통과", scan));
        steps.Add(Step("lockon", TutorialStepType.LockOn, "락온", "{lockon} — 경비 안드로이드 고정", lockon));
        steps.Add(Step("combo", TutorialStepType.BasicAttack, "약공 · 강공", "{light} 약공 3 · {heavy} 강공 1 적중 후 처치", _comboChecker));
        steps.Add(Step("guard", TutorialStepType.Guard, "방어", "{guard} 누르고 있기 — 일반 공격 막기", guard));
        steps.Add(Step("parry", TutorialStepType.Parry, "패링", "금색 섬광 뒤 맞기 직전에 {guard}", parry));
        steps.Add(Step("clear", TutorialStepType.Combo, "검문소 돌파", "남은 경비 처치 → 차단기 개방", _clearChecker));
        steps.Add(Step("to_dock", TutorialStepType.Movement, "하역장", "서비스 골목을 지나 하역장으로", toDock));
        steps.Add(Step("red", TutorialStepType.Dodge, "빨강 = 회피 전용", "조준선이 고정되면 옆으로 · 컨테이너 엄폐", _redChecker));
        steps.Add(Step("heal", TutorialStepType.Heal, "회복", "{heal} — 앰플 투여", heal));
        steps.Add(Step("pd", TutorialStepType.PerfectDodge, "퍼펙트 회피", "탄이 닿기 직전 {dodge}", _pdChecker));
        steps.Add(Step("to_side", TutorialStepType.Movement, "측면 출입구", "셔터 통로를 지나 본사 측면으로", toSide));
        steps.Add(Step("takedown", TutorialStepType.Combo, "잠입 암살", "경계 전 경비 등 뒤에서 {light}", _takedownChecker));
        steps.Add(Step("enforcer", TutorialStepType.Combo, "방패 집행관", "{heavy} 강공 또는 측면 공격으로 가드 붕괴", _guardBreakChecker));
        steps.Add(Step("ultimate", TutorialStepType.Ultimate, "궁극기", "{ult} — 위상단절검", ult));
        steps.Add(Step("exit", TutorialStepType.Exit, "시뮬레이션 종료", "로비 문 패널에서 {interact}", exit));

        // 오프닝: 기존 VR 부팅 대사(tut.open.*) + 침투 경로 소개
        var opening = new List<TutorialGuideLine>(Lines("tut.open", EGOGuideMessageType.Briefing));
        opening.AddRange(steps[0].introLines);
        steps[0].introLines = opening.ToArray();
        steps[2].autoAdvanceDelay = 0.2f;
        // 반복 14A 페이싱: 나머지 단계는 완료 시 남은 소개 대사를 끊고(최소 1.1 s 표시) 바로 진행.
        // 오프닝 브리핑(VR 부팅)과 본사 리빌 대사만 끝까지 듣게 짧게 기다린다.
        steps[0].holdAdvanceForLines = 7f;
        steps[1].holdAdvanceForLines = 3.5f;
        _steps = steps.ToArray();
        return _steps;
    }

    readonly Dictionary<string, string> _rawHints = new Dictionary<string, string>();
    readonly List<(TutorialConditionChecker checker, string id, float delay)> _reminders = new List<(TutorialConditionChecker, string, float)>();
    TutorialStepDefinition[] _steps;

    TutorialStepDefinition Step(string id, TutorialStepType type, string title, string body, TutorialConditionChecker checker)
    {
        _rawHints[id] = body;
        return new TutorialStepDefinition
        {
            stepId = id,
            stepType = type,
            hintLine1 = title,
            hintLine2 = Glyphs(body),
            introLines = Lines("tut.inf." + id + ".intro", EGOGuideMessageType.Briefing),
            successLines = Lines("tut.inf." + id + ".success", EGOGuideMessageType.Success),
            autoAdvance = true,
            autoAdvanceDelay = 0.6f,
            requiredCheckers = new[] { checker },
            activateOnStart = Array.Empty<GameObject>(),
            deactivateOnStart = Array.Empty<GameObject>(),
            activateOnComplete = Array.Empty<GameObject>(),
            deactivateOnComplete = Array.Empty<GameObject>()
        };
    }

    void Remind(TutorialConditionChecker checker, string id, float delay)
    {
        if (!_reminders.Exists(r => r.checker == checker))
            _reminders.Add((checker, id, delay));
        TutorialGuideLine[] lines = Lines("tut.inf." + id + ".remind", EGOGuideMessageType.FailureAssist);
        if (lines.Length > 0)
            checker.ConfigureRuntimeReminders(lines, delay, true, delay + 6f);
    }

    /// <summary>대사 테이블 우선, 없으면 DefaultLines.</summary>
    public static TutorialGuideLine[] Lines(string prefix, EGOGuideMessageType type)
    {
        var result = new List<TutorialGuideLine>();
        List<DialogueTable.Entry> entries = DialogueTable.GetSequence(prefix);
        if (entries.Count > 0)
        {
            foreach (DialogueTable.Entry e in entries)
                result.Add(new TutorialGuideLine
                {
                    messageType = type,
                    speaker = string.IsNullOrEmpty(e.speaker) ? EGO : e.speaker,
                    text = Glyphs(e.text),
                    duration = e.duration > 0f ? e.duration : DialogueTable.SuggestedDuration(e.text)
                });
            return result.ToArray();
        }

        for (int i = 0; ; i++)
        {
            string key = prefix + "." + i;
            int found = Array.FindIndex(DefaultLines, l => l.key == key);
            if (found < 0) break;
            Line l = DefaultLines[found];
            result.Add(new TutorialGuideLine { messageType = type, speaker = l.speaker, text = Glyphs(l.text), duration = DialogueTable.SuggestedDuration(l.text) });
        }
        return result.ToArray();
    }

    // ---------------------------------------------------------------- 입력 표기 ({토큰} → 현재 장치/리바인드 표기, 키보드 기본값이면 원래 한국어 표기)
    static string Label(ChuonAction a, string keyboardDefault)
        => !ChuonInput.IsGamepadScheme && !ChuonInput.KeyboardOverridden(a) ? keyboardDefault : InputGlyphs.Get(a);

    public static string Glyphs(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            return text;
        return text
            .Replace("{move}", Label(ChuonAction.Move, "WASD"))
            .Replace("{look}", ChuonInput.IsGamepadScheme ? InputGlyphs.LookLabel() : "\ub9c8\uc6b0\uc2a4")
            .Replace("{dodge}", Label(ChuonAction.Dodge, "L-Shift"))
            .Replace("{lockon}", Label(ChuonAction.LockOn, "\ud720 \ud074\ub9ad"))
            .Replace("{light}", Label(ChuonAction.AttackLight, "\uc88c\ud074\ub9ad"))
            .Replace("{heavy}", Label(ChuonAction.AttackHeavy, "\uc6b0\ud074\ub9ad"))
            .Replace("{guard}", Label(ChuonAction.Guard, "E"))
            .Replace("{heal}", Label(ChuonAction.Heal, "Q"))
            .Replace("{ult}", Label(ChuonAction.Ultimate, "R"))
            .Replace("{interact}", Label(ChuonAction.Interact, "F"));
    }

    void HandleGlyphsChanged()
    {
        if (_steps == null)
            return;
        foreach (TutorialStepDefinition st in _steps)
        {
            if (st == null) continue;
            if (_rawHints.TryGetValue(st.stepId, out string raw)) st.hintLine2 = Glyphs(raw);
            TutorialGuideLine[] intro = Lines("tut.inf." + st.stepId + ".intro", EGOGuideMessageType.Briefing);
            if (intro.Length > 0 && st != _steps[0]) st.introLines = intro;
        }
        foreach (var r in _reminders)
            Remind(r.checker, r.id, r.delay);
        TutorialStepDefinition cur = _flow != null ? _flow.CurrentStep : null;
        if (cur != null)
            _hint.ShowHint(cur.hintLine1, string.IsNullOrEmpty(_flow.CurrentStepProgressText) || _flow.CurrentStepProgressText == "\uc644\ub8cc" ? cur.hintLine2 : cur.hintLine2 + "\n" + _flow.CurrentStepProgressText);
    }

    void Say(string prefix, bool urgent = true)
    {
        TutorialGuideLine[] lines = Lines(prefix, EGOGuideMessageType.FailureAssist);
        if (lines.Length > 0)
            _ego.QueueGuideLines(lines, false, urgent);
    }

    string StepIdAt(int index)
    {
        TutorialStepDefinition[] all = null;
        FieldInfo f = typeof(TutorialFlowController).GetField("steps", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f != null) all = f.GetValue(_flow) as TutorialStepDefinition[];
        return all != null && index >= 0 && index < all.Length ? all[index].stepId : null;
    }

    // ---------------------------------------------------------------- step hooks
    void HandleStepStarted(TutorialStepDefinition step)
    {
        StepStartedAt = Time.time;
        StepLog.Add($"{Time.realtimeSinceStartup:F1}s start {step.stepId}");
        _respawnAt = -1f;
        GameObject p = _player;
        switch (step.stepId)
        {
            case "move": SetGoal(zoneMove != null ? zoneMove.transform : null, false); break;
            case "look": SetGoal(null, false); SetIndicator(lookTarget, "TSUKASA", new Color(1f, 0.25f, 0.2f)); break;
            case "dodge": SetGoal(null, false); break;
            case "scan":
                if (scanner != null) scanner.Armed = true;
                SetGoal(zoneScanPassed != null ? zoneScanPassed.transform : null, false);
                break;
            case "lockon":
                if (_android1 == null || _android1.IsDead)
                    _android1 = SpawnMob(androidPrefab, androidPatrolSpawn, true, false, androidPatrolPoints);
                if (_android1 != null) _android1.PerceptionSuppressed = true;
                SetGoal(_android1 != null ? _android1.transform : null, true);
                GetChecker<TutorialLockOnConditionChecker>(step)?.ConfigureRuntime(_bridge, _android1 != null ? _android1.transform : null);
                RestartChecker(step);
                break;
            case "combo":
                if (_android1 == null || _android1.IsDead)
                    _android1 = SpawnMob(androidPrefab, androidPatrolSpawn, false, true, androidPatrolPoints);
                _android1.PerceptionSuppressed = false;
                _comboChecker.Observe(_android1);
                SetGoal(_android1.transform, true);
                break;
            case "guard":
                if (_android2 == null || _android2.IsDead)
                    _android2 = SpawnMob(androidPrefab, boothSpawn, false, true, null);
                _hpFloor.Add(_android2);
                SetLessonThrust(_android2, false); // 반복 14A: 가드 레슨 = 막을 수 있는 공격만 (찌르기 연속 피격 → 재동기화 방지)
                SetGoal(_android2.transform, true);
                break;
            case "parry":
                if (_android2 == null || _android2.IsDead)
                    _android2 = SpawnMob(androidPrefab, boothSpawn, false, true, null);
                _hpFloor.Add(_android2);
                SetLessonThrust(_android2, false);
                SetGoal(_android2.transform, true);
                break;
            case "clear":
                SetLessonThrust(_android2, true);
                _hpFloor.Clear();
                foreach (EnemyBrain b in _spawned)
                    if (b != null && !b.IsDead && b is not SentryTurretBrain) _clearChecker.Observe(b);
                SetGoal(gateGoal, false);
                break;
            case "to_dock":
                if (checkpointGate != null) checkpointGate.Open();
                SetGoal(zoneDockYard != null ? zoneDockYard.transform : null, false);
                break;
            case "red":
                if (_wallTurret == null || _wallTurret.IsDead)
                    _wallTurret = SpawnMob(wallTurretPrefab, wallTurretMount, false, false, null);
                _redChecker.Observe(_wallTurret);
                SetGoal(rampGoal, false); // 엄폐를 따라 램프까지 (터렛 사계 통과)
                SetIndicator(_wallTurret != null ? _wallTurret.transform : null, "터렛", new Color(1f, 0.2f, 0.15f));
                break;
            case "heal":
                PowerDown(_wallTurret);
                SetGoal(null, false);
                break;
            case "pd":
                _pdFailures = 0; _pdAssist = false;
                if (_platformTurret == null || _platformTurret.IsDead)
                    _platformTurret = SpawnMob(floorTurretPrefab, platformTurretSpawn, false, false, null);
                if (_platformTurret != null)
                {
                    _platformTurret.CueIssued -= HandlePdCue;
                    _platformTurret.CueIssued += HandlePdCue;
                }
                SetGoal(passageGoal, false);
                break;
            case "to_side":
                PowerDown(_platformTurret);
                if (passageBarrier != null) passageBarrier.Open();
                SetGoal(zoneSideYard != null ? zoneSideYard.transform : null, false);
                break;
            case "takedown":
                // 측면 마당 한가운데, 집행관 쪽(플레이어 반대)을 보고 서 있는 경계 전 경비
                if (_takedownTarget == null || _takedownTarget.IsDead)
                    _takedownTarget = SpawnMob(androidPrefab, ResolveTakedownSpawn(), true, false, null);
                if (_takedownTarget != null)
                {
                    _takedownTarget.PerceptionSuppressed = true;
                    _takedownChecker.Observe(_takedownTarget);
                    SetGoal(_takedownTarget.transform, true);
                }
                ExecutionDirector.Ensure();
                break;
            case "enforcer":
                if (_enforcer == null || _enforcer.IsDead)
                    _enforcer = SpawnMob(enforcerPrefab, enforcerSpawn, false, true, null);
                if (_sideTurret == null || _sideTurret.IsDead)
                    _sideTurret = SpawnMob(floorTurretPrefab, sideTurretSpawn, false, false, null);
                _hpFloor.Add(_enforcer);
                if (_enforcer is ShieldEnforcerBrain se) _guardBreakChecker.Observe(se);
                SetGoal(_enforcer.transform, true);
                break;
            case "ultimate":
                _hpFloor.Clear();
                PowerDown(_sideTurret);
                if (_enforcer == null || _enforcer.IsDead)
                    _enforcer = SpawnMob(enforcerPrefab, enforcerSpawn, false, true, null);
                PrepareUltimateTarget(_enforcer);
                SetGoal(_enforcer.transform, true);
                break;
            case "exit":
                SetGoal(exitInteractable != null ? exitInteractable.transform : (zoneExit != null ? zoneExit.transform : null), false);
                break;
        }
        StepChanged?.Invoke(step);
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        StepLog.Add($"{Time.realtimeSinceStartup:F1}s done  {step.stepId} ({Time.time - StepStartedAt:F1}s)");
        switch (step.stepId)
        {
            case "scan":
                if (scanner != null) scanner.Armed = false;
                break;
            case "parry":
            case "guard":
                break;
            case "clear":
                if (checkpointGate != null) checkpointGate.Open();
                break;
            case "red":
                PowerDown(_wallTurret);
                break;
            case "pd":
                PowerDown(_platformTurret);
                if (passageBarrier != null) passageBarrier.Open();
                break;
            case "enforcer":
                PowerDown(_sideTurret);
                break;
            case "ultimate":
                if (_enforcer != null && !_enforcer.IsDead)
                {
                    EnemyHealth h = _enforcer.GetComponent<EnemyHealth>();
                    if (h != null) h.ApplyDamage(h.CurrentHP + 1);
                }
                break;
        }
    }

    T GetChecker<T>(TutorialStepDefinition step) where T : TutorialConditionChecker
    {
        if (step?.requiredCheckers == null) return null;
        foreach (TutorialConditionChecker c in step.requiredCheckers)
            if (c is T t) return t;
        return null;
    }

    void RestartChecker(TutorialStepDefinition step)
    {
        // 대상이 단계 시작 뒤에 생기는 검사기(락온)는 대상을 넣고 다시 시작
        foreach (TutorialConditionChecker c in step.requiredCheckers)
            if (c != null && c.IsRunning) c.BeginChecking(step);
    }

    // ---------------------------------------------------------------- lesson mobs
    static readonly FieldInfo PatrolField = typeof(EnemyBrain).GetField("patrolPoints", BindingFlags.Instance | BindingFlags.NonPublic);

    EnemyBrain SpawnMob(GameObject prefab, Transform at, bool suppressed, bool alert, Transform[] patrol)
    {
        if (prefab == null || at == null)
            return null;
        GameObject go = Instantiate(prefab, at.position, at.rotation);
        go.name = prefab.name + "_Sim";
        EnemyBrain brain = go.GetComponent<EnemyBrain>();
        if (brain == null)
            return null;
        if (patrol != null && patrol.Length > 0 && PatrolField != null)
            PatrolField.SetValue(brain, patrol);
        brain.PerceptionSuppressed = suppressed || brain is SentryTurretBrain; // 터렛: WakeWhenNear
        // 레슨 터렛(빨강·퍼펙트 회피)은 훈련 장비: 부수는 대신 피하는 법을 배우게 파괴 불가 (레슨이 끝나면 정지)
        if (prefab == wallTurretPrefab || (prefab == floorTurretPrefab && at == platformTurretSpawn))
        {
            EnemyHealth h = go.GetComponent<EnemyHealth>();
            if (h != null) h.isInvincible = true;
        }
        _spawned.Add(brain);
        SpawnFx(at.position);
        if (alert)
            StartCoroutine(CoAlertNextFrame(brain));
        return brain;
    }

    IEnumerator CoAlertNextFrame(EnemyBrain brain)
    {
        yield return null;
        yield return null;
        if (brain != null && brain.isActiveAndEnabled && !brain.IsDead && _player != null)
            brain.ReceiveAlert(_player.transform.position);
    }

    // 반복 16: 암살 레슨 경비 위치 = 측면 마당 진입점과 집행관 위치 사이 40 %, 집행관 쪽을 본다 (플레이어에게 등을 보임)
    Transform ResolveTakedownSpawn()
    {
        if (_takedownSpawn != null)
            return _takedownSpawn;
        Vector3 from = zoneSideYard != null ? zoneSideYard.transform.position : (_player != null ? _player.transform.position : transform.position);
        Vector3 to = enforcerSpawn != null ? enforcerSpawn.position : from + Vector3.forward * 8f;
        Vector3 pos = Vector3.Lerp(from, to, 0.4f);
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            pos = hit.position;
        else
            pos.y = from.y;
        Vector3 face = to - from;
        face.y = 0f;
        _takedownSpawn = new GameObject("InfTakedownSpawn").transform;
        _takedownSpawn.SetParent(transform, true);
        _takedownSpawn.SetPositionAndRotation(pos, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized, Vector3.up) : Quaternion.identity);
        return _takedownSpawn;
    }

    public EnemyBrain TakedownTarget => _takedownTarget;

    // 암살은 '잠입 암살' 단계의 경비에게만, 경직 처형은 HP 하한이 걸린 레슨 경비(방어·패링·집행관)와 초반 레슨 단계에서는 막는다.
    bool AllowMobExecution(EnemyBrain brain, ExecutionKind kind)
    {
        string id = CurrentStepId;
        if (kind == ExecutionKind.Takedown)
            return id == "takedown" && brain == _takedownTarget;
        if (_hpFloor.Contains(brain))
            return false;
        return id != "lockon" && id != "combo" && id != "guard" && id != "parry";
    }

    static void SetLessonThrust(EnemyBrain b, bool enabled)
    {
        if (b is SecurityAndroidBrain android)
            android.ThrustDisabled = !enabled;
    }

    static void PowerDown(EnemyBrain b)
    {
        if (b == null || b.IsDead) return;
        if (b is SentryTurretBrain t && t.isActiveAndEnabled) t.PowerDown();
        else b.gameObject.SetActive(false);
    }

    // 가르칠 대상이 레슨 완료 전에 쓰러지면 재생성 (= 레슨 재도전)
    void CheckLessonMobLost()
    {
        string id = CurrentStepId;
        bool lost = id switch
        {
            "combo" => _android1 == null || (_android1.IsDead && !_comboChecker.HitsSatisfied),
            "guard" or "parry" => _android2 == null || _android2.IsDead,
            "red" => _wallTurret == null || _wallTurret.IsDead,
            "pd" => _platformTurret == null || _platformTurret.IsDead,
            "enforcer" => _enforcer == null || _enforcer.IsDead,
            "ultimate" => (_enforcer == null || _enforcer.IsDead) && !_ultFired && !UltimateRunning(),
            _ => false
        };
        if (lost && !_resyncing)
            _respawnAt = Time.time + 1.6f;
    }

    bool UltimateRunning()
    {
        PlayerUltimateController u = _player != null ? _player.GetComponent<PlayerUltimateController>() : null;
        return u != null && u.IsCinematic;
    }

    void RespawnLessonMob()
    {
        string id = CurrentStepId;
        if (id == null) return;
        LessonRespawns++;
        Say("tut.inf.respawn");
        switch (id)
        {
            case "combo":
                _android1 = SpawnMob(androidPrefab, androidPatrolSpawn, false, true, null);
                _comboChecker.Observe(_android1);
                SetGoal(_android1.transform, true);
                break;
            case "guard":
            case "parry":
                _android2 = SpawnMob(androidPrefab, boothSpawn, false, true, null);
                _hpFloor.Add(_android2);
                SetLessonThrust(_android2, false);
                SetGoal(_android2.transform, true);
                break;
            case "red":
                _wallTurret = SpawnMob(wallTurretPrefab, wallTurretMount, false, false, null);
                _redChecker.Observe(_wallTurret);
                break;
            case "pd":
                _platformTurret = SpawnMob(floorTurretPrefab, platformTurretSpawn, false, false, null);
                if (_platformTurret != null) _platformTurret.CueIssued += HandlePdCue;
                break;
            case "enforcer":
            case "ultimate":
                _enforcer = SpawnMob(enforcerPrefab, enforcerSpawn, false, true, null);
                if (id == "enforcer") { _hpFloor.Add(_enforcer); if (_enforcer is ShieldEnforcerBrain se) _guardBreakChecker.Observe(se); }
                else PrepareUltimateTarget(_enforcer);
                SetGoal(_enforcer.transform, true);
                break;
        }
    }

    void ClearLessonMobs()
    {
        foreach (EnemyBrain b in _spawned)
            if (b != null) Destroy(b.gameObject);
        _spawned.Clear();
        _hpFloor.Clear();
        _android1 = _android2 = _wallTurret = _platformTurret = _enforcer = _sideTurret = null;
    }

    // ---------------------------------------------------------------- perfect dodge assist
    int _pdDamageAtCue;
    float _pdResolveAt = -1f;
    float _pdHitAt = -1f;

    void HandlePdCue(EnemyBrain b, EnemyCueType type, float lead)
    {
        if (type != EnemyCueType.DodgeOnly || CurrentStepId != "pd")
            return;
        _pdDamageAtCue = _bridge.PlayerDamageCount;
        _pdHitAt = Time.time + lead;
        _pdResolveAt = Time.time + lead + 0.6f;
        StartCoroutine(CoResolvePd(_bridge.PerfectDodgeCount));
    }

    IEnumerator CoResolvePd(int perfectBefore)
    {
        while (Time.time < _pdResolveAt)
            yield return null;
        if (CurrentStepId != "pd" || _bridge.PerfectDodgeCount > perfectBefore)
            yield break;
        _pdFailures++;
        if (_pdFailures >= 3 && !_pdAssist)
        {
            _pdAssist = true;
            Say("tut.inf.pd.assist");
        }
    }

    // 레슨 판정: 직선 레이저는 옆으로 구르면 탄이 아예 안 닿아 실제 퍼펙트 회피가 성립하지 않는다.
    // 그래서 이 레슨은 "타이밍"을 가르친다 — 판정 0.30 s 전 ~ 0.05 s 후에 회피를 시작하면 퍼펙트 회피로 인정
    // (실패 3회 뒤 판정 보조: 0.45 s 전 ~ 0.10 s 후). 인정 시 실제 퍼펙트 회피 연출(슬로우·게이지)을 그대로 재생.
    void HandleDodgeStartedForAssist()
    {
        if (CurrentStepId != "pd" || _platformTurret == null)
            return;
        float hitAt = _pdHitAt;
        float early = _pdAssist ? 0.45f : 0.30f, late = _pdAssist ? 0.10f : 0.05f;
        if (hitAt < 0f || hitAt - Time.time > early || Time.time - hitAt > late)
            return;
        StartCoroutine(CoGrantPerfectDodge(hitAt, _bridge.PerfectDodgeCount));
    }

    IEnumerator CoGrantPerfectDodge(float hitAt, int perfectBefore)
    {
        while (Time.time < hitAt)
            yield return null;
        if (CurrentStepId != "pd" || _bridge.PerfectDodgeCount > perfectBefore || _platformTurret == null)
            yield break; // 실제 퍼펙트 회피가 이미 성립
        PerfectDodgeController pd = _player.GetComponent<PerfectDodgeController>();
        if (pd == null)
            yield break;
        pd.PerfectDodgeWindow_Open(0.12f);
        if (pd.ResolvePerfectDodge(_player.transform.position + Vector3.up * 1.1f, _platformTurret.transform))
            PerfectDodgeAssists++; // (ResolvePerfectDodge 가 OnPerfectDodge 를 발행 → 검사기 완료)
    }

    // ---------------------------------------------------------------- ultimate
    void PrepareUltimateTarget(EnemyBrain target)
    {
        if (target == null) return;
        if (target.GetComponent<InfMobUltimateTarget>() == null)
            target.gameObject.AddComponent<InfMobUltimateTarget>();
        _hpFloor.Remove(target);
        if (!_ultimateConfigured)
        {
            _ultimateConfigured = true;
            TutorialRuntimeBootstrap.ConfigureTutorialModernUltimate(_player, target.gameObject, _camera);
        }
        if (_lockOn != null)
            _lockOn.LockTo(target.transform);
    }

    // ---------------------------------------------------------------- soft failure (VR 재동기화)
    void HandleHealthChanged(int current, int max)
    {
        if (_resyncing || current <= 0 || max <= 0 || CurrentStepId == "heal")
            return;
        if (current <= max * resyncHealthRatio)
            StartCoroutine(CoResync());
    }

    void HandlePlayerDied()
    {
        // 안전망을 넘는 한 방으로 쓰러졌을 때: 같은 단계부터 씬을 다시 연다 (게임 오버 없음)
        s_resumeStep = Mathf.Max(0, CurrentStepIndex);
        ResyncCount++;
        VrTransitionOverlay.Play("SIMULATION RESYNC", "동기화 재설정  //  체크포인트 복귀", 0.5f);
        StartCoroutine(CoReload());
    }

    IEnumerator CoReload()
    {
        yield return new WaitForSecondsRealtime(0.6f);
        GameTimeScale.ReleaseAll();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    IEnumerator CoResync()
    {
        _resyncing = true;
        ResyncCount++;
        StepLog.Add($"{Time.realtimeSinceStartup:F1}s RESYNC at {CurrentStepId}");
        VrTransitionOverlay.Play("SIMULATION RESYNC", "동기화 재설정  //  체크포인트 복귀", 0.35f);
        yield return new WaitForSecondsRealtime(0.3f);
        if (_lockOn != null) _lockOn.Unlock();
        string id = CurrentStepId;
        bool keepMobs = id == "move" || id == "look" || id == "dodge" || id == "scan" || id == "to_dock" || id == "to_side" || id == "exit";
        if (!keepMobs)
        {
            ClearLessonMobs();
            if (id == "clear" || id == "parry")
            {
                // 검문소: 두 번째 경비만 다시 (첫 경비는 이미 가르쳤다)
            }
        }
        PlacePlayer(CheckpointFor(id));
        if (_playerHealth != null)
        {
            _playerHealth.Heal(_playerHealth.MaxHP);
            _playerHealth.SetInvincibleRealtime(2.5f);
        }
        PlayerConsumables c = _player.GetComponent<PlayerConsumables>();
        if (c != null) c.Refill();
        Say("tut.inf.resync");
        if (!keepMobs)
            _flow.RestartCurrentStep();
        yield return new WaitForSecondsRealtime(0.5f);
        _resyncing = false;
    }

    Transform CheckpointFor(string stepId)
    {
        switch (stepId)
        {
            case "move": case "look": case "dodge": case "scan": case null: return cpStreet;
            case "lockon": case "combo": case "guard": case "parry": case "clear": return cpPlaza;
            case "to_dock": case "red": case "heal": return cpDock;
            case "pd": return cpPlatform;
            default: return cpSide;
        }
    }

    void PlacePlayer(Transform at)
    {
        if (at == null || _player == null)
            return;
        CharacterController cc = _player.GetComponent<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (was) cc.enabled = false;
        _player.transform.SetPositionAndRotation(at.position + Vector3.up * 0.05f, Quaternion.Euler(0f, at.eulerAngles.y, 0f));
        if (was) cc.enabled = true;
        _player.GetComponent<PlayerMoveController>()?.ResetVerticalVelocity();
        if (SoulsCameraRig.Active != null)
            SoulsCameraRig.Active.SnapToYaw(at.eulerAngles.y, 8f);
    }

    // ---------------------------------------------------------------- guidance (앰버 = 경로, 빨강 = 적)
    void BuildGuidance()
    {
        GameObject m = new GameObject("InfGoalMarker");
        _marker = m.AddComponent<TutorialWorldMarker>();
        _marker.SetVisible(false);
        if (_canvas != null)
        {
            GameObject ind = new GameObject("InfGoalIndicator", typeof(RectTransform));
            ind.transform.SetParent(_canvas.transform, false);
            _indicator = ind.AddComponent<TutorialScreenTargetIndicator>();
            _indicator.gameObject.SetActive(false);
        }
    }

    static readonly Color Amber = new Color(1.00f, 0.62f, 0.16f, 0.92f);
    static readonly Color Danger = new Color(1.00f, 0.24f, 0.18f, 0.92f);

    void SetGoal(Transform target, bool enemy)
    {
        _goal = target;
        if (_marker != null)
        {
            if (target != null && !enemy)
            {
                _marker.ConfigureRuntime(target, Amber, new Vector3(1.3f, 0.025f, 1.3f), new Vector3(0f, 0.78f, 0f),
                    new Vector3(0.12f, 0.42f, 0.12f), new Vector3(0f, 1.32f, 0f), new Vector3(0.32f, 0.14f, 0.32f));
                _marker.SetVisible(true);
            }
            else
            {
                _marker.SetVisible(false);
            }
        }
        SetIndicator(target, enemy ? "적" : "목표", enemy ? Danger : Amber);
    }

    void SetIndicator(Transform target, string label, Color color)
    {
        if (_indicator == null)
            return;
        if (target == null)
        {
            _indicator.gameObject.SetActive(false);
            return;
        }
        _indicator.gameObject.SetActive(true);
        _indicator.ConfigureRuntime(_camera, _canvas, target, label, color);
    }

    // ---------------------------------------------------------------- misc
    void SpawnFx(Vector3 at)
    {
        if (simSpawnSfx != null)
            OneShotAudio.Play2D(simSpawnSfx, 0.35f);
        if (simFxMaterial == null)
            return;
        GameObject fx = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fx.name = "InfSimSpawnFx";
        Destroy(fx.GetComponent<Collider>());
        fx.transform.position = at + Vector3.up * 1.1f;
        fx.transform.localScale = new Vector3(1.6f, 1.15f, 1.6f);
        Renderer r = fx.GetComponent<Renderer>();
        r.sharedMaterial = simFxMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        StartCoroutine(CoFadeFx(fx, r));
    }

    static IEnumerator CoFadeFx(GameObject fx, Renderer r)
    {
        var mpb = new MaterialPropertyBlock();
        int baseColor = Shader.PropertyToID("_BaseColor");
        int st = Shader.PropertyToID("_BaseMap_ST");
        float t = 0f;
        while (t < 0.9f && fx != null)
        {
            t += Time.deltaTime;
            float k = t / 0.9f;
            fx.transform.localScale = new Vector3(1.6f - k * 0.9f, 1.15f + k * 0.4f, 1.6f - k * 0.9f);
            mpb.SetColor(baseColor, new Color(0.5f, 0.9f, 1f, (1f - k) * 0.8f));
            mpb.SetVector(st, new Vector4(3f, 2f, 0f, -k * 2f));
            r.SetPropertyBlock(mpb);
            yield return null;
        }
        if (fx != null) Destroy(fx);
    }

    void HandleExit(SceneLoadInteractable interactable, object invoker)
    {
        StepLog.Add($"{Time.realtimeSinceStartup:F1}s EXIT -> {interactable.sceneName}");
        // 반복 14: VR 종료 연출이 설치돼 있으면 오버레이는 연출이 끝난 뒤(ExitCinematicRoutine) 이어 받는다.
        if (!CinematicDirector.Has(ExitCinematicId))
            PlayExitOverlay();
    }

    const string BootCinematicId = "tut_vr_boot";
    const float BootOverlayHold = 0.15f;

    System.Collections.IEnumerator CoPlayBootCinematic()
    {
        yield return new WaitForSecondsRealtime(BootOverlayHold);
        if (CinematicDirector.Has(BootCinematicId) && !CinematicDirector.IsPlaying)
            yield return CinematicDirector.PlayRoutine(BootCinematicId);
        // 첫 궁극기·퍼펙트 회피 히치 예열(반복 14A)은 부팅 연출 뒤 — 튜토리얼 첫 교전까지 충분히 남는다
        CombatVfxPrewarmer.EnsureAndPrewarm(gameObject);
    }
    const string ExitCinematicId = "tut_vr_exit";

    static void PlayExitOverlay() =>
        VrTransitionOverlay.Play("SIMULATION TERMINATED", "VR LINK RELEASED  //  현실 복귀", 0.45f);

    // 반복 14: SceneLoadInteractable 가 씬 로드 전에 기다린다. 미설치면 null → 기존처럼 즉시 로드.
    IEnumerator ExitCinematicRoutine(SceneLoadInteractable interactable, object invoker)
    {
        if (!CinematicDirector.Has(ExitCinematicId))
            return null;
        return CoExitCinematic();
    }

    IEnumerator CoExitCinematic()
    {
        yield return CinematicDirector.PlayRoutine(ExitCinematicId);
        PlayExitOverlay();
    }

    static Canvas ResolveCanvas(TutorialManager legacy)
    {
        if (legacy != null && legacy.dialogueGroup != null)
        {
            Canvas c = legacy.dialogueGroup.GetComponentInParent<Canvas>();
            if (c != null) return c;
        }
        GameObject hud = GameObject.Find("Player_HUD");
        if (hud != null && hud.TryGetComponent(out Canvas hc)) return hc;
        return FindFirstObjectByType<Canvas>();
    }

    /// <summary>QA/디버그: 해당 단계부터 (체크포인트로 이동, 지난 게이트 열기).</summary>
    public void QaJumpTo(string stepId)
    {
        int idx = _steps != null ? Array.FindIndex(_steps, s => s.stepId == stepId) : -1;
        if (idx < 0) return;
        int Index(string id) => Array.FindIndex(_steps, s => s.stepId == id);
        ClearLessonMobs();
        if (scanner != null && idx > Index("scan")) scanner.Armed = false;
        if (checkpointGate != null && idx > Index("clear")) checkpointGate.Open();
        if (passageBarrier != null && idx > Index("pd")) passageBarrier.Open();
        StepLog.Add($"{Time.realtimeSinceStartup:F1}s QA jump -> {stepId}");
        PlacePlayer(CheckpointFor(stepId));
        _flow.SkipToStep(idx);
    }

    /// <summary>QA/디버그: 현재 단계를 강제 완료.</summary>
    public void QaForceCompleteStep()
    {
        StepLog.Add($"{Time.realtimeSinceStartup:F1}s FORCED {CurrentStepId}");
        _flow.ForceCompleteCurrentStep();
    }
}
