using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single owner of Time.timeScale. Systems (pause, hit stop, slow-mo, cinematics) add a request
/// keyed by an owner object; the effective scale is the minimum of all live requests (1 if none).
/// This prevents overlapping effects from "restoring" each other to the wrong value.
/// fixedDeltaTime follows the effective scale so physics stays smooth during slow-mo.
/// </summary>
public static class GameTimeScale
{
    static readonly Dictionary<object, float> s_requests = new Dictionary<object, float>();
    static readonly List<object> s_pruneBuffer = new List<object>();
    static float s_baseFixedDeltaTime = 0.02f;

    public static float Effective { get; private set; } = 1f;
    public static bool IsPaused => Effective <= 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_requests.Clear();
        Effective = 1f;
        // Guard against a corrupted project setting (it was once saved as 0.0001 = 10,000 physics steps/sec).
        s_baseFixedDeltaTime = Time.fixedDeltaTime >= 0.005f ? Time.fixedDeltaTime : 0.02f;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

#if UNITY_EDITOR
    // Time.timeScale / fixedDeltaTime set during Play Mode leak into Edit Mode and can be saved into
    // ProjectSettings/TimeManager.asset. Always return to normal time when Play Mode ends.
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterEditorPlayModeReset()
    {
        UnityEditor.EditorApplication.playModeStateChanged += state =>
        {
            if (state != UnityEditor.PlayModeStateChange.EnteredEditMode)
                return;

            s_requests.Clear();
            Effective = 1f;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = s_baseFixedDeltaTime;
        };
    }
#endif

    /// <summary>Add or update this owner's requested scale (0 = pause, 1 = normal).</summary>
    public static void Request(object owner, float scale)
    {
        if (owner == null)
            return;

        s_requests[owner] = Mathf.Clamp01(scale);
        Apply();
    }

    /// <summary>Remove this owner's request.</summary>
    public static void Release(object owner)
    {
        if (owner != null && s_requests.Remove(owner))
            Apply();
    }

    public static bool HasRequest(object owner) => owner != null && s_requests.ContainsKey(owner);

    /// <summary>Drop every request (scene transitions, returning to menus).</summary>
    public static void ReleaseAll()
    {
        s_requests.Clear();
        Apply();
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply();
    }

    static void Apply()
    {
        PruneDestroyedOwners();

        float scale = 1f;
        foreach (float requested in s_requests.Values)
            scale = Mathf.Min(scale, requested);

        Effective = scale;
        Time.timeScale = scale;
        Time.fixedDeltaTime = s_baseFixedDeltaTime * (scale > 0f ? scale : 1f);
    }

    static void PruneDestroyedOwners()
    {
        s_pruneBuffer.Clear();
        foreach (object owner in s_requests.Keys)
        {
            // Unity objects that were destroyed (e.g. by a scene load) must not keep time frozen.
            if (owner is Object unityObject && !unityObject)
                s_pruneBuffer.Add(owner);
        }

        for (int i = 0; i < s_pruneBuffer.Count; i++)
            s_requests.Remove(s_pruneBuffer[i]);
    }
}
