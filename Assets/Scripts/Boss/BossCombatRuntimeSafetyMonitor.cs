using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossCombatRuntimeSafetyMonitor : MonoBehaviour
{
    [SerializeField] private BossController bossController;
    [SerializeField] private AttackHitbox attackHitbox;
    [SerializeField] private bool autoCloseHitboxOutsideAttack = true;
    [SerializeField, Min(0f)] private float hitboxOutsideAttackGraceTime = 0.12f;
    [SerializeField] private bool autoRecoverFromStuckRecovery = true;
    [SerializeField, Min(0.25f)] private float maxRecoveryStateDuration = 2.5f;
    [SerializeField] private bool autoRecoverFromStuckAttack = true;
    [SerializeField, Min(1f)] private float maxAttackStateDuration = 6f;
    [SerializeField] private bool autoRecoverFromNoPatternProgress = true;
    [SerializeField, Min(1f)] private float maxNoPatternProgressDuration = 5f;
    [SerializeField] private bool enableWarningLogs = true;
    [SerializeField, Range(2, 8)] private int maxSamePatternStreak = 4;
    [SerializeField, Range(2, 8)] private int maxSamePostActionStreak = 3;
    [SerializeField, Min(0f)] private float farRangeThreshold = 8f;
    [SerializeField, Range(2, 8)] private int maxFarNoPressureStreak = 3;
    [SerializeField, Range(2, 8)] private int maxMovingAwayNoPressureStreak = 3;
    [SerializeField, Range(2, 8)] private int maxGuardNoPunishStreak = 3;
    [SerializeField, Range(2, 8)] private int maxAttackNoCounterStreak = 3;
    [SerializeField, Range(2, 8)] private int maxDefenseNoMixupStreak = 3;

    BossController _subscribedController;
    BossPatternId _lastPatternId;
    BossPatternPostActionType _lastPostActionType;
    int _samePatternStreak;
    int _samePostActionStreak;
    int _farNoPressureStreak;
    int _movingAwayNoPressureStreak;
    int _guardNoPunishStreak;
    int _attackNoCounterStreak;
    int _defenseNoMixupStreak;
    float _hitboxOutsideAttackSince = float.NegativeInfinity;
    float _recoveryStateStartedAt = float.NegativeInfinity;
    float _attackStateStartedAt = float.NegativeInfinity;
    float _noPatternProgressSince = float.NegativeInfinity;
    float _lastPatternSampleAt = float.NegativeInfinity;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        Bind();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Update()
    {
        MonitorLingeringHitbox();
        MonitorRecoveryState();
        MonitorAttackState();
        MonitorNoPatternProgress();
    }

    [ContextMenu("Resolve References")]
    void ResolveReferences()
    {
        if (bossController == null)
            bossController = GetComponentInParent<BossController>();
        if (bossController == null)
            bossController = GetComponentInChildren<BossController>();

        if (attackHitbox == null && bossController != null)
            attackHitbox = bossController.attackHitbox;
    }

    void Bind()
    {
        if (_subscribedController == bossController)
            return;

        Unbind();
        _subscribedController = bossController;
        if (_subscribedController != null)
            _subscribedController.OnPatternTelemetrySample += OnPatternTelemetrySample;

        _lastPatternSampleAt = Time.time;
    }

    void Unbind()
    {
        if (_subscribedController != null)
            _subscribedController.OnPatternTelemetrySample -= OnPatternTelemetrySample;
        _subscribedController = null;
        _lastPatternSampleAt = float.NegativeInfinity;
    }

    void MonitorLingeringHitbox()
    {
        if (bossController == null || attackHitbox == null || attackHitbox.Collider == null)
            return;

        bool activeOutsideAttack = attackHitbox.Collider.enabled &&
                                   bossController.currentState != BossState.Attack &&
                                   !bossController.CanProcessAttackAnimationEvents;
        if (!activeOutsideAttack)
        {
            _hitboxOutsideAttackSince = float.NegativeInfinity;
            return;
        }

        if (float.IsNegativeInfinity(_hitboxOutsideAttackSince))
        {
            _hitboxOutsideAttackSince = Time.time;
            return;
        }

        if (Time.time - _hitboxOutsideAttackSince < hitboxOutsideAttackGraceTime)
            return;

        if (enableWarningLogs)
        {
            Debug.LogWarning(
                "[BossCombatSafety] Hitbox remained active outside Attack state. state=" +
                bossController.currentState +
                " pattern=" +
                bossController.CurrentPatternName,
                this);
        }

        if (autoCloseHitboxOutsideAttack)
            bossController.DeactivateHitbox();

        _hitboxOutsideAttackSince = float.NegativeInfinity;
    }

    void MonitorRecoveryState()
    {
        if (bossController == null || bossController.currentState != BossState.Recovery)
        {
            _recoveryStateStartedAt = float.NegativeInfinity;
            return;
        }

        if (float.IsNegativeInfinity(_recoveryStateStartedAt))
        {
            _recoveryStateStartedAt = Time.time;
            return;
        }

        if (Time.time - _recoveryStateStartedAt < maxRecoveryStateDuration)
            return;

        if (enableWarningLogs)
        {
            Debug.LogWarning(
                "[BossCombatSafety] Recovery state exceeded " +
                maxRecoveryStateDuration.ToString("0.00") +
                "s. state=" +
                bossController.currentState,
                this);
        }

        if (autoRecoverFromStuckRecovery)
            bossController.RequestCombatRecovery(BossRecoveryReason.TargetReacquire, forceRestart: true);

        _recoveryStateStartedAt = Time.time;
    }

    void MonitorAttackState()
    {
        if (bossController == null ||
            bossController.currentState != BossState.Attack ||
            bossController.IsInUltimateVictimState)
        {
            _attackStateStartedAt = float.NegativeInfinity;
            return;
        }

        if (float.IsNegativeInfinity(_attackStateStartedAt))
        {
            _attackStateStartedAt = Time.time;
            return;
        }

        if (Time.time - _attackStateStartedAt < maxAttackStateDuration)
            return;

        if (enableWarningLogs)
        {
            Debug.LogWarning(
                "[BossCombatSafety] Attack state exceeded " +
                maxAttackStateDuration.ToString("0.00") +
                "s. pattern=" +
                bossController.CurrentPatternName,
                this);
        }

        if (autoRecoverFromStuckAttack)
            bossController.RequestCombatRecovery(BossRecoveryReason.Generic, forceRestart: true);

        _attackStateStartedAt = Time.time;
    }

    void MonitorNoPatternProgress()
    {
        if (bossController == null || !IsPatternProgressExpected())
        {
            _noPatternProgressSince = float.NegativeInfinity;
            return;
        }

        float lastProgressAt = float.IsNegativeInfinity(_lastPatternSampleAt) ? Time.time : _lastPatternSampleAt;
        if (float.IsNegativeInfinity(_noPatternProgressSince) || _noPatternProgressSince < lastProgressAt)
            _noPatternProgressSince = lastProgressAt;

        if (Time.time - _noPatternProgressSince < maxNoPatternProgressDuration)
            return;

        if (enableWarningLogs)
        {
            Debug.LogWarning(
                "[BossCombatSafety] No attack pattern executed for " +
                maxNoPatternProgressDuration.ToString("0.00") +
                "s while combat progress was expected. state=" +
                bossController.currentState +
                " distance=" +
                bossController.CurrentPlayerObservation.playerDistance.ToString("0.00"),
                this);
        }

        if (autoRecoverFromNoPatternProgress)
            bossController.RequestCombatRecovery(BossRecoveryReason.TargetReacquire, forceRestart: true);

        _noPatternProgressSince = Time.time;
        _lastPatternSampleAt = Time.time;
    }

    bool IsPatternProgressExpected()
    {
        if (bossController.IsInUltimateVictimState || bossController.IsCombatRecoveryActive)
            return false;

        BossState state = bossController.currentState;
        if (state != BossState.Detect && state != BossState.Move && state != BossState.CombatIdle)
            return false;

        float distance = bossController.CurrentPlayerObservation.playerDistance;
        return !float.IsNaN(distance) && !float.IsInfinity(distance);
    }

    void OnPatternTelemetrySample(BossPatternTelemetrySample sample)
    {
        _lastPatternSampleAt = Time.time;
        TrackPatternStreak(sample);
        TrackPostActionStreak(sample);
        TrackFarPressure(sample);
        TrackPlayerResponsePressure(sample);
    }

    void TrackPatternStreak(BossPatternTelemetrySample sample)
    {
        if (sample.patternId == BossPatternId.None)
            return;

        if (sample.patternId == _lastPatternId)
            _samePatternStreak++;
        else
        {
            _lastPatternId = sample.patternId;
            _samePatternStreak = 1;
        }

        if (!enableWarningLogs || _samePatternStreak != maxSamePatternStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Same pattern streak reached " +
            _samePatternStreak +
            ". pattern=" +
            sample.patternId +
            " phase=" +
            sample.phase,
            this);
    }

    void TrackPostActionStreak(BossPatternTelemetrySample sample)
    {
        if (!IsMovementPostAction(sample.postActionType))
        {
            _lastPostActionType = BossPatternPostActionType.None;
            _samePostActionStreak = 0;
            return;
        }

        if (sample.postActionType == _lastPostActionType)
            _samePostActionStreak++;
        else
        {
            _lastPostActionType = sample.postActionType;
            _samePostActionStreak = 1;
        }

        if (!enableWarningLogs || _samePostActionStreak != maxSamePostActionStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Same movement postAction streak reached " +
            _samePostActionStreak +
            ". postAction=" +
            sample.postActionType +
            " lastPattern=" +
            sample.patternId,
            this);
    }

    void TrackFarPressure(BossPatternTelemetrySample sample)
    {
        bool far = sample.distanceToPlayer >= farRangeThreshold;
        bool pressure = IsPressurePattern(sample);

        if (!far || pressure)
        {
            _farNoPressureStreak = 0;
            return;
        }

        _farNoPressureStreak++;
        if (!enableWarningLogs || _farNoPressureStreak != maxFarNoPressureStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Far-range samples without pressure reached " +
            _farNoPressureStreak +
            ". lastPattern=" +
            sample.patternId +
            " distance=" +
            sample.distanceToPlayer.ToString("0.00"),
            this);
    }

    void TrackPlayerResponsePressure(BossPatternTelemetrySample sample)
    {
        TrackMovingAwayPressure(sample);
        TrackGuardPunish(sample);
        TrackAttackCounter(sample);
        TrackDefenseMixup(sample);
    }

    void TrackMovingAwayPressure(BossPatternTelemetrySample sample)
    {
        if (!sample.playerIsMovingAway || IsPressurePattern(sample))
        {
            _movingAwayNoPressureStreak = 0;
            return;
        }

        _movingAwayNoPressureStreak++;
        if (!enableWarningLogs || _movingAwayNoPressureStreak != maxMovingAwayNoPressureStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Moving-away player samples without chase/ranged pressure reached " +
            _movingAwayNoPressureStreak +
            ". lastPattern=" +
            sample.patternId,
            this);
    }

    void TrackGuardPunish(BossPatternTelemetrySample sample)
    {
        if (!sample.playerIsGuarding || IsGuardPunishPattern(sample.patternId))
        {
            _guardNoPunishStreak = 0;
            return;
        }

        _guardNoPunishStreak++;
        if (!enableWarningLogs || _guardNoPunishStreak != maxGuardNoPunishStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Guarding player samples without punish pressure reached " +
            _guardNoPunishStreak +
            ". lastPattern=" +
            sample.patternId,
            this);
    }

    void TrackAttackCounter(BossPatternTelemetrySample sample)
    {
        if (!sample.playerIsAttacking || IsCounterPattern(sample.patternId))
        {
            _attackNoCounterStreak = 0;
            return;
        }

        _attackNoCounterStreak++;
        if (!enableWarningLogs || _attackNoCounterStreak != maxAttackNoCounterStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Attacking player samples without counter pressure reached " +
            _attackNoCounterStreak +
            ". lastPattern=" +
            sample.patternId,
            this);
    }

    void TrackDefenseMixup(BossPatternTelemetrySample sample)
    {
        bool repeatedDefense = sample.playerRecentlyParried || sample.playerRecentlyDodged;
        if (!repeatedDefense || IsDefenseMixupPattern(sample.patternId))
        {
            _defenseNoMixupStreak = 0;
            return;
        }

        _defenseNoMixupStreak++;
        if (!enableWarningLogs || _defenseNoMixupStreak != maxDefenseNoMixupStreak)
            return;

        Debug.LogWarning(
            "[BossCombatSafety] Repeated-defense samples without mixup pressure reached " +
            _defenseNoMixupStreak +
            ". lastPattern=" +
            sample.patternId,
            this);
    }

    static bool IsPressurePattern(BossPatternTelemetrySample sample)
    {
        return sample.usedEngage ||
               sample.patternId == BossPatternId.DashSlash ||
               sample.patternId == BossPatternId.SwordWave;
    }

    static bool IsGuardPunishPattern(BossPatternId patternId)
    {
        return patternId == BossPatternId.HeavySlash ||
               patternId == BossPatternId.BackstepSlash;
    }

    static bool IsCounterPattern(BossPatternId patternId)
    {
        return patternId == BossPatternId.QuickSlash ||
               patternId == BossPatternId.DashSlash ||
               patternId == BossPatternId.BackstepSlash;
    }

    static bool IsDefenseMixupPattern(BossPatternId patternId)
    {
        return patternId == BossPatternId.HeavySlash ||
               patternId == BossPatternId.DashSlash ||
               patternId == BossPatternId.SwordWave;
    }

    static bool IsMovementPostAction(BossPatternPostActionType postActionType)
    {
        return postActionType == BossPatternPostActionType.Backstep ||
               postActionType == BossPatternPostActionType.StrafeLeft ||
               postActionType == BossPatternPostActionType.StrafeRight ||
               postActionType == BossPatternPostActionType.ChaseReposition ||
               postActionType == BossPatternPostActionType.Recenter;
    }
}
