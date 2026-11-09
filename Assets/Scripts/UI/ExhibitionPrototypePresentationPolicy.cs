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
    // 대사(EGO/추온/시스템) 표시: 기본 켜짐 (대사 스크립트 v2, Docs/Dialogue_Script_v2.md).
    // 옵션/PlayerPrefs(DialoguePrefKey=0)로 끌 수 있고, QA 스위치(-qaDialogue)는 항상 강제로 켠다.
    public const string DialoguePrefKey = "chuon.dialogue.enabled";
    public const string CombatBarkPrefKey = "chuon.dialogue.barks";
    public static bool ForceDialogueForQa;

    static int s_dialoguePref = -1;
    static int s_barkPref = -1;

    public static bool DialogueEnabled
    {
        get
        {
            if (ForceDialogueForQa)
                return true;
            if (s_dialoguePref < 0)
                s_dialoguePref = UnityEngine.PlayerPrefs.GetInt(DialoguePrefKey, 1);
            return s_dialoguePref != 0;
        }
    }

    // 전투 중 짧은 대사(CombatBarkDirector). 대사가 꺼져 있으면 함께 꺼진다.
    public static bool CombatBarksEnabled
    {
        get
        {
            if (!DialogueEnabled)
                return false;
            if (s_barkPref < 0)
                s_barkPref = UnityEngine.PlayerPrefs.GetInt(CombatBarkPrefKey, 1);
            return s_barkPref != 0;
        }
    }

    public static void SetDialogueEnabled(bool enabled)
    {
        s_dialoguePref = enabled ? 1 : 0;
        UnityEngine.PlayerPrefs.SetInt(DialoguePrefKey, s_dialoguePref);
    }

    public static void SetCombatBarksEnabled(bool enabled)
    {
        s_barkPref = enabled ? 1 : 0;
        UnityEngine.PlayerPrefs.SetInt(CombatBarkPrefKey, s_barkPref);
    }
}
