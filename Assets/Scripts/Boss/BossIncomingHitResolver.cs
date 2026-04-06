using UnityEngine;

public static class BossIncomingHitResolver
{
    public static ResolvedHitResult Resolve(
        HitPayload payload,
        bool overrideHitType,
        HitType overrideType,
        bool consumeParryCounterBonus,
        bool grantBreakBonusOnParryCounter,
        bool applyPunishWindowBonus,
        BossController bossController)
    {
        HitType resolvedHitType = overrideHitType ? overrideType : payload.hitType;
        float resolvedDamage = payload.damage;
        float resolvedBreakBonus = 0f;

        if (consumeParryCounterBonus)
        {
            PlayerGuardController attackerGuard = ResolveAttackerGuard(payload.attacker);
            if (attackerGuard != null &&
                attackerGuard.TryConsumeParryCounter(out float damageMultiplier, out HitType counterHitType, out float breakBonus))
            {
                resolvedDamage *= damageMultiplier;
                if (!overrideHitType)
                    resolvedHitType = counterHitType;

                if (grantBreakBonusOnParryCounter)
                    resolvedBreakBonus = Mathf.Max(0f, breakBonus);
            }
        }

        if (applyPunishWindowBonus && bossController != null && bossController.IsPunishWindowActive)
            resolvedDamage *= bossController.CurrentPunishDamageMultiplier;

        return ResolvedHitResult.Damage(
            resolvedDamage,
            resolvedHitType,
            resolvedBreakBonus);
    }

    static PlayerGuardController ResolveAttackerGuard(Transform attacker)
    {
        if (attacker == null)
            return null;

        return attacker.GetComponentInParent<PlayerGuardController>();
    }
}
