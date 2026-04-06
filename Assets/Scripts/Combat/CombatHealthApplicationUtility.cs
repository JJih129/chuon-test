using UnityEngine;

public static class CombatHealthApplicationUtility
{
    public static void ApplyHitPayload(IHealth health, HitPayload payload)
    {
        if (health == null)
            return;

        health.TakeDamage(
            Mathf.RoundToInt(payload.damage),
            payload.hitType,
            payload.hitPoint);
    }
}
