using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class AspToonMaterialToolWindow : EditorWindow
{
    private const string DefaultOutputFolder = "Assets/Materials/Boss/ASPToon";
    private const string DefaultRampPath = "Assets/ASP/Textures/5RampMap.png";

    private GameObject targetRoot;
    private Texture2D rampMap;
    private string outputFolder = DefaultOutputFolder;
    private float outlineWidth = 1.6f;
    private Color outlineColor = new Color(0.015f, 0.018f, 0.025f, 1f);
    private Color rimColor = new Color(0.45f, 0.8f, 1f, 1f);
    private bool saveScene = true;

    [MenuItem("Tools/Art/ASP Toon Material Tool")]
    public static void Open()
    {
        GetWindow<AspToonMaterialToolWindow>("ASP Toon Tool");
    }

    private void OnEnable()
    {
        rampMap = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultRampPath);
        targetRoot = Selection.activeGameObject != null ? Selection.activeGameObject : GameObject.Find("boss");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("ASP Toon Material Applier", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Duplicates source materials, converts the copies to ASP/Character, then assigns them to Mesh/SkinnedMesh renderers. Source materials are not overwritten.", MessageType.Info);

        targetRoot = (GameObject)EditorGUILayout.ObjectField("Target Root", targetRoot, typeof(GameObject), true);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        rampMap = (Texture2D)EditorGUILayout.ObjectField("Ramp Map", rampMap, typeof(Texture2D), false);
        outlineWidth = EditorGUILayout.Slider("Outline Width", outlineWidth, 0f, 8f);
        outlineColor = EditorGUILayout.ColorField("Outline Color", outlineColor);
        rimColor = EditorGUILayout.ColorField("Rim Color", rimColor);
        saveScene = EditorGUILayout.Toggle("Save Scene", saveScene);

        EditorGUILayout.Space(8f);

        using (new EditorGUI.DisabledScope(targetRoot == null))
        {
            if (GUILayout.Button("Apply To Target Root"))
            {
                Apply(targetRoot);
            }
        }

        if (GUILayout.Button("Find boss And Apply"))
        {
            GameObject boss = GameObject.Find("boss");
            if (boss == null)
            {
                Debug.LogError("[BossAspToon] GameObject named 'boss' was not found.");
                return;
            }

            targetRoot = boss;
            Apply(boss);
        }

        if (GUILayout.Button("Use Selected GameObject"))
        {
            targetRoot = Selection.activeGameObject;
        }
    }

    private void Apply(GameObject root)
    {
        var settings = new BossAspToonMaterialApplier.Settings
        {
            outputFolder = outputFolder,
            ramp = rampMap,
            outlineWidth = outlineWidth,
            outlineColor = outlineColor,
            rimColor = rimColor,
            saveScene = saveScene
        };

        BossAspToonMaterialApplier.ApplyToRoot(root, settings);
    }
}

public static class BossAspToonMaterialApplier
{
    public struct Settings
    {
        public string outputFolder;
        public Texture ramp;
        public float outlineWidth;
        public Color outlineColor;
        public Color rimColor;
        public bool saveScene;
    }

    private const string ShaderName = "ASP/Character";
    private const string DefaultOutputFolder = "Assets/Materials/Boss/ASPToon";
    private const string DefaultRampPath = "Assets/ASP/Textures/5RampMap.png";
    private const string OneShotMarkerPath = "Assets/Editor/ApplyBossAspToon.once";

    [InitializeOnLoadMethod]
    private static void ApplyIfRequested()
    {
        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), OneShotMarkerPath)))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            try
            {
                ApplyToActiveSceneBoss();
            }
            finally
            {
                AssetDatabase.DeleteAsset(OneShotMarkerPath);
                AssetDatabase.Refresh();
            }
        };
    }

    [MenuItem("Tools/Art/Apply ASP Toon Shader To Boss")]
    public static void ApplyToActiveSceneBoss()
    {
        GameObject boss = GameObject.Find("boss");
        if (boss == null)
        {
            Debug.LogError("[BossAspToon] GameObject named 'boss' was not found in the active scene.");
            return;
        }

        ApplyToRoot(boss, CreateDefaultSettings());
    }

    public static void ApplyToRoot(GameObject root, Settings settings)
    {
        if (root == null)
        {
            Debug.LogError("[BossAspToon] Target root is null.");
            return;
        }

        Shader aspShader = Shader.Find(ShaderName);
        if (aspShader == null)
        {
            Debug.LogError($"[BossAspToon] Shader not found: {ShaderName}");
            return;
        }

        settings = Normalize(settings);
        EnsureFolder(settings.outputFolder);

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        int rendererCount = 0;
        int materialCount = 0;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (ShouldSkipRenderer(renderer))
            {
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material source = materials[slot];
                if (source == null)
                {
                    continue;
                }

                Material target = CreateOrUpdateAspMaterial(aspShader, settings, source, root.name, renderer.name, rendererIndex, slot);
                if (target == null)
                {
                    continue;
                }

                materials[slot] = target;
                changed = true;
                materialCount++;
            }

            if (!changed)
            {
                continue;
            }

            Undo.RecordObject(renderer, "Apply ASP Toon Material");
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
            rendererCount++;
        }

        AssetDatabase.SaveAssets();

        if (settings.saveScene)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
                EditorSceneManager.SaveScene(activeScene);
            }
        }

        Debug.Log($"[BossAspToon] Applied {materialCount} ASP materials to {rendererCount} renderers under '{root.name}'.");
    }

    private static Settings CreateDefaultSettings()
    {
        return new Settings
        {
            outputFolder = DefaultOutputFolder,
            ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultRampPath),
            outlineWidth = 1.6f,
            outlineColor = new Color(0.015f, 0.018f, 0.025f, 1f),
            rimColor = new Color(0.45f, 0.8f, 1f, 1f),
            saveScene = true
        };
    }

    private static Settings Normalize(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.outputFolder))
        {
            settings.outputFolder = DefaultOutputFolder;
        }

        if (settings.ramp == null)
        {
            settings.ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultRampPath);
        }

        if (settings.outlineWidth <= 0f)
        {
            settings.outlineWidth = 1.6f;
        }

        if (settings.outlineColor.a <= 0f)
        {
            settings.outlineColor = new Color(0.015f, 0.018f, 0.025f, 1f);
        }

        if (settings.rimColor.a <= 0f)
        {
            settings.rimColor = new Color(0.45f, 0.8f, 1f, 1f);
        }

        return settings;
    }

    private static bool ShouldSkipRenderer(Renderer renderer)
    {
        return renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer;
    }

    private static Material CreateOrUpdateAspMaterial(
        Shader aspShader,
        Settings settings,
        Material source,
        string rootName,
        string rendererName,
        int rendererIndex,
        int slot)
    {
        string assetName = SafeAssetName($"{rootName}_{rendererIndex:00}_{slot:00}_{rendererName}_{source.name}_ASP") + ".mat";
        string path = $"{settings.outputFolder}/{assetName}";
        Material target = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (target == null)
        {
            target = new Material(aspShader);
            AssetDatabase.CreateAsset(target, path);
        }
        else
        {
            target.shader = aspShader;
        }

        CopyBaseSurface(source, target);
        CopyNormal(source, target);
        CopyEmission(source, target);
        ConfigureToonDefaults(target, settings);

        EditorUtility.SetDirty(target);
        return target;
    }

    private static void CopyBaseSurface(Material source, Material target)
    {
        Texture baseTexture = GetTexture(source, "_BaseMap");
        if (baseTexture == null) baseTexture = GetTexture(source, "_MainTex");
        if (baseTexture == null) baseTexture = GetTexture(source, "_BaseColorMap");
        if (baseTexture == null) baseTexture = GetTexture(source, "_AlbedoMap");

        if (baseTexture != null)
        {
            SetTexture(target, "_BaseMap", baseTexture);
            SetTexture(target, "_MainTex", baseTexture);
        }

        Color baseColor = Color.white;
        if (!TryGetColor(source, "_BaseColor", out baseColor))
        {
            TryGetColor(source, "_Color", out baseColor);
        }

        SetColor(target, "_BaseColor", baseColor);
        SetColor(target, "_Color", baseColor);

        float cutoff = GetFloat(source, "_Cutoff", 0.5f);
        bool alphaClip = source.IsKeywordEnabled("_ALPHATEST_ON") || GetFloat(source, "_AlphaClip", 0f) > 0.5f;
        SetFloat(target, "_AlphaClip", alphaClip ? 1f : 0f);
        SetFloat(target, "_Cutoff", cutoff);
        SetFloat(target, "_SurfaceType", 0f);
        SetFloat(target, "_Cull", 0f);

        if (alphaClip)
        {
            target.EnableKeyword("_ALPHATEST_ON");
        }
        else
        {
            target.DisableKeyword("_ALPHATEST_ON");
        }
    }

    private static void CopyNormal(Material source, Material target)
    {
        Texture normal = GetTexture(source, "_BumpMap");
        if (normal == null) normal = GetTexture(source, "_NormalMap");
        if (normal != null)
        {
            SetTexture(target, "_BumpMap", normal);
            SetFloat(target, "_BumpScale", GetFloat(source, "_BumpScale", 0.6f));
            target.EnableKeyword("_NORMALMAP");
        }
        else
        {
            target.DisableKeyword("_NORMALMAP");
        }
    }

    private static void CopyEmission(Material source, Material target)
    {
        Texture emission = GetTexture(source, "_EmissionMap");
        if (emission == null) emission = GetTexture(source, "_EmissiveColorMap");

        Color emissionColor;
        bool hasEmissionColor = TryGetColor(source, "_EmissionColor", out emissionColor);
        if (!hasEmissionColor)
        {
            hasEmissionColor = TryGetColor(source, "_EmissiveColor", out emissionColor);
        }

        bool useEmission = emission != null || (hasEmissionColor && emissionColor.maxColorComponent > 0.01f);
        SetFloat(target, "_EmissionToggle", useEmission ? 1f : 0f);
        if (emission != null)
        {
            SetTexture(target, "_EmissionMap", emission);
        }

        SetColor(target, "_EmissionColor", useEmission ? emissionColor : Color.black);
        if (useEmission)
        {
            target.EnableKeyword("_EMISSION");
        }
        else
        {
            target.DisableKeyword("_EMISSION");
        }
    }

    private static void ConfigureToonDefaults(Material material, Settings settings)
    {
        SetFloat(material, "_style", 1f);
        material.DisableKeyword("_STYLE_STYLIZEPBR");
        material.EnableKeyword("_STYLE_CELSHADING");
        material.DisableKeyword("_STYLE_FACE");

        if (settings.ramp != null)
        {
            SetTexture(material, "_RampMap", settings.ramp);
        }

        SetFloat(material, "_OutlineWidth", settings.outlineWidth);
        SetColor(material, "_OutlineColor", settings.outlineColor);
        SetFloat(material, "_FlattenAdditionalLighting", 1f);
        material.EnableKeyword("_FlattenAdditionalLighting");

        SetFloat(material, "_ReceiveShadows", 1f);
        SetFloat(material, "_RimLightOn", 1f);
        SetFloat(material, "_RimLightStrength", 0.18f);
        SetColor(material, "_RimLightColor", settings.rimColor);
        material.EnableKeyword("_RIMLIGHTING_ON");

        SetFloat(material, "_DepthRimLightOn", 1f);
        SetFloat(material, "_DepthRimLightStrength", 0.06f);
        SetColor(material, "_DepthRimLightColor", settings.rimColor);
        material.EnableKeyword("_DEPTH_RIMLIGHTING_ON");

        material.renderQueue = -1;
    }

    private static Texture GetTexture(Material material, string property)
    {
        return material.HasProperty(property) ? material.GetTexture(property) : null;
    }

    private static bool TryGetColor(Material material, string property, out Color color)
    {
        if (material.HasProperty(property))
        {
            color = material.GetColor(property);
            return true;
        }

        color = Color.white;
        return false;
    }

    private static float GetFloat(Material material, string property, float fallback)
    {
        return material.HasProperty(property) ? material.GetFloat(property) : fallback;
    }

    private static void SetTexture(Material material, string property, Texture value)
    {
        if (material.HasProperty(property))
        {
            material.SetTexture(property, value);
        }
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, value);
        }
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folder = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folder);
    }

    private static string SafeAssetName(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        value = value.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        return string.IsNullOrWhiteSpace(value) ? "ASP_Toon" : value;
    }
}
