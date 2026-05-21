using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateSequencePlayer : MonoBehaviour
{
    const float VictimAnchorRefreshSqrThreshold = 0.0004f;

    [Header("참조")]
    [SerializeField] private UltimateTargetBinder targetBinder;
    [SerializeField] private UltimateCameraDirector cameraDirector;
    [SerializeField] private UltimateHitProcessor hitProcessor;
    [SerializeField] private UltimateVFXPresenter vfxPresenter;

    [Header("디버그")]
    [SerializeField] private bool debugLog;

    readonly HashSet<int> _animatorTriggerHashes = new HashSet<int>();

    PlayerUltimateController _owner;
    UltimateSequenceData _data;
    UltimateTargetBinder.BoundTarget _boundTarget;
    Coroutine _hitStopCoroutine;
    float _lastHitStopRealtime;
    int _introTriggerHash;
    int _dashTriggerHash;
    int _multiSlashTriggerHash;
    int _finalTriggerHash;
    int _walkoutTriggerHash;
    bool _hasIntroFallbackState;
    bool _hasDashFallbackState;
    bool _hasMultiSlashFallbackState;
    bool _hasFinalFallbackState;
    bool _hasWalkoutFallbackState;
    int _introFallbackLayerIndex;
    int _dashFallbackLayerIndex;
    int _multiSlashFallbackLayerIndex;
    int _finalFallbackLayerIndex;
    int _walkoutFallbackLayerIndex;
    int _introFallbackStateHash;
    int _dashFallbackStateHash;
    int _multiSlashFallbackStateHash;
    int _finalFallbackStateHash;
    int _walkoutFallbackStateHash;
    int _slashIndex;
    float _stepTimer;
    float _phaseElapsed;
    Vector3 _phaseStartPosition;
    Vector3 _phaseEndPosition;
    bool _sequenceActive;
    bool _dashHitApplied;
    bool _finalExplosionApplied;
    UltimateSequenceData.CameraShotSettings _currentPhaseShot;
    float _currentTargetBottomToCenterRatio = 0.8f;
    float _currentTargetVerticalOffset;
    Vector3 _lastVictimAnchorLookTarget;
    Vector3 _finalExplosionLookTarget;
    bool _hasVictimAnchorLookTarget;
    readonly UltimateAnimatorClipSampler _playerClipSampler = new UltimateAnimatorClipSampler("UltimatePlayerPhaseClip");
    readonly UltimateAnimatorClipSampler _presentationClipSampler = new UltimateAnimatorClipSampler("UltimatePresentationPhaseClip");
    AnimationClip _activePhaseClip;

    public UltimateSequencePhase CurrentPhase { get; private set; } = UltimateSequencePhase.None;
    public bool IsSequenceActive => _sequenceActive;
    public string ActivePhaseClipName => _activePhaseClip != null ? _activePhaseClip.name : "-";

    void Awake()
    {
        ResolveReferences(true);
        enabled = false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences(false);
    }
#endif

    void OnDisable()
    {
        StopPhaseClipSampling();
        if (_sequenceActive)
            CleanupSequence(true);
    }

    void OnDestroy()
    {
        _playerClipSampler.Dispose();
        _presentationClipSampler.Dispose();
    }

    public bool TryPlay(PlayerUltimateController owner, UltimateSequenceData data)
    {
        if (_sequenceActive)
            return false;

        ResolveReferences(true);
        _owner = owner;
        _data = data != null ? data : UltimateSequenceData.CreateRuntimeDefaultInstance();

        if (!targetBinder.TryBind(owner, _data, out _boundTarget, out string failureReason))
        {
            if (ShouldLog)
                Debug.Log($"[UltimateSequence] Start blocked: {failureReason}", this);
            return false;
        }

        if (!_owner.TryBeginExternalCinematicSession(
                _data.Activation.blockInputDuringSequence,
                _data.Activation.grantInvulnerability,
                _data.Activation.freezeGameplayTime,
                true))
        {
            return false;
        }

        CacheAnimatorTriggers();
        targetBinder.BeginSequence(_owner, _data, _boundTarget);
        cameraDirector.BeginSequence(_owner, targetBinder, _data);
        _owner.PrepareModernIntroStagePresentation(cameraDirector.ActiveStage, _boundTarget != null ? _boundTarget.TargetRoot : null, targetBinder);
        hitProcessor.BeginSequence(_boundTarget, _data);
        vfxPresenter.BeginSequence(_data, targetBinder, cameraDirector);

        if (_data.Activation.alignPlayerOnStart)
        {
            Vector3 introAimPoint = _boundTarget != null && _boundTarget.TargetRoot != null
                ? targetBinder.GetTargetAimPoint(_boundTarget.TargetRoot, 0.8f, 0f)
                : targetBinder.GetTargetAimPoint(0.8f, 0f);
            Vector3 awayDirection = GetFlattenedDirection(targetBinder.PlayerRoot.position - introAimPoint, -targetBinder.PlayerRoot.forward);
            Vector3 right = Vector3.Cross(Vector3.up, awayDirection).normalized;
            Vector3 introPosition = introAimPoint
                + awayDirection * Mathf.Max(0.4f, _data.Movement.introDistance)
                + right * _data.Movement.introSideOffset;
            introPosition.y = targetBinder.PlayerRoot.position.y;
            UltimateSequenceData.CameraShotSettings introShot = _data.ResolveCameraShot(UltimateSequencePhase.IntroPose);
            float introBottomToCenterRatio = introShot != null ? introShot.targetBottomToCenterRatio : 0.8f;
            float introVerticalOffset = introShot != null ? introShot.targetVerticalOffset : 0f;
            Vector3 introLookTarget = _boundTarget != null && _boundTarget.TargetRoot != null
                ? targetBinder.GetTargetAimPoint(_boundTarget.TargetRoot, introBottomToCenterRatio, introVerticalOffset)
                : targetBinder.GetTargetAimPoint(introBottomToCenterRatio, introVerticalOffset);
            targetBinder.SnapPlayerTo(introPosition, introLookTarget);
        }

        _sequenceActive = true;
        enabled = true;
        ChangePhase(UltimateSequencePhase.PreCast);
        return true;
    }

    void Update()
    {
        if (!_sequenceActive || _data == null)
        {
            enabled = false;
            return;
        }

        if (_data.Activation.abortOnTargetLost && !targetBinder.HasActiveTarget && CurrentPhase < UltimateSequencePhase.Walkout)
        {
            ChangePhase(UltimateSequencePhase.Failed);
        }

        if (!_data.Activation.allowGracefulFinishIfTargetDies && !targetBinder.IsTargetAlive && CurrentPhase < UltimateSequencePhase.Walkout)
        {
            ChangePhase(UltimateSequencePhase.Failed);
        }

        float dt = Time.unscaledDeltaTime;
        _phaseElapsed += dt;
        RefreshVictimAnchorIfNeeded();
        cameraDirector.Tick(dt);

        switch (CurrentPhase)
        {
            case UltimateSequencePhase.PreCast:
                if (_phaseElapsed >= _data.Timings.preCastDuration)
                    ChangePhase(UltimateSequencePhase.IntroPose);
                break;

            case UltimateSequencePhase.IntroPose:
                SamplePhaseClip(
                    _data.CinematicAnimation.introPoseClip,
                    _phaseElapsed,
                    Mathf.Max(0.01f, _data.Timings.introPoseDuration),
                    _data.CinematicAnimation.introPoseClipStartNormalized,
                    _data.CinematicAnimation.introPoseClipEndNormalized);
                if (_phaseElapsed >= _data.Timings.introPoseDuration)
                    ChangePhase(UltimateSequencePhase.DashSlash);
                break;

            case UltimateSequencePhase.DashSlash:
                TickDashSlash();
                break;

            case UltimateSequencePhase.MultiSlash:
                TickMultiSlash(dt);
                break;

            case UltimateSequencePhase.CrackBurst:
                if (_phaseElapsed >= _data.Timings.crackHoldDuration)
                    ChangePhase(UltimateSequencePhase.Walkout);
                break;

            case UltimateSequencePhase.FinalExplosion:
                TickFinalExplosion();
                break;

            case UltimateSequencePhase.Walkout:
                SampleWalkoutClip(_phaseElapsed);
                TickWalkout();
                break;

            case UltimateSequencePhase.Recover:
                if (_phaseElapsed >= _data.Timings.recoverDuration)
                    CleanupSequence(false);
                break;

            case UltimateSequencePhase.Failed:
                if (_phaseElapsed >= Mathf.Max(0.05f, _data.Timings.recoverDuration))
                    CleanupSequence(false);
                break;
        }
    }

    void LateUpdate()
    {
        if (!_sequenceActive || _data == null)
            return;

        switch (CurrentPhase)
        {
            case UltimateSequencePhase.IntroPose:
                SamplePhaseClip(
                    _data.CinematicAnimation.introPoseClip,
                    _phaseElapsed,
                    Mathf.Max(0.01f, _data.Timings.introPoseDuration),
                    _data.CinematicAnimation.introPoseClipStartNormalized,
                    _data.CinematicAnimation.introPoseClipEndNormalized);
                break;

            case UltimateSequencePhase.DashSlash:
                SamplePhaseClip(
                    _data.CinematicAnimation.dashSlashClip,
                    _phaseElapsed,
                    Mathf.Max(0.01f, _data.Timings.dashDuration),
                    _data.CinematicAnimation.dashSlashClipStartNormalized,
                    _data.CinematicAnimation.dashSlashClipEndNormalized);
                break;

            case UltimateSequencePhase.MultiSlash:
            {
                float cycleDuration = Mathf.Max(0.01f, _data.Timings.defaultMultiSlashInterval);
                float cycleElapsed = Mathf.Repeat(_phaseElapsed, cycleDuration);
                SamplePhaseClip(
                    _data.CinematicAnimation.dashSlashClip,
                    cycleElapsed,
                    cycleDuration,
                    _data.CinematicAnimation.dashSlashClipStartNormalized,
                    _data.CinematicAnimation.dashSlashClipEndNormalized);
                break;
            }

            case UltimateSequencePhase.Walkout:
                SampleWalkoutClip(_phaseElapsed);
                break;
        }
    }

    void TickDashSlash()
    {
        float duration = Mathf.Max(0.01f, _data.Timings.dashDuration);
        float normalized = Mathf.Clamp01(_phaseElapsed / duration);
        Vector3 lookTarget = GetAimPointForCurrentPhase();
        float travelT = EvaluateDashTravelNormalized(normalized);
        targetBinder.MovePlayerLinear(_phaseStartPosition, _phaseEndPosition, lookTarget, travelT);

        if (!_dashHitApplied && normalized >= _data.Timings.dashImpactNormalizedTime)
        {
            _dashHitApplied = true;
            hitProcessor.ApplyDashSlashHit();
            vfxPresenter.PlayDashSlash(targetBinder.PlayerRoot.position, lookTarget);
            TryApplyHitStop(_data.TimeFx.useDashImpactSlow, _data.TimeFx.dashImpactTimeScale, _data.TimeFx.dashImpactSlowDuration);
        }

        if (_phaseElapsed >= duration)
            ChangePhase(UltimateSequencePhase.MultiSlash);
    }

    void TickMultiSlash(float dt)
    {
        int slashCount = Mathf.Max(1, _data.SlashCount);
        _stepTimer -= dt;

        if (_slashIndex >= slashCount)
        {
            if (_stepTimer <= 0f)
                ChangePhase(UltimateSequencePhase.CrackBurst);
            return;
        }

        if (_stepTimer > 0f)
            return;

        UltimateSequenceData.SlashStepData step = _data.GetSlashStep(_slashIndex);
        Vector3 lookTarget = GetAimPointForCurrentPhase();
        Vector3 slashPosition = targetBinder.GetSlashPosition(step);
        targetBinder.SnapPlayerTo(slashPosition, lookTarget);
        hitProcessor.ApplyMultiSlashHit(step, _slashIndex);
        vfxPresenter.PlayMultiSlash(_slashIndex, slashCount, step, slashPosition, lookTarget);
        _stepTimer = Mathf.Max(0.01f, step.delay > 0f ? step.delay : _data.Timings.defaultMultiSlashInterval);
        _slashIndex++;
    }

    void TickWalkout()
    {
        float duration = Mathf.Max(0.01f, _data.Timings.walkoutDuration);
        float normalized = Mathf.Clamp01(_phaseElapsed / duration);
        Vector3 lookTarget = targetBinder.GetWalkoutLookTarget(_phaseEndPosition);
        targetBinder.MovePlayerLinear(_phaseStartPosition, _phaseEndPosition, lookTarget, normalized);

        if (_phaseElapsed >= duration)
            ChangePhase(UltimateSequencePhase.FinalExplosion);
    }

    void TickFinalExplosion()
    {
        if (targetBinder != null)
        {
            targetBinder.FacePlayerTowardsImmediate(_finalExplosionLookTarget);
        }

        if (!_finalExplosionApplied && _phaseElapsed >= GetFinalExplosionDelay())
            TriggerFinalExplosion();

        if (_phaseElapsed >= _data.Timings.finalExplosionHoldDuration)
            ChangePhase(UltimateSequencePhase.Recover);
    }

    float GetFinalExplosionDelay()
    {
        return 0f;
    }

    void TriggerFinalExplosion()
    {
        if (_finalExplosionApplied)
            return;

        _finalExplosionApplied = true;
        hitProcessor.ApplyFinalExplosionHit();
        hitProcessor.FlushBufferedDamage();
        vfxPresenter.PlayFinalExplosion(GetAimPointForCurrentPhase());
        TryApplyHitStop(_data.TimeFx.useFinalExplosionSlow, _data.TimeFx.finalExplosionTimeScale, _data.TimeFx.finalExplosionSlowDuration);
    }

    void ChangePhase(UltimateSequencePhase nextPhase)
    {
        CurrentPhase = nextPhase;
        _phaseElapsed = 0f;

        if (ShouldLog)
            Debug.Log($"[UltimateSequence] Phase -> {nextPhase}", this);

        bool useSwordCloseupShot = nextPhase == UltimateSequencePhase.IntroPose &&
            _data.CinematicAnimation.useSwordCloseupDuringIntro &&
            _data.CinematicAnimation.introSwordCloseupCamera != null;

        _currentPhaseShot = useSwordCloseupShot
            ? _data.CinematicAnimation.introSwordCloseupCamera
            : _data.ResolveCameraShot(nextPhase);
        _currentTargetBottomToCenterRatio = _currentPhaseShot != null ? _currentPhaseShot.targetBottomToCenterRatio : 0.8f;
        _currentTargetVerticalOffset = _currentPhaseShot != null ? _currentPhaseShot.targetVerticalOffset : 0f;
        cameraDirector.SetShot(_data.ResolveCameraStage(nextPhase), _currentPhaseShot, false, useSwordCloseupShot);

        if (ResolvePhaseClip(nextPhase) == null && !ShouldPreserveLastSampledClip(nextPhase))
            StopPhaseClipSampling();

        switch (nextPhase)
        {
            case UltimateSequencePhase.PreCast:
                break;

            case UltimateSequencePhase.IntroPose:
                vfxPresenter.PlayIntroPose();
                if (!BeginPhaseClip(_data.CinematicAnimation.introPoseClip))
                    TryPlayAnimationCue(_data.IntroTrigger, _data.IntroFallbackState, _introTriggerHash);
                else
                {
                    SamplePhaseClip(
                        _data.CinematicAnimation.introPoseClip,
                        0f,
                        Mathf.Max(0.01f, _data.Timings.introPoseDuration),
                        _data.CinematicAnimation.introPoseClipStartNormalized,
                        _data.CinematicAnimation.introPoseClipEndNormalized);
                }
                TryApplyHitStop(_data.TimeFx.useIntroSlow, _data.TimeFx.introTimeScale, _data.TimeFx.introSlowDuration);
                break;

            case UltimateSequencePhase.DashSlash:
                _owner?.PrepareModernPresentationActor();
                targetBinder.ClearPresentationAnchorOverrides();
                _phaseStartPosition = targetBinder.PlayerRoot.position;
                _phaseEndPosition = targetBinder.GetDashDestination(_data.Movement.dashEndDistance, _data.Movement.dashSideOffset);
                _dashHitApplied = false;
                if (!BeginPhaseClip(_data.CinematicAnimation.dashSlashClip))
                    TryPlayAnimationCue(_data.DashTrigger, _data.DashFallbackState, _dashTriggerHash);
                else
                {
                    SamplePhaseClip(
                        _data.CinematicAnimation.dashSlashClip,
                        0f,
                        Mathf.Max(0.01f, _data.Timings.dashDuration),
                        _data.CinematicAnimation.dashSlashClipStartNormalized,
                        _data.CinematicAnimation.dashSlashClipEndNormalized);
                }
                break;

            case UltimateSequencePhase.MultiSlash:
                _slashIndex = 0;
                _stepTimer = 0f;
                if (!BeginPhaseClip(_data.CinematicAnimation.dashSlashClip))
                    TryPlayAnimationCue(_data.MultiSlashTrigger, _data.MultiSlashFallbackState, _multiSlashTriggerHash);
                else
                {
                    SamplePhaseClip(
                        _data.CinematicAnimation.dashSlashClip,
                        0f,
                        Mathf.Max(0.01f, _data.Timings.defaultMultiSlashInterval),
                        _data.CinematicAnimation.dashSlashClipStartNormalized,
                        _data.CinematicAnimation.dashSlashClipEndNormalized);
                }
                break;

            case UltimateSequencePhase.CrackBurst:
                vfxPresenter.PlayCrackBurst(GetAimPointForCurrentPhase());
                break;

            case UltimateSequencePhase.FinalExplosion:
                {
                    Vector3 preservedForward = targetBinder != null
                        ? GetFlattenedDirection(targetBinder.PlayerRoot.forward, Vector3.forward)
                        : Vector3.forward;
                    Vector3 playerPosition = targetBinder != null ? targetBinder.PlayerRoot.position : Vector3.zero;
                    _finalExplosionLookTarget = playerPosition + preservedForward * 4f;
                }
                break;

            case UltimateSequencePhase.Walkout:
                _phaseStartPosition = targetBinder.PlayerRoot.position;
                _phaseEndPosition = targetBinder.GetWalkoutPosition(_data.Movement.walkoutDistance, _data.Movement.walkoutSideOffset);
                vfxPresenter.PlayWalkout(_phaseStartPosition, _phaseEndPosition - _phaseStartPosition);
                if (!BeginPhaseClip(_data.CinematicAnimation.walkoutClip))
                    TryPlayAnimationCue(_data.WalkoutTrigger, _data.WalkoutFallbackState, _walkoutTriggerHash);
                else
                    SampleWalkoutClip(0f);
                break;

            case UltimateSequencePhase.Recover:
                break;

            case UltimateSequencePhase.Failed:
                break;
        }
    }

    void CleanupSequence(bool force)
    {
        CombatFeelRuntimeUtility.ForceRestoreActiveHitStop(this, ref _hitStopCoroutine);
        StopPhaseClipSampling();
        vfxPresenter.EndSequence();
        cameraDirector.EndSequence();
        hitProcessor.EndSequence();
        targetBinder.EndSequence();

        if (_owner != null)
            _owner.EndExternalCinematicSession();

        _owner = null;
        _data = null;
        _boundTarget = null;
        _currentPhaseShot = null;
        _currentTargetBottomToCenterRatio = 0.8f;
        _currentTargetVerticalOffset = 0f;
        _hasIntroFallbackState = false;
        _hasDashFallbackState = false;
        _hasMultiSlashFallbackState = false;
        _hasFinalFallbackState = false;
        _hasWalkoutFallbackState = false;
        _introFallbackLayerIndex = 0;
        _dashFallbackLayerIndex = 0;
        _multiSlashFallbackLayerIndex = 0;
        _finalFallbackLayerIndex = 0;
        _walkoutFallbackLayerIndex = 0;
        _introFallbackStateHash = 0;
        _dashFallbackStateHash = 0;
        _multiSlashFallbackStateHash = 0;
        _finalFallbackStateHash = 0;
        _walkoutFallbackStateHash = 0;
        _lastVictimAnchorLookTarget = Vector3.zero;
        _finalExplosionLookTarget = Vector3.zero;
        _hasVictimAnchorLookTarget = false;
        _sequenceActive = false;
        _dashHitApplied = false;
        _finalExplosionApplied = false;
        _slashIndex = 0;
        _stepTimer = 0f;
        CurrentPhase = force ? UltimateSequencePhase.Failed : UltimateSequencePhase.None;
        enabled = false;
    }

    AnimationClip ResolvePhaseClip(UltimateSequencePhase phase)
    {
        switch (phase)
        {
            case UltimateSequencePhase.IntroPose:
                return _data != null ? _data.CinematicAnimation.introPoseClip : null;
            case UltimateSequencePhase.DashSlash:
                return _data != null ? _data.CinematicAnimation.dashSlashClip : null;
            case UltimateSequencePhase.MultiSlash:
                return _data != null ? _data.CinematicAnimation.dashSlashClip : null;
            case UltimateSequencePhase.Walkout:
                return _data != null ? _data.CinematicAnimation.walkoutClip : null;
            default:
                return null;
        }
    }

    bool BeginPhaseClip(AnimationClip clip)
    {
        if (clip == null)
        {
            Debug.LogWarning($"[UltimateSequence] Missing phase clip for {CurrentPhase}.", this);
            return false;
        }

        Animator playerAnimator = targetBinder != null ? targetBinder.PlayerAnimator : null;
        Animator presentationAnimator = targetBinder != null ? targetBinder.PlayerPresentationAnimator : null;
        bool usePresentationAnimator = presentationAnimator != null && presentationAnimator.runtimeAnimatorController != null;
        bool started = false;
        if (usePresentationAnimator)
        {
            _playerClipSampler.StopPlayback();
            started = _presentationClipSampler.Begin(presentationAnimator, clip);
        }
        else if (playerAnimator != null)
        {
            _presentationClipSampler.StopPlayback();
            started = _playerClipSampler.Begin(playerAnimator, clip);
        }

        if (!started)
        {
            string playerAnimatorName = playerAnimator != null ? playerAnimator.name : "null";
            string presentationAnimatorName = presentationAnimator != null ? presentationAnimator.name : "null";
            Debug.LogWarning(
                $"[UltimateSequence] Failed to begin clip '{clip.name}' for phase {CurrentPhase}. " +
                $"playerAnimator={playerAnimatorName}, presentationAnimator={presentationAnimatorName}, humanMotion={clip.humanMotion}",
                this);
            return false;
        }

        _activePhaseClip = clip;
        return true;
    }

    void SamplePhaseClip(AnimationClip clip, float elapsed, float duration)
    {
        SamplePhaseClip(clip, elapsed, duration, 0f, 1f);
    }

    void SampleWalkoutClip(float elapsed)
    {
        SamplePhaseClip(
            _data.CinematicAnimation.walkoutClip,
            elapsed,
            Mathf.Max(0.01f, _data.Timings.walkoutDuration),
            _data.CinematicAnimation.walkoutClipStartNormalized,
            _data.CinematicAnimation.walkoutClipEndNormalized);
    }

    void SamplePhaseClip(AnimationClip clip, float elapsed, float duration, float clipStartNormalized, float clipEndNormalized)
    {
        if (clip == null)
            return;

        float normalizedTime = duration > 0.0001f ? Mathf.Clamp01(elapsed / duration) : 1f;
        if (TrySamplePresentationIntroPoseDirect(clip, normalizedTime, clipStartNormalized, clipEndNormalized))
        {
            StopPhaseClipSampling();
            _activePhaseClip = clip;
            return;
        }

        if (_activePhaseClip != clip && !BeginPhaseClip(clip))
            return;

        _playerClipSampler.SampleNormalizedRange(normalizedTime, clipStartNormalized, clipEndNormalized);
        _presentationClipSampler.SampleNormalizedRange(normalizedTime, clipStartNormalized, clipEndNormalized);
    }

    bool TrySamplePresentationIntroPoseDirect(AnimationClip clip, float normalizedTime, float clipStartNormalized, float clipEndNormalized)
    {
        if (CurrentPhase != UltimateSequencePhase.IntroPose || _data == null || clip != _data.CinematicAnimation.introPoseClip)
            return false;

        UltimatePresentationClone presentationClone = targetBinder != null ? targetBinder.PlayerPresentationClone : null;
        Transform cloneRoot = presentationClone != null ? presentationClone.CloneRoot : null;
        if (cloneRoot == null)
            return false;

        float clipLength = Mathf.Max(1f / 60f, clip.length);
        float rangeStart = Mathf.Clamp01(clipStartNormalized);
        float rangeEnd = Mathf.Clamp01(clipEndNormalized);
        if (rangeEnd < rangeStart)
        {
            float swap = rangeStart;
            rangeStart = rangeEnd;
            rangeEnd = swap;
        }

        if (Mathf.Abs(rangeEnd - rangeStart) > 0.0001f)
            return false;

        float rangedNormalized = Mathf.Lerp(rangeStart, rangeEnd, Mathf.Clamp01(normalizedTime));
        float sampleTime = Mathf.Clamp(rangedNormalized * clipLength, 0f, Mathf.Max(0f, clipLength - 0.0001f));
        clip.SampleAnimation(cloneRoot.gameObject, sampleTime);
        return true;
    }

    float EvaluateDashTravelNormalized(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        float bias = _data != null ? Mathf.Max(0.5f, _data.Movement.dashTravelBias) : 2.1f;
        return 1f - Mathf.Pow(1f - normalized, bias);
    }

    static Vector3 GetFlattenedDirection(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            return direction.normalized;

        fallback.y = 0f;
        if (fallback.sqrMagnitude > 0.0001f)
            return fallback.normalized;

        return Vector3.forward;
    }

    void StopPhaseClipSampling()
    {
        _playerClipSampler.StopPlayback();
        _presentationClipSampler.StopPlayback();
        _activePhaseClip = null;
    }

    void TryApplyHitStop(bool enabledHitStop, float timeScale, float duration)
    {
        if (!enabledHitStop || _owner == null || _owner.DidFreezeWorldTimeThisCinematic)
            return;

        CombatFeelRuntimeUtility.StartHitStop(
            this,
            ref _hitStopCoroutine,
            timeScale,
            duration,
            true,
            0f,
            ref _lastHitStopRealtime);
    }

    void CacheAnimatorTriggers()
    {
        _animatorTriggerHashes.Clear();
        Animator animator = targetBinder.PlayerAnimator;
        if (animator == null)
            return;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].type == AnimatorControllerParameterType.Trigger)
                _animatorTriggerHashes.Add(Animator.StringToHash(parameters[i].name));
        }

        _introTriggerHash = Animator.StringToHash(_data.IntroTrigger);
        _dashTriggerHash = Animator.StringToHash(_data.DashTrigger);
        _multiSlashTriggerHash = Animator.StringToHash(_data.MultiSlashTrigger);
        _finalTriggerHash = Animator.StringToHash(_data.FinalExplosionTrigger);
        _walkoutTriggerHash = Animator.StringToHash(_data.WalkoutTrigger);
        CacheFallbackStates(animator);
    }

    void TrySetTrigger(int triggerHash)
    {
        Animator animator = targetBinder.PlayerAnimator;
        if (animator == null || triggerHash == 0 || !_animatorTriggerHashes.Contains(triggerHash))
            return;

        animator.ResetTrigger(triggerHash);
        animator.SetTrigger(triggerHash);
    }

    void TryPlayAnimationCue(string triggerName, string fallbackState, int triggerHash)
    {
        if (string.IsNullOrWhiteSpace(triggerName) && string.IsNullOrWhiteSpace(fallbackState))
            return;

        UltimatePresentationClone presentationClone = targetBinder != null ? targetBinder.PlayerPresentationClone : null;
        if (presentationClone != null)
        {
            if (presentationClone.TrySetTrigger(triggerName))
                return;

            if (presentationClone.TryCrossFadeState(fallbackState, _data.AnimatorCrossFadeDuration))
                return;
        }

        if (TrySetTriggerInternal(triggerHash))
            return;

        TryCrossFadeCachedFallbackState(triggerHash, fallbackState, _data.AnimatorCrossFadeDuration);
    }

    static bool ShouldPreserveLastSampledClip(UltimateSequencePhase phase)
    {
        return phase == UltimateSequencePhase.MultiSlash
            || phase == UltimateSequencePhase.CrackBurst
            || phase == UltimateSequencePhase.FinalExplosion;
    }

    bool TrySetTriggerInternal(int triggerHash)
    {
        Animator animator = targetBinder.PlayerAnimator;
        if (animator == null || triggerHash == 0 || !_animatorTriggerHashes.Contains(triggerHash))
            return false;

        animator.ResetTrigger(triggerHash);
        animator.SetTrigger(triggerHash);
        return true;
    }

    bool TryCrossFadeState(Animator animator, string statePath, float duration)
    {
        if (animator == null || string.IsNullOrWhiteSpace(statePath) || animator.runtimeAnimatorController == null)
            return false;

        if (!TryResolveState(animator, statePath, out int layerIndex, out int stateHash))
            return false;

        animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0.01f, duration), layerIndex, 0f);
        return true;
    }

    bool TryCrossFadeCachedFallbackState(int triggerHash, string fallbackState, float duration)
    {
        Animator animator = targetBinder.PlayerAnimator;
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;

        if (!TryGetCachedFallbackState(triggerHash, out int layerIndex, out int stateHash))
            return TryCrossFadeState(animator, fallbackState, duration);

        animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0.01f, duration), layerIndex, 0f);
        return true;
    }

    void CacheFallbackStates(Animator animator)
    {
        CacheFallbackState(animator, _data.IntroFallbackState, out _hasIntroFallbackState, out _introFallbackLayerIndex, out _introFallbackStateHash);
        CacheFallbackState(animator, _data.DashFallbackState, out _hasDashFallbackState, out _dashFallbackLayerIndex, out _dashFallbackStateHash);
        CacheFallbackState(animator, _data.MultiSlashFallbackState, out _hasMultiSlashFallbackState, out _multiSlashFallbackLayerIndex, out _multiSlashFallbackStateHash);
        CacheFallbackState(animator, _data.FinalExplosionFallbackState, out _hasFinalFallbackState, out _finalFallbackLayerIndex, out _finalFallbackStateHash);
        CacheFallbackState(animator, _data.WalkoutFallbackState, out _hasWalkoutFallbackState, out _walkoutFallbackLayerIndex, out _walkoutFallbackStateHash);
    }

    void CacheFallbackState(Animator animator, string statePath, out bool hasState, out int layerIndex, out int stateHash)
    {
        if (TryResolveState(animator, statePath, out layerIndex, out stateHash))
        {
            hasState = true;
            return;
        }

        hasState = false;
        layerIndex = 0;
        stateHash = 0;
    }

    bool TryGetCachedFallbackState(int triggerHash, out int layerIndex, out int stateHash)
    {
        if (triggerHash == _introTriggerHash)
        {
            layerIndex = _introFallbackLayerIndex;
            stateHash = _introFallbackStateHash;
            return _hasIntroFallbackState;
        }

        if (triggerHash == _dashTriggerHash)
        {
            layerIndex = _dashFallbackLayerIndex;
            stateHash = _dashFallbackStateHash;
            return _hasDashFallbackState;
        }

        if (triggerHash == _multiSlashTriggerHash)
        {
            layerIndex = _multiSlashFallbackLayerIndex;
            stateHash = _multiSlashFallbackStateHash;
            return _hasMultiSlashFallbackState;
        }

        if (triggerHash == _finalTriggerHash)
        {
            layerIndex = _finalFallbackLayerIndex;
            stateHash = _finalFallbackStateHash;
            return _hasFinalFallbackState;
        }

        if (triggerHash == _walkoutTriggerHash)
        {
            layerIndex = _walkoutFallbackLayerIndex;
            stateHash = _walkoutFallbackStateHash;
            return _hasWalkoutFallbackState;
        }

        layerIndex = 0;
        stateHash = 0;
        return false;
    }

    static bool TryResolveState(Animator animator, string statePath, out int layerIndex, out int stateHash)
    {
        layerIndex = 0;
        stateHash = 0;
        if (animator == null || string.IsNullOrWhiteSpace(statePath))
            return false;

        string trimmed = statePath.Trim();
        int separator = trimmed.IndexOf('.');
        if (separator > 0)
        {
            string layerName = trimmed.Substring(0, separator);
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerName(i) != layerName)
                    continue;

                int fullPathHash = Animator.StringToHash(trimmed);
                if (!animator.HasState(i, fullPathHash))
                    return false;

                layerIndex = i;
                stateHash = fullPathHash;
                return true;
            }
        }

        for (int i = 0; i < animator.layerCount; i++)
        {
            string fullPath = $"{animator.GetLayerName(i)}.{trimmed}";
            int fullPathHash = Animator.StringToHash(fullPath);
            if (!animator.HasState(i, fullPathHash))
                continue;

            layerIndex = i;
            stateHash = fullPathHash;
            return true;
        }

        return false;
    }

    Vector3 GetAimPointForCurrentPhase()
    {
        return targetBinder.GetTargetAimPoint(_currentTargetBottomToCenterRatio, _currentTargetVerticalOffset);
    }

    void RefreshVictimAnchorIfNeeded()
    {
        if (targetBinder == null)
            return;

        Vector3 lookTarget = targetBinder.PlayerRoot.position;
        if (_hasVictimAnchorLookTarget &&
            (_lastVictimAnchorLookTarget - lookTarget).sqrMagnitude <= VictimAnchorRefreshSqrThreshold)
        {
            return;
        }

        _lastVictimAnchorLookTarget = lookTarget;
        _hasVictimAnchorLookTarget = true;
        targetBinder.RefreshVictimAnchor(lookTarget);
    }

    bool ShouldLog => debugLog || (_data != null && _data.DebugLog);

    void ResolveReferences(bool allowCreate)
    {
        if (targetBinder == null)
            targetBinder = GetComponent<UltimateTargetBinder>() ?? (allowCreate ? gameObject.AddComponent<UltimateTargetBinder>() : null);
        if (cameraDirector == null)
            cameraDirector = GetComponent<UltimateCameraDirector>() ?? (allowCreate ? gameObject.AddComponent<UltimateCameraDirector>() : null);
        if (hitProcessor == null)
            hitProcessor = GetComponent<UltimateHitProcessor>() ?? (allowCreate ? gameObject.AddComponent<UltimateHitProcessor>() : null);
        if (vfxPresenter == null)
            vfxPresenter = GetComponent<UltimateVFXPresenter>() ?? (allowCreate ? gameObject.AddComponent<UltimateVFXPresenter>() : null);
    }
}
