using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

public enum UltimateActivationBlockReason
{
    None,
    AlreadyRunning,
    InputBlocked,
    GaugeNotReady,
    Staggered,
    Airborne,
    Attacking,
    Guarding,
    Dodging,
    TargetUnavailable,
    SequenceStartFailed
}

public class PlayerUltimateController : MonoBehaviour
{
    [Header("Gauge")]
    [Tooltip("Maximum ultimate gauge value.")]
    public float gaugeMax = 100f;
    [Tooltip("Gauge gained from normal attack hits.")]
    public float gaugePerH = 3f;
    [Tooltip("Gauge gained from successful parries.")]
    public float gaugePerParry = 6f;
    [Tooltip("Gauge gained from successful perfect dodges.")]
    public float gaugePerPerfectDodge = 16f;

    [Header("Ultimate Activation")]
    [Tooltip("Optional Input System action for ultimate activation.")]
    public InputActionReference activateAction;
    [Tooltip("Allow legacy hotkey fallback when no action is bound.")]
    public bool useLegacyHotkey = true;
    [Tooltip("Legacy fallback key for ultimate activation.")]
    public KeyCode legacyHotkey = KeyCode.R;
    [Tooltip("Ignore activation input while a global input blocker is active.")]
    public bool ignoreActivationWhenBlocked = true;

    [Header("Activation Rules")]
    [Tooltip("Allow activation while airborne.")]
    public bool allowInAir = false;
    [Tooltip("Block activation while staggered.")]
    public bool blockWhenStaggered = true;
    [Tooltip("Block activation while attacking.")]
    public bool blockWhenAttacking = true;
    [Tooltip("Block activation while guarding.")]
    public bool blockWhenGuarding = true;
    [Tooltip("Block activation while dodging.")]
    public bool blockWhenDodging = true;

    [Header("Cinematic State")]
    [Tooltip("Enable invulnerability during the cinematic.")]
    public bool invulnerableDuringCinematic = true;
    [Tooltip("Block player input during the cinematic.")]
    public bool lockInputDuringCinematic = true;
    [Tooltip("Restore lock-on camera control after the cinematic.")]
    public bool restoreCameraAndLockOn = true;
    [Tooltip("Freeze gameplay world time while the cinematic runs.")]
    public bool freezeWorldTimeDuringCinematic = true;

    [Header("Damage")]
    [Tooltip("Fixed damage applied on the finisher hit.")]
    public int finisherFixedDamage = 1200;
    [Tooltip("Fixed damage applied on each multi-hit slash.")]
    public int multihitFixedDamage = 120;

    [Header("Timeline")]
    [Tooltip("PlayableDirector used for the ultimate cinematic.")]
    public PlayableDirector director;
    [Tooltip("Optional virtual cameras used by the sequence.")]
    public CinemachineVirtualCameraBase[] vCams;
    [Tooltip("Prefer a dedicated runtime vcam so the ultimate always takes camera control.")]
    public bool preferDedicatedRuntimeSequenceCamera = true;
    [Tooltip("Use a dedicated runtime presentation stage for the scripted ultimate sequence.")]
    public bool useUltimateStageRuntime = true;
    [Tooltip("Optional runtime stage reference. If empty one is created automatically.")]
    public UltimateStageRuntime runtimeStage;

    [Header("Scripted Sequence")]
    [Tooltip("Use the built-in scripted ultimate sequence instead of relying on a populated timeline.")]
    public bool useScriptedSequence = true;
    [Tooltip("Allow falling back to the legacy PlayableDirector path when the scripted sequence cannot resolve a target.")]
    public bool allowDirectorFallbackWhenScriptedUnavailable = false;
    [Tooltip("Camera priority used while the scripted sequence is active.")]
    public int scriptedCameraPriority = 100;
    [Tooltip("Optional animator used for the ultimate presentation. Falls back to PlayerReferences.MainAnimator.")]
    public Animator scriptedAnimator;
    [Tooltip("Optional slash burst spawner used for the multihit section.")]
    public UltimateSlashBurstSpawner slashBurstSpawner;
    [Tooltip("Optional impact glass or burst effect on the final hit.")]
    public GameObject finisherImpactPrefab;
    [Tooltip("Point where the finishing impact effect is spawned.")]
    public Transform finisherImpactPoint;
    [Tooltip("Optional override for the sword focus point. Falls back to PlayerReferences.UltimateSpawnRoot.")]
    public Transform swordFocusOverride;
    [Tooltip("Optional override for the intro close-up camera pivot. If empty a runtime pivot is created near the player's right shoulder.")]
    public Transform introSwordCameraPivotOverride;
    [Tooltip("Optional override for the intro close-up look target. If empty the sword focus point is used.")]
    public Transform introSwordLookTargetOverride;
    [Tooltip("Animator trigger played at the beginning of the scripted sequence.")]
    public string scriptedStartTrigger = "Ultimate_Start";
    [Tooltip("Animator trigger played during the walk-out section.")]
    public string scriptedWalkTrigger = "Ultimate_Walk";
    [Tooltip("Number of repeated slash hits before the final strike.")]
    public int scriptedMultiHitCount = 4;
    [Tooltip("Unscaled time spent on the opening sword close-up.")]
    public float scriptedCloseupDuration = 0.32f;
    [Tooltip("Unscaled time spent transitioning to the wide shot.")]
    public float scriptedWideDuration = 0.72f;
    [Tooltip("Unscaled delay between each repeated slash hit.")]
    public float scriptedHitInterval = 0.17f;
    [Tooltip("Hold time after the finisher before the walk-out starts.")]
    public float scriptedFinisherHold = 0.35f;
    [Tooltip("Unscaled time spent on the final walk-out shot.")]
    public float scriptedWalkOutDuration = 1.05f;
    [Tooltip("Distance between the player and the target during the slash section.")]
    public float scriptedStrikeDistance = 1.62f;
    [Tooltip("Distance between the player and the target during the final setup.")]
    public float scriptedFinisherDistance = 1.1f;
    [Tooltip("How far in front of the player the victim boss is staged during the cinematic.")]
    public float scriptedVictimCenterDistance = 2.2f;
    [Tooltip("Additional height offset applied to the staged victim root.")]
    public float scriptedVictimCenterHeight = 0f;
    [Tooltip("Lateral offset applied to the victim on the ultimate stage.")]
    public float scriptedStageVictimSideOffset = 0.42f;
    [Tooltip("Lateral offset applied to the player on the ultimate stage.")]
    public float scriptedStagePlayerSideOffset = -0.95f;
    [Tooltip("Forward offset applied to the player base position on the ultimate stage.")]
    public float scriptedStagePlayerForwardOffset = 0.55f;
    [Tooltip("Distance used for the player's orbiting slash position on the ultimate stage.")]
    public float scriptedStageStrikeDistance = 1.85f;
    [Tooltip("Distance used for the player's finishing position on the ultimate stage.")]
    public float scriptedStageFinisherDistance = 1.65f;
    [Tooltip("How far in front of the player the sword close-up camera should sit.")]
    public float scriptedCloseupDistance = 0.65f;
    [Tooltip("How far back the wide shot camera should be placed.")]
    public float scriptedWideDistance = 4.45f;
    [Tooltip("How far in front of the player the walk-out camera should be placed.")]
    public float scriptedWalkCameraDistance = 2.8f;
    [Tooltip("Vertical offset applied to the walk-out camera.")]
    public float scriptedWalkCameraHeight = 1.45f;

    [Header("Screen Effects")]
    [Tooltip("Screen FX controller used during the ultimate.")]
    public UltimateScreenFX screenFX;
    [Tooltip("Optional crack effect spawned on the finisher.")]
    public GameObject glassCrackPrefab;

    [Header("Debug")]
    [Tooltip("Allow instantly filling the ultimate gauge for testing.")]
    public bool enableDebugFillGaugeShortcut = true;
    [Tooltip("Debug hotkey used to fill the ultimate gauge.")]
    public KeyCode debugFillGaugeKey = KeyCode.Alpha1;
    [Tooltip("Log camera restore state for a few frames after the ultimate ends.")]
    public bool enableUltimateCameraDebugLogs = false;

    [Header("Events")]
    public Action OnUltimateStarted;
    public Action OnUltimateEnded;
    public Action<float, float, bool> OnGaugeChanged;
    public Action<UltimateActivationBlockReason, string> OnActivationRejected;
    public Action<UltimateSequencePhase, string> OnUltimatePhaseChanged;

    public float Gauge { get; private set; }
    public bool IsCinematic => _isCinematic;
    public bool IsGaugeReady => gaugeMax > 0f && Gauge >= gaugeMax - 0.0001f;
    public bool DidApplyInputBlockThisCinematic { get; private set; }
    public bool DidFreezeWorldTimeThisCinematic { get; private set; }
    public UltimateActivationBlockReason LastActivationBlockReason { get; private set; } = UltimateActivationBlockReason.None;
    public string LastActivationBlockMessage { get; private set; } = string.Empty;
    public UltimateSequencePhase CurrentUltimatePhase { get; private set; } = UltimateSequencePhase.None;

    bool _isCinematic;
    float _cachedTimeScale = 1f;
    float _cachedFixedDeltaTime = 0.02f;
    DirectorUpdateMode _cachedDirectorTimeUpdateMode = DirectorUpdateMode.GameTime;
    IInputBlocker _input;
    ILockOnController _lockOn;
    ICombatStateReader _combatStateReader;
    PlayerLockOn _playerLockOn;
    UltimateSkillController _ultimateSkillController;
    UltimateHitProcessor _ultimateHitProcessor;
    UltimateCinematicController _activeUltimateCinematic;
    IInvulnerabilityToggle _invul;
    PlayerCombatController _combatController;
    PlayerGuardController _guardController;
    PlayerDodgeController _dodgeController;
    PlayerHealth _playerHealth;
    PlayerReferences _playerReferences;
    CharacterController _characterController;
    FreeLookCamera _freeLookCamera;
    CinemachineVirtualCameraBase _cinemachineFreeLook;
    LockOnCameraManager _lockOnCameraManager;
    FinisherStaticCam _finisherStaticCam;
    CinemachineBrain _cachedCinemachineBrain;
    Camera _cachedMainCamera;
    BossBreakController _fallbackBreakController;
    CinemachineVirtualCameraBase _fallbackSequenceCamera;
    CameraShake _sequenceCameraShake;
    CinemachineCamera _runtimeSequenceCamera;
    UltimateStageRuntime _resolvedRuntimeStage;
    UltimatePresentationClone _playerPresentationClone;
    UltimatePresentationClone _victimPresentationClone;
    public UltimatePresentationClone PlayerPresentationClone => _playerPresentationClone;
    public UltimatePresentationClone VictimPresentationClone => _victimPresentationClone;
    AnimatorUpdateMode _cachedAnimatorUpdateMode = AnimatorUpdateMode.Normal;
    bool _cachedAnimatorApplyRootMotion;
    bool _cachedAnimatorStateValid;
    Coroutine _cameraRestoreCoroutine;
    float _cachedFreeLookYaw;
    float _cachedFreeLookPitch;
    bool _cachedFreeLookStateValid;
    float _cachedCinemachineFreeLookXAxis;
    float _cachedCinemachineFreeLookYAxis;
    bool _cachedCinemachineFreeLookStateValid;
    bool _wasLockedOnBeforeCinematic;
    Transform _cachedLockOnTargetBeforeCinematic;
    LockOnCameraManager.RuntimeStateSnapshot _cachedLockOnCameraRuntimeState;
    bool _cachedLockOnCameraRuntimeStateValid;
    BossBreakController _externalPreservedBreakController;
    float _externalPreservedBreakValue;
    float _externalPreservedBreakTimer;
    bool _externalPreservedBreakWasActive;
    bool _externalSessionLockInput;
    bool _externalSessionInvulnerable;
    bool _externalSessionFreezeTime;
    bool _externalSessionRestoreCamera;

    void Awake()
    {
        _input = GetComponent<IInputBlocker>();
        _playerLockOn = GetComponent<PlayerLockOn>();
        _ultimateSkillController = GetComponent<UltimateSkillController>();
        _ultimateHitProcessor = GetComponent<UltimateHitProcessor>();
        _combatStateReader = CombatStateReaderResolver.ResolveOrAttach(this);
        _lockOn = _playerLockOn as ILockOnController ?? GetComponent<ILockOnController>();
        _invul = GetComponent<IInvulnerabilityToggle>();
        _combatController = GetComponent<PlayerCombatController>();
        _guardController = GetComponent<PlayerGuardController>();
        _dodgeController = GetComponent<PlayerDodgeController>();
        _playerHealth = GetComponent<PlayerHealth>();
        _playerReferences = GetComponent<PlayerReferences>();
        _characterController = GetComponent<CharacterController>();
        ResolveSceneObjectCaches();
        if (scriptedAnimator == null && _playerReferences != null)
            scriptedAnimator = _playerReferences.MainAnimator;
        if (slashBurstSpawner == null)
            slashBurstSpawner = GetComponent<UltimateSlashBurstSpawner>() ?? GetComponentInChildren<UltimateSlashBurstSpawner>(true);

        SyncGaugeUi();
    }

    void OnEnable()
    {
        ResolveSceneObjectCaches();
        if (activateAction?.action != null)
            activateAction.action.Enable();
    }

    void OnDisable()
    {
        if (activateAction?.action != null)
            activateAction.action.Disable();
    }

    void Update()
    {
        if (_isCinematic) return;

        if (enableDebugFillGaugeShortcut && WasKeyPressedThisFrame(debugFillGaugeKey))
            FillGaugeForDebug();

        if (!ShouldConsumeActivationInput()) return;

        TryActivate();
    }

    public void AddGauge(float amount)
    {
        if (_isCinematic) return;

        Gauge = Mathf.Clamp(Gauge + amount, 0f, gaugeMax);
        SyncGaugeUi();
    }

    public void FillGaugeForDebug()
    {
        if (_isCinematic)
            return;

        Gauge = gaugeMax;
        SyncGaugeUi();
    }

    public bool CanActivate(out string reason)
    {
        return CanActivate(out reason, out _);
    }

    public bool CanActivate(out string reason, out UltimateActivationBlockReason blockReason)
    {
        reason = string.Empty;
        blockReason = UltimateActivationBlockReason.None;

        if (_isCinematic)
        {
            reason = "Ultimate cinematic is already running.";
            blockReason = UltimateActivationBlockReason.AlreadyRunning;
            return false;
        }

        if (ignoreActivationWhenBlocked && _input != null && _input.IsBlocked)
        {
            reason = "Global input blocker is active.";
            blockReason = UltimateActivationBlockReason.InputBlocked;
            return false;
        }

        if (Gauge < gaugeMax)
        {
            reason = "Ultimate gauge is not full.";
            blockReason = UltimateActivationBlockReason.GaugeNotReady;
            return false;
        }

        if (blockWhenStaggered && IsPlayerStaggered())
        {
            reason = "Player is staggered.";
            blockReason = UltimateActivationBlockReason.Staggered;
            return false;
        }

        if (!allowInAir && IsPlayerInAir())
        {
            reason = "Player is airborne.";
            blockReason = UltimateActivationBlockReason.Airborne;
            return false;
        }

        if (blockWhenAttacking && IsPlayerAttacking())
        {
            reason = "Player is attacking.";
            blockReason = UltimateActivationBlockReason.Attacking;
            return false;
        }

        if (blockWhenGuarding && IsPlayerGuarding())
        {
            reason = "Player is guarding.";
            blockReason = UltimateActivationBlockReason.Guarding;
            return false;
        }

        if (blockWhenDodging && IsPlayerDodging())
        {
            reason = "Player is dodging.";
            blockReason = UltimateActivationBlockReason.Dodging;
            return false;
        }

        return true;
    }

    bool IsPlayerInAir()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsInAir();

        return _characterController != null && _characterController.enabled && !_characterController.isGrounded;
    }

    bool IsPlayerStaggered()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsStaggered();

        if (_combatController != null && _combatController.IsInHit)
            return true;

        if (_guardController != null && _guardController.IsGuardBroken)
            return true;

        if (_playerHealth != null && (_playerHealth.IsDead || _playerHealth.IsStaggered))
            return true;

        return false;
    }

    bool IsPlayerAttacking()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsAttacking();

        if (_combatController != null && _combatController.IsAttacking)
            return true;

        if (_guardController != null && _guardController.IsAttacking)
            return true;

        return false;
    }

    bool IsPlayerGuarding()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsGuarding();

        return _guardController != null && _guardController.IsGuarding;
    }

    bool IsPlayerDodging()
    {
        if (_combatStateReader != null)
            return _combatStateReader.IsDodging();

        return _dodgeController != null && _dodgeController.IsDodging;
    }

    public bool TryActivate()
    {
        if (!CanActivate(out string reason, out UltimateActivationBlockReason blockReason))
        {
            NotifyActivationRejected(blockReason, reason);
            return false;
        }

        if (_ultimateSkillController == null)
            _ultimateSkillController = GetComponent<UltimateSkillController>();

        if (_ultimateSkillController != null && _ultimateSkillController.UseModernSequence)
        {
            bool started = _ultimateSkillController.TryPlayModernUltimate(this);
            if (!started)
            {
                bool hasTarget = TryResolveUltimateTarget(out _, out _);
                NotifyActivationRejected(
                    hasTarget ? UltimateActivationBlockReason.SequenceStartFailed : UltimateActivationBlockReason.TargetUnavailable,
                    hasTarget ? "Ultimate sequence failed to start." : "No valid ultimate target.");
            }
            return started;
        }

        if (useScriptedSequence && !allowDirectorFallbackWhenScriptedUnavailable &&
            !TryResolveUltimateTarget(out _, out _))
        {
            NotifyActivationRejected(UltimateActivationBlockReason.TargetUnavailable, "No valid ultimate target.");
            return false;
        }

        StartCoroutine(Co_Cinematic());
        return true;
    }

    void NotifyActivationRejected(UltimateActivationBlockReason blockReason, string message)
    {
        LastActivationBlockReason = blockReason;
        LastActivationBlockMessage = message ?? string.Empty;
        OnActivationRejected?.Invoke(blockReason, LastActivationBlockMessage);
    }

    public void NotifyUltimatePhaseChanged(UltimateSequencePhase phase, string context = null)
    {
        if (CurrentUltimatePhase == phase)
            return;

        CurrentUltimatePhase = phase;
        OnUltimatePhaseChanged?.Invoke(phase, context ?? string.Empty);
    }

    public bool TryBeginExternalCinematicSession(
        bool lockInputOverride,
        bool invulnerableOverride,
        bool freezeTimeOverride,
        bool restoreCameraOverride)
    {
        if (_isCinematic)
            return false;

        _isCinematic = true;
        DidApplyInputBlockThisCinematic = false;
        DidFreezeWorldTimeThisCinematic = false;
        _externalSessionLockInput = lockInputOverride;
        _externalSessionInvulnerable = invulnerableOverride;
        _externalSessionFreezeTime = freezeTimeOverride;
        _externalSessionRestoreCamera = restoreCameraOverride;
        Gauge = 0f;
        SyncGaugeUi();

        CacheExternalBreakPresentationState();

        if (_cameraRestoreCoroutine != null)
        {
            StopCoroutine(_cameraRestoreCoroutine);
            _cameraRestoreCoroutine = null;
        }

        if (_externalSessionLockInput)
        {
            _input?.BlockAll(true);
            DidApplyInputBlockThisCinematic = _input != null;
        }

        if (_externalSessionInvulnerable)
            _invul?.SetInvulnerable(true);

        CacheFreeLookCameraState();
        _wasLockedOnBeforeCinematic = _lockOn != null && _lockOn.IsLockedOn();
        _cachedLockOnTargetBeforeCinematic = _playerLockOn != null ? _playerLockOn.GetCurrentTarget() : null;
        CacheLockOnCameraRuntimeStateIfNeeded();
        _lockOn?.GiveCameraControlToTimeline(true);
        OnUltimateStarted?.Invoke();

        if (_externalSessionFreezeTime)
        {
            _cachedTimeScale = Time.timeScale;
            _cachedFixedDeltaTime = Time.fixedDeltaTime;
            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0f;
            DidFreezeWorldTimeThisCinematic = true;
        }

        return true;
    }

    public void EndExternalCinematicSession()
    {
        if (!_isCinematic)
            return;

        ForceCleanupScriptedPresentation();
        RestoreScriptedAnimator();

        if (_externalSessionFreezeTime)
        {
            Time.timeScale = _cachedTimeScale;
            Time.fixedDeltaTime = _cachedFixedDeltaTime;
        }

        if (_externalPreservedBreakController != null)
        {
            _externalPreservedBreakController.RestoreStateAfterPresentation(
                _externalPreservedBreakValue,
                _externalPreservedBreakWasActive,
                _externalPreservedBreakTimer);
        }

        if (_cameraRestoreCoroutine != null)
        {
            StopCoroutine(_cameraRestoreCoroutine);
            _cameraRestoreCoroutine = null;
        }

        if (_externalSessionRestoreCamera)
            QueueGameplayCameraRestore();

        if (_externalSessionInvulnerable)
            _invul?.SetInvulnerable(false);

        if (_externalSessionLockInput)
            _input?.BlockAll(false);

        _externalPreservedBreakController = null;
        _externalPreservedBreakValue = 0f;
        _externalPreservedBreakTimer = 0f;
        _externalPreservedBreakWasActive = false;
        _externalSessionLockInput = false;
        _externalSessionInvulnerable = false;
        _externalSessionFreezeTime = false;
        _externalSessionRestoreCamera = false;

        OnUltimateEnded?.Invoke();
        NotifyUltimatePhaseChanged(UltimateSequencePhase.None, "Ended");
        _isCinematic = false;
    }

    public void SetActiveUltimateCinematic(UltimateCinematicController controller)
    {
        _activeUltimateCinematic = controller;
    }

    public void ClearActiveUltimateCinematic(UltimateCinematicController controller)
    {
        if (_activeUltimateCinematic == controller)
            _activeUltimateCinematic = null;
    }

    public void OnUltimateDamageCommit()
    {
        if (_activeUltimateCinematic != null)
        {
            _activeUltimateCinematic.CommitGameplayDamage();
            return;
        }

        _ultimateHitProcessor?.FlushBufferedDamage();
    }

    public void PrepareModernPresentationActor()
    {
        Transform playerRoot = ResolvePlayerRoot();
        if (playerRoot == null)
            return;

        Transform playerPresentationSource = ResolvePlayerPresentationSource(playerRoot);
        if (playerPresentationSource == null)
            return;

        ResolveSwordFocus(playerRoot);
        ResolveIntroSwordCameraPivot(playerRoot);
        ResolveIntroSwordLookTarget(playerRoot);

        Animator presentationSourceAnimator = ResolvePresentationAnimator(playerPresentationSource);
        if (presentationSourceAnimator == null)
            presentationSourceAnimator = scriptedAnimator != null ? scriptedAnimator : _playerReferences != null ? _playerReferences.MainAnimator : null;

        _playerPresentationClone = EnsurePresentationClone(
            ref _playerPresentationClone,
            "__UltimatePlayerPresentation",
            playerPresentationSource,
            playerRoot,
            true);

        _playerPresentationClone?.SyncAnimatorFrom(presentationSourceAnimator);
        _victimPresentationClone?.ClearClone();
    }

    public void PrepareModernIntroStagePresentation(UltimateStageRuntime activeStage, Transform targetTransform, UltimateTargetBinder binder)
    {
        if (activeStage == null || binder == null)
        {
            PrepareModernPresentationActor();
            binder?.ClearPresentationAnchorOverrides();
            return;
        }

        Transform playerRoot = ResolvePlayerRoot();
        if (playerRoot == null)
            return;

        Vector3 targetFocus = GetTargetFocusPoint(targetTransform);
        activeStage.PositionStageFixed(playerRoot.position);

        Transform playerPresentationSource = ResolvePlayerPresentationSource(playerRoot);
        Transform victimPresentationSource = ResolveVictimPresentationSource(targetTransform);

        ResolveSwordFocus(playerRoot);
        ResolveIntroSwordCameraPivot(playerRoot);
        ResolveIntroSwordLookTarget(playerRoot);

        Animator playerPresentationAnimator = ResolvePresentationAnimator(playerPresentationSource);
        if (playerPresentationAnimator == null)
            playerPresentationAnimator = scriptedAnimator != null ? scriptedAnimator : _playerReferences != null ? _playerReferences.MainAnimator : null;

        Animator victimPresentationAnimator = ResolvePresentationAnimator(victimPresentationSource != null ? victimPresentationSource : targetTransform);

        _playerPresentationClone = EnsurePresentationClone(
            ref _playerPresentationClone,
            "__UltimatePlayerPresentation",
            playerPresentationSource,
            activeStage.PlayerAnchor,
            false);
        _victimPresentationClone = EnsurePresentationClone(
            ref _victimPresentationClone,
            "__UltimateVictimPresentation",
            victimPresentationSource,
            activeStage.VictimAnchor,
            false);

        _playerPresentationClone?.SyncAnimatorFrom(playerPresentationAnimator);
        _victimPresentationClone?.SyncAnimatorFrom(victimPresentationAnimator);

        Vector3 stageVictimPosition = ResolveStageVictimPosition(activeStage);
        Vector3 stageTargetFocus = stageVictimPosition + Vector3.up * 1.05f;
        Vector3 stagePlayerPosition = ResolveStagePlayerOrigin(activeStage);
        UpdatePresentationActors(activeStage, stagePlayerPosition, stageTargetFocus, stageVictimPosition, stagePlayerPosition, stagePlayerPosition);

        Transform playerOverride = _playerPresentationClone != null && _playerPresentationClone.CloneRoot != null
            ? _playerPresentationClone.CloneRoot
            : activeStage.PlayerAnchor;
        Transform victimOverride = _victimPresentationClone != null && _victimPresentationClone.CloneRoot != null
            ? _victimPresentationClone.CloneRoot
            : activeStage.VictimAnchor;
        binder.SetPresentationAnchorOverrides(playerOverride, victimOverride);
    }

    void CacheExternalBreakPresentationState()
    {
        _externalPreservedBreakController = null;
        _externalPreservedBreakValue = 0f;
        _externalPreservedBreakTimer = 0f;
        _externalPreservedBreakWasActive = false;

        if (TryResolveUltimateTarget(out _, out Transform preservedBreakTargetTransform))
            _externalPreservedBreakController = ResolveUltimateBreakController(preservedBreakTargetTransform);

        if (_externalPreservedBreakController == null)
            _externalPreservedBreakController = ResolveFallbackBreakController();

        if (_externalPreservedBreakController == null)
            return;

        _externalPreservedBreakValue = _externalPreservedBreakController.CurrentBreak;
        _externalPreservedBreakTimer = _externalPreservedBreakController.RemainingBreakTime;
        _externalPreservedBreakWasActive = _externalPreservedBreakController.IsInBreak;
    }

    bool ShouldConsumeActivationInput()
    {
        if (ignoreActivationWhenBlocked && _input != null && _input.IsBlocked)
            return false;

        if (activateAction != null && activateAction.action != null && activateAction.action.WasPressedThisFrame())
            return true;

        if (useLegacyHotkey && WasKeyPressedThisFrame(legacyHotkey))
            return true;

        return false;
    }

    static bool WasKeyPressedThisFrame(KeyCode key)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            switch (key)
            {
                case KeyCode.R: return keyboard.rKey.wasPressedThisFrame;
                case KeyCode.Q: return keyboard.qKey.wasPressedThisFrame;
                case KeyCode.E: return keyboard.eKey.wasPressedThisFrame;
                case KeyCode.F: return keyboard.fKey.wasPressedThisFrame;
                case KeyCode.Alpha1: return keyboard.digit1Key.wasPressedThisFrame;
                case KeyCode.Alpha2: return keyboard.digit2Key.wasPressedThisFrame;
                case KeyCode.Alpha3: return keyboard.digit3Key.wasPressedThisFrame;
                case KeyCode.Alpha4: return keyboard.digit4Key.wasPressedThisFrame;
                case KeyCode.Alpha5: return keyboard.digit5Key.wasPressedThisFrame;
                case KeyCode.Space: return keyboard.spaceKey.wasPressedThisFrame;
                case KeyCode.LeftShift: return keyboard.leftShiftKey.wasPressedThisFrame;
                case KeyCode.RightShift: return keyboard.rightShiftKey.wasPressedThisFrame;
            }
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

    IEnumerator Co_Cinematic()
    {
        _isCinematic = true;
        DidApplyInputBlockThisCinematic = false;
        DidFreezeWorldTimeThisCinematic = false;
        bool shouldTouchLegacyDirector = !useScriptedSequence || allowDirectorFallbackWhenScriptedUnavailable;
        bool cachedDirectorMode = false;
        BossBreakController preservedBreakController = null;
        float preservedBreakValue = 0f;
        float preservedBreakTimer = 0f;
        bool preservedBreakWasActive = false;
        Gauge = 0f;
        SyncGaugeUi();

        if (TryResolveUltimateTarget(out _, out Transform preservedBreakTargetTransform))
            preservedBreakController = ResolveUltimateBreakController(preservedBreakTargetTransform);

        if (preservedBreakController == null)
            preservedBreakController = ResolveFallbackBreakController();

        if (preservedBreakController != null)
        {
            preservedBreakValue = preservedBreakController.CurrentBreak;
            preservedBreakTimer = preservedBreakController.RemainingBreakTime;
            preservedBreakWasActive = preservedBreakController.IsInBreak;
        }

        if (lockInputDuringCinematic)
        {
            _input?.BlockAll(true);
            DidApplyInputBlockThisCinematic = _input != null;
        }
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(true);
        CacheFreeLookCameraState();
        _wasLockedOnBeforeCinematic = _lockOn != null && _lockOn.IsLockedOn();
        _cachedLockOnTargetBeforeCinematic = _playerLockOn != null ? _playerLockOn.GetCurrentTarget() : null;
        CacheLockOnCameraRuntimeStateIfNeeded();
        _lockOn?.GiveCameraControlToTimeline(true);

        OnUltimateStarted?.Invoke();
        NotifyUltimatePhaseChanged(UltimateSequencePhase.PreCast, "Legacy");
        screenFX?.PlayChargeIn();

        if (director != null && shouldTouchLegacyDirector)
        {
            _cachedDirectorTimeUpdateMode = director.timeUpdateMode;
            cachedDirectorMode = true;
            director.Stop();
            director.time = 0d;
            director.Evaluate();
        }

        if (useScriptedSequence)
            SuppressLegacyUltimateCameras();

        if (freezeWorldTimeDuringCinematic)
        {
            _cachedTimeScale = Time.timeScale;
            _cachedFixedDeltaTime = Time.fixedDeltaTime;
            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0f;
            DidFreezeWorldTimeThisCinematic = true;
        }

        try
        {
            bool ranScriptedSequence = false;
            if (useScriptedSequence && TryResolveUltimateTarget(out IUltimateTarget scriptedTarget, out Transform scriptedTargetTransform))
            {
                PrepareScriptedAnimator();
                yield return Co_ScriptedSequence(scriptedTarget, scriptedTargetTransform);
                RestoreScriptedAnimator();
                ranScriptedSequence = true;
            }

            if (!ranScriptedSequence && allowDirectorFallbackWhenScriptedUnavailable && director != null)
            {
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                director.time = 0d;
                director.Evaluate();
                director.Play();
            }

            while (!ranScriptedSequence && director != null && director.state == PlayState.Playing)
                yield return null;
        }
        finally
        {
            ForceCleanupScriptedPresentation();
            RestoreScriptedAnimator();
            screenFX?.PlayChargeOut();
            if (director != null && cachedDirectorMode)
                director.timeUpdateMode = _cachedDirectorTimeUpdateMode;
            if (freezeWorldTimeDuringCinematic)
            {
                Time.timeScale = _cachedTimeScale;
                Time.fixedDeltaTime = _cachedFixedDeltaTime;
            }
            if (preservedBreakController != null)
                preservedBreakController.RestoreStateAfterPresentation(preservedBreakValue, preservedBreakWasActive, preservedBreakTimer);
            if (_cameraRestoreCoroutine != null)
            {
                StopCoroutine(_cameraRestoreCoroutine);
                _cameraRestoreCoroutine = null;
            }
            if (restoreCameraAndLockOn)
                QueueGameplayCameraRestore();
            if (invulnerableDuringCinematic) _invul?.SetInvulnerable(false);
            if (lockInputDuringCinematic) _input?.BlockAll(false);

            OnUltimateEnded?.Invoke();
            NotifyUltimatePhaseChanged(UltimateSequencePhase.None, "Ended");
            _isCinematic = false;
        }
    }

    void SyncGaugeUi()
    {
        float normalized = gaugeMax > 0f ? Mathf.Clamp01(Gauge / gaugeMax) : 0f;
        bool ready = normalized >= 1f - 0.0001f;

        UI_UltimateGauge.UpdateValue(normalized);
        UI_UltimateGauge.SetReady(ready);

        // UI sync already happens here, so broadcasting from this point keeps every consumer event-driven.
        OnGaugeChanged?.Invoke(Gauge, normalized, ready);
    }

    public void OnMultiHit()
    {
        ApplyBurstDamage(null, multihitFixedDamage);
        screenFX?.PulseMinor();
    }

    public void OnFinisher()
    {
        ApplyBurstDamage(null, finisherFixedDamage);
        SpawnFinisherImpact(null);
        screenFX?.PulseMajor();
    }

    void ApplyBurstDamage(IUltimateTarget explicitTarget, int damage)
    {
        var ultimateTarget = explicitTarget;
        if (ultimateTarget == null)
        {
            var target = _lockOn?.GetCurrentTarget();
            ultimateTarget = ResolveLockedUltimateTarget(target);
        }

        if (ultimateTarget == null)
            ultimateTarget = FindFallbackUltimateTarget();

        if (ultimateTarget != null)
        {
            ultimateTarget.ApplyUltimateDamage(damage);
            return;
        }
    }

    IEnumerator Co_ScriptedSequence(IUltimateTarget target, Transform targetTransform)
    {
        Transform playerRoot = ResolvePlayerRoot();
        Transform swordFocus = ResolveSwordFocus(playerRoot);
        UltimateStageRuntime activeStage = ResolveUltimateStageRuntime();
        CinemachineVirtualCameraBase sequenceCam = ResolveSequenceCamera();
        if (sequenceCam == null || playerRoot == null || targetTransform == null)
        {
            ApplyBurstDamage(target, finisherFixedDamage);
            yield break;
        }

        Vector3 targetFocus = GetTargetFocusPoint(targetTransform);
        Vector3 approachDirection = GetFlattenedDirection(targetFocus - playerRoot.position, playerRoot.forward);
        Vector3 victimAnchorPosition = ResolveVictimAnchorPosition(activeStage, playerRoot, targetTransform, approachDirection);
        Vector3 stageVictimPosition = victimAnchorPosition;
        Vector3 stageTargetFocus = targetFocus;
        Vector3 stagePlayerPosition = playerRoot.position;
        Vector3 cachedPlayerWorldPosition = playerRoot.position;
        Quaternion cachedPlayerWorldRotation = playerRoot.rotation;
        bool preserveGameplayPlayerPose = activeStage != null;
        bool stageCaptureActive = false;
        IUltimateVictimState victimState = ResolveUltimateVictimState(targetTransform);
        bool victimStateActive = false;
        SequenceCameraState cameraState = default;
        bool cameraStateCached = false;

        try
        {
            if (activeStage != null)
            {
                activeStage.PositionStageFixed(playerRoot.position);
                activeStage.BeginPresentationCapture(Camera.main);
                stageCaptureActive = true;
                ReleasePresentationClones();

                Transform playerPresentationSource = ResolvePlayerPresentationSource(playerRoot);
                Transform victimPresentationSource = ResolveVictimPresentationSource(targetTransform);
                Animator victimPresentationAnimator = ResolvePresentationAnimator(victimPresentationSource != null ? victimPresentationSource : targetTransform);

                _playerPresentationClone = EnsurePresentationClone(ref _playerPresentationClone, "__UltimatePlayerPresentation", playerPresentationSource, activeStage.PlayerAnchor);
                _victimPresentationClone = EnsurePresentationClone(ref _victimPresentationClone, "__UltimateVictimPresentation", victimPresentationSource, activeStage.VictimAnchor);
                _playerPresentationClone?.SyncAnimatorFrom(scriptedAnimator);
                _victimPresentationClone?.SyncAnimatorFrom(victimPresentationAnimator);

                stageVictimPosition = ResolveStageVictimPosition(activeStage);
                stageTargetFocus = stageVictimPosition + Vector3.up * 1.05f;
                stagePlayerPosition = ResolveStagePlayerOrigin(activeStage);
                UpdatePresentationActors(activeStage, stagePlayerPosition, stageTargetFocus, stageVictimPosition, stagePlayerPosition, stagePlayerPosition);
                slashBurstSpawner?.PushPresentationOverride(activeStage.PlayerAnchor, activeStage.PlayerAnchor);
            }

            victimState?.BeginUltimateVictimState(playerRoot, EstimateScriptedSequenceDuration());
            victimStateActive = victimState != null;
            UpdateUltimateVictimAnchor(victimState, victimAnchorPosition, playerRoot.position);
            UpdateUltimateStageAnchors(activeStage, playerRoot.position, victimAnchorPosition, playerRoot.position);

            targetFocus = GetTargetFocusPoint(targetTransform);
            approachDirection = GetFlattenedDirection(targetFocus - playerRoot.position, approachDirection);
            victimAnchorPosition = ResolveVictimAnchorPosition(activeStage, playerRoot, targetTransform, approachDirection);
            if (!preserveGameplayPlayerPose)
                SnapPlayerPose(playerRoot, targetFocus - approachDirection * scriptedStrikeDistance, targetFocus);
            UpdateUltimateVictimAnchor(victimState, victimAnchorPosition, playerRoot.position);
            UpdateUltimateStageAnchors(activeStage, playerRoot.position, victimAnchorPosition, playerRoot.position);

            cameraState = CacheSequenceCameraState(sequenceCam);
            cameraStateCached = true;
            bool ownsSequenceCamera = sequenceCam == _runtimeSequenceCamera
                || (activeStage != null && sequenceCam == activeStage.SequenceCamera);
            int sequencePriority = ownsSequenceCamera
                ? scriptedCameraPriority + 200
                : scriptedCameraPriority;
            sequenceCam.Priority = Mathf.Max(sequenceCam.Priority, sequencePriority);
            sequenceCam.Follow = null;
            sequenceCam.LookAt = null;
            _sequenceCameraShake = activeStage != null && sequenceCam == activeStage.SequenceCamera
                ? activeStage.SequenceCameraShake
                : sequenceCam.GetComponent<CameraShake>();

        Vector3 swordFocusPoint = activeStage != null
            ? ResolvePresentationSwordFocus(activeStage, swordFocus)
            : swordFocus.position;
        Vector3 closeupLookTarget = activeStage != null ? stageTargetFocus : swordFocusPoint + approachDirection * 0.35f;
        Vector3 closeupRight = activeStage != null && activeStage.PlayerAnchor != null ? activeStage.PlayerAnchor.right : playerRoot.right;
        Vector3 closeupPos = swordFocusPoint - approachDirection * scriptedCloseupDistance + closeupRight * 0.28f + Vector3.up * 0.18f;
        Quaternion closeupRot = Quaternion.LookRotation((closeupLookTarget - closeupPos).normalized, Vector3.up);
        if (TryGetStageShotPose(activeStage != null ? activeStage.OpenShotAnchor : null, out Vector3 stagedOpenPos, out Quaternion stagedOpenRot))
        {
            closeupPos = stagedOpenPos;
            closeupRot = stagedOpenRot;
        }
        sequenceCam.transform.SetPositionAndRotation(closeupPos, closeupRot);
        TrySetAnimatorTrigger(scriptedStartTrigger);
        yield return WaitForSecondsRealtimeSafe(scriptedCloseupDuration);

        targetFocus = GetTargetFocusPoint(targetTransform);
        victimAnchorPosition = ResolveVictimAnchorPosition(activeStage, playerRoot, targetTransform, approachDirection);
        UpdateUltimateVictimAnchor(victimState, victimAnchorPosition, playerRoot.position);
        UpdateUltimateStageAnchors(activeStage, playerRoot.position, victimAnchorPosition, playerRoot.position);
        if (activeStage != null)
        {
            stageVictimPosition = ResolveStageVictimPosition(activeStage);
            stageTargetFocus = stageVictimPosition + Vector3.up * 1.15f;
            stagePlayerPosition = ResolveStageStrikePosition(activeStage, stageVictimPosition, approachDirection, scriptedStageStrikeDistance);
            UpdatePresentationActors(activeStage, stagePlayerPosition, stageTargetFocus, stageVictimPosition, stagePlayerPosition, stagePlayerPosition);
        }
        Vector3 wideLookTarget = activeStage != null && activeStage.FocusAnchor != null
            ? activeStage.FocusAnchor.position
            : targetFocus + Vector3.up * 1.15f;
        Vector3 wideSide = Vector3.Cross(Vector3.up, approachDirection).normalized;
        Vector3 widePos = wideLookTarget - approachDirection * scriptedWideDistance + wideSide * 1.35f + Vector3.up * 0.9f;
        Quaternion wideRot = Quaternion.LookRotation((wideLookTarget - widePos).normalized, Vector3.up);
        if (TryGetStageShotPose(activeStage != null ? activeStage.WideShotAnchor : null, out Vector3 stagedWidePos, out Quaternion stagedWideRot))
        {
            widePos = stagedWidePos;
            wideRot = stagedWideRot;
        }
        yield return Co_MoveCamera(sequenceCam.transform, closeupPos, closeupRot, widePos, wideRot, scriptedWideDuration);

        int totalHits = Mathf.Max(1, scriptedMultiHitCount);
        int seed = Mathf.Abs((int)(Time.unscaledTime * 1000f)) + totalHits * 31;
        for (int i = 0; i < totalHits; i++)
        {
            targetFocus = GetTargetFocusPoint(targetTransform);
            Vector3 radialDir = Quaternion.AngleAxis(i * 137.5078f, Vector3.up) * approachDirection;
            radialDir = GetFlattenedDirection(radialDir, approachDirection);

            Vector3 strikePos = targetFocus - radialDir * scriptedStrikeDistance;
            if (!preserveGameplayPlayerPose)
                SnapPlayerPose(playerRoot, strikePos, targetFocus);
            victimAnchorPosition = ResolveVictimAnchorPosition(activeStage, playerRoot, targetTransform, approachDirection);
            UpdateUltimateVictimAnchor(victimState, victimAnchorPosition, playerRoot.position);
            UpdateUltimateStageAnchors(activeStage, playerRoot.position, victimAnchorPosition, playerRoot.position);
            if (activeStage != null)
            {
                stageTargetFocus = stageVictimPosition + Vector3.up * 1.05f;
                stagePlayerPosition = ResolveStageStrikePosition(activeStage, stageVictimPosition, radialDir, scriptedStageStrikeDistance);
                UpdatePresentationActors(activeStage, stagePlayerPosition, stageTargetFocus, stageVictimPosition, stagePlayerPosition, stagePlayerPosition);
            }
            slashBurstSpawner?.EmitOneSlash(i, totalHits, seed, strikePos, targetFocus + Vector3.up * 0.95f, Mathf.Max(0.45f, scriptedHitInterval * 4f));
            ApplyBurstDamage(target, multihitFixedDamage);
            TriggerVictimPresentationHit(false);
            screenFX?.PulseMinor();
            PulseSequenceCamera();

            Vector3 dynamicLookTarget = activeStage != null && activeStage.FocusAnchor != null
                ? activeStage.FocusAnchor.position
                : targetFocus + Vector3.up * 1.05f;
            Vector3 dynamicPos = dynamicLookTarget - radialDir * (scriptedWideDistance - 0.6f) + Vector3.up * 0.75f;
            Quaternion dynamicRot = Quaternion.LookRotation((dynamicLookTarget - dynamicPos).normalized, Vector3.up);
            Transform dynamicShotAnchor = activeStage != null
                ? (i % 2 == 0 ? activeStage.SlashLeftShotAnchor : activeStage.SlashRightShotAnchor)
                : null;
            if (TryGetStageShotPose(dynamicShotAnchor, out Vector3 stagedDynamicPos, out Quaternion stagedDynamicRot))
            {
                dynamicPos = stagedDynamicPos;
                dynamicRot = stagedDynamicRot;
            }
            yield return Co_MoveCamera(sequenceCam.transform, sequenceCam.transform.position, sequenceCam.transform.rotation, dynamicPos, dynamicRot, scriptedHitInterval * 0.7f);
            yield return WaitForSecondsRealtimeSafe(scriptedHitInterval * 0.3f);
        }

        targetFocus = GetTargetFocusPoint(targetTransform);
        Vector3 finisherDir = GetFlattenedDirection(targetFocus - playerRoot.position, approachDirection);
        Vector3 finisherPos = targetFocus - finisherDir * scriptedFinisherDistance;
        if (!preserveGameplayPlayerPose)
            SnapPlayerPose(playerRoot, finisherPos, targetFocus);
        UpdateUltimateVictimAnchor(victimState, victimAnchorPosition, playerRoot.position);
        UpdateUltimateStageAnchors(activeStage, playerRoot.position, victimAnchorPosition, playerRoot.position);
        if (activeStage != null)
        {
            stageTargetFocus = stageVictimPosition + Vector3.up * 0.9f;
            stagePlayerPosition = ResolveStageStrikePosition(activeStage, stageVictimPosition, finisherDir, scriptedStageFinisherDistance);
            UpdatePresentationActors(activeStage, stagePlayerPosition, stageTargetFocus, stageVictimPosition, stagePlayerPosition, stagePlayerPosition);
        }

        Vector3 finisherLookTarget = activeStage != null && activeStage.VictimAnchor != null
            ? activeStage.VictimAnchor.position + Vector3.up * 0.8f
            : targetFocus + Vector3.up * 0.8f;
        Vector3 finisherPosCam = finisherLookTarget - finisherDir * 5.35f + Vector3.Cross(Vector3.up, finisherDir) * 2.35f + Vector3.up * 1.75f;
        Quaternion finisherRot = Quaternion.LookRotation((finisherLookTarget - finisherPosCam).normalized, Vector3.up);
        if (TryGetStageShotPose(activeStage != null ? activeStage.FinisherShotAnchor : null, out Vector3 stagedFinisherPos, out Quaternion stagedFinisherRot))
        {
            finisherPosCam = stagedFinisherPos;
            finisherRot = stagedFinisherRot;
        }
        yield return Co_MoveCamera(sequenceCam.transform, sequenceCam.transform.position, sequenceCam.transform.rotation, finisherPosCam, finisherRot, 0.16f);

        ApplyBurstDamage(target, finisherFixedDamage);
        TriggerVictimPresentationHit(true);
        SpawnFinisherImpact(activeStage != null ? stageTargetFocus : targetFocus);
        screenFX?.PulseMajor();
        PulseSequenceCamera(0.16f, 0.09f);
        yield return WaitForSecondsRealtimeSafe(scriptedFinisherHold);

        Vector3 walkDirection = finisherDir;
        Vector3 walkStart = finisherPos;
        Vector3 walkEnd = walkStart + walkDirection * 1.8f;
        Vector3 walkLookTarget = walkStart + Vector3.up * 1.35f;
        Vector3 walkCamPos = walkStart - walkDirection * scriptedWalkCameraDistance + Vector3.up * scriptedWalkCameraHeight;
        Quaternion walkCamRot = Quaternion.LookRotation((walkLookTarget - walkCamPos).normalized, Vector3.up);
        if (TryGetStageShotPose(activeStage != null ? activeStage.WalkOutShotAnchor : null, out Vector3 stagedWalkPos, out Quaternion stagedWalkRot))
        {
            walkCamPos = stagedWalkPos;
            walkCamRot = stagedWalkRot;
        }
        sequenceCam.transform.SetPositionAndRotation(walkCamPos, walkCamRot);
        UpdateUltimateVictimAnchor(victimState, victimAnchorPosition, walkEnd);
        UpdateUltimateStageAnchors(activeStage, playerRoot.position, victimAnchorPosition, walkEnd);
        if (activeStage != null)
        {
            Vector3 stageWalkStart = stagePlayerPosition;
            Vector3 stageWalkEnd = stageWalkStart + walkDirection * 1.8f;
            UpdatePresentationActors(activeStage, stageWalkStart, stageWalkStart + walkDirection, stageVictimPosition, stageWalkStart, stageWalkEnd);
        }
        TrySetAnimatorTrigger(scriptedWalkTrigger);
        if (preserveGameplayPlayerPose)
        {
            yield return Co_MovePresentationActors(activeStage, stagePlayerPosition, stagePlayerPosition + walkDirection * 1.8f, walkDirection, scriptedWalkOutDuration, stageVictimPosition);
        }
        else
        {
            yield return Co_MovePlayerWithPresentation(playerRoot, walkStart, walkEnd, targetFocus, scriptedWalkOutDuration, activeStage, stagePlayerPosition, stagePlayerPosition + walkDirection * 1.8f, stageVictimPosition);
        }

        }
        finally
        {
            if (cameraStateCached)
                RestoreSequenceCamera(sequenceCam, cameraState);
            if (victimStateActive)
                victimState?.EndUltimateVictimState();
            if (preserveGameplayPlayerPose && playerRoot != null)
                SetPlayerPose(playerRoot, cachedPlayerWorldPosition, cachedPlayerWorldRotation);
            slashBurstSpawner?.ClearPresentationOverride();
            ReleasePresentationClones();
            if (stageCaptureActive && activeStage != null)
                activeStage.EndPresentationCapture();
        }
    }

    IUltimateTarget FindFallbackUltimateTarget()
    {
        Transform origin = ResolvePlayerRoot();
        Vector3 originPoint = origin != null ? origin.position : transform.position;
        var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        IUltimateTarget bestTarget = null;
        float bestDistanceSq = float.PositiveInfinity;
        foreach (var behaviour in behaviours)
        {
            if (behaviour is not IUltimateTarget target)
                continue;

            Vector3 targetPoint = behaviour.transform.position;
            float distanceSq = (targetPoint - originPoint).sqrMagnitude;
            if (distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            bestTarget = target;
        }

        return bestTarget;
    }

    IUltimateTarget ResolveLockedUltimateTarget(Transform target)
    {
        if (target == null)
            return null;

        if (target.TryGetComponent<IUltimateTarget>(out var direct))
            return direct;

        var parentTarget = target.GetComponentInParent<IUltimateTarget>();
        if (parentTarget != null)
            return parentTarget;

        if (target.root != null && target.root.TryGetComponent<IUltimateTarget>(out var rootTarget))
            return rootTarget;

        return null;
    }

    IUltimateVictimState ResolveUltimateVictimState(Transform target)
    {
        if (target == null)
            return null;

        if (target.TryGetComponent<IUltimateVictimState>(out var direct))
            return direct;

        var parentVictim = target.GetComponentInParent<IUltimateVictimState>();
        if (parentVictim != null)
            return parentVictim;

        if (target.root != null && target.root.TryGetComponent<IUltimateVictimState>(out var rootVictim))
            return rootVictim;

        return null;
    }

    BossBreakController ResolveUltimateBreakController(Transform target)
    {
        if (target == null)
            return null;

        if (target.TryGetComponent<BossBreakController>(out var direct))
            return direct;

        BossBreakController parentBreak = target.GetComponentInParent<BossBreakController>();
        if (parentBreak != null)
            return parentBreak;

        if (target.root != null && target.root.TryGetComponent<BossBreakController>(out var rootBreak))
            return rootBreak;

        return null;
    }

    float EstimateScriptedSequenceDuration()
    {
        int totalHits = Mathf.Max(1, scriptedMultiHitCount);
        return Mathf.Max(0f, scriptedCloseupDuration)
            + Mathf.Max(0f, scriptedWideDuration)
            + Mathf.Max(0f, scriptedHitInterval) * totalHits
            + Mathf.Max(0f, scriptedFinisherHold)
            + Mathf.Max(0f, scriptedWalkOutDuration)
            + 0.35f;
    }

    void UpdateUltimateVictimAnchor(IUltimateVictimState victimState, Vector3 anchorPosition, Vector3 lookTarget)
    {
        if (victimState == null)
            return;

        victimState.SetUltimateVictimAnchor(anchorPosition, lookTarget);
    }

    void UpdateUltimateStageAnchors(UltimateStageRuntime activeStage, Vector3 playerWorld, Vector3 victimWorld, Vector3 walkOutWorld)
    {
        if (activeStage == null)
            return;

        if (_playerPresentationClone != null || _victimPresentationClone != null)
            return;

        activeStage.UpdateAnchors(playerWorld, victimWorld, walkOutWorld);
    }

    Vector3 ResolveVictimAnchorPosition(UltimateStageRuntime activeStage, Transform playerRoot, Transform targetTransform, Vector3 approachDirection)
    {
        Vector3 playerPosition = playerRoot != null ? playerRoot.position : transform.position;
        Vector3 targetPosition = targetTransform != null ? targetTransform.position : playerPosition + approachDirection;
        if (activeStage != null)
            return activeStage.ComputeVictimPosition(playerPosition, targetPosition, approachDirection, scriptedVictimCenterDistance, scriptedVictimCenterHeight);

        Vector3 anchorPosition = playerPosition + approachDirection * Mathf.Max(0.8f, scriptedVictimCenterDistance);
        anchorPosition.y = targetPosition.y + scriptedVictimCenterHeight;
        return anchorPosition;
    }

    bool TryResolveUltimateTarget(out IUltimateTarget target, out Transform targetTransform)
    {
        targetTransform = _lockOn?.GetCurrentTarget();
        target = ResolveLockedUltimateTarget(targetTransform);
        if (target != null)
            return true;

        target = FindFallbackUltimateTarget();
        if (target is Component component)
        {
            targetTransform = component.transform;
            return true;
        }

        targetTransform = null;
        return false;
    }

    public bool TryGetUltimateTarget(out IUltimateTarget target, out Transform targetTransform)
    {
        return TryResolveUltimateTarget(out target, out targetTransform);
    }

    public IUltimateVictimState GetUltimateVictimState(Transform target)
    {
        return ResolveUltimateVictimState(target);
    }

    public BossBreakController GetUltimateBreakController(Transform target)
    {
        return ResolveUltimateBreakController(target);
    }

    public Transform GetUltimateSwordFocusTransform()
    {
        return ResolveSwordFocus(ResolvePlayerRoot());
    }

    public Transform GetUltimateIntroSwordCameraPivotTransform()
    {
        return ResolveIntroSwordCameraPivot(ResolvePlayerRoot());
    }

    public Transform GetUltimateIntroSwordLookTargetTransform()
    {
        return ResolveIntroSwordLookTarget(ResolvePlayerRoot());
    }

    public Transform GetUltimatePlayerRoot()
    {
        return ResolvePlayerRoot();
    }

    void SpawnFinisherImpact(Vector3? fallbackPosition)
    {
        GameObject impactPrefab = finisherImpactPrefab != null ? finisherImpactPrefab : glassCrackPrefab;
        if (impactPrefab == null)
            return;

        Transform playerRoot = ResolvePlayerRoot();
        Vector3 spawnPosition = fallbackPosition
            ?? (finisherImpactPoint != null
                ? finisherImpactPoint.position
                : (playerRoot.position + playerRoot.forward * 1.2f + Vector3.up));
        Quaternion spawnRotation = fallbackPosition.HasValue || finisherImpactPoint == null
            ? Quaternion.identity
            : finisherImpactPoint.rotation;
        Instantiate(impactPrefab, spawnPosition, spawnRotation);
    }

    Transform ResolvePlayerRoot()
    {
        if (_playerReferences != null && _playerReferences.PlayerRoot != null)
            return _playerReferences.PlayerRoot;

        return transform;
    }

    Transform ResolveSwordFocus(Transform playerRoot)
    {
        if (swordFocusOverride != null)
            return swordFocusOverride;

        Transform swordVisualFocus = ResolveSwordVisualFocus(playerRoot);
        if (swordVisualFocus != null)
            return swordVisualFocus;

        if (_playerReferences != null && _playerReferences.UltimateSpawnRoot != null)
            return _playerReferences.UltimateSpawnRoot;

        return playerRoot;
    }

    Transform ResolveIntroSwordCameraPivot(Transform playerRoot)
    {
        if (introSwordCameraPivotOverride != null)
            return introSwordCameraPivotOverride;
        return null;
    }

    Transform ResolveIntroSwordLookTarget(Transform playerRoot)
    {
        if (introSwordLookTargetOverride != null)
            return introSwordLookTargetOverride;

        Transform swordTransform = ResolveSwordVisualTransform(playerRoot);
        if (swordTransform == null)
            return ResolveSwordFocus(playerRoot);

        const string anchorName = "RuntimeUltimateSwordLookTarget";
        Transform existing = swordTransform.Find(anchorName);
        if (existing != null)
            return existing;

        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(swordTransform, false);
        if (TryGetLocalMeshBounds(swordTransform, out Bounds localBounds))
        {
            Vector3 localPosition = localBounds.center;
            localPosition.x = Mathf.Lerp(localBounds.min.x, localBounds.max.x, 0.38f);
            localPosition.y = Mathf.Lerp(localBounds.min.y, localBounds.max.y, 0.56f);
            localPosition.z = Mathf.Lerp(localBounds.min.z, localBounds.max.z, 0.5f);
            anchor.localPosition = localPosition;
        }
        else
        {
            anchor.localPosition = Vector3.zero;
        }

        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    Transform ResolveSwordVisualFocus(Transform playerRoot)
    {
        Transform swordTransform = ResolveSwordVisualTransform(playerRoot);

        if (swordTransform == null)
            return null;

        const string anchorName = "RuntimeUltimateSwordFocusAnchor";
        Transform existing = swordTransform.Find(anchorName);
        if (existing != null)
            return existing;

        if (!TryGetLocalMeshBounds(swordTransform, out Bounds localBounds))
            return swordTransform;

        Vector3 size = localBounds.size;
        int axis = 0;
        if (size.y > size.x && size.y >= size.z)
            axis = 1;
        else if (size.z > size.x && size.z >= size.y)
            axis = 2;

        Vector3 localPosition = localBounds.center;
        switch (axis)
        {
            case 1:
                localPosition.y = Mathf.Lerp(localBounds.min.y, localBounds.max.y, 0.5f);
                break;
            case 2:
                localPosition.z = Mathf.Lerp(localBounds.min.z, localBounds.max.z, 0.5f);
                break;
            default:
                localPosition.x = Mathf.Lerp(localBounds.min.x, localBounds.max.x, 0.5f);
                break;
        }

        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(swordTransform, false);
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    Transform ResolveSwordVisualTransform(Transform playerRoot)
    {
        Transform searchRoot = _playerReferences != null && _playerReferences.VisualRoot != null
            ? _playerReferences.VisualRoot
            : playerRoot;
        if (searchRoot == null)
            return null;

        Transform swordTransform = FindChildRecursive(searchRoot, "Object002");
        if (swordTransform == null)
        {
            string[] preferredNames =
            {
                "sword",
                "weapon_r",
                "sword_holder",
                "9CG_Sword(Clone)"
            };

            for (int i = 0; i < preferredNames.Length; i++)
            {
                swordTransform = FindChildRecursive(searchRoot, preferredNames[i]);
                if (swordTransform != null)
                    break;
            }
        }

        return swordTransform;
    }

    static bool TryGetLocalMeshBounds(Transform target, out Bounds bounds)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    Transform ResolvePlayerPresentationSource(Transform playerRoot)
    {
        return playerRoot;
    }

    Transform ResolveVictimPresentationSource(Transform targetTransform)
    {
        if (targetTransform == null)
            return null;

        BossController bossController = targetTransform.GetComponentInParent<BossController>()
            ?? targetTransform.GetComponent<BossController>()
            ?? targetTransform.root.GetComponent<BossController>();
        if (bossController != null)
        {
            BossReferences bossReferences = bossController.GetComponent<BossReferences>();
            if (bossReferences != null)
            {
                if (bossReferences.VisualRig != null && bossReferences.VisualRig.VisualRoot != null)
                    return bossReferences.VisualRig.VisualRoot;

                if (bossReferences.VisualRoot != null)
                    return bossReferences.VisualRoot;
            }

            if (bossController.bossAnimator != null)
                return bossController.bossAnimator.transform.root;

            return bossController.transform;
        }

        Animator targetAnimator = ResolvePresentationAnimator(targetTransform);
        if (targetAnimator != null)
            return targetAnimator.transform.root;

        Renderer renderer = targetTransform.GetComponentInChildren<Renderer>(true) ?? targetTransform.GetComponentInParent<Renderer>();
        return renderer != null ? renderer.transform.root : targetTransform.root;
    }

    Animator ResolvePresentationAnimator(Transform sourceTransform)
    {
        if (sourceTransform == null)
            return null;

        Animator directAnimator = sourceTransform.GetComponent<Animator>();
        if (IsUsablePresentationAnimator(directAnimator))
            return directAnimator;

        Animator[] childAnimators = sourceTransform.GetComponentsInChildren<Animator>(true);
        if (childAnimators != null)
        {
            Animator fallbackAnimator = directAnimator;
            for (int i = 0; i < childAnimators.Length; i++)
            {
                Animator childAnimator = childAnimators[i];
                if (childAnimator == null)
                    continue;

                if (IsUsablePresentationAnimator(childAnimator))
                    return childAnimator;

                if (fallbackAnimator == null)
                    fallbackAnimator = childAnimator;
            }

            if (fallbackAnimator != null)
                return fallbackAnimator;
        }

        return sourceTransform.root != null ? sourceTransform.root.GetComponentInChildren<Animator>(true) : null;
    }

    static bool IsUsablePresentationAnimator(Animator animator)
    {
        return animator != null
            && animator.avatar != null
            && animator.runtimeAnimatorController != null
            && animator.gameObject.activeInHierarchy;
    }

    UltimatePresentationClone EnsurePresentationClone(ref UltimatePresentationClone clone, string cloneName, Transform sourceRoot, Transform anchor, bool preserveSourceLocalPose = false)
    {
        if (sourceRoot == null || anchor == null)
            return null;

        if (clone == null)
        {
            GameObject cloneObject = new GameObject(cloneName);
            clone = cloneObject.AddComponent<UltimatePresentationClone>();
        }

        clone.BuildFromSource(sourceRoot, anchor, cloneName, preserveSourceLocalPose);
        return clone;
    }

    void ReleasePresentationClones()
    {
        _playerPresentationClone?.ClearClone();
        _victimPresentationClone?.ClearClone();
    }

    Vector3 ResolvePresentationSwordFocus(UltimateStageRuntime activeStage, Transform sourceSwordFocus)
    {
        if (_playerPresentationClone != null && _playerPresentationClone.CloneRoot != null)
        {
            Transform mapped = _playerPresentationClone.ResolveMappedTransform(sourceSwordFocus);
            if (mapped != null)
                return mapped.position;

            return _playerPresentationClone.CloneRoot.position + _playerPresentationClone.CloneRoot.forward * 0.35f + Vector3.up * 1.15f;
        }

        if (activeStage != null && activeStage.PlayerAnchor != null)
            return activeStage.PlayerAnchor.position + activeStage.PlayerAnchor.forward * 0.35f + Vector3.up * 1.15f;

        return sourceSwordFocus != null ? sourceSwordFocus.position : transform.position + transform.forward;
    }

    Vector3 ResolveStageVictimPosition(UltimateStageRuntime activeStage)
    {
        if (activeStage == null)
            return transform.position;

        return activeStage.StagePoint(new Vector3(scriptedStageVictimSideOffset, scriptedVictimCenterHeight, Mathf.Max(1.2f, scriptedVictimCenterDistance + 0.45f)));
    }

    Vector3 ResolveStagePlayerOrigin(UltimateStageRuntime activeStage)
    {
        if (activeStage == null)
            return transform.position;

        return activeStage.StagePoint(new Vector3(scriptedStagePlayerSideOffset, 0f, scriptedStagePlayerForwardOffset));
    }

    Vector3 ResolveStageStrikePosition(UltimateStageRuntime activeStage, Vector3 stageVictimPosition, Vector3 orbitDirection, float distance)
    {
        if (activeStage == null)
            return stageVictimPosition - orbitDirection.normalized * distance;

        Vector3 flattenedDirection = GetFlattenedDirection(orbitDirection, activeStage.transform.forward);
        Vector3 basePosition = stageVictimPosition - flattenedDirection * Mathf.Max(1.35f, distance);
        basePosition += activeStage.transform.right * scriptedStagePlayerSideOffset * 0.35f;
        return basePosition;
    }

    void UpdatePresentationActors(UltimateStageRuntime activeStage, Vector3 playerPosition, Vector3 playerLookTarget, Vector3 victimPosition, Vector3 victimLookTarget, Vector3 walkOutPosition)
    {
        if (activeStage == null)
            return;

        SetAnchorPose(activeStage.PlayerAnchor, playerPosition, playerLookTarget);
        SetAnchorPose(activeStage.VictimAnchor, victimPosition, victimLookTarget);

        if (activeStage.FocusAnchor != null)
            activeStage.FocusAnchor.position = Vector3.Lerp(playerPosition, victimPosition, 0.6f) + Vector3.up * 1.1f;

        if (activeStage.WalkOutAnchor != null)
            activeStage.WalkOutAnchor.position = walkOutPosition;

        activeStage.UpdateShotAnchors(playerPosition, victimPosition, walkOutPosition);
    }

    bool TryGetStageShotPose(Transform shotAnchor, out Vector3 position, out Quaternion rotation)
    {
        if (shotAnchor != null)
        {
            position = shotAnchor.position;
            rotation = shotAnchor.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    void SetAnchorPose(Transform anchor, Vector3 position, Vector3 lookTarget)
    {
        if (anchor == null)
            return;

        Vector3 lookDirection = GetFlattenedDirection(lookTarget - position, anchor.forward);
        anchor.SetPositionAndRotation(position, Quaternion.LookRotation(lookDirection, Vector3.up));
    }

    void TriggerVictimPresentationHit(bool heavy)
    {
        if (_victimPresentationClone == null)
            return;

        _victimPresentationClone.TrySetTrigger(heavy ? "Stagger" : "Hit");
    }

    UltimateStageRuntime ResolveUltimateStageRuntime()
    {
        if (!useUltimateStageRuntime)
            return null;

        if (runtimeStage != null)
            _resolvedRuntimeStage = runtimeStage;
        else if (_resolvedRuntimeStage == null)
            _resolvedRuntimeStage = UltimateStageRuntime.GetOrCreate();

        if (_resolvedRuntimeStage == null)
            return null;

        _resolvedRuntimeStage.EnsureRuntimeObjects();
        _resolvedRuntimeStage.SyncLensFrom(Camera.main);
        return _resolvedRuntimeStage;
    }

    CinemachineVirtualCameraBase ResolveSequenceCamera()
    {
        UltimateStageRuntime activeStage = ResolveUltimateStageRuntime();
        if (activeStage != null && activeStage.SequenceCamera != null)
            return activeStage.SequenceCamera;

        if (preferDedicatedRuntimeSequenceCamera)
            return ResolveOrCreateRuntimeSequenceCamera();

        if (vCams != null)
        {
            for (int i = 0; i < vCams.Length; i++)
            {
                if (vCams[i] != null)
                    return vCams[i];
            }
        }

        FinisherStaticCam finisherCam = ResolveFinisherStaticCam();
        if (finisherCam != null && finisherCam.vCam != null)
            return finisherCam.vCam;

        return ResolveFallbackSequenceCamera();
    }

    CinemachineCamera ResolveOrCreateRuntimeSequenceCamera()
    {
        if (_runtimeSequenceCamera != null)
            return _runtimeSequenceCamera;

        const string runtimeCameraName = "__UltimateRuntimeSequenceCam";
        var existing = GameObject.Find(runtimeCameraName);
        if (existing != null)
            _runtimeSequenceCamera = existing.GetComponent<CinemachineCamera>();

        if (_runtimeSequenceCamera == null)
        {
            GameObject go = existing != null ? existing : new GameObject(runtimeCameraName);
            _runtimeSequenceCamera = go.GetComponent<CinemachineCamera>();
            if (_runtimeSequenceCamera == null)
                _runtimeSequenceCamera = go.AddComponent<CinemachineCamera>();

            if (go.GetComponent<CameraShake>() == null)
                go.AddComponent<CameraShake>();
        }

        _runtimeSequenceCamera.Follow = null;
        _runtimeSequenceCamera.LookAt = null;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            CinemachineCompat.TryCopyLensFromUnityCamera(_runtimeSequenceCamera, mainCamera);
        }

        return _runtimeSequenceCamera;
    }

    Vector3 GetTargetFocusPoint(Transform targetTransform)
    {
        if (targetTransform == null)
            return transform.position;

        if (CombatTargetBoundsUtility.TryGetCombinedBounds(targetTransform, out Bounds combinedBounds))
            return combinedBounds.center;

        return targetTransform.position;
    }

    Vector3 GetFlattenedDirection(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        fallback.y = 0f;
        if (fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;

        return Vector3.forward;
    }

    void SnapPlayerPose(Transform playerRoot, Vector3 position, Vector3 lookTarget)
    {
        Vector3 lookDirection = GetFlattenedDirection(lookTarget - position, playerRoot.forward);
        Quaternion rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        SetPlayerPose(playerRoot, position, rotation);
    }

    void SetPlayerPose(Transform playerRoot, Vector3 position, Quaternion rotation)
    {
        if (_characterController != null && _characterController.enabled)
        {
            _characterController.enabled = false;
            playerRoot.SetPositionAndRotation(position, rotation);
            _characterController.enabled = true;
            return;
        }

        playerRoot.SetPositionAndRotation(position, rotation);
    }

    void PrepareScriptedAnimator()
    {
        if (scriptedAnimator == null)
            return;

        _cachedAnimatorUpdateMode = scriptedAnimator.updateMode;
        _cachedAnimatorApplyRootMotion = scriptedAnimator.applyRootMotion;
        _cachedAnimatorStateValid = true;
        scriptedAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        scriptedAnimator.applyRootMotion = false;
    }

    void RestoreScriptedAnimator()
    {
        if (!_cachedAnimatorStateValid || scriptedAnimator == null)
            return;

        scriptedAnimator.updateMode = _cachedAnimatorUpdateMode;
        scriptedAnimator.applyRootMotion = _cachedAnimatorApplyRootMotion;
        _cachedAnimatorStateValid = false;
    }

    void TrySetAnimatorTrigger(string triggerName)
    {
        if (string.IsNullOrWhiteSpace(triggerName))
            return;

        SetAnimatorTriggerIfExists(scriptedAnimator, triggerName);
        _playerPresentationClone?.TrySetTrigger(triggerName);
    }

    void SetAnimatorTriggerIfExists(Animator animator, string triggerName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
            return;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName)
            {
                animator.ResetTrigger(triggerName);
                animator.SetTrigger(triggerName);
                return;
            }
        }
    }

    void PulseSequenceCamera(float amplitude = 0.08f, float duration = 0.06f)
    {
        if (_sequenceCameraShake != null)
            _sequenceCameraShake.Shake(amplitude, duration);
    }

    IEnumerator WaitForSecondsRealtimeSafe(float duration)
    {
        float elapsed = 0f;
        float waitDuration = Mathf.Max(0f, duration);
        while (elapsed < waitDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator Co_MoveCamera(Transform cameraTransform, Vector3 startPos, Quaternion startRot, Vector3 endPos, Quaternion endRot, float duration)
    {
        if (cameraTransform == null)
            yield break;

        float elapsed = 0f;
        float moveDuration = Mathf.Max(0.01f, duration);
        while (elapsed < moveDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            cameraTransform.position = Vector3.Lerp(startPos, endPos, eased);
            cameraTransform.rotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        cameraTransform.SetPositionAndRotation(endPos, endRot);
    }

    IEnumerator Co_MovePlayer(Transform playerRoot, Vector3 startPos, Vector3 endPos, Vector3 lookTarget, float duration)
    {
        if (playerRoot == null)
            yield break;

        Vector3 facingDirection = GetFlattenedDirection(lookTarget - startPos, playerRoot.forward);
        Quaternion facingRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
        bool hadCharacterController = _characterController != null && _characterController.enabled;
        if (hadCharacterController)
            _characterController.enabled = false;

        float elapsed = 0f;
        float moveDuration = Mathf.Max(0.01f, duration);
        while (elapsed < moveDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            Vector3 position = Vector3.Lerp(startPos, endPos, eased);
            playerRoot.SetPositionAndRotation(position, facingRotation);
            yield return null;
        }

        if (hadCharacterController && _characterController != null)
            _characterController.enabled = true;

        SnapPlayerPose(playerRoot, endPos, lookTarget);
    }

    IEnumerator Co_MovePlayerWithPresentation(Transform playerRoot, Vector3 startPos, Vector3 endPos, Vector3 lookTarget, float duration, UltimateStageRuntime activeStage, Vector3 stageStart, Vector3 stageEnd, Vector3 stageVictimPosition)
    {
        if (playerRoot == null)
            yield break;

        Vector3 facingDirection = GetFlattenedDirection(lookTarget - startPos, playerRoot.forward);
        Quaternion facingRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
        bool hadCharacterController = _characterController != null && _characterController.enabled;
        if (hadCharacterController)
            _characterController.enabled = false;

        float elapsed = 0f;
        float moveDuration = Mathf.Max(0.01f, duration);
        while (elapsed < moveDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            Vector3 position = Vector3.Lerp(startPos, endPos, eased);
            playerRoot.SetPositionAndRotation(position, facingRotation);

            if (activeStage != null)
            {
                Vector3 stagePosition = Vector3.Lerp(stageStart, stageEnd, eased);
                UpdatePresentationActors(activeStage, stagePosition, stagePosition + facingDirection, stageVictimPosition, stagePosition, stageEnd);
            }

            yield return null;
        }

        if (hadCharacterController && _characterController != null)
            _characterController.enabled = true;

        SnapPlayerPose(playerRoot, endPos, lookTarget);
        if (activeStage != null)
            UpdatePresentationActors(activeStage, stageEnd, stageEnd + facingDirection, stageVictimPosition, stageEnd, stageEnd);
    }

    IEnumerator Co_MovePresentationActors(UltimateStageRuntime activeStage, Vector3 stageStart, Vector3 stageEnd, Vector3 facingDirection, float duration, Vector3 stageVictimPosition)
    {
        if (activeStage == null)
            yield break;

        Vector3 flattenedFacing = GetFlattenedDirection(facingDirection, activeStage.transform.forward);
        float elapsed = 0f;
        float moveDuration = Mathf.Max(0.01f, duration);
        while (elapsed < moveDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            Vector3 stagePosition = Vector3.Lerp(stageStart, stageEnd, eased);
            UpdatePresentationActors(activeStage, stagePosition, stagePosition + flattenedFacing, stageVictimPosition, stagePosition, stageEnd);
            yield return null;
        }

        UpdatePresentationActors(activeStage, stageEnd, stageEnd + flattenedFacing, stageVictimPosition, stageEnd, stageEnd);
    }

    SequenceCameraState CacheSequenceCameraState(CinemachineVirtualCameraBase sequenceCam)
    {
        return new SequenceCameraState
        {
            priority = sequenceCam.Priority,
            follow = sequenceCam.Follow,
            lookAt = sequenceCam.LookAt,
            position = sequenceCam.transform.position,
            rotation = sequenceCam.transform.rotation
        };
    }

    void RestoreSequenceCamera(CinemachineVirtualCameraBase sequenceCam, SequenceCameraState state)
    {
        if (sequenceCam == null)
            return;

        sequenceCam.Priority = state.priority;
        sequenceCam.Follow = state.follow;
        sequenceCam.LookAt = state.lookAt;
        sequenceCam.transform.SetPositionAndRotation(state.position, state.rotation);
    }

    void ForceCleanupScriptedPresentation()
    {
        LogUltimateCameraState("cleanup-before");
        slashBurstSpawner?.ClearPresentationOverride();
        ReleasePresentationClones();
        _resolvedRuntimeStage?.EndPresentationCapture();
        LogUltimateCameraState("cleanup-after");
    }

    IEnumerator Co_RestoreGameplayCameraNextFrame()
    {
        LogUltimateCameraState("restore-enter");
        yield return null;
        LogUltimateCameraState("restore+1-pre-route");
        if (_wasLockedOnBeforeCinematic && _playerLockOn != null)
        {
            RestoreLockOnTargetIfNeeded();
        }
        else if (_playerLockOn != null)
        {
            _playerLockOn.RestoreFreeLookAfterTimeline();
        }
        else
        {
            _lockOn?.GiveCameraControlToTimeline(false);
        }
        LogUltimateCameraState("restore+1-post-route");
        yield return null;
        LogUltimateCameraState("restore+2-pre-final");
        RestoreFreeLookCameraStateIfNeeded();
        LogUltimateCameraState("restore+2-post-final");
        yield return null;
        LogUltimateCameraState("restore+3");
        if (enableUltimateCameraDebugLogs)
        {
            for (int i = 4; i <= 8; i++)
            {
                yield return null;
                LogUltimateCameraState($"restore+{i}");
            }
        }
        _cameraRestoreCoroutine = null;
    }

    void QueueGameplayCameraRestore()
    {
        if (isActiveAndEnabled && gameObject.activeInHierarchy)
        {
            _cameraRestoreCoroutine = StartCoroutine(Co_RestoreGameplayCameraNextFrame());
            return;
        }

        RestoreGameplayCameraImmediately();
    }

    void RestoreGameplayCameraImmediately()
    {
        if (_wasLockedOnBeforeCinematic && _playerLockOn != null)
        {
            RestoreLockOnTargetIfNeeded();
        }
        else if (_playerLockOn != null)
        {
            _playerLockOn.RestoreFreeLookAfterTimeline();
        }
        else
        {
            _lockOn?.GiveCameraControlToTimeline(false);
        }

        RestoreFreeLookCameraStateIfNeeded();
        _cameraRestoreCoroutine = null;
    }

    void CacheFreeLookCameraState()
    {
        ResolveSceneObjectCaches();

        if (_freeLookCamera == null || !_freeLookCamera)
        {
            _cachedFreeLookStateValid = false;
        }
        else
        {
            _freeLookCamera.CaptureOrbitState(out _cachedFreeLookYaw, out _cachedFreeLookPitch);
            _cachedFreeLookStateValid = true;
        }

        if (_cinemachineFreeLook == null)
        {
            _cachedCinemachineFreeLookStateValid = false;
            return;
        }

        _cachedCinemachineFreeLookStateValid = CinemachineCompat.TryGetLegacyFreeLookAxes(
            _cinemachineFreeLook,
            out _cachedCinemachineFreeLookXAxis,
            out _cachedCinemachineFreeLookYAxis);
    }

    void RestoreFreeLookCameraStateIfNeeded()
    {
        if (!_cachedFreeLookStateValid && !_cachedCinemachineFreeLookStateValid)
            return;

        ResolveSceneObjectCaches();

        if (_lockOn != null && _lockOn.IsLockedOn())
            return;

        _lockOnCameraManager?.ForceRestoreGameplayFreeLook();
        SuppressLegacyUltimateCameras();

        if (_freeLookCamera != null && _freeLookCamera)
            _freeLookCamera.RestoreManualControl(false);

        if (_freeLookCamera != null && _freeLookCamera && _cachedFreeLookStateValid)
            _freeLookCamera.RestoreOrbitState(_cachedFreeLookYaw, _cachedFreeLookPitch);

        if (_cinemachineFreeLook != null && _cachedCinemachineFreeLookStateValid)
            CinemachineCompat.TrySetLegacyFreeLookAxes(
                _cinemachineFreeLook,
                _cachedCinemachineFreeLookXAxis,
                _cachedCinemachineFreeLookYAxis);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _cachedFreeLookStateValid = false;
        _cachedCinemachineFreeLookStateValid = false;
    }

    void RestoreLockOnTargetIfNeeded()
    {
        ResolveSceneObjectCaches();
        if (_playerLockOn == null)
            return;

        Transform cachedTarget = _cachedLockOnTargetBeforeCinematic;
        _cachedLockOnTargetBeforeCinematic = null;
        if (cachedTarget == null || !cachedTarget.gameObject.activeInHierarchy)
        {
            _cachedLockOnCameraRuntimeStateValid = false;
            return;
        }

        AlignPlayerTowardsTargetForLockOnRestore(cachedTarget);
        _playerLockOn.RestoreLockOnAfterTimeline(cachedTarget);

        if (_lockOnCameraManager != null && _playerLockOn.IsLockedOn())
        {
            Transform target = _playerLockOn.GetCurrentTarget();
            if (_playerReferences != null && _playerReferences.LockPivot != null)
                _lockOnCameraManager.SetPlayerPivot(_playerReferences.LockPivot);

            _lockOnCameraManager.RefreshLockOnTarget(target);
            _lockOnCameraManager.StartLockOn(target);
        }

        _cachedLockOnCameraRuntimeStateValid = false;
    }

    void AlignPlayerTowardsTargetForLockOnRestore(Transform target)
    {
        Transform playerRoot = ResolvePlayerRoot();
        if (playerRoot == null || target == null)
            return;

        Vector3 lookDirection = target.position - playerRoot.position;
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        SetPlayerPose(playerRoot, playerRoot.position, targetRotation);
    }

    void CacheLockOnCameraRuntimeStateIfNeeded()
    {
        _cachedLockOnCameraRuntimeStateValid = false;
        if (!_wasLockedOnBeforeCinematic || _lockOnCameraManager == null)
            return;

        _cachedLockOnCameraRuntimeStateValid = _lockOnCameraManager.TryCaptureCurrentRuntimeState(out _cachedLockOnCameraRuntimeState);
    }

    void SuppressLegacyUltimateCameras()
    {
        if (director != null)
        {
            director.Stop();
            director.enabled = false;
        }

        if (vCams != null)
        {
            for (int i = 0; i < vCams.Length; i++)
            {
                CinemachineVirtualCameraBase legacyCam = vCams[i];
                if (legacyCam == null)
                    continue;

                legacyCam.Priority = -100;
                legacyCam.enabled = false;
                legacyCam.gameObject.SetActive(false);

                PlayableDirector legacyDirector = legacyCam.GetComponent<PlayableDirector>();
                if (legacyDirector != null)
                {
                    legacyDirector.Stop();
                    legacyDirector.enabled = false;
                }
            }
        }

        FinisherStaticCam finisherCam = ResolveFinisherStaticCam();
        if (finisherCam != null && finisherCam.vCam != null)
        {
            finisherCam.vCam.Priority = -100;
            finisherCam.vCam.enabled = false;
            finisherCam.vCam.gameObject.SetActive(false);
        }
    }

    void LogUltimateCameraState(string phase)
    {
        if (!enableUltimateCameraDebugLogs)
            return;

        ResolveSceneObjectCaches();
        Camera mainCamera = ResolveMainCamera();
        CinemachineBrain brain = ResolveCinemachineBrain();
        UltimateStageRuntime activeStage = _resolvedRuntimeStage != null ? _resolvedRuntimeStage : ResolveUltimateStageRuntime();

        string activeVcamName = brain != null && brain.ActiveVirtualCamera != null
            ? brain.ActiveVirtualCamera.Name
            : "<null>";
        string mainCameraState = mainCamera != null
            ? $"{mainCamera.name}(enabled={mainCamera.enabled},depth={mainCamera.depth:0.##})"
            : "<null>";
        string stageCameraState = activeStage != null && activeStage.PresentationCamera != null
            ? $"{activeStage.PresentationCamera.name}(enabled={activeStage.PresentationCamera.enabled},depth={activeStage.PresentationCamera.depth:0.##})"
            : "<null>";
        string freeLookDriverState = _freeLookCamera != null
            ? $"{_freeLookCamera.name}(enabled={_freeLookCamera.enabled},active={_freeLookCamera.gameObject.activeInHierarchy})"
            : "<null>";
        string cineFreeLookState = "<null>";
        if (_cinemachineFreeLook != null)
        {
            bool hasAxes = CinemachineCompat.TryGetLegacyFreeLookAxes(_cinemachineFreeLook, out float xAxis, out float yAxis);
            string axisState = hasAxes ? $"x={xAxis:0.###},y={yAxis:0.###}" : "x=<n/a>,y=<n/a>";
            cineFreeLookState =
                $"{_cinemachineFreeLook.name}(enabled={_cinemachineFreeLook.enabled},active={_cinemachineFreeLook.gameObject.activeInHierarchy},priority={_cinemachineFreeLook.Priority},{axisState})";
        }
        string lockOnState = _lockOn != null ? _lockOn.IsLockedOn().ToString() : "<null>";
        string playerLockOnState = _playerLockOn != null ? _playerLockOn.BuildDebugSummary() : "<null>";
        string cameraManagerState = _lockOnCameraManager != null ? _lockOnCameraManager.BuildDebugSummary() : "<null>";
        string stageSequenceState = activeStage != null && activeStage.SequenceCamera != null
            ? $"{activeStage.SequenceCamera.name}(enabled={activeStage.SequenceCamera.enabled},priority={activeStage.SequenceCamera.Priority})"
            : "<null>";
        Transform playerRoot = ResolvePlayerRoot();
        Transform lockOnTarget = _playerLockOn != null ? _playerLockOn.GetCurrentTarget() : null;
        string facingState = "<null>";
        if (playerRoot != null && lockOnTarget != null)
        {
            Vector3 playerForward = playerRoot.forward;
            playerForward.y = 0f;
            Vector3 targetDirection = lockOnTarget.position - playerRoot.position;
            targetDirection.y = 0f;
            if (playerForward.sqrMagnitude > 0.0001f && targetDirection.sqrMagnitude > 0.0001f)
            {
                float facingDot = Vector3.Dot(playerForward.normalized, targetDirection.normalized);
                facingState = $"dot={facingDot:0.###} playerYaw={playerRoot.eulerAngles.y:0.##} targetYaw={Quaternion.LookRotation(targetDirection.normalized, Vector3.up).eulerAngles.y:0.##}";
            }
        }

        Debug.Log(
            $"[ULT CAM] phase={phase} frame={Time.frameCount} " +
            $"main={mainCameraState} stage={stageCameraState} activeVcam={activeVcamName} stageSeq={stageSequenceState} " +
            $"freeDriver={freeLookDriverState} cineFree={cineFreeLookState} " +
            $"lockOn={lockOnState} wasLockedBefore={_wasLockedOnBeforeCinematic} " +
            $"cursor={Cursor.lockState}/{Cursor.visible} timeScale={Time.timeScale:0.###} " +
            $"playerLockOn={playerLockOnState} facing={facingState} cameraMgr={cameraManagerState}",
            this);
    }

    void ResolveSceneObjectCaches()
    {
        if (_freeLookCamera == null || !_freeLookCamera)
            _freeLookCamera = FindAnyObjectByType<FreeLookCamera>();

        if (_cinemachineFreeLook == null || !_cinemachineFreeLook)
            _cinemachineFreeLook = CinemachineCompat.FindLegacyFreeLookCamera();

        if (_lockOnCameraManager == null || !_lockOnCameraManager)
            _lockOnCameraManager = FindAnyObjectByType<LockOnCameraManager>();

        if (_finisherStaticCam == null || !_finisherStaticCam)
            _finisherStaticCam = FindAnyObjectByType<FinisherStaticCam>();

        if (_cachedMainCamera == null || !_cachedMainCamera)
            _cachedMainCamera = Camera.main;

        if ((_cachedCinemachineBrain == null || !_cachedCinemachineBrain) && _cachedMainCamera != null)
            _cachedCinemachineBrain = _cachedMainCamera.GetComponent<CinemachineBrain>();

        if ((_cachedCinemachineBrain == null || !_cachedCinemachineBrain) && _cachedMainCamera == null)
            _cachedCinemachineBrain = FindAnyObjectByType<CinemachineBrain>();

        if (_resolvedRuntimeStage == null && runtimeStage != null)
            _resolvedRuntimeStage = runtimeStage;
    }

    Camera ResolveMainCamera()
    {
        if (_cachedMainCamera == null || !_cachedMainCamera)
            _cachedMainCamera = Camera.main;

        return _cachedMainCamera;
    }

    CinemachineBrain ResolveCinemachineBrain()
    {
        ResolveSceneObjectCaches();
        return _cachedCinemachineBrain;
    }

    FinisherStaticCam ResolveFinisherStaticCam()
    {
        if (_finisherStaticCam == null)
            _finisherStaticCam = FindAnyObjectByType<FinisherStaticCam>();

        return _finisherStaticCam;
    }

    CinemachineVirtualCameraBase ResolveFallbackSequenceCamera()
    {
        if (_fallbackSequenceCamera != null)
            return _fallbackSequenceCamera;

        _fallbackSequenceCamera = FindAnyObjectByType<CinemachineCamera>() ?? FindAnyObjectByType<CinemachineVirtualCameraBase>();
        return _fallbackSequenceCamera;
    }

    BossBreakController ResolveFallbackBreakController()
    {
        if (_fallbackBreakController == null)
            _fallbackBreakController = FindObjectOfType<BossBreakController>(true);

        return _fallbackBreakController;
    }

    struct SequenceCameraState
    {
        public int priority;
        public Transform follow;
        public Transform lookAt;
        public Vector3 position;
        public Quaternion rotation;
    }
}
