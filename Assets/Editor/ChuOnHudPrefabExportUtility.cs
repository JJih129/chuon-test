using System.IO;
using UnityEditor;
using UnityEngine;

public static class ChuOnHudPrefabExportUtility
{
    const string FolderPath = "Assets/Prefabs/UI/HUD";
    const string PlayerHudPrefabPath = FolderPath + "/ChuOn_PlayerHUD.prefab";
    const string BossHudPrefabPath = FolderPath + "/ChuOn_BossHUD.prefab";

    [MenuItem("Tools/ChuOn/UI/Export HUD Prefabs")]
    public static void ExportHudPrefabs()
    {
        EnsureFolder();

        bool exportedAny = false;
        exportedAny |= SaveSceneObjectAsPrefab("Player_HUD", PlayerHudPrefabPath);
        exportedAny |= SaveSceneObjectAsPrefab("Boss_HUD", BossHudPrefabPath);

        if (exportedAny)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    static bool SaveSceneObjectAsPrefab(string objectName, string prefabPath)
    {
        GameObject source = FindSceneObject(objectName);
        if (source == null)
        {
            Debug.LogWarning($"[ChuOnHudPrefabExport] Scene object not found: {objectName}.");
            return false;
        }

        PrefabUtility.SaveAsPrefabAsset(source, prefabPath, out bool success);
        if (!success)
            Debug.LogWarning($"[ChuOnHudPrefabExport] Failed to export {objectName} to {prefabPath}.");
        else
            Debug.Log($"[ChuOnHudPrefabExport] Exported {prefabPath}.");

        return success;
    }

    static GameObject FindSceneObject(string objectName)
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject candidate = allObjects[i];
            if (candidate == null || candidate.name != objectName)
                continue;

            if (EditorUtility.IsPersistent(candidate))
                continue;

            if (candidate.scene.IsValid() && candidate.scene.isLoaded)
                return candidate;
        }

        return null;
    }

    static void EnsureFolder()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/UI");
        EnsureFolder(FolderPath);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folder = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folder);
    }
}
