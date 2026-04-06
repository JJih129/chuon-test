using UnityEngine;

public enum DefenseFeelKind
{
    Parry = 0,
    Block = 1,
    PerfectDodge = 2,
}

public readonly struct CombatFeelPreset
{
    public readonly float TimeScale;
    public readonly float StopDuration;
    public readonly float CameraImpulseForce;
    public readonly float CameraShakeAmplitude;
    public readonly float CameraShakeDuration;
    public readonly float AudioVolumeMultiplier;
    public readonly float AudioPitchMin;
    public readonly float AudioPitchMax;

    public CombatFeelPreset(
        float timeScale,
        float stopDuration,
        float cameraImpulseForce,
        float cameraShakeAmplitude,
        float cameraShakeDuration,
        float audioVolumeMultiplier,
        float audioPitchMin,
        float audioPitchMax)
    {
        TimeScale = timeScale;
        StopDuration = stopDuration;
        CameraImpulseForce = cameraImpulseForce;
        CameraShakeAmplitude = cameraShakeAmplitude;
        CameraShakeDuration = cameraShakeDuration;
        AudioVolumeMultiplier = audioVolumeMultiplier;
        AudioPitchMin = audioPitchMin;
        AudioPitchMax = audioPitchMax;
    }
}

public static class CombatFeelPolicy
{
    public static CombatFeelPreset GetDefensePreset(DefenseFeelKind kind)
    {
        switch (kind)
        {
            case DefenseFeelKind.Parry:
                return new CombatFeelPreset(0.12f, 0.03f, 0.7f, 0.08f, 0.05f, 1.08f, 0.98f, 1.02f);

            case DefenseFeelKind.PerfectDodge:
                return new CombatFeelPreset(0.10f, 0.05f, 1.15f, 0.14f, 0.09f, 1.04f, 0.99f, 1.05f);

            case DefenseFeelKind.Block:
            default:
                return new CombatFeelPreset(0.24f, 0.045f, 0.75f, 0.09f, 0.075f, 0.88f, 0.99f, 1.02f);
        }
    }

    public static CombatFeelPreset GetBossHitPreset(HitType hitType)
    {
        switch (hitType)
        {
            case HitType.Heavy:
            case HitType.Force:
                return new CombatFeelPreset(1f, 0f, 0f, 0.24f, 0.11f, 1.12f, 0.94f, 1.00f);

            case HitType.Strong:
            case HitType.Parried:
                return new CombatFeelPreset(1f, 0f, 0f, 0.20f, 0.10f, 1.06f, 0.96f, 1.02f);

            case HitType.Light:
                return new CombatFeelPreset(1f, 0f, 0f, 0.11f, 0.07f, 0.90f, 1.00f, 1.05f);

            case HitType.Guarded:
                return new CombatFeelPreset(1f, 0f, 0f, 0.08f, 0.06f, 0.82f, 1.01f, 1.04f);

            case HitType.Normal:
            case HitType.None:
            default:
                return new CombatFeelPreset(1f, 0f, 0f, 0.14f, 0.08f, 0.96f, 0.98f, 1.04f);
        }
    }

    public static CombatFeelPreset GetPlayerHitPreset(int damage, int heavyDamageThreshold, HitType hitType)
    {
        switch (hitType)
        {
            case HitType.Heavy:
            case HitType.Force:
                return new CombatFeelPreset(0.08f, 0.065f, 0f, 0.30f, 0.17f, 1f, 1f, 1f);

            case HitType.Strong:
            case HitType.Parried:
                return new CombatFeelPreset(0.12f, 0.055f, 0f, 0.25f, 0.14f, 1f, 1f, 1f);

            case HitType.Light:
                return new CombatFeelPreset(0.24f, 0.030f, 0f, 0.12f, 0.08f, 1f, 1f, 1f);
        }

        if (damage >= Mathf.Max(1, heavyDamageThreshold))
            return new CombatFeelPreset(0.10f, 0.060f, 0f, 0.28f, 0.16f, 1f, 1f, 1f);

        return new CombatFeelPreset(0.20f, 0.038f, 0f, 0.18f, 0.11f, 1f, 1f, 1f);
    }
}
