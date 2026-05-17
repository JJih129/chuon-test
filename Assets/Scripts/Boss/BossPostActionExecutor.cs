using System;
using System.Collections;
using UnityEngine;

[Serializable]
public struct BossPostActionSettings
{
    public float combatIdleMinTime;
    public float combatIdleMaxTime;
    public float backstepDistance;
    public float backstepDuration;
    public float strafeDistance;
    public float strafeDuration;
    public float chaseDesiredDistance;
    public float chaseStopDistance;
    public float chaseMaxDuration;
    public float recenterDuration;
    public float recenterAngleTolerance;
    public float turnSpeed;
    public bool keepFacingTargetDuringPostAction;

    public static BossPostActionSettings CreateDefault()
    {
        return new BossPostActionSettings
        {
            combatIdleMinTime = 0.25f,
            combatIdleMaxTime = 0.55f,
            backstepDistance = 1.4f,
            backstepDuration = 0.35f,
            strafeDistance = 1.6f,
            strafeDuration = 0.55f,
            chaseDesiredDistance = 3.2f,
            chaseStopDistance = 2.6f,
            chaseMaxDuration = 0.8f,
            recenterDuration = 0.35f,
            recenterAngleTolerance = 5f,
            turnSpeed = 540f,
            keepFacingTargetDuringPostAction = true
        };
    }
}

public struct BossPostActionContext
{
    public Transform bossTransform;
    public Transform targetTransform;
    public float currentDistance;
    public float currentAngle;
    public float currentTime;
    public bool canMove;
    public bool targetValid;
}

public sealed class BossPostActionExecutor
{
    public delegate void MoveDeltaHandler(Vector3 delta);
    public delegate void MoveAnimationHandler(float moveBlend, Vector3 worldMoveDirection);
    public delegate bool CanContinueHandler();

    public IEnumerator Execute(
        BossPatternPostActionType actionType,
        BossPostActionContext context,
        BossPostActionSettings settings,
        MoveDeltaHandler moveDelta,
        MoveAnimationHandler moveAnimation,
        CanContinueHandler canContinue,
        bool enableDebugLog,
        UnityEngine.Object logContext)
    {
        if (actionType == BossPatternPostActionType.None)
            yield break;

        if (!IsContextValid(context, canContinue))
        {
            Log(enableDebugLog, logContext, actionType, "skip invalid context");
            yield break;
        }

        Log(enableDebugLog, logContext, actionType, "start");

        switch (actionType)
        {
            case BossPatternPostActionType.CombatIdle:
                yield return ExecuteCombatIdle(context, settings, moveAnimation, canContinue);
                break;
            case BossPatternPostActionType.Backstep:
                yield return ExecuteMove(context, settings, -context.bossTransform.forward, settings.backstepDistance, settings.backstepDuration, moveDelta, moveAnimation, canContinue);
                break;
            case BossPatternPostActionType.StrafeLeft:
                yield return ExecuteMove(context, settings, -context.bossTransform.right, settings.strafeDistance, settings.strafeDuration, moveDelta, moveAnimation, canContinue);
                break;
            case BossPatternPostActionType.StrafeRight:
                yield return ExecuteMove(context, settings, context.bossTransform.right, settings.strafeDistance, settings.strafeDuration, moveDelta, moveAnimation, canContinue);
                break;
            case BossPatternPostActionType.ChaseReposition:
                yield return ExecuteChaseReposition(context, settings, moveDelta, moveAnimation, canContinue);
                break;
            case BossPatternPostActionType.Recenter:
                yield return ExecuteRecenter(context, settings, moveAnimation, canContinue);
                break;
        }

        moveAnimation?.Invoke(0f, Vector3.zero);
        Log(enableDebugLog, logContext, actionType, "end");
    }

    private IEnumerator ExecuteCombatIdle(
        BossPostActionContext context,
        BossPostActionSettings settings,
        MoveAnimationHandler moveAnimation,
        CanContinueHandler canContinue)
    {
        float min = Mathf.Max(0f, settings.combatIdleMinTime);
        float max = Mathf.Max(min, settings.combatIdleMaxTime);
        float duration = UnityEngine.Random.Range(min, max);
        float elapsed = 0f;

        while (elapsed < duration && IsContextValid(context, canContinue))
        {
            FaceTargetIfNeeded(context, settings, Time.deltaTime);
            moveAnimation?.Invoke(0f, Vector3.zero);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator ExecuteMove(
        BossPostActionContext context,
        BossPostActionSettings settings,
        Vector3 direction,
        float distance,
        float duration,
        MoveDeltaHandler moveDelta,
        MoveAnimationHandler moveAnimation,
        CanContinueHandler canContinue)
    {
        if (!context.canMove || moveDelta == null)
            yield break;

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            yield break;

        direction.Normalize();
        distance = Mathf.Max(0f, distance);
        duration = Mathf.Max(0.01f, duration);
        float speed = distance / duration;
        float elapsed = 0f;
        float moved = 0f;
        float blockedTime = 0f;
        float blockedAbortTime = Mathf.Min(0.22f, duration * 0.35f);

        while (elapsed < duration && moved < distance && IsContextValid(context, canContinue))
        {
            FaceTargetIfNeeded(context, settings, Time.deltaTime);

            float step = Mathf.Min(speed * Time.deltaTime, distance - moved);
            if (step <= 0.0001f)
                break;

            Vector3 delta = direction * step;
            Vector3 before = context.bossTransform.position;
            moveDelta(delta);
            Vector3 actualDelta = context.bossTransform.position - before;
            actualDelta.y = 0f;
            float actualMove = actualDelta.magnitude;
            if (actualMove < step * 0.15f)
            {
                blockedTime += Time.deltaTime;
                if (blockedTime >= blockedAbortTime)
                    break;
            }
            else
            {
                blockedTime = 0f;
            }

            moveAnimation?.Invoke(0.45f, direction);

            moved += Mathf.Max(0f, actualMove);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator ExecuteChaseReposition(
        BossPostActionContext context,
        BossPostActionSettings settings,
        MoveDeltaHandler moveDelta,
        MoveAnimationHandler moveAnimation,
        CanContinueHandler canContinue)
    {
        if (!context.canMove || moveDelta == null)
            yield break;

        float stopDistance = Mathf.Max(0.1f, settings.chaseStopDistance);
        float desiredDistance = Mathf.Max(stopDistance, settings.chaseDesiredDistance);
        float maxDuration = Mathf.Max(0.05f, settings.chaseMaxDuration);
        float elapsed = 0f;
        float blockedTime = 0f;

        while (elapsed < maxDuration && IsContextValid(context, canContinue))
        {
            Vector3 toTarget = context.targetTransform.position - context.bossTransform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance <= stopDistance || distance <= desiredDistance * 0.8f || distance <= 0.001f)
                break;

            Vector3 direction = toTarget / distance;
            FaceTargetIfNeeded(context, settings, Time.deltaTime);
            Vector3 before = context.bossTransform.position;
            moveDelta(direction * (Mathf.Max(0.1f, desiredDistance) * Time.deltaTime));
            Vector3 afterToTarget = context.targetTransform.position - context.bossTransform.position;
            afterToTarget.y = 0f;
            float progress = distance - afterToTarget.magnitude;
            Vector3 actualDelta = context.bossTransform.position - before;
            actualDelta.y = 0f;
            if (progress <= 0.01f && actualDelta.sqrMagnitude <= 0.0004f)
            {
                blockedTime += Time.deltaTime;
                if (blockedTime >= Mathf.Min(0.22f, maxDuration * 0.35f))
                    break;
            }
            else
            {
                blockedTime = 0f;
            }

            moveAnimation?.Invoke(0.55f, direction);

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator ExecuteRecenter(
        BossPostActionContext context,
        BossPostActionSettings settings,
        MoveAnimationHandler moveAnimation,
        CanContinueHandler canContinue)
    {
        float duration = Mathf.Max(0.01f, settings.recenterDuration);
        float elapsed = 0f;

        while (elapsed < duration && IsContextValid(context, canContinue))
        {
            float angle = AbsHorizontalAngleToTarget(context);
            if (angle <= Mathf.Max(0.1f, settings.recenterAngleTolerance))
                break;

            FaceTarget(context, Mathf.Max(0f, settings.turnSpeed), Time.deltaTime);
            moveAnimation?.Invoke(0f, Vector3.zero);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private static bool IsContextValid(BossPostActionContext context, CanContinueHandler canContinue)
    {
        return context.bossTransform != null &&
               context.targetValid &&
               context.targetTransform != null &&
               (canContinue == null || canContinue());
    }

    private static void FaceTargetIfNeeded(BossPostActionContext context, BossPostActionSettings settings, float deltaTime)
    {
        if (!settings.keepFacingTargetDuringPostAction)
            return;

        FaceTarget(context, Mathf.Max(0f, settings.turnSpeed), deltaTime);
    }

    private static void FaceTarget(BossPostActionContext context, float turnSpeed, float deltaTime)
    {
        Vector3 toTarget = context.targetTransform.position - context.bossTransform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        context.bossTransform.rotation = Quaternion.RotateTowards(
            context.bossTransform.rotation,
            targetRotation,
            turnSpeed * deltaTime);
    }

    private static float AbsHorizontalAngleToTarget(BossPostActionContext context)
    {
        Vector3 toTarget = context.targetTransform.position - context.bossTransform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return 0f;

        Vector3 forward = context.bossTransform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            return 0f;

        return Mathf.Abs(Vector3.SignedAngle(forward.normalized, toTarget.normalized, Vector3.up));
    }

    private static void Log(bool enabled, UnityEngine.Object context, BossPatternPostActionType actionType, string message)
    {
        if (!enabled)
            return;

        Debug.Log($"[BossPostAction] {actionType}: {message}", context);
    }
}
