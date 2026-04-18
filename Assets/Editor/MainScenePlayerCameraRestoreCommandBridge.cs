using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MainScenePlayerCameraRestoreCommandBridge
{
    public const string RequestAssetPath = "Assets/Editor/MainScenePlayerCameraRestore.request.txt";

    static MainScenePlayerCameraRestoreCommandBridge()
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

        string result = MainScenePlayerCameraRestoreUtility.RunFromFastMcp();
        Debug.Log("[MainScenePlayerCameraRestoreCommandBridge] " + result);
    }
}
