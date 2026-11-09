// GameLog.cs
// Development-only log helper. Calls (including argument evaluation) are stripped
// in player builds unless the scripting define symbol CHUON_DEBUG_LOGS is set (always kept in the Editor,
// where the per-component debug toggles still decide whether anything is logged).
// Use Debug.LogWarning / Debug.LogError directly for real problems.

using System.Diagnostics;

public static class GameLog
{
    [Conditional("CHUON_DEBUG_LOGS"), Conditional("UNITY_EDITOR")]
    public static void Info(string message, UnityEngine.Object context = null)
    {
        UnityEngine.Debug.Log(message, context);
    }

    [Conditional("CHUON_DEBUG_LOGS"), Conditional("UNITY_EDITOR")]
    public static void Warn(string message, UnityEngine.Object context = null)
    {
        UnityEngine.Debug.LogWarning(message, context);
    }
}
