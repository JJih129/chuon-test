using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ApplyChuonLastPlayerModel
{
    const string ModelPath = "Assets/Modeling/Player/chuonLast.fbx";
    const string VisualPrefabPath = "Assets/Prefabs/Generated/PlayerVisual_Chuon.prefab";
    const string PlayerRootPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";
    const string SessionKey = "ProjectChuOn.ApplyChuonLastPlayerModel.Attempted";

    [InitializeOnLoadMethod]
    static void AutoApplyOnLoad()
    {
        EditorApplication.delayCall += TryApplyOnce;
    }

    [MenuItem("Tools/Player/Apply chuonLast Model")]
    public static void ApplyFromMenu()
    {
        SessionState.EraseBool(SessionKey);
        TryApplyOnce();
    }

    static void TryApplyOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (SessionState.GetBool(SessionKey, false))
            return;

        SessionState.SetBool(SessionKey, true);

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            Debug.LogWarning("[ApplyChuonLastPlayerModel] Model asset not found: " + ModelPath);
            return;
        }

        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null)
        {
            Debug.LogWarning("[ApplyChuonLastPlayerModel] Avatar not found in model asset.");
            return;
        }

        if (IsAlreadyApplied(modelAsset, avatar))
            return;

        RebuildVisualPrefab(modelAsset);
        UpdatePlayerRootPrefab(avatar);
        SyncLoadedSceneVisuals();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ApplyChuonLastPlayerModel] Applied chuonLast to player visual prefab, prefab hierarchy, and loaded scene visuals.");
    }

    static bool IsAlreadyApplied(GameObject modelAsset, Avatar avatar)
    {
        var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
        var playerRootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRootPrefabPath);
        if (visualPrefab == null || playerRootPrefab == null)
            return false;

        var childSource = PrefabUtility.LoadPrefabContents(VisualPrefabPath);
        try
        {
            if (childSource.transform.childCount == 0)
                return false;

            var firstChild = childSource.transform.GetChild(0).gameObject;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(firstChild);
            var sameModel = source == modelAsset;

            var rootAnimator = playerRootPrefab.GetComponent<Animator>();
            var sameAvatar = rootAnimator != null && rootAnimator.avatar == avatar;
            return sameModel && sameAvatar;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(childSource);
        }
    }

    static void RebuildVisualPrefab(GameObject modelAsset)
    {
        var tempRoot = new GameObject("PlayerVisual_Chuon");
        try
        {
            var modelInstance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
            if (modelInstance == null)
                throw new System.InvalidOperationException("Could not instantiate model prefab asset.");

            modelInstance.transform.SetParent(tempRoot.transform, false);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            var mainAnimator = modelInstance.GetComponent<Animator>() ?? modelInstance.GetComponentInChildren<Animator>(true);
            var rig = tempRoot.AddComponent<PlayerVisualRig>();
            var rigSo = new SerializedObject(rig);
            rigSo.FindProperty("visualRoot").objectReferenceValue = modelInstance.transform;
            rigSo.FindProperty("mainAnimator").objectReferenceValue = mainAnimator;
            rigSo.FindProperty("attackHitboxes").arraySize = 0;
            rigSo.ApplyModifiedPropertiesWithoutUndo();
            rig.SyncSerializedReferences();

            PrefabUtility.SaveAsPrefabAsset(tempRoot, VisualPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(tempRoot);
        }
    }

    static void UpdatePlayerRootPrefab(Avatar avatar)
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerRootPrefabPath);
        try
        {
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
            var references = root.GetComponent<PlayerReferences>();
            if (references != null && visualPrefab != null)
            {
                var refsSo = new SerializedObject(references);
                refsSo.FindProperty("visualPrefab").objectReferenceValue = visualPrefab;
                refsSo.ApplyModifiedPropertiesWithoutUndo();
                references.SyncSerializedReferences();
                references.SyncVisualPrefabHierarchyForEditor();
                EditorUtility.SetDirty(references);
            }

            var animator = root.GetComponent<Animator>();
            if (animator != null)
            {
                animator.avatar = avatar;
                EditorUtility.SetDirty(animator);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerRootPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SyncLoadedSceneVisuals()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            var dirtyBefore = scene.isDirty;
            var roots = scene.GetRootGameObjects();
            var updated = false;
            foreach (var root in roots)
            {
                if (root == null)
                    continue;

                var references = root.GetComponentsInChildren<PlayerReferences>(true);
                if (references == null || references.Length == 0)
                    continue;

                foreach (var playerReferences in references)
                {
                    if (playerReferences == null)
                        continue;

                    playerReferences.SyncSerializedReferences();
                    playerReferences.SyncVisualPrefabHierarchyForEditor();
                    EditorUtility.SetDirty(playerReferences);
                    EditorUtility.SetDirty(playerReferences.gameObject);
                    updated = true;
                }
            }

            if (updated && (scene.isDirty || !dirtyBefore))
                EditorSceneManager.SaveScene(scene);
        }
    }
}
