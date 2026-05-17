using UnityEngine;

public static class BossResolvedHitApplier
{
    public static void Apply(
        BossHealth bossHealth,
        BossBreakController bossBreakController,
        ResolvedHitResult resolvedHit,
        HitPayload payload,
        bool suppressBreak = false,
        bool suppressReward = false)
    {
        if (bossHealth == null || !resolvedHit.IsDamage)
            return;

        if (!suppressBreak && resolvedHit.BreakBonus > 0f && bossBreakController != null)
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
            if (!suppressBreak && bossBreakController != null && resolvedHit.HitType != HitType.Force)
                bossBreakController.AddBreak(0f, BossBreakController.BreakSource.Generic);

            if (!suppressReward)
                CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);
        }
    }
}
