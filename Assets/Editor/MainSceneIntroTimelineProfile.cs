using UnityEngine;

[CreateAssetMenu(fileName = "MainSceneIntroTimelineProfile", menuName = "Project ChuOn/Cinematics/Main Scene Intro Timeline Profile")]
public sealed class MainSceneIntroTimelineProfile : ScriptableObject
{
    [Header("Signals")]
    [Min(0f)] public double beginTime = 0d;
    [Min(0f)] public double doorOpenTime = 0.65d;
    [Min(0f)] public double playerWalkOutTime = 2.25d;
    [Min(0f)] public double bossRevealTime = 4.35d;
    [Min(0f)] public double firstDialogueTime = 0.25d;
    [Min(0f)] public double combatStartLead = 0.05d;
    [Min(0f)] public double endLead = 0.15d;
    [Min(0f)] public double timelineTailPadding = 0.5d;

    [Header("Camera Shots")]
    [Min(0.1f)] public double elevatorShotDuration = 4.2d;
    [Min(0.1f)] public double bossRevealShotDuration = 12.8d;

    [Header("Dialogue Spacing")]
    [Min(0.1f)] public double defaultLineSpacing = 1.55d;
    [Min(0.1f)] public double bossIdentifyLineSpacing = 2.0d;
    [Min(0.1f)] public double longLineSpacing = 2.0d;

    public double ResolveLineSpacing(int index)
    {
        if (index == 4)
            return bossIdentifyLineSpacing;
        if (index == 6 || index == 8)
            return longLineSpacing;

        return defaultLineSpacing;
    }
}
