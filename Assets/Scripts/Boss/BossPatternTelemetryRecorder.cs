using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossPatternTelemetryRecorder : MonoBehaviour
{
    const int PatternBucketCount = 6;
    const int PhaseBucketCount = 4;
    const int RecoveryBucketCount = 10;
    const int StateBucketCount = 6;
    const int StateMovingAway = 0;
    const int StateApproaching = 1;
    const int StateDodging = 2;
    const int StateGuarding = 3;
    const int StateAttacking = 4;
    const int StateRecentDefense = 5;

    [SerializeField] private BossController bossController;
    [SerializeField] private bool resetOnEnable = true;
    [SerializeField] private bool logSummary;
    [SerializeField, Min(1)] private int logEverySamples = 10;
    [SerializeField, Min(0f)] private float closeRangeMax = 3f;
    [SerializeField, Min(0f)] private float midRangeMax = 8f;
    [Header("Playtest Verdict")]
    [SerializeField, Min(1)] private int minSamplesForVerdict = 20;
    [SerializeField, Range(0f, 1f)] private float minFarPressureRatio = 0.35f;
    [SerializeField, Range(0f, 1f)] private float minMovingAwayPressureRatio = 0.35f;
    [SerializeField, Range(0f, 1f)] private float minGuardPunishRatio = 0.25f;
    [SerializeField, Range(0f, 1f)] private float minAttackCounterRatio = 0.25f;
    [SerializeField, Range(0f, 1f)] private float minDefenseMixupRatio = 0.25f;
    [SerializeField, Range(0f, 1f)] private float minRecoveryCompletionRatio = 0.80f;
    [SerializeField, Range(0f, 1f)] private float maxRecoveryAbortRatio = 0.15f;
    [SerializeField, Range(0f, 1f)] private float maxRecoveryNoTargetRatio = 0.20f;

    readonly int[] _patternCounts = new int[PatternBucketCount];
    readonly int[] _phaseCounts = new int[PhaseBucketCount];
    readonly int[] _recoveryCounts = new int[RecoveryBucketCount];
    readonly int[] _statePatternCounts = new int[StateBucketCount * PatternBucketCount];
    readonly StringBuilder _summaryBuilder = new StringBuilder(1024);

    BossController _subscribedController;
    int _totalSamples;
    int _followUpSamples;
    int _engageSamples;
    int _closeSamples;
    int _midSamples;
    int _farSamples;
    int _farPressureSamples;
    int _movingAwaySamples;
    int _approachingSamples;
    int _dodgingSamples;
    int _guardingSamples;
    int _attackingSamples;
    int _recentParrySamples;
    int _recentDodgeSamples;
    int _recentHitBossSamples;
    int _recoverySamples;
    int _recoveryCompletedSamples;
    int _recoveryAbortedSamples;
    int _recoveryNoTargetSamples;
    float _distanceSum;
    float _angleSum;
    float _recoveryDurationSum;

    public int TotalSamples => _totalSamples;
    public int FollowUpSamples => _followUpSamples;
    public int EngageSamples => _engageSamples;
    public int CloseSamples => _closeSamples;
    public int MidSamples => _midSamples;
    public int FarSamples => _farSamples;
    public int FarPressureSamples => _farPressureSamples;
    public int MovingAwaySamples => _movingAwaySamples;
    public int ApproachingSamples => _approachingSamples;
    public int DodgingSamples => _dodgingSamples;
    public int GuardingSamples => _guardingSamples;
    public int AttackingSamples => _attackingSamples;
    public int RecentParrySamples => _recentParrySamples;
    public int RecentDodgeSamples => _recentDodgeSamples;
    public int RecentHitBossSamples => _recentHitBossSamples;
    public int RecoverySamples => _recoverySamples;
    public int RecoveryCompletedSamples => _recoveryCompletedSamples;
    public int RecoveryAbortedSamples => _recoveryAbortedSamples;
    public int RecoveryNoTargetSamples => _recoveryNoTargetSamples;

    void OnEnable()
    {
        if (resetOnEnable)
            ResetStats();

        Bind();
    }

    void OnDisable()
    {
        Unbind();
    }

    void OnValidate()
    {
        if (midRangeMax < closeRangeMax)
            midRangeMax = closeRangeMax;
    }

    [ContextMenu("Reset Stats")]
    public void ResetStats()
    {
        _totalSamples = 0;
        _followUpSamples = 0;
        _engageSamples = 0;
        _closeSamples = 0;
        _midSamples = 0;
        _farSamples = 0;
        _farPressureSamples = 0;
        _movingAwaySamples = 0;
        _approachingSamples = 0;
        _dodgingSamples = 0;
        _guardingSamples = 0;
        _attackingSamples = 0;
        _recentParrySamples = 0;
        _recentDodgeSamples = 0;
        _recentHitBossSamples = 0;
        _recoverySamples = 0;
        _recoveryCompletedSamples = 0;
        _recoveryAbortedSamples = 0;
        _recoveryNoTargetSamples = 0;
        _distanceSum = 0f;
        _angleSum = 0f;
        _recoveryDurationSum = 0f;

        for (int i = 0; i < _patternCounts.Length; i++)
            _patternCounts[i] = 0;

        for (int i = 0; i < _phaseCounts.Length; i++)
            _phaseCounts[i] = 0;

        for (int i = 0; i < _recoveryCounts.Length; i++)
            _recoveryCounts[i] = 0;

        for (int i = 0; i < _statePatternCounts.Length; i++)
            _statePatternCounts[i] = 0;
    }

    [ContextMenu("Log Summary")]
    public void LogSummary()
    {
        Debug.Log(BuildSummary(), this);
    }

    [ContextMenu("Log Playtest Verdict")]
    public void LogPlaytestVerdict()
    {
        Debug.Log(BuildPlaytestVerdict(), this);
    }

    void Bind()
    {
        if (bossController == null)
            bossController = GetComponentInParent<BossController>();
        if (bossController == null)
            bossController = GetComponentInChildren<BossController>();

        if (_subscribedController == bossController)
            return;

        Unbind();
        _subscribedController = bossController;
        if (_subscribedController != null)
        {
            _subscribedController.OnPatternTelemetrySample += OnPatternTelemetrySample;
            _subscribedController.OnCombatRecoveryTelemetrySample += OnCombatRecoveryTelemetrySample;
        }
    }

    void Unbind()
    {
        if (_subscribedController != null)
        {
            _subscribedController.OnPatternTelemetrySample -= OnPatternTelemetrySample;
            _subscribedController.OnCombatRecoveryTelemetrySample -= OnCombatRecoveryTelemetrySample;
        }
        _subscribedController = null;
    }

    void OnPatternTelemetrySample(BossPatternTelemetrySample sample)
    {
        _totalSamples++;

        int patternIndex = Mathf.Clamp((int)sample.patternId, 0, PatternBucketCount - 1);
        _patternCounts[patternIndex]++;

        int phaseIndex = Mathf.Clamp(sample.phase, 0, PhaseBucketCount - 1);
        _phaseCounts[phaseIndex]++;

        if (sample.isFollowUp)
            _followUpSamples++;
        if (sample.usedEngage)
            _engageSamples++;
        if (sample.playerIsMovingAway)
        {
            _movingAwaySamples++;
            TrackStatePattern(StateMovingAway, patternIndex);
        }
        if (sample.playerIsApproaching)
        {
            _approachingSamples++;
            TrackStatePattern(StateApproaching, patternIndex);
        }
        if (sample.playerIsDodging)
        {
            _dodgingSamples++;
            TrackStatePattern(StateDodging, patternIndex);
        }
        if (sample.playerIsGuarding)
        {
            _guardingSamples++;
            TrackStatePattern(StateGuarding, patternIndex);
        }
        if (sample.playerIsAttacking)
        {
            _attackingSamples++;
            TrackStatePattern(StateAttacking, patternIndex);
        }
        if (sample.playerRecentlyParried)
        {
            _recentParrySamples++;
            TrackStatePattern(StateRecentDefense, patternIndex);
        }
        if (sample.playerRecentlyDodged)
        {
            _recentDodgeSamples++;
            TrackStatePattern(StateRecentDefense, patternIndex);
        }
        if (sample.playerRecentlyHitBoss)
            _recentHitBossSamples++;

        float distance = float.IsInfinity(sample.distanceToPlayer) ? midRangeMax : Mathf.Max(0f, sample.distanceToPlayer);
        _distanceSum += distance;
        _angleSum += Mathf.Clamp(sample.angleToPlayer, 0f, 180f);

        if (distance <= closeRangeMax)
            _closeSamples++;
        else if (distance <= midRangeMax)
            _midSamples++;
        else
        {
            _farSamples++;
            if (IsPressurePattern(sample.patternId, sample.usedEngage))
                _farPressureSamples++;
        }

        if (logSummary && _totalSamples % Mathf.Max(1, logEverySamples) == 0)
            Debug.Log(BuildSummary(), this);
    }

    void OnCombatRecoveryTelemetrySample(BossCombatRecoveryTelemetrySample sample)
    {
        _recoverySamples++;
        if (sample.completed)
            _recoveryCompletedSamples++;
        if (sample.aborted)
            _recoveryAbortedSamples++;
        if (!sample.targetValid)
            _recoveryNoTargetSamples++;

        int recoveryIndex = Mathf.Clamp((int)sample.reason, 0, RecoveryBucketCount - 1);
        _recoveryCounts[recoveryIndex]++;
        _recoveryDurationSum += Mathf.Max(0f, sample.duration);
    }

    void TrackStatePattern(int stateIndex, int patternIndex)
    {
        if (stateIndex < 0 || stateIndex >= StateBucketCount)
            return;

        patternIndex = Mathf.Clamp(patternIndex, 0, PatternBucketCount - 1);
        _statePatternCounts[(stateIndex * PatternBucketCount) + patternIndex]++;
    }

    string BuildSummary()
    {
        _summaryBuilder.Clear();
        _summaryBuilder.Append("[BossPatternTelemetrySummary] samples=").Append(_totalSamples);

        if (_totalSamples > 0)
        {
            _summaryBuilder.Append(" avgDist=").Append((_distanceSum / _totalSamples).ToString("0.00"));
            _summaryBuilder.Append(" avgAngle=").Append((_angleSum / _totalSamples).ToString("0"));
        }

        _summaryBuilder.Append(" engage=").Append(_engageSamples);
        _summaryBuilder.Append(" followUp=").Append(_followUpSamples);
        _summaryBuilder.Append(" range[C/M/F]=").Append(_closeSamples).Append('/').Append(_midSamples).Append('/').Append(_farSamples);
        _summaryBuilder.Append(" farPressure=").Append(_farPressureSamples).Append('/').Append(_farSamples);
        _summaryBuilder.Append(" player[away/approach/dodge/guard/attack]=")
            .Append(_movingAwaySamples).Append('/')
            .Append(_approachingSamples).Append('/')
            .Append(_dodgingSamples).Append('/')
            .Append(_guardingSamples).Append('/')
            .Append(_attackingSamples);
        _summaryBuilder.Append(" recent[parry/dodge/hitBoss]=")
            .Append(_recentParrySamples).Append('/')
            .Append(_recentDodgeSamples).Append('/')
            .Append(_recentHitBossSamples);
        _summaryBuilder.Append(" recovery[total/completed/aborted/noTarget]=")
            .Append(_recoverySamples).Append('/')
            .Append(_recoveryCompletedSamples).Append('/')
            .Append(_recoveryAbortedSamples).Append('/')
            .Append(_recoveryNoTargetSamples);
        if (_recoverySamples > 0)
            _summaryBuilder.Append(" avgRecovery=").Append((_recoveryDurationSum / _recoverySamples).ToString("0.00"));
        _summaryBuilder.Append(" recoveryReason[parry/break/stagger/knock/ult/down/phase/reacquire/cutscene]=")
            .Append(_recoveryCounts[(int)BossRecoveryReason.ParryStun]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.Break]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.Stagger]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.Knockback]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.UltimateVictim]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.Down]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.PhaseTransition]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.TargetReacquire]).Append('/')
            .Append(_recoveryCounts[(int)BossRecoveryReason.CutsceneAttack]);
        _summaryBuilder.Append(" phase[1/2/3]=").Append(_phaseCounts[1]).Append('/').Append(_phaseCounts[2]).Append('/').Append(_phaseCounts[3]);
        _summaryBuilder.Append(" pattern[Quick/Dash/Wave/Heavy/Backstep]=")
            .Append(_patternCounts[(int)BossPatternId.QuickSlash]).Append('/')
            .Append(_patternCounts[(int)BossPatternId.DashSlash]).Append('/')
            .Append(_patternCounts[(int)BossPatternId.SwordWave]).Append('/')
            .Append(_patternCounts[(int)BossPatternId.HeavySlash]).Append('/')
            .Append(_patternCounts[(int)BossPatternId.BackstepSlash]);
        AppendStatePatternSummary("away", StateMovingAway);
        AppendStatePatternSummary("approach", StateApproaching);
        AppendStatePatternSummary("dodge", StateDodging);
        AppendStatePatternSummary("guard", StateGuarding);
        AppendStatePatternSummary("attack", StateAttacking);
        AppendStatePatternSummary("defense", StateRecentDefense);

        return _summaryBuilder.ToString();
    }

    string BuildPlaytestVerdict()
    {
        _summaryBuilder.Clear();
        _summaryBuilder.Append("[BossPatternTelemetryVerdict] samples=").Append(_totalSamples);

        bool pass = _totalSamples >= minSamplesForVerdict;
        if (_totalSamples < minSamplesForVerdict)
            _summaryBuilder.Append(" issue=insufficient_samples(").Append(_totalSamples).Append('/').Append(minSamplesForVerdict).Append(')');

        AppendRatioVerdict("farPressure", _farPressureSamples, _farSamples, minFarPressureRatio, ref pass);
        AppendRatioVerdict(
            "movingAwayPressure",
            CountStatePatterns(StateMovingAway, BossPatternId.DashSlash, BossPatternId.SwordWave),
            CountStateTotal(StateMovingAway),
            minMovingAwayPressureRatio,
            ref pass);
        AppendRatioVerdict(
            "guardPunish",
            CountStatePatterns(StateGuarding, BossPatternId.HeavySlash, BossPatternId.BackstepSlash),
            CountStateTotal(StateGuarding),
            minGuardPunishRatio,
            ref pass);
        AppendRatioVerdict(
            "attackCounter",
            CountStatePatterns(StateAttacking, BossPatternId.QuickSlash, BossPatternId.DashSlash, BossPatternId.BackstepSlash),
            CountStateTotal(StateAttacking),
            minAttackCounterRatio,
            ref pass);
        AppendRatioVerdict(
            "defenseMixup",
            CountStatePatterns(StateRecentDefense, BossPatternId.HeavySlash, BossPatternId.DashSlash, BossPatternId.SwordWave),
            CountStateTotal(StateRecentDefense),
            minDefenseMixupRatio,
            ref pass);
        if (_recoverySamples > 0)
        {
            AppendRatioVerdict("recoveryCompleted", _recoveryCompletedSamples, _recoverySamples, minRecoveryCompletionRatio, ref pass);
            AppendMaxRatioVerdict("recoveryAborted", _recoveryAbortedSamples, _recoverySamples, maxRecoveryAbortRatio, ref pass);
            AppendMaxRatioVerdict("recoveryNoTarget", _recoveryNoTargetSamples, _recoverySamples, maxRecoveryNoTargetRatio, ref pass);
        }

        _summaryBuilder.Append(" result=").Append(pass ? "pass" : "needs_tuning");
        return _summaryBuilder.ToString();
    }

    void AppendStatePatternSummary(string label, int stateIndex)
    {
        int baseIndex = stateIndex * PatternBucketCount;
        _summaryBuilder.Append(' ').Append(label).Append("[Q/D/W/H/B]=")
            .Append(_statePatternCounts[baseIndex + (int)BossPatternId.QuickSlash]).Append('/')
            .Append(_statePatternCounts[baseIndex + (int)BossPatternId.DashSlash]).Append('/')
            .Append(_statePatternCounts[baseIndex + (int)BossPatternId.SwordWave]).Append('/')
            .Append(_statePatternCounts[baseIndex + (int)BossPatternId.HeavySlash]).Append('/')
            .Append(_statePatternCounts[baseIndex + (int)BossPatternId.BackstepSlash]);
    }

    void AppendRatioVerdict(string label, int matchCount, int totalCount, float minimumRatio, ref bool pass)
    {
        if (totalCount <= 0)
        {
            _summaryBuilder.Append(' ').Append(label).Append("=no_samples");
            return;
        }

        float ratio = (float)matchCount / totalCount;
        _summaryBuilder.Append(' ')
            .Append(label)
            .Append('=')
            .Append(matchCount)
            .Append('/')
            .Append(totalCount)
            .Append('(')
            .Append(ratio.ToString("0.00"))
            .Append(')');

        if (ratio >= minimumRatio)
            return;

        pass = false;
        _summaryBuilder.Append("<").Append(minimumRatio.ToString("0.00"));
    }

    void AppendMaxRatioVerdict(string label, int matchCount, int totalCount, float maximumRatio, ref bool pass)
    {
        if (totalCount <= 0)
        {
            _summaryBuilder.Append(' ').Append(label).Append("=no_samples");
            return;
        }

        float ratio = (float)matchCount / totalCount;
        _summaryBuilder.Append(' ')
            .Append(label)
            .Append('=')
            .Append(matchCount)
            .Append('/')
            .Append(totalCount)
            .Append('(')
            .Append(ratio.ToString("0.00"))
            .Append(')');

        if (ratio <= maximumRatio)
            return;

        pass = false;
        _summaryBuilder.Append(">").Append(maximumRatio.ToString("0.00"));
    }

    int CountStateTotal(int stateIndex)
    {
        if (stateIndex < 0 || stateIndex >= StateBucketCount)
            return 0;

        int total = 0;
        int baseIndex = stateIndex * PatternBucketCount;
        for (int i = 1; i < PatternBucketCount; i++)
            total += _statePatternCounts[baseIndex + i];

        return total;
    }

    int CountStatePatterns(int stateIndex, BossPatternId first, BossPatternId second)
    {
        return CountStatePattern(stateIndex, first) + CountStatePattern(stateIndex, second);
    }

    int CountStatePatterns(int stateIndex, BossPatternId first, BossPatternId second, BossPatternId third)
    {
        return CountStatePattern(stateIndex, first) +
               CountStatePattern(stateIndex, second) +
               CountStatePattern(stateIndex, third);
    }

    int CountStatePattern(int stateIndex, BossPatternId patternId)
    {
        if (stateIndex < 0 || stateIndex >= StateBucketCount)
            return 0;

        int patternIndex = Mathf.Clamp((int)patternId, 0, PatternBucketCount - 1);
        return _statePatternCounts[(stateIndex * PatternBucketCount) + patternIndex];
    }

    static bool IsPressurePattern(BossPatternId patternId, bool usedEngage)
    {
        return usedEngage ||
               patternId == BossPatternId.DashSlash ||
               patternId == BossPatternId.SwordWave;
    }
}
