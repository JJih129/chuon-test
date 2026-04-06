using System;
using UnityEngine;

public enum TutorialStepType
{
    Movement = 0,
    CameraFocus = 1,
    LockOn = 2,
    BasicAttack = 3,
    Combo = 4,
    Guard = 5,
    Parry = 6,
    Dodge = 7,
    PerfectDodge = 8,
    Heal = 9,
    Ultimate = 10,
    Exit = 11
}

public enum EGOGuideMessageType
{
    Briefing = 0,
    Tactical = 1,
    Success = 2,
    FailureAssist = 3
}

public enum TutorialAttackKind
{
    Unknown = 0,
    Light = 1,
    Heavy = 2
}

public enum TutorialDummyRole
{
    PassiveTarget = 0,
    GuardParry = 1,
    Dodge = 2,
    UltimateTarget = 3
}

public enum TrainingDummyAttackResult
{
    None = 0,
    Missed = 1,
    Hit = 2,
    Guarded = 3,
    Parried = 4,
    Dodged = 5,
    PerfectDodged = 6
}

[Serializable]
public struct TutorialGuideLine
{
    public EGOGuideMessageType messageType;
    public string speaker;
    [TextArea(2, 4)] public string text;
    public AudioClip clip;
    [Min(0f)] public float duration;
    public bool urgent;

    public string DeduplicationKey
    {
        get
        {
            string resolvedSpeaker = string.IsNullOrWhiteSpace(speaker) ? "EGO" : speaker.Trim();
            string resolvedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            return resolvedSpeaker + "|" + resolvedText;
        }
    }
}

[Serializable]
public struct TutorialCombatHitInfo
{
    public TutorialAttackKind attackKind;
    public int comboDepth;
    public HitPayload payload;
}

[Serializable]
public sealed class TrainingDummyStepProfile
{
    public TutorialStepType stepType;
    public bool active = true;
    public bool loopAttack;
    public bool useProjectileAttack;
    [Min(0f)] public float initialDelay = 0.75f;
    [Min(0.1f)] public float attackInterval = 2.25f;
    [Min(0.1f)] public float telegraphDuration = 0.8f;
    [Min(0f)] public float damage = 8f;
    [Min(0.5f)] public float hitRange = 3f;
    [Min(0.1f)] public float hitRadius = 1.1f;
    [Min(1f)] public float projectileSpeed = 12f;
    [Min(0.25f)] public float projectileLifeTime = 3f;
    public bool countProjectileMissAsDodge = true;
    public bool canParry = true;
    public bool canPerfectDodge;
    public bool unblockable;
    public bool invulnerable = true;
    public bool useHealth;
    public Color stateColor = new Color(0.10f, 0.85f, 1.00f, 1f);
    public Color dangerIndicatorColor = new Color(1f, 0.32f, 0.16f, 0.95f);
    [Min(0.02f)] public float dangerIndicatorWidth = 0.18f;
    public bool showTargetMarker = true;
    public Color targetMarkerColor = new Color(1f, 0.22f, 0.14f, 0.72f);
    [Min(0.1f)] public float targetMarkerSize = 0.55f;
    public bool enableAdaptiveAssist;
    [Min(1)] public int assistStartAfterFailures = 2;
    [Min(0)] public int maxAdaptiveFailureStacks = 2;
    [Min(0f)] public float adaptiveTelegraphBonusPerStack = 0.12f;
    [Range(0f, 0.45f)] public float adaptiveProjectileSpeedReductionPerStack = 0.12f;
    [Min(0f)] public float adaptiveTargetMarkerSizeBonusPerStack = 0.08f;
    [Min(0f)] public float adaptiveIndicatorWidthBonusPerStack = 0.02f;
}
