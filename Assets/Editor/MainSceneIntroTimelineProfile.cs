using UnityEngine;

[CreateAssetMenu(fileName = "MainSceneIntroTimelineProfile", menuName = "Project ChuOn/Cinematics/Main Scene Intro Timeline Profile")]
public sealed class MainSceneIntroTimelineProfile : ScriptableObject
{
    [Header("Signals")]
    [Min(0f)] public double beginTime = 0d;
    [Min(0f)] public double doorOpenTime = 0.65d;
    [Min(0f)] public double playerWalkOutTime = 2.25d;
    [Min(0f)] public double bossRevealTime = 5.65d;
    [Min(0f)] public double firstDialogueTime = 0.25d;
    [Min(0f)] public double combatStartLead = 0.05d;
    [Min(0f)] public double endLead = 0.15d;
    [Min(0f)] public double timelineTailPadding = 0.5d;

    [Header("Camera Shots")]
    [Min(0.1f)] public double elevatorShotDuration = 5.55d;
    [Min(0.1f)] public double bossRevealShotDuration = 13.05d;

    [Header("Dialogue Spacing")]
    [Min(0.1f)] public double defaultLineSpacing = 2.15d;
    [Min(0.1f)] public double bossIdentifyLineSpacing = 3.0d;
    [Min(0.1f)] public double longLineSpacing = 2.7d;

    public double ResolveLineSpacing(int index)
    {
        if (index == 4)
            return bossIdentifyLineSpacing;
        if (index == 6 || index == 8)
            return longLineSpacing;

        return defaultLineSpacing;
    }
}
