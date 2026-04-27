using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class MainSceneArrivalController : MonoBehaviour
{
    [SerializeField] private PlayerHUD playerHud;
    [SerializeField] private BossController bossController;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BossBreakController bossBreakController;
    [SerializeField] private PlayerUltimateController playerUltimateController;

    [Header("Arrival Sequence")]
    [SerializeField] private string arrivalMessage = "\uc2e4\uc804 \uad6c\uac04 \uc9c4\uc785";
    [SerializeField] private string threatMessage = "\uace0\uc704\ud5d8 \ubc18\uc751 \uac10\uc9c0";
    [SerializeField] private string egoMessage = "EGO SYNC";
    [SerializeField, Min(0.1f)] private float arrivalDelay = 0.45f;
    [SerializeField, Min(0.1f)] private float arrivalHold = 0.72f;
    [SerializeField, Min(0.1f)] private float threatHold = 0.82f;
    [SerializeField, Min(0.05f)] private float betweenDelay = 0.26f;
    [SerializeField] private AttackTelegraphType arrivalTelegraphType = AttackTelegraphType.Guard;
    [SerializeField] private AttackTelegraphType threatTelegraphType = AttackTelegraphType.Danger;

    [Header("Elevator Arrival")]
    [SerializeField] bool playElevatorArrivalOnSceneStart = true;
    [SerializeField] Transform elevatorArrivalAnchor;
    [SerializeField] GameObject closedElevatorDoorVisual;
    [SerializeField] GameObject openElevatorDoorVisual;
    [SerializeField] bool useExplicitElevatorArrivalPosition = true;
    [SerializeField] Vector3 elevatorArrivalWorldPosition = new Vector3(0f, 0f, -22f);
    [SerializeField] Vector3 elevatorArrivalWorldOffset = Vector3.zero;
    [SerializeField] bool useExplicitElevatorArrivalRotation = true;
    [SerializeField] bool faceBossOnElevatorArrival = true;
    [SerializeField] float elevatorArrivalYaw = 0f;
    [SerializeField] bool snapCameraBehindPlayerOnElevatorArrival = true;
    [SerializeField] float elevatorArrivalCameraPitch = 8f;
    [SerializeField] float elevatorArrivalCameraYawOffset = 180f;
    [SerializeField, Min(0.5f)] float elevatorArrivalCameraDistance = 4.2f;
    [SerializeField] float elevatorArrivalCameraHeight = 1.25f;
    [SerializeField, Min(0f)] float elevatorDoorClosedHold = 0.9f;
    [SerializeField, Min(0f)] float elevatorDoorOpenHold = 0.75f;
    [SerializeField] string elevatorArrivedMessage = "\uc2b9\uac15\uae30 \uc815\ucc29";
    [SerializeField] string elevatorDoorOpenMessage = "\uc804\ud22c \uad6c\uc5ed \uc811\uc18d";

    [Header("Start Facing")]
    [SerializeField] bool applyPlayerStartFacingYaw = true;
    [SerializeField] bool faceCameraForwardOnStart = true;
    [SerializeField] float playerStartYawOffset = 180f;

    [Header("Encounter Coach")]
    [SerializeField] private string firstDefenseCoachMessage = "\uccab \uad50\ud658\uc740 \uc0b4\uc544\ub0a8\ub294 \ucabd\uc774 \uc6b0\uc120\uc774\uc57c";
    [SerializeField] private string firstPunishCoachMessage = "\ube48\ud2c8\uc774 \uc5f4\ub838\ub2e4. \uc9e7\uac8c \ub123\uace0 \ube60\uc838";
    [SerializeField] private string firstDamageCoachMessage = "\ubb34\ub9ac\ud558\uc9c0 \ub9c8. \ub2e4\uc74c \ud328\ud134\uc744 \uba3c\uc800 \uc77d\uc5b4";
    [SerializeField] private string phaseTwoCoachMessage = "\ud398\uc774\uc988 2. \uacf5\uc138\uac00 \ube68\ub77c\uc9c4\ub2e4. \uc751\uc218 \ud6c4 \uc9e7\uac8c";
    [SerializeField] private string phaseThreeCoachMessage = "\ud398\uc774\uc988 3. \ubc84\uc11c\ud06c \uad6c\uac04\uc774\uc57c. \ud328\ud134\uc744 \ub05d\uae4c\uc9c0 \ubd10";
    [SerializeField] private string breakCoachMessage = "\ube0c\ub808\uc774\ud06c\ub2e4. \uc9e7\uac8c \ubab0\uc544\uce58\uace0 \uad81\uadf9\uae30\ub97c \uacb9\uccd0";
    [SerializeField] private string ultimateReadyMessage = "\uad81\uadf9\uae30 \uc900\ube44 \uc644\ub8cc. \ube0c\ub808\uc774\ud06c \ud0c0\uc774\ubc0d\uc5d0 \ub9de\ucdb0";
    [SerializeField, Min(0.05f)] private float coachDelayAfterTelegraph = 0.14f;
    [SerializeField, Min(0.1f)] private float coachHold = 0.72f;
    [SerializeField, Min(0.05f)] private float phaseCoachDelay = 0.48f;
    [SerializeField, Min(0.1f)] private float breakCoachHold = 0.84f;
    [SerializeField, Min(0.1f)] private float ultimateReadyHold = 0.78f;
    Coroutine _arrivalRoutine;
    bool _fromLobbyTransition;
    bool _subscribed;
    bool _firstDefenseCoachShown;
    bool _firstPunishCoachShown;
    bool _firstDamageCoachShown;
    bool _phaseTwoCoachShown;
    bool _phaseThreeCoachShown;
    bool _breakCoachShown;
    bool _ultimateReadyCoachShown;
    bool _startFacingApplied;
    IInputBlocker _inputBlocker;
    bool _arrivalInputLocked;

    public bool IsLobbyTransitionActive => _fromLobbyTransition;

    public void ConfigureRuntime(
        PlayerHUD runtimeHud,
        BossController runtimeBoss,
        PlayerHealth runtimePlayerHealth,
        BossBreakController runtimeBossBreakController,
        PlayerUltimateController runtimePlayerUltimateController)
    {
        playerHud = runtimeHud;
        bossController = runtimeBoss;
        playerHealth = runtimePlayerHealth;
        bossBreakController = runtimeBossBreakController;
        playerUltimateController = runtimePlayerUltimateController;
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        bool enteredFromLobby = TutorialSceneTransitionState.ConsumeLobbyToMain();
        _fromLobbyTransition = enteredFromLobby || playElevatorArrivalOnSceneStart;

        _firstDefenseCoachShown = false;
        _firstPunishCoachShown = false;
        _firstDamageCoachShown = false;
        _phaseTwoCoachShown = false;
        _phaseThreeCoachShown = false;
        _breakCoachShown = false;
        _ultimateReadyCoachShown = false;

        if (_arrivalRoutine != null)
            StopCoroutine(_arrivalRoutine);

        RefreshSubscriptions();
        if (playElevatorArrivalOnSceneStart || enteredFromLobby)
            _arrivalRoutine = StartCoroutine(CoPlayArrivalSequence());
        else
            StartCoroutine(CoApplyPlayerStartFacingYawDelayed());
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        SetArrivalInputLocked(false);

        if (_arrivalRoutine == null)
            return;

        StopCoroutine(_arrivalRoutine);
        _arrivalRoutine = null;
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    IEnumerator CoPlayArrivalSequence()
    {
        if (playerHud == null)
            playerHud = FindObjectOfType<PlayerHUD>(true);

        if (bossController == null)
            bossController = FindObjectOfType<BossController>(true);

        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>(true);

        if (bossBreakController == null && bossController != null)
            bossBreakController = bossController.GetComponent<BossBreakController>();
        if (bossBreakController == null)
            bossBreakController = FindObjectOfType<BossBreakController>(true);

        if (playerUltimateController == null)
            playerUltimateController = FindObjectOfType<PlayerUltimateController>(true);

        RefreshSubscriptions();

        if (playerHud == null)
            yield break;

        if (playElevatorArrivalOnSceneStart)
            yield return StartCoroutine(CoPlayElevatorArrivalIntro());

        if (arrivalDelay > 0f)
            yield return new WaitForSeconds(arrivalDelay);

        playerHud.ShowRuntimeTelegraphMessage(arrivalMessage, arrivalTelegraphType, arrivalHold, false);

        if (betweenDelay > 0f)
            yield return new WaitForSeconds(betweenDelay);

        playerHud.ShowRuntimeTelegraphMessage(egoMessage, AttackTelegraphType.Parry, 0.56f, false);

        if (bossController != null)
        {
            if (betweenDelay > 0f)
                yield return new WaitForSeconds(betweenDelay);

            playerHud.ShowRuntimeTelegraphMessage(threatMessage, threatTelegraphType, threatHold, true);
        }

        SetArrivalInputLocked(false);
        _arrivalRoutine = null;
    }

    IEnumerator CoPlayElevatorArrivalIntro()
    {
        SetArrivalInputLocked(true);
        CacheElevatorArrivalReferences();
        PlacePlayerAtElevatorArrival();

        if (closedElevatorDoorVisual != null)
            closedElevatorDoorVisual.SetActive(true);
        if (openElevatorDoorVisual != null)
            openElevatorDoorVisual.SetActive(false);

        if (!string.IsNullOrWhiteSpace(elevatorArrivedMessage))
            playerHud.ShowRuntimeTelegraphMessage(elevatorArrivedMessage, AttackTelegraphType.Guard, Mathf.Max(0.4f, elevatorDoorClosedHold), false);

        if (elevatorDoorClosedHold > 0f)
            yield return new WaitForSeconds(elevatorDoorClosedHold);

        if (closedElevatorDoorVisual != null)
            closedElevatorDoorVisual.SetActive(false);
        if (openElevatorDoorVisual != null)
            openElevatorDoorVisual.SetActive(true);

        if (!string.IsNullOrWhiteSpace(elevatorDoorOpenMessage))
            playerHud.ShowRuntimeTelegraphMessage(elevatorDoorOpenMessage, AttackTelegraphType.Parry, Mathf.Max(0.4f, elevatorDoorOpenHold), false);

        if (elevatorDoorOpenHold > 0f)
            yield return new WaitForSeconds(elevatorDoorOpenHold);
    }

    void CacheElevatorArrivalReferences()
    {
        if (elevatorArrivalAnchor == null)
        {
            GameObject elevator = GameObject.Find("Elevator");
            if (elevator != null)
                elevatorArrivalAnchor = elevator.transform;
        }

        if (closedElevatorDoorVisual == null)
            closedElevatorDoorVisual = GameObject.Find("closedoor (1)");

        if (openElevatorDoorVisual == null)
            openElevatorDoorVisual = GameObject.Find("opendoor (1)");
    }

    void PlacePlayerAtElevatorArrival()
    {
        Transform playerRoot = ResolvePlayerFacingRoot();
        if (playerRoot == null)
            return;

        Vector3 basePosition = ResolveElevatorArrivalBasePosition(playerRoot.position) + elevatorArrivalWorldOffset;

        Quaternion rotation = ResolveElevatorArrivalRotation(playerRoot);
        SetPlayerWorldPose(playerRoot, basePosition, rotation);
        SnapCameraBehindPlayer(playerRoot, true);
        _startFacingApplied = true;
    }

    IEnumerator CoSnapCameraBehindPlayerDelayed()
    {
        yield return null;
        SnapCameraBehindPlayer(ResolvePlayerFacingRoot(), false);

        yield return new WaitForEndOfFrame();
        SnapCameraBehindPlayer(ResolvePlayerFacingRoot(), false);
    }

    void SnapCameraBehindPlayer(Transform playerRoot, bool scheduleSecondSnap)
    {
        if (!snapCameraBehindPlayerOnElevatorArrival || playerRoot == null)
            return;

        FreeLookCamera freeLookCamera = FindObjectOfType<FreeLookCamera>(true);
        if (freeLookCamera != null)
        {
            freeLookCamera.player = playerRoot;
            freeLookCamera.distance = elevatorArrivalCameraDistance;
            freeLookCamera.height = elevatorArrivalCameraHeight;
            freeLookCamera.SnapBehindPlayer(elevatorArrivalCameraPitch, elevatorArrivalCameraYawOffset);
            if (scheduleSecondSnap)
                StartCoroutine(CoSnapCameraBehindPlayerDelayed());
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Quaternion yawRotation = Quaternion.Euler(0f, playerRoot.eulerAngles.y + elevatorArrivalCameraYawOffset, 0f);
        Vector3 focusPoint = playerRoot.position + Vector3.up;
        Vector3 cameraPosition = playerRoot.position + yawRotation * new Vector3(0f, 0f, -elevatorArrivalCameraDistance) + Vector3.up * elevatorArrivalCameraHeight;
        mainCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(focusPoint - cameraPosition, Vector3.up));

        if (scheduleSecondSnap)
            StartCoroutine(CoSnapCameraBehindPlayerDelayed());
    }

    Quaternion ResolveElevatorArrivalRotation(Transform playerRoot)
    {
        if (faceBossOnElevatorArrival)
        {
            Transform bossRoot = ResolveBossRoot();
            if (bossRoot != null && playerRoot != null)
            {
                Vector3 toBoss = bossRoot.position - playerRoot.position;
                toBoss.y = 0f;
                if (toBoss.sqrMagnitude > 0.0001f)
                    return Quaternion.LookRotation(toBoss.normalized, Vector3.up);
            }
        }

        return useExplicitElevatorArrivalRotation
            ? Quaternion.Euler(0f, elevatorArrivalYaw, 0f)
            : ResolveStartFacingRotation(playerRoot != null ? playerRoot.rotation : Quaternion.identity);
    }

    Transform ResolveBossRoot()
    {
        if (bossController != null)
            return bossController.transform;

        BossController runtimeBoss = FindObjectOfType<BossController>(true);
        return runtimeBoss != null ? runtimeBoss.transform : null;
    }

    Vector3 ResolveElevatorArrivalBasePosition(Vector3 fallbackPosition)
    {
        if (useExplicitElevatorArrivalPosition)
            return elevatorArrivalWorldPosition;

        if (TryGetRendererBoundsCenter(closedElevatorDoorVisual, out Vector3 closedDoorCenter))
            return closedDoorCenter;

        if (TryGetRendererBoundsCenter(openElevatorDoorVisual, out Vector3 openDoorCenter))
            return openDoorCenter;

        if (closedElevatorDoorVisual != null)
            return closedElevatorDoorVisual.transform.position;

        if (openElevatorDoorVisual != null)
            return openElevatorDoorVisual.transform.position;

        if (elevatorArrivalAnchor != null)
            return elevatorArrivalAnchor.position;

        return fallbackPosition;
    }

    static bool TryGetRendererBoundsCenter(GameObject target, out Vector3 center)
    {
        center = Vector3.zero;
        if (target == null)
            return false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        Bounds bounds = default;
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        if (!hasBounds)
            return false;

        center = bounds.center;
        return true;
    }

    static void SetPlayerWorldPose(Transform playerRoot, Vector3 position, Quaternion rotation)
    {
        CharacterController characterController = playerRoot.GetComponent<CharacterController>();
        bool restoreCharacterController = characterController != null && characterController.enabled;
        if (restoreCharacterController)
            characterController.enabled = false;

        playerRoot.SetPositionAndRotation(position, rotation);

        if (restoreCharacterController)
            characterController.enabled = true;
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
        if (playerHealth != null)
            return playerHealth.transform;

        PlayerHealth runtimeHealth = FindObjectOfType<PlayerHealth>(true);
        if (runtimeHealth != null)
            return runtimeHealth.transform;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            PlayerReferences runtimeReferences = FindObjectOfType<PlayerReferences>(true);
            if (runtimeReferences != null)
                return runtimeReferences.PlayerRoot != null ? runtimeReferences.PlayerRoot : runtimeReferences.transform;

            return null;
        }

        PlayerReferences references = player.GetComponent<PlayerReferences>();
        if (references != null && references.PlayerRoot != null)
            return references.PlayerRoot;

        return player.transform;
    }

    void SetArrivalInputLocked(bool locked)
    {
        if (_arrivalInputLocked == locked)
            return;

        IInputBlocker inputBlocker = ResolveInputBlocker();
        if (inputBlocker == null)
            return;

        inputBlocker.BlockAll(locked);
        _arrivalInputLocked = locked;
    }

    IInputBlocker ResolveInputBlocker()
    {
        if (_inputBlocker != null)
        {
            UnityEngine.Object cachedObject = _inputBlocker as UnityEngine.Object;
            if (cachedObject != null)
                return _inputBlocker;

            _inputBlocker = null;
        }

        Transform playerRoot = ResolvePlayerFacingRoot();
        if (playerRoot == null)
            return null;

        _inputBlocker = playerRoot.GetComponent<IInputBlocker>();
        if (_inputBlocker == null)
            _inputBlocker = playerRoot.gameObject.AddComponent<SimpleInputBlocker>();

        return _inputBlocker;
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

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();

        if (!_fromLobbyTransition)
            return;

        if (bossController == null)
            bossController = FindObjectOfType<BossController>(true);

        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>(true);

        if (bossBreakController == null && bossController != null)
            bossBreakController = bossController.GetComponent<BossBreakController>();
        if (bossBreakController == null)
            bossBreakController = FindObjectOfType<BossBreakController>(true);

        if (playerUltimateController == null)
            playerUltimateController = FindObjectOfType<PlayerUltimateController>(true);

        if (bossController != null)
        {
            bossController.OnAttackTelegraph += HandleBossAttackTelegraph;
            bossController.OnPunishWindowOpened += HandlePunishWindowOpened;
            bossController.OnBossPhaseChanged += HandleBossPhaseChanged;
            _subscribed = true;
        }

        if (bossBreakController != null)
        {
            bossBreakController.OnBreakEnter.AddListener(HandleBossBreakEnter);
            _subscribed = true;
        }

        if (playerHealth != null)
        {
            playerHealth.OnDamaged += HandlePlayerDamaged;
            _subscribed = true;
        }

        if (playerUltimateController != null)
        {
            playerUltimateController.OnGaugeChanged += HandleUltimateGaugeChanged;
            playerUltimateController.OnUltimateStarted += HandleUltimateStarted;
            HandleUltimateGaugeChanged(
                playerUltimateController.Gauge,
                playerUltimateController.gaugeMax > 0f ? Mathf.Clamp01(playerUltimateController.Gauge / playerUltimateController.gaugeMax) : 0f,
                playerUltimateController.IsGaugeReady);
            _subscribed = true;
        }
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed)
            return;

        if (bossController != null)
        {
            bossController.OnAttackTelegraph -= HandleBossAttackTelegraph;
            bossController.OnPunishWindowOpened -= HandlePunishWindowOpened;
            bossController.OnBossPhaseChanged -= HandleBossPhaseChanged;
        }

        if (bossBreakController != null)
            bossBreakController.OnBreakEnter.RemoveListener(HandleBossBreakEnter);

        if (playerHealth != null)
            playerHealth.OnDamaged -= HandlePlayerDamaged;

        if (playerUltimateController != null)
        {
            playerUltimateController.OnGaugeChanged -= HandleUltimateGaugeChanged;
            playerUltimateController.OnUltimateStarted -= HandleUltimateStarted;
        }

        _subscribed = false;
    }

    void HandleBossAttackTelegraph(AttackTelegraphType telegraphType, float leadTime, string label)
    {
        if (!_fromLobbyTransition || _firstDefenseCoachShown || playerHud == null)
            return;

        _firstDefenseCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            ResolveDefenseCoachMessage(telegraphType),
            telegraphType,
            Mathf.Max(coachHold, leadTime),
            telegraphType == AttackTelegraphType.Danger,
            coachDelayAfterTelegraph));
    }

    void HandlePunishWindowOpened(float duration, float damageMultiplier, string patternName)
    {
        if (!_fromLobbyTransition || _firstPunishCoachShown || playerHud == null)
            return;

        _firstPunishCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            firstPunishCoachMessage,
            AttackTelegraphType.Parry,
            Mathf.Max(0.42f, duration),
            false,
            0.08f));
    }

    void HandlePlayerDamaged(int damage)
    {
        if (!_fromLobbyTransition || _firstDamageCoachShown || playerHud == null)
            return;

        _firstDamageCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            firstDamageCoachMessage,
            AttackTelegraphType.Guard,
            coachHold,
            false,
            0.08f));
    }

    void HandleBossPhaseChanged(int phase, float hpNormalized)
    {
        if (!_fromLobbyTransition || playerHud == null)
            return;

        if (phase == 2 && !_phaseTwoCoachShown)
        {
            _phaseTwoCoachShown = true;
            StartCoroutine(CoShowCoachMessage(
                phaseTwoCoachMessage,
                AttackTelegraphType.Guard,
                coachHold,
                false,
                phaseCoachDelay));
            return;
        }

        if (phase >= 3 && !_phaseThreeCoachShown)
        {
            _phaseThreeCoachShown = true;
            StartCoroutine(CoShowCoachMessage(
                phaseThreeCoachMessage,
                AttackTelegraphType.Danger,
                Mathf.Max(coachHold, 0.82f),
                true,
                phaseCoachDelay));
        }
    }

    void HandleBossBreakEnter()
    {
        if (!_fromLobbyTransition || _breakCoachShown || playerHud == null)
            return;

        _breakCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            breakCoachMessage,
            AttackTelegraphType.Parry,
            breakCoachHold,
            false,
            0.06f));
    }

    void HandleUltimateStarted()
    {
        _ultimateReadyCoachShown = true;
    }

    void HandleUltimateGaugeChanged(float gauge, float normalized, bool ready)
    {
        if (!_fromLobbyTransition || _ultimateReadyCoachShown || !ready || playerHud == null)
            return;

        _ultimateReadyCoachShown = true;
        playerHud.ShowRuntimeTelegraphMessage(
            ultimateReadyMessage,
            AttackTelegraphType.Danger,
            ultimateReadyHold,
            false);
    }

    IEnumerator CoShowCoachMessage(string message, AttackTelegraphType telegraphType, float hold, bool useDangerFeedback, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (playerHud != null)
            playerHud.ShowRuntimeTelegraphMessage(message, telegraphType, hold, useDangerFeedback);
    }

    string ResolveDefenseCoachMessage(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return "\ubc1b\uc544\ub0bc \uc218 \uc788\ub2e4. \ud0c0\uc774\ubc0d\uc744 \ub05d\uae4c\uc9c0 \ubd10";
            case AttackTelegraphType.Guard:
                return "\uc815\uba74\uc5d0\uc11c \ubc1b\uc544. \ub9ac\ub4ec\uc744 \ub04a\uc9c0 \ub9c8";
            case AttackTelegraphType.Danger:
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                return firstDefenseCoachMessage;
        }
    }
}
