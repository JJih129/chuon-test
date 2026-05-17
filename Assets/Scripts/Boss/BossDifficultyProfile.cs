using System;
using UnityEngine;

public enum BossDifficultyTier
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
    Expert = 3
}

[Serializable]
public struct BossDifficultyProfile
{
    public BossDifficultyTier tier;
    [Min(0f)] public float damageMultiplier;
    [Min(0.01f)] public float cooldownMultiplier;
    [Min(0.01f)] public float telegraphDurationMultiplier;
    [Min(0.01f)] public float hitboxActiveDurationMultiplier;
    [Min(0f)] public float reactionDelay;
    [Min(0.01f)] public float patternWeightMultiplier;
    [Min(0.01f)] public float engageSpeedMultiplier;
    [Min(0.01f)] public float postActionDurationMultiplier;
    [Range(0f, 1f)] public float phaseTransitionHpThreshold;
    public bool overrideMaxFollowUpCount;
    [Min(0)] public int maxFollowUpCount;
    [Min(0.01f)] public float parryWindowMultiplier;

    public static BossDifficultyProfile CreateDefault(BossDifficultyTier tier)
    {
        switch (tier)
        {
            case BossDifficultyTier.Easy:
                return new BossDifficultyProfile
                {
                    tier = tier,
                    damageMultiplier = 0.80f,
                    cooldownMultiplier = 1.25f,
                    telegraphDurationMultiplier = 1.25f,
                    hitboxActiveDurationMultiplier = 0.92f,
                    reactionDelay = 0.28f,
                    patternWeightMultiplier = 0.95f,
                    engageSpeedMultiplier = 0.88f,
                    postActionDurationMultiplier = 1.18f,
                    phaseTransitionHpThreshold = 0.72f,
                    overrideMaxFollowUpCount = true,
                    maxFollowUpCount = 0,
                    parryWindowMultiplier = 1.25f
                };
            case BossDifficultyTier.Hard:
                return new BossDifficultyProfile
                {
                    tier = tier,
                    damageMultiplier = 1.10f,
                    cooldownMultiplier = 0.90f,
                    telegraphDurationMultiplier = 0.92f,
                    hitboxActiveDurationMultiplier = 1.00f,
                    reactionDelay = 0.12f,
                    patternWeightMultiplier = 1.08f,
                    engageSpeedMultiplier = 1.14f,
                    postActionDurationMultiplier = 0.86f,
                    phaseTransitionHpThreshold = 0.62f,
                    overrideMaxFollowUpCount = true,
                    maxFollowUpCount = 2,
                    parryWindowMultiplier = 0.92f
                };
            case BossDifficultyTier.Expert:
                return new BossDifficultyProfile
                {
                    tier = tier,
                    damageMultiplier = 1.20f,
                    cooldownMultiplier = 0.78f,
                    telegraphDurationMultiplier = 0.84f,
                    hitboxActiveDurationMultiplier = 1.00f,
                    reactionDelay = 0.08f,
                    patternWeightMultiplier = 1.15f,
                    engageSpeedMultiplier = 1.24f,
                    postActionDurationMultiplier = 0.76f,
                    phaseTransitionHpThreshold = 0.58f,
                    overrideMaxFollowUpCount = true,
                    maxFollowUpCount = 3,
                    parryWindowMultiplier = 0.86f
                };
            case BossDifficultyTier.Normal:
            default:
                return new BossDifficultyProfile
                {
                    tier = BossDifficultyTier.Normal,
                    damageMultiplier = 1f,
                    cooldownMultiplier = 1f,
                    telegraphDurationMultiplier = 1f,
                    hitboxActiveDurationMultiplier = 1f,
                    reactionDelay = 0.18f,
                    patternWeightMultiplier = 1f,
                    engageSpeedMultiplier = 1f,
                    postActionDurationMultiplier = 1f,
                    phaseTransitionHpThreshold = 0f,
                    overrideMaxFollowUpCount = false,
                    maxFollowUpCount = 1,
                    parryWindowMultiplier = 1f
                };
        }
    }

    public static BossDifficultyProfile[] CreateDefaultSet()
    {
        return new[]
        {
            CreateDefault(BossDifficultyTier.Easy),
            CreateDefault(BossDifficultyTier.Normal),
            CreateDefault(BossDifficultyTier.Hard),
            CreateDefault(BossDifficultyTier.Expert)
        };
    }
}
