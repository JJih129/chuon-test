using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerWiringSyncUtility
{
    const string PlayerRootPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";

    static readonly string[] TargetScenePaths =
    {
        "Assets/Scenes/MainScene.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Tutorial.unity",
    };

    [MenuItem("Tools/Validation/Sync Player Wiring")]
    static void SyncPlayerWiring()
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

        try
        {
            SyncPlayerRootPrefab(report);

            foreach (var scenePath in TargetScenePaths)
                SyncScene(scenePath, report);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(activeScenePath))
                EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
        }

        report.Emit(showDialog);
        return report.ToSummary();
    }

    static void SyncPlayerRootPrefab(SyncReport report)
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerRootPrefabPath);
        try
        {
            var changed = SyncPlayerObject(root, report, PlayerRootPrefabPath);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PlayerRootPrefabPath);
                report.Info($"Synced prefab: {PlayerRootPrefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SyncScene(string scenePath, SyncReport report)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var player = FindTaggedObject(scene, "Player");
        if (player == null)
        {
            report.Warn($"Skipped scene without Player tag: {scenePath}");
            return;
        }

        var changed = SyncPlayerObject(player, report, scenePath);
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Info($"Synced scene: {scenePath}");
        }
    }

    static bool SyncPlayerObject(GameObject player, SyncReport report, string scopeLabel)
    {
        var changed = false;

        var playerReferences = player.GetComponent<PlayerReferences>();
        if (playerReferences == null)
        {
            report.Warn($"PlayerReferences missing in {scopeLabel}");
        }
        else
        {
            playerReferences.SyncSerializedReferences();
            EditorUtility.SetDirty(playerReferences);
            changed = true;
        }

        var rigs = player.GetComponentsInChildren<PlayerVisualRig>(true);
        foreach (var rig in rigs)
        {
            if (rig == null)
                continue;

            rig.SyncSerializedReferences();
            EditorUtility.SetDirty(rig);
            changed = true;
        }

        return changed;
    }

    static GameObject FindTaggedObject(Scene scene, string tag)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var current in transforms)
            {
                if (current.CompareTag(tag))
                    return current.gameObject;
            }
        }

        return null;
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
                    "Player Wiring Sync",
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
            builder.AppendLine("Player wiring sync completed.");
            builder.AppendLine($"Warnings: {warnings.Count}");

            foreach (var info in infos)
                builder.AppendLine(info);

            foreach (var warning in warnings)
                builder.AppendLine(warning);

            return builder.ToString();
        }
    }
}
