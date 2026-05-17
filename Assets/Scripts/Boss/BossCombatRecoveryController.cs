using System;
using System.Collections;
using UnityEngine;

public enum BossRecoveryReason
{
    Generic = 0,
    ParryStun = 1,
    Break = 2,
    Stagger = 3,
    Knockback = 4,
    UltimateVictim = 5,
    Down = 6,
    PhaseTransition = 7,
    TargetReacquire = 8,
    CutsceneAttack = 9
}

[Serializable]
public struct BossCombatRecoveryTelemetrySample
{
    public BossRecoveryReason reason;
    public int phase;
    public float bossHpNormalized;
    public float duration;
    public bool completed;
    public bool aborted;
    public bool targetValid;
    public float time;
}

[Serializable]
public struct BossCombatRecoverySettings
{
    [Min(0f)] public float recoveryDuration;
    [Min(0f)] public float invulnerableDuration;
    [Min(0f)] public float superArmorDuration;
    [Min(0f)] public float lockoutDuration;
    [Min(0f)] public float recenterDuration;
    [Min(1f)] public float recenterTurnSpeed;
    public bool returnToCombatAnchor;
    [Min(0f)] public float maxAnchorDistance;
    [Min(0f)] public float anchorReturnSpeed;
    [Min(0f)] public float postRecoveryCombatIdleDuration;
    public bool keepDamageRewardDuringSuperArmor;

    public static BossCombatRecoverySettings CreateDefault()
    {
        return new BossCombatRecoverySettings
        {
            recoveryDuration = 0.35f,
            invulnerableDuration = 0.18f,
            superArmorDuration = 0.55f,
            lockoutDuration = 0.25f,
            recenterDuration = 0.30f,
            recenterTurnSpeed = 540f,
            returnToCombatAnchor = true,
            maxAnchorDistance = 7.5f,
            anchorReturnSpeed = 3.5f,
            postRecoveryCombatIdleDuration = 0.25f,
            keepDamageRewardDuringSuperArmor = true
        };
    }
}

public sealed class BossCombatRecoveryController
{
    public delegate bool ShouldAbortHandler();
    public delegate void CleanupHandler();
    public delegate void TimedStateHandler(float duration);
    public delegate void MoveDeltaHandler(Vector3 delta);
    public delegate void MoveAnimationHandler(float moveBlend, Vector3 worldMoveDirection, bool useDirectionalBlend);

    float _superArmorUntil = float.NegativeInfinity;

    public bool IsRunning { get; private set; }

    public bool IsSuperArmorActive(float time)
    {
        return IsRunning && time < _superArmorUntil;
    }

    public void Cancel()
    {
        IsRunning = false;
        _superArmorUntil = float.NegativeInfinity;
    }

    public IEnumerator Execute(
        BossRecoveryReason reason,
        BossCombatRecoverySettings settings,
        Transform bossTransform,
        Transform targetTransform,
        Vector3 combatAnchor,
        ShouldAbortHandler shouldAbort,
        CleanupHandler cleanup,
        TimedStateHandler grantInvulnerable,
        TimedStateHandler grantSuperArmor,
        MoveDeltaHandler moveDelta,
        MoveAnimationHandler moveAnimation)
    {
        IsRunning = true;
        cleanup?.Invoke();
        settings = ResolveReasonSettings(reason, settings);

        float now = Time.time;
        float invulnerableDuration = Mathf.Max(0f, settings.invulnerableDuration);
        float superArmorDuration = Mathf.Max(0f, settings.superArmorDuration);
        if (invulnerableDuration > 0f)
            grantInvulnerable?.Invoke(invulnerableDuration);
        if (superArmorDuration > 0f)
        {
            _superArmorUntil = now + superArmorDuration;
            grantSuperArmor?.Invoke(superArmorDuration);
        }
        else
        {
            _superArmorUntil = float.NegativeInfinity;
        }

        float duration = Mathf.Max(
            Mathf.Max(0f, settings.recoveryDuration),
            Mathf.Max(0f, settings.recenterDuration));
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (shouldAbort != null && shouldAbort())
                break;

            float deltaTime = Time.deltaTime;
            FaceTarget(bossTransform, targetTransform, settings.recenterTurnSpeed, deltaTime);

            Vector3 moveDirection = ResolveAnchorReturnDirection(settings, bossTransform, combatAnchor);
            if (moveDirection.sqrMagnitude > 0.0001f && moveDelta != null)
            {
                float speed = Mathf.Max(0f, settings.anchorReturnSpeed);
                moveDelta(moveDirection * (speed * deltaTime));
                moveAnimation?.Invoke(Mathf.Clamp01(speed / 6f), moveDirection, false);
            }
            else
            {
                moveAnimation?.Invoke(0f, Vector3.zero, false);
            }

            elapsed += deltaTime;
            yield return null;
        }

        moveAnimation?.Invoke(0f, Vector3.zero, false);
        IsRunning = false;
        _superArmorUntil = float.NegativeInfinity;
        grantSuperArmor?.Invoke(0f);
    }

    static void FaceTarget(Transform bossTransform, Transform targetTransform, float turnSpeed, float deltaTime)
    {
        if (bossTransform == null || targetTransform == null)
            return;

        Vector3 toTarget = targetTransform.position - bossTransform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        bossTransform.rotation = Quaternion.RotateTowards(
            bossTransform.rotation,
            targetRotation,
            Mathf.Max(1f, turnSpeed) * deltaTime);
    }

    static Vector3 ResolveAnchorReturnDirection(
        BossCombatRecoverySettings settings,
        Transform bossTransform,
        Vector3 combatAnchor)
    {
        if (!settings.returnToCombatAnchor || bossTransform == null)
            return Vector3.zero;

        Vector3 toAnchor = combatAnchor - bossTransform.position;
        toAnchor.y = 0f;
        float distance = toAnchor.magnitude;
        if (distance <= Mathf.Max(0f, settings.maxAnchorDistance) || distance <= 0.001f)
            return Vector3.zero;

        return toAnchor / distance;
    }

    static BossCombatRecoverySettings ResolveReasonSettings(
        BossRecoveryReason reason,
        BossCombatRecoverySettings settings)
    {
        switch (reason)
        {
            case BossRecoveryReason.ParryStun:
            case BossRecoveryReason.Stagger:
                settings.recoveryDuration *= 0.85f;
                settings.invulnerableDuration *= 0.75f;
                break;
            case BossRecoveryReason.Break:
            case BossRecoveryReason.UltimateVictim:
            case BossRecoveryReason.CutsceneAttack:
                settings.recoveryDuration *= 1.25f;
                settings.superArmorDuration *= 1.15f;
                break;
            case BossRecoveryReason.PhaseTransition:
                settings.recoveryDuration *= 1.10f;
                settings.invulnerableDuration *= 1.10f;
                break;
            case BossRecoveryReason.TargetReacquire:
                settings.invulnerableDuration = 0f;
                settings.superArmorDuration *= 0.5f;
                break;
        }

        return settings;
    }
}
