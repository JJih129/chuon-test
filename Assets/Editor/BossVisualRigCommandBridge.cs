using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BossVisualRigCommandBridge
{
    public const string RequestAssetPath = "Assets/Editor/BossVisualRigSync.request.txt";

    static BossVisualRigCommandBridge()
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

        BossVisualRigSyncUtility.SyncSummary summary = BossVisualRigSyncUtility.RunFromFastMcp();
        Debug.Log("[BossVisualRigCommandBridge] " + summary.Details);
    }
}
