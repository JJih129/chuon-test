using System;
using UnityEngine;

public enum BossPatternId
{
    None = 0,
    QuickSlash = 1,
    DashSlash = 2,
    SwordWave = 3,
    HeavySlash = 4,
    BackstepSlash = 5
}

public enum BossPatternPostActionType
{
    None = 0,
    CombatIdle = 1,
    Backstep = 2,
    StrafeLeft = 3,
    StrafeRight = 4,
    ChaseReposition = 5,
    Recenter = 6
}

public enum BossPatternEngageMode
{
    None = 0,
    RequireInRange = 1,
    ChaseUntilInRange = 2,
    DashEngage = 3,
    UseRangedFallback = 4
}

[Serializable]
public struct BossPlayerCombatObservation
{
    public float playerDistance;
    public float playerAngle;
    public bool playerIsMovingAway;
    public bool playerIsApproaching;
    public bool playerIsDodging;
    public bool playerIsGuarding;
    public bool playerIsAttacking;
    public bool playerIsStunned;
    public bool playerIsDowned;
    public bool playerRecentlyParried;
    public bool playerRecentlyDodged;
    public bool playerRecentlyHitBoss;
    public float timeSincePlayerLastAttack;
    public float timeSinceBossLastHitPlayer;

    public static BossPlayerCombatObservation CreateInvalid()
    {
        return new BossPlayerCombatObservation
        {
            playerDistance = float.PositiveInfinity,
            playerAngle = 180f,
            timeSincePlayerLastAttack = float.PositiveInfinity,
            timeSinceBossLastHitPlayer = float.PositiveInfinity
        };
    }
}

[Serializable]
public struct BossPatternTelemetrySample
{
    public BossPatternId patternId;
    public BossPatternPostActionType postActionType;
    public BossPatternEngageMode engageMode;
    public BossAttackTelegraphType telegraphType;
    public int phase;
    public float bossHpNormalized;
    public float distanceToPlayer;
    public float angleToPlayer;
    public bool isFollowUp;
    public bool usedEngage;
    public bool playerIsMovingAway;
    public bool playerIsApproaching;
    public bool playerIsDodging;
    public bool playerIsGuarding;
    public bool playerIsAttacking;
    public bool playerRecentlyParried;
    public bool playerRecentlyDodged;
    public bool playerRecentlyHitBoss;
    public float time;
}

[Serializable]
public struct BossPhasePatternModifier
{
    public BossPatternId patternId;
    [Range(1, 3)] public int phase;
    public float baseWeightMultiplier;
    public float cooldownMultiplier;
    public float telegraphDurationMultiplier;
    public float recoveryDurationMultiplier;
    public float damageMultiplier;
    public float engageMoveSpeedMultiplier;
    public bool overridePostAction;
    public BossPatternPostActionType postActionOverride;
    public BossPatternId followUpPatternId;
    public bool overrideFollowUp;
    public bool allowFollowUp;
    public int maxFollowUpCount;

    public static float ResolveMultiplier(float value)
    {
        return value > 0.001f ? Mathf.Max(0f, value) : 1f;
    }
}

[Serializable]
public struct BossPatternData
{
    public BossPatternId patternId;
    public float minRange;
    public float maxRange;
    public float minAngle;
    public float maxAngle;
    public float baseWeight;
    public float cooldown;
    public int minPhase;
    public int maxPhase;
    public bool canRepeat;
    public int recentRepeatBlockCount;
    public bool isFallback;
    public int fallbackPriority;
    public BossPatternPostActionType postActionType;
    public BossPatternEngageMode engageMode;
    public float engageStartRange;
    public float engageStopRange;
    public float engageMaxDuration;
    public float engageMoveSpeedMultiplier;
    public BossPatternId rangedFallbackPatternId;
    public BossPatternId dashFallbackPatternId;

    public static BossPatternData CreateDefault(BossPatternId patternId)
    {
        return new BossPatternData
        {
            patternId = patternId,
            minRange = 0f,
            maxRange = 5f,
            minAngle = 0f,
            maxAngle = 180f,
            baseWeight = 1f,
            cooldown = 2f,
            minPhase = 1,
            maxPhase = 3,
            canRepeat = false,
            recentRepeatBlockCount = 1,
            isFallback = true,
            fallbackPriority = 0,
            postActionType = BossPatternPostActionType.CombatIdle,
            engageMode = BossPatternEngageMode.RequireInRange,
            engageStartRange = 0f,
            engageStopRange = 0f,
            engageMaxDuration = 0.5f,
            engageMoveSpeedMultiplier = 1f,
            rangedFallbackPatternId = BossPatternId.SwordWave,
            dashFallbackPatternId = BossPatternId.DashSlash
        };
    }
}

public struct BossPatternContext
{
    public readonly float distanceToTarget;
    public readonly float angleToTarget;
    public readonly int currentPhase;
    public readonly float currentTime;
    public readonly BossPlayerCombatObservation playerObservation;

    public BossPatternContext(float distanceToTarget, float angleToTarget, int currentPhase, float currentTime)
        : this(distanceToTarget, angleToTarget, currentPhase, currentTime, BossPlayerCombatObservation.CreateInvalid())
    {
    }

    public BossPatternContext(
        float distanceToTarget,
        float angleToTarget,
        int currentPhase,
        float currentTime,
        BossPlayerCombatObservation playerObservation)
    {
        this.distanceToTarget = distanceToTarget;
        this.angleToTarget = angleToTarget;
        this.currentPhase = currentPhase;
        this.currentTime = currentTime;
        this.playerObservation = playerObservation;
    }
}

[Serializable]
public struct BossPatternRuntimeState
{
    public BossPatternId patternId;
    public float lastUsedTime;

    public BossPatternRuntimeState(BossPatternId patternId)
    {
        this.patternId = patternId;
        lastUsedTime = -9999f;
    }
}

[Serializable]
public sealed class BossPatternHistory
{
    [SerializeField, Range(1, 8)] private int capacity = 3;

    private BossPatternId[] _entries;
    private int _writeIndex;
    private int _count;

    public void EnsureInitialized()
    {
        int resolvedCapacity = Mathf.Clamp(capacity, 1, 8);
        if (_entries != null && _entries.Length == resolvedCapacity)
            return;

        _entries = new BossPatternId[resolvedCapacity];
        _writeIndex = 0;
        _count = 0;
    }

    public void Record(BossPatternId patternId)
    {
        if (patternId == BossPatternId.None)
            return;

        EnsureInitialized();
        _entries[_writeIndex] = patternId;
        _writeIndex = (_writeIndex + 1) % _entries.Length;
        _count = Mathf.Min(_entries.Length, _count + 1);
    }

    public bool WasUsedRecently(BossPatternId patternId, int lookback)
    {
        if (patternId == BossPatternId.None || _count <= 0)
            return false;

        EnsureInitialized();
        int count = Mathf.Min(_count, Mathf.Clamp(lookback, 1, _entries.Length));
        for (int i = 0; i < count; i++)
        {
            int index = (_writeIndex - 1 - i + _entries.Length) % _entries.Length;
            if (_entries[index] == patternId)
                return true;
        }

        return false;
    }
}

[Serializable]
public sealed class BossPatternSelector
{
    [SerializeField] private bool enableDebugLog;
    [SerializeField, Range(0.05f, 1f)] private float recentRepeatWeightMultiplier = 0.55f;

    private BossPatternData[] _patterns;
    private BossPatternRuntimeState[] _runtimeStates;
    private BossPatternHistory _history;
    private int[] _candidateIndices = new int[8];
    private float[] _candidateWeights = new float[8];
    private UnityEngine.Object _logContext;

    public bool EnableDebugLog
    {
        get => enableDebugLog;
        set => enableDebugLog = value;
    }

    public void Configure(
        BossPatternData[] patterns,
        BossPatternRuntimeState[] runtimeStates,
        BossPatternHistory history,
        UnityEngine.Object logContext)
    {
        _patterns = patterns;
        _runtimeStates = runtimeStates;
        _history = history;
        _logContext = logContext;
        _history?.EnsureInitialized();
        EnsureCandidateCapacity(patterns != null ? patterns.Length : 0);
    }

    public bool TrySelect(BossPatternContext context, out int selectedIndex, out BossPatternId selectedPatternId)
    {
        selectedIndex = -1;
        selectedPatternId = BossPatternId.None;

        if (_patterns == null || _runtimeStates == null || _patterns.Length == 0 || _runtimeStates.Length == 0)
            return false;

        int patternCount = Mathf.Min(_patterns.Length, _runtimeStates.Length);
        int candidateCount = 0;
        float totalWeight = 0f;

        for (int i = 0; i < patternCount; i++)
        {
            BossPatternData pattern = _patterns[i];
            if (!IsSelectable(pattern, _runtimeStates[i], context, out string reason))
            {
                LogExclude(pattern, reason);
                continue;
            }

            float weight = Mathf.Max(0f, pattern.baseWeight);
            if (weight <= 0f)
            {
                LogExclude(pattern, "weight");
                continue;
            }

            weight *= ResolveObservationWeightBias(context, pattern.patternId);

            if (pattern.canRepeat && _history != null && _history.WasUsedRecently(pattern.patternId, 1))
                weight *= Mathf.Clamp(recentRepeatWeightMultiplier, 0.05f, 1f);

            _candidateIndices[candidateCount] = i;
            _candidateWeights[candidateCount] = weight;
            candidateCount++;
            totalWeight += weight;
        }

        if (candidateCount > 0 && totalWeight > 0f)
        {
            selectedIndex = SelectWeightedIndex(candidateCount, totalWeight);
            if (selectedIndex >= 0)
            {
                selectedPatternId = _patterns[selectedIndex].patternId;
                return true;
            }
        }

        return TrySelectFallback(context, patternCount, out selectedIndex, out selectedPatternId);
    }

    public void MarkUsed(int patternIndex, float currentTime)
    {
        if (_patterns == null || _runtimeStates == null)
            return;

        if (patternIndex < 0 || patternIndex >= _patterns.Length || patternIndex >= _runtimeStates.Length)
            return;

        BossPatternId patternId = _patterns[patternIndex].patternId;
        _runtimeStates[patternIndex].patternId = patternId;
        _runtimeStates[patternIndex].lastUsedTime = currentTime;
        _history?.Record(patternId);
    }

    private bool IsSelectable(
        BossPatternData pattern,
        BossPatternRuntimeState runtimeState,
        BossPatternContext context,
        out string reason)
    {
        reason = null;

        if (pattern.patternId == BossPatternId.None)
        {
            reason = "none";
            return false;
        }

        if (context.currentPhase < pattern.minPhase || context.currentPhase > pattern.maxPhase)
        {
            reason = "phase";
            return false;
        }

        if (context.distanceToTarget < pattern.minRange || context.distanceToTarget > pattern.maxRange)
        {
            reason = "range";
            return false;
        }

        float angle = Mathf.Abs(context.angleToTarget);
        if (angle < pattern.minAngle || angle > pattern.maxAngle)
        {
            reason = "angle";
            return false;
        }

        if (pattern.cooldown > 0f && context.currentTime < runtimeState.lastUsedTime + pattern.cooldown)
        {
            reason = "cooldown";
            return false;
        }

        if (!pattern.canRepeat && _history != null && _history.WasUsedRecently(pattern.patternId, pattern.recentRepeatBlockCount))
        {
            reason = "repeat";
            return false;
        }

        return true;
    }

    private bool TrySelectFallback(BossPatternContext context, int patternCount, out int selectedIndex, out BossPatternId selectedPatternId)
    {
        selectedIndex = -1;
        selectedPatternId = BossPatternId.None;

        int bestPriority = int.MinValue;
        float bestScore = float.PositiveInfinity;

        for (int i = 0; i < patternCount; i++)
        {
            BossPatternData pattern = _patterns[i];
            if (!IsFallbackSelectable(pattern, _runtimeStates[i], context))
                continue;

            float rangeGap = GetRangeGap(context.distanceToTarget, pattern);
            float distanceBias = ResolveFallbackDistanceBias(context, pattern.patternId);
            int effectivePriority = pattern.fallbackPriority + ResolveFallbackPriorityBias(context, pattern.patternId, _history);
            float score = rangeGap + distanceBias;

            if (effectivePriority < bestPriority)
                continue;

            if (effectivePriority == bestPriority && score >= bestScore)
                continue;

            bestPriority = effectivePriority;
            bestScore = score;
            selectedIndex = i;
            selectedPatternId = pattern.patternId;
        }

        if (selectedIndex >= 0)
        {
            if (enableDebugLog)
                Debug.Log($"[BossPatternSelector] fallback={selectedPatternId} priority={bestPriority} score={bestScore:0.00}", _logContext);
            return true;
        }

        if (enableDebugLog)
            Debug.Log("[BossPatternSelector] no fallback pattern", _logContext);

        return false;
    }

    private bool IsFallbackSelectable(
        BossPatternData pattern,
        BossPatternRuntimeState runtimeState,
        BossPatternContext context)
    {
        if (!pattern.isFallback || pattern.patternId == BossPatternId.None)
            return false;

        if (context.currentPhase < pattern.minPhase || context.currentPhase > pattern.maxPhase)
            return false;

        float angle = Mathf.Abs(context.angleToTarget);
        if (angle < pattern.minAngle || angle > pattern.maxAngle)
            return false;

        if (pattern.cooldown > 0f && context.currentTime < runtimeState.lastUsedTime + pattern.cooldown)
            return false;

        if (!pattern.canRepeat && _history != null && _history.WasUsedRecently(pattern.patternId, pattern.recentRepeatBlockCount))
            return false;

        return pattern.baseWeight > 0f;
    }

    private int SelectWeightedIndex(int candidateCount, float totalWeight)
    {
        if (candidateCount <= 0)
            return -1;

        if (totalWeight <= 0f)
            return -1;

        float value = UnityEngine.Random.value * totalWeight;
        float accum = 0f;
        for (int i = 0; i < candidateCount; i++)
        {
            accum += _candidateWeights[i];
            if (value <= accum)
                return _candidateIndices[i];
        }

        return _candidateIndices[candidateCount - 1];
    }

    private void EnsureCandidateCapacity(int count)
    {
        if (_candidateIndices != null && _candidateIndices.Length >= count)
            return;

        int capacity = Mathf.Max(8, count);
        _candidateIndices = new int[capacity];
        _candidateWeights = new float[capacity];
    }

    private static float GetRangeGap(float distance, BossPatternData pattern)
    {
        if (distance < pattern.minRange)
            return pattern.minRange - distance;

        if (distance > pattern.maxRange)
            return distance - pattern.maxRange;

        return 0f;
    }

    private static float ResolveFallbackDistanceBias(BossPatternContext context, BossPatternId patternId)
    {
        float distance = context.distanceToTarget;
        bool far = distance >= 3.2f;
        BossPlayerCombatObservation observation = context.playerObservation;
        bool hasObservation = !float.IsInfinity(observation.playerDistance);
        bool playerEscaping = hasObservation && observation.playerIsMovingAway;
        bool playerClosePressure = hasObservation && (observation.playerIsApproaching || observation.playerRecentlyHitBoss);
        bool repeatedDefense = hasObservation && (observation.playerRecentlyParried || observation.playerRecentlyDodged);

        switch (patternId)
        {
            case BossPatternId.SwordWave:
            case BossPatternId.DashSlash:
                return (far ? 0f : 2f) + (playerEscaping ? -0.35f : 0f);
            case BossPatternId.QuickSlash:
                return (far ? 2f : 0f) + (playerEscaping ? 0.65f : 0f) + (repeatedDefense ? 0.35f : 0f);
            case BossPatternId.BackstepSlash:
                return (far ? 2f : 0f) + (playerClosePressure ? -0.35f : 0f);
            case BossPatternId.HeavySlash:
                return playerClosePressure ? -0.25f : 1f;
            default:
                return 1f;
        }
    }

    private static int ResolveFallbackPriorityBias(
        BossPatternContext context,
        BossPatternId patternId,
        BossPatternHistory history)
    {
        int bias = 0;
        float distance = context.distanceToTarget;
        bool close = distance < 3.2f;
        bool far = distance >= 6.0f;

        if (close)
        {
            if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.BackstepSlash)
                bias += 18;
            else if (patternId == BossPatternId.SwordWave)
                bias -= 35;
        }
        else if (far)
        {
            if (patternId == BossPatternId.DashSlash)
                bias += 22;
            else if (patternId == BossPatternId.SwordWave)
                bias += 8;
            else if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                bias -= 20;
        }

        if (history != null)
        {
            if (history.WasUsedRecently(patternId, 1))
                bias -= 35;
            else if (history.WasUsedRecently(patternId, 2))
                bias -= 16;
        }

        return bias;
    }

    private static float ResolveObservationWeightBias(BossPatternContext context, BossPatternId patternId)
    {
        BossPlayerCombatObservation observation = context.playerObservation;
        if (float.IsInfinity(observation.playerDistance))
            return 1f;

        float multiplier = 1f;
        bool far = observation.playerDistance >= 3.2f;

        if (observation.playerIsMovingAway)
        {
            if (patternId == BossPatternId.SwordWave || patternId == BossPatternId.DashSlash)
                multiplier *= far ? 1.12f : 1.06f;
            else if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= 0.94f;
        }

        if (observation.playerIsApproaching || observation.playerRecentlyHitBoss)
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= 1.10f;
            else if (patternId == BossPatternId.SwordWave && observation.playerDistance <= 3.2f)
                multiplier *= 0.90f;
        }

        if (observation.playerIsDodging || observation.playerRecentlyDodged)
        {
            if (patternId == BossPatternId.HeavySlash)
                multiplier *= 1.08f;
            else if (patternId == BossPatternId.QuickSlash)
                multiplier *= 0.94f;
        }

        if (observation.playerIsGuarding)
        {
            if (patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash)
                multiplier *= 1.08f;
        }

        if (observation.playerIsAttacking)
        {
            if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.DashSlash || patternId == BossPatternId.BackstepSlash)
                multiplier *= 1.06f;
        }

        if (observation.playerIsStunned || observation.playerIsDowned)
        {
            if (patternId == BossPatternId.SwordWave)
                multiplier *= 0.86f;
            else if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= 1.05f;
        }

        return Mathf.Clamp(multiplier, 0.70f, 1.35f);
    }

#if UNITY_EDITOR
    public static float CalculateObservationWeightBiasForValidation(BossPatternContext context, BossPatternId patternId)
    {
        return ResolveObservationWeightBias(context, patternId);
    }
#endif

    private void LogExclude(BossPatternData pattern, string reason)
    {
        if (!enableDebugLog)
            return;

        Debug.Log($"[BossPatternSelector] exclude={pattern.patternId} reason={reason}", _logContext);
    }
}
