using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[DisallowMultipleComponent]
public sealed class UltimateCinematicController : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private PlayableDirector playableDirector;
    [SerializeField] private SignalReceiver signalReceiver;
    [SerializeField] private UltimateCinematicBindings bindings;
    [SerializeField] private UltimateTargetBinder targetBinder;
    [SerializeField] private UltimateHitProcessor hitProcessor;
    [SerializeField] private UltimateVFXPresenter vfxPresenter;
    [SerializeField] private UltimateCameraSessionController cameraSession;
    [SerializeField] private SlashStormVfxController slashStormVfx;
    [SerializeField] private ExplosionVfxController explosionVfx;
    [SerializeField] private UltimateEnemyCinematicState enemyCinematicState;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float finalExplosionReturnDelay = 2f;

    [Header("Player Visual Control")]
    [SerializeField] private GameObject playerVisualRoot;
    [SerializeField] private Renderer[] playerRenderers;
    [SerializeField] private GameObject playerGhostHelper;

    [Header("Shot03 Orbit")]
    [SerializeField] private float shot03OrbitRadius = 5.8f;
    [SerializeField] private float shot03OrbitHeight = 3.05f;
    [SerializeField] private float shot03OrbitDegreesPerSecond = 46f;
    [SerializeField] private float shot03LookAtHeight = 1.05f;
    [SerializeField] private float shot03LookAhead = 0.08f;

    [Header("Signals")]
    [SerializeField] private SignalAsset sigCameraSessionBegin;
    [SerializeField] private SignalAsset sigDrawPoseStart;
    [SerializeField] private SignalAsset sigCloseUpStart;
    [SerializeField] private SignalAsset sigDrawSlashRelease;
    [SerializeField] private SignalAsset sigSlashStormStart;
    [SerializeField] private SignalAsset sigSlashStormSustainStart;
    [SerializeField] private SignalAsset sigPlayerHideForStorm;
    [SerializeField] private SignalAsset sigPlayerShowForWalkout;
    [SerializeField] private SignalAsset sigWalkoutStart;
    [SerializeField] private SignalAsset sigGameplayCommitDamage;
    [SerializeField] private SignalAsset sigExplosionPrepare;
    [SerializeField] private SignalAsset sigFinalExplosion;
    [SerializeField] private SignalAsset sigCameraSessionEnd;
    [SerializeField] private SignalAsset sigGameplayRestore;

    [Header("Debug")]
    [SerializeField] private bool debugLog;

    PlayerUltimateController _owner;
    UltimateSequenceData _data;
    UltimateTargetBinder.BoundTarget _boundTarget;
    Coroutine _walkoutRoutine;
    Coroutine _safetyCleanupRoutine;
    Coroutine _directorStartVerifyRoutine;
    Coroutine _slashStormCameraRoutine;
    bool _finalExplosionPlayed;
    bool _cleanupCompleted = true;
    bool _gameplayDamageCommitted;
    bool _cachedPlayerPoseValid;
    bool _cachedVisualRootPoseValid;
    bool _cachedAnimationTargetPoseValid;
    bool _suppressDirectorStoppedCallback;
    Vector3 _cachedPlayerWorldPosition;
    Quaternion _cachedPlayerWorldRotation = Quaternion.identity;
    Vector3 _cachedVisualRootLocalPosition;
    Quaternion _cachedVisualRootLocalRotation = Quaternion.identity;
    Vector3 _cachedVisualRootLocalScale = Vector3.one;
    Transform _cachedAnimationTargetTransform;
    Vector3 _cachedAnimationTargetLocalPosition;
    Quaternion _cachedAnimationTargetLocalRotation = Quaternion.identity;
    Vector3 _cachedAnimationTargetLocalScale = Vector3.one;

    void Awake()
    {
        if (playerVisualRoot == null && bindings != null && bindings.PlayerVisualRoot != null)
            playerVisualRoot = bindings.PlayerVisualRoot.gameObject;

        if ((playerRenderers == null || playerRenderers.Length == 0) && playerVisualRoot != null)
            playerRenderers = playerVisualRoot.GetComponentsInChildren<Renderer>(true);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        if (playableDirector == null || bindings == null || cameraSession == null)
            return;

        try
        {
            SyncEditorTimelineBindings();
        }
        catch
        {
            // Avoid noisy editor exceptions during partial scene deserialization.
        }
    }
#endif

    void OnEnable()
    {
        if (playableDirector != null)
            playableDirector.stopped += HandleDirectorStopped;
    }

    void OnDisable()
    {
        if (playableDirector != null)
            playableDirector.stopped -= HandleDirectorStopped;

        CleanupIfNeeded("OnDisable");
    }

    void OnDestroy()
    {
        CleanupIfNeeded("OnDestroy");
    }

    public void ConfigureRuntime(
        PlayableDirector runtimePlayableDirector,
        SignalReceiver runtimeSignalReceiver,
        UltimateCinematicBindings runtimeBindings,
        UltimateTargetBinder runtimeTargetBinder,
        UltimateHitProcessor runtimeHitProcessor,
        UltimateVFXPresenter runtimeVfxPresenter,
        UltimateCameraSessionController runtimeCameraSession,
        SlashStormVfxController runtimeSlashStormVfx,
        ExplosionVfxController runtimeExplosionVfx,
        UltimateEnemyCinematicState runtimeEnemyCinematicState,
        GameObject runtimePlayerVisualRoot,
        Renderer[] runtimePlayerRenderers,
        GameObject runtimePlayerGhostHelper,
        SignalAsset runtimeSigCameraSessionBegin,
        SignalAsset runtimeSigDrawPoseStart,
        SignalAsset runtimeSigCloseUpStart,
        SignalAsset runtimeSigDrawSlashRelease,
        SignalAsset runtimeSigSlashStormStart,
        SignalAsset runtimeSigSlashStormSustainStart,
        SignalAsset runtimeSigPlayerHideForStorm,
        SignalAsset runtimeSigPlayerShowForWalkout,
        SignalAsset runtimeSigWalkoutStart,
        SignalAsset runtimeSigGameplayCommitDamage,
        SignalAsset runtimeSigExplosionPrepare,
        SignalAsset runtimeSigFinalExplosion,
        SignalAsset runtimeSigCameraSessionEnd,
        SignalAsset runtimeSigGameplayRestore)
    {
        playableDirector = runtimePlayableDirector;
        signalReceiver = runtimeSignalReceiver;
        bindings = runtimeBindings;
        targetBinder = runtimeTargetBinder;
        hitProcessor = runtimeHitProcessor;
        vfxPresenter = runtimeVfxPresenter;
        cameraSession = runtimeCameraSession;
        slashStormVfx = runtimeSlashStormVfx;
        explosionVfx = runtimeExplosionVfx;
        enemyCinematicState = runtimeEnemyCinematicState;
        playerVisualRoot = runtimePlayerVisualRoot;
        playerRenderers = runtimePlayerRenderers;
        playerGhostHelper = runtimePlayerGhostHelper;
        sigCameraSessionBegin = runtimeSigCameraSessionBegin;
        sigDrawPoseStart = runtimeSigDrawPoseStart;
        sigCloseUpStart = runtimeSigCloseUpStart;
        sigDrawSlashRelease = runtimeSigDrawSlashRelease;
        sigSlashStormStart = runtimeSigSlashStormStart;
        sigSlashStormSustainStart = runtimeSigSlashStormSustainStart;
        sigPlayerHideForStorm = runtimeSigPlayerHideForStorm;
        sigPlayerShowForWalkout = runtimeSigPlayerShowForWalkout;
        sigWalkoutStart = runtimeSigWalkoutStart;
        sigGameplayCommitDamage = runtimeSigGameplayCommitDamage;
        sigExplosionPrepare = runtimeSigExplosionPrepare;
        sigFinalExplosion = runtimeSigFinalExplosion;
        sigCameraSessionEnd = runtimeSigCameraSessionEnd;
        sigGameplayRestore = runtimeSigGameplayRestore;
    }

    public bool Play(PlayerUltimateController owner, UltimateSequenceData data)
    {
        if (owner == null || data == null)
            return false;

        if (!ValidateConfiguration())
            return false;

        CleanupIfNeeded("Replay");
        _cleanupCompleted = false;
        _gameplayDamageCommitted = false;
        _finalExplosionPlayed = false;
        CacheInitialPlayerPose();

        if (!targetBinder.TryBind(owner, data, out _boundTarget, out string failureReason))
        {
            Warn($"Target bind failed: {failureReason}");
            _cleanupCompleted = true;
            return false;
        }

        if (!owner.TryBeginExternalCinematicSession(
                data.Activation.blockInputDuringSequence,
                data.Activation.grantInvulnerability,
                false,
                true))
        {
            Warn("Failed to begin external cinematic session.");
            _cleanupCompleted = true;
            return false;
        }

        _owner = owner;
        _data = data;
        _owner.SetActiveUltimateCinematic(this);

        targetBinder.BeginSequence(owner, data, _boundTarget);
        enemyCinematicState?.EnterCinematicState(_boundTarget);
        hitProcessor.BeginSequence(_boundTarget, data);
        vfxPresenter.BeginSequence(data, targetBinder, null);
        cameraSession.BeginSession(owner);

        if (_data.Activation.alignPlayerOnStart)
        {
            Vector3 introPosition = targetBinder.GetIntroPosition(_data.Movement.introDistance, _data.Movement.introSideOffset);
            targetBinder.SnapPlayerTo(introPosition, GetTargetLookPoint());
        }

        RefreshIntroShotAnchors();

        playableDirector.enabled = true;
        if (!playableDirector.gameObject.activeSelf)
            playableDirector.gameObject.SetActive(true);
        playableDirector.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        playableDirector.extrapolationMode = DirectorWrapMode.None;
        _suppressDirectorStoppedCallback = true;
        try
        {
            playableDirector.Stop();
            if (!playableDirector.playableGraph.IsValid())
                playableDirector.RebuildGraph();
        }
        finally
        {
            _suppressDirectorStoppedCallback = false;
        }

        ConfigureDirectorBindings();
        EnsureSignalBindings();
        playableDirector.RebindPlayableGraphOutputs();
        playableDirector.time = 0d;
        playableDirector.initialTime = 0d;
        playableDirector.Play(playableDirector.playableAsset);
        StartDirectorStartVerify();
        StartSafetyCleanupWatchdog();

        return true;
    }

    public void ForceCancel()
    {
        CleanupIfNeeded("ForceCancel");
    }

    public void CommitGameplayDamage()
    {
        if (_gameplayDamageCommitted || _data == null)
            return;

        hitProcessor.ApplyFinalExplosionHit();
        hitProcessor.FlushBufferedDamage();

        _gameplayDamageCommitted = true;
    }

    public void OnCameraSessionBegin()
    {
        cameraSession?.OnCameraSessionBegin();
    }

    public void OnDrawPoseStart()
    {
        if (_data == null)
            return;

        Vector3 introPosition = targetBinder.GetIntroPosition(_data.Movement.introDistance, _data.Movement.introSideOffset);
        Vector3 targetLookPoint = GetTargetLookPoint();
        targetBinder.SnapPlayerTo(introPosition, targetLookPoint);
        targetBinder.FacePlayerTowardsImmediate(targetLookPoint);
        RefreshIntroShotAnchors();
        RefreshTargetAnchor();
        slashStormVfx?.PlayIntroPose();
    }

    public void OnCloseUpStart()
    {
        RefreshTargetAnchor();
    }

    public void OnDrawSlashRelease()
    {
        if (_data == null)
            return;

        Vector3 lookTarget = GetTargetLookPoint();
        Vector3 dashDestination = targetBinder.GetDashDestination(_data.Movement.dashEndDistance, _data.Movement.dashSideOffset);
        targetBinder.SnapPlayerTo(dashDestination, lookTarget);
        slashStormVfx?.PlayDrawRelease(targetBinder.PlayerRoot.position, lookTarget);
        RefreshTargetAnchor();
    }

    public void OnSlashStormStart()
    {
        RefreshTargetAnchor();
        StartSlashStormCameraOrbit();
    }

    public void OnSlashStormSustainStart()
    {
        slashStormVfx?.BeginStorm(_data, targetBinder);
    }

    public void OnHidePlayerForStorm()
    {
        SetPlayerPresentationState(false, true);
    }

    public void OnShowPlayerForWalkout()
    {
        SetPlayerPresentationState(true, false);
    }

    public void OnWalkoutStart()
    {
        if (_data == null)
            return;

        StopSlashStormCameraOrbit();
        slashStormVfx?.StopStorm(true);

        if (_walkoutRoutine != null)
            StopCoroutine(_walkoutRoutine);

        Vector3 startPosition = targetBinder.PlayerRoot.position;
        Vector3 endPosition = targetBinder.GetWalkoutPosition(_data.Movement.walkoutDistance, _data.Movement.walkoutSideOffset);
        PositionTargetForWalkout(startPosition, endPosition);
        slashStormVfx?.PlayDrawRelease(startPosition, startPosition + (endPosition - startPosition).normalized * 2f);
        vfxPresenter?.PlayWalkout(startPosition, endPosition - startPosition);
        _walkoutRoutine = StartCoroutine(CoWalkoutAndFinish(startPosition, endPosition, Mathf.Max(0.01f, _data.Timings.walkoutDuration)));
    }

    public void OnExplosionPrepare()
    {
        RefreshTargetAnchor();
    }

    public void OnFinalExplosion()
    {
        PlayFinalExplosionOnce();
    }

    public void OnCameraSessionEnd()
    {
        cameraSession?.OnCameraSessionEnd();
    }

    public void OnRestoreGameplay()
    {
        CleanupIfNeeded("TimelineSignal");
    }

    bool ValidateConfiguration()
    {
        bool valid = true;
        valid &= ValidateRef(playableDirector, nameof(playableDirector));
        valid &= ValidateRef(signalReceiver, nameof(signalReceiver));
        valid &= ValidateRef(bindings, nameof(bindings));
        valid &= ValidateRef(targetBinder, nameof(targetBinder));
        valid &= ValidateRef(hitProcessor, nameof(hitProcessor));
        valid &= ValidateRef(vfxPresenter, nameof(vfxPresenter));
        valid &= ValidateRef(cameraSession, nameof(cameraSession));
        valid &= ValidateRef(slashStormVfx, nameof(slashStormVfx));
        valid &= ValidateRef(explosionVfx, nameof(explosionVfx));
        valid &= ValidateRef(enemyCinematicState, nameof(enemyCinematicState));
        valid &= ValidateRef(sigCameraSessionBegin, nameof(sigCameraSessionBegin));
        valid &= ValidateRef(sigDrawPoseStart, nameof(sigDrawPoseStart));
        valid &= ValidateRef(sigCloseUpStart, nameof(sigCloseUpStart));
        valid &= ValidateRef(sigDrawSlashRelease, nameof(sigDrawSlashRelease));
        valid &= ValidateRef(sigSlashStormStart, nameof(sigSlashStormStart));
        valid &= ValidateRef(sigSlashStormSustainStart, nameof(sigSlashStormSustainStart));
        valid &= ValidateRef(sigPlayerHideForStorm, nameof(sigPlayerHideForStorm));
        valid &= ValidateRef(sigPlayerShowForWalkout, nameof(sigPlayerShowForWalkout));
        valid &= ValidateRef(sigWalkoutStart, nameof(sigWalkoutStart));
        valid &= ValidateRef(sigGameplayCommitDamage, nameof(sigGameplayCommitDamage));
        valid &= ValidateRef(sigExplosionPrepare, nameof(sigExplosionPrepare));
        valid &= ValidateRef(sigCameraSessionEnd, nameof(sigCameraSessionEnd));
        valid &= ValidateRef(sigGameplayRestore, nameof(sigGameplayRestore));
        valid &= ValidateRef(sigFinalExplosion, nameof(sigFinalExplosion));

        if (playerVisualRoot == null && bindings != null && bindings.PlayerVisualRoot != null)
            playerVisualRoot = bindings.PlayerVisualRoot.gameObject;

        if ((playerRenderers == null || playerRenderers.Length == 0) && playerVisualRoot != null)
            playerRenderers = playerVisualRoot.GetComponentsInChildren<Renderer>(true);

        if (playerRenderers == null || playerRenderers.Length == 0)
        {
            Warn("Player renderers are not assigned.");
            valid = false;
        }

        if (bindings != null)
        {
            valid &= ValidateRef(bindings.PlayerRoot, nameof(bindings.PlayerRoot));
            valid &= ValidateRef(bindings.PlayerAnimator, nameof(bindings.PlayerAnimator));
            valid &= ValidateRef(bindings.TargetRoot, nameof(bindings.TargetRoot));
            valid &= ValidateRef(bindings.TargetCenter, nameof(bindings.TargetCenter));
            valid &= ValidateRef(bindings.TargetExplosionAnchor, nameof(bindings.TargetExplosionAnchor));
        }

        return valid;
    }

    bool ValidateRef(UnityEngine.Object value, string fieldName)
    {
        if (value != null)
            return true;

        Warn($"Missing required reference: {fieldName}");
        return false;
    }

    void ConfigureDirectorBindings()
    {
        TimelineAsset timelineAsset = playableDirector.playableAsset as TimelineAsset;
        if (timelineAsset == null)
        {
            Warn("PlayableDirector asset is not a TimelineAsset.");
            return;
        }

        ConfigureTimelineAnimationClips(timelineAsset);
        Animator animationTarget = ResolveTimelineAnimationTarget();

        foreach (TrackAsset track in timelineAsset.GetOutputTracks())
        {
            if (track is AnimationTrack)
            {
                playableDirector.SetGenericBinding(track, animationTarget);
                continue;
            }

            if (track is SignalTrack)
            {
                playableDirector.SetGenericBinding(track, signalReceiver);
                continue;
            }

            if (track is CinemachineTrack)
            {
                playableDirector.SetGenericBinding(track, cameraSession.Brain);
            }
        }

        BindCinemachineShotReferences(timelineAsset);
    }

    void ConfigureTimelineAnimationClips(TimelineAsset timelineAsset)
    {
        if (timelineAsset == null || _data == null || _data.CinematicAnimation == null)
            return;

        foreach (TrackAsset track in timelineAsset.GetOutputTracks())
        {
            if (track is not AnimationTrack)
                continue;

            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip.asset is not AnimationPlayableAsset animationAsset)
                    continue;

                if (clip.displayName.Contains("IntroPose", StringComparison.OrdinalIgnoreCase) && _data.CinematicAnimation.introPoseClip != null)
                {
                    animationAsset.clip = _data.CinematicAnimation.introPoseClip;
                    ConfigureAnimationClipRange(
                        clip,
                        _data.CinematicAnimation.introPoseClip,
                        _data.CinematicAnimation.introPoseClipStartNormalized,
                        _data.CinematicAnimation.introPoseClipEndNormalized,
                        true);
                }
                else if (clip.displayName.Contains("DrawSlash", StringComparison.OrdinalIgnoreCase) && _data.CinematicAnimation.dashSlashClip != null)
                {
                    animationAsset.clip = _data.CinematicAnimation.dashSlashClip;
                    ConfigureAnimationClipRange(
                        clip,
                        _data.CinematicAnimation.dashSlashClip,
                        _data.CinematicAnimation.dashSlashClipStartNormalized,
                        _data.CinematicAnimation.dashSlashClipEndNormalized,
                        false);
                }
                else if (clip.displayName.Contains("Walkout", StringComparison.OrdinalIgnoreCase) && _data.CinematicAnimation.walkoutClip != null)
                {
                    animationAsset.clip = _data.CinematicAnimation.walkoutClip;
                    ConfigureAnimationClipRange(
                        clip,
                        _data.CinematicAnimation.walkoutClip,
                        _data.CinematicAnimation.walkoutClipStartNormalized,
                        _data.CinematicAnimation.walkoutClipEndNormalized,
                        false);
                }
            }
        }
    }

    static void ConfigureAnimationClipRange(
        TimelineClip timelineClip,
        AnimationClip animationClip,
        float startNormalized,
        float endNormalized,
        bool holdFrame)
    {
        if (timelineClip == null || animationClip == null)
            return;

        float clipLength = Mathf.Max(1f / 60f, animationClip.length);
        float rangeStart = Mathf.Clamp01(startNormalized);
        float rangeEnd = Mathf.Clamp01(endNormalized);
        if (rangeEnd < rangeStart)
        {
            float swap = rangeStart;
            rangeStart = rangeEnd;
            rangeEnd = swap;
        }

        float startTime = Mathf.Clamp(rangeStart * clipLength, 0f, Mathf.Max(0f, clipLength - 0.0001f));
        float rangeDuration = Mathf.Max(0f, (rangeEnd - rangeStart) * clipLength);
        timelineClip.clipIn = startTime;
        timelineClip.timeScale = holdFrame || rangeDuration <= 0.0001f
            ? 0.0001d
            : Mathf.Max(0.0001f, rangeDuration / Mathf.Max(0.0001f, (float)timelineClip.duration));
    }

    Animator ResolveTimelineAnimationTarget()
    {
        if (targetBinder != null && targetBinder.PlayerPresentationAnimator != null)
            return targetBinder.PlayerPresentationAnimator;

        if (bindings != null && bindings.PlayerAnimator != null)
            return bindings.PlayerAnimator;

        return null;
    }

#if UNITY_EDITOR
    void SyncEditorTimelineBindings()
    {
        TimelineAsset timelineAsset = playableDirector.playableAsset as TimelineAsset;
        if (timelineAsset == null)
            return;

        foreach (TrackAsset track in timelineAsset.GetOutputTracks())
        {
            if (track is AnimationTrack && bindings.PlayerAnimator != null)
                playableDirector.SetGenericBinding(track, bindings.PlayerAnimator);
            else if (track is SignalTrack && signalReceiver != null)
                playableDirector.SetGenericBinding(track, signalReceiver);
            else if (track is CinemachineTrack && cameraSession.Brain != null)
                playableDirector.SetGenericBinding(track, cameraSession.Brain);
        }

        BindCinemachineShotReferences(timelineAsset);
        UnityEditor.EditorUtility.SetDirty(playableDirector);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    void BindCinemachineShotReferences(TimelineAsset timelineAsset)
    {
        if (timelineAsset == null || cameraSession == null || cameraSession.SequenceCameras == null)
            return;

        var cameras = cameraSession.SequenceCameras;
        foreach (TrackAsset track in timelineAsset.GetOutputTracks())
        {
            if (track is not CinemachineTrack)
                continue;

            int cameraIndex = 0;
            foreach (TimelineClip clip in track.GetClips())
            {
                var shot = clip.asset as CinemachineShot;
                if (shot == null)
                    continue;

                if (cameraIndex >= cameras.Length || cameras[cameraIndex] == null)
                {
                    Warn($"Missing sequence camera for clip '{clip.displayName}' at index {cameraIndex}.");
                    cameraIndex++;
                    continue;
                }

                playableDirector.SetReferenceValue(shot.VirtualCamera.exposedName, cameras[cameraIndex]);
                cameraIndex++;
            }
        }
    }

    void EnsureSignalBindings()
    {
        if (signalReceiver == null)
            return;

        BindSignal(sigCameraSessionBegin, OnCameraSessionBegin);
        BindSignal(sigDrawPoseStart, OnDrawPoseStart);
        BindSignal(sigCloseUpStart, OnCloseUpStart);
        BindSignal(sigDrawSlashRelease, OnDrawSlashRelease);
        BindSignal(sigSlashStormStart, OnSlashStormStart);
        BindSignal(sigSlashStormSustainStart, OnSlashStormSustainStart);
        BindSignal(sigPlayerHideForStorm, OnHidePlayerForStorm);
        BindSignal(sigPlayerShowForWalkout, OnShowPlayerForWalkout);
        BindSignal(sigWalkoutStart, OnWalkoutStart);
        BindSignal(sigExplosionPrepare, OnExplosionPrepare);
        BindSignal(sigFinalExplosion, OnFinalExplosion);
        BindSignal(sigCameraSessionEnd, OnCameraSessionEnd);
        BindSignal(sigGameplayRestore, OnRestoreGameplay);
        BindSignal(sigGameplayCommitDamage, _owner != null ? _owner.OnUltimateDamageCommit : (UnityAction)null);

    }

    void BindSignal(SignalAsset signal, UnityAction action)
    {
        if (signal == null)
            return;

        UnityEvent reaction = signalReceiver.GetReaction(signal);
        if (reaction == null)
        {
            reaction = new UnityEvent();
            signalReceiver.AddReaction(signal, reaction);
        }

        reaction.RemoveAllListeners();
        if (action != null)
            reaction.AddListener(action);
    }

    IEnumerator CoWalkoutAndFinish(Vector3 startPosition, Vector3 endPosition, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && _data != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            Vector3 lookTarget = targetBinder.GetWalkoutLookTarget(endPosition);
            targetBinder.MovePlayerLinear(startPosition, endPosition, lookTarget, normalized);
            yield return null;
        }

        _walkoutRoutine = null;
        if (_cleanupCompleted || _data == null)
            yield break;

        PlayFinalExplosionOnce();
        yield return new WaitForSecondsRealtime(finalExplosionReturnDelay);
        CleanupIfNeeded("WalkoutExplosionEnd");
    }

    void PlayFinalExplosionOnce()
    {
        if (_finalExplosionPlayed)
            return;

        _finalExplosionPlayed = true;
        explosionVfx?.PlayFinalExplosion(GetExplosionPoint());
        CommitGameplayDamage();
    }

    void StartSlashStormCameraOrbit()
    {
        StopSlashStormCameraOrbit();

        if (bindings == null || bindings.Shot03Pos == null || bindings.Shot03LookAt == null)
            return;

        _slashStormCameraRoutine = StartCoroutine(CoSlashStormCameraOrbit());
    }

    void StopSlashStormCameraOrbit()
    {
        if (_slashStormCameraRoutine == null)
            return;

        StopCoroutine(_slashStormCameraRoutine);
        _slashStormCameraRoutine = null;
    }

    IEnumerator CoSlashStormCameraOrbit()
    {
        float angle = 0f;

        while (!_cleanupCompleted && _data != null)
        {
            UpdateSlashStormShotAnchors(angle);
            angle += shot03OrbitDegreesPerSecond * Time.unscaledDeltaTime;
            yield return null;
        }

        _slashStormCameraRoutine = null;
    }

    void UpdateSlashStormShotAnchors(float angleDegrees)
    {
        if (bindings == null || bindings.Shot03Pos == null || bindings.Shot03LookAt == null)
            return;

        Vector3 center = GetTargetLookPoint();
        Vector3 playerPosition = bindings.PlayerRoot != null ? bindings.PlayerRoot.position : center;
        Vector3 toPlayer = playerPosition - center;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.0001f)
            toPlayer = Vector3.back;
        toPlayer.Normalize();

        Quaternion orbitRotation = Quaternion.AngleAxis(angleDegrees, Vector3.up);
        Vector3 orbitDirection = orbitRotation * toPlayer;
        Vector3 cameraPosition = center + orbitDirection * shot03OrbitRadius + Vector3.up * shot03OrbitHeight;

        bindings.Shot03Pos.position = cameraPosition;
        bindings.Shot03LookAt.position = center + Vector3.up * shot03LookAtHeight + orbitDirection * shot03LookAhead;
    }

    Vector3 GetTargetLookPoint()
    {
        if (bindings != null && bindings.TargetCenter != null)
            return bindings.TargetCenter.position;

        if (_boundTarget != null && _boundTarget.TargetRoot != null)
            return _boundTarget.TargetRoot.position;

        return bindings != null && bindings.PlayerRoot != null
            ? bindings.PlayerRoot.position + bindings.PlayerRoot.forward * 2f
            : transform.position + transform.forward * 2f;
    }

    Vector3 GetExplosionPoint()
    {
        if (bindings != null && bindings.TargetExplosionAnchor != null)
            return bindings.TargetExplosionAnchor.position;

        if (_boundTarget != null && _boundTarget.TargetRoot != null)
            return _boundTarget.TargetRoot.position;

        return GetTargetLookPoint();
    }

    void PositionTargetForWalkout(Vector3 startPosition, Vector3 endPosition)
    {
        if (bindings == null || bindings.TargetCineHoldAnchor == null)
        {
            RefreshTargetAnchor();
            return;
        }

        Vector3 walkDirection = endPosition - startPosition;
        walkDirection.y = 0f;
        if (walkDirection.sqrMagnitude <= 0.0001f)
        {
            RefreshTargetAnchor();
            return;
        }

        walkDirection.Normalize();
        float holdDistance = Mathf.Max(1.2f, _data != null ? _data.Movement.walkoutDistance * 0.72f : 1.8f);
        Vector3 holdPosition = endPosition - walkDirection * holdDistance;
        holdPosition.y = bindings.TargetRoot != null ? bindings.TargetRoot.position.y : holdPosition.y;
        bindings.TargetCineHoldAnchor.position = holdPosition;
        RefreshTargetAnchor();
    }

    void RefreshTargetAnchor()
    {
        if (enemyCinematicState == null)
            return;

        Vector3 targetPosition = bindings != null && bindings.TargetCineHoldAnchor != null
            ? bindings.TargetCineHoldAnchor.position
            : GetTargetLookPoint();
        enemyCinematicState.RefreshAnchor(targetPosition, bindings != null && bindings.PlayerRoot != null ? bindings.PlayerRoot.position : transform.position);
    }

    void SetPlayerPresentationState(bool showRenderers, bool showGhost)
    {
        if (playerRenderers != null)
        {
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                Renderer renderer = playerRenderers[i];
                if (renderer != null)
                    renderer.enabled = showRenderers;
            }
        }

        if (playerGhostHelper != null)
            playerGhostHelper.SetActive(showGhost);
    }

    void HandleDirectorStopped(PlayableDirector _)
    {
        if (_suppressDirectorStoppedCallback)
            return;

        CleanupIfNeeded("DirectorStopped");
    }

    void StartSafetyCleanupWatchdog()
    {
        if (_safetyCleanupRoutine != null)
        {
            StopCoroutine(_safetyCleanupRoutine);
            _safetyCleanupRoutine = null;
        }

        double duration = playableDirector != null ? playableDirector.duration : 0d;
        if (duration <= 0d && playableDirector != null && playableDirector.playableAsset is TimelineAsset asset)
            duration = asset.duration;

        float timeout = Mathf.Max(0.5f, (float)duration + 0.35f);
        _safetyCleanupRoutine = StartCoroutine(CoSafetyCleanupWatchdog(timeout));
    }

    void StartDirectorStartVerify()
    {
        if (_directorStartVerifyRoutine != null)
        {
            StopCoroutine(_directorStartVerifyRoutine);
            _directorStartVerifyRoutine = null;
        }

        _directorStartVerifyRoutine = StartCoroutine(CoDirectorStartVerify());
    }

    IEnumerator CoDirectorStartVerify()
    {
        yield return null;
        if (_cleanupCompleted || playableDirector == null)
        {
            _directorStartVerifyRoutine = null;
            yield break;
        }

        if (playableDirector.state != PlayState.Playing)
        {
            playableDirector.enabled = true;
            if (!playableDirector.gameObject.activeSelf)
                playableDirector.gameObject.SetActive(true);
            _suppressDirectorStoppedCallback = true;
            try
            {
                playableDirector.Stop();
                playableDirector.time = 0d;
                playableDirector.initialTime = 0d;
            }
            finally
            {
                _suppressDirectorStoppedCallback = false;
            }

            ConfigureDirectorBindings();
            EnsureSignalBindings();
            playableDirector.RebindPlayableGraphOutputs();
            playableDirector.Play(playableDirector.playableAsset);
            yield return null;
        }

        _directorStartVerifyRoutine = null;

        if (_cleanupCompleted || playableDirector == null)
            yield break;

        if (playableDirector.state == PlayState.Playing)
            yield break;

        Warn($"Director failed to start. state={playableDirector.state}, time={playableDirector.time:0.###}");
        CleanupIfNeeded("DirectorStartFailed");
    }

    void RefreshIntroShotAnchors()
    {
        if (bindings == null || bindings.PlayerRoot == null)
            return;

        Transform shot01Pos = bindings.Shot01Pos;
        Transform shot01LookAt = bindings.Shot01LookAt;
        Transform shot02Pos = bindings.Shot02Pos;
        Transform shot02LookAt = bindings.Shot02LookAt;
        if (shot01Pos == null || shot01LookAt == null || shot02Pos == null || shot02LookAt == null)
            return;

        Vector3 playerPosition = bindings.PlayerRoot.position;
        Vector3 forward = bindings.PlayerRoot.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = GetTargetLookPoint() - playerPosition;

        forward.Normalize();
        Vector3 right = bindings.PlayerRoot.right;
        right.y = 0f;
        if (right.sqrMagnitude <= 0.0001f)
            right = Vector3.Cross(Vector3.up, forward);
        right.Normalize();
        Vector3 chest = playerPosition + Vector3.up * 1.08f;
        Vector3 head = playerPosition + Vector3.up * 1.38f;

        Vector3 swordFocus = targetBinder != null
            ? targetBinder.GetPlayerIntroSwordLookPoint(new Vector3(-0.04f, -0.04f, 0f))
            : chest;
        Vector3 weaponLook = Vector3.Lerp(swordFocus, chest, 0.3f);

        shot01Pos.position = swordFocus + forward * 1.65f - right * 0.42f + Vector3.up * 0.32f;
        shot01LookAt.position = weaponLook;

        shot02Pos.position = chest + forward * 2.45f - right * 0.2f + Vector3.up * 0.36f;
        shot02LookAt.position = Vector3.Lerp(chest, head, 0.24f) - right * 0.02f;
    }

    IEnumerator CoSafetyCleanupWatchdog(float timeout)
    {
        float elapsed = 0f;
        while (!_cleanupCompleted && elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _safetyCleanupRoutine = null;

        if (!_cleanupCompleted)
            CleanupIfNeeded("SafetyWatchdog");
    }

    void CacheInitialPlayerPose()
    {
        _cachedPlayerPoseValid = false;
        _cachedVisualRootPoseValid = false;
        _cachedAnimationTargetPoseValid = false;
        _cachedAnimationTargetTransform = null;

        if (bindings == null || bindings.PlayerRoot == null)
            return;

        _cachedPlayerWorldPosition = bindings.PlayerRoot.position;
        _cachedPlayerWorldRotation = bindings.PlayerRoot.rotation;
        _cachedPlayerPoseValid = true;

        if (bindings.PlayerVisualRoot != null)
        {
            _cachedVisualRootLocalPosition = bindings.PlayerVisualRoot.localPosition;
            _cachedVisualRootLocalRotation = bindings.PlayerVisualRoot.localRotation;
            _cachedVisualRootLocalScale = bindings.PlayerVisualRoot.localScale;
            _cachedVisualRootPoseValid = true;
        }

        Animator animationTarget = ResolveTimelineAnimationTarget();
        if (animationTarget != null)
        {
            _cachedAnimationTargetTransform = animationTarget.transform;
            _cachedAnimationTargetLocalPosition = animationTarget.transform.localPosition;
            _cachedAnimationTargetLocalRotation = animationTarget.transform.localRotation;
            _cachedAnimationTargetLocalScale = animationTarget.transform.localScale;
            _cachedAnimationTargetPoseValid = true;
        }
    }

    void RestoreInitialPlayerPoseIfNeeded()
    {
        if (!_cachedPlayerPoseValid || bindings == null || bindings.PlayerRoot == null || targetBinder == null)
            return;

        Vector3 lookTarget = _cachedPlayerWorldPosition + (_cachedPlayerWorldRotation * Vector3.forward);
        targetBinder.SnapPlayerTo(_cachedPlayerWorldPosition, lookTarget);

        if (_cachedVisualRootPoseValid && bindings.PlayerVisualRoot != null)
        {
            bindings.PlayerVisualRoot.localPosition = _cachedVisualRootLocalPosition;
            bindings.PlayerVisualRoot.localRotation = _cachedVisualRootLocalRotation;
            bindings.PlayerVisualRoot.localScale = _cachedVisualRootLocalScale;
        }

        Animator animationTarget = ResolveTimelineAnimationTarget();
        if (_cachedAnimationTargetPoseValid && _cachedAnimationTargetTransform != null)
        {
            _cachedAnimationTargetTransform.localPosition = _cachedAnimationTargetLocalPosition;
            _cachedAnimationTargetTransform.localRotation = _cachedAnimationTargetLocalRotation;
            _cachedAnimationTargetTransform.localScale = _cachedAnimationTargetLocalScale;
        }

        if (animationTarget != null)
        {
            animationTarget.Rebind();
            animationTarget.Update(0f);
        }
    }

    void CleanupIfNeeded(string reason)
    {
        if (_cleanupCompleted)
            return;

        _cleanupCompleted = true;

        if (_walkoutRoutine != null)
        {
            StopCoroutine(_walkoutRoutine);
            _walkoutRoutine = null;
        }

        if (_slashStormCameraRoutine != null)
        {
            StopCoroutine(_slashStormCameraRoutine);
            _slashStormCameraRoutine = null;
        }

        if (_safetyCleanupRoutine != null)
        {
            StopCoroutine(_safetyCleanupRoutine);
            _safetyCleanupRoutine = null;
        }

        if (_directorStartVerifyRoutine != null)
        {
            StopCoroutine(_directorStartVerifyRoutine);
            _directorStartVerifyRoutine = null;
        }

        if (playableDirector != null)
        {
            _suppressDirectorStoppedCallback = true;
            try
            {
                playableDirector.time = 0d;
                playableDirector.Stop();
            }
            finally
            {
                _suppressDirectorStoppedCallback = false;
            }
        }

        SetPlayerPresentationState(true, false);
        slashStormVfx?.StopStorm(false);
        vfxPresenter?.EndSequence();
        hitProcessor?.EndSequence();
        RestoreInitialPlayerPoseIfNeeded();
        targetBinder?.EndSequence();
        enemyCinematicState?.CleanupIfNeeded();
        cameraSession?.CleanupIfNeeded();

        if (_owner != null)
        {
            _owner.ClearActiveUltimateCinematic(this);
            _owner.EndExternalCinematicSession();
        }

        _owner = null;
        _data = null;
        _boundTarget = null;
        _gameplayDamageCommitted = false;
        _finalExplosionPlayed = false;
        _cachedPlayerPoseValid = false;
        _cachedVisualRootPoseValid = false;
        _cachedAnimationTargetPoseValid = false;
        _cachedAnimationTargetTransform = null;

    }

    void Warn(string message)
    {
        Debug.LogWarning($"[UltimateCinematic] {message}", this);
    }
}
