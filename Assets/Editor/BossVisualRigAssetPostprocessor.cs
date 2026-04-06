using UnityEditor;
using UnityEngine;

public sealed class BossVisualRigAssetPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (ContainsBossModel(importedAssets) || ContainsBossModel(movedAssets))
        {
            BossVisualRigSyncUtility.ScheduleSyncFromImport();
            return;
        }

        if (ContainsBossModel(deletedAssets))
            Debug.LogWarning("[BossVisualRigAssetPostprocessor] Preferred boss visual model was deleted. Boss visual sync skipped.");
    }

    static bool ContainsBossModel(string[] assetPaths)
    {
        if (assetPaths == null || assetPaths.Length == 0)
            return false;

        for (int i = 0; i < assetPaths.Length; i++)
        {
            if (BossVisualRigSyncUtility.IsBossModelAssetPath(assetPaths[i]))
                return true;
        }

        return false;
    }
}
