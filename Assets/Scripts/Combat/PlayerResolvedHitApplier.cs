using UnityEngine;

public static class PlayerResolvedHitApplier
{
    public static void Apply(
        ResolvedHitResult resolvedHit,
        HitPayload payload,
        PlayerHealth health,
        PlayerGuardController guard,
        ParryFeedbackController feedback,
        Animator anim,
        bool hasHitTrigger,
        string hitTriggerParam,
        bool suppressHitAnimWhenGuarding,
        bool debugLogs,
        UnityEngine.Object logContext)
    {
        if (resolvedHit.IsParry)
        {
            ApplyParry(
                payload,
                guard,
                feedback,
                debugLogs,
                logContext);
            return;
        }

        if (resolvedHit.IsGuardBlock)
        {
            ApplyGuardBlock(
                resolvedHit,
                payload,
                health,
                guard,
                feedback,
                debugLogs,
                logContext);
            return;
        }

        ApplyDamage(
            resolvedHit,
            payload,
            health,
            anim,
            hasHitTrigger,
            hitTriggerParam,
            suppressHitAnimWhenGuarding);
    }

    static void ApplyParry(
        HitPayload payload,
        PlayerGuardController guard,
        ParryFeedbackController feedback,
        bool debugLogs,
        UnityEngine.Object logContext)
    {
        if (guard != null)
        {
            guard.RegisterParrySuccess();
            guard.CloseParryWindow();
            guard.PlayParrySuccess();
        }

        if (feedback != null)
            feedback.PlayParryFeedback(payload.hitPoint, payload.attacker);

        CombatRewardUtility.TryNotifyParryBreak(payload.attacker, guard != null ? guard.gameObject : null);

        if (TutorialManager.Instance != null)
            TutorialManager.Instance.OnPlayerParrySuccess();

        if (debugLogs)
            Debug.Log("[PlayerDamageReceiver] Parry Success", logContext);
    }

    static void ApplyGuardBlock(
        ResolvedHitResult resolvedHit,
        HitPayload payload,
        PlayerHealth health,
        PlayerGuardController guard,
        ParryFeedbackController feedback,
        bool debugLogs,
        UnityEngine.Object logContext)
    {
        bool guardBroken = false;
        if (guard != null)
        {
            guardBroken = payload.causesGuardBreak
                ? guard.ForceGuardBreakFromAttack()
                : guard.ApplyGuardStrainFromBlock(payload.damage);
        }

        float chip = resolvedHit.ChipDamage;
        if (chip > 0f && health != null)
            health.ApplyChipDamage(chip);

        if (!guardBroken && guard != null)
            guard.PlayBlockReaction();

        if (!guardBroken && feedback != null)
            feedback.PlayGuardBlockFeedback(payload.hitPoint, payload.attacker);

        if (!guardBroken && TutorialManager.Instance != null)
            TutorialManager.Instance.OnPlayerGuardSuccess();

        if (debugLogs)
            Debug.Log($"[PlayerDamageReceiver] Guard Block, chip={chip}, break={guardBroken}", logContext);
    }

    static void ApplyDamage(
        ResolvedHitResult resolvedHit,
        HitPayload payload,
        PlayerHealth health,
        Animator anim,
        bool hasHitTrigger,
        string hitTriggerParam,
        bool suppressHitAnimWhenGuarding)
    {
        if (health != null)
        {
            health.TakeDamage(
                Mathf.RoundToInt(resolvedHit.HealthDamage),
                resolvedHit.HitType,
                payload.hitPoint);
        }

        bool shouldSuppressHitAnim = suppressHitAnimWhenGuarding && resolvedHit.ShouldSuppressHitAnimation;
        if (anim != null && hasHitTrigger && !shouldSuppressHitAnim)
        {
            anim.ResetTrigger(hitTriggerParam);
            anim.SetTrigger(hitTriggerParam);
        }
    }
}
