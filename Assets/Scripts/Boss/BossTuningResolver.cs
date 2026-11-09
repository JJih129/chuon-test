// 파일명: BossTuningResolver.cs
// 보스 난이도/페이즈 튜닝 수치 계산 전용 (씬 참조 없음, 순수 계산 위주).
// 직렬화 데이터는 BossController에 그대로 두고, IBossTuningContext로 현재 값을 읽어온다.

using UnityEngine;

public interface IBossTuningContext
{
    BossDifficultyTier DifficultyTier { get; }
    BossDifficultyProfile[] DifficultyProfiles { get; }
    float PhaseTwoThresholdNormalized { get; }
    float PhaseThreeThresholdNormalized { get; }
    float PhaseTwoTelegraphScale { get; }
    float PhaseThreeTelegraphScale { get; }
    float PhaseTwoFollowUpChanceBonus { get; }
    float PhaseThreeFollowUpChanceBonus { get; }
    int CurrentPhase { get; }
    float PhaseEntryPressureUntilTime { get; }
    float PhaseEntryUnlockedPatternWeight { get; }
    bool UseDefaultPhaseModifierFallback { get; }
    float ParryWindowEaseLeadTime { get; }
    float ParryWindowEaseTailTime { get; }
    BossPatternId ResolvePatternId(AttackPattern pattern);
}

public sealed class BossTuningResolver
{
    readonly IBossTuningContext _context;

    public BossTuningResolver(IBossTuningContext context)
    {
        _context = context;
    }

    // ---------------- 난이도 ----------------

    public BossDifficultyProfile ResolveDifficultyProfile()
    {
        BossDifficultyProfile[] profiles = _context.DifficultyProfiles;
        BossDifficultyTier tier = _context.DifficultyTier;
        if (profiles != null)
        {
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i].tier == tier)
                    return profiles[i];
            }
        }

        return BossDifficultyProfile.CreateDefault(tier);
    }

    public static float ResolveDifficultyMultiplier(float value, float fallback, float min, float max)
    {
        float resolved = value > 0.001f ? value : fallback;
        return Mathf.Clamp(resolved, min, max);
    }

    BossDifficultyProfile ResolveDefaults()
    {
        return BossDifficultyProfile.CreateDefault(_context.DifficultyTier);
    }

    public float ResolveDifficultyDamageMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.damageMultiplier, defaults.damageMultiplier, 0.01f, 5f);
    }

    public float ResolveDifficultyCooldownMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.cooldownMultiplier, defaults.cooldownMultiplier, 0.05f, 5f);
    }

    public float ResolveDifficultyTelegraphDurationMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.telegraphDurationMultiplier, defaults.telegraphDurationMultiplier, 0.25f, 3f);
    }

    public float ResolveDifficultyHitboxActiveDurationMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.hitboxActiveDurationMultiplier, defaults.hitboxActiveDurationMultiplier, 0.25f, 2f);
    }

    public float ResolveDifficultyParryWindowMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.parryWindowMultiplier, defaults.parryWindowMultiplier, 0.25f, 3f);
    }

    public float ResolveDifficultyPatternWeightMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.patternWeightMultiplier, defaults.patternWeightMultiplier, 0.05f, 5f);
    }

    public float ResolveDifficultyEngageSpeedMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.engageSpeedMultiplier, defaults.engageSpeedMultiplier, 0.15f, 3f);
    }

    public float ResolveDifficultyPostActionDurationMultiplier()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        return ResolveDifficultyMultiplier(profile.postActionDurationMultiplier, defaults.postActionDurationMultiplier, 0.15f, 3f);
    }

    public float ResolveDifficultyReactionDelay()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        float fallback = Mathf.Max(0f, defaults.reactionDelay);
        float configured = profile.reactionDelay >= 0f ? profile.reactionDelay : fallback;
        return Mathf.Clamp(configured, 0f, 1f);
    }

    public float ResolveDifficultyPhaseTwoThreshold()
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        BossDifficultyProfile defaults = ResolveDefaults();
        float fallback = defaults.phaseTransitionHpThreshold > 0.001f
            ? defaults.phaseTransitionHpThreshold
            : _context.PhaseTwoThresholdNormalized;
        float threshold = profile.phaseTransitionHpThreshold > 0.001f
            ? profile.phaseTransitionHpThreshold
            : fallback;
        return Mathf.Clamp(threshold, 0.05f, 0.95f);
    }

    public float ResolveDifficultyPhaseThreeThreshold(float phaseTwoThreshold)
    {
        float basePhaseTwo = Mathf.Max(0.001f, _context.PhaseTwoThresholdNormalized);
        float baseRatio = Mathf.Clamp01(_context.PhaseThreeThresholdNormalized / basePhaseTwo);
        float threshold = phaseTwoThreshold * baseRatio;
        return Mathf.Clamp(threshold, 0.01f, Mathf.Max(0.01f, phaseTwoThreshold - 0.05f));
    }

    public int ResolveDifficultyMaxFollowUpChains(int activeMax)
    {
        BossDifficultyProfile profile = ResolveDifficultyProfile();
        if (!profile.overrideMaxFollowUpCount)
            return Mathf.Max(0, activeMax);

        return Mathf.Max(0, profile.maxFollowUpCount);
    }

    public BossPostActionSettings ResolveDifficultyPostActionSettings(BossPostActionSettings settings)
    {
        float multiplier = ResolveDifficultyPostActionDurationMultiplier();
        settings.combatIdleMinTime = Mathf.Max(0f, settings.combatIdleMinTime * multiplier);
        settings.combatIdleMaxTime = Mathf.Max(settings.combatIdleMinTime, settings.combatIdleMaxTime * multiplier);
        settings.backstepDuration = Mathf.Max(0.01f, settings.backstepDuration * multiplier);
        settings.strafeDuration = Mathf.Max(0.01f, settings.strafeDuration * multiplier);
        settings.chaseMaxDuration = Mathf.Max(0.05f, settings.chaseMaxDuration * multiplier);
        settings.recenterDuration = Mathf.Max(0.01f, settings.recenterDuration * multiplier);
        return settings;
    }

    // ---------------- 페이즈 ----------------

    public int ResolvePhaseFromHp(float hpNormalized)
    {
        float phaseTwoThreshold = ResolveDifficultyPhaseTwoThreshold();
        float phaseThreeThreshold = ResolveDifficultyPhaseThreeThreshold(phaseTwoThreshold);

        if (hpNormalized <= phaseThreeThreshold)
            return 3;

        if (hpNormalized <= phaseTwoThreshold)
            return 2;

        return 1;
    }

    public float ResolvePhaseTelegraphScale()
    {
        switch (_context.CurrentPhase)
        {
            case 2:
                return Mathf.Clamp(_context.PhaseTwoTelegraphScale, 0.6f, 1.1f);
            case 3:
                return Mathf.Clamp(_context.PhaseThreeTelegraphScale, 0.5f, 1.05f);
            default:
                return 1f;
        }
    }

    public float ResolvePhaseFollowUpChanceBonus()
    {
        switch (_context.CurrentPhase)
        {
            case 2:
                return Mathf.Max(0f, _context.PhaseTwoFollowUpChanceBonus);
            case 3:
                return Mathf.Max(0f, _context.PhaseThreeFollowUpChanceBonus);
            default:
                return 0f;
        }
    }

    /// <summary>
    /// 페이즈/페이즈 모디파이어 기반 가중치만 계산한다.
    /// 플레이어 상태/패턴 다양성 배율은 호출자(BossController)가 이어서 곱한다.
    /// </summary>
    public float ResolvePhasePatternWeightMultiplier(AttackPattern pattern, bool isFollowUp)
    {
        if (pattern == null)
            return 1f;

        int currentPhase = _context.CurrentPhase;
        AttackTelegraphType type = pattern.ResolveTelegraphType();
        float multiplier = 1f;

        switch (currentPhase)
        {
            case 2:
                switch (type)
                {
                    case AttackTelegraphType.Dodge:
                        multiplier = 1.32f;
                        break;
                    case AttackTelegraphType.Guard:
                        multiplier = 1.10f;
                        break;
                    case AttackTelegraphType.Parry:
                        multiplier = 0.96f;
                        break;
                    case AttackTelegraphType.Danger:
                        multiplier = 0.95f;
                        break;
                }
                break;

            case 3:
                switch (type)
                {
                    case AttackTelegraphType.Danger:
                        multiplier = 1.34f;
                        break;
                    case AttackTelegraphType.Dodge:
                        multiplier = 1.18f;
                        break;
                    case AttackTelegraphType.Guard:
                        multiplier = 1.08f;
                        break;
                    case AttackTelegraphType.Parry:
                        multiplier = 0.84f;
                        break;
                }
                break;
        }

        if (isFollowUp && currentPhase >= 2 && (type == AttackTelegraphType.Dodge || type == AttackTelegraphType.Danger))
            multiplier *= 1.10f;

        if (Time.time < _context.PhaseEntryPressureUntilTime && pattern.ResolveMinPhase() == currentPhase)
            multiplier *= Mathf.Max(1f, _context.PhaseEntryUnlockedPatternWeight);

        if (TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier phaseModifier))
            multiplier *= BossPhasePatternModifier.ResolveMultiplier(phaseModifier.baseWeightMultiplier);

        return multiplier;
    }

    public bool TryResolvePhasePatternModifier(AttackPattern pattern, out BossPhasePatternModifier modifier)
    {
        return TryResolvePhasePatternModifier(pattern, _context.CurrentPhase, out modifier);
    }

    public bool TryResolvePhasePatternModifier(AttackPattern pattern, int phase, out BossPhasePatternModifier modifier)
    {
        modifier = default;
        if (pattern == null)
            return false;

        BossPatternId patternId = pattern.selectorData.patternId != BossPatternId.None
            ? pattern.selectorData.patternId
            : _context.ResolvePatternId(pattern);

        if (pattern.phaseModifiers != null)
        {
            for (int i = 0; i < pattern.phaseModifiers.Length; i++)
            {
                BossPhasePatternModifier candidate = pattern.phaseModifiers[i];
                if (candidate.phase != phase)
                    continue;

                if (candidate.patternId != BossPatternId.None && candidate.patternId != patternId)
                    continue;

                modifier = candidate;
                return true;
            }
        }

        if (!_context.UseDefaultPhaseModifierFallback || phase <= 1 || patternId == BossPatternId.None)
            return false;

        modifier = BossController.CreateDefaultPhaseModifier(patternId, phase);
        return true;
    }

    public float ResolvePhaseTelegraphDurationMultiplier(AttackPattern pattern)
    {
        return TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier)
            ? BossPhasePatternModifier.ResolveMultiplier(modifier.telegraphDurationMultiplier)
            : 1f;
    }

    public float ResolvePhaseRecoveryDurationMultiplier(AttackPattern pattern)
    {
        return TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier)
            ? BossPhasePatternModifier.ResolveMultiplier(modifier.recoveryDurationMultiplier)
            : 1f;
    }

    public float ResolvePhaseDamageMultiplier(AttackPattern pattern)
    {
        return TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier)
            ? BossPhasePatternModifier.ResolveMultiplier(modifier.damageMultiplier)
            : 1f;
    }

    // ---------------- 공격 타이밍 ----------------

    public void ApplyPhaseAttackTimingModifiers(AttackPattern pattern, ref BossAttackTimingData timingData)
    {
        if (!TryResolvePhasePatternModifier(pattern, out BossPhasePatternModifier modifier))
            return;

        float telegraphMultiplier = BossPhasePatternModifier.ResolveMultiplier(modifier.telegraphDurationMultiplier);
        float recoveryMultiplier = BossPhasePatternModifier.ResolveMultiplier(modifier.recoveryDurationMultiplier);
        timingData.telegraphDuration *= telegraphMultiplier;
        if (timingData.useTimingDataHitboxControl)
        {
            ScaleTimingPointFromStart(ref timingData.hitboxOpenTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.hitboxCloseTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowStartTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowEndTime, timingData.telegraphStartTime, telegraphMultiplier);
        }

        timingData.recoveryDuration *= recoveryMultiplier;
        timingData.Normalize();
    }

    public void ApplyDifficultyAttackTimingModifiers(ref BossAttackTimingData timingData)
    {
        float telegraphMultiplier = ResolveDifficultyTelegraphDurationMultiplier();
        float hitboxDurationMultiplier = ResolveDifficultyHitboxActiveDurationMultiplier();
        float parryWindowMultiplier = ResolveDifficultyParryWindowMultiplier();

        timingData.telegraphDuration *= telegraphMultiplier;
        if (timingData.useTimingDataHitboxControl)
        {
            ScaleTimingPointFromStart(ref timingData.hitboxOpenTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.hitboxCloseTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowStartTime, timingData.telegraphStartTime, telegraphMultiplier);
            ScaleTimingPointFromStart(ref timingData.parryWindowEndTime, timingData.telegraphStartTime, telegraphMultiplier);

            float hitboxDuration = Mathf.Max(0f, timingData.hitboxCloseTime - timingData.hitboxOpenTime);
            if (hitboxDuration > 0.001f)
                timingData.hitboxCloseTime = timingData.hitboxOpenTime + (hitboxDuration * hitboxDurationMultiplier);
        }

        float parryDuration = Mathf.Max(0f, timingData.parryWindowEndTime - timingData.parryWindowStartTime);
        if (parryDuration > 0.001f)
            timingData.parryWindowEndTime = timingData.parryWindowStartTime + (parryDuration * parryWindowMultiplier);

        timingData.Normalize();
    }

    public void ApplyParryWindowEase(ref BossAttackTimingData timingData)
    {
        float parryDuration = Mathf.Max(0f, timingData.parryWindowEndTime - timingData.parryWindowStartTime);
        if (parryDuration <= 0.001f)
            return;

        timingData.parryWindowStartTime = Mathf.Max(0f, timingData.parryWindowStartTime - Mathf.Max(0f, _context.ParryWindowEaseLeadTime));
        timingData.parryWindowEndTime += Mathf.Max(0f, _context.ParryWindowEaseTailTime);
        timingData.Normalize();
    }

    public static void ScaleTimingPointFromStart(ref float time, float startTime, float multiplier)
    {
        if (time <= startTime)
            return;

        time = startTime + ((time - startTime) * multiplier);
    }
}
