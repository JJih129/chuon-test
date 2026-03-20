using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PauseSettingsContentSyncUtility
{
    const string PauseCanvasPrefabPath = "Assets/PauseCanvas.prefab";
    const string PauseOptionsRootPrefabPath = "Assets/Prefabs/Generated/PauseOptionsRoot.prefab";

    static readonly string[] TargetScenePaths =
    {
        "Assets/Scenes/MainScene.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Tutorial.unity",
    };

    [MenuItem("Tools/Validation/Sync Pause Settings Content")]
    static void SyncPauseSettingsContent()
    {
        RunInternal(showDialog: true);
    }

    public static SyncSummary RunFromFastMcp()
    {
        return RunInternal(showDialog: false);
    }

    static SyncSummary RunInternal(bool showDialog)
    {
        var activeScene = SceneManager.GetActiveScene();
        var activeScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
        var report = new SyncReport();
        var chromeSummary = PauseOptionsChromeSyncUtility.RunFromFastMcp();
        var optionsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PauseOptionsRootPrefabPath);

        if (optionsPrefab == null)
        {
            report.Warn($"Pause options prefab missing: {PauseOptionsRootPrefabPath}");
            report.Emit(showDialog);
            return report.ToSummary();
        }

        if (!string.IsNullOrWhiteSpace(chromeSummary.Details))
            report.Info(chromeSummary.Details.Trim());

        try
        {
            SyncPauseCanvasPrefab(optionsPrefab, report);

            foreach (var scenePath in TargetScenePaths)
                SyncScene(scenePath, optionsPrefab, report);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(activeScenePath))
                EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
        }

        report.Emit(showDialog);
        return report.ToSummary();
    }

    static void SyncPauseCanvasPrefab(GameObject optionsPrefab, SyncReport report)
    {
        var root = PrefabUtility.LoadPrefabContents(PauseCanvasPrefabPath);
        try
        {
            var changed = SyncPauseViews(root.GetComponentsInChildren<PauseMenuView>(true), optionsPrefab);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PauseCanvasPrefabPath);
                report.Info($"Synced prefab: {PauseCanvasPrefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SyncScene(string scenePath, GameObject optionsPrefab, SyncReport report)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var pauseViews = FindComponentsInScene<PauseMenuView>(scene);
        if (pauseViews.Count == 0)
            return;

        var changed = SyncPauseViews(pauseViews, optionsPrefab);
        if (!changed)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.Info($"Synced scene: {scenePath}");
    }

    static bool SyncPauseViews(IEnumerable<PauseMenuView> pauseViews, GameObject optionsPrefab)
    {
        var changed = false;

        foreach (var pauseView in pauseViews)
        {
            if (pauseView == null)
                continue;

            var serialized = new SerializedObject(pauseView);
            var prop = serialized.FindProperty("settingsContentPrefab");
            if (prop == null)
                continue;

            if (prop.objectReferenceValue == optionsPrefab)
                continue;

            prop.objectReferenceValue = optionsPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pauseView);
            changed = true;
        }

        return changed;
    }

    static List<T> FindComponentsInScene<T>(Scene scene) where T : Component
    {
        var results = new List<T>();
        foreach (var root in scene.GetRootGameObjects())
            results.AddRange(root.GetComponentsInChildren<T>(true));
        return results;
    }

    public readonly struct SyncSummary
    {
        public SyncSummary(string details)
        {
            Details = details;
        }

        public string Details { get; }
        public override string ToString() => Details;
    }

    sealed class SyncReport
    {
        readonly List<string> infos = new List<string>();
        readonly List<string> warnings = new List<string>();

        public void Info(string message)
        {
            infos.Add("[INFO] " + message);
        }

        public void Warn(string message)
        {
            warnings.Add("[WARN] " + message);
        }

        public void Emit(bool showDialog)
        {
            var details = BuildDetails();
            if (warnings.Count > 0)
                Debug.LogWarning(details);
            else
                Debug.Log(details);

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Pause Settings Content Sync",
                    $"Warnings: {warnings.Count}\n\nSee Console for details.",
                    "OK");
            }
        }

        public SyncSummary ToSummary()
        {
            return new SyncSummary(BuildDetails());
        }

        string BuildDetails()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Pause settings content sync completed.");
            builder.AppendLine($"Warnings: {warnings.Count}");

            foreach (var info in infos)
                builder.AppendLine(info);

            foreach (var warning in warnings)
                builder.AppendLine(warning);

            return builder.ToString();
        }
    }
}
