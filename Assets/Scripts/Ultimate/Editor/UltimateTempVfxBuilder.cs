using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UltimateTempVfxBuilder
{
    const string RootFolder = "Assets/Prefabs/Generated/UltimateTempVfx";
    const string MaterialFolder = RootFolder + "/Materials";
    const string SequenceAssetPath = "Assets/Resources/Ultimate/UltimateSequence_Default.asset";
    const string AutoBuildPreferenceKey = "ChuOn.UltimateTempVfxBuilder.AutoBuildPending.v2";

    [InitializeOnLoadMethod]
    static void AutoBuildOnLoad()
    {
        EditorApplication.delayCall += TryAutoBuild;
    }

    [MenuItem("Tools/Ultimate/Build Temporary Ultimate VFX")]
    public static void BuildAndAssign()
    {
        EnsureFolders();

        Material cyan = CreateParticleMaterial(MaterialFolder + "/UltimateTemp_Cyan.mat", new Color(0.15f, 0.9f, 1f, 1f));
        Material white = CreateParticleMaterial(MaterialFolder + "/UltimateTemp_White.mat", new Color(1f, 1f, 1f, 1f));
        Material red = CreateParticleMaterial(MaterialFolder + "/UltimateTemp_Red.mat", new Color(1f, 0.3f, 0.45f, 1f));
        Material amber = CreateParticleMaterial(MaterialFolder + "/UltimateTemp_Amber.mat", new Color(1f, 0.72f, 0.25f, 1f));
        Material beamOuter = CreateBeamMaterial(MaterialFolder + "/UltimateTemp_BeamOuter.mat", new Color(0.15f, 0.9f, 1f, 0.22f));
        Material beamInner = CreateBeamMaterial(MaterialFolder + "/UltimateTemp_BeamInner.mat", new Color(1f, 1f, 1f, 0.72f));

        GameObject intro = BuildIntroPrefab(RootFolder + "/UltimateTemp_Intro.prefab", cyan, white);
        GameObject dash = BuildDashSlashPrefab(RootFolder + "/UltimateTemp_DashSlash.prefab", red, white);
        GameObject lightBeam = BuildLightBeamPrefab(RootFolder + "/UltimateTemp_LightBeam.prefab", beamOuter, beamInner);
        GameObject crack = BuildCrackPrefab(RootFolder + "/UltimateTemp_Crack.prefab", cyan, white);
        GameObject explosion = BuildExplosionPrefab(RootFolder + "/UltimateTemp_Explosion.prefab", red, amber, white);
        GameObject shard = BuildShardPrefab(RootFolder + "/UltimateTemp_Shard.prefab", cyan, white);
        GameObject walkout = BuildWalkoutPrefab(RootFolder + "/UltimateTemp_Walkout.prefab", white, amber);

        AssignToSequenceAsset(intro, dash, lightBeam, crack, explosion, shard, walkout);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorPrefs.SetBool(AutoBuildPreferenceKey, false);

        Debug.Log("[UltimateTempVfxBuilder] Built temporary ultimate VFX prefabs and assigned them to UltimateSequence_Default.");
    }

    static void TryAutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (!EditorPrefs.GetBool(AutoBuildPreferenceKey, true))
            return;
        if (HasGeneratedAssignedSequence())
        {
            EditorPrefs.SetBool(AutoBuildPreferenceKey, false);
            return;
        }

        BuildAndAssign();
    }

    static bool HasGeneratedAssignedSequence()
    {
        UltimateSequenceData sequence = AssetDatabase.LoadAssetAtPath<UltimateSequenceData>(SequenceAssetPath);
        if (sequence == null)
            return false;

        SerializedObject serialized = new SerializedObject(sequence);
        SerializedProperty vfx = serialized.FindProperty("vfx");
        if (vfx == null)
            return false;

        return IsGeneratedPrefab(vfx.FindPropertyRelative("dashSlashVfxPrefab")?.objectReferenceValue)
            && IsGeneratedPrefab(vfx.FindPropertyRelative("lightBeamVfxPrefab")?.objectReferenceValue)
            && IsGeneratedPrefab(vfx.FindPropertyRelative("crackWorldVfxPrefab")?.objectReferenceValue)
            && IsGeneratedPrefab(vfx.FindPropertyRelative("explosionVfxPrefab")?.objectReferenceValue)
            && IsGeneratedPrefab(vfx.FindPropertyRelative("shardVfxPrefab")?.objectReferenceValue);
    }

    static bool IsGeneratedPrefab(Object reference)
    {
        if (reference == null)
            return false;

        string path = AssetDatabase.GetAssetPath(reference);
        return !string.IsNullOrEmpty(path) && path.StartsWith(RootFolder, System.StringComparison.OrdinalIgnoreCase);
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/Generated");
        EnsureFolder(RootFolder);
        EnsureFolder(MaterialFolder);
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string parent = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(assetPath);
        if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent))
            AssetDatabase.CreateFolder(parent, name);
    }

    static Material CreateParticleMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            ApplyColor(existing, color);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Unlit/Color");

        Material material = new Material(shader)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path)
        };
        ApplyColor(material, color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Material CreateBeamMaterial(string path, Color color)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            ApplyColor(existing, color);
            ConfigureTransparentMaterial(existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");

        Material material = new Material(shader)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(path)
        };
        ApplyColor(material, color);
        ConfigureTransparentMaterial(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void ApplyColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * 1.25f);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_Mode"))
            material.SetFloat("_Mode", 2f);
    }

    static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
    }

    static GameObject BuildIntroPrefab(string path, Material cyan, Material white)
    {
        GameObject root = new GameObject("UltimateTemp_Intro");
        CreateParticleChild(root.transform, "Aura", cyan, new Vector3(0f, 0.9f, 0f), ParticleSystemShapeType.Circle, 0.85f, 16, 1.1f, 0.35f, 0.95f, 0.15f, 2.2f, true);
        CreateParticleChild(root.transform, "Spark", white, new Vector3(0f, 0.9f, 0f), ParticleSystemShapeType.Sphere, 0.35f, 22, 1.25f, 0.06f, 0.28f, 1.5f, 0f, false);
        return SavePrefab(path, root);
    }

    static GameObject BuildDashSlashPrefab(string path, Material red, Material white)
    {
        GameObject root = new GameObject("UltimateTemp_DashSlash");
        GameObject streak = CreateParticleChild(root.transform, "Streak", red, Vector3.zero, ParticleSystemShapeType.Cone, 0.04f, 28, 0.35f, 0.08f, 1.45f, 16f, 0f, false);
        ConfigureRenderer(streak, ParticleSystemRenderMode.Stretch, 0.18f, 1.4f, red);
        GameObject flash = CreateParticleChild(root.transform, "Flash", white, Vector3.zero, ParticleSystemShapeType.Sphere, 0.08f, 12, 0.22f, 0.18f, 0.55f, 0f, 0f, false);
        ConfigureRenderer(flash, ParticleSystemRenderMode.Billboard, 0.4f, 0.4f, white);
        return SavePrefab(path, root);
    }

    static GameObject BuildLightBeamPrefab(string path, Material outer, Material inner)
    {
        GameObject root = new GameObject("UltimateTemp_LightBeam");
        CreateBeamMeshChild(root.transform, "BeamOuter", outer, new Vector3(0f, 0f, 0.5f), new Vector3(0.32f, 0.11f, 1f), Quaternion.identity);
        CreateBeamMeshChild(root.transform, "BeamCore", inner, new Vector3(0f, 0f, 0.5f), new Vector3(0.18f, 0.055f, 1f), Quaternion.identity);
        return SavePrefab(path, root);
    }

    static GameObject BuildCrackPrefab(string path, Material cyan, Material white)
    {
        GameObject root = new GameObject("UltimateTemp_Crack");
        GameObject ring = CreateParticleChild(root.transform, "Ring", cyan, Vector3.zero, ParticleSystemShapeType.Circle, 0.12f, 24, 0.55f, 0.12f, 0.8f, 6f, 0f, false);
        ConfigureRenderer(ring, ParticleSystemRenderMode.Billboard, 0.55f, 0.55f, cyan);
        GameObject shards = CreateParticleChild(root.transform, "ShardBurst", white, Vector3.zero, ParticleSystemShapeType.Circle, 0.2f, 36, 0.75f, 0.05f, 0.22f, 9f, 0f, false);
        ConfigureRenderer(shards, ParticleSystemRenderMode.Stretch, 0.1f, 1.1f, white);
        return SavePrefab(path, root);
    }

    static GameObject BuildExplosionPrefab(string path, Material red, Material amber, Material white)
    {
        GameObject root = new GameObject("UltimateTemp_Explosion");
        GameObject core = CreateParticleChild(root.transform, "Core", amber, Vector3.zero, ParticleSystemShapeType.Sphere, 0.15f, 18, 0.45f, 0.28f, 1.25f, 1.5f, 0f, false);
        ConfigureRenderer(core, ParticleSystemRenderMode.Billboard, 0.8f, 0.8f, amber);
        GameObject sparks = CreateParticleChild(root.transform, "Sparks", red, Vector3.zero, ParticleSystemShapeType.Sphere, 0.2f, 40, 0.55f, 0.06f, 0.26f, 11f, 0f, false);
        ConfigureRenderer(sparks, ParticleSystemRenderMode.Stretch, 0.12f, 1.25f, red);
        GameObject shock = CreateParticleChild(root.transform, "ShockRing", white, Vector3.zero, ParticleSystemShapeType.Circle, 0.12f, 20, 0.4f, 0.18f, 0.95f, 8f, 0f, false);
        ConfigureRenderer(shock, ParticleSystemRenderMode.Billboard, 0.7f, 0.7f, white);
        return SavePrefab(path, root);
    }

    static GameObject BuildShardPrefab(string path, Material cyan, Material white)
    {
        GameObject root = new GameObject("UltimateTemp_Shard");
        GameObject shard = CreateParticleChild(root.transform, "Shard", cyan, Vector3.zero, ParticleSystemShapeType.Sphere, 0.03f, 8, 0.5f, 0.05f, 0.14f, 4.5f, 0f, false);
        ConfigureRenderer(shard, ParticleSystemRenderMode.Stretch, 0.06f, 0.7f, cyan);
        GameObject sparkle = CreateParticleChild(root.transform, "Sparkle", white, Vector3.zero, ParticleSystemShapeType.Sphere, 0.02f, 4, 0.32f, 0.03f, 0.08f, 0.2f, 0f, false);
        ConfigureRenderer(sparkle, ParticleSystemRenderMode.Billboard, 0.18f, 0.18f, white);
        return SavePrefab(path, root);
    }

    static GameObject BuildWalkoutPrefab(string path, Material white, Material amber)
    {
        GameObject root = new GameObject("UltimateTemp_Walkout");
        GameObject ember = CreateParticleChild(root.transform, "EmberTrail", amber, new Vector3(0f, 0.7f, 0f), ParticleSystemShapeType.Cone, 0.15f, 12, 0.9f, 0.04f, 0.12f, 0.5f, 0f, true);
        ConfigureRenderer(ember, ParticleSystemRenderMode.Stretch, 0.08f, 0.4f, amber);
        GameObject glow = CreateParticleChild(root.transform, "SoftGlow", white, new Vector3(0f, 0.8f, 0f), ParticleSystemShapeType.Sphere, 0.12f, 10, 0.7f, 0.18f, 0.42f, 0.1f, 0f, true);
        ConfigureRenderer(glow, ParticleSystemRenderMode.Billboard, 0.35f, 0.35f, white);
        return SavePrefab(path, root);
    }

    static GameObject CreateParticleChild(Transform parent, string name, Material material, Vector3 localPosition,
        ParticleSystemShapeType shapeType, float radius, short burstCount, float duration, float startSizeMin, float startSizeMax,
        float startSpeed, float emissionRate, bool loop)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;

        var main = ps.main;
        main.loop = loop;
        main.playOnAwake = false;
        main.duration = duration;
        main.startLifetime = new ParticleSystem.MinMaxCurve(duration * 0.65f, duration * 1.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(startSizeMin, startSizeMax);
        main.startSpeed = startSpeed;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(8, burstCount * 3);

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = loop ? emissionRate : 0f;
        if (!loop)
        {
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, burstCount)
            });
        }

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = shapeType;
        shape.radius = radius;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        Color baseColor = ResolveColor(material);
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(baseColor, 0f),
                new GradientColorKey(baseColor * 0.9f, 0.75f),
                new GradientColorKey(baseColor * 0.6f, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.08f),
                new GradientAlphaKey(0.75f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.45f),
            new Keyframe(0.2f, 1f),
            new Keyframe(1f, 0.15f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = loop ? 0.12f : 0.25f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.15f;

        return go;
    }

    static GameObject CreateBeamMeshChild(Transform parent, string name, Material material, Vector3 localPosition, Vector3 localScale, Quaternion localRotation)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;
        go.transform.localScale = localScale;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        return go;
    }

    static void ConfigureRenderer(GameObject go, ParticleSystemRenderMode renderMode, float minSize, float maxSize, Material material)
    {
        if (go == null)
            return;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return;

        renderer.renderMode = renderMode;
        renderer.minParticleSize = minSize;
        renderer.maxParticleSize = maxSize;
        renderer.sharedMaterial = material;
    }

    static GameObject SavePrefab(string path, GameObject root)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject prefab = existing != null
            ? PrefabUtility.SaveAsPrefabAsset(root, path)
            : PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void AssignToSequenceAsset(GameObject intro, GameObject dash, GameObject lightBeam, GameObject crack, GameObject explosion, GameObject shard, GameObject walkout)
    {
        UltimateSequenceData sequence = AssetDatabase.LoadAssetAtPath<UltimateSequenceData>(SequenceAssetPath);
        if (sequence == null)
        {
            Debug.LogWarning("[UltimateTempVfxBuilder] UltimateSequence_Default.asset not found. Prefabs were generated but not assigned.");
            return;
        }

        SerializedObject serialized = new SerializedObject(sequence);
        SerializedProperty vfx = serialized.FindProperty("vfx");
        if (vfx == null)
            return;

        vfx.FindPropertyRelative("introPoseVfxPrefab").objectReferenceValue = intro;
        vfx.FindPropertyRelative("dashSlashVfxPrefab").objectReferenceValue = dash;
        vfx.FindPropertyRelative("lightBeamVfxPrefab").objectReferenceValue = lightBeam;
        vfx.FindPropertyRelative("crackWorldVfxPrefab").objectReferenceValue = crack;
        vfx.FindPropertyRelative("crackScreenVfxPrefab").objectReferenceValue = null;
        vfx.FindPropertyRelative("explosionVfxPrefab").objectReferenceValue = explosion;
        vfx.FindPropertyRelative("shardVfxPrefab").objectReferenceValue = shard;
        vfx.FindPropertyRelative("walkoutVfxPrefab").objectReferenceValue = walkout;
        vfx.FindPropertyRelative("useScreenSpaceCrack").boolValue = false;
        vfx.FindPropertyRelative("useAfterImageStyleSlash").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sequence);
    }

    static Color ResolveColor(Material material)
    {
        if (material != null)
        {
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
        }
        return Color.white;
    }
}
