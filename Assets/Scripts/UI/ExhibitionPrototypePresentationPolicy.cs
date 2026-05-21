public static class ExhibitionPrototypePresentationPolicy
{
    // Prototype-only overlays are disabled in playable builds/scenes.
    public static bool RuntimeObjectivePanelEnabled => false;
    public static bool RuntimePrototypeUiEnabled => false;
    public static bool RuntimeSupportPanelEnabled => false;
    public static bool RuntimeBossStatusPanelEnabled => false;
    public static bool RuntimeObjectiveTacticalTextEnabled => false;
    public static bool RuntimeTutorialObjectiveControllerEnabled => false;
    public static bool RuntimeTutorialGuidePathEnabled => false;
    public static bool RuntimeTutorialKeyCueEnabled => true;
    public static bool RuntimeLobbyGuidePathEnabled => false;
    public static bool RuntimeDiagnosticsEnabled => false;
    public static bool RuntimeCoachFeedbackEnabled => false;
    public static bool RuntimePrototypeGuidesEnabled => false;
    public static bool DialogueEnabled => false;
}
