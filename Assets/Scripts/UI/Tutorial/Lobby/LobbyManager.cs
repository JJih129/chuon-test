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
    public bool simplifyLobbyDroneVisual = true;
    public bool useLightweightLobbyDroneSimulation = true;
    [Range(0.016f, 0.12f)] public float lightweightLobbyDroneTickInterval = 0.05f;
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

    [Header("Lobby Drone Motion")]
    [SerializeField] bool useAerialLobbyDroneMotion = true;
    [SerializeField, Range(0.8f, 2f)] float lobbyDroneMoveSpeedMultiplier = 1.28f;
    [SerializeField, Range(0.45f, 1f)] float lobbyDroneDistanceMultiplier = 0.72f;
    [SerializeField, Range(0f, 0.5f)] float lobbyDroneStrafeBoost = 0.28f;
    [SerializeField, Range(0f, 1f)] float lobbyDroneHoverAmplitude = 0.34f;
    [SerializeField, Range(0.3f, 3f)] float lobbyDroneHoverFrequency = 1.55f;

    [Header("Elevator")]
    public GameObject elevatorPanel;
    public string nextSceneName = "MainScene";

    [Header("Elevator Tension")]
    [TextArea] public string elevatorThreatLine = "\ube44\uc0c1 \uacbd\ub85c \uc804\uc6a9 \uc2b9\uac15\uae30\uc57c. \ub204\uad70\uac00 \uba3c\uc800 \uc774 \ub9c1\ud06c\ub97c \uae68\uc6cc\ub1a8\uc5b4.";
    [TextArea] public string elevatorResponseLine = "\uba3c\uc800 \uae30\ub2e4\ub9ac\uace0 \uc788\ub2e4\ub294 \ub73b\uc774\uaca0\uc9c0.";
    [TextArea] public string elevatorInstructionLine = "\ud328\ub110\uc744 \uc870\uc791\ud574. \ub9c1\ud06c\uac00 \ub04a\uae30\uae30 \uc804\uc5d0 \ub2e4\uc74c \uce35\uc73c\ub85c \ub0b4\ub824\uac04\ub2e4.";
    [TextArea] public string elevatorThreatCueTitle = "\ube44\uc815\uc0c1 \uc2e0\ud638";
    [TextArea] public string elevatorThreatCueDescription = "\ub204\uad70\uac00 \uba3c\uc800 \uc544\ub798\uce35 \uacbd\ub85c\ub97c \uc5f4\uc5b4\ub450\uc5c8\ub2e4.";
    [TextArea] public string elevatorPanelCueTitle = "\ud328\ub110 \uc5f0\ub3d9";
    [TextArea] public string elevatorPanelCueDescription = "\ud328\ub110\uc744 \uc870\uc791\ud574 \uce35\uac04 \ub9c1\ud06c\ub97c \uace0\uc815\ud574.";
    [TextArea] public string elevatorPromptText = "F: \ud328\ub110 \uc870\uc791";
    [TextArea] public string elevatorSyncCueTitle = "\ub9c1\ud06c \uace0\uc815";
    [TextArea] public string elevatorSyncCueDescription = "\ud558\uac15 \uacbd\ub85c \uace0\uc815. \ubc14\ub85c \ub2e4\uc74c \uce35\uc73c\ub85c \uc774\ub3d9\ud55c\ub2e4.";
    [TextArea] public string elevatorSyncLine = "\ub9c1\ud06c \uace0\uc815 \uc644\ub8cc. \ubc14\ub85c \ub0b4\ub824\uac04\ub2e4.";
    [TextArea] public string elevatorSyncPromptText = "\uc5f0\ub3d9 \uc911...";
    [Range(0.1f, 3f)] public float elevatorThreatCueHold = 1.6f;
    [Range(0.1f, 3f)] public float elevatorPanelCueHold = 1.5f;
    [Range(0f, 2f)] public float elevatorPanelCueDelay = 1.05f;
    [Range(0.1f, 2f)] public float elevatorSyncCueHold = 0.85f;
    [Range(0.05f, 1f)] public float elevatorSyncDelay = 0.45f;

    [Header("Elevator Door")]
    public Transform elevatorCabinRoot;
    public Transform doorLeft;
    public Transform doorRight;
    public float doorOpenHeight = 4f;
    public float doorOpenSpeed = 2f;
    [Min(0.1f)] public float doorCloseSpeed = 0.8f;
    [Min(0f)] public float elevatorLowerDistance = 5f;
    [Min(0.1f)] public float elevatorLowerDuration = 2.6f;
    [Range(0f, 1.5f)] public float elevatorFadeDelay = 1.25f;
    public bool parentPlayerToElevatorDuringDescent = true;

    [Header("Guide / Triggers")]
    public GuidePathSystem guideSystem;
    public Transform triggerApproach;
    public Transform triggerBoard;
    public Transform[] pathWaypoints;
    public LobbyPresentationController presentationController;

    [Header("Elevator Boarding Gate")]
    [SerializeField] bool requireBoardCenterBeforeTransition = true;
    [SerializeField, Min(0.1f)] float boardCenterRequiredRadius = 1.05f;
    [SerializeField] Vector3 boardCenterLocalOffset = Vector3.zero;

    [Header("Atmosphere")]
    [TextArea] public string routeSuspicionLine = "\ub204\uad70\uac00 \uba3c\uc800 \uc774 \uae38\uc744 \uc5f4\uc5b4\ub1a8\uc5b4. \uc548\uc804\ud558\ub2e4\uace0 \uac00\uc815\ud558\uc9c0 \ub9c8.";
    [Range(0.5f, 4f)] public float routeSuspicionDelay = 2.2f;

    [Header("Typing")]
    [Range(0.01f, 0.2f)] public float textTypingSpeed = 0.05f;
    [SerializeField] bool debugLogs = false;

    [Header("Input Lock")]
    [SerializeField] bool lockInputDuringArrivalIntro = true;
    [SerializeField] bool lockInputDuringPostCombatBrief = true;
    [SerializeField] bool lockInputDuringElevatorBrief = true;

    [Header("Flow Persistence")]
    [SerializeField] bool skipDroneEncounterAfterFirstClear = true;

    [Header("Start Facing")]
    [SerializeField] bool applyPlayerStartFacingYaw = true;
    [SerializeField] bool faceCameraForwardOnStart = true;
    [SerializeField] float playerStartYawOffset = 180f;

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
    int _combatEscalationStage;
    IInputBlocker _playerInputBlocker;
    bool _lobbyInputLocked;
    static bool s_droneEncounterClearedThisSession;
    bool _doorPoseCached;
    Vector3 _doorLeftClosedLocalPosition;
    Vector3 _doorRightClosedLocalPosition;
    bool _startFacingApplied;

    public bool IsDoorReached => _isDoorReached;
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
        if (_combatCoachController == null && ExhibitionPrototypePresentationPolicy.RuntimeCoachFeedbackEnabled)
            _combatCoachController = gameObject.AddComponent<LobbyCombatCoachController>();
        _objectivePanelController = GetComponent<LobbyObjectivePanelController>();
        if (_objectivePanelController == null && ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
            _objectivePanelController = gameObject.AddComponent<LobbyObjectivePanelController>();
    }

    void Start()
    {
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

        ConfigurePresentationController();
        ConfigureCombatCoachController();
        ConfigureObjectivePanelController();
        StartCoroutine(CoApplyPlayerStartFacingYawDelayed());
        _fromTutorialTransition = TutorialSceneTransitionState.ConsumeTutorialToLobby();
        SetPhase(LobbyFlowPhase.Arrival);
        StartCoroutine(SequenceArrival());
    }

    void OnDisable()
    {
        SetLobbyInputLocked(false);
    }

    IEnumerator SequenceArrival()
    {
        SetLobbyInputLocked(lockInputDuringArrivalIntro);
        bool skipEncounter = ShouldSkipDroneEncounter();

        if (_fromTutorialTransition)
        {
            yield return StartCoroutine(PlayDialogue("EGO", "\uc2dc\ubbac\ub808\uc774\uc158 \ub9c1\ud06c \ud574\uc81c. \uc2e4\uc804 \uc804\ud22c\ub97c \uc2dc\uc791\ud55c\ub2e4.", 2.1f));
            yield return StartCoroutine(PlayDialogue("ChuOn", "\uc54c\uaca0\uc5b4. \ubc14\ub85c \ud22c\uc785\ud55c\ub2e4.", 1.7f));
        }

        yield return StartCoroutine(PlayDialogue("ChuOn", "\uc5ec\uae30\uac00 \uce68\ud22c \uc9c0\uc810\uc778\uac00.", 2f));

        if (skipEncounter)
        {
            yield return StartCoroutine(PlayDialogue("EGO", "\ub85c\ube44 \ubcf4\uc548 \uc2e0\ud638\ub294 \uc774\ubbf8 \uc815\ub9ac\ub410\uc5b4. \uc2b9\uac15\uae30\ub85c \uac00.", 1.8f));
        }
        else
        {
            if (alertOverlay != null)
                alertOverlay.DOFade(0.3f, 0.5f).SetLoops(6, LoopType.Yoyo);

            yield return StartCoroutine(PlayDialogue("System", "\uce68\uc785\uc790 \uac10\uc9c0. \uc804\ud22c \ud504\ub85c\ud1a0\ucf5c \uae30\ub3d9.", 3f));
            yield return StartCoroutine(PlayDialogue("EGO", "\uc6b0\ud68c \uacbd\ub85c\ub294 \uc5c6\uc5b4. \uba3c\uc800 \uc815\ub9ac\ud558\uace0 \uc774\ub3d9\ud574.", 2.5f));
            yield return StartCoroutine(PlayDialogue("ChuOn", "\uc88b\uc544. \ubc00\uace0 \ub098\uac04\ub2e4.", 2f));
        }

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        SetLobbyInputLocked(false);

        if (skipEncounter)
            StartCoroutine(SequenceRouteGuide(false));
        else
            StartCoroutine(SequenceCombat());
    }

    IEnumerator SequenceCombat()
    {
        SetLobbyInputLocked(false);
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

            PrewarmLobbyProjectilePool();
            yield return StartCoroutine(SpawnLobbyDrones(playerTransform));
        }
        else
        {
            _totalEnemyCount = 1;
            OnEnemyKilled();
        }

        UpdateQuestUI("\uad50\uc804 \uac1c\uc2dc", $"\uc801 \uc81c\uac70 ({_currentDeadEnemy} / {_totalEnemyCount})");
        yield return new WaitUntil(() => _currentDeadEnemy >= _totalEnemyCount);
        yield return new WaitForSeconds(1f);
        CombatCompleted?.Invoke();
        s_droneEncounterClearedThisSession = true;
        StartCoroutine(SequencePostCombat());
    }

    public void OnEnemyKilled()
    {
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
            Transform spawnPoint = spawnPoints[i];
            if (spawnPoint == null)
                continue;

            GameObject drone = Instantiate(dronePrefab, spawnPoint.position, spawnPoint.rotation);
            PrepareLobbyDrone(drone);
            DroneController controller = drone.GetComponent<DroneController>();
            if (controller != null)
            {
                DroneCombatRole role = i < rolePlan.Length ? rolePlan[i] : DroneCombatRole.Standard;
                controller.SetCombatRole(role);
                controller.target = playerTransform;
                controller.SetCollisionAwareMovement(false);
                controller.SetLightweightSimulation(useLightweightLobbyDroneSimulation, lightweightLobbyDroneTickInterval);
                controller.SetAttackTelegraph(droneAttackTelegraphLeadTime);
                controller.SetAerialCombatMotion(useAerialLobbyDroneMotion, lobbyDroneHoverAmplitude, lobbyDroneHoverFrequency);
                controller.SetCombatMovementProfile(lobbyDroneMoveSpeedMultiplier, lobbyDroneDistanceMultiplier, lobbyDroneStrafeBoost);
                controller.enabled = true;
                controller.SetInitialFireDelay(ResolveLobbyDroneInitialFireDelay(i, role));
                RegisterActiveLobbyDrone(controller);
                DroneSpawned?.Invoke(controller);
            }

            if (drone.GetComponent<LobbyEnemy>() == null)
                drone.AddComponent<LobbyEnemy>();

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

    void SimplifyLobbyDroneVisual(GameObject drone)
    {
        Transform simpleVisual = drone.transform.Find("__SimpleDroneVisual");
        if (simpleVisual == null)
        {
            GameObject simpleVisualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            simpleVisualGo.name = "__SimpleDroneVisual";
            simpleVisualGo.layer = drone.layer;

            Transform simpleTransform = simpleVisualGo.transform;
            simpleTransform.SetParent(drone.transform, false);
            simpleTransform.localPosition = Vector3.zero;
            simpleTransform.localRotation = Quaternion.identity;
            simpleTransform.localScale = new Vector3(0.9f, 0.55f, 0.9f);
            simpleVisual = simpleTransform;

            Collider simpleCollider = simpleVisualGo.GetComponent<Collider>();
            if (simpleCollider != null)
                Destroy(simpleCollider);
        }

        Renderer simpleRenderer = simpleVisual.GetComponent<Renderer>();
        if (simpleRenderer != null)
        {
            simpleRenderer.enabled = true;
            simpleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            simpleRenderer.receiveShadows = false;
            simpleRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Material material = ResolveSimpleDroneMaterial();
            if (material != null)
                simpleRenderer.sharedMaterial = material;
        }

        Renderer[] renderers = drone.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer == simpleRenderer)
                continue;

            renderer.enabled = false;

            Animator childAnimator = renderer.GetComponentInParent<Animator>();
            if (childAnimator != null)
                childAnimator.enabled = false;
        }

        DroneController controller = drone.GetComponent<DroneController>();
        if (controller != null)
            controller.droneRenderer = simpleRenderer;

        BoxCollider hitCollider = drone.GetComponent<BoxCollider>();
        if (hitCollider != null)
        {
            hitCollider.enabled = true;
            hitCollider.isTrigger = false;
            hitCollider.center = Vector3.zero;
            hitCollider.size = new Vector3(1.2f, 0.9f, 1.2f);
        }
    }

    static Material ResolveSimpleDroneMaterial()
    {
        if (_simpleDroneMaterial != null)
            return _simpleDroneMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;

        _simpleDroneMaterial = new Material(shader)
        {
            name = "LobbyTempDroneCubeMaterial",
            color = new Color(0.75f, 0.9f, 1f, 1f)
        };
        return _simpleDroneMaterial;
    }

    static Material _simpleDroneMaterial;

    IEnumerator SequencePostCombat()
    {
        yield return StartCoroutine(SequenceRouteGuide(true));
    }

    IEnumerator SequenceRouteGuide(bool fromCombat)
    {
        SetLobbyInputLocked(lockInputDuringPostCombatBrief);
        SetPhase(LobbyFlowPhase.Route);
        UpdateQuestUI(
            fromCombat ? "\uad50\uc804 \uc885\ub8cc" : "\uacbd\ub85c \ud655\uc778",
            fromCombat ? "\ubaa8\ub4e0 \uc801 \uc2e0\ud638 \uc81c\uac70." : "\uc2b9\uac15\uae30 \uacbd\ub85c\uac00 \uc774\ubbf8 \uc5f4\ub824 \uc788\ub2e4.");
        _isDoorReached = false;

        yield return new WaitForSeconds(0.5f);
        if (fromCombat)
        {
            yield return StartCoroutine(PlayDialogue("EGO", "\uc88b\uc544. \uacbd\ub85c\uac00 \uc5f4\ub838\uc5b4. \uc2b9\uac15\uae30\ub85c \uc774\ub3d9\ud574.", 2f));
            yield return StartCoroutine(PlayDialogue("ChuOn", "\ub05d\uae4c\uc9c0 \uae34\uc7a5 \ud480\uc9c0 \ub9c8.", 1.5f));
            yield return StartCoroutine(PlayDialogue("EGO", routeSuspicionLine, routeSuspicionDelay));
        }
        else
        {
            yield return StartCoroutine(PlayDialogue("EGO", "\ubcf4\uc548 \uc815\ub9ac \uc644\ub8cc. \uc2b9\uac15\uae30 \ub9c1\ud06c\ub85c \uc774\ub3d9\ud574.", 1.8f));
        }

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("\uc774\ub3d9", "\ubcf5\ub3c4 \ub05d \uc2b9\uac15\uae30\ub85c \uc774\ub3d9\ud574.");

        if (guideSystem != null)
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

        SetLobbyInputLocked(false);
        yield return new WaitUntil(() => _isDoorReached);
        StartCoroutine(SequenceAtElevator());
    }

    bool ShouldSkipDroneEncounter()
    {
        return skipDroneEncounterAfterFirstClear && s_droneEncounterClearedThisSession;
    }

    void ApplyPlayerStartFacingYawOnce()
    {
        if (_startFacingApplied || !applyPlayerStartFacingYaw || Mathf.Abs(playerStartYawOffset) <= 0.001f)
            return;

        Transform facingRoot = ResolvePlayerFacingRoot();
        if (facingRoot == null)
            return;

        facingRoot.rotation = ResolveStartFacingRotation(facingRoot.rotation);
        _startFacingApplied = true;
    }

    IEnumerator CoApplyPlayerStartFacingYawDelayed()
    {
        ApplyPlayerStartFacingYawOnce();

        if (_startFacingApplied)
            yield break;

        yield return null;
        ApplyPlayerStartFacingYawOnce();
    }

    Transform ResolvePlayerFacingRoot()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return null;

        PlayerReferences references = player.GetComponent<PlayerReferences>();
        if (references != null && references.PlayerRoot != null)
            return references.PlayerRoot;

        return player.transform;
    }

    Quaternion ResolveStartFacingRotation(Quaternion fallbackRotation)
    {
        if (faceCameraForwardOnStart)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 forward = mainCamera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    return Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        return Quaternion.AngleAxis(playerStartYawOffset, Vector3.up) * fallbackRotation;
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
        if (guideSystem == null)
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
    }

    IEnumerator SequenceAtElevator()
    {
        SetLobbyInputLocked(lockInputDuringElevatorBrief);
        SetPhase(LobbyFlowPhase.Board);
        OpenDoor(doorLeft);
        OpenDoor(doorRight);

        yield return StartCoroutine(PlayDialogue("ChuOn", "\ud3c9\ubc94\ud55c \uc2b9\uac15\uae30\ub85c\ub294 \uc548 \ubcf4\uc774\ub124.", 2f));
        yield return StartCoroutine(PlayDialogue("EGO", "\uadf8\ub798\ub3c4 \uc9c0\uae08\uc740 \uc774 \uae38\ubfd0\uc774\uc57c. \uc548\uc73c\ub85c \ub4e4\uc5b4\uac00.", 2f));

        if (dialogueGroup != null)
            dialogueGroup.DOFade(0f, 0.5f);

        UpdateQuestUI("\ud0d1\uc2b9", "\uc2b9\uac15\uae30 \uc548\uc73c\ub85c \ub4e4\uc5b4\uac00.");

        if (guideSystem != null && triggerBoard != null)
            guideSystem.ShowPath(triggerBoard);

        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);

        _isBoarded = false;
        SetLobbyInputLocked(false);
        yield return new WaitUntil(() => _isBoarded);

        if (guideSystem != null)
            guideSystem.HidePath();

        StartCoroutine(CoBoardElevatorTransition());
    }

    void OpenDoor(Transform door)
    {
        if (door == null)
            return;

        CacheDoorClosedPoseIfNeeded();

        Collider collider = door.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        Vector3 closedPosition = GetClosedDoorLocalPosition(door);
        door.DOLocalMoveY(closedPosition.y + doorOpenHeight, doorOpenSpeed).SetEase(Ease.OutQuad);
    }

    void CloseDoor(Transform door)
    {
        if (door == null)
            return;

        CacheDoorClosedPoseIfNeeded();

        Vector3 closedPosition = GetClosedDoorLocalPosition(door);
        door.DOKill();
        door.DOLocalMoveY(closedPosition.y, doorCloseSpeed).SetEase(Ease.InOutQuad);

        Collider collider = door.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = false;
    }

    void CacheDoorClosedPoseIfNeeded()
    {
        if (_doorPoseCached)
            return;

        _doorLeftClosedLocalPosition = doorLeft != null ? doorLeft.localPosition : Vector3.zero;
        _doorRightClosedLocalPosition = doorRight != null ? doorRight.localPosition : Vector3.zero;
        _doorPoseCached = true;
    }

    Vector3 GetClosedDoorLocalPosition(Transform door)
    {
        if (door == doorLeft)
            return _doorLeftClosedLocalPosition;

        if (door == doorRight)
            return _doorRightClosedLocalPosition;

        return door != null ? door.localPosition : Vector3.zero;
    }

    Transform ResolveElevatorCabinRoot()
    {
        if (elevatorCabinRoot != null)
            return elevatorCabinRoot;

        if (doorLeft != null && doorRight != null)
        {
            Transform common = FindCommonAncestor(doorLeft, doorRight);
            if (common != null)
                return common;
        }

        if (doorLeft != null && doorLeft.parent != null)
            return doorLeft.parent;

        if (doorRight != null && doorRight.parent != null)
            return doorRight.parent;

        return triggerBoard != null ? triggerBoard : null;
    }

    static Transform FindCommonAncestor(Transform a, Transform b)
    {
        if (a == null || b == null)
            return null;

        Transform cursor = a;
        while (cursor != null)
        {
            if (b.IsChildOf(cursor))
                return cursor;

            cursor = cursor.parent;
        }

        return null;
    }

    Transform ResolvePlayerSceneRootTransform()
    {
        GameObject player = GameObject.FindWithTag("Player");
        return player != null ? player.transform : null;
    }

    public bool OnEnterElevator()
    {
        if (_isElevatorTransitioning)
            return false;

        if (_isBoarded)
            return true;

        if (!IsPlayerAtElevatorBoardCenter())
            return false;

        _isBoarded = true;

        if (presentationController != null)
            presentationController.HideObjectiveMarker();

        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);

        return true;
    }

    bool IsPlayerAtElevatorBoardCenter()
    {
        if (!requireBoardCenterBeforeTransition || triggerBoard == null)
            return true;

        Transform playerRoot = ResolvePlayerSceneRootTransform();
        if (playerRoot == null)
            return false;

        Vector3 center = triggerBoard.TransformPoint(boardCenterLocalOffset);
        Vector3 toPlayer = playerRoot.position - center;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude <= boardCenterRequiredRadius * boardCenterRequiredRadius;
    }

    public void OnInteractElevator()
    {
        if (_isElevatorTransitioning)
            return;

        StartCoroutine(CoBoardElevatorTransition());
    }

    IEnumerator CoBoardElevatorTransition()
    {
        if (_isElevatorTransitioning)
            yield break;

        _isElevatorTransitioning = true;
        SetLobbyInputLocked(true);
        SetPhase(LobbyFlowPhase.Elevator);

        AsyncOperation sceneLoadOperation = null;
        if (!string.IsNullOrWhiteSpace(nextSceneName))
        {
            sceneLoadOperation = SceneManager.LoadSceneAsync(nextSceneName);
            if (sceneLoadOperation != null)
                sceneLoadOperation.allowSceneActivation = false;
        }

        if (presentationController != null)
        {
            presentationController.HideObjectiveMarker();
            presentationController.HideElevatorPanelGuide();
        }

        if (elevatorPanel != null)
            elevatorPanel.SetActive(false);

        UpdateQuestUI("\uc2b9\uac15\uae30 \ud558\uac15", "\ubcf4\uc2a4 \uad6c\uc5ed\uc73c\ub85c \uc774\ub3d9 \uc911.");
        ShowTransientQuestCue(elevatorSyncCueTitle, "\uc2b9\uac15\uae30 \uc911\uc559 \ud0d1\uc2b9 \ud655\uc778. \ud558\uac15\uc744 \uc2dc\uc791\ud55c\ub2e4.", elevatorSyncCueHold, false);

        CloseDoor(doorLeft);
        CloseDoor(doorRight);
        yield return new WaitForSeconds(Mathf.Max(0.05f, doorCloseSpeed));

        Transform cabinRoot = ResolveElevatorCabinRoot();
        Transform playerRoot = ResolvePlayerSceneRootTransform();
        Transform originalPlayerParent = null;

        if (parentPlayerToElevatorDuringDescent && cabinRoot != null && playerRoot != null && !playerRoot.IsChildOf(cabinRoot))
        {
            originalPlayerParent = playerRoot.parent;
            playerRoot.SetParent(cabinRoot, true);
        }

        if (cabinRoot != null && elevatorLowerDistance > 0f)
        {
            cabinRoot.DOKill();
            Vector3 targetPosition = cabinRoot.position + Vector3.down * elevatorLowerDistance;
            cabinRoot.DOMove(targetPosition, elevatorLowerDuration).SetEase(Ease.InOutSine);
        }

        if (elevatorFadeDelay > 0f)
            yield return new WaitForSeconds(elevatorFadeDelay);

        TutorialSceneTransitionState.MarkLobbyToMain();

        if (SceneFader.Instance != null)
        {
            bool fadeComplete = false;
            SceneFader.Instance.FadeOut(() => fadeComplete = true);
            yield return new WaitUntil(() => fadeComplete);
        }
        else
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, elevatorLowerDuration - elevatorFadeDelay));
        }

        while (sceneLoadOperation != null && sceneLoadOperation.progress < 0.9f)
            yield return null;

        if (originalPlayerParent != null && playerRoot != null)
            playerRoot.SetParent(originalPlayerParent, true);

        if (sceneLoadOperation != null)
        {
            sceneLoadOperation.allowSceneActivation = true;
            yield break;
        }

        SceneManager.LoadScene(nextSceneName);
    }

    IEnumerator PlayDialogue(string speaker, string content, float waitTime)
    {
        if (!ExhibitionPrototypePresentationPolicy.DialogueEnabled)
        {
            if (dialogueGroup != null)
            {
                dialogueGroup.alpha = 0f;
                dialogueGroup.interactable = false;
                dialogueGroup.blocksRaycasts = false;
                dialogueGroup.gameObject.SetActive(false);
            }

            if (speakerText != null)
                speakerText.text = string.Empty;

            if (contentText != null)
                contentText.text = string.Empty;

            yield break;
        }

        if (dialogueGroup != null)
        {
            dialogueGroup.gameObject.SetActive(true);
            dialogueGroup.alpha = 1f;
        }

        if (speakerText != null)
            speakerText.text = speaker;

        if (contentText != null)
        {
            contentText.text = string.Empty;
            for (int i = 0; i < content.Length; i++)
            {
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
        _currentQuestTitle = title;
        _currentQuestDescription = desc;

        if (questTitleText != null)
            questTitleText.text = title;
        if (questDescriptionText != null)
            questDescriptionText.text = desc;

        QuestUpdated?.Invoke(title, desc);
    }

    void ShowQuestPanel()
    {
        if (questPanelGroup == null)
            return;

        if (!ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
        {
            questPanelGroup.alpha = 0f;
            questPanelGroup.interactable = false;
            questPanelGroup.blocksRaycasts = false;
            return;
        }

        if (_objectivePanelController != null)
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
        if (!ExhibitionPrototypePresentationPolicy.RuntimeSupportPanelEnabled)
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

    void ConfigureCombatCoachController()
    {
        if (_combatCoachController == null || !ExhibitionPrototypePresentationPolicy.RuntimeCoachFeedbackEnabled)
            return;

        GameObject player = GameObject.FindWithTag("Player");
        PlayerHealth playerHealth = player != null ? player.GetComponent<PlayerHealth>() : null;
        _combatCoachController.ConfigureRuntime(this, playerHealth, presentationController);
    }

    void ConfigureObjectivePanelController()
    {
        if (_objectivePanelController == null || !ExhibitionPrototypePresentationPolicy.RuntimeObjectivePanelEnabled)
            return;

        _objectivePanelController.ConfigureRuntime(this);
    }

    void SetLobbyInputLocked(bool locked)
    {
        if (_lobbyInputLocked == locked)
            return;

        IInputBlocker inputBlocker = ResolvePlayerInputBlocker();
        if (inputBlocker == null)
            return;

        inputBlocker.BlockAll(locked);
        _lobbyInputLocked = locked;
    }

    IInputBlocker ResolvePlayerInputBlocker()
    {
        if (_playerInputBlocker != null)
        {
            UnityEngine.Object cachedObject = _playerInputBlocker as UnityEngine.Object;
            if (cachedObject != null)
                return _playerInputBlocker;

            _playerInputBlocker = null;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            return null;

        _playerInputBlocker = player.GetComponent<IInputBlocker>();
        if (_playerInputBlocker == null)
            _playerInputBlocker = player.AddComponent<SimpleInputBlocker>();

        return _playerInputBlocker;
    }

    IEnumerator CoShowTransientQuestCue(string title, string description, float holdDuration, bool flashAlert)
    {
        ShowQuestPanel();

        if (_objectivePanelController != null)
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
            alertOverlay.DOFade(0.24f, 0.14f).SetLoops(2, LoopType.Yoyo);

        yield return new WaitForSeconds(Mathf.Max(0.25f, holdDuration));

        if (_objectivePanelController != null)
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

    void SetPhase(LobbyFlowPhase phase)
    {
        _currentPhase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
