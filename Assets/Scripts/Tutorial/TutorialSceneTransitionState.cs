public static class TutorialSceneTransitionState
{
    static bool s_fromTutorialToLobby;
    static bool s_fromLobbyToMain;
    static bool s_fromMainClear;
    static bool s_fromCreditsToTitle;

    public static void MarkTutorialToLobby()
    {
        s_fromTutorialToLobby = true;
    }

    public static bool ConsumeTutorialToLobby()
    {
        bool value = s_fromTutorialToLobby;
        s_fromTutorialToLobby = false;
        return value;
    }

    public static void MarkLobbyToMain()
    {
        s_fromLobbyToMain = true;
    }

    public static bool ConsumeLobbyToMain()
    {
        bool value = s_fromLobbyToMain;
        s_fromLobbyToMain = false;
        return value;
    }

    public static void MarkMainClearExit()
    {
        s_fromMainClear = true;
    }

    public static bool ConsumeMainClearExit()
    {
        bool value = s_fromMainClear;
        s_fromMainClear = false;
        return value;
    }

    public static void MarkCreditsToTitle()
    {
        s_fromCreditsToTitle = true;
    }

    public static bool ConsumeCreditsToTitle()
    {
        bool value = s_fromCreditsToTitle;
        s_fromCreditsToTitle = false;
        return value;
    }
}
