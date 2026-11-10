using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour
{
    public enum LobbyFlowPhase
    {
        Arrival,
        Combat,
        Route,
        Board,
        Elevator
    }

    public enum LobbyDroneWaveProfile
    {
        Balanced,
        Suppression,
        Crossfire,
        Skirmish,
        MixedPressure
    }

    public static LobbyManager Instance;
    public event System.Action CombatStarted;
    public event System.Action<DroneController> DroneSpawned;
    public event System.Action<int, int> EnemyProgressUpdated;
    public event System.Action CombatCompleted;
    public event System.Action<LobbyFlowPhase> PhaseChanged;
    public event System.Action<string, string> QuestUpdated;

    [Header("Dialogue UI")]
    public CanvasGroup dialogueGroup;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI contentText;
    public Image alertOverlay;

    [Header("Quest UI")]
    public CanvasGroup questPanelGroup;
    public TextMeshProUGUI questTitleText;
    public TextMeshProUGUI questDescriptionText;

    [Header("Combat")]
    public GameObject dronePrefab;
    public Transform[] spawnPoints;
    [Range(0f, 0.3f)] public float droneSpawnInterval = 0.08f;
    [Range(0f, 1f)] public float droneInitialFireStagger = 0.12f;
    [Range(0f, 2f)] public float droneInitialFireBaseDelay = 0.75f;
    [Range(0f, 1.2f)] public float droneAttackTelegraphLeadTime = 0.55f;
    [Range(0.1f, 2f)] public float threatMarkerLeadTime = 0.85f;
    public bool disableLobbyDroneWorldUi = true;
    public bool disableLobbyDroneShadows = true;
    public bool simplifyLobbyDroneVisual = false;
    public bool useLightweightLobbyDroneSimulation = true;
    [Range(0.016f, 0.12f)] public float lightweightLobbyDroneTickInterval = 0.05f;
    [Range(0.1f, 1f)] public float lobbyDroneMoveSpeedMultiplier = 0.45f;
    [Min(0f)] public float lobbyDroneProjectileSpawnForwardOffset = 0.95f;
    [Header("Combat Roles")]
    public LobbyDroneWaveProfile lobbyDroneWaveProfile = LobbyDroneWaveProfile.MixedPressure;
    public bool useRoleAwareSpawnPlan = true;
    [Range(0f, 0.4f)] public float suppressorFireDelayOffset = 0.14f;
    [Range(0f, 0.4f)] public float flankerFireDelayOffset = 0.08f;
    [Range(0f, 0.4f)] public float skirmisherFireLead = 0.1f;
    [Header("Combat Escalation")]
    public bool enableLobbyCombatEscalation = true;
    [Range(0.1f, 0.9f)] public float escalationStageOneKillRatio = 0.34f;
    [Range(0.1f, 0.95f)] public float escalationStageTwoKillRatio = 0.67f;
    [Range(0.55f, 1f)] public float escalationStageOneTelegraphScale = 0.9f;
    [Range(0.4f, 1f)] public float escalationStageTwoTelegraphScale = 0.8f;
    public bool showEscalationQuestCue = true;
    int _totalEnemyCount;
    int _currentDeadEnemy;

    [Header("Elevator")]
    public GameObject elevatorPanel;
    public string nextSceneName = "MainScene";
    public Transform elevatorLiftRoot;
    [Min(0f)] public float elevatorLowerDistance = 4.5f;
    [Min(0.05f)] public float elevatorLowerDuration = 0.85f;

    [Header("Elevator Tension")]
    [TextArea] public string elevatorThreatLine = "\ube44\uc0c1 \uacbd\ub85c \uc804\uc6a9 \uc2b9\uac15\uae30\uc57c. \ub204\uad70\uac00 \uba3c\uc800 \uc774 \ub9c1\ud06c\ub97c \uae68\uc6cc\ub1a8\uc5b4.";
    [TextArea] public string elevatorResponseLine = "\uba3c\uc800 \uae30\ub2e4\ub9ac\uace0 \uc788\ub2e4\ub294 \ub73b\uc774\uaca0\uc9c0.";
    [TextArea] public string elevatorInstructionLine = "\ud328\ub110\uc744 \uc870\uc791\ud574. \ub9c1\ud06c\uac00 \ub04a\uae30\uae30 \uc804\uc5d0 \ub2e4\uc74c \uce35\uc73c\ub85c \ub0b4\ub824\uac04\ub2e4.";
    [TextArea] public string elevatorThreatCueTitle = "\ube44\uc815\uc0c1 \uc2e0\ud638";
    [TextArea] public string elevatorThreatCueDescription = "격납고 층 직통 경로만 열려 있다.";
    [TextArea] public string elevatorPanelCueTitle = "\ud328\ub110 \uc5f0\ub3d9";
    [TextArea] public string elevatorPanelCueDescription = "\ud328\ub110\uc744 \uc870\uc791\ud574 \uce35\uac04 \ub9c1\ud06c\ub97c \uace0\uc815\ud574.";
    [TextArea] public string elevatorPromptText = "F: \ud328\ub110 \uc870\uc791";
    [TextArea] public string elevatorSyncCueTitle = "\ub9c1\ud06c \uace0\uc815";
    [TextArea] public string elevatorSyncCueDescription = "하강 경로 고정. 격납고 층으로 이동한다.";
    [TextArea] public string elevatorSyncLine = "경로 고정. 격납고 층으로 내려간다.";
    [TextArea] public string elevatorSyncPromptText = "\uc5f0\ub3d9 \uc911...";
    [Range(0.1f, 3f)] public float elevatorThreatCueHold = 1.6f;
    [Range(0.1f, 3f)] public float elevatorPanelCueHold = 1.5f;
    [Range(0f, 2f)] public float elevatorPanelCueDelay = 1.05f;
    [Range(0.1f, 2f)] public float elevatorSyncCueHold = 0.85f;
    [Range(0.05f, 1f)] public float elevatorSyncDelay = 0.45f;

    [Header("Elevator Door")]
    public Transform doorLeft;
    public Transform doorRight;
    public float doorOpenHeight = 4f;
    public float doorOpenSpeed = 2f;

    [Header("Guide / Triggers")]
    public GuidePathSystem guideSystem;
    public Transform triggerApproach;
    public Transform triggerBoard;
    public Transform[] pathWaypoints;
    public LobbyPresentationController presentationController;
    [SerializeField] private Color elevatorRouteMarkerColor = new Color(0.18f, 0.85f, 1f, 0.9f);
    [SerializeField] private Color elevatorBoardMarkerColor = new Color(0.28f, 1f, 0.72f, 0.95f);

    [Header("Atmosphere")]
    [TextArea] public string routeSuspicionLine = "프론트 데스크 뒤편 엘리베이터 하나만 살아 있어. 저쪽이야.";
    [Range(0.5f, 4f)] public float routeSuspicionDelay = 2.2f;

    [Header("Typing")]
    [Range(0.01f, 0.2f)] public float textTypingSpeed = 0.05f;
    [SerializeField] bool debugLogs = false;

    [Header("Same-Scene Continuation (Level_Facility)")]
    [Tooltip("When set, the elevator descent fades and raises ElevatorDescentFinished instead of loading nextSceneName.")]
    [SerializeField] bool continueInSameScene = false;
    [SerializeField, Min(0f)] float sameSceneFadeOutDuration = 0.65f;
    [SerializeField, Min(0f)] float sameSceneBlackHold = 0.4f;
    [SerializeField, Min(0f)] float sameSceneFadeInDuration = 0.9f;
    [Tooltip("Teach -> test: spawn the first drone alone, then the rest once it is destroyed.")]
    [SerializeField] bool staggerFirstDroneAlone = false;
    [SerializeField, Min(0f)] float staggeredWaveDelay = 1.1f;
    [Tooltip("Optional: the drone wave starts only after the player crosses this transform's plane (forward = into the combat hall).")]
    [SerializeField] Transform combatStartGate;
    [Tooltip("Pre-instantiate the drone wave (+ projectile pool) and render it once off-screen during the arrival beat, so combat start has no Instantiate / first-render (shader, texture upload) hitch. Off = original behaviour.")]
    [SerializeField] bool prewarmDronesBeforeCombat = false;
    [Tooltip("Optional: upper-floor mob encounter (teach android gates the drone wave, elevator guards gate the elevator door). Null = LobbyMobEncounter.Active or none (original flow).")]
    [SerializeField] LobbyMobEncounter mobEncounter;
    readonly List<GameObject> _prewarmedDrones = new List<GameObject>();

    // Raised when the elevator descent finished in continueInSameScene mode (lower floor should take over).
    public static event System.Action<LobbyManager> ElevatorDescentFinished;
    // Raised once the upper floor hands control to the lower floor (descent or checkpoint skip).
    public static event System.Action<LobbyManager> LowerFloorReached;

    static bool s_skipToLowerFloorOnNextLoad;
    public static void RequestSkipToLowerFloorOnNextLoad() => s_skipToLowerFloorOnNextLoad = true;
    static bool ConsumeSkipToLowerFloor()
    {
        bool value = s_skipToLowerFloorOnNextLoad;
        s_skipToLowerFloorOnNextLoad = false;
        return value;
    }

    bool _handedOffToLowerFloor;
    public bool ContinuesInSameScene => continueInSameScene;
    public bool HasHandedOffToLowerFloor => _handedOffToLowerFloor;

    readonly List<Transform> _activeWaypoints = new List<Transform>();
    bool _isDoorReached;
    bool _isBoarded;
    bool _isBranchSelected;
    bool _isElevatorTransitioning;
    bool _fromTutorialTransition;
    string _currentQuestTitle;
    string _currentQuestDescription;
    Coroutine _transientQuestCueRoutine;
    LobbyCombatCoachController _combatCoachController;
    LobbyObjectivePanelController _objectivePanelController;
    LobbyFlowPhase _currentPhase;
    readonly List<DroneController> _activeLobbyDrones = new List<DroneController>();
    TutorialWorldMarker _approachWorldMarker;
    TutorialWorldMarker _boardWorldMarker;
    int _combatEscalationStage;
    Vector3 _elevatorLiftInitialLocalPosition;
    bool _elevatorLiftInitialLocalPositionValid;

    public bool IsDoorReached => _isDoorReached;
    public Transform CombatStartGate => combatStartGate;
    public bool IsBoarded => _isBoarded;
    public LobbyFlowPhase CurrentPhase => _currentPhase;

    void Awake()
    {
        if (Instance == null)
            Instance = this;

        if (presentationController == null)
            presentationController = GetComponent<LobbyPresentationController>();
        if (presentationController == null)
            presentationController = gameObject.AddComponent<LobbyPresentationController>();
        _combatCoachController = GetComponent<LobbyCombatCoachController>();
        if (_combatCoachController == null)
            _combatCoachController = gameObject.AddComponent<LobbyCombatCoachController>();
        _objectivePanelController = GetComponent<LobbyObjectivePanelController>();
        if (ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
        {
            if (_objectivePanelController == null)
                _objectivePanelController = gameObject.AddComponent<LobbyObjectivePanelController>();
        }
        else if (_objectivePanelController != null)
        {
            _objectivePanelController.enabled = false;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        // 씬 로드로 파괴될 때 경고 플래시(Image 요요 페이드) 등 남은 트윈 정리 (DOTween 파괴 대상 경고 방지).
        // 대상이 이미 먼저 파괴됐을 수 있으므로 Unity null 비교 대신 참조로 Kill 한다.
        if (!ReferenceEquals(alertOverlay, null))
            DOTween.Kill(alertOverlay);
        if (!ReferenceEquals(dialogueGroup, null))
            DOTween.Kill(dialogueGroup);
        if (!ReferenceEquals(questPanelGroup, null))
            DOTween.Kill(questPanelGroup);
    }

    void Start()
    {
        bool skipToLowerFloor = ConsumeSkipToLowerFloor();
        if (skipToLowerFloor && continueInSameScene)
        {
            ApplyLowerFloorCheckpointState();
            return;
        }

        if (dialogueGroup != null)
            dialogueGroup.alpha = 0f;

        if (questPanelGroup != null)
        {
            questPanelGroup.alpha = 0f;
            questPanelGroup.interactable = false;
        }

        if (alertOverlay != null)
            alertOverlay.color = new Color(1f, 0f, 0f, 0f);

        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);

        ResolveElevatorLiftRoot();
        ConfigureLobbyWorldMarkers();
        ConfigurePresentationController();
        ConfigureCombatCoachController();
        ConfigureObjectivePanelController();
        if (!ExhibitionPrototypePresentationPolicy.RuntimeLobbyGuidePathEnabled && guideSystem != null)
            guideSystem.HidePath();
        _fromTutorialTransition = TutorialSceneTransitionState.ConsumeTutorialToLobby();
        SetPhase(LobbyFlowPhase.Arrival);
        StartCoroutine(SequenceArrival());
        if (prewarmDronesBeforeCombat)
            StartCoroutine(CoPrewarmLobbyDrones());
    }

    // 드론 웨이브 사전 생성 + 오프스크린 1회 렌더(셰이더/텍스처 워밍업). 스폰 시에는 SetActive 만 한다.
    IEnumerator CoPrewarmLobbyDrones()
    {
        if (dronePrefab == null || spawnPoints == null || spawnPoints.Length == 0 || spawnPoints[0] == null)
            yield break;

        yield return null; // 씬 첫 프레임은 건드리지 않는다
        PrewarmLobbyProjectilePool();

        // 레벨 위쪽 빈 공간(플레이어 시야 밖)에 모아 두고 전용 카메라로 RT 에 2프레임 렌더.
        Vector3 basePosition = spawnPoints[0].position + Vector3.up * 150f;
        var cameraObject = new GameObject("__DroneWarmupCamera");
        cameraObject.transform.SetPositionAndRotation(basePosition + new Vector3(0f, 0.5f, -5f), Quaternion.identity);
        Camera warmupCamera = cameraObject.AddComponent<Camera>();
        RenderTexture warmupTarget = RenderTexture.GetTemporary(128, 128, 16);
        warmupCamera.targetTexture = warmupTarget;
        warmupCamera.depth = -100f;
        warmupCamera.nearClipPlane = 0.1f;
        warmupCamera.farClipPlane = 40f;
        // 홀과 같은 셰이더 변형(_ADDITIONAL_LIGHTS)을 컴파일하도록 임시 포인트 라이트를 하나 둔다.
        var lightObject = new GameObject("__DroneWarmupLight");
        lightObject.transform.position = basePosition + new Vector3(0f, 2f, -2f);
        Light warmupLight = lightObject.AddComponent<Light>();
        warmupLight.type = LightType.Point;
        warmupLight.range = 10f;
        warmupLight.shadows = LightShadows.None;

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Vector3 position = basePosition + new Vector3((i - (spawnPoints.Length - 1) * 0.5f) * 1.6f, 0f, 0f);
            GameObject drone = Instantiate(dronePrefab, position, Quaternion.identity);
            PrepareLobbyDrone(drone);
            DroneController controller = drone.GetComponent<DroneController>();
            if (controller != null)
                controller.target = null; // target 없음 → 이동/사격 tick 은 즉시 반환
            _prewarmedDrones.Add(drone);
        }

        DroneController template = dronePrefab.GetComponent<DroneController>();
        GameObject warmBullet = template != null && template.projectilePrefab != null
            ? RuntimeObjectPool.Acquire(template.projectilePrefab, basePosition + new Vector3(0f, 2.2f, 1.5f), Quaternion.identity)
            : null;
        if (template != null && template.hitVFX != null)
            TransientVfxPool.Spawn(template.hitVFX, basePosition + new Vector3(-1.2f, 1.6f, 1.5f), Quaternion.identity, null, 0.5f);
        if (template != null && template.deathVFX != null)
            TransientVfxPool.Spawn(template.deathVFX, basePosition + new Vector3(1.2f, 1.6f, 1.5f), Quaternion.identity, null, 0.5f);

        yield return null;
        yield return null;

        if (warmBullet != null)
            RuntimeObjectPool.Release(warmBullet);
        for (int i = 0; i < _prewarmedDrones.Count; i++)
        {
            if (_prewarmedDrones[i] != null)
                _prewarmedDrones[i].SetActive(false);
        }

        warmupCamera.targetTexture = null;
        RenderTexture.ReleaseTemporary(warmupTarget);
        Destroy(cameraObject);
        Destroy(lightObject);
    }

    GameObject TakePrewarmedDrone(Vector3 position, Quaternion rotation)
    {
        while (_prewarmedDrones.Count > 0)
        {
            GameObject drone = _prewarmedDrones[_prewarmedDrones.Count - 1];
            _prewarmedDrones.RemoveAt(_prewarmedDrones.Count - 1);
            if (drone == null)
                continue;

            drone.transform.SetPositionAndRotation(position, rotation);
            Rigidbody body = drone.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
            }

            drone.SetActive(true);
            return drone;
        }

        return null;
    }

    void FlashLobbyAlert(int loops)
    {
        if (alertOverlay == null)
            return;
        alertOverlay.DOKill();
        alertOverlay.color = new Color(1f, 0f, 0f, 0f);
        alertOverlay.DOFade(AccessibilityOptionsRuntime.ScaleFlash(0.3f), 0.5f).SetLoops(loops, LoopType.Yoyo);
    }

    /// <summary>
    /// 반복 15: 로비 경보 연출(lobby_alarm)은 대본상 고정 비트다. 이 값이 true 인 동안 LobbyMobEncounter 는 가르치기 경비를 깨우지 않는다
    /// (예전에는 플레이어가 먼저 게이트를 넘어 교전이 시작되면 IsCombatActive 로 연출이 통째로 생략됐다).
    /// </summary>
    public bool AlarmBeatPending { get; private set; }

    IEnumerator SequenceArrival()
    {
        AlarmBeatPending = true;
        if (_fromTutorialTransition)
        {
            // \ubc18\ubcf5 14: \uc624\ud504\ub2dd \uc5f0\ucd9c\uc774 \uc124\uce58\ub3fc \uc788\uc73c\uba74 \uc7ac\uc0dd\ud558\uace0 lobby.fromtut 2\uc904\uc740 \uc0dd\ub7b5(\ub0b4\uc6a9 \uc911\ubcf5). \uc5c6\uc73c\uba74 \uae30\uc874 2\uc904.
            if (CinematicDirector.Has("opening"))
            {
                yield return CinematicDirector.PlayRoutine("opening");
            }
            else
            {
                // \ub300\uc0ac \uc2a4\ud06c\ub9bd\ud2b8: \ud29c\ud1a0\ub9ac\uc5bc(\uac00\uc0c1\ud604\uc2e4 \ud6c8\ub828) \ub2e4\uc74c \ub0a0, \uce20\uce74\uc0ac \ubcf8\uc0ac \ub85c\ube44 \uce68\ud22c
                yield return StartCoroutine(PlayKeyed("lobby.fromtut.0", "EGO", "\uc2dc\ubbac\ub808\uc774\uc158\uc740 \ub05d\ub0ac\uc5b4, \ucd94\uc628. \uc5ec\uae30\uc11c\ubd80\ud134 \uc2e4\uc804\uc774\uc57c.", 2.1f));
                yield return StartCoroutine(PlayKeyed("lobby.fromtut.1", "ChuOn", "\uc54c\uaca0\uc5b4. \ubc14\ub85c \ud22c\uc785\ud55c\ub2e4.", 1.7f));
            }
        }

        if (_handedOffToLowerFloor)
        {
            AlarmBeatPending = false;
            yield break;
        }

        yield return StartCoroutine(PlayKeyed("lobby.arrive.0", "ChuOn", "\uc5ec\uae30\uac00 \ub188\ub4e4\uc758 \uc2ec\uc7a5\ubd80\u2026", 2.2f));
        yield return StartCoroutine(PlayOptional("lobby.arrive.1", 2f));

        // 하층으로 이미 넘어갔으면(도착 중 엘리베이터 호출 등) 상층 시퀀스를 더 진행하지 않는다.
        if (_handedOffToLowerFloor)
        {
            AlarmBeatPending = false;
            yield break;
        }

        // 반복 15: 다른 연출(스카이덱 내려다보기 등)이 재생 중이면 끝날 때까지 기다린다 — 예전에는 Play 가 거절돼 경보 연출이 조용히 생략됐다(PV 테이크 b/c).
        float waitOtherUntil = Time.realtimeSinceStartup + 20f;
        bool waitedForOther = false;
        while (CinematicDirector.IsPlaying && !_handedOffToLowerFloor && Time.realtimeSinceStartup < waitOtherUntil)
        {
            waitedForOther = true;
            yield return null;
        }
        if (waitedForOther && !_handedOffToLowerFloor)
            yield return new WaitForSecondsRealtime(0.4f); // 앞 연출의 복귀 블렌드(0.35 s)가 끝난 뒤 시작

        // 반복 14C: 연출이 재생되면 화면 전체 경보 점멸은 연출 뒤로 미룬다 (연출 안의 회전 경보등이 대신하고, 클로즈업이 붉게 덮이지 않게).
        // 반복 15: 전투 가드(IsCombatActive)로 생략하지 않는다 — 경비는 AlarmBeatPending 동안 깨지 않고, 연출 중엔 무적·공격 토큰 차단.
        bool cineAlarm = CinematicDirector.Has("lobby_alarm") && !_isElevatorTransitioning && !_handedOffToLowerFloor && !CinematicDirector.IsPlaying;
        if (!cineAlarm)
            FlashLobbyAlert(6);

        // \ubc18\ubcf5 14: \uacbd\ubcf4\u00b7\ub4dc\ub860 \ub9ac\ube4c \uc5f0\ucd9c\uc774 \uc124\uce58\ub3fc \uc788\uc73c\uba74 \uac19\uc740 \ud0a4(lobby.alarm/drones)\ub97c \uc5f0\ucd9c \uc790\ub9c9\uc73c\ub85c \ud45c\uc2dc\ud558\ubbc0\ub85c \uae30\uc874 4\uc904\uc740 \uac74\ub108\ub6f4\ub2e4.
        // (엘리베이터를 이미 탔으면 연출을 시작하지 않는다 — 하강 연출과 겹침 방지)
        if (cineAlarm)
        {
            yield return CinematicDirector.PlayRoutine("lobby_alarm");
            if (!_handedOffToLowerFloor)
                FlashLobbyAlert(2);
        }
        else
        {
            yield return StartCoroutine(PlayKeyed("lobby.alarm.0", "System", "\uce68\uc785\uc790 \uac10\uc9c0. \ubc29\uc5b4 \ud504\ub85c\ud1a0\ucf5c\uc744 \uac00\ub3d9\ud569\ub2c8\ub2e4.", 3f));
            yield return StartCoroutine(PlayOptional("lobby.alarm.1", 2f));
            yield return StartCoroutine(PlayKeyed("lobby.drones.0", "EGO", "\ud504\ub860\ud2b8 \ub370\uc2a4\ud06c \ub4a4\uc5d0\uc11c \ub4dc\ub860\uc774 \ub098\uc628\ub2e4. \uba3c\uc800 \uc815\ub9ac\ud558\uc790.", 2.5f));
            yield return StartCoroutine(PlayKeyed("lobby.drones.1", "ChuOn", "\uc88b\uc544. \ubc00\uace0 \ub098\uac04\ub2e4.", 2f));
        }
        AlarmBeatPending = false;

        if (_handedOffToLowerFloor)
            yield break;

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        // Optional gate: hold the drone wave until the player crosses combatStartGate (along its forward axis).
        if (combatStartGate != null)
        {
            UpdateQuestUI("침투", "복도를 따라 본사 로비로 진입해.");
            ShowQuestPanel();
            yield return new WaitUntil(() => _handedOffToLowerFloor || HasPlayerPassedCombatGate());
        }

        // 가르치기: 아트리움 경비 안드로이드를 정리해야 드론 웨이브가 시작된다 (잡몹 교전이 없으면 즉시 통과)
        LobbyMobEncounter mobs = ResolveMobEncounter();
        if (mobs != null && !mobs.TeachCleared && !_handedOffToLowerFloor)
        {
            UpdateQuestUI("경비 병력", "로비 경비 안드로이드를 제압해.");
            ShowQuestPanel();
            yield return new WaitUntil(() => _handedOffToLowerFloor || mobs == null || mobs.TeachCleared);
        }

        if (_handedOffToLowerFloor)
            yield break;

        StartCoroutine(SequenceCombat());
    }

    LobbyMobEncounter ResolveMobEncounter()
    {
        if (mobEncounter == null)
            mobEncounter = LobbyMobEncounter.Active;
        return mobEncounter != null && mobEncounter.isActiveAndEnabled ? mobEncounter : null;
    }

    bool HasPlayerPassedCombatGate()
    {
        if (combatStartGate == null)
            return true;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return false;

        return Vector3.Dot(player.transform.position - combatStartGate.position, combatStartGate.forward) > 0f;
    }

    IEnumerator SequenceCombat()
    {
        if (_handedOffToLowerFloor)
            yield break;

        SetPhase(LobbyFlowPhase.Combat);
        _currentDeadEnemy = 0;
        _combatEscalationStage = 0;
        _activeLobbyDrones.Clear();
        UpdateQuestUI("\ud604\uc7ac \ubaa9\ud45c", "\uc801 \uc2e0\ud638\ub97c \uc804\ubd80 \uc81c\uac70\ud574.");
        ShowQuestPanel();
        CombatStarted?.Invoke();

        if (dronePrefab != null && spawnPoints != null && spawnPoints.Length > 0)
        {
            _totalEnemyCount = spawnPoints.Length;
            GameObject player = GameObject.FindWithTag("Player");
            Transform playerTransform = player != null ? player.transform : null;

            if (presentationController != null)
            {
                presentationController.ShowThreatMarkers(threatMarkerLeadTime);
                if (threatMarkerLeadTime > 0f)
                    yield return new WaitForSeconds(threatMarkerLeadTime);
            }

            if (_handedOffToLowerFloor)
                yield break;

            PrewarmLobbyProjectilePool();
            yield return StartCoroutine(SpawnLobbyDrones(playerTransform));
        }
        else
        {
            _totalEnemyCount = 1;
            OnEnemyKilled();
        }

        if (_handedOffToLowerFloor)
            yield break;

        UpdateQuestUI("\uad50\uc804 \uac1c\uc2dc", $"\uc801 \uc81c\uac70 ({_currentDeadEnemy} / {_totalEnemyCount})");
        yield return new WaitUntil(() => _currentDeadEnemy >= _totalEnemyCount || _handedOffToLowerFloor);
        if (_handedOffToLowerFloor)
            yield break;
        yield return new WaitForSeconds(1f);
        if (_handedOffToLowerFloor)
            yield break;
        CombatCompleted?.Invoke();
        StartCoroutine(SequencePostCombat());
    }

    public void OnEnemyKilled()
    {
        // 핸드오프 후 상층 드론 정리(SetActive false → LobbyEnemy.OnDisable)는 처치로 세지 않는다
        if (_handedOffToLowerFloor)
            return;

        _currentDeadEnemy++;
        EvaluateCombatEscalation();
        UpdateQuestUI("\uad50\uc804", $"\uc801 \uc81c\uac70 ({_currentDeadEnemy} / {_totalEnemyCount})");
        EnemyProgressUpdated?.Invoke(_currentDeadEnemy, _totalEnemyCount);
        if (questDescriptionText != null)
            questDescriptionText.transform.DOPunchScale(Vector3.one * 0.2f, 0.15f);
    }

    void PrewarmLobbyProjectilePool()
    {
        if (dronePrefab == null || spawnPoints == null)
            return;

        DroneController droneTemplate = dronePrefab.GetComponent<DroneController>();
        if (droneTemplate == null || droneTemplate.projectilePrefab == null)
            return;

        int prewarmCount = Mathf.Max(6, spawnPoints.Length * 2);
        RuntimeObjectPool.Prewarm(droneTemplate.projectilePrefab, prewarmCount);
    }

    IEnumerator SpawnLobbyDrones(Transform playerTransform)
    {
        DroneCombatRole[] rolePlan = BuildLobbyDroneRolePlan(playerTransform);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            // 하층으로 넘어간 뒤에는 상층 드론을 더 만들지 않는다 (강제/체크포인트 핸드오프 시 상층에서 하층 플레이어를 계속 저격하던 문제)
            if (_handedOffToLowerFloor)
                yield break;

            Transform spawnPoint = spawnPoints[i];
            if (spawnPoint == null)
                continue;

            GameObject drone = TakePrewarmedDrone(spawnPoint.position, spawnPoint.rotation);
            if (drone == null)
            {
                drone = Instantiate(dronePrefab, spawnPoint.position, spawnPoint.rotation);
                PrepareLobbyDrone(drone);
            }
            DroneController controller = drone.GetComponent<DroneController>();
            if (controller != null)
            {
                DroneCombatRole role = i < rolePlan.Length ? rolePlan[i] : DroneCombatRole.Standard;
                controller.SetCombatRole(role);
                ApplyLobbyDroneRuntimeTuning(controller);
                controller.target = playerTransform;
                controller.SetLightweightSimulation(useLightweightLobbyDroneSimulation, lightweightLobbyDroneTickInterval);
                controller.SetAttackTelegraph(droneAttackTelegraphLeadTime);
                controller.enabled = true;
                controller.SetInitialFireDelay(ResolveLobbyDroneInitialFireDelay(i, role));
                RegisterActiveLobbyDrone(controller);
                DroneSpawned?.Invoke(controller);
            }

            if (drone.GetComponent<LobbyEnemy>() == null)
                drone.AddComponent<LobbyEnemy>();

            // Teach -> test: the first drone fights alone, the rest arrive together after it falls.
            if (staggerFirstDroneAlone && i == 0 && spawnPoints.Length > 1)
            {
                yield return new WaitUntil(() => _currentDeadEnemy >= 1 || _handedOffToLowerFloor);
                if (_handedOffToLowerFloor)
                    yield break;
                if (staggeredWaveDelay > 0f)
                    yield return new WaitForSeconds(staggeredWaveDelay);
                if (presentationController != null && threatMarkerLeadTime > 0f)
                {
                    presentationController.ShowThreatMarkers(threatMarkerLeadTime);
                    yield return new WaitForSeconds(threatMarkerLeadTime);
                }
                continue;
            }

            if (droneSpawnInterval > 0f && i < spawnPoints.Length - 1)
                yield return new WaitForSeconds(droneSpawnInterval);
        }
    }

    void RegisterActiveLobbyDrone(DroneController controller)
    {
        if (controller == null || _activeLobbyDrones.Contains(controller))
            return;

        _activeLobbyDrones.Add(controller);
    }

    void EvaluateCombatEscalation()
    {
        if (!enableLobbyCombatEscalation || _totalEnemyCount <= 1)
            return;

        PruneInactiveLobbyDrones();

        float killRatio = _totalEnemyCount > 0 ? (float)_currentDeadEnemy / _totalEnemyCount : 0f;
        int targetStage = 0;
        if (killRatio >= escalationStageTwoKillRatio)
            targetStage = 2;
        else if (killRatio >= escalationStageOneKillRatio)
            targetStage = 1;

        if (targetStage <= _combatEscalationStage)
            return;

        for (int stage = _combatEscalationStage + 1; stage <= targetStage; stage++)
            ApplyCombatEscalationStage(stage);

        _combatEscalationStage = targetStage;
    }

    void ApplyCombatEscalationStage(int stage)
    {
        if (stage <= 0)
            return;

        PruneInactiveLobbyDrones();

        float telegraphScale = ResolveCombatEscalationTelegraphScale(stage);
        for (int i = 0; i < _activeLobbyDrones.Count; i++)
        {
            DroneController controller = _activeLobbyDrones[i];
            if (controller == null)
                continue;

            DroneCombatRole escalatedRole = ResolveEscalatedCombatRole(controller.CombatRole, stage);
            controller.SetCombatRole(escalatedRole);
            ApplyLobbyDroneRuntimeTuning(controller);
            controller.SetAttackTelegraph(droneAttackTelegraphLeadTime * telegraphScale);
        }

        if (!showEscalationQuestCue)
            return;

        if (stage == 1)
        {
            ShowTransientQuestCue(
                "\uc801 \uc7ac\ubc30\uce58",
                "\uc0dd\uc874 \ub4dc\ub860\uc774 \uce21\uba74 \uc555\ubc15\uc73c\ub85c \uc804\ud658\ud55c\ub2e4.",
                1.4f,
                true);
        }
        else if (stage == 2)
        {
            ShowTransientQuestCue(
                "\ucd5c\uc885 \uc555\ubc15",
                "\ub0a8\uc740 \ub4dc\ub860\uc774 \uc804\uc9c4 \uc18d\ub3c4\uc640 \uc0ac\uaca9 \ud15c\ud3ec\ub97c \ub04c\uc5b4\uc62c\ub9b0\ub2e4.",
                1.5f,
                true);
        }
    }

    float ResolveCombatEscalationTelegraphScale(int stage)
    {
        switch (stage)
        {
            case 2:
                return escalationStageTwoTelegraphScale;
            case 1:
                return escalationStageOneTelegraphScale;
            default:
                return 1f;
        }
    }

    DroneCombatRole ResolveEscalatedCombatRole(DroneCombatRole currentRole, int stage)
    {
        if (stage <= 0)
            return currentRole;

        if (stage == 1)
        {
            switch (currentRole)
            {
                case DroneCombatRole.Standard:
                    return DroneCombatRole.Flanker;
                case DroneCombatRole.Suppressor:
                    return DroneCombatRole.Suppressor;
                default:
                    return currentRole;
            }
        }

        switch (currentRole)
        {
            case DroneCombatRole.Standard:
            case DroneCombatRole.Flanker:
                return DroneCombatRole.Skirmisher;
            case DroneCombatRole.Suppressor:
                return DroneCombatRole.Suppressor;
            default:
                return currentRole;
        }
    }

    void PruneInactiveLobbyDrones()
    {
        for (int i = _activeLobbyDrones.Count - 1; i >= 0; i--)
        {
            DroneController controller = _activeLobbyDrones[i];
            if (controller == null || !controller.gameObject.activeInHierarchy || controller.IsDead)
                _activeLobbyDrones.RemoveAt(i);
        }
    }

    DroneCombatRole[] BuildLobbyDroneRolePlan(Transform playerTransform)
    {
        int spawnCount = spawnPoints != null ? spawnPoints.Length : 0;
        if (spawnCount <= 0)
            return System.Array.Empty<DroneCombatRole>();

        DroneCombatRole[] roles = new DroneCombatRole[spawnCount];
        for (int i = 0; i < roles.Length; i++)
            roles[i] = DroneCombatRole.Standard;

        if (!useRoleAwareSpawnPlan || spawnCount == 1)
            return roles;

        Vector3 center = ResolveSpawnCenter();
        Vector3 encounterForward = ResolveEncounterForward(center, playerTransform);
        Vector3 encounterRight = Vector3.Cross(Vector3.up, encounterForward).normalized;

        int[] validIndices = GetValidSpawnIndices();
        if (validIndices.Length == 0)
            return roles;

        int farthestIndex = GetExtremumIndexByDistance(validIndices, playerTransform, true);
        int closestIndex = GetExtremumIndexByDistance(validIndices, playerTransform, false);
        int leftMostIndex = GetExtremumIndexByLateral(validIndices, center, encounterRight, false);
        int rightMostIndex = GetExtremumIndexByLateral(validIndices, center, encounterRight, true);

        switch (lobbyDroneWaveProfile)
        {
            case LobbyDroneWaveProfile.Balanced:
                AssignRoleIfValid(roles, farthestIndex, DroneCombatRole.Suppressor);
                AssignRoleIfValid(roles, ResolveOuterIndexExcluding(leftMostIndex, rightMostIndex, farthestIndex), DroneCombatRole.Flanker);
                if (spawnCount >= 5)
                    AssignRoleIfValid(roles, closestIndex, DroneCombatRole.Skirmisher);
                break;

            case LobbyDroneWaveProfile.Suppression:
                AssignRoleIfValid(roles, farthestIndex, DroneCombatRole.Suppressor);
                AssignRoleIfValid(roles, ResolveSecondaryDistanceIndex(validIndices, playerTransform, farthestIndex, true), DroneCombatRole.Suppressor);
                AssignRoleIfValid(roles, ResolveOuterIndexExcluding(leftMostIndex, rightMostIndex, farthestIndex), DroneCombatRole.Flanker);
                break;

            case LobbyDroneWaveProfile.Crossfire:
                AssignRoleIfValid(roles, leftMostIndex, DroneCombatRole.Flanker);
                AssignRoleIfValid(roles, rightMostIndex, DroneCombatRole.Flanker);
                if (spawnCount >= 4)
                    AssignRoleIfValid(roles, farthestIndex, DroneCombatRole.Suppressor);
                break;

            case LobbyDroneWaveProfile.Skirmish:
                AssignRoleIfValid(roles, leftMostIndex, DroneCombatRole.Flanker);
                AssignRoleIfValid(roles, rightMostIndex, DroneCombatRole.Flanker);
                AssignRemainingRoles(roles, DroneCombatRole.Skirmisher);
                break;

            case LobbyDroneWaveProfile.MixedPressure:
            default:
                AssignRoleIfValid(roles, farthestIndex, DroneCombatRole.Suppressor);
                AssignRoleIfValid(roles, leftMostIndex, DroneCombatRole.Flanker);
                AssignRoleIfValid(roles, rightMostIndex, DroneCombatRole.Flanker);
                if (spawnCount >= 5)
                    AssignRoleIfValid(roles, closestIndex, DroneCombatRole.Skirmisher);
                break;
        }

        return roles;
    }

    float ResolveLobbyDroneInitialFireDelay(int spawnIndex, DroneCombatRole role)
    {
        float delay = droneInitialFireBaseDelay + (droneInitialFireStagger * spawnIndex);
        if (!useRoleAwareSpawnPlan)
            return Mathf.Max(0f, delay);

        switch (role)
        {
            case DroneCombatRole.Suppressor:
                delay += suppressorFireDelayOffset;
                break;

            case DroneCombatRole.Flanker:
                delay += flankerFireDelayOffset;
                break;

            case DroneCombatRole.Skirmisher:
                delay -= skirmisherFireLead;
                break;
        }

        float deterministicVariance = (((spawnIndex * 17) + (spawnPoints.Length * 13)) % 7) * 0.015f;
        return Mathf.Max(0f, delay + deterministicVariance);
    }

    Vector3 ResolveSpawnCenter()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform.position;

        Vector3 center = Vector3.zero;
        int count = 0;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Transform spawnPoint = spawnPoints[i];
            if (spawnPoint == null)
                continue;

            center += spawnPoint.position;
            count++;
        }

        if (count == 0)
            return transform.position;

        return center / count;
    }

    Vector3 ResolveEncounterForward(Vector3 center, Transform playerTransform)
    {
        if (playerTransform != null)
        {
            Vector3 toCenter = center - playerTransform.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.0001f)
                return toCenter.normalized;
        }

        Vector3 fallback = transform.forward;
        fallback.y = 0f;
        if (fallback.sqrMagnitude <= 0.0001f)
            fallback = Vector3.forward;
        return fallback.normalized;
    }

    int[] GetValidSpawnIndices()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return System.Array.Empty<int>();

        List<int> indices = new List<int>(spawnPoints.Length);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] != null)
                indices.Add(i);
        }

        return indices.ToArray();
    }

    int GetExtremumIndexByDistance(int[] validIndices, Transform playerTransform, bool farthest)
    {
        if (validIndices == null || validIndices.Length == 0)
            return -1;

        Vector3 origin = playerTransform != null ? playerTransform.position : ResolveSpawnCenter();
        float selectedDistance = farthest ? float.MinValue : float.MaxValue;
        int selectedIndex = validIndices[0];

        for (int i = 0; i < validIndices.Length; i++)
        {
            int index = validIndices[i];
            Transform spawnPoint = spawnPoints[index];
            if (spawnPoint == null)
                continue;

            float distance = Vector3.SqrMagnitude(spawnPoint.position - origin);
            bool replace = farthest ? distance > selectedDistance : distance < selectedDistance;
            if (!replace)
                continue;

            selectedDistance = distance;
            selectedIndex = index;
        }

        return selectedIndex;
    }

    int ResolveSecondaryDistanceIndex(int[] validIndices, Transform playerTransform, int excludedIndex, bool farthest)
    {
        if (validIndices == null || validIndices.Length == 0)
            return -1;

        Vector3 origin = playerTransform != null ? playerTransform.position : ResolveSpawnCenter();
        float selectedDistance = farthest ? float.MinValue : float.MaxValue;
        int selectedIndex = -1;

        for (int i = 0; i < validIndices.Length; i++)
        {
            int index = validIndices[i];
            if (index == excludedIndex)
                continue;

            Transform spawnPoint = spawnPoints[index];
            if (spawnPoint == null)
                continue;

            float distance = Vector3.SqrMagnitude(spawnPoint.position - origin);
            bool replace = farthest ? distance > selectedDistance : distance < selectedDistance;
            if (!replace)
                continue;

            selectedDistance = distance;
            selectedIndex = index;
        }

        return selectedIndex;
    }

    int GetExtremumIndexByLateral(int[] validIndices, Vector3 center, Vector3 encounterRight, bool rightMost)
    {
        if (validIndices == null || validIndices.Length == 0)
            return -1;

        float selectedValue = rightMost ? float.MinValue : float.MaxValue;
        int selectedIndex = validIndices[0];

        for (int i = 0; i < validIndices.Length; i++)
        {
            int index = validIndices[i];
            Transform spawnPoint = spawnPoints[index];
            if (spawnPoint == null)
                continue;

            float lateral = Vector3.Dot(spawnPoint.position - center, encounterRight);
            bool replace = rightMost ? lateral > selectedValue : lateral < selectedValue;
            if (!replace)
                continue;

            selectedValue = lateral;
            selectedIndex = index;
        }

        return selectedIndex;
    }

    int ResolveOuterIndexExcluding(int leftMostIndex, int rightMostIndex, int excludedIndex)
    {
        if (leftMostIndex >= 0 && leftMostIndex != excludedIndex)
            return leftMostIndex;

        if (rightMostIndex >= 0 && rightMostIndex != excludedIndex)
            return rightMostIndex;

        return -1;
    }

    void AssignRoleIfValid(DroneCombatRole[] roles, int index, DroneCombatRole role)
    {
        if (roles == null || index < 0 || index >= roles.Length)
            return;

        if (roles[index] != DroneCombatRole.Standard)
            return;

        roles[index] = role;
    }

    void AssignRemainingRoles(DroneCombatRole[] roles, DroneCombatRole role)
    {
        if (roles == null)
            return;

        for (int i = 0; i < roles.Length; i++)
        {
            if (roles[i] == DroneCombatRole.Standard)
                roles[i] = role;
        }
    }

    void PrepareLobbyDrone(GameObject drone)
    {
        if (drone == null)
            return;

        if (disableLobbyDroneWorldUi)
        {
            ProximityBossUI proximityUi = drone.GetComponent<ProximityBossUI>();
            if (proximityUi != null && proximityUi.enabled)
                proximityUi.enabled = false;
        }

        if (simplifyLobbyDroneVisual)
            SimplifyLobbyDroneVisual(drone);

        if (!disableLobbyDroneShadows)
            return;

        Renderer[] renderers = drone.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }

    void ApplyLobbyDroneRuntimeTuning(DroneController controller)
    {
        if (controller == null)
            return;

        controller.moveSpeed *= Mathf.Clamp(lobbyDroneMoveSpeedMultiplier, 0.1f, 1f);
        controller.SetProjectileSpawnForwardOffset(lobbyDroneProjectileSpawnForwardOffset);
    }

    void SimplifyLobbyDroneVisual(GameObject drone)
    {
        Transform[] children = drone.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || child == drone.transform)
                continue;

            if (!child.name.Equals("drone"))
                continue;

            child.gameObject.SetActive(false);

            Animator childAnimator = child.GetComponentInChildren<Animator>(true);
            if (childAnimator != null)
                childAnimator.enabled = false;

            break;
        }

        Transform simpleVisual = drone.transform.Find("__SimpleDroneVisual");
        if (simpleVisual == null)
        {
            GameObject simpleVisualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            simpleVisualGo.name = "__SimpleDroneVisual";
            simpleVisualGo.layer = drone.layer;

            Transform simpleTransform = simpleVisualGo.transform;
            simpleTransform.SetParent(drone.transform, false);
            simpleTransform.localPosition = new Vector3(0f, 0.3f, 0f);
            simpleTransform.localRotation = Quaternion.identity;
            simpleTransform.localScale = new Vector3(0.7f, 0.22f, 0.7f);

            Collider simpleCollider = simpleVisualGo.GetComponent<Collider>();
            if (simpleCollider != null)
                Destroy(simpleCollider);

            Renderer simpleRenderer = simpleVisualGo.GetComponent<Renderer>();
            if (simpleRenderer != null)
            {
                simpleRenderer.shadowCastingMode = ShadowCastingMode.Off;
                simpleRenderer.receiveShadows = false;
                simpleRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
        }
    }

    IEnumerator SequencePostCombat()
    {
        if (_handedOffToLowerFloor)
            yield break;

        SetPhase(LobbyFlowPhase.Route);
        UpdateQuestUI("\uad50\uc804 \uc885\ub8cc", "\ubaa8\ub4e0 \uc801 \uc2e0\ud638 \uc81c\uac70.");
        _isDoorReached = false;

        // 시험·비틀기: 드론 전멸 → 엘리베이터 홀 경비(안드로이드 + 집행관, 터렛 지원)를 정리해야 엘리베이터로 갈 수 있다
        LobbyMobEncounter mobs = ResolveMobEncounter();
        if (mobs != null && mobs.GuardsPendingOrActive && !mobs.GuardsCleared)
        {
            int lastDead = -1;
            while (!_handedOffToLowerFloor && mobs != null && !mobs.GuardsCleared)
            {
                if (mobs.GuardsActivated && mobs.GuardDead != lastDead)
                {
                    lastDead = mobs.GuardDead;
                    UpdateQuestUI("엘리베이터 경비", $"경비 병력 제거 ({lastDead} / {mobs.GuardTotal})");
                    ShowQuestPanel();
                }
                yield return null;
            }
            if (_handedOffToLowerFloor)
                yield break;
            UpdateQuestUI("교전 종료", "엘리베이터 경비 해제.");
        }

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(PlayKeyed("lobby.post.0", "EGO", "\uc88b\uc544, \uc815\ub9ac \ub05d. \uc77c\ubc18 \uc5d8\ub9ac\ubca0\uc774\ud130\ub294 \uc804\ubd80 \uc7a0\uaca8 \uc788\uc5b4.", 2.2f));
        yield return StartCoroutine(PlayKeyed("lobby.post.1", "ChuOn", "\ub2e4\ub978 \uae38\uc740?", 1.3f));
        yield return StartCoroutine(PlayKeyed("lobby.post.2", "EGO", routeSuspicionLine, routeSuspicionDelay));

        if (_handedOffToLowerFloor)
            yield break;

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("\uc774\ub3d9", "\ud504\ub860\ud2b8 \ub370\uc2a4\ud06c \ub4a4\ud3b8 \uc5d8\ub9ac\ubca0\uc774\ud130\ub85c \uc774\ub3d9\ud574.");

        if (guideSystem != null && ExhibitionPrototypePresentationPolicy.RuntimeLobbyGuidePathEnabled)
        {
            _activeWaypoints.Clear();
            if (pathWaypoints != null)
            {
                for (int i = 0; i < pathWaypoints.Length; i++)
                {
                    Transform waypoint = pathWaypoints[i];
                    if (waypoint == null)
                        continue;

                    waypoint.gameObject.SetActive(true);
                    _activeWaypoints.Add(waypoint);
                }
            }

            UpdateNavigationPath();
        }

        if (presentationController != null)
            presentationController.ShowApproachMarker();
        ShowLobbyWorldMarker(_approachWorldMarker, true);
        ShowLobbyWorldMarker(_boardWorldMarker, false);

        yield return new WaitUntil(() => _isDoorReached || _handedOffToLowerFloor);
        if (_handedOffToLowerFloor)
            yield break;

        StartCoroutine(SequenceAtElevator());
    }

    public void OnWaypointReached(Transform reachedPoint)
    {
        if (!_activeWaypoints.Contains(reachedPoint))
            return;

        _activeWaypoints.Remove(reachedPoint);
        UpdateNavigationPath();
    }

    void UpdateNavigationPath()
    {
        if (guideSystem == null || !ExhibitionPrototypePresentationPolicy.RuntimeLobbyGuidePathEnabled)
            return;

        List<Transform> pathList = new List<Transform>(_activeWaypoints.Count + 1);
        pathList.AddRange(_activeWaypoints);
        if (triggerApproach != null)
            pathList.Add(triggerApproach);
        guideSystem.ShowPath(pathList.ToArray());
    }

    public void OnReachElevator()
    {
        _isDoorReached = true;
        if (guideSystem != null)
            guideSystem.HidePath();

        if (presentationController != null)
            presentationController.ShowBoardMarker();
        ShowLobbyWorldMarker(_approachWorldMarker, false);
        ShowLobbyWorldMarker(_boardWorldMarker, true);
    }

    IEnumerator SequenceAtElevator()
    {
        if (_handedOffToLowerFloor)
            yield break;

        SetPhase(LobbyFlowPhase.Board);
        OpenDoor(doorLeft);
        OpenDoor(doorRight);

        yield return StartCoroutine(PlayKeyed("lobby.elev.0", "EGO", "\uc774 \uc5d8\ub9ac\ubca0\uc774\ud130\ub3c4 \uacbd\ub85c\uac00 \uc81c\ud55c\ub3fc \uc788\uc5b4. \uaca9\ub0a9\uace0 \uce35\uc73c\ub85c\ub9cc \uc5f0\uacb0\ub418\ub294 \ub4ef\ud574.", 2.6f));
        yield return StartCoroutine(PlayKeyed("lobby.elev.1", "ChuOn", "\ub188\ub4e4\uc774 \uc77c\ubd80\ub7ec \uc5f4\uc5b4\ub454 \uae38\uc778\uac00.", 1.9f));
        yield return StartCoroutine(PlayKeyed("lobby.elev.2", "EGO", "\uadf8\ub798\ub3c4 \uc9c0\uae08\uc740 \uc774 \uae38\ubfd0\uc774\uc57c. \uc548\uc73c\ub85c \ub4e4\uc5b4\uac00.", 2f));
        yield return StartCoroutine(PlayOptional("lobby.elev.3", 1.6f));
        yield return StartCoroutine(PlayOptional("lobby.elev.4", 2f));

        if (_handedOffToLowerFloor)
            yield break;

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("\ud0d1\uc2b9", "\uc2b9\uac15\uae30 \uc548\uc73c\ub85c \ub4e4\uc5b4\uac00.");

        if (guideSystem != null && triggerBoard != null && ExhibitionPrototypePresentationPolicy.RuntimeLobbyGuidePathEnabled)
            guideSystem.ShowPath(triggerBoard);

        _isBoarded = false;
        yield return new WaitUntil(() => _isBoarded || _handedOffToLowerFloor);
        if (_handedOffToLowerFloor)
            yield break;

        if (guideSystem != null)
            guideSystem.HidePath();

        StartCoroutine(SequenceInside());
    }

    void OpenDoor(Transform door)
    {
        if (door == null)
            return;

        Collider collider = door.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        Vector3 localPosition = door.localPosition;
        door.DOLocalMoveY(localPosition.y + doorOpenHeight, doorOpenSpeed).SetEase(Ease.OutQuad);
    }

    public void OnEnterElevator()
    {
        _isBoarded = true;

        if (presentationController != null)
            presentationController.HideObjectiveMarker();
        ShowLobbyWorldMarker(_approachWorldMarker, false);
        ShowLobbyWorldMarker(_boardWorldMarker, false);
    }

    IEnumerator SequenceInside()
    {
        if (_handedOffToLowerFloor)
            yield break;

        SetPhase(LobbyFlowPhase.Elevator);
        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.15f);

        UpdateQuestUI("\ud558\uac15", "\uc2b9\uac15\uae30\ub85c \ub2e4\uc74c \uad6c\uc5ed\uc73c\ub85c \uc774\ub3d9 \uc911.");

        if (presentationController != null)
        {
            presentationController.HideElevatorPanelGuide();
            presentationController.HideObjectiveMarker();
        }

        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);

        if (_isElevatorTransitioning)
            yield break;

        _isElevatorTransitioning = true;
        yield return StartCoroutine(CoLowerElevator());
        yield return StartCoroutine(CoTransitionFromElevator(false));
    }

    IEnumerator CoLowerElevator()
    {
        Transform liftRoot = ResolveElevatorLiftRoot();
        if (liftRoot == null || elevatorLowerDistance <= 0f)
            yield break;

        GameObject player = GameObject.FindWithTag("Player");
        Transform playerRoot = player != null ? player.transform : null;
        Transform originalParent = playerRoot != null ? playerRoot.parent : null;

        if (playerRoot != null && !playerRoot.IsChildOf(liftRoot))
            playerRoot.SetParent(liftRoot, true);

        Vector3 start = _elevatorLiftInitialLocalPositionValid
            ? _elevatorLiftInitialLocalPosition
            : liftRoot.localPosition;
        Vector3 end = start + Vector3.down * elevatorLowerDistance;

        float duration = Mathf.Max(0.05f, elevatorLowerDuration);
        Tween tween = liftRoot.DOLocalMove(end, duration).SetEase(Ease.InOutSine);
        yield return tween.WaitForCompletion();

        if (playerRoot != null && originalParent != liftRoot)
            playerRoot.SetParent(originalParent, true);
    }

    Transform ResolveElevatorLiftRoot()
    {
        if (elevatorLiftRoot != null)
        {
            CacheElevatorLiftRootStart();
            return elevatorLiftRoot;
        }

        GameObject liftObject = GameObject.Find("elevator");
        if (liftObject != null)
            elevatorLiftRoot = liftObject.transform;

        CacheElevatorLiftRootStart();
        return elevatorLiftRoot;
    }

    void CacheElevatorLiftRootStart()
    {
        if (_elevatorLiftInitialLocalPositionValid || elevatorLiftRoot == null)
            return;

        _elevatorLiftInitialLocalPosition = elevatorLiftRoot.localPosition;
        _elevatorLiftInitialLocalPositionValid = true;
    }

    public void OnInteractElevator()
    {
        if (_isElevatorTransitioning || _handedOffToLowerFloor)
            return;

        _isElevatorTransitioning = true;

        if (presentationController != null)
        {
            presentationController.HideElevatorPanelGuide();
            presentationController.PulseElevatorInteractable();
        }

        if (elevatorPanel != null)
        {
            BaseInteractable elevatorInteractable = elevatorPanel.GetComponent<BaseInteractable>();
            if (elevatorInteractable != null)
                elevatorInteractable.promptText = elevatorSyncPromptText;

            elevatorPanel.transform.DOPunchScale(Vector3.one * 0.14f, 0.28f);
        }

        ShowTransientQuestCue(
            elevatorSyncCueTitle,
            elevatorSyncCueDescription,
            elevatorSyncCueHold,
            false);

        StartCoroutine(CoTransitionFromElevator(true));
    }

    /// <summary>상층 대사 패널이 보이는 중인가 (전투 bark 가 겹치지 않게).</summary>
    public bool IsDialogueVisible => dialogueGroup != null && dialogueGroup.alpha > 0.05f && !_handedOffToLowerFloor;

    // 대사 테이블(DialogueTable) 키 우선, 없으면 기존(씬/코드) 문구로 재생
    IEnumerator PlayKeyed(string key, string speaker, string fallback, float waitTime)
    {
        string resolvedSpeaker = speaker;
        string resolvedText = fallback;
        float resolvedWait = waitTime;
        DialogueTable.Resolve(key, ref resolvedSpeaker, ref resolvedText, ref resolvedWait);
        return PlayDialogue(resolvedSpeaker, resolvedText, resolvedWait);
    }

    // 테이블에 키가 있을 때만 재생하는 추가 대사 (기획 검토용 확장 슬롯)
    IEnumerator PlayOptional(string key, float waitTime)
    {
        if (!DialogueTable.TryGet(key, out DialogueTable.Entry entry))
            return EmptyRoutine();

        return PlayKeyed(key, entry.speaker, entry.text, waitTime);
    }

    static IEnumerator EmptyRoutine()
    {
        yield break;
    }

    IEnumerator PlayDialogue(string speaker, string content, float waitTime)
    {
        // 하층으로 넘어간 뒤에는 상층 대사를 띄우지 않는다.
        if (_handedOffToLowerFloor)
            yield break;

        if (!ExhibitionPrototypePresentationPolicy.DialogueEnabled)
        {
            if (dialogueGroup != null)
                dialogueGroup.alpha = 0f;
            if (speakerText != null)
                speakerText.text = string.Empty;
            if (contentText != null)
                contentText.text = string.Empty;
            yield break;
        }

        if (dialogueGroup != null)
            dialogueGroup.alpha = 1f;

        if (speakerText != null)
            speakerText.text = speaker == "ChuOn" ? "\ucd94\uc628" : speaker;

        if (contentText != null)
        {
            contentText.text = string.Empty;
            for (int i = 0; i < content.Length; i++)
            {
                if (_handedOffToLowerFloor)
                    yield break;
                contentText.text += content[i];
                yield return new WaitForSeconds(textTypingSpeed);
            }
        }

        yield return new WaitForSeconds(waitTime);
    }

    public void OnBranchSelected()
    {
        if (_isBranchSelected)
            return;

        _isBranchSelected = true;
        if (debugLogs)
            Debug.Log(">> [LobbyManager] Branch selected.", this);
    }

    void UpdateQuestUI(string title, string desc)
    {
        // 하층(보스) 퀘스트 위에 상층 퀘스트가 덮어쓰지 않게
        if (_handedOffToLowerFloor)
            return;

        _currentQuestTitle = title;
        _currentQuestDescription = desc;

        if (questTitleText != null)
        {
            FitQuestText(questTitleText);
            questTitleText.text = title;
        }
        if (questDescriptionText != null)
        {
            FitQuestText(questDescriptionText);
            questDescriptionText.text = desc;
        }

        QuestUpdated?.Invoke(title, desc);
    }

    // 반복 15: 긴 목표 문구가 패널 밖으로 잘리던 문제(예: "복도를 따라 본사 로비로 진입해.") — 줄바꿈 + 패널 안 자동 크기(최소 70 %).
    static void FitQuestText(TMP_Text text)
    {
        if (text.enableAutoSizing && text.enableWordWrapping)
            return;
        float size = text.fontSize;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.fontSizeMax = size;
        text.fontSizeMin = Mathf.Max(12f, size * 0.7f);
        text.enableAutoSizing = true;
    }

    void ShowQuestPanel()
    {
        if (questPanelGroup == null || _handedOffToLowerFloor)
            return;

        if (_objectivePanelController != null && _objectivePanelController.enabled)
        {
            questPanelGroup.alpha = 0f;
            questPanelGroup.interactable = false;
            questPanelGroup.blocksRaycasts = false;
            questPanelGroup.transform.localScale = Vector3.one;
            return;
        }

        questPanelGroup.DOFade(1f, 0.5f);
        questPanelGroup.transform.DOPunchScale(Vector3.one * 0.1f, 0.3f);
    }

    public void ShowTransientQuestCue(string title, string description, float holdDuration = 1.8f, bool flashAlert = false)
    {
        if (_handedOffToLowerFloor)
            return;

        if (_transientQuestCueRoutine != null)
            StopCoroutine(_transientQuestCueRoutine);

        _transientQuestCueRoutine = StartCoroutine(CoShowTransientQuestCue(title, description, holdDuration, flashAlert));
    }

    void ConfigurePresentationController()
    {
        if (presentationController == null)
            return;

        GameObject player = GameObject.FindWithTag("Player");
        BaseInteractable elevatorInteractable = elevatorPanel != null ? elevatorPanel.GetComponent<BaseInteractable>() : null;

        presentationController.ConfigureRuntime(
            player != null ? player.transform : null,
            spawnPoints,
            triggerApproach,
            triggerBoard,
            elevatorInteractable);
    }

    void ConfigureLobbyWorldMarkers()
    {
        _approachWorldMarker = EnsureLobbyWorldMarker(triggerApproach, "LobbyElevatorApproachWorldMarker", elevatorRouteMarkerColor);
        _boardWorldMarker = EnsureLobbyWorldMarker(triggerBoard, "LobbyElevatorBoardWorldMarker", elevatorBoardMarkerColor);
        ShowLobbyWorldMarker(_approachWorldMarker, false);
        ShowLobbyWorldMarker(_boardWorldMarker, false);
    }

    static TutorialWorldMarker EnsureLobbyWorldMarker(Transform target, string markerName, Color markerColor)
    {
        if (target == null)
            return null;

        Transform existing = target.Find(markerName);
        TutorialWorldMarker marker = existing != null ? existing.GetComponent<TutorialWorldMarker>() : null;
        if (marker == null)
        {
            GameObject markerObject = new GameObject(markerName);
            markerObject.layer = target.gameObject.layer;
            markerObject.transform.SetParent(target, false);
            marker = markerObject.AddComponent<TutorialWorldMarker>();
        }

        marker.ConfigureRuntime(
            target,
            markerColor,
            new Vector3(1.05f, 0.025f, 1.05f),
            new Vector3(0f, 0.72f, 0f),
            new Vector3(0.09f, 0.42f, 0.09f),
            new Vector3(0f, 1.32f, 0f),
            new Vector3(0.26f, 0.12f, 0.26f));
        return marker;
    }

    static void ShowLobbyWorldMarker(TutorialWorldMarker marker, bool visible)
    {
        if (marker != null)
            marker.SetVisible(visible);
    }

    void ConfigureCombatCoachController()
    {
        if (_combatCoachController == null)
            return;

        GameObject player = GameObject.FindWithTag("Player");
        PlayerHealth playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
        _combatCoachController.ConfigureRuntime(this, playerHealth, presentationController);
    }

    void ConfigureObjectivePanelController()
    {
        if (_objectivePanelController == null || !_objectivePanelController.enabled)
            return;

        _objectivePanelController.ConfigureRuntime(this);
    }

    IEnumerator CoShowTransientQuestCue(string title, string description, float holdDuration, bool flashAlert)
    {
        ShowQuestPanel();

        if (_objectivePanelController != null && _objectivePanelController.enabled)
        {
            _objectivePanelController.ShowTransientCue(title, description);
        }
        else
        {
            if (questTitleText != null)
                questTitleText.text = title;
            if (questDescriptionText != null)
                questDescriptionText.text = description;
            QuestUpdated?.Invoke(title, description);

            if (questDescriptionText != null)
                questDescriptionText.transform.DOPunchScale(Vector3.one * 0.14f, 0.22f);
        }

        if (flashAlert && alertOverlay != null)
        {
            // Restart from clear so overlapping yoyo flashes can't leave the overlay stuck tinted.
            alertOverlay.DOKill();
            alertOverlay.color = new Color(1f, 0f, 0f, 0f);
            alertOverlay.DOFade(AccessibilityOptionsRuntime.ScaleFlash(0.24f), 0.14f).SetLoops(2, LoopType.Yoyo);
        }

        yield return new WaitForSeconds(Mathf.Max(0.25f, holdDuration));

        if (_objectivePanelController != null && _objectivePanelController.enabled)
        {
            _objectivePanelController.HideTransientCue();
        }
        else
        {
            if (questTitleText != null)
                questTitleText.text = _currentQuestTitle;
            if (questDescriptionText != null)
                questDescriptionText.text = _currentQuestDescription;
            QuestUpdated?.Invoke(_currentQuestTitle, _currentQuestDescription);
        }

        _transientQuestCueRoutine = null;
    }

    IEnumerator CoTransitionFromElevator(bool playSyncLine = true)
    {
        if (playSyncLine && !string.IsNullOrWhiteSpace(elevatorSyncLine))
            yield return StartCoroutine(PlayKeyed("lobby.sync.0", "EGO", elevatorSyncLine, Mathf.Min(0.45f, elevatorSyncCueHold)));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.15f);

        yield return new WaitForSeconds(Mathf.Max(0.05f, elevatorSyncDelay));

        if (continueInSameScene)
        {
            yield return StartCoroutine(CoContinueInSameScene());
            yield break;
        }

        TutorialSceneTransitionState.MarkLobbyToMain();

        if (SceneFader.Instance != null)
        {
            SceneFader.Instance.FadeOutAndLoadScene(nextSceneName);
            yield break;
        }

        SceneManager.LoadScene(nextSceneName);
    }

    // Same-scene descent: fade to black, hand off to the lower floor, fade back in (no scene load).
    IEnumerator CoContinueInSameScene()
    {
        CanvasGroup fade = EnsureSameSceneFadeOverlay();
        if (fade != null)
        {
            fade.blocksRaycasts = true;
            yield return fade.DOFade(1f, sameSceneFadeOutDuration).SetUpdate(true).SetEase(Ease.InOutQuad).WaitForCompletion();
        }

        HideUpperFloorPresentation();
        yield return null; // 반복 15: 상층 정리(대량 비활성화) 프레임과 하강 연출 첫 프레임을 분리 (검정 아래) — 첫 프레임 히치 59 ms 대책

        // 반복 14: 엘리베이터 하강 연출 (검정에서 시작·끝). 같은 씬 검정(정렬 32000)이 연출 오버레이(870)를 덮으므로
        // 연출 오버레이를 먼저 검정으로 채운 뒤 같은 씬 검정을 걷고, 끝나면 다시 덮는다. 미설치면 기존 흐름 그대로.
        if (CinematicDirector.Has("elevator_descent"))
        {
            // 다른 연출(예: 경보 리빌)이 아직 재생 중이면 끝 상태로 넘긴 뒤 시작한다 (검정 아래라 보이지 않음).
            if (CinematicDirector.IsPlaying)
            {
                CinematicDirector.SkipCurrent();
                float skipUntil = Time.realtimeSinceStartup + 1.5f;
                while (CinematicDirector.IsPlaying && Time.realtimeSinceStartup < skipUntil)
                    yield return null;
            }

            if (!CinematicDirector.IsPlaying)
            {
                CinematicOverlay cineOverlay = CinematicOverlay.Ensure();
                cineOverlay.SetFade(Color.black, 1f);
                if (fade != null)
                    fade.alpha = 0f;
                yield return CinematicDirector.PlayRoutine("elevator_descent");
                if (fade != null)
                    fade.alpha = 1f;
                cineOverlay.SetFade(Color.black, 0f); // 재도전 생략(즉시 끝) 경로에서도 남은 검정을 비운다 (같은 씬 검정 아래)
            }
        }

        _handedOffToLowerFloor = true;
        DisableUpperFloorDrones();

        try
        {
            ElevatorDescentFinished?.Invoke(this);
            LowerFloorReached?.Invoke(this);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (sameSceneBlackHold > 0f)
            yield return new WaitForSecondsRealtime(sameSceneBlackHold);

        if (fade != null)
        {
            yield return fade.DOFade(0f, sameSceneFadeInDuration).SetUpdate(true).SetEase(Ease.InOutQuad).WaitForCompletion();
            fade.blocksRaycasts = false;
        }
    }

    // 핸드오프 후 남은 상층 드론 정리 (상층 전투 도중 엘리베이터가 강제로 쓰인 경우 등)
    void DisableUpperFloorDrones()
    {
        for (int i = _activeLobbyDrones.Count - 1; i >= 0; i--)
        {
            DroneController controller = _activeLobbyDrones[i];
            if (controller != null && controller.gameObject.activeSelf)
                controller.gameObject.SetActive(false);
        }

        _activeLobbyDrones.Clear();
    }

    // Checkpoint retry after the boss arrival: skip the whole upper floor.
    void ApplyLowerFloorCheckpointState()
    {
        _handedOffToLowerFloor = true;
        DisableUpperFloorDrones();
        _isElevatorTransitioning = true;
        if (dialogueGroup != null)
            dialogueGroup.alpha = 0f;
        if (alertOverlay != null)
            alertOverlay.color = new Color(1f, 0f, 0f, 0f);
        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);
        if (guideSystem != null)
            guideSystem.HidePath();

        SetPhase(LobbyFlowPhase.Elevator);
        LowerFloorReached?.Invoke(this);
    }

    void HideUpperFloorPresentation()
    {
        if (dialogueGroup != null)
        {
            dialogueGroup.DOKill();
            dialogueGroup.alpha = 0f;
        }

        if (alertOverlay != null)
        {
            alertOverlay.DOKill();
            alertOverlay.color = new Color(1f, 0f, 0f, 0f);
        }

        if (guideSystem != null)
            guideSystem.HidePath();

        if (presentationController != null)
        {
            presentationController.HideElevatorPanelGuide();
            presentationController.HideObjectiveMarker();
        }

        ShowLobbyWorldMarker(_approachWorldMarker, false);
        ShowLobbyWorldMarker(_boardWorldMarker, false);

        // 상층 퀘스트 큐/런타임 목표 패널이 하층 보스 퀘스트 위에 남지 않게 정리한다.
        if (_transientQuestCueRoutine != null)
        {
            StopCoroutine(_transientQuestCueRoutine);
            _transientQuestCueRoutine = null;
        }

        if (_objectivePanelController != null && _objectivePanelController.enabled)
            _objectivePanelController.enabled = false;
    }

    CanvasGroup _sameSceneFade;

    CanvasGroup EnsureSameSceneFadeOverlay()
    {
        if (_sameSceneFade != null)
            return _sameSceneFade;

        GameObject root = new GameObject("LevelTransitionFade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        GameObject panel = new GameObject("Black", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = panel.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;

        _sameSceneFade = root.GetComponent<CanvasGroup>();
        _sameSceneFade.alpha = 0f;
        _sameSceneFade.blocksRaycasts = false;
        _sameSceneFade.interactable = false;
        return _sameSceneFade;
    }

    void SetPhase(LobbyFlowPhase phase)
    {
        _currentPhase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
