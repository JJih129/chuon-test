using UnityEngine;

/// <summary>
/// Restores a predictable runtime state when moving between gameplay and menu scenes.
/// Keep this utility lightweight and side-effect free outside of cursor/audio/time normalization.
/// </summary>
public static class RuntimeMenuSceneStateUtility
{

    public static void PrepareForMenuScene(bool unlockCursor = true)
    {
        ResetSimulationClock();
        ResetAudioPause();
        ApplyCursorState(unlockCursor);
    }

    public static void PrepareForGameplayScene(bool lockCursor = true)
    {
        ResetSimulationClock();
        ResetAudioPause();
        ApplyCursorState(!lockCursor);
    }

    /// <summary>
    /// Resume from the pause menu: release only the pause request so other freezes
    /// (ultimate cinematic, credits, tutorial prompts) keep running correctly.
    /// </summary>
    public static void ResumeFromPause(object pauseOwner, bool lockCursor = true)
    {
        GameTimeScale.Release(pauseOwner);
        ResetAudioPause();
        ApplyCursorState(!lockCursor);
    }

    public static void ResetSimulationClock()
    {
        if (TimeScaleController.Instance != null)
            TimeScaleController.Instance.ResetTimeScale(0f);

        // Menus / scene entry always start from normal time: drop every pause or slow-mo request.
        GameTimeScale.ReleaseAll();
    }

    public static void ResetAudioPause()
    {
        AudioListener.pause = false;
    }

    public static void ApplyCursorState(bool unlockCursor)
    {
        Cursor.lockState = unlockCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = unlockCursor;
    }
}
