using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CreateTutorialCombatGirlEnemy
{
    const string OutputFolder = "Assets/Prefabs/Tutorial";
    const string MaterialFolder = OutputFolder + "/Materials";
    const string ControllerPath = OutputFolder + "/TutorialCombatGirlEnemy.controller";
    const string PrefabPath = OutputFolder + "/TutorialCombatGirlEnemy.prefab";
    const string SourceVisualPath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Prefab/School_Katana_FullBody-Magica cloth2.prefab";
    const string IdlePath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Normal/Idle.fbx";
    const string RunPath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Special/Sp_Run.fbx";
    const string AttackPath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Special/Sp_Skill3.fbx";
    const string HitPath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Normal/Hit1.fbx";
    const string DiePath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Animations/Normal/Die.fbx";
    const string AutoCreateSessionKey = "CreateTutorialCombatGirlEnemy.AutoCreate.v8";

    [InitializeOnLoadMethod]
    static void AutoCreateOnce()
    {
        if (SessionState.GetBool(AutoCreateSessionKey, false))
            return;

        SessionState.SetBool(AutoCreateSessionKey, true);
        EditorApplication.delayCall += () =>
        {
            if (!AssetDatabase.IsValidFolder("Assets"))
                return;

            Create();
        };
    }

    [MenuItem("Tools/Tutorial/Create CombatGirl Light Enemy")]
    public static void Create()
    {
        EnsureFolder(OutputFolder);

        AnimatorController controller = BuildController();
        GameObject sourceVisual = AssetDatabase.LoadAssetAtPath<GameObject>(SourceVisualPath);
        if (sourceVisual == null || controller == null)
        {
            Debug.LogWarning("[TutorialEnemy] CombatGirls source assets are missing.");
            return;
        }

        GameObject root = new GameObject("TutorialCombatGirlEnemy");
        try
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            root.layer = enemyLayer >= 0 ? enemyLayer : 12;
            TrySetTag(root, "Enemy");

            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.95f, 0f);
            capsule.height = 1.9f;
            capsule.radius = 0.38f;

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(root.transform, false);

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(sourceVisual, visualRoot.transform);
            visual.name = "CombatGirlVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            SetLayerRecursively(visual, root.layer);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(visual);
            RemoveMissingScriptsInChildren(visual);

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null)
                animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            Transform lockPivot = CreateChild(root.transform, "LockPivot", new Vector3(0f, 1.35f, 0f));
            Transform firePoint = CreateChild(root.transform, "FirePoint", new Vector3(0f, 1.05f, 0.85f));

            Renderer renderer = visual.GetComponentInChildren<Renderer>(true);
            ApplyUrpLitMaterials(visual);
            AlignVisualFeetToRoot(visualRoot.transform, visual);

            TutorialEnemyVisualRig rig = root.AddComponent<TutorialEnemyVisualRig>();
            SerializedObject rigSo = new SerializedObject(rig);
            rigSo.FindProperty("visualRoot").objectReferenceValue = visualRoot.transform;
            rigSo.FindProperty("primaryRenderer").objectReferenceValue = renderer;
            rigSo.FindProperty("lockPivot").objectReferenceValue = lockPivot;
            rigSo.FindProperty("firePoint").objectReferenceValue = firePoint;
            rigSo.FindProperty("instantiateVisualPrefabAtRuntime").boolValue = false;
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            TrainingDummyController dummy = root.AddComponent<TrainingDummyController>();
            SerializedObject dummySo = new SerializedObject(dummy);
            dummySo.FindProperty("visualAnimator").objectReferenceValue = animator;
            dummySo.FindProperty("primaryRenderer").objectReferenceValue = renderer;
            dummySo.FindProperty("attackOrigin").objectReferenceValue = firePoint;
            dummySo.FindProperty("projectileSpawnPoint").objectReferenceValue = firePoint;
            dummySo.FindProperty("role").enumValueIndex = (int)TutorialDummyRole.GuardParry;
            dummySo.FindProperty("desiredMeleeDistance").floatValue = 2.3f;
            dummySo.FindProperty("chaseMoveSpeed").floatValue = 2.65f;
            dummySo.FindProperty("meleeHitDelay").floatValue = 0.42f;
            dummySo.FindProperty("meleeHitWindow").floatValue = 0.32f;
            dummySo.FindProperty("useAnimationHitTiming").boolValue = true;
            dummySo.FindProperty("meleeHitNormalizedTime").floatValue = 0.16f;
            dummySo.FindProperty("maxAnimationHitWaitSeconds").floatValue = 2.2f;
            dummySo.FindProperty("useAttackLunge").boolValue = true;
            dummySo.FindProperty("attackLungeStartNormalizedTime").floatValue = 0.04f;
            dummySo.FindProperty("attackLungeEndNormalizedTime").floatValue = 0.30f;
            dummySo.FindProperty("attackLungeSpeed").floatValue = 15f;
            dummySo.FindProperty("attackLungeStopDistance").floatValue = 1.25f;
            dummySo.FindProperty("attackLungeMaxDistance").floatValue = 7.5f;
            dummySo.FindProperty("parryAssistLeadSeconds").floatValue = 0.05f;
            dummySo.FindProperty("freezeForFirstParrySuccess").boolValue = true;
            dummySo.FindProperty("forcedParryFreezeNormalizedTime").floatValue = 0.12f;
            dummySo.FindProperty("forcedParryTimeScale").floatValue = 0.02f;
            dummySo.FindProperty("forcedParryWindowSeconds").floatValue = 0.75f;
            dummySo.FindProperty("meleeHitboxCenter").vector3Value = new Vector3(0f, 1.05f, 1.28f);
            dummySo.FindProperty("meleeHitboxSize").vector3Value = new Vector3(1.35f, 1.35f, 1.85f);
            ConfigureProfiles(dummySo.FindProperty("stepProfiles"));
            dummySo.ApplyModifiedPropertiesWithoutUndo();

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                AssetDatabase.DeleteAsset(PrefabPath);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TutorialEnemy] Created {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static AnimatorController BuildController()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idle = AddState(sm, "Idle", LoadClip(IdlePath), new Vector3(240f, 120f, 0f));
        AnimatorState run = AddState(sm, "Run", LoadClip(RunPath), new Vector3(240f, 220f, 0f));
        AnimatorState attack = AddState(sm, "Attack1", LoadClip(AttackPath), new Vector3(240f, 320f, 0f));
        AnimatorState hit = AddState(sm, "Hit1", LoadClip(HitPath), new Vector3(240f, 420f, 0f));
        AnimatorState die = AddState(sm, "Die", LoadClip(DiePath), new Vector3(240f, 520f, 0f));

        sm.defaultState = idle;
        run.speed = 1.05f;
        AddReturnTransition(attack, idle, 0.92f);
        AddReturnTransition(hit, idle, 0.88f);
        die.speed = 1f;
        return controller;
    }

    static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
    {
        AnimatorState state = sm.AddState(name, position);
        state.motion = motion;
        state.writeDefaultValues = true;
        return state;
    }

    static void AddReturnTransition(AnimatorState state, AnimatorState idle, float exitTime)
    {
        AnimatorStateTransition transition = state.AddTransition(idle);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.duration = 0.08f;
    }

    static AnimationClip LoadClip(string path)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview"))
                return clip;
        }

        return null;
    }

    static void ConfigureProfiles(SerializedProperty profiles)
    {
        profiles.arraySize = 6;
        SetProfile(profiles.GetArrayElementAtIndex(0), TutorialStepType.LockOn, false, false, false, false, 0.8f, 2.4f, 1.0f);
        SetProfile(profiles.GetArrayElementAtIndex(1), TutorialStepType.BasicAttack, false, false, false, false, 0.8f, 2.4f, 1.0f);
        SetProfile(profiles.GetArrayElementAtIndex(2), TutorialStepType.Combo, false, false, false, false, 0.8f, 2.4f, 1.0f);
        SetProfile(profiles.GetArrayElementAtIndex(3), TutorialStepType.Guard, true, false, false, false, 0.9f, 2.6f, 1.0f);
        SetProfile(profiles.GetArrayElementAtIndex(4), TutorialStepType.Parry, true, true, false, false, 0.85f, 2.7f, 1.0f);
        SetProfile(profiles.GetArrayElementAtIndex(5), TutorialStepType.PerfectDodge, true, false, true, true, 0.85f, 2.7f, 0.95f);
    }

    static void SetProfile(
        SerializedProperty profile,
        TutorialStepType stepType,
        bool loopAttack,
        bool canParry,
        bool canPerfectDodge,
        bool unblockable,
        float initialDelay,
        float interval,
        float telegraph)
    {
        profile.FindPropertyRelative("stepType").enumValueIndex = (int)stepType;
        profile.FindPropertyRelative("active").boolValue = true;
        profile.FindPropertyRelative("loopAttack").boolValue = loopAttack;
        profile.FindPropertyRelative("useProjectileAttack").boolValue = false;
        profile.FindPropertyRelative("initialDelay").floatValue = initialDelay;
        profile.FindPropertyRelative("attackInterval").floatValue = interval;
        profile.FindPropertyRelative("telegraphDuration").floatValue = telegraph;
        profile.FindPropertyRelative("damage").floatValue = 8f;
        profile.FindPropertyRelative("hitRange").floatValue = 3.2f;
        profile.FindPropertyRelative("canParry").boolValue = canParry;
        profile.FindPropertyRelative("canPerfectDodge").boolValue = canPerfectDodge;
        profile.FindPropertyRelative("unblockable").boolValue = unblockable;
        profile.FindPropertyRelative("invulnerable").boolValue = true;
        profile.FindPropertyRelative("stateColor").colorValue = loopAttack ? new Color(1f, 0.42f, 0.18f, 1f) : new Color(0.25f, 0.85f, 1f, 1f);
        profile.FindPropertyRelative("dangerIndicatorColor").colorValue = new Color(1f, 0.28f, 0.12f, 0.95f);
        profile.FindPropertyRelative("dangerIndicatorWidth").floatValue = 0.2f;
        profile.FindPropertyRelative("targetMarkerColor").colorValue = new Color(1f, 0.22f, 0.12f, 0.72f);
        profile.FindPropertyRelative("targetMarkerSize").floatValue = 0.62f;
        profile.FindPropertyRelative("enableAdaptiveAssist").boolValue = true;
    }

    static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child.transform;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string name = Path.GetFileName(folder);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static void ApplyUrpLitMaterials(GameObject visual)
    {
        EnsureFolder(MaterialFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
        if (shader == null)
            return;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            bool changed = false;
            for (int j = 0; j < materials.Length; j++)
            {
                Material source = materials[j];
                Material converted = CreateConvertedMaterial(source, renderer.name, shader);
                if (converted != null)
                {
                    materials[j] = converted;
                    changed = true;
                }
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    static void AlignVisualFeetToRoot(Transform visualRoot, GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        if (!hasBounds)
            return;

        float bottomOffset = bounds.min.y - visualRoot.position.y;
        visualRoot.localPosition -= Vector3.up * bottomOffset;
    }

    static Material CreateConvertedMaterial(Material source, string rendererName, Shader shader)
    {
        string sourceName = source != null ? source.name : rendererName;
        string safeName = MakeSafeFileName("TutorialCombatGirl_" + sourceName);
        string path = MaterialFolder + "/" + safeName + ".mat";

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        Texture mainTexture = ResolveMainTexture(source, rendererName);
        if (mainTexture != null)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", mainTexture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", mainTexture);
        }

        Color color = Color.white;
        if (source != null)
        {
            if (source.HasProperty("_BaseColor"))
                color = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color"))
                color = source.GetColor("_Color");
        }

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture ResolveMainTexture(Material source, string rendererName)
    {
        if (source != null)
        {
            string[] textureProperties = source.GetTexturePropertyNames();
            for (int i = 0; i < textureProperties.Length; i++)
            {
                Texture texture = source.GetTexture(textureProperties[i]);
                if (texture != null && IsLikelyAlbedo(texture.name))
                    return texture;
            }

            if (source.HasProperty("_BaseMap"))
            {
                Texture texture = source.GetTexture("_BaseMap");
                if (texture != null)
                    return texture;
            }

            if (source.HasProperty("_MainTex"))
            {
                Texture texture = source.GetTexture("_MainTex");
                if (texture != null)
                    return texture;
            }
        }

        return FindTextureByName(rendererName) ?? FindTextureByName(source != null ? source.name : null);
    }

    static Texture FindTextureByName(string hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return null;

        string lower = hint.ToLowerInvariant();
        string query = null;
        if (lower.Contains("body")) query = "Body";
        else if (lower.Contains("face")) query = "Face";
        else if (lower.Contains("eye")) query = "Eye";
        else if (lower.Contains("hair")) query = "Hair";
        else if (lower.Contains("cloth") || lower.Contains("uniform")) query = "Body";
        else if (lower.Contains("weapon") || lower.Contains("katana")) query = "Weapon";

        if (string.IsNullOrEmpty(query))
            return null;

        string[] guids = AssetDatabase.FindAssets(query + " t:Texture", new[]
        {
            "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/School_Katana_Girl/Texture"
        });

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            string file = Path.GetFileNameWithoutExtension(path);
            if (!IsLikelyAlbedo(file))
                continue;

            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (texture != null)
                return texture;
        }

        return null;
    }

    static bool IsLikelyAlbedo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string lower = name.ToLowerInvariant();
        return !lower.Contains("normal") &&
               !lower.Contains("mask") &&
               !lower.Contains("metal") &&
               !lower.Contains("smooth") &&
               !lower.Contains("ao");
    }

    static string MakeSafeFileName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Replace(' ', '_');
    }

    static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    static void RemoveMissingScriptsInChildren(GameObject root)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(children[i].gameObject);
        }
    }

    static void TrySetTag(GameObject obj, string tag)
    {
        try { obj.tag = tag; }
        catch { }
    }
}
