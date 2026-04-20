using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SyncAuxScenePlayerRoots
{
    const string SessionKey = "ProjectChuOn.SyncAuxScenePlayerRoots.v2";
    const string PlayerRootPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";

    static readonly string[] TargetScenePaths =
    {
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Tutorial.unity",
    };

    static SyncAuxScenePlayerRoots()
    {
        EditorApplication.delayCall += RunOnceAfterReload;
    }

    [MenuItem("Tools/Player/Sync Lobby Tutorial PlayerRoot")]
    public static void RunFromMenu()
    {
        SessionState.EraseString(SessionKey);
        RunInternal();
    }

    static void RunOnceAfterReload()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        SessionState.SetBool(SessionKey, true);
        RunInternal();
    }

    static void RunInternal()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRootPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[SyncAuxScenePlayerRoots] Missing player prefab at {PlayerRootPrefabPath}.");
            return;
        }

        string reopenScenePath = SceneManager.GetActiveScene().path;

        try
        {
            for (int i = 0; i < TargetScenePaths.Length; i++)
                SyncScene(TargetScenePaths[i], prefab);
        }
        finally
        {
            if (!string.IsNullOrEmpty(reopenScenePath))
                EditorSceneManager.OpenScene(reopenScenePath, OpenSceneMode.Single);
        }
    }

    static void SyncScene(string scenePath, GameObject playerRootPrefab)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var oldRoot = FindScenePlayerRoot();
        if (oldRoot == null)
        {
            Debug.LogWarning($"[SyncAuxScenePlayerRoots] No PlayerRoot found in {scenePath}.");
            return;
        }

        var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(oldRoot);
        if (prefabSource == playerRootPrefab)
        {
            PrefabUtility.RevertPrefabInstance(oldRoot, InteractionMode.AutomatedAction);
            SyncPlayerReferences(oldRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SyncAuxScenePlayerRoots] Reverted prefab instance in {scenePath}.");
            return;
        }

        var oldRootTransform = oldRoot.transform;
        var oldParent = oldRootTransform.parent;
        int oldSiblingIndex = oldRootTransform.GetSiblingIndex();
        var oldPosition = oldRootTransform.position;
        var oldRotation = oldRootTransform.rotation;
        var oldScale = oldRootTransform.localScale;
        string oldName = oldRoot.name;
        string oldTag = oldRoot.tag;
        int oldLayer = oldRoot.layer;
        bool oldActive = oldRoot.activeSelf;

        var oldTransformMap = BuildTransformMap(oldRootTransform);

        var newRoot = PrefabUtility.InstantiatePrefab(playerRootPrefab, scene) as GameObject;
        if (newRoot == null)
        {
            Debug.LogWarning($"[SyncAuxScenePlayerRoots] Failed to instantiate player prefab for {scenePath}.");
            return;
        }

        var newTransform = newRoot.transform;
        newTransform.SetParent(oldParent, false);
        newTransform.SetSiblingIndex(oldSiblingIndex);
        newTransform.position = oldPosition;
        newTransform.rotation = oldRotation;
        newTransform.localScale = oldScale;
        newRoot.name = oldName;
        newRoot.tag = oldTag;
        newRoot.layer = oldLayer;
        newRoot.SetActive(oldActive);

        SyncPlayerReferences(newRoot);

        var newTransformMap = BuildTransformMap(newTransform);
        RemapSceneReferences(scene, oldRootTransform, newTransform, oldTransformMap, newTransformMap);

        UnityEngine.Object.DestroyImmediate(oldRoot);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[SyncAuxScenePlayerRoots] Replaced player root in {scenePath}.");
    }

    static GameObject FindScenePlayerRoot()
    {
        var refs = UnityEngine.Object.FindObjectOfType<PlayerReferences>(true);
        if (refs != null)
            return refs.gameObject;

        var candidate = GameObject.Find("PlayerRoot");
        if (candidate != null)
            return candidate;

        return GameObject.FindWithTag("Player");
    }

    static void SyncPlayerReferences(GameObject playerRoot)
    {
        if (playerRoot == null)
            return;

        var refs = playerRoot.GetComponent<PlayerReferences>();
        if (refs == null)
            return;

        refs.SyncVisualPrefabHierarchyForEditor();
        refs.SyncSerializedReferences();
        EditorUtility.SetDirty(refs);
    }

    static Dictionary<string, Transform> BuildTransformMap(Transform root)
    {
        var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
        if (root == null)
            return map;

        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            map[GetRelativePath(root, current)] = current;
            for (int i = current.childCount - 1; i >= 0; i--)
                stack.Push(current.GetChild(i));
        }

        return map;
    }

    static string GetRelativePath(Transform root, Transform target)
    {
        if (root == target)
            return string.Empty;

        var parts = new Stack<string>();
        var current = target;
        while (current != null && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts.ToArray());
    }

    static void RemapSceneReferences(
        Scene scene,
        Transform oldRoot,
        Transform newRoot,
        Dictionary<string, Transform> oldMap,
        Dictionary<string, Transform> newMap)
    {
        var components = UnityEngine.Object.FindObjectsOfType<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
                continue;

            if (component.transform == oldRoot || component.transform.IsChildOf(oldRoot))
                continue;

            var serializedObject = new SerializedObject(component);
            var iterator = serializedObject.GetIterator();
            bool changed = false;

            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var sourceRef = iterator.objectReferenceValue;
                var remappedRef = TryRemapReference(sourceRef, oldRoot, newRoot, oldMap, newMap);
                if (remappedRef == null || remappedRef == sourceRef)
                    continue;

                iterator.objectReferenceValue = remappedRef;
                changed = true;
            }

            if (!changed)
                continue;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }
    }

    static UnityEngine.Object TryRemapReference(
        UnityEngine.Object sourceRef,
        Transform oldRoot,
        Transform newRoot,
        Dictionary<string, Transform> oldMap,
        Dictionary<string, Transform> newMap)
    {
        if (sourceRef == null || oldRoot == null || newRoot == null)
            return null;

        Transform sourceTransform = null;
        Type targetType = null;
        bool wantsGameObject = false;

        if (sourceRef is GameObject gameObject)
        {
            sourceTransform = gameObject.transform;
            wantsGameObject = true;
            targetType = typeof(GameObject);
        }
        else if (sourceRef is Transform transform)
        {
            sourceTransform = transform;
            targetType = typeof(Transform);
        }
        else if (sourceRef is Component component)
        {
            sourceTransform = component.transform;
            targetType = component.GetType();
        }

        if (sourceTransform == null)
            return null;

        if (sourceTransform != oldRoot && !sourceTransform.IsChildOf(oldRoot))
            return null;

        string path = GetRelativePath(oldRoot, sourceTransform);
        if (!newMap.TryGetValue(path, out var newTransform) || newTransform == null)
            return null;

        if (wantsGameObject)
            return newTransform.gameObject;

        if (targetType == typeof(Transform))
            return newTransform;

        var remappedComponent = newTransform.GetComponent(targetType);
        if (remappedComponent != null)
            return remappedComponent;

        return newTransform.gameObject;
    }
}
