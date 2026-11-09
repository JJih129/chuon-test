#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 반복 13: Tutorial.unity 를 "침투 경로 VR 시뮬레이션" 맵으로 재구성 (Docs/LevelDesign_Infiltration.md).
// Tools/Chuon/Tutorial/Build Infiltration Map — 재실행 가능(INF_ROOT 를 지우고 다시 만든다). 자동 실행 없음.
// 입력: Tools/ArtGen/tutorial_city_blender.py(SM_INF_*.fbx), tutorial_textures.py(T_INF_*.png).
public static class InfiltrationMapBuilder
{
    const string ScenePath = "Assets/Scenes/Tutorial.unity";
    const string ArchivePath = "Assets/Scenes/Archive/Tutorial_Range.unity";
    const string ArtRoot = "Assets/Art/ChuonPolish/Tutorial";
    const string MeshDir = ArtRoot + "/Meshes";
    const string GenDir = ArtRoot + "/Meshes/Generated";
    const string TexDir = ArtRoot + "/Textures";
    const string MatDir = ArtRoot + "/Materials";
    const string LogoTex = "Assets/Art/ChuonPolish/Level_Facility/Textures/T_LF_Holo_TsukasaLogo.png";
    const string MobDir = "Assets/Prefabs/Mobs";

    static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
    static Transform s_env, s_props, s_light, s_play, s_sim, s_city;
    static readonly System.Text.StringBuilder Log = new System.Text.StringBuilder();

    [MenuItem("Tools/Chuon/Tutorial/Build Infiltration Map")]
    public static void BuildMenu() => Debug.Log(Build(true));

    /// <summary>MCP/QA 진입점. bake = 반사 프로브·NavMesh 굽기.</summary>
    public static string Build(bool bake)
    {
        Log.Clear();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "ABORT: play mode";
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty)
                return "ABORT: active scene is dirty: " + scene.path;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ArchiveRange(scene);
        ImportSettings();
        BuildMaterials();
        RemapModels();
        Cleanup(scene);

        GameObject root = new GameObject("INF_ROOT");
        s_env = Child(root.transform, "Environment");
        s_city = Child(root.transform, "City");
        s_props = Child(root.transform, "Props");
        s_light = Child(root.transform, "Lighting");
        s_sim = Child(root.transform, "Simulation");
        s_play = Child(root.transform, "Gameplay");

        BuildGround();
        BuildStreet();
        BuildPlaza();
        BuildLaneAndDock();
        BuildSideYard();
        BuildHQ();
        BuildCity();
        BuildSimBoundaries();
        BuildLighting();
        InfiltrationTutorialDirector director = BuildGameplay();
        UpsertDialogue();

        StaticFlags(s_env); StaticFlags(s_city); StaticFlags(s_props);
        if (bake)
        {
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            Log.Append("navmesh baked; ");
            foreach (ReflectionProbe p in s_light.GetComponentsInChildren<ReflectionProbe>())
            {
                string dir = Path.GetDirectoryName(ScenePath) + "/Tutorial";
                Directory.CreateDirectory(dir);
                Lightmapping.BakeReflectionProbe(p, $"{dir}/INF_{p.name}.exr");
            }
            Log.Append("probes baked; ");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Log.Append("saved=" + saved);
        return Log.ToString();
    }

    // ------------------------------------------------------------------ setup
    static void ArchiveRange(Scene scene)
    {
        if (File.Exists(ArchivePath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(ArchivePath));
        AssetDatabase.Refresh();
        bool ok = AssetDatabase.CopyAsset(ScenePath, ArchivePath);
        Log.Append($"archived range scene -> {ArchivePath} ok={ok}; ");
    }

    static void ImportSettings()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(p).StartsWith("T_INF_")) continue;
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            bool changed = false;
            bool normal = p.Contains("_Normal");
            bool linear = p.Contains("_Mask");
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            bool alpha = p.Contains("Skyline") || p.Contains("VRGrid") || p.Contains("Beam");
            if (alpha && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            int max = p.Contains("Sky_Pano") || p.Contains("Skyline") || p.Contains("Signs") ? 4096 : 1024;
            if (ti.maxTextureSize != max) { ti.maxTextureSize = max; changed = true; }
            if (p.Contains("Sky_Pano") && (ti.mipmapEnabled || ti.wrapModeV != TextureWrapMode.Clamp))
            { ti.mipmapEnabled = false; ti.wrapModeV = TextureWrapMode.Clamp; changed = true; }
            if (p.Contains("Skyline") && ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapModeV = TextureWrapMode.Clamp; changed = true; }
            if (ti.anisoLevel < 4 && !p.Contains("Sky")) { ti.anisoLevel = 4; changed = true; }
            if (changed) ti.SaveAndReimport();
        }
    }

    static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

    static Material Mat(string name, string shader)
    {
        Directory.CreateDirectory(MatDir);
        string path = $"{MatDir}/M_INF_{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader == null || m.shader.name != shader)
        {
            m.shader = Shader.Find(shader);
        }
        m.enableInstancing = true;
        M[name] = m;
        return m;
    }

    static void Lit(string name, string tex, Vector2 tiling, Color tint, float smoothness = 1f, float metallic = 0f, Color? emission = null, string emissionTex = null)
    {
        Material m = Mat(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_WorkflowMode", 1f);
        Texture2D alb = tex != null ? Tex(tex + "_Albedo") : null;
        Texture2D nrm = tex != null ? Tex(tex + "_Normal") : null;
        Texture2D msk = tex != null ? Tex(tex + "_Mask") : null;
        m.SetTexture("_BaseMap", alb);
        m.SetTextureScale("_BaseMap", tiling);
        m.SetTexture("_BumpMap", nrm);
        m.SetFloat("_BumpScale", 1f);
        if (nrm != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        m.SetTexture("_MetallicGlossMap", msk);
        m.SetTexture("_OcclusionMap", msk);
        m.SetFloat("_OcclusionStrength", msk != null ? 0.8f : 1f);
        if (msk != null) { m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP"); }
        else { m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_OCCLUSIONMAP"); }
        if (emission.HasValue)
        {
            m.SetColor("_EmissionColor", emission.Value);
            Texture2D em = emissionTex != null ? (emissionTex.StartsWith("Assets/") ? AssetDatabase.LoadAssetAtPath<Texture2D>(emissionTex) : Tex(emissionTex)) : null;
            m.SetTexture("_EmissionMap", em);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        EditorUtility.SetDirty(m);
    }

    static void Unlit(string name, Texture2D tex, Color color, bool transparent, bool cullOff, Vector2 tiling, bool alphaClip = false)
    {
        Material m = Mat(name, "Universal Render Pipeline/Unlit");
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Cull", cullOff ? 0f : 2f);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
            m.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : -1;
        }
        m.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        m.SetFloat("_Cutoff", 0.4f);
        if (alphaClip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(m);
    }

    static void BuildMaterials()
    {
        Lit("Graphite", "T_INF_Graphite", new Vector2(0.5f, 0.5f), new Color(0.9f, 0.9f, 0.95f), 1f, 1f);
        Lit("Ceramic", "T_INF_Ceramic", new Vector2(0.25f, 0.25f), Color.white, 1f, 0f);
        Lit("Concrete", "T_INF_Ceramic", new Vector2(0.33f, 0.33f), new Color(0.52f, 0.52f, 0.53f), 0.55f, 0f);
        Lit("Glass", null, Vector2.one, new Color(0.025f, 0.035f, 0.05f), 0.95f, 0.25f);
        Lit("Red", null, Vector2.one, new Color(0.3f, 0.02f, 0.02f), 0.6f, 0f, new Color(1f, 0.05f, 0.04f) * 4.5f);
        Lit("Amber", null, Vector2.one, new Color(0.35f, 0.18f, 0.02f), 0.6f, 0f, new Color(1f, 0.48f, 0.08f) * 3.5f);
        Lit("White", null, Vector2.one, new Color(0.8f, 0.82f, 0.85f), 0.6f, 0f, new Color(0.92f, 0.95f, 1f) * 3.2f);
        Lit("Rubber", null, Vector2.one, new Color(0.03f, 0.03f, 0.032f), 0.3f, 0f);
        Lit("Steel", null, Vector2.one, new Color(0.34f, 0.35f, 0.37f), 0.55f, 0.9f);
        Lit("FacadeA", "T_INF_FacadeA", new Vector2(1f / 12f, 1f / 14.4f), Color.white, 1f, 1f, new Color(1f, 0.95f, 0.88f) * 1.35f, "T_INF_FacadeA_Emission");
        Lit("FacadeB", "T_INF_FacadeB", new Vector2(1f / 12f, 1f / 18f), Color.white, 1f, 1f, new Color(1f, 0.95f, 0.88f) * 1.2f, "T_INF_FacadeB_Emission");
        Lit("Container", "T_INF_Container", new Vector2(1f / 6.06f, 1f / 2.6f), Color.white, 1f, 1f);
        Lit("Shutter", "T_INF_Shutter", new Vector2(0.25f, 0.25f), Color.white, 1f, 1f);
        Lit("Road", "T_INF_Road", Vector2.one, Color.white, 1f, 0f);
        Lit("Sidewalk", "T_INF_Sidewalk", new Vector2(1f / 2.4f, 1f / 2.4f), Color.white, 1f, 0f);
        Lit("Asphalt", "T_INF_Asphalt", new Vector2(0.25f, 0.25f), Color.white, 1f, 0f);
        Lit("Signs", null, Vector2.one, Color.white, 0.5f, 0f, Color.white * 1.7f, "T_INF_Signs_Emission");
        M["Signs"].SetTexture("_BaseMap", Tex("T_INF_Signs_Albedo"));
        Lit("Emblem", null, Vector2.one, new Color(0.03f, 0.03f, 0.035f), 0.6f, 0f, new Color(1f, 0.06f, 0.05f) * 5f, LogoTex);
        Lit("EmblemWhite", null, Vector2.one, new Color(0.9f, 0.9f, 0.9f), 0.6f, 0f, new Color(0.95f, 0.97f, 1f) * 2.2f, LogoTex);
        Lit("LobbyGlow", null, Vector2.one, new Color(0.9f, 0.9f, 0.9f), 0.5f, 0f, new Color(1f, 0.97f, 0.92f) * 1.6f);
        // 하자드: 기존 사격장 테이프 텍스처 재사용
        Material hz = Mat("Hazard", "Universal Render Pipeline/Lit");
        hz.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/ChuonPolish/Tutorial/Textures/T_Range_HazardTape.png"));
        hz.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
        hz.SetColor("_BaseColor", Color.white);
        hz.SetFloat("_Smoothness", 0.4f);
        EditorUtility.SetDirty(hz);

        Unlit("VRGrid", Tex("T_INF_VRGrid"), new Color(0.45f, 0.85f, 1f, 0.4f), true, true, Vector2.one);
        Unlit("SimFx", Tex("T_INF_VRGrid"), new Color(0.5f, 0.9f, 1f, 0.8f), true, true, new Vector2(3f, 2f));
        Unlit("Beam", Tex("T_INF_Beam"), new Color(1f, 0.08f, 0.06f, 0.85f), true, true, Vector2.one);
        Unlit("Skyline", Tex("T_INF_Skyline"), new Color(0.75f, 0.8f, 0.95f, 1f), false, true, Vector2.one, true);
        Unlit("Barrier", Tex("T_INF_VRGrid"), new Color(1f, 0.15f, 0.1f, 0.55f), true, true, new Vector2(2.5f, 2.5f));

        Material sky = Mat("Sky", "Skybox/Panoramic");
        sky.SetTexture("_MainTex", Tex("T_INF_Sky_Pano"));
        sky.SetFloat("_Exposure", 1.0f);
        sky.SetFloat("_Rotation", 0f);
        sky.SetFloat("_Mapping", 1f);
        sky.SetFloat("_ImageType", 0f);
        EditorUtility.SetDirty(sky);
        AssetDatabase.SaveAssets();
    }

    static void RemapModels()
    {
        string[] slots = { "Graphite", "Ceramic", "Concrete", "Glass", "Red", "Amber", "White", "Rubber", "Hazard", "FacadeA", "FacadeB", "Container", "Shutter", "Steel" };
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MeshDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            bool changed = false;
            foreach (string s in slots)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "INF_" + s);
                Dictionary<AssetImporter.SourceAssetIdentifier, Object> map = mi.GetExternalObjectMap();
                if (!map.TryGetValue(id, out Object cur) || cur != M[s]) { mi.AddRemap(id, M[s]); changed = true; }
            }
            if (mi.importCameras || mi.importLights || mi.importAnimation) { mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; changed = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
            // 출구 패널은 SceneMove 의 Outline(QuickOutline) 이 메시를 읽는다 → Read/Write 필요
            bool readable = p.Contains("DoorPanel");
            if (mi.isReadable != readable) { mi.isReadable = readable; changed = true; }
            if (changed) mi.SaveAndReimport();
        }
    }

    static void Cleanup(Scene scene)
    {
        string[] remove = { "INF_ROOT", "ENV_TestRange", "tile", "Cube (1)", "Cube (2)", "Cube (3)", "Cube (4)", "QuestGoal", "더미", "Drone", "TutorialUltimateCinematicRoot" };
        foreach (GameObject g in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(remove, g.name) >= 0)
            {
                string n = g.name;
                Object.DestroyImmediate(g);
                Log.Append("removed " + n + "; ");
            }
        }
        TutorialManager tm = Object.FindFirstObjectByType<TutorialManager>(FindObjectsInactive.Include);
        if (tm != null) { tm.movementGoal = null; tm.attackDummy = null; tm.droneEnemy = null; EditorUtility.SetDirty(tm); }
    }

    // ------------------------------------------------------------------ helpers
    static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Kit(string name, Vector3 pos, float yaw, Transform parent, bool collider = true, string rename = null)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/SM_INF_{name}.fbx");
        if (asset == null) { Log.Append($"MISSING kit {name}; "); return new GameObject(name + "_MISSING"); }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = rename ?? name;
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (collider)
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
        return go;
    }

    static Transform Find(GameObject go, string child)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == child) return t;
        return null;
    }

    /// <summary>World-metre UV box (uv = world xz/xy/zy in metres) saved as a mesh asset.</summary>
    static GameObject Slab(string name, Vector3 min, Vector3 max, string mat, Transform parent, bool collider = true, Vector2? uvOffset = null)
    {
        Vector3 size = max - min;
        Vector3 c = (min + max) * 0.5f;
        Mesh mesh = MetreBox(size, c, uvOffset ?? Vector2.zero);
        Directory.CreateDirectory(GenDir);
        string path = $"{GenDir}/SM_INF_{name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
        else AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = c;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = M[mat];
        if (collider) { var bc = go.AddComponent<BoxCollider>(); bc.size = size; }
        return go;
    }

    static Mesh MetreBox(Vector3 s, Vector3 worldCentre, Vector2 off)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3 h = s * 0.5f;
        void Face(Vector3 normal, Vector3 u, Vector3 w)
        {
            int b = v.Count;
            Vector3 centre = Vector3.Scale(normal, h);
            float hu = Mathf.Abs(Vector3.Dot(h, u)), hw = Mathf.Abs(Vector3.Dot(h, w));
            Vector3[] corners = { centre - u * hu - w * hw, centre + u * hu - w * hw, centre + u * hu + w * hw, centre - u * hu + w * hw };
            foreach (Vector3 p in corners)
            {
                v.Add(p); n.Add(normal);
                Vector3 wp = p + worldCentre;
                uv.Add(new Vector2(Vector3.Dot(wp, u), Vector3.Dot(wp, w)) + off);
            }
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        Face(Vector3.up, Vector3.right, Vector3.forward);
        Face(Vector3.down, Vector3.right, Vector3.back);
        Face(Vector3.forward, Vector3.left, Vector3.up);
        Face(Vector3.back, Vector3.right, Vector3.up);
        Face(Vector3.right, Vector3.forward, Vector3.up);
        Face(Vector3.left, Vector3.back, Vector3.up);
        var m = new Mesh { name = "MetreBox" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    /// <summary>Flat quad facing +normal (yaw), uv rect = atlas cell; no collider.</summary>
    static GameObject Quad(string name, Vector3 pos, float yaw, Vector2 size, string mat, Transform parent, Rect? uvRect = null, float pitch = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        // Unity Quad 앞면 = -Z. yaw 는 "보는 사람이 있는 방향"(앞면 법선) 기준으로 받는다.
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw + 180f, 0f));
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = M[mat];
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (uvRect.HasValue)
        {
            Rect u = uvRect.Value;
            string key = $"{GenDir}/SM_INF_QuadUV_{Mathf.RoundToInt(u.x * 100)}_{Mathf.RoundToInt(u.y * 100)}_{Mathf.RoundToInt(u.width * 100)}_{Mathf.RoundToInt(u.height * 100)}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(key);
            if (mesh == null)
            {
                Directory.CreateDirectory(GenDir);
                mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
                mesh.uv = new[] { new Vector2(u.xMin, u.yMin), new Vector2(u.xMax, u.yMin), new Vector2(u.xMin, u.yMax), new Vector2(u.xMax, u.yMax) };
                AssetDatabase.CreateAsset(mesh, key);
            }
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        return go;
    }

    // 간판 아틀라스 4x2 (셀 512 px), 내용이 있는 세로 20..300 px 만 잘라 쓴다 → 가로:세로 ≈ 1.83
    static Rect SignCell(int i) => new Rect((i % 4) * 0.25f, i < 4 ? 1f - 300f / 1024f : 1f - 812f / 1024f, 0.25f, 280f / 1024f);

    static void Sign(int cell, Vector3 pos, float yaw, float width, Transform parent)
    {
        Quad("Sign_" + cell, pos, yaw, new Vector2(width, width / 1.83f), "Signs", parent, SignCell(cell));
    }

    static GameObject Box(string name, Vector3 centre, Vector3 size, string mat, Transform parent, bool collider = true)
        => Slab(name, centre - size * 0.5f, centre + size * 0.5f, mat, parent, collider);

    static Light SpotLight(string name, Vector3 pos, Vector3 lookAt, Color c, float intensity, float range, float angle, bool shadows = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(s_light, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.color = c; l.intensity = intensity; l.range = range; l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        return l;
    }

    static Light PointLight(string name, Vector3 pos, Color c, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(s_light, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        return l;
    }

    static void StaticFlags(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            StaticEditorFlags f = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            if (t.GetComponent<Renderer>() != null && t.GetComponent<Collider>() != null)
                f |= StaticEditorFlags.NavigationStatic;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, f);
        }
    }

    static Transform Point(string name, Vector3 pos, float yaw, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        return go.transform;
    }

    static TutorialZoneTrigger Zone(string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(s_play, false);
        go.transform.position = centre;
        var bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = true; bc.size = size;
        return go.AddComponent<TutorialZoneTrigger>();
    }

    // ------------------------------------------------------------------ layout (Docs/LevelDesign_Infiltration.md, north = +Z)
    static void BuildGround()
    {
        Slab("G_Base", new Vector3(-800, -0.6f, -800), new Vector3(800, -0.02f, 800), "Asphalt", s_env);
        // 거리
        Road("G_RoadStreet", -4f, 4f, -70f, 99.2f);
        Slab("G_WalkW", new Vector3(-7, -0.3f, -70), new Vector3(-4, 0.12f, 52), "Sidewalk", s_env);
        Slab("G_WalkE", new Vector3(4, -0.3f, -70), new Vector3(7, 0.12f, 52), "Sidewalk", s_env);
        // 검문소 광장 (도로 양옆 포장)
        Slab("G_PlazaW", new Vector3(-24, -0.3f, 52), new Vector3(-4, 0.02f, 100), "Sidewalk", s_env);
        Slab("G_PlazaE", new Vector3(4, -0.3f, 52), new Vector3(24, 0.02f, 100), "Sidewalk", s_env);
        // 게이트 안 마당 + 서비스 골목
        Slab("G_InnerYard", new Vector3(-10, -0.3f, 99.2f), new Vector3(31, 0.02f, 108), "Concrete", s_env);
        // 하역장 야드
        Slab("G_DockYard", new Vector3(30, -0.3f, 104), new Vector3(68, 0.0f, 157), "Asphalt", s_env, true, new Vector2(0.37f, 0.11f));
        // 측면 마당 (본사 앞 단, 1.2 m)
        Slab("G_SideYard", new Vector3(28, -0.3f, 177), new Vector3(70, 1.2f, 201), "Sidewalk", s_env);
    }

    static void Road(string name, float x0, float x1, float z0, float z1)
    {
        // 도로 스트립: u = 폭(0..1), v = 길이/16 m
        var mesh = new Mesh { name = name };
        float w = x1 - x0, len = z1 - z0;
        mesh.vertices = new[] { new Vector3(x0, 0.01f, z0), new Vector3(x1, 0.01f, z0), new Vector3(x0, 0.01f, z1), new Vector3(x1, 0.01f, z1) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, len / 16f), new Vector2(1, len / 16f) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        string path = $"{GenDir}/SM_INF_{name}.asset";
        Directory.CreateDirectory(GenDir);
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); mesh = existing; } else AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(s_env, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = M["Road"];
        var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3((x0 + x1) / 2, -0.15f, (z0 + z1) / 2); bc.size = new Vector3(w, 0.32f, len);
    }

    static void BuildStreet()
    {
        Transform st = Child(s_env, "A_Street");
        // 거리 양옆 건물 (앞면 = 거리)
        Kit("BlockA", new Vector3(-16, 0, 6), 90, st, false);
        Kit("BlockB", new Vector3(-15, 0, 26), 90, st, false);
        Kit("BlockD", new Vector3(-14, 0, 43), 90, st, false);
        Kit("BlockC", new Vector3(17, 0, 8), -90, st, false);
        Kit("BlockB", new Vector3(15, 0, 30), -90, st, false);
        Kit("BlockD", new Vector3(14, 0, 44), -90, st, false);
        // 시뮬레이션 경계 너머로 이어지는 거리 (남쪽)
        Kit("BlockC", new Vector3(-17, 0, -22), 90, st, false);
        Kit("BlockA", new Vector3(16, 0, -20), -90, st, false);
        Kit("BlockB", new Vector3(-15, 0, -44), 90, st, false);
        Kit("BlockD", new Vector3(14, 0, -42), -90, st, false);
        // 건물 충돌 (거리 쪽 면만 막으면 충분)
        Box("W_StreetW", new Vector3(-7.25f, 6, 23), new Vector3(0.5f, 12, 58), "Graphite", st).GetComponent<MeshRenderer>().enabled = false;
        Box("W_StreetE", new Vector3(7.25f, 6, 23), new Vector3(0.5f, 12, 58), "Graphite", st).GetComponent<MeshRenderer>().enabled = false;
        // 가로등 열 (리딩 라인)
        float[] zs = { 4, 14, 24, 34, 44 };
        for (int i = 0; i < zs.Length; i++)
        {
            bool west = i % 2 == 0;
            GameObject sl = Kit("StreetLight", new Vector3(west ? -5.6f : 5.6f, 0.12f, zs[i]), west ? 90 : -90, s_props, true, "StreetLight_" + i);
            Transform a = Find(sl, "LightAnchor");
            Vector3 p = a != null ? a.position : sl.transform.position + Vector3.up * 7.7f;
            SpotLight("L_Street_" + i, p, p + Vector3.down * 8f + (west ? Vector3.right : Vector3.left) * 1.5f, new Color(1f, 0.93f, 0.82f), 9f, 15f, 120f);
        }
        for (float z = -2; z < 50; z += 6)
        {
            Kit("Bollard", new Vector3(-4.35f, 0.12f, z), 0, s_props);
            Kit("Bollard", new Vector3(4.35f, 0.12f, z + 3), 0, s_props);
        }
        Kit("Planter", new Vector3(-6.2f, 0.12f, 10), 90, s_props);
        Kit("Planter", new Vector3(6.2f, 0.12f, 20), -90, s_props);
        Kit("Planter", new Vector3(-6.2f, 0.12f, 30), 90, s_props);
        // 광고판 / 슬로건 (환경 스토리텔링)
        Sign(1, new Vector3(-6.94f, 9.5f, 26), 90, 9f, s_props);
        Sign(4, new Vector3(6.94f, 11f, 10), -90, 11f, s_props);
        Sign(0, new Vector3(-6.94f, 7f, 43), 90, 7f, s_props);
        // 보안 스캐너 게이트 (회피 시험)
        GameObject sf = Kit("ScannerFrame", new Vector3(0, 0, 36), 0, s_props);
        Sign(6, new Vector3(0, 4.4f, 35.72f), 180, 5f, s_props);
    }

    static void BuildPlaza()
    {
        Transform pz = Child(s_env, "B_Checkpoint");
        // 외곽 보안벽 (광장 둘레)
        foreach (float z in new[] { 57f, 67f, 77f, 87f, 95f })
        {
            Kit("SecurityWall", new Vector3(-24, 0, z), 90, pz);
            Kit("SecurityWall", new Vector3(24, 0, z), 90, pz);
        }
        foreach (float x in new[] { -19f, -12f })
        {
            Kit("SecurityWall", new Vector3(x, 0, 52), 0, pz);
            Kit("SecurityWall", new Vector3(-x, 0, 52), 0, pz);
        }
        foreach (float x in new[] { -19f, -10f })
        {
            Kit("SecurityWall", new Vector3(x, 0, 100), 0, pz);
            Kit("SecurityWall", new Vector3(-x, 0, 100), 0, pz);
        }
        foreach (Vector3 p in new[] { new Vector3(-24, 0, 52), new Vector3(24, 0, 52), new Vector3(-24, 0, 100), new Vector3(24, 0, 100) })
            Kit("WallPillar", p, 180, pz);
        Kit("WallPillar", new Vector3(-5.2f, 0, 100), 180, pz, true, "GatePillarW");
        Kit("WallPillar", new Vector3(5.2f, 0, 100), 180, pz, true, "GatePillarE");
        // 검문 부스 · ID 게이트 · 저지 바리케이드 · 투광등
        Kit("Booth", new Vector3(9.5f, 0, 90), -90, s_props);
        Kit("IDGate", new Vector3(-8.2f, 0, 95.5f), 180, s_props);
        Kit("IDGate", new Vector3(-10.6f, 0, 95.5f), 180, s_props);
        Kit("IDGate", new Vector3(-13.0f, 0, 95.5f), 180, s_props);
        foreach (var (p, yaw) in new[] { (new Vector3(-6.2f, 0, 92.5f), 15f), (new Vector3(6.4f, 0, 93.5f), -15f), (new Vector3(-20, 0, 70), 90f), (new Vector3(-20, 0, 72.1f), 90f), (new Vector3(20.5f, 0, 64), 90f), (new Vector3(20.5f, 0, 66.1f), 90f), (new Vector3(-15.5f, 0, 92.5f), 0f), (new Vector3(-17.6f, 0, 92.5f), 0f) })
            Kit("Jersey", p, yaw, s_props);
        (Vector3 p, float yaw)[] masts = { (new Vector3(-21, 0, 57), 45f), (new Vector3(21, 0, 57), -45f), (new Vector3(-21, 0, 96), 135f), (new Vector3(21, 0, 96), -135f) };
        for (int i = 0; i < masts.Length; i++)
        {
            GameObject m = Kit("FloodMast", masts[i].p, masts[i].yaw, s_props, true, "FloodMast_" + i);
            Transform a = Find(m, "LightAnchor");
            SpotLight("L_Flood_" + i, a != null ? a.position : masts[i].p + Vector3.up * 10.5f, new Vector3(0, 0, 76), new Color(0.92f, 0.96f, 1f), 26f, 46f, 62f, i == 1);
        }
        // 슬로건
        Sign(0, new Vector3(-14.5f, 4.3f, 99.52f), 180, 8f, s_props);
        Sign(2, new Vector3(14.5f, 4.3f, 99.52f), 180, 8f, s_props);
        Sign(1, new Vector3(23.52f, 4.3f, 76f), -90, 7f, s_props);
    }

    static void BuildLaneAndDock()
    {
        Transform ld = Child(s_env, "C_LaneDock");
        // 안마당 · 서비스 골목 (압축: 폭 4 m, 벽 9 m)
        foreach (float x in new[] { -4f, 8f, 20f })
            Kit("ServiceWall", new Vector3(x, 0, 108), 180, ld);
        Kit("ServiceWall", new Vector3(18, 0, 104), 0, ld);
        Kit("ServiceWall", new Vector3(24, 0, 104), 0, ld);
        Kit("ServiceWall", new Vector3(-10, 0, 104), -90, ld);
        Box("W_InnerEast", new Vector3(12.25f, 4.5f, 102f), new Vector3(0.5f, 9, 4), "Concrete", ld);
        // 안마당 북쪽은 낮은 보안동 (타워 엠블럼 시선축을 가리지 않게 10 m)
        Slab("Annex_Lane", new Vector3(-12, 0, 108.5f), new Vector3(30, 10, 124), "FacadeB", ld, false);
        Box("Annex_LaneCap", new Vector3(9, 10.4f, 116.25f), new Vector3(42.4f, 0.8f, 15.9f), "Graphite", ld, false);
        PointLight("L_Lane", new Vector3(21, 5.5f, 106), new Color(1f, 0.86f, 0.66f), 5f, 12f);
        // 하역장 야드 둘레
        foreach (float x in new[] { 35f, 45f, 55f, 63f })
            Kit("SecurityWall", new Vector3(x, 0, 104), 0, ld);
        foreach (float z in new[] { 113f, 123f, 133f, 143f, 152f })
        {
            Kit("SecurityWall", new Vector3(30, 0, z), 90, ld);
            Kit("SecurityWall", new Vector3(68, 0, z), 90, ld);
        }
        Kit("WallPillar", new Vector3(30, 0, 104), 180, ld); Kit("WallPillar", new Vector3(68, 0, 104), 180, ld);
        // 창고(하역장 건물): 앞면 남쪽, 중앙 셔터 = 통로
        Kit("DockBuilding", new Vector3(47, 0, 157), 180, ld);
        Sign(3, new Vector3(47, 10.6f, 156.83f), 180, 8f, s_props);
        // 엄폐 (컨테이너·트럭) — 터렛 조준선을 끊는 위치
        Kit("Container", new Vector3(40, 0, 128), 90, s_props);
        Kit("Container", new Vector3(40, 2.59f, 128.4f), 92, s_props, true, "ContainerStack");
        Kit("Container", new Vector3(53, 0, 134), 90, s_props);
        Kit("Container", new Vector3(45, 0, 144.5f), 0, s_props);
        Kit("Container", new Vector3(59, 0, 141.5f), 90, s_props);
        Kit("Truck", new Vector3(35.5f, 0, 146.5f), 180, s_props);
        Kit("Jersey", new Vector3(63, 0, 132), 0, s_props);
        Kit("Jersey", new Vector3(65.1f, 0, 132), 0, s_props);
        // 조명
        GameObject m0 = Kit("FloodMast", new Vector3(32.5f, 0, 106.5f), 25, s_props, true, "FloodMast_Dock0");
        GameObject m1 = Kit("FloodMast", new Vector3(65.5f, 0, 106.5f), -25, s_props, true, "FloodMast_Dock1");
        SpotLight("L_DockFlood0", Find(m0, "LightAnchor").position, new Vector3(45, 0, 140), new Color(0.9f, 0.95f, 1f), 40f, 55f, 70f, true);
        SpotLight("L_DockFlood1", Find(m1, "LightAnchor").position, new Vector3(55, 0, 145), new Color(0.9f, 0.95f, 1f), 34f, 55f, 70f);
        PointLight("L_PassageAmber", new Vector3(47, 4.2f, 155.6f), new Color(1f, 0.55f, 0.15f), 5f, 9f);
        PointLight("L_Tunnel", new Vector3(47, 5.4f, 167), new Color(0.92f, 0.95f, 1f), 4f, 12f);
    }

    static void BuildSideYard()
    {
        Transform sy = Child(s_env, "D_SideYard");
        foreach (float z in new[] { 182f, 192f, 197f })
        {
            Kit("SecurityWall", new Vector3(30, 0, z), 90, sy);
            Kit("SecurityWall", new Vector3(68, 0, z), 90, sy);
        }
        Kit("Planter", new Vector3(36, 1.2f, 198.6f), 0, s_props);
        Kit("Planter", new Vector3(58, 1.2f, 198.6f), 0, s_props);
        Kit("Bollard", new Vector3(42.5f, 1.2f, 196.5f), 0, s_props);
        Kit("Bollard", new Vector3(51.5f, 1.2f, 196.5f), 0, s_props);
        Sign(5, new Vector3(38.5f, 4.6f, 199.45f), 180, 6f, s_props);
        SpotLight("L_Canopy0", new Vector3(44, 7.6f, 197.5f), new Vector3(44, 1.2f, 195), new Color(1f, 0.95f, 0.86f), 10f, 12f, 95f);
        SpotLight("L_Canopy1", new Vector3(50, 7.6f, 197.5f), new Vector3(50, 1.2f, 195), new Color(1f, 0.95f, 0.86f), 10f, 12f, 95f);
        PointLight("L_Vestibule", new Vector3(47, 6f, 207), new Color(1f, 0.96f, 0.9f), 6f, 11f);
    }

    static void BuildHQ()
    {
        Transform hq = Child(s_env, "E_HQ");
        GameObject podium = Kit("HQPodium", new Vector3(21, 1.2f, 200), 180, hq);
        Transform sign = Find(podium, "SideSignAnchor");
        Sign(7, (sign != null ? sign.position : new Vector3(47, 9.3f, 195.4f)) + new Vector3(0, 0, -0.05f), 180, 7f, s_props);
        // 로비 유리 너머의 밝은 로비 (리딩 라이트) + 백색 엠블럼 홀로그램
        // 유리문은 불투명 재질이므로 젖빛 유리처럼 문 앞면에 붙인다 (문 너머 밝은 로비)
        Quad("LobbyGlow", new Vector3(47, 4.45f, 212.66f), 180, new Vector2(7.2f, 6.3f), "LobbyGlow", hq);
        Quad("LobbyEmblem", new Vector3(47, 4.7f, 212.62f), 180, new Vector2(3.0f, 3.0f), "EmblemWhite", hq);
        // 타워 = 위니 (거리 축과 일직선)
        GameObject tower = Kit("HQTower", new Vector3(0, 0, 300), 180, hq, false);
        tower.AddComponent<BoxCollider>().size = new Vector3(30, 240, 30);
        tower.GetComponent<BoxCollider>().center = new Vector3(0, 120, 0);
        Box("TowerEmblemPlate", new Vector3(0, 61, 284.55f), new Vector3(25, 25, 0.8f), "Graphite", hq, false);
        Quad("TowerEmblemLow", new Vector3(0, 62, 284.1f), 180, new Vector2(22, 22), "Emblem", hq);
        Transform ea = Find(tower, "EmblemAnchor");
        Quad("TowerEmblemCrown", ea != null ? ea.position + new Vector3(0, 0, -0.1f) : new Vector3(0, 207, 287.2f), 180, new Vector2(14, 14), "Emblem", hq);
        // 본사 캠퍼스 보조 동
        Kit("BlockC", new Vector3(72, 0, 290), 180, hq, false);
        Kit("BlockA", new Vector3(-55, 0, 285), 180, hq, false);
        Kit("BlockC", new Vector3(-30, 0, 330), 180, hq, false);
    }

    static void BuildCity()
    {
        // 동선 바깥을 채우는 도시 블록 (결정론적 배치)
        var rnd = new System.Random(1307);
        string[] kinds = { "BlockA", "BlockB", "BlockC", "BlockD" };
        var placed = 0;
        for (int gx = -8; gx <= 9; gx++)
        for (int gz = -4; gz <= 14; gz++)
        {
            float x = gx * 30f + (float)(rnd.NextDouble() * 8 - 4);
            float z = gz * 30f + (float)(rnd.NextDouble() * 8 - 4);
            if (InsideRoute(x, z, 22f)) continue;
            if (Mathf.Abs(x) < 34f && z > 40f && z < 300f) continue; // 타워 시선축 (위니)
            if (Mathf.Abs(x) > 260 || z > 430 || z < -130) continue;
            if (rnd.NextDouble() < 0.35) continue;
            string k = kinds[rnd.Next(kinds.Length)];
            float yaw = rnd.Next(4) * 90f;
            float s = 0.85f + (float)rnd.NextDouble() * 0.4f;
            GameObject b = Kit(k, new Vector3(x, 0, z), yaw, s_city, false);
            b.transform.localScale = new Vector3(1f, s, 1f);
            placed++;
        }
        Log.Append($"city blocks={placed}; ");
        // 원경 스카이라인 링
        SkylineRing("SkylineNear", 620f, 150f, 0.0f);
        SkylineRing("SkylineFar", 880f, 230f, 0.37f);
    }

    static bool InsideRoute(float x, float z, float margin)
    {
        Rect[] r =
        {
            new Rect(-7, -70, 14, 122), new Rect(-24, 52, 48, 48), new Rect(-10, 99, 41, 9), new Rect(30, 104, 38, 53),
            new Rect(27, 157, 40, 20), new Rect(28, 177, 42, 24), new Rect(-29, 200, 100, 50), new Rect(-15, 285, 30, 30),
            new Rect(-25, -70, 50, 125)
        };
        foreach (Rect q in r)
            if (x > q.xMin - margin && x < q.xMax + margin && z > q.yMin - margin && z < q.yMax + margin) return true;
        return false;
    }

    static void SkylineRing(string name, float radius, float height, float uOffset)
    {
        int seg = 48;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            v.Add(d * radius + Vector3.down * 2f); v.Add(d * radius + Vector3.up * height);
            float u = i / (float)seg * 6f + uOffset;
            uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, 1));
            if (i < seg) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = $"{GenDir}/SM_INF_{name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); mesh = existing; } else AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(s_city, false);
        go.transform.position = new Vector3(20, 0, 150);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = M["Skyline"]; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
    }

    static void BuildSimBoundaries()
    {
        var renderers = new List<Renderer>();
        void Grid(string n, Vector3 p, float yaw, Vector2 size)
        {
            GameObject q = Quad(n, p, yaw, size, "VRGrid", s_sim);
            renderers.Add(q.GetComponent<Renderer>());
        }
        Grid("Grid_StreetSouth", new Vector3(0, 4, -6f), 0, new Vector2(14, 8));
        Box("W_StreetSouth", new Vector3(0, 3, -6.3f), new Vector3(14, 6, 0.5f), "Graphite", s_sim).GetComponent<MeshRenderer>().enabled = false;
        Grid("Grid_InnerWest", new Vector3(-9.4f, 4.5f, 104), 90, new Vector2(8, 9));
        Grid("Grid_DockEast", new Vector3(67.5f, 10f, 128), -90, new Vector2(48, 4));
        Grid("Grid_DockWest", new Vector3(30.5f, 10f, 128), 90, new Vector2(48, 4));
        Grid("Grid_SideEast", new Vector3(67.5f, 9f, 189), -90, new Vector2(24, 4));
        Grid("Grid_SideWest", new Vector3(30.5f, 9f, 189), 90, new Vector2(24, 4));
        Grid("Grid_PlazaWestTop", new Vector3(-23.5f, 10f, 76), 90, new Vector2(48, 4));
        Grid("Grid_PlazaEastTop", new Vector3(23.5f, 10f, 76), -90, new Vector2(48, 4));
        var sb = s_sim.gameObject.AddComponent<InfSimBoundary>();
        sb.Configure(renderers.ToArray());
    }

    static void BuildLighting()
    {
        // 해 질 녘: 서남서 낮은 태양 (따뜻한 림), 차가운 하늘 앰비언트, 거리 깊이감용 안개
        GameObject sunGo = GameObject.Find("Directional Light");
        if (sunGo != null)
        {
            Light sun = sunGo.GetComponent<Light>();
            sun.color = new Color(1f, 0.8f, 0.66f);
            sun.intensity = 0.7f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGo.transform.rotation = Quaternion.Euler(14f, 62f, 0f);
        }
        RenderSettings.skybox = M["Sky"];
        M["Sky"].SetFloat("_Rotation", 0f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.23f, 0.29f, 0.42f);
        RenderSettings.ambientEquatorColor = new Color(0.22f, 0.19f, 0.21f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.1f, 0.115f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0028f;
        RenderSettings.fogColor = new Color(0.17f, 0.19f, 0.26f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.85f;
        DynamicGI.UpdateEnvironment();

        // 반사 프로브 (구역별, 박스 투영)
        void Probe(string n, Vector3 c, Vector3 size)
        {
            var go = new GameObject(n);
            go.transform.SetParent(s_light, false);
            go.transform.position = c;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Baked; p.size = size; p.boxProjection = true; p.resolution = 128; p.hdr = true;
            p.importance = 1;
        }
        Probe("RP_Street", new Vector3(0, 4, 23), new Vector3(14, 14, 62));
        Probe("RP_Plaza", new Vector3(0, 6, 76), new Vector3(48, 16, 48));
        Probe("RP_Lane", new Vector3(10, 4, 104), new Vector3(42, 10, 10));
        Probe("RP_Dock", new Vector3(49, 6, 130), new Vector3(38, 16, 54));
        Probe("RP_Side", new Vector3(49, 6, 195), new Vector3(42, 14, 40));

        // 포스트 프로세스: Level_Facility 그레이드를 복사해 시작
        const string vp = ArtRoot + "/Profiles/VP_INF_Grade.asset";
        if (!File.Exists(vp))
        {
            Directory.CreateDirectory(ArtRoot + "/Profiles");
            AssetDatabase.CopyAsset("Assets/Art/ChuonPolish/Level_Facility/Profiles/VP_LF_Grade.asset", vp);
        }
        var volGo = new GameObject("PostVolume_Infiltration");
        volGo.transform.SetParent(s_light, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 1;
        vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(vp);

        // 감시 드론 (비전투)
        GameObject drone = Kit("Drone", new Vector3(0, 12, 4), 0, s_play, false, "AmbientDrone");
        GameObjectUtility.SetStaticEditorFlags(drone, 0);
        var spotGo = new GameObject("DroneSpot");
        spotGo.transform.SetParent(drone.transform, false);
        spotGo.transform.localPosition = new Vector3(0, -0.2f, 0.3f);
        spotGo.transform.localRotation = Quaternion.Euler(70f, 0, 0);
        var dl = spotGo.AddComponent<Light>();
        dl.type = LightType.Spot; dl.color = new Color(0.85f, 0.92f, 1f); dl.intensity = 30f; dl.range = 26f; dl.spotAngle = 26f; dl.innerSpotAngle = 12f; dl.shadows = LightShadows.None;
        drone.AddComponent<InfAmbientDrone>().Configure(new[]
        {
            new Vector3(0, 12, -2), new Vector3(2, 13, 46), new Vector3(14, 15, 70), new Vector3(-12, 14, 84), new Vector3(-3, 13, 50), new Vector3(-2, 12, 10)
        }, spotGo.transform, 5f);
    }

    // ------------------------------------------------------------------ gameplay
    static InfiltrationTutorialDirector BuildGameplay()
    {
        // 플레이어 시작
        Transform start = Point("PlayerStart", new Vector3(0, 0.02f, 1.5f), 0, s_play);
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) player.transform.SetPositionAndRotation(start.position, start.rotation);

        TutorialZoneTrigger zMove = Zone("Zone_Move", new Vector3(0, 1.2f, 15.5f), new Vector3(14, 2.4f, 2.5f));
        TutorialZoneTrigger zScan = Zone("Zone_ScanPassed", new Vector3(0, 1.2f, 39.5f), new Vector3(14, 2.4f, 2f));
        TutorialZoneTrigger zDock = Zone("Zone_DockYard", new Vector3(36, 1.5f, 111), new Vector3(10, 3, 6));
        TutorialZoneTrigger zSide = Zone("Zone_SideYard", new Vector3(47, 2.6f, 180.5f), new Vector3(8, 3, 2.5f));
        TutorialZoneTrigger zExit = Zone("Zone_Exit", new Vector3(47, 2.6f, 208.5f), new Vector3(7, 3, 4));

        // 스캐너 커튼 (트리거 + 붉은 빔 3줄)
        var scanGo = new GameObject("ScannerCurtain");
        scanGo.transform.SetParent(s_play, false);
        scanGo.transform.SetPositionAndRotation(new Vector3(0, 1.2f, 36), Quaternion.identity);
        var scanBox = scanGo.AddComponent<BoxCollider>();
        scanBox.size = new Vector3(12.6f, 2.4f, 0.5f);
        var beams = new List<Renderer>();
        foreach (float y in new[] { 0.45f, 1.05f, 1.65f, 2.25f })
        {
            GameObject b = Quad("Beam", new Vector3(0, y, 36), 180, new Vector2(12.4f, 0.09f), "Beam", scanGo.transform);
            beams.Add(b.GetComponent<Renderer>());
        }
        GameObject sheet = Quad("BeamSheet", new Vector3(0, 1.35f, 36), 180, new Vector2(12.4f, 2.4f), "Beam", scanGo.transform);
        sheet.transform.localScale = new Vector3(12.4f, 2.4f, 1f);
        beams.Add(sheet.GetComponent<Renderer>());
        var scanner = scanGo.AddComponent<InfScannerCurtain>();
        scanner.Configure(beams.ToArray());

        // 검문소 게이트 (차단기 2 + 막는 벽)
        Transform gateRoot = Child(s_play, "CheckpointGate");
        GameObject bw = Kit("BoomBarrier", new Vector3(-4.0f, 0, 98.3f), 180, gateRoot, true, "BoomW");
        GameObject be = Kit("BoomBarrier", new Vector3(4.0f, 0, 98.8f), 0, gateRoot, true, "BoomE");
        var blocker = new GameObject("GateBlocker");
        blocker.transform.SetParent(gateRoot, false);
        blocker.transform.position = new Vector3(0, 1.6f, 98.6f);
        blocker.AddComponent<BoxCollider>().size = new Vector3(9.2f, 3.2f, 1.4f);
        GameObject redW = Quad("GateStatusW", new Vector3(-5.2f, 7.2f, 99.24f), 180, new Vector2(0.9f, 0.18f), "Red", gateRoot);
        GameObject redE = Quad("GateStatusE", new Vector3(5.2f, 7.2f, 99.24f), 180, new Vector2(0.9f, 0.18f), "Red", gateRoot);
        var gate = gateRoot.gameObject.AddComponent<InfGate>();
        gate.Configure(new[] { Find(bw, "Arm"), Find(be, "Arm") }, new[] { -82f, -82f }, new[] { blocker }, null,
            new[] { redW.GetComponent<Renderer>(), redE.GetComponent<Renderer>() });

        // 하역장 통로 시뮬레이션 차단막 (퍼펙트 회피 전까지)
        Transform pRoot = Child(s_play, "PassageBarrier");
        GameObject pb = Quad("BarrierGrid", new Vector3(47, 3.7f, 156.7f), 180, new Vector2(4.8f, 5f), "Barrier", pRoot);
        var pbBlock = new GameObject("PassageBlocker");
        pbBlock.transform.SetParent(pRoot, false);
        pbBlock.transform.position = new Vector3(47, 3.7f, 157.2f);
        pbBlock.AddComponent<BoxCollider>().size = new Vector3(5f, 5f, 0.8f);
        var passage = pRoot.gameObject.AddComponent<InfGate>();
        passage.Configure(null, null, new[] { pbBlock }, new[] { pb.GetComponent<Renderer>() }, null);

        // 스폰 · 체크포인트
        Transform spawns = Child(s_play, "Spawns");
        Transform aSpawn = Point("AndroidPatrol", new Vector3(6, 0, 75), -90, spawns);
        Transform pa = Point("PatrolA", new Vector3(-7, 0, 76), 0, spawns);
        Transform pbP = Point("PatrolB", new Vector3(7, 0, 76), 0, spawns);
        Transform booth = Point("BoothSpawn", new Vector3(7.0f, 0, 89.5f), -120, spawns);
        Transform wallT = Point("WallTurretMount", new Vector3(53, 6.8f, 156.4f), 180, spawns);
        Transform platT = Point("PlatformTurret", new Vector3(35.5f, 1.2f, 155.5f), 90, spawns);
        Transform enf = Point("EnforcerSpawn", new Vector3(47, 1.2f, 192.5f), 180, spawns);
        Transform sideT = Point("SideTurret", new Vector3(47, 1.2f, 198.0f), 180, spawns);
        Transform cps = Child(s_play, "Checkpoints");
        Transform cpStreet = Point("CP_Street", start.position, 0, cps);
        Transform cpPlaza = Point("CP_Plaza", new Vector3(0, 0.02f, 56), 0, cps);
        Transform cpDock = Point("CP_Dock", new Vector3(40.5f, 0.02f, 123.5f), 30, cps); // 첫 컨테이너 엄폐 뒤
        Transform cpPlat = Point("CP_Platform", new Vector3(59, 1.22f, 155.4f), -90, cps);
        Transform cpSide = Point("CP_Side", new Vector3(47, 1.22f, 179.5f), 0, cps);
        Transform gateGoal = Point("Goal_Gate", new Vector3(0, 0, 97), 0, s_play);
        Transform passGoal = Point("Goal_Passage", new Vector3(47, 1.2f, 155.2f), 0, s_play);
        Transform rampGoal = Point("Goal_Ramp", new Vector3(64, 0, 145.0f), 0, s_play);
        Transform look = Point("LookTarget_Emblem", new Vector3(0, 62, 284.0f), 180, s_play);

        // 출구 = 기존 SceneMove(SceneLoadInteractable → Level_Facility)를 로비 문 패널로
        GameObject sceneMove = GameObject.Find("SceneMove");
        SceneLoadInteractable exit = null;
        if (sceneMove != null)
        {
            sceneMove.transform.SetPositionAndRotation(new Vector3(50.45f, 2.5f, 210.6f), Quaternion.Euler(0, -90, 0));
            sceneMove.transform.localScale = Vector3.one;
            var bc = sceneMove.GetComponent<BoxCollider>();
            if (bc != null) { bc.size = new Vector3(2.6f, 2.6f, 2.6f); bc.center = new Vector3(0, 0, 0.9f); }
            var mr = sceneMove.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            for (int i = sceneMove.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(sceneMove.transform.GetChild(i).gameObject);
            GameObject panel = Kit("DoorPanel", sceneMove.transform.position + Vector3.down * 0.4f, -90, sceneMove.transform, false, "LobbyDoorPanel");
            panel.transform.localPosition = new Vector3(0, -0.4f, 0);
            panel.transform.localRotation = Quaternion.identity;
            exit = sceneMove.GetComponent<SceneLoadInteractable>();
            if (exit != null) { exit.sceneName = "Level_Facility"; exit.promptText = "F: 로비 진입"; EditorUtility.SetDirty(exit); }
        }
        else Log.Append("WARN SceneMove missing; ");

        // 디렉터
        var dirGo = new GameObject("InfiltrationTutorial");
        dirGo.transform.SetParent(s_play, false);
        dirGo.AddComponent<TutorialFlowController>();
        var d = dirGo.AddComponent<InfiltrationTutorialDirector>();
        var so = new SerializedObject(d);
        void Set(string prop, Object v) { var p = so.FindProperty(prop); if (p == null) Log.Append("noprop " + prop + "; "); else p.objectReferenceValue = v; }
        Set("playerStart", start);
        Set("zoneMove", zMove); Set("zoneScanPassed", zScan); Set("zoneDockYard", zDock); Set("zoneSideYard", zSide); Set("zoneExit", zExit);
        Set("lookTarget", look); Set("gateGoal", gateGoal); Set("passageGoal", passGoal); Set("rampGoal", rampGoal);
        Set("cpStreet", cpStreet); Set("cpPlaza", cpPlaza); Set("cpDock", cpDock); Set("cpPlatform", cpPlat); Set("cpSide", cpSide);
        Set("androidPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(MobDir + "/Mob_SecurityAndroid.prefab"));
        Set("enforcerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(MobDir + "/Mob_ShieldEnforcer.prefab"));
        Set("wallTurretPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(MobDir + "/Mob_SentryTurret_Wall.prefab"));
        Set("floorTurretPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(MobDir + "/Mob_SentryTurret_Floor.prefab"));
        Set("androidPatrolSpawn", aSpawn); Set("boothSpawn", booth); Set("wallTurretMount", wallT); Set("platformTurretSpawn", platT);
        Set("enforcerSpawn", enf); Set("sideTurretSpawn", sideT);
        var pp = so.FindProperty("androidPatrolPoints"); pp.arraySize = 2; pp.GetArrayElementAtIndex(0).objectReferenceValue = pa; pp.GetArrayElementAtIndex(1).objectReferenceValue = pbP;
        Set("checkpointGate", gate); Set("passageBarrier", passage); Set("scanner", scanner);
        Set("exitInteractable", exit);
        Set("simFxMaterial", M["SimFx"]);
        so.ApplyModifiedPropertiesWithoutUndo();
        return d;
    }

    static void UpsertDialogue()
    {
        var table = AssetDatabase.LoadAssetAtPath<DialogueTable>("Assets/Resources/Dialogue/DialogueTable_KO.asset");
        if (table == null) { Log.Append("WARN dialogue table missing; "); return; }
        var so = new SerializedObject(table);
        SerializedProperty entries = so.FindProperty("entries");
        int added = 0, updated = 0;
        foreach (InfiltrationTutorialDirector.Line l in InfiltrationTutorialDirector.DefaultLines)
        {
            int idx = -1;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue == l.key) { idx = i; break; }
            if (idx < 0) { idx = entries.arraySize; entries.arraySize++; added++; }
            else
            {
                SerializedProperty e0 = entries.GetArrayElementAtIndex(idx);
                if (e0.FindPropertyRelative("text").stringValue == l.text && e0.FindPropertyRelative("speaker").stringValue == l.speaker) continue;
                updated++;
            }
            SerializedProperty e = entries.GetArrayElementAtIndex(idx);
            e.FindPropertyRelative("key").stringValue = l.key;
            e.FindPropertyRelative("speaker").stringValue = l.speaker;
            e.FindPropertyRelative("text").stringValue = l.text;
            e.FindPropertyRelative("duration").floatValue = 0f;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(table);
        table.Rebuild();
        Log.Append($"dialogue +{added} ~{updated}; ");
    }
}
#endif
