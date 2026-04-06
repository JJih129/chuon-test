using UnityEngine;

public enum ResolvedHitOutcome
{
    Ignored = 0,
    PerfectDodge = 1,
    Parry = 2,
    GuardBlock = 3,
    Damage = 4
}

public readonly struct ResolvedHitResult
{
    public ResolvedHitOutcome Outcome { get; }
    public float HealthDamage { get; }
    public float ChipDamage { get; }
    public HitType HitType { get; }
    public float BreakBonus { get; }
    public bool ShouldSuppressHitAnimation { get; }

    public bool IsIgnored => Outcome == ResolvedHitOutcome.Ignored;
    public bool IsPerfectDodge => Outcome == ResolvedHitOutcome.PerfectDodge;
    public bool IsParry => Outcome == ResolvedHitOutcome.Parry;
    public bool IsGuardBlock => Outcome == ResolvedHitOutcome.GuardBlock;
    public bool IsDamage => Outcome == ResolvedHitOutcome.Damage;

    ResolvedHitResult(
        ResolvedHitOutcome outcome,
        float healthDamage,
        float chipDamage,
        HitType hitType,
        float breakBonus,
        bool shouldSuppressHitAnimation)
    {
        Outcome = outcome;
        HealthDamage = Mathf.Max(0f, healthDamage);
        ChipDamage = Mathf.Max(0f, chipDamage);
        HitType = hitType;
        BreakBonus = Mathf.Max(0f, breakBonus);
        ShouldSuppressHitAnimation = shouldSuppressHitAnimation;
    }

    public static ResolvedHitResult Ignored()
    {
        return new ResolvedHitResult(ResolvedHitOutcome.Ignored, 0f, 0f, HitType.None, 0f, false);
    }

    public static ResolvedHitResult PerfectDodge()
    {
        return new ResolvedHitResult(ResolvedHitOutcome.PerfectDodge, 0f, 0f, HitType.None, 0f, false);
    }

    public static ResolvedHitResult Parry()
    {
        return new ResolvedHitResult(ResolvedHitOutcome.Parry, 0f, 0f, HitType.None, 0f, false);
    }

    public static ResolvedHitResult GuardBlock(float chipDamage, bool shouldSuppressHitAnimation)
    {
        return new ResolvedHitResult(
            ResolvedHitOutcome.GuardBlock,
            0f,
            chipDamage,
            HitType.Guarded,
            0f,
            shouldSuppressHitAnimation);
    }

    public static ResolvedHitResult Damage(
        float healthDamage,
        HitType hitType,
        float breakBonus = 0f,
        bool shouldSuppressHitAnimation = false)
    {
        return new ResolvedHitResult(
            ResolvedHitOutcome.Damage,
            healthDamage,
            0f,
            hitType,
            breakBonus,
            shouldSuppressHitAnimation);
    }
}
