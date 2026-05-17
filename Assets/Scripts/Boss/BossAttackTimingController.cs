using System;
using System.Collections;
using UnityEngine;

public enum BossAttackTelegraphType
{
    None = 0,
    Normal = 1,
    Parryable = 2,
    DodgeOnly = 3,
    Unblockable = 4,
    Heavy = 5,
    Ranged = 6
}

[Flags]
public enum BossAttackTimingCueFlags
{
    None = 0,
    WeaponFlash = 1 << 0,
    BodyFlash = 1 << 1,
    WarningSound = 1 << 2,
    CameraShake = 1 << 3,
    ChargeSound = 1 << 4,
    SwingSound = 1 << 5,
    ImpactSound = 1 << 6,
    TimeScaleHitStop = 1 << 7,
    TrailEffect = 1 << 8,
    GroundWarning = 1 << 9,
    ProjectileWarning = 1 << 10,
    All = WeaponFlash | BodyFlash | WarningSound | CameraShake |
          ChargeSound | SwingSound | ImpactSound | TimeScaleHitStop |
          TrailEffect | GroundWarning | ProjectileWarning
}

[Serializable]
public struct BossAttackTimingData
{
    public BossPatternId patternId;
    public BossAttackTelegraphType telegraphType;
    [Min(0f)] public float telegraphStartTime;
    [Min(0f)] public float telegraphDuration;
    [Min(0f)] public float hitboxOpenTime;
    [Min(0f)] public float hitboxCloseTime;
    [Min(0f)] public float parryWindowStartTime;
    [Min(0f)] public float parryWindowEndTime;
    [Min(0f)] public float recoveryStartTime;
    [Min(0f)] public float recoveryDuration;
    public bool useWeaponFlash;
    public bool useBodyFlash;
    public bool useWarningSound;
    public bool useCameraShake;
    public bool useTimingDataHitboxControl;

    public bool HasAnyTiming =>
        telegraphDuration > 0.001f ||
        useTimingDataHitboxControl ||
        parryWindowEndTime > parryWindowStartTime + 0.001f ||
        recoveryDuration > 0.001f;

    public void Normalize()
    {
        telegraphStartTime = Mathf.Max(0f, telegraphStartTime);
        telegraphDuration = Mathf.Max(0f, telegraphDuration);
        hitboxOpenTime = Mathf.Max(0f, hitboxOpenTime);
        hitboxCloseTime = Mathf.Max(hitboxOpenTime, hitboxCloseTime);
        parryWindowStartTime = Mathf.Max(0f, parryWindowStartTime);
        parryWindowEndTime = Mathf.Max(parryWindowStartTime, parryWindowEndTime);
        recoveryStartTime = Mathf.Max(0f, recoveryStartTime);
        recoveryDuration = Mathf.Max(0f, recoveryDuration);
    }
}

public sealed class BossAttackTimingController
{
    public IEnumerator Execute(
        BossAttackTimingData data,
        Func<bool> canContinue,
        Action<BossAttackTelegraphType, float> onTelegraph,
        Action onHitboxOpen,
        Action onHitboxClose,
        Action onParryWindowOpen,
        Action onParryWindowClose,
        bool enableDebugLog,
        UnityEngine.Object logContext)
    {
        data.Normalize();

        float elapsed = 0f;
        bool telegraphStarted = false;
        bool hitboxOpened = false;
        bool hitboxClosed = !data.useTimingDataHitboxControl;
        bool parryOpened = false;
        bool parryClosed = false;
        float endTime = ResolveEndTime(data);

        try
        {
            while (elapsed <= endTime)
            {
                if (canContinue == null || !canContinue())
                    yield break;

                if (!telegraphStarted && data.telegraphDuration > 0.001f && elapsed >= data.telegraphStartTime)
                {
                    telegraphStarted = true;
                    onTelegraph?.Invoke(data.telegraphType, data.telegraphDuration);
                    Log(enableDebugLog, logContext, "telegraph", data.patternId);
                }

                if (data.useTimingDataHitboxControl && !hitboxOpened && elapsed >= data.hitboxOpenTime)
                {
                    hitboxOpened = true;
                    onHitboxOpen?.Invoke();
                    Log(enableDebugLog, logContext, "hitbox open", data.patternId);
                }

                if (data.useTimingDataHitboxControl && hitboxOpened && !hitboxClosed && elapsed >= data.hitboxCloseTime)
                {
                    hitboxClosed = true;
                    onHitboxClose?.Invoke();
                    Log(enableDebugLog, logContext, "hitbox close", data.patternId);
                }

                if (!parryOpened && data.parryWindowEndTime > data.parryWindowStartTime + 0.001f && elapsed >= data.parryWindowStartTime)
                {
                    parryOpened = true;
                    onParryWindowOpen?.Invoke();
                    Log(enableDebugLog, logContext, "parry open", data.patternId);
                }

                if (parryOpened && !parryClosed && elapsed >= data.parryWindowEndTime)
                {
                    parryClosed = true;
                    onParryWindowClose?.Invoke();
                    Log(enableDebugLog, logContext, "parry close", data.patternId);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            if (hitboxOpened && !hitboxClosed)
                onHitboxClose?.Invoke();

            if (parryOpened && !parryClosed)
                onParryWindowClose?.Invoke();
        }
    }

    static float ResolveEndTime(BossAttackTimingData data)
    {
        float end = data.telegraphStartTime + data.telegraphDuration;
        if (data.useTimingDataHitboxControl)
            end = Mathf.Max(end, data.hitboxCloseTime);
        end = Mathf.Max(end, data.parryWindowEndTime);
        end = Mathf.Max(end, data.recoveryStartTime + data.recoveryDuration);
        return Mathf.Max(0.001f, end);
    }

    static void Log(bool enableDebugLog, UnityEngine.Object context, string message, BossPatternId patternId)
    {
        if (!enableDebugLog)
            return;

        Debug.Log("[BossAttackTiming] " + message + " " + patternId, context);
    }
}
