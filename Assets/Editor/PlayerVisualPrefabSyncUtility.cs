using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerVisualPrefabSyncUtility
{
    const string PlayerRootPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";

    static readonly string[] TargetScenePaths =
    {
        "Assets/Scenes/MainScene.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Tutorial.unity",
    };

    [MenuItem("Tools/Validation/Sync Player Visual Prefab")]
    static void SyncPlayerVisualPrefab()
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
                report.Info($"Synced prefab visual: {PlayerRootPrefabPath}");
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
            report.Info($"Synced scene visual: {scenePath}");
        }
    }

    static bool SyncPlayerObject(GameObject player, SyncReport report, string scopeLabel)
    {
        if (player == null)
        {
            report.Warn($"Player object missing in {scopeLabel}");
            return false;
        }

        var references = player.GetComponent<PlayerReferences>();
        if (references == null)
        {
            report.Warn($"PlayerReferences missing in {scopeLabel}");
            return false;
        }

        references.SyncSerializedReferences();
        EditorUtility.SetDirty(references);

        var visualRoot = references.VisualRoot;
        var visualPrefab = references.VisualPrefab;
        if (visualRoot == null)
        {
            report.Warn($"VisualRoot missing in {scopeLabel}");
            return false;
        }

        if (visualPrefab == null)
        {
            report.Warn($"visualPrefab missing in {scopeLabel}");
            return false;
        }

        var changed = false;
        for (var i = visualRoot.childCount - 1; i >= 0; i--)
        {
            var child = visualRoot.GetChild(i);
            if (child == null)
                continue;

            Object.DestroyImmediate(child.gameObject);
            changed = true;
        }

        var instance = InstantiateVisualPrefab(visualPrefab, visualRoot, player.scene);
        if (instance == null)
        {
            report.Warn($"Failed to instantiate visual prefab in {scopeLabel}: {visualPrefab.name}");
            return changed;
        }

        changed = true;
        instance.name = visualPrefab.name;

        var instanceTransform = instance.transform;
        instanceTransform.SetParent(visualRoot, false);
        instanceTransform.localPosition = Vector3.zero;
        instanceTransform.localRotation = Quaternion.identity;
        instanceTransform.localScale = Vector3.one;

        var rig = instance.GetComponent<PlayerVisualRig>() ?? instance.GetComponentInChildren<PlayerVisualRig>(true);
        if (rig == null)
            rig = instance.AddComponent<PlayerVisualRig>();

        rig.SyncSerializedReferences();
        EditorUtility.SetDirty(rig);

        references.SyncSerializedReferences();
        EditorUtility.SetDirty(instance);
        EditorUtility.SetDirty(player);

        var rootAnimator = player.GetComponent<Animator>();
        var visualAnimator = rig.MainAnimator != null ? rig.MainAnimator : instance.GetComponent<Animator>();
        if (rootAnimator != null && visualAnimator != null && visualAnimator.avatar != null && rootAnimator.avatar != visualAnimator.avatar)
        {
            rootAnimator.avatar = visualAnimator.avatar;
            EditorUtility.SetDirty(rootAnimator);
            changed = true;
        }

        return changed;
    }

    static GameObject InstantiateVisualPrefab(GameObject visualPrefab, Transform visualRoot, Scene destinationScene)
    {
        var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visualPrefab);
        if (string.IsNullOrWhiteSpace(assetPath))
            assetPath = AssetDatabase.GetAssetPath(visualPrefab);

        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            var prefabContentsRoot = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                return Object.Instantiate(prefabContentsRoot);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContentsRoot);
            }
        }

        var prefabAssetType = PrefabUtility.GetPrefabAssetType(visualPrefab);
        if (prefabAssetType != PrefabAssetType.NotAPrefab)
            return PrefabUtility.InstantiatePrefab(visualPrefab, destinationScene) as GameObject;

        return null;
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
        readonly StringBuilder builder = new StringBuilder();
        int warningCount;

        public void Info(string message)
        {
            builder.AppendLine("[INFO] " + message);
        }

        public void Warn(string message)
        {
            warningCount++;
            builder.AppendLine("[WARN] " + message);
        }

        public void Emit(bool showDialog)
        {
            var details = BuildDetails();
            if (warningCount > 0)
                Debug.LogWarning(details);
            else
                Debug.Log(details);

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Player Visual Prefab Sync",
                    $"Warnings: {warningCount}\n\nSee Console for details.",
                    "OK");
            }
        }

        public SyncSummary ToSummary()
        {
            return new SyncSummary(BuildDetails());
        }

        string BuildDetails()
        {
            return "Player visual prefab sync completed.\n"
                + $"Warnings: {warningCount}\n"
                + builder;
        }
    }
}
