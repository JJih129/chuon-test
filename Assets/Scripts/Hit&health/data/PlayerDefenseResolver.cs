using UnityEngine;

public enum PlayerDefenseResolutionType
{
    None = 0,
    PerfectDodge = 1,
    Parry = 2,
    GuardBlock = 3
}

public struct PlayerDefenseResolution
{
    public PlayerDefenseResolutionType Type { get; }
    public bool CanDefense { get; }
    public bool ShouldSuppressHitAnimation { get; }
    public float ChipDamage { get; }

    public bool IsPerfectDodge => Type == PlayerDefenseResolutionType.PerfectDodge;
    public bool IsParry => Type == PlayerDefenseResolutionType.Parry;
    public bool IsGuardBlock => Type == PlayerDefenseResolutionType.GuardBlock;

    public PlayerDefenseResolution(
        PlayerDefenseResolutionType type,
        bool canDefense,
        bool shouldSuppressHitAnimation,
        float chipDamage)
    {
        Type = type;
        CanDefense = canDefense;
        ShouldSuppressHitAnimation = shouldSuppressHitAnimation;
        ChipDamage = chipDamage;
    }
}

public static class PlayerDefenseResolver
{
    public static PlayerDefenseResolution Resolve(
        Transform defender,
        PlayerGuardController guard,
        PerfectDodgeController perfectDodge,
        Vector3 hitPoint,
        Transform attacker,
        float baseDamage,
        bool unblockable,
        bool allowPerfectDodge,
        bool allowParry,
        bool allowGuard,
        float frontArcDegrees,
        float chipDamageMultiplier)
    {
        if (allowPerfectDodge && perfectDodge != null && perfectDodge.ResolvePerfectDodge(hitPoint, attacker))
        {
            return new PlayerDefenseResolution(
                PlayerDefenseResolutionType.PerfectDodge,
                false,
                false,
                0f);
        }

        bool hasGuard = guard != null;
        bool isFront = hasGuard && IsFront(defender, hitPoint, attacker, frontArcDegrees);
        bool canParryDefense = hasGuard && isFront && !unblockable && allowParry;
        bool canBlockDefense = hasGuard && isFront && !unblockable && allowGuard;
        bool canDefense = canParryDefense || canBlockDefense;
        bool canResolveParry = canParryDefense && guard != null && guard.CanResolveParryDefense();
        bool canResolveBlock = canBlockDefense && guard != null && guard.CanResolveGuardBlock();
        bool shouldSuppressHitAnimation = canResolveBlock;

        if (canResolveParry)
        {
            return new PlayerDefenseResolution(
                PlayerDefenseResolutionType.Parry,
                true,
                shouldSuppressHitAnimation,
                0f);
        }

        if (canResolveBlock)
        {
            return new PlayerDefenseResolution(
                PlayerDefenseResolutionType.GuardBlock,
                true,
                shouldSuppressHitAnimation,
                Mathf.Max(0f, baseDamage) * Mathf.Clamp01(chipDamageMultiplier));
        }

        return new PlayerDefenseResolution(
            PlayerDefenseResolutionType.None,
            canDefense,
            shouldSuppressHitAnimation,
            0f);
    }

    static bool IsFront(Transform defender, Vector3 hitPoint, Transform attacker, float frontArcDegrees)
    {
        if (defender == null)
            return false;

        Vector3 defenseDirection = attacker != null
            ? attacker.position - defender.position
            : hitPoint - defender.position;

        defenseDirection.y = 0f;
        if (defenseDirection.sqrMagnitude < 0.0001f)
            return true;

        defenseDirection.Normalize();

        Vector3 forward = defender.forward;
        forward.y = 0f;
        forward.Normalize();

        float dot = Vector3.Dot(forward, defenseDirection);
        float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        return angle <= frontArcDegrees * 0.5f;
    }
}
