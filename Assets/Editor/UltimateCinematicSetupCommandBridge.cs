using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UltimateCinematicSetupCommandBridge
{
    public const string RequestAssetPath = "Assets/Editor/UltimateCinematicSetup.request.txt";

    static UltimateCinematicSetupCommandBridge()
    {
        EditorApplication.delayCall += TryProcessPendingRequest;
        EditorApplication.projectChanged += TryProcessPendingRequest;
    }

    static void TryProcessPendingRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!AssetDatabase.LoadAssetAtPath<TextAsset>(RequestAssetPath))
            return;

        AssetDatabase.DeleteAsset(RequestAssetPath);
        AssetDatabase.Refresh();

        UltimateCinematicSetupUtility.SetupSummary summary = UltimateCinematicSetupUtility.RunFromFastMcp();
        Debug.Log("[UltimateCinematicSetupCommandBridge] " + summary.Details);
    }
}
