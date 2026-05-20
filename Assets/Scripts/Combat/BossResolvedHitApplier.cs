using UnityEngine;

public static class BossResolvedHitApplier
{
    public static void Apply(
        BossHealth bossHealth,
        BossBreakController bossBreakController,
        ResolvedHitResult resolvedHit,
        HitPayload payload)
    {
        if (bossHealth == null || !resolvedHit.IsDamage)
            return;

        if (resolvedHit.BreakBonus > 0f && bossBreakController != null)
        {
            bossBreakController.AddBreak(
                resolvedHit.BreakBonus,
                BossBreakController.BreakSource.Parry);
        }

        int beforeHp = bossHealth.CurrentHP;
        bossHealth.TakeDamage(
            Mathf.RoundToInt(resolvedHit.HealthDamage),
            resolvedHit.HitType,
            payload.hitPoint);

        if (bossHealth.CurrentHP < beforeHp)
        {
            if (bossBreakController != null && resolvedHit.HitType != HitType.Force)
                bossBreakController.AddBreak(0f, BossBreakController.BreakSource.Generic);

            CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);
        }
    }
}
