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

    [Header("Player Visual Control")]
    [SerializeField] private GameObject playerVisualRoot;
    [SerializeField] private Renderer[] playerRenderers;
    [SerializeField] private GameObject playerGhostHelper;

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
    bool _cleanupCompleted = true;
    bool _gameplayDamageCommitted;

    void Awake()
    {
        if (playerVisualRoot == null && bindings != null && bindings.PlayerVisualRoot != null)
            playerVisualRoot = bindings.PlayerVisualRoot.gameObject;

        if ((playerRenderers == null || playerRenderers.Length == 0) && playerVisualRoot != null)
            playerRenderers = playerVisualRoot.GetComponentsInChildren<Renderer>(true);
    }

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

    public bool Play(PlayerUltimateController owner, UltimateSequenceData data)
    {
        if (owner == null || data == null)
            return false;

        if (!ValidateConfiguration())
            return false;

        CleanupIfNeeded("Replay");
        _cleanupCompleted = false;
        _gameplayDamageCommitted = false;

        if (!targetBinder.TryBind(owner, data, out _boundTarget, out string failureReason))
        {
            Warn($"Target bind failed: {failureReason}");
            _cleanupCompleted = true;
            return false;
        }

        if (!owner.TryBeginExternalCinematicSession(
                data.Activation.blockInputDuringSequence,
                data.Activation.grantInvulnerability,
                data.Activation.freezeGameplayTime,
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

        ConfigureDirectorBindings();
        EnsureSignalBindings();

        playableDirector.time = 0d;
        playableDirector.Play();

        if (debugLog)
            Debug.Log("[UltimateCinematic] Play started.", this);

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
        targetBinder.SnapPlayerTo(introPosition, GetTargetLookPoint());
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

        slashStormVfx?.StopStorm(true);

        if (_walkoutRoutine != null)
            StopCoroutine(_walkoutRoutine);

        Vector3 startPosition = targetBinder.PlayerRoot.position;
        Vector3 endPosition = targetBinder.GetWalkoutPosition(_data.Movement.walkoutDistance, _data.Movement.walkoutSideOffset);
        slashStormVfx?.PlayDrawRelease(startPosition, startPosition + (endPosition - startPosition).normalized * 2f);
        vfxPresenter?.PlayWalkout(startPosition, endPosition - startPosition);
        _walkoutRoutine = StartCoroutine(CoWalkout(startPosition, endPosition, Mathf.Max(0.01f, _data.Timings.walkoutDuration)));
    }

    public void OnExplosionPrepare()
    {
        RefreshTargetAnchor();
    }

    public void OnFinalExplosion()
    {
        explosionVfx?.PlayFinalExplosion(GetExplosionPoint());
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

        foreach (TrackAsset track in timelineAsset.GetOutputTracks())
        {
            if (track is AnimationTrack)
            {
                playableDirector.SetGenericBinding(track, bindings.PlayerAnimator);
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

    IEnumerator CoWalkout(Vector3 startPosition, Vector3 endPosition, float duration)
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
        CleanupIfNeeded("DirectorStopped");
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

        SetPlayerPresentationState(true, false);
        slashStormVfx?.StopStorm(false);
        vfxPresenter?.EndSequence();
        hitProcessor?.EndSequence();
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

        if (debugLog)
            Debug.Log($"[UltimateCinematic] Cleanup: {reason}", this);
    }

    void Warn(string message)
    {
        Debug.LogWarning($"[UltimateCinematic] {message}", this);
    }
}
