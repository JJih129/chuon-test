// 파일명: BossPlayerObservationTracker.cs
// 플레이어 전투 상태 관찰(거리/각도/회피/가드/공격 등) + 반응 지연 + 플레이어 상태 기반 패턴 가중치 계산.
// 직렬화 설정값은 BossController에 그대로 두고 IBossPlayerObservationSettings로 읽어온다.

using UnityEngine;

public interface IBossPlayerObservationSettings
{
    bool UsePlayerStatePatternBias { get; }
    float FarPressureDistance { get; }
    float FarPressureDelay { get; }
    float FarPressureRangedWeightMultiplier { get; }
    float FarPressureChaseWeightMultiplier { get; }
    float FarPressureEngageSpeedMultiplier { get; }
    float CloseLingerDistance { get; }
    float CloseLingerDelay { get; }
    float CloseLingerPunishWeightMultiplier { get; }
    float CloseLingerRangedWeightMultiplier { get; }
    float PlayerDefenseResponseMemoryTime { get; }
    int RepeatedParryResponseThreshold { get; }
    int RepeatedPerfectDodgeResponseThreshold { get; }
    float RepeatedParryParryableWeightMultiplier { get; }
    float RepeatedParryDodgeOnlyWeightMultiplier { get; }
    float RepeatedPerfectDodgeDodgeOnlyWeightMultiplier { get; }
    float RepeatedPerfectDodgeParryableWeightMultiplier { get; }
    float PlayerObservationMoveSpeedThreshold { get; }
    float PlayerDodgingDelayedAttackWeightMultiplier { get; }
    float PlayerGuardingPunishWeightMultiplier { get; }
    float PlayerAttackingCounterWeightMultiplier { get; }
    float PlayerStunnedRangedWeightMultiplier { get; }
    float PlayerRecentlyHitBossCounterWeightMultiplier { get; }
    float BossMissedPressureDelay { get; }
    float BossMissedPressureWeightMultiplier { get; }
    float PlayerSideAngleThreshold { get; }
    float PlayerBackAngleThreshold { get; }
    float PlayerBackAngleCounterWeightMultiplier { get; }
    bool EnablePlayerObservationDebugLog { get; }
    float PlayerObservationDebugInterval { get; }
    int ObservationPhase { get; }
    float ObservationReactionDelay { get; }
    BossPatternId ResolvePatternId(AttackPattern pattern);
}

public sealed class BossPlayerObservationTracker
{
    readonly IBossPlayerObservationSettings _s;

    float _farPressureStartedAt = float.NegativeInfinity;
    float _closeLingerStartedAt = float.NegativeInfinity;
    float _lastParryResponseAt = float.NegativeInfinity;
    int _recentParryResponseCount;
    float _lastPerfectDodgeResponseAt = float.NegativeInfinity;
    int _recentPerfectDodgeResponseCount;
    BossPlayerCombatObservation _playerObservation = BossPlayerCombatObservation.CreateInvalid();
    BossPlayerCombatObservation _effectivePlayerObservation = BossPlayerCombatObservation.CreateInvalid();
    BossPlayerCombatObservation _pendingPlayerReactionObservation = BossPlayerCombatObservation.CreateInvalid();
    bool _hasPendingPlayerReactionObservation;
    float _pendingPlayerReactionReadyAt = float.NegativeInfinity;
    ICombatStateReader _cachedPlayerCombatStateReader;
    PlayerHealth _cachedPlayerHealth;
    Transform _lastObservedPlayerTarget;
    Vector3 _lastObservedPlayerPosition;
    float _lastPlayerObservationTime = float.NegativeInfinity;
    float _lastPlayerAttackAt = float.NegativeInfinity;
    float _lastBossHitPlayerAt = float.NegativeInfinity;
    float _lastPlayerHitBossAt = float.NegativeInfinity;
    float _nextPlayerObservationDebugLogAt = float.NegativeInfinity;

    public BossPlayerObservationTracker(IBossPlayerObservationSettings settings)
    {
        _s = settings;
    }

    public BossPlayerCombatObservation Observation => _playerObservation;

    /// <summary>관찰 비활성(사망/타겟 없음 등) 상태로 초기화.</summary>
    public void ResetInactive()
    {
        ResetPlayerCombatObservationState();
        _farPressureStartedAt = float.NegativeInfinity;
        _closeLingerStartedAt = float.NegativeInfinity;
    }

    /// <summary>매 프레임 관찰 갱신. 호출자는 타겟 유효성을 미리 검사한다.</summary>
    public void Tick(Transform bossTransform, Transform playerTarget, bool playerTargetValid, Object logContext)
    {
        RefreshPlayerCombatObservation(bossTransform, playerTarget, playerTargetValid);
        ApplyPlayerReactionDelay(_playerObservation, Time.time);
        BossPlayerCombatObservation observation = _playerObservation;
        float now = Time.time;

        if (observation.playerDistance >= _s.FarPressureDistance || observation.playerIsMovingAway)
        {
            if (float.IsNegativeInfinity(_farPressureStartedAt))
                _farPressureStartedAt = now;
        }
        else
        {
            _farPressureStartedAt = float.NegativeInfinity;
        }

        if (observation.playerDistance <= _s.CloseLingerDistance)
        {
            if (float.IsNegativeInfinity(_closeLingerStartedAt))
                _closeLingerStartedAt = now;
        }
        else
        {
            _closeLingerStartedAt = float.NegativeInfinity;
        }

        LogPlayerCombatObservation(now, logContext);
    }

    void RefreshPlayerCombatObservation(Transform bossTransform, Transform playerTarget, bool playerTargetValid)
    {
        if (playerTarget == null || !playerTargetValid)
        {
            ResetPlayerCombatObservationState();
            return;
        }

        float now = Time.time;
        Vector3 bossPos = bossTransform.position;
        Vector3 playerPos = playerTarget.position;
        Vector3 toPlayer = playerPos - bossPos;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        float angle = 180f;
        if (toPlayer.sqrMagnitude > 0.0001f)
            angle = Mathf.Abs(Vector3.SignedAngle(bossTransform.forward, toPlayer.normalized, Vector3.up));

        bool movingAway = false;
        bool approaching = false;
        if (_lastObservedPlayerTarget == playerTarget && !float.IsNegativeInfinity(_lastPlayerObservationTime))
        {
            float deltaTime = Mathf.Max(0.0001f, now - _lastPlayerObservationTime);
            Vector3 displacement = playerPos - _lastObservedPlayerPosition;
            displacement.y = 0f;
            Vector3 velocity = displacement / deltaTime;
            float speedThreshold = Mathf.Max(0.01f, _s.PlayerObservationMoveSpeedThreshold);
            if (velocity.sqrMagnitude >= speedThreshold * speedThreshold && toPlayer.sqrMagnitude > 0.0001f)
            {
                Vector3 awayFromBoss = toPlayer.normalized;
                float radialSpeed = Vector3.Dot(velocity, awayFromBoss);
                movingAway = radialSpeed >= speedThreshold;
                approaching = radialSpeed <= -speedThreshold;
            }
        }

        ICombatStateReader combatState = ResolvePlayerCombatStateReader(playerTarget);
        PlayerHealth playerHealth = ResolvePlayerHealth(playerTarget);
        bool playerIsAttacking = combatState != null && combatState.IsAttacking();
        bool playerIsDodging = combatState != null && combatState.IsDodging();
        bool playerIsGuarding = combatState != null && combatState.IsGuarding();
        bool playerIsStunned = combatState != null && (combatState.IsStaggered() || combatState.IsInHitState());
        bool playerIsDowned = playerHealth != null && playerHealth.IsDead;

        if (playerIsAttacking)
            _lastPlayerAttackAt = now;

        float memoryTime = Mathf.Max(0.1f, _s.PlayerDefenseResponseMemoryTime);
        _playerObservation = new BossPlayerCombatObservation
        {
            playerDistance = distance,
            playerAngle = angle,
            playerIsMovingAway = movingAway,
            playerIsApproaching = approaching,
            playerIsDodging = playerIsDodging,
            playerIsGuarding = playerIsGuarding,
            playerIsAttacking = playerIsAttacking,
            playerIsStunned = playerIsStunned,
            playerIsDowned = playerIsDowned,
            playerRecentlyParried = now - _lastParryResponseAt <= memoryTime,
            playerRecentlyDodged = now - _lastPerfectDodgeResponseAt <= memoryTime,
            playerRecentlyHitBoss = now - _lastPlayerHitBossAt <= memoryTime,
            timeSincePlayerLastAttack = ResolveElapsed(now, _lastPlayerAttackAt),
            timeSinceBossLastHitPlayer = ResolveElapsed(now, _lastBossHitPlayerAt)
        };

        _lastObservedPlayerTarget = playerTarget;
        _lastObservedPlayerPosition = playerPos;
        _lastPlayerObservationTime = now;
    }

    void ResetPlayerCombatObservationState()
    {
        _playerObservation = BossPlayerCombatObservation.CreateInvalid();
        _effectivePlayerObservation = BossPlayerCombatObservation.CreateInvalid();
        _pendingPlayerReactionObservation = BossPlayerCombatObservation.CreateInvalid();
        _hasPendingPlayerReactionObservation = false;
        _pendingPlayerReactionReadyAt = float.NegativeInfinity;
    }

    void ApplyPlayerReactionDelay(BossPlayerCombatObservation rawObservation, float now)
    {
        float reactionDelay = Mathf.Max(0f, _s.ObservationReactionDelay);
        if (reactionDelay <= 0f || float.IsInfinity(rawObservation.playerDistance))
        {
            _effectivePlayerObservation = rawObservation;
            _playerObservation = rawObservation;
            _hasPendingPlayerReactionObservation = false;
            _pendingPlayerReactionReadyAt = float.NegativeInfinity;
            return;
        }

        BossPlayerCombatObservation effective = _effectivePlayerObservation;
        if (float.IsInfinity(effective.playerDistance))
            effective = rawObservation;

        // Distance and movement pressure stay current. Reactive tells are delayed to avoid perfect-read AI.
        effective.playerDistance = rawObservation.playerDistance;
        effective.playerAngle = rawObservation.playerAngle;
        effective.playerIsMovingAway = rawObservation.playerIsMovingAway;
        effective.playerIsApproaching = rawObservation.playerIsApproaching;
        effective.playerIsStunned = rawObservation.playerIsStunned;
        effective.playerIsDowned = rawObservation.playerIsDowned;
        effective.timeSinceBossLastHitPlayer = rawObservation.timeSinceBossLastHitPlayer;

        if (HasReactiveObservationChanged(rawObservation, effective))
        {
            if (!_hasPendingPlayerReactionObservation)
            {
                _pendingPlayerReactionObservation = rawObservation;
                _pendingPlayerReactionReadyAt = now + reactionDelay;
                _hasPendingPlayerReactionObservation = true;
            }
            else
            {
                MergePendingReactiveObservation(rawObservation);
            }
        }
        else
        {
            _hasPendingPlayerReactionObservation = false;
            _pendingPlayerReactionReadyAt = float.NegativeInfinity;
        }

        if (_hasPendingPlayerReactionObservation && now >= _pendingPlayerReactionReadyAt)
        {
            CopyReactiveObservation(ref effective, _pendingPlayerReactionObservation);
            _hasPendingPlayerReactionObservation = false;
            _pendingPlayerReactionReadyAt = float.NegativeInfinity;
        }

        _effectivePlayerObservation = effective;
        _playerObservation = effective;
    }

    static bool HasReactiveObservationChanged(BossPlayerCombatObservation source, BossPlayerCombatObservation target)
    {
        return source.playerIsDodging != target.playerIsDodging ||
               source.playerIsGuarding != target.playerIsGuarding ||
               source.playerIsAttacking != target.playerIsAttacking ||
               source.playerRecentlyParried != target.playerRecentlyParried ||
               source.playerRecentlyDodged != target.playerRecentlyDodged ||
               source.playerRecentlyHitBoss != target.playerRecentlyHitBoss;
    }

    static void CopyReactiveObservation(ref BossPlayerCombatObservation target, BossPlayerCombatObservation source)
    {
        target.playerIsDodging = source.playerIsDodging;
        target.playerIsGuarding = source.playerIsGuarding;
        target.playerIsAttacking = source.playerIsAttacking;
        target.playerRecentlyParried = source.playerRecentlyParried;
        target.playerRecentlyDodged = source.playerRecentlyDodged;
        target.playerRecentlyHitBoss = source.playerRecentlyHitBoss;
        target.timeSincePlayerLastAttack = source.timeSincePlayerLastAttack;
    }

    void MergePendingReactiveObservation(BossPlayerCombatObservation source)
    {
        _pendingPlayerReactionObservation.playerIsDodging |= source.playerIsDodging;
        _pendingPlayerReactionObservation.playerIsGuarding |= source.playerIsGuarding;
        _pendingPlayerReactionObservation.playerIsAttacking |= source.playerIsAttacking;
        _pendingPlayerReactionObservation.playerRecentlyParried |= source.playerRecentlyParried;
        _pendingPlayerReactionObservation.playerRecentlyDodged |= source.playerRecentlyDodged;
        _pendingPlayerReactionObservation.playerRecentlyHitBoss |= source.playerRecentlyHitBoss;
        if (source.timeSincePlayerLastAttack < _pendingPlayerReactionObservation.timeSincePlayerLastAttack)
            _pendingPlayerReactionObservation.timeSincePlayerLastAttack = source.timeSincePlayerLastAttack;
    }

    ICombatStateReader ResolvePlayerCombatStateReader(Transform target)
    {
        if (target == null)
            return null;

        if (_cachedPlayerCombatStateReader is Component cachedComponent &&
            cachedComponent != null &&
            cachedComponent.transform != null &&
            cachedComponent.transform.IsChildOf(target.root))
        {
            return _cachedPlayerCombatStateReader;
        }

        _cachedPlayerCombatStateReader = target.GetComponentInParent<ICombatStateReader>();
        if (_cachedPlayerCombatStateReader == null)
            _cachedPlayerCombatStateReader = target.GetComponentInChildren<ICombatStateReader>();

        return _cachedPlayerCombatStateReader;
    }

    PlayerHealth ResolvePlayerHealth(Transform target)
    {
        if (target == null)
            return null;

        if (_cachedPlayerHealth != null &&
            _cachedPlayerHealth.transform != null &&
            _cachedPlayerHealth.transform.IsChildOf(target.root))
        {
            return _cachedPlayerHealth;
        }

        _cachedPlayerHealth = target.GetComponentInParent<PlayerHealth>();
        if (_cachedPlayerHealth == null)
            _cachedPlayerHealth = target.GetComponentInChildren<PlayerHealth>();

        return _cachedPlayerHealth;
    }

    static float ResolveElapsed(float now, float timestamp)
    {
        return float.IsNegativeInfinity(timestamp) ? float.PositiveInfinity : Mathf.Max(0f, now - timestamp);
    }

    public void RecordPlayerHitBoss(float now)
    {
        _lastPlayerHitBossAt = now;
    }

    public void RecordBossHitPlayer(float now)
    {
        _lastBossHitPlayerAt = now;
    }

    void LogPlayerCombatObservation(float now, Object logContext)
    {
        if (!_s.EnablePlayerObservationDebugLog)
            return;

        if (now < _nextPlayerObservationDebugLogAt)
            return;

        _nextPlayerObservationDebugLogAt = now + Mathf.Max(0.1f, _s.PlayerObservationDebugInterval);
        GameLog.Info(
            "[BossPlayerObservation] phase=" + _s.ObservationPhase +
            " dist=" + _playerObservation.playerDistance.ToString("0.00") +
            " angle=" + _playerObservation.playerAngle.ToString("0") +
            " away=" + _playerObservation.playerIsMovingAway +
            " approach=" + _playerObservation.playerIsApproaching +
            " dodge=" + _playerObservation.playerIsDodging +
            " guard=" + _playerObservation.playerIsGuarding +
            " attack=" + _playerObservation.playerIsAttacking +
            " stun=" + _playerObservation.playerIsStunned +
            " down=" + _playerObservation.playerIsDowned +
            " recentParry=" + _playerObservation.playerRecentlyParried +
            " recentDodge=" + _playerObservation.playerRecentlyDodged +
            " recentHitBoss=" + _playerObservation.playerRecentlyHitBoss +
            " sincePlayerAttack=" + FormatObservationTime(_playerObservation.timeSincePlayerLastAttack) +
            " sinceBossHitPlayer=" + FormatObservationTime(_playerObservation.timeSinceBossLastHitPlayer),
            logContext);
    }

    public static string FormatObservationTime(float value)
    {
        return float.IsInfinity(value) ? "inf" : value.ToString("0.00");
    }

    public bool IsFarPressureActive()
    {
        return _s.UsePlayerStatePatternBias &&
               !float.IsNegativeInfinity(_farPressureStartedAt) &&
               Time.time - _farPressureStartedAt >= Mathf.Max(0f, _s.FarPressureDelay);
    }

    public bool IsCloseLingerActive()
    {
        return _s.UsePlayerStatePatternBias &&
               !float.IsNegativeInfinity(_closeLingerStartedAt) &&
               Time.time - _closeLingerStartedAt >= Mathf.Max(0f, _s.CloseLingerDelay);
    }

    public float ResolvePlayerStatePatternWeightMultiplier(AttackPattern pattern)
    {
        if (pattern == null)
            return 1f;

        BossPatternId patternId = pattern.selectorData.patternId != BossPatternId.None
            ? pattern.selectorData.patternId
            : _s.ResolvePatternId(pattern);

        float multiplier = ResolvePlayerStatePatternWeightMultiplier(patternId);
        AttackTelegraphType telegraphType = pattern.ResolveTelegraphType();

        if (IsRepeatedParryResponseActive())
        {
            if (pattern.ResolveCanParry() || telegraphType == AttackTelegraphType.Parry)
                multiplier *= _s.RepeatedParryParryableWeightMultiplier;
            else if (telegraphType == AttackTelegraphType.Dodge || telegraphType == AttackTelegraphType.Danger)
                multiplier *= _s.RepeatedParryDodgeOnlyWeightMultiplier;
        }

        if (IsRepeatedPerfectDodgeResponseActive())
        {
            if (telegraphType == AttackTelegraphType.Dodge)
                multiplier *= _s.RepeatedPerfectDodgeDodgeOnlyWeightMultiplier;
            else if (pattern.ResolveCanParry() || telegraphType == AttackTelegraphType.Parry || telegraphType == AttackTelegraphType.Guard)
                multiplier *= _s.RepeatedPerfectDodgeParryableWeightMultiplier;
        }

        if (_playerObservation.playerIsDodging || _playerObservation.playerRecentlyDodged)
        {
            AttackTimingStyle timingStyle = pattern.ResolveTimingStyle();
            if (timingStyle == AttackTimingStyle.Delayed || timingStyle == AttackTimingStyle.FakeOut || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, _s.PlayerDodgingDelayedAttackWeightMultiplier);
            else if (patternId == BossPatternId.QuickSlash)
                multiplier *= 0.88f;
        }

        if (_playerObservation.playerIsGuarding)
        {
            if (patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash)
                multiplier *= Mathf.Max(1f, _s.PlayerGuardingPunishWeightMultiplier);
            else if (pattern.ResolveCanParry() && telegraphType == AttackTelegraphType.Parry)
                multiplier *= 0.90f;
        }

        if (_playerObservation.playerIsAttacking)
        {
            if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, _s.PlayerAttackingCounterWeightMultiplier);
        }

        if (_playerObservation.playerRecentlyHitBoss || _playerObservation.playerIsApproaching)
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, _s.PlayerRecentlyHitBossCounterWeightMultiplier);
            else if (patternId == BossPatternId.SwordWave && _playerObservation.playerDistance <= _s.CloseLingerDistance + 1.0f)
                multiplier *= 0.82f;
        }

        if (IsPlayerAtBackAngle())
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, _s.PlayerBackAngleCounterWeightMultiplier);
            else if (patternId == BossPatternId.SwordWave)
                multiplier *= 0.76f;
        }

        return Mathf.Max(0.01f, multiplier);
    }

    float ResolvePlayerStatePatternWeightMultiplier(BossPatternId patternId)
    {
        if (!_s.UsePlayerStatePatternBias)
            return 1f;

        float multiplier = 1f;

        if (IsFarPressureActive())
        {
            if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Max(1f, _s.FarPressureRangedWeightMultiplier);
            else if (patternId == BossPatternId.DashSlash || patternId == BossPatternId.QuickSlash)
                multiplier *= Mathf.Max(1f, _s.FarPressureChaseWeightMultiplier);
            else if (patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash)
                multiplier *= 0.82f;
        }

        if (IsCloseLingerActive())
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, _s.CloseLingerPunishWeightMultiplier);
            else if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Clamp(_s.CloseLingerRangedWeightMultiplier, 0.1f, 1f);
        }

        if (_playerObservation.playerIsStunned || _playerObservation.playerIsDowned)
        {
            if (patternId == BossPatternId.SwordWave)
                multiplier *= Mathf.Clamp(_s.PlayerStunnedRangedWeightMultiplier, 0.1f, 1f);
            else if (patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= 1.10f;
        }

        if (_playerObservation.timeSinceBossLastHitPlayer >= _s.BossMissedPressureDelay)
        {
            if (patternId == BossPatternId.SwordWave || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, _s.BossMissedPressureWeightMultiplier);
        }

        return Mathf.Max(0.01f, multiplier);
    }

    public float ResolvePlayerStateEngageSpeedMultiplier(BossPatternId patternId)
    {
        if (!IsFarPressureActive() && !_playerObservation.playerIsMovingAway)
            return 1f;

        return patternId == BossPatternId.QuickSlash || patternId == BossPatternId.DashSlash
            ? Mathf.Max(1f, _s.FarPressureEngageSpeedMultiplier)
            : 1f;
    }

    public bool IsPlayerAtSideAngle()
    {
        return _playerObservation.playerAngle >= Mathf.Clamp(_s.PlayerSideAngleThreshold, 0f, 180f);
    }

    public bool IsPlayerAtBackAngle()
    {
        return _playerObservation.playerAngle >= Mathf.Clamp(_s.PlayerBackAngleThreshold, 0f, 180f);
    }

    bool IsRepeatedParryResponseActive()
    {
        return _s.UsePlayerStatePatternBias &&
               _recentParryResponseCount >= _s.RepeatedParryResponseThreshold &&
               Time.time - _lastParryResponseAt <= _s.PlayerDefenseResponseMemoryTime;
    }

    bool IsRepeatedPerfectDodgeResponseActive()
    {
        return _s.UsePlayerStatePatternBias &&
               _recentPerfectDodgeResponseCount >= _s.RepeatedPerfectDodgeResponseThreshold &&
               Time.time - _lastPerfectDodgeResponseAt <= _s.PlayerDefenseResponseMemoryTime;
    }

    public void RecordPlayerDefenseResponse(bool parry)
    {
        if (!_s.UsePlayerStatePatternBias)
            return;

        float now = Time.time;
        float memoryTime = Mathf.Max(0.1f, _s.PlayerDefenseResponseMemoryTime);

        if (parry)
        {
            if (now - _lastParryResponseAt > memoryTime)
                _recentParryResponseCount = 0;

            _recentParryResponseCount = Mathf.Min(
                Mathf.Max(1, _s.RepeatedParryResponseThreshold),
                _recentParryResponseCount + 1);
            _lastParryResponseAt = now;
        }
        else
        {
            if (now - _lastPerfectDodgeResponseAt > memoryTime)
                _recentPerfectDodgeResponseCount = 0;

            _recentPerfectDodgeResponseCount = Mathf.Min(
                Mathf.Max(1, _s.RepeatedPerfectDodgeResponseThreshold),
                _recentPerfectDodgeResponseCount + 1);
            _lastPerfectDodgeResponseAt = now;
        }
    }
}
