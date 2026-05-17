using System;
using UnityEngine;

[Serializable]
public struct BossArenaAwarenessSettings
{
    public bool enabled;
    [Min(0f)] public float farDistance;
    [Range(0f, 180f)] public float rearAngleThreshold;
    [Min(0f)] public float centerReturnDistance;
    [Min(0f)] public float playerWallProbeDistance;
    [Range(0.05f, 1f)] public float wallBackstepWeightMultiplier;
    [Range(1f, 3f)] public float rearRecenterWeightMultiplier;
    [Range(1f, 3f)] public float farPressureWeightMultiplier;
    [Range(1f, 3f)] public float centerReturnWeightMultiplier;
    [Range(1f, 3f)] public float blockedPathFallbackWeightMultiplier;
    [Range(1f, 3f)] public float playerWallPressureWeightMultiplier;
    [Range(0f, 1f)] public float centerReturnDirectionBlend;

    public static BossArenaAwarenessSettings CreateDefault()
    {
        return new BossArenaAwarenessSettings
        {
            enabled = true,
            farDistance = 8f,
            rearAngleThreshold = 125f,
            centerReturnDistance = 7.5f,
            playerWallProbeDistance = 1.5f,
            wallBackstepWeightMultiplier = 0.35f,
            rearRecenterWeightMultiplier = 1.35f,
            farPressureWeightMultiplier = 1.35f,
            centerReturnWeightMultiplier = 1.35f,
            blockedPathFallbackWeightMultiplier = 1.25f,
            playerWallPressureWeightMultiplier = 1.15f,
            centerReturnDirectionBlend = 0.45f
        };
    }
}

public struct BossArenaAwarenessSnapshot
{
    public bool enabled;
    public bool playerTooFar;
    public bool playerBehindBoss;
    public bool bossNearWall;
    public bool bossInCorner;
    public bool playerNearWall;
    public bool bossFarFromCenter;
    public bool pathToPlayerBlocked;
    public float distanceToPlayer;
    public float angleToPlayer;
    public float bossDistanceFromCenter;
}

public sealed class BossArenaAwareness
{
    public BossArenaAwarenessSnapshot Current { get; private set; }

    public BossArenaAwarenessSnapshot Evaluate(
        BossArenaAwarenessSettings settings,
        float distanceToPlayer,
        float angleToPlayer,
        float bossDistanceFromCenter,
        bool backBlocked,
        bool leftBlocked,
        bool rightBlocked,
        bool playerNearWall,
        bool pathToPlayerBlocked)
    {
        if (!settings.enabled)
        {
            Current = default;
            return Current;
        }

        bool bossInCorner = backBlocked && leftBlocked && rightBlocked;
        Current = new BossArenaAwarenessSnapshot
        {
            enabled = true,
            playerTooFar = distanceToPlayer >= Mathf.Max(0f, settings.farDistance),
            playerBehindBoss = Mathf.Abs(angleToPlayer) >= Mathf.Clamp(settings.rearAngleThreshold, 0f, 180f),
            bossNearWall = backBlocked || leftBlocked || rightBlocked,
            bossInCorner = bossInCorner,
            playerNearWall = playerNearWall,
            bossFarFromCenter = bossDistanceFromCenter >= Mathf.Max(0f, settings.centerReturnDistance),
            pathToPlayerBlocked = pathToPlayerBlocked,
            distanceToPlayer = Mathf.Max(0f, distanceToPlayer),
            angleToPlayer = Mathf.Abs(angleToPlayer),
            bossDistanceFromCenter = Mathf.Max(0f, bossDistanceFromCenter)
        };

        return Current;
    }

    public BossPatternPostActionType ResolvePostAction(BossPatternPostActionType current)
    {
        BossArenaAwarenessSnapshot snapshot = Current;
        if (!snapshot.enabled)
            return current;

        if (snapshot.bossFarFromCenter || snapshot.bossInCorner)
            return BossPatternPostActionType.Recenter;

        if (snapshot.playerBehindBoss)
            return current == BossPatternPostActionType.Backstep
                ? BossPatternPostActionType.Backstep
                : BossPatternPostActionType.Recenter;

        if (snapshot.bossNearWall && current == BossPatternPostActionType.Backstep)
            return BossPatternPostActionType.Recenter;

        if (snapshot.playerTooFar || snapshot.pathToPlayerBlocked)
            return BossPatternPostActionType.ChaseReposition;

        return current;
    }

    public float ResolvePatternWeightMultiplier(BossArenaAwarenessSettings settings, BossPatternId patternId)
    {
        BossArenaAwarenessSnapshot snapshot = Current;
        if (!snapshot.enabled)
            return 1f;

        float multiplier = 1f;
        if (snapshot.bossNearWall && patternId == BossPatternId.BackstepSlash)
            multiplier *= Mathf.Clamp(settings.wallBackstepWeightMultiplier, 0.05f, 1f);

        if (snapshot.playerBehindBoss)
        {
            if (patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, settings.rearRecenterWeightMultiplier);
        }

        if (snapshot.playerTooFar)
        {
            if (patternId == BossPatternId.SwordWave || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, settings.farPressureWeightMultiplier);
        }

        if (snapshot.bossFarFromCenter)
        {
            if (patternId == BossPatternId.DashSlash || patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash)
                multiplier *= Mathf.Max(1f, settings.centerReturnWeightMultiplier);
        }

        if (snapshot.pathToPlayerBlocked)
        {
            if (patternId == BossPatternId.SwordWave || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, settings.blockedPathFallbackWeightMultiplier);
        }

        if (snapshot.playerNearWall)
        {
            if (patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash || patternId == BossPatternId.DashSlash)
                multiplier *= Mathf.Max(1f, settings.playerWallPressureWeightMultiplier);
        }

        return Mathf.Max(0.01f, multiplier);
    }
}
