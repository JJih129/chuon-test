#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SwordTrailStaticInstaller
{
    const string PlayerPrefabPath = "Assets/Prefabs/Generated/PlayerRoot.prefab";
    const string PlayerSlashPrefabPath = "Assets/Free Slash VFX/Prefabs/Slash Fire VFX.prefab";
    const string BossSlashPrefabPath = "Assets/Free Slash VFX/Prefabs/Slash Eletric VFX.prefab";

    static readonly string[] ScenePaths =
    {
        "Assets/Scenes/Tutorial.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/MainScene.unity",
    };

    [MenuItem("Tools/ChuOn/VFX/Install Static Sword Trails")]
    public static void InstallAll()
    {
        InstallAllInternal(promptForUnsavedScenes: true);
    }

    public static void InstallAllFromAutomation()
    {
        InstallAllInternal(promptForUnsavedScenes: false);
    }

    static void InstallAllInternal(bool promptForUnsavedScenes)
    {
        if (promptForUnsavedScenes && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[SwordTrailStaticInstaller] Install canceled to preserve unsaved scene changes.");
            return;
        }

        if (!promptForUnsavedScenes)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        string activeScenePath = SceneManager.GetActiveScene().path;

        InstallPlayerPrefab();

        for (int i = 0; i < ScenePaths.Length; i++)
        {
            string scenePath = ScenePaths[i];
            if (!File.Exists(scenePath))
                continue;

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            bool changed = InstallInOpenScene();
            if (changed)
                EditorSceneManager.SaveScene(scene);
        }

        if (!string.IsNullOrEmpty(activeScenePath) && File.Exists(activeScenePath))
            EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SwordTrailStaticInstaller] Static sword trails installed.");
    }

    static void InstallPlayerPrefab()
    {
        if (!File.Exists(PlayerPrefabPath))
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        bool changed = InstallPlayer(root);
        if (changed)
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);

        PrefabUtility.UnloadPrefabContents(root);
    }

    static bool InstallInOpenScene()
    {
        bool changed = false;

        PlayerReferences[] players = Object.FindObjectsOfType<PlayerReferences>(true);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
                changed |= InstallPlayer(players[i].gameObject);
        }

        BossAttackVfxPresenter[] bosses = Object.FindObjectsOfType<BossAttackVfxPresenter>(true);
        for (int i = 0; i < bosses.Length; i++)
        {
            if (bosses[i] != null)
                changed |= InstallBoss(bosses[i]);
        }

        return changed;
    }

    static bool InstallPlayer(GameObject owner)
    {
        if (owner == null)
            return false;

        bool changed = false;
        PlayerReferences references = owner.GetComponent<PlayerReferences>() ?? owner.GetComponentInChildren<PlayerReferences>(true);
        Transform visualRoot = references != null && references.VisualRoot != null ? references.VisualRoot : FindChildRecursive(owner.transform, "PlayerVisual_Chuon");
        Transform sword = FindChildRecursive(visualRoot != null ? visualRoot : owner.transform, "Object002");
        if (sword == null)
            sword = FindChildRecursive(visualRoot != null ? visualRoot : owner.transform, "Object009");
        if (sword == null)
            return false;

        SwordTrailPoints points = EnsureTrailPoints(sword, "Player", ref changed);
        SwordTrailMeshRenderer trail = EnsureTrailRenderer(owner.transform, "PlayerSwordMotionTrail", 1.6f, 0.95f, 0.75f, ref changed);
        PlayerAttackVfxPresenter presenter = owner.GetComponent<PlayerAttackVfxPresenter>();
        if (presenter == null)
        {
            presenter = owner.AddComponent<PlayerAttackVfxPresenter>();
            changed = true;
        }

        SerializedObject serialized = new SerializedObject(presenter);
        changed |= SetObject(serialized, "swordMotionTrailVfxPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSlashPrefabPath));
        changed |= SetObject(serialized, "swordMotionTrail", trail);
        changed |= SetObject(serialized, "swordMotionTrailPoints", points);
        changed |= SetBool(serialized, "useSwordMotionMeshTrail", true);
        changed |= SetColor(serialized, "swordMotionTrailTint", new Color(1f, 0.08f, 0.03f, 1f));
        changed |= SetFloat(serialized, "swordMotionTrailMinSampleDistance", 0.012f);
        changed |= SetFloat(serialized, "swordMotionTrailLifeTime", 0.28f);
        changed |= SetInt(serialized, "swordMotionTrailMaxSamples", 56);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(owner);
        return changed;
    }

    static bool InstallBoss(BossAttackVfxPresenter presenter)
    {
        if (presenter == null)
            return false;

        bool changed = false;
        BossReferences references = presenter.GetComponent<BossReferences>() ?? presenter.GetComponentInChildren<BossReferences>(true);
        Transform visualRoot = references != null && references.VisualRoot != null ? references.VisualRoot : presenter.transform;
        Transform sword = references != null && references.AttackHitboxSourceVisual != null
            ? references.AttackHitboxSourceVisual
            : FindChildRecursive(visualRoot, "Object002");
        if (sword == null)
            sword = FindChildRecursive(visualRoot, "Object009");
        if (sword == null)
            return false;

        SwordTrailPoints points = EnsureTrailPoints(sword, "Boss", ref changed);
        SwordTrailMeshRenderer trail = EnsureTrailRenderer(presenter.transform, "BossSwordMotionTrail", 1.5f, 1.05f, 0.7f, ref changed);

        SerializedObject serialized = new SerializedObject(presenter);
        changed |= SetObject(serialized, "swordMotionTrailVfxPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(BossSlashPrefabPath));
        changed |= SetObject(serialized, "swordMotionTrail", trail);
        changed |= SetObject(serialized, "swordMotionTrailPoints", points);
        changed |= SetBool(serialized, "useSwordMotionMeshTrail", true);
        changed |= SetColor(serialized, "swordMotionTrailTint", new Color(0.1f, 0.75f, 1f, 1f));
        changed |= SetFloat(serialized, "swordMotionTrailMinSampleDistance", 0.015f);
        changed |= SetFloat(serialized, "swordMotionTrailLifeTime", 0.32f);
        changed |= SetInt(serialized, "swordMotionTrailMaxSamples", 64);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(presenter);
        return changed;
    }

    static SwordTrailPoints EnsureTrailPoints(Transform sword, string prefix, ref bool changed)
    {
        Transform basePoint = EnsureChild(sword, prefix + "_TrailBase", ref changed);
        Transform tipPoint = EnsureChild(sword, prefix + "_TrailTip", ref changed);

        Bounds bounds;
        if (TryGetLocalMeshBounds(sword, out bounds))
        {
            basePoint.localPosition = ResolveBladePoint(bounds, false);
            tipPoint.localPosition = ResolveBladePoint(bounds, true);
            changed = true;
        }

        SwordTrailPoints points = sword.GetComponent<SwordTrailPoints>();
        if (points == null)
        {
            points = sword.gameObject.AddComponent<SwordTrailPoints>();
            changed = true;
        }

        SerializedObject serialized = new SerializedObject(points);
        changed |= SetObject(serialized, "trailBase", basePoint);
        changed |= SetObject(serialized, "trailTip", tipPoint);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(points);
        return points;
    }

    static SwordTrailMeshRenderer EnsureTrailRenderer(Transform owner, string name, float widthMultiplier, float minimumWidth, float cameraBlend, ref bool changed)
    {
        Transform existing = owner.Find(name);
        if (existing == null)
        {
            GameObject trailObject = new GameObject(name);
            existing = trailObject.transform;
            existing.SetParent(owner, false);
            existing.localPosition = Vector3.zero;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            changed = true;
        }

        MeshFilter meshFilter = existing.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = existing.gameObject.AddComponent<MeshFilter>();
            changed = true;
        }

        MeshRenderer meshRenderer = existing.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = existing.gameObject.AddComponent<MeshRenderer>();
            changed = true;
        }

        SwordTrailMeshRenderer trail = existing.GetComponent<SwordTrailMeshRenderer>();
        if (trail == null)
        {
            trail = existing.gameObject.AddComponent<SwordTrailMeshRenderer>();
            changed = true;
        }

        EditorUtility.SetDirty(meshFilter);
        EditorUtility.SetDirty(meshRenderer);

        SerializedObject serialized = new SerializedObject(trail);
        changed |= SetFloat(serialized, "visualWidthMultiplier", widthMultiplier);
        changed |= SetFloat(serialized, "minimumVisualWidth", minimumWidth);
        changed |= SetFloat(serialized, "cameraFacingBlend", cameraBlend);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(trail);
        return trail;
    }

    static Transform EnsureChild(Transform parent, string name, ref bool changed)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        GameObject childObject = new GameObject(name);
        child = childObject.transform;
        child.SetParent(parent, false);
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        changed = true;
        return child;
    }

    static Vector3 ResolveBladePoint(Bounds bounds, bool tip)
    {
        Vector3 size = bounds.size;
        int axis = 0;
        if (size.y > size.x && size.y >= size.z)
            axis = 1;
        else if (size.z > size.x && size.z >= size.y)
            axis = 2;

        Vector3 point = bounds.center;
        if (axis == 1)
            point.y = tip ? bounds.max.y : bounds.min.y;
        else if (axis == 2)
            point.z = tip ? bounds.max.z : bounds.min.z;
        else
            point.x = tip ? bounds.max.x : bounds.min.x;

        return point;
    }

    static bool TryGetLocalMeshBounds(Transform target, out Bounds bounds)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (string.Equals(root.name, name, System.StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static bool SetObject(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == value)
            return false;

        property.objectReferenceValue = value;
        return true;
    }

    static bool SetBool(SerializedObject serialized, string propertyName, bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || property.boolValue == value)
            return false;

        property.boolValue = value;
        return true;
    }

    static bool SetColor(SerializedObject serialized, string propertyName, Color value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || property.colorValue == value)
            return false;

        property.colorValue = value;
        return true;
    }

    static bool SetFloat(SerializedObject serialized, string propertyName, float value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || Mathf.Approximately(property.floatValue, value))
            return false;

        property.floatValue = value;
        return true;
    }

    static bool SetInt(SerializedObject serialized, string propertyName, int value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || property.intValue == value)
            return false;

        property.intValue = value;
        return true;
    }
}
#endif
