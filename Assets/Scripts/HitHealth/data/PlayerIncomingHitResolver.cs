public static class PlayerIncomingHitResolver
{
    public static ResolvedHitResult Resolve(HitPayload payload, PlayerDefenseResolution defenseResolution)
    {
        if (defenseResolution.IsPerfectDodge)
            return ResolvedHitResult.PerfectDodge();

        if (defenseResolution.IsParry)
            return ResolvedHitResult.Parry();

        if (defenseResolution.IsGuardBlock)
        {
            return ResolvedHitResult.GuardBlock(
                defenseResolution.ChipDamage,
                defenseResolution.ShouldSuppressHitAnimation);
        }

        return ResolvedHitResult.Damage(
            payload.damage,
            payload.hitType,
            0f,
            defenseResolution.ShouldSuppressHitAnimation);
    }
}
