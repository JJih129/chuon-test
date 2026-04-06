using UnityEngine;

/// <summary>
/// Restores a predictable runtime state when moving between gameplay and menu scenes.
/// Keep this utility lightweight and side-effect free outside of cursor/audio/time normalization.
/// </summary>
public static class RuntimeMenuSceneStateUtility
{
    const float DefaultFixedDeltaTime = 0.02f;

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

    public static void ResetSimulationClock()
    {
        if (TimeScaleController.Instance != null)
        {
            TimeScaleController.Instance.ResetTimeScale(0f);
            return;
        }

        Time.timeScale = 1f;
        Time.fixedDeltaTime = DefaultFixedDeltaTime;
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
