#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MissingScriptScannerUtility
{
    public readonly struct ScanSummary
    {
        public ScanSummary(string details, int findingCount)
        {
            Details = details;
            FindingCount = findingCount;
        }

        public string Details { get; }
        public int FindingCount { get; }
        public override string ToString() => Details;
    }

    public static ScanSummary RunFromFastMcp()
    {
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        string activeScenePath = EditorSceneManager.GetActiveScene().path;
        List<string> findings = new List<string>();

        try
        {
            ScanScenes(findings);
            ScanPrefabs(findings);
        }
        finally
        {
            if (setup != null && setup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
                if (!string.IsNullOrWhiteSpace(activeScenePath))
                {
                    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    {
                        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                        if (scene.path == activeScenePath)
                        {
                            EditorSceneManager.SetActiveScene(scene);
                            break;
                        }
                    }
                }
            }
        }

        string details = BuildDetails(findings);
        if (findings.Count > 0)
            Debug.LogWarning(details);
        else
            Debug.Log(details);

        return new ScanSummary(details, findings.Count);
    }

    static void ScanScenes(List<string> findings)
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int j = 0; j < roots.Length; j++)
                CollectFindings("Scene", path, roots[j].transform, findings);
        }
    }

    static void ScanPrefabs(List<string> findings)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                if (root != null)
                    CollectFindings("Prefab", path, root.transform, findings);
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    static void CollectFindings(string assetType, string assetPath, Transform transform, List<string> findings)
    {
        int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        if (missingCount > 0)
            findings.Add(assetType + "\t" + assetPath + "\t" + HierarchyPath(transform) + "\tmissing=" + missingCount);

        for (int i = 0; i < transform.childCount; i++)
            CollectFindings(assetType, assetPath, transform.GetChild(i), findings);
    }

    static string HierarchyPath(Transform transform)
    {
        List<string> names = new List<string>();
        while (transform != null)
        {
            names.Add(transform.name);
            transform = transform.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    static string BuildDetails(List<string> findings)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Missing script scan completed.");
        builder.AppendLine("Findings: " + findings.Count);

        for (int i = 0; i < findings.Count; i++)
            builder.AppendLine(findings[i]);

        return builder.ToString();
    }
}
#endif
