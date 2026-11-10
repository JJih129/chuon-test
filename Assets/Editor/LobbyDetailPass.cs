using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Level_Facility 본사 로비(상층 Y+40) + 엘리베이터/B4 착지 디테일 패스.
/// 에셋: Tools/ArtGen/lobby_detail_textures.py + lobby_detail_blender.py → Assets/Art/ChuonPolish/Level_Facility/Detail/
///  (트림 시트는 튜토리얼 M_INFD_Trim, 슬로건은 M_INF_Signs 를 그대로 재사용, 로비 전용 셀만 T_LFD_Atlas).
///  - "Tools/Chuon/Level/Lobby Detail/Rebuild Lobby Detail Prefabs": 텍스처 설정·URP Lit 재질·FBX 재질 리맵·LODGroup 프리팹 (프리뷰 씬, 열린 씬 미수정).
///  - "Tools/Chuon/Level/Apply Lobby Detail Pass": 열린 Level_Facility 에서 LF_LevelDesign/11_LobbyDetailPass 아래에
///    LFD_Layout.json 슬롯대로 결정론적으로 배치. 플레이어 경로(NavMesh 경로)·전투 구역·게임플레이 지점(CineAnchor_*,
///    스폰/몹/트리거)·기존 콜라이더와 겹치면 바닥 소품은 건너뛴다. 바닥 소품은 NavMeshObstacle(carving).
///    기존 오브젝트는 하나도 수정하지 않는다(추가만). Undo 한 번으로 되돌림, 씬 저장 안 함.
///  - "Tools/Chuon/Level/Remove Lobby Detail Pass": 11_LobbyDetailPass 삭제.
/// 자동 실행 없음 (EditorAutoFixupGate 규칙).
/// </summary>
public static class LobbyDetailPass
{
    const string ScenePath = "Assets/Scenes/Level_Facility.unity";
    const string Root = "Assets/Art/ChuonPolish/Level_Facility/Detail";
    const string MeshDir = Root + "/Meshes";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";
    const string LayoutPath = Root + "/LFD_Layout.json";
    const string TrimMatPath = "Assets/Art/ChuonPolish/Tutorial/Detail/Materials/M_INFD_Trim.mat";
    const string SignsMatPath = "Assets/Art/ChuonPolish/Tutorial/Materials/M_INF_Signs.mat";
    const string PassName = "11_LobbyDetailPass";

    enum Kind { Arch, Ceiling, Prop, Decal, Sign, Light, Overlay }

    struct Def
    {
        public string name; public Kind kind; public float cull; public float[] lods; public Bounds[] cols;
        public Def(string n, Kind k, float cull, params float[] lods) { name = n; kind = k; this.cull = cull; this.lods = lods; cols = null; }
        public Def Cols(params Bounds[] b) { cols = b; return this; }
    }

    static Bounds Box(float cx, float cy, float cz, float sx, float sy, float sz) => new Bounds(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));

    // 화면 높이 비율 LOD (QualitySettings.lodBias 곱). 벽/천장 모듈은 실내라 멀리서도 유지, 소품은 일찍 LOD1.
    static readonly Def[] Defs =
    {
        new Def("WallPanel_A", Kind.Arch, 0.012f, 0.3f), new Def("WallPanel_B", Kind.Arch, 0.012f, 0.3f),
        new Def("WallPanel_Tall", Kind.Arch, 0.01f, 0.35f), new Def("CorridorPanel", Kind.Arch, 0.012f, 0.3f),
        new Def("ColumnClad", Kind.Prop, 0.012f, 0.3f),
        new Def("CeilingCoffer_Aisle", Kind.Ceiling, 0.008f), new Def("CeilingCoffer_Rear", Kind.Ceiling, 0.008f),
        new Def("CeilingCoffer_Nave", Kind.Ceiling, 0.006f), new Def("CorridorCeiling", Kind.Ceiling, 0.008f),
        new Def("GlassBalustrade_2m", Kind.Prop, 0.015f, 0.2f),
        new Def("FloorInlay_Emblem", Kind.Decal, 0.01f), new Def("FloorInlay_Field3", Kind.Decal, 0.01f),
        new Def("FloorInlay_Runner", Kind.Decal, 0.01f), new Def("HazardFloorEdge", Kind.Decal, 0.015f),
        new Def("SignWayfinding_Lift", Kind.Sign, 0.012f), new Def("SignWayfinding_Showroom", Kind.Sign, 0.012f),
        new Def("SignWayfinding_Reception", Kind.Sign, 0.012f), new Def("SignWayfinding_Skydeck", Kind.Sign, 0.012f),
        new Def("SignFloor_1F", Kind.Sign, 0.012f), new Def("SignFloor_B4", Kind.Sign, 0.012f),
        new Def("SloganLightbox_1", Kind.Sign, 0.01f), new Def("SloganLightbox_4", Kind.Sign, 0.01f), new Def("SloganLightbox_7", Kind.Sign, 0.01f),
        new Def("WarnPlate", Kind.Sign, 0.015f),
        new Def("ElevatorPortal_Bank", Kind.Arch, 0.012f, 0.25f),
        // 메인 리프트 기둥: 깊이 0.32 m 라 플레이어·카메라가 파고들지 않게 콜라이더만 (carving 불필요, 벽에 붙음)
        new Def("ElevatorPortal_Main", Kind.Arch, 0.01f, 0.25f).Cols(Box(3.5f, 3.6f, 0.16f, 1.04f, 7.2f, 0.34f), Box(-3.5f, 3.6f, 0.16f, 1.04f, 7.2f, 0.34f)),
        new Def("ReceptionDeskTop", Kind.Overlay, 0.03f), new Def("ReceptionChair", Kind.Prop, 0.03f, 0.15f),
        new Def("WaitingBench", Kind.Prop, 0.02f, 0.15f), new Def("Planter_Low", Kind.Prop, 0.02f, 0.15f), new Def("Planter_Tall", Kind.Prop, 0.02f, 0.15f),
        new Def("HoloInfoPillar", Kind.Prop, 0.02f, 0.15f),
        // 보안 스캔 아치: 기둥에만 콜라이더 (가운데 3.8 m 통로는 비움)
        new Def("SecurityScanArch", Kind.Overlay, 0.015f, 0.15f).Cols(Box(2.05f, 1.6f, 0f, 0.3f, 3.2f, 0.5f), Box(-2.05f, 1.6f, 0f, 0.3f, 3.2f, 0.5f)),
        new Def("SpeedGate_Pair", Kind.Prop, 0.02f, 0.15f),
        new Def("WallScreen_Ad", Kind.Sign, 0.01f), new Def("WallScreen_News", Kind.Sign, 0.01f),
        new Def("CleaningRobot", Kind.Prop, 0.03f),
        new Def("CableTray_4m", Kind.Ceiling, 0.015f, 0.15f), new Def("WallVent", Kind.Sign, 0.015f), new Def("DuctRun_4m", Kind.Arch, 0.012f, 0.15f),
        new Def("PipeRun_4m", Kind.Arch, 0.012f, 0.15f), new Def("B4_DoorPost", Kind.Arch, 0.012f, 0.15f),
        new Def("B4_DoorHeader_3", Kind.Arch, 0.012f), new Def("B4_DoorHeader_6", Kind.Arch, 0.012f),
        new Def("LightRig_LinearPendant", Kind.Light, 0.008f, 0.12f), new Def("LightRig_CageLamp", Kind.Light, 0.015f),
    };

    static readonly System.Text.StringBuilder Log = new System.Text.StringBuilder();
    static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

    // ================================================================== prefabs
    [MenuItem("Tools/Chuon/Level/Lobby Detail/Rebuild Lobby Detail Prefabs")]
    public static void RebuildPrefabsMenu() => Debug.Log(RebuildPrefabs());

    /// <summary>MCP 진입점. 열린 씬을 수정하지 않는다.</summary>
    public static string RebuildPrefabs()
    {
        Log.Clear();
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "ABORT: play mode";
        AssetDatabase.Refresh();
        TextureSettings();
        BuildMaterials();
        int models = RemapModels();
        int prefabs = 0;
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (Def d in Defs)
                if (BuildPrefab(d, preview)) prefabs++;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        AssetDatabase.SaveAssets();
        Log.Append($"models={models} prefabs={prefabs}/{Defs.Length}");
        return Log.ToString();
    }

    static void TextureSettings()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(p).StartsWith("T_LFD_")) continue;
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            bool ch = false;
            bool normal = p.Contains("_Normal"), mask = p.Contains("_Mask");
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
            if (mask && ti.sRGBTexture) { ti.sRGBTexture = false; ch = true; }
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ch = true; }
            if (ti.anisoLevel < 4) { ti.anisoLevel = 4; ch = true; }
            if (ch) ti.SaveAndReimport();
        }
    }

    static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{n}.png");

    static Material MatAsset(string name)
    {
        Directory.CreateDirectory(MatDir);
        string path = $"{MatDir}/M_{name}.mat";
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        m.enableInstancing = true;
        return m;
    }

    static void BuildMaterials()
    {
        Mats.Clear();
        // 로비 아틀라스: 사인·스크린·인디케이터 발광 (Emission 맵 × 2.2), 금속/스무스니스 = Mask (트림과 같은 규약)
        Material a = MatAsset("LFD_Atlas");
        a.SetFloat("_WorkflowMode", 1f);
        a.SetColor("_BaseColor", Color.white);
        a.SetTexture("_BaseMap", Tex("T_LFD_Atlas_Albedo"));
        a.SetFloat("_Smoothness", 1f); a.SetFloat("_Metallic", 1f);
        Texture2D n = Tex("T_LFD_Atlas_Normal"), k = Tex("T_LFD_Atlas_Mask"), e = Tex("T_LFD_Atlas_Emission");
        a.SetTexture("_BumpMap", n); a.SetFloat("_BumpScale", 1f);
        if (n != null) a.EnableKeyword("_NORMALMAP"); else a.DisableKeyword("_NORMALMAP");
        a.SetTexture("_MetallicGlossMap", k); a.SetTexture("_OcclusionMap", k); a.SetFloat("_OcclusionStrength", 0.85f);
        if (k != null) { a.EnableKeyword("_METALLICSPECGLOSSMAP"); a.EnableKeyword("_OCCLUSIONMAP"); }
        a.SetTexture("_EmissionMap", e); a.SetColor("_EmissionColor", new Color(1f, 0.98f, 0.95f) * 2.2f); a.EnableKeyword("_EMISSION");
        a.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(a);
        Mats["LFD_Atlas"] = a;

        // 유리 난간·게이트 플랩: URP Lit Transparent (Alpha blend, ZWrite off)
        Material g = MatAsset("LFD_Glass");
        g.SetFloat("_WorkflowMode", 1f);
        g.SetColor("_BaseColor", new Color(0.72f, 0.8f, 0.84f, 0.16f));
        g.SetTexture("_BaseMap", null);
        g.SetFloat("_Smoothness", 0.96f); g.SetFloat("_Metallic", 0f);
        g.SetFloat("_Surface", 1f); g.SetFloat("_Blend", 0f); g.SetFloat("_AlphaClip", 0f);
        g.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); g.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (g.HasProperty("_SrcBlendAlpha")) { g.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); g.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha); }
        g.SetFloat("_ZWrite", 0f); g.SetFloat("_Cull", (float)CullMode.Off);
        g.SetOverrideTag("RenderType", "Transparent");
        g.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); g.DisableKeyword("_ALPHAPREMULTIPLY_ON"); g.DisableKeyword("_ALPHATEST_ON");
        g.SetShaderPassEnabled("ShadowCaster", false);
        g.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(g);
        Mats["LFD_Glass"] = g;

        Mats["INFD_Trim"] = AssetDatabase.LoadAssetAtPath<Material>(TrimMatPath);
        Mats["INF_Signs"] = AssetDatabase.LoadAssetAtPath<Material>(SignsMatPath);
        if (Mats["INFD_Trim"] == null) Log.Append("WARN M_INFD_Trim missing (run Tools/Chuon/Tutorial/Detail/Rebuild Detail Prefabs); ");
        if (Mats["INF_Signs"] == null) Log.Append("WARN M_INF_Signs missing; ");
        AssetDatabase.SaveAssets();
    }

    static int RemapModels()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MeshDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            bool ch = false;
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> map = mi.GetExternalObjectMap();
            foreach (KeyValuePair<string, Material> kv in Mats)
            {
                if (kv.Value == null) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                if (!map.TryGetValue(id, out Object cur) || cur != kv.Value) { mi.AddRemap(id, kv.Value); ch = true; }
            }
            if (mi.importCameras || mi.importLights || mi.importAnimation || mi.importBlendShapes)
            { mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.importBlendShapes = false; ch = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; ch = true; }
            if (mi.isReadable) { mi.isReadable = false; ch = true; }
            if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; ch = true; }
            if (ch) mi.SaveAndReimport();
            count++;
        }
        return count;
    }

    static string Folder(Kind k)
    {
        switch (k)
        {
            case Kind.Arch: return "Architecture";
            case Kind.Ceiling: return "Ceiling";
            case Kind.Decal: return "Floor";
            case Kind.Sign: return "Signage";
            case Kind.Light: return "Lighting";
            default: return "Props";
        }
    }

    static Def? Find(string name)
    {
        foreach (Def d in Defs) if (d.name == name) return d;
        return null;
    }

    static string PrefabPath(Def d) => $"{PrefabDir}/{Folder(d.kind)}/PF_LFD_{d.name}.prefab";

    static bool BuildPrefab(Def d, Scene preview)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/SM_LFD_{d.name}.fbx");
        if (model == null) { Log.Append($"MISSING model {d.name}; "); return false; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = "PF_LFD_" + d.name;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        bool noShadow = d.kind == Kind.Decal || d.kind == Kind.Ceiling || d.kind == Kind.Sign || d.name.StartsWith("GlassBalustrade");
        var byLod = new SortedDictionary<int, List<Renderer>>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            int lod = 0;
            int i = r.name.LastIndexOf("_LOD");
            if (i >= 0) int.TryParse(r.name.Substring(i + 4), out lod);
            if (!byLod.TryGetValue(lod, out List<Renderer> list)) byLod[lod] = list = new List<Renderer>();
            list.Add(r);
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            r.shadowCastingMode = noShadow ? ShadowCastingMode.Off : ShadowCastingMode.On;
            r.receiveShadows = true;
            if (r is MeshRenderer mr) mr.lightProbeUsage = LightProbeUsage.BlendProbes;
        }
        LODGroup lg = go.GetComponent<LODGroup>();
        if (lg == null) lg = go.AddComponent<LODGroup>();
        var lods = new List<LOD>();
        int k = 0;
        foreach (KeyValuePair<int, List<Renderer>> kv in byLod)
        {
            float h = k < d.lods.Length ? d.lods[k] : d.cull;
            if (k == byLod.Count - 1) h = Mathf.Min(h, d.cull);
            lods.Add(new LOD(h, kv.Value.ToArray()));
            k++;
        }
        lg.SetLODs(lods.ToArray());
        lg.fadeMode = LODFadeMode.None;
        lg.RecalculateBounds();

        if (d.cols != null)
            foreach (Bounds b in d.cols) { var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size; }
        else if (d.kind == Kind.Prop && byLod.TryGetValue(0, out List<Renderer> l0))
        {
            Bounds b = LocalBounds(go.transform, l0);
            var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
            var ob = go.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box; ob.center = b.center; ob.size = b.size + new Vector3(0.1f, 0f, 0.1f);
            ob.carving = true; ob.carveOnlyStationary = true;
        }
        StaticEditorFlags f = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        if (d.kind == Kind.Arch) f |= StaticEditorFlags.OccluderStatic;
        if (d.kind != Kind.Decal && d.kind != Kind.Sign && d.kind != Kind.Light) f |= StaticEditorFlags.ContributeGI;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, f);

        string path = PrefabPath(d);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
        Object.DestroyImmediate(go);
        if (!ok) Log.Append($"FAILED prefab {d.name}; ");
        return ok;
    }

    static Bounds LocalBounds(Transform root, List<Renderer> rs)
    {
        bool init = false; var b = new Bounds();
        foreach (Renderer r in rs)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // ================================================================== layout (LFD_Layout.json, written by lobby_detail_blender.py)
    [System.Serializable] class SlotData
    {
        public string room, p, frame, kind, light;
        public float x, y, z, yaw, clr, off, sx, sy;
        public int nosnap, zoneOk;
    }
    [System.Serializable] class ZoneData { public string frame; public float x0, z0, x1, z1; }
    [System.Serializable] class LayoutData { public int version; public SlotData[] slots; public ZoneData[] zones; }

    // ================================================================== apply
    [MenuItem("Tools/Chuon/Level/Apply Lobby Detail Pass")]
    public static void ApplyMenu() => Debug.Log(Apply());

    [MenuItem("Tools/Chuon/Level/Remove Lobby Detail Pass")]
    public static void RemoveMenu() => Debug.Log(Remove());

    static bool CheckScene(out Scene scene, out Transform levelDesign, out string err)
    {
        scene = SceneManager.GetActiveScene(); levelDesign = null; err = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { err = "ABORT: play mode"; return false; }
        if (scene.path != ScenePath) { err = "ABORT: open " + ScenePath + " first (active: " + scene.path + ")"; return false; }
        foreach (GameObject g in scene.GetRootGameObjects())
            if (g.name == "LF_LevelDesign") levelDesign = g.transform;
        if (levelDesign == null) { err = "ABORT: LF_LevelDesign root missing"; return false; }
        return true;
    }

    static Transform FindIn(Scene scene, string name, Transform under = null)
    {
        foreach (GameObject g in scene.GetRootGameObjects())
        {
            if (under != null && g.transform != under.root) continue;
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                if (t.name == name && (under == null || t.IsChildOf(under))) return t;
        }
        return null;
    }

    /// <summary>MCP/메뉴 진입점. 반환 = 로그. 씬 저장은 하지 않는다 (dirty 만 표시).</summary>
    public static string Apply()
    {
        Log.Clear();
        if (!CheckScene(out Scene scene, out Transform lfd, out string err)) return err;
        if (!File.Exists(LayoutPath)) return "ABORT: " + LayoutPath + " missing (run Tools/ArtGen/lobby_detail_blender.py)";
        LayoutData layout = JsonUtility.FromJson<LayoutData>(File.ReadAllText(LayoutPath));
        if (layout == null || layout.slots == null) return "ABORT: layout parse failed";
        foreach (Def d in Defs)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(d)) == null) { Log.Append(RebuildPrefabs() + " | "); break; }

        // 방(프레임) 찾기: 로비 쉘(lobby_object) 위치 = 홀 원점. 없으면 문서상 기본값 (0, 40, 0).
        Transform upper = FindIn(scene, "UpperFloor_HQLobby");
        Transform shell = upper != null ? FindIn(scene, "lobby_object", upper) : null;
        Vector3 hall = shell != null ? shell.position : new Vector3(0f, 40f, 0f);
        if (shell == null) Log.Append("WARN lobby_object not found, hall origin (0,40,0); ");
        string[] rooms = { "01_EntryCorridor", "02_AtriumSkydeck", "10_HQLobby", "06_FrontDeskLift", "07_ArenaExit" };
        foreach (string r in rooms) if (lfd.Find(r) == null) Log.Append($"WARN room {r} missing; ");

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Lobby Detail Pass");
        int group = Undo.GetCurrentGroup();
        RemoveInternal(lfd);

        var root = new GameObject(PassName).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Lobby detail root");
        Undo.SetTransformParent(root, lfd, "Lobby detail root");
        root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
        var roomRoots = new Dictionary<string, Transform>();
        Transform Room(string n)
        {
            if (roomRoots.TryGetValue(n, out Transform t)) return t;
            var go = new GameObject(n);
            Undo.RegisterCreatedObjectUndo(go, "Lobby detail room");
            go.transform.SetParent(root, false);
            return roomRoots[n] = go.transform;
        }

        Physics.SyncTransforms();
        var ex = new Exclusion(scene, root, hall, layout.zones);
        int placed = 0, skipped = 0, lights = 0;
        var reasons = new Dictionary<string, int>();
        void Count(string why) { skipped++; reasons[why] = reasons.TryGetValue(why, out int c) ? c + 1 : 1; }

        foreach (SlotData s in layout.slots)
        {
            Vector3 origin = s.frame == "world" ? Vector3.zero : hall;
            GameObject g = TryPlace(s, origin + new Vector3(s.x, s.y, s.z), Room(s.room), ex, out string why);
            if (g == null) { Count(why); continue; }
            placed++;
            if (!string.IsNullOrEmpty(s.light)) lights += AddLight(s.light, g, Room("09_Lighting"));
            Physics.SyncTransforms();
        }

        // 비활성 엘리베이터 뱅크: 각 Lift_* 문 위치에서 포털 프레임 (기존 문·프레임·홀로는 그대로)
        int portals = 0;
        Transform bank = FindIn(scene, "ElevatorBank_Inactive");
        if (bank != null)
            foreach (Transform lift in bank)
            {
                Transform a = lift.Find("Door_A"), b = lift.Find("Door_B");
                if (a == null || b == null) continue;
                Vector3 c = (RendererCentre(a) + RendererCentre(b)) * 0.5f;
                float side = Mathf.Sign(c.x - hall.x);
                Vector3 front = new Vector3(-side, 0f, 0f);
                Vector3 p = new Vector3(c.x, hall.y, c.z);
                p.x = hall.x + side * 12.03f;  // 측정값 (lobby_object 바깥벽) — 레이로 확인되면 덮어씀
                foreach (RaycastHit hit in SortedHits(p + Vector3.up * 1.5f + front * 1.5f, -front, 3f))
                {
                    if (hit.collider.transform.IsChildOf(lift) || hit.collider.transform.IsChildOf(root) || Vector3.Dot(hit.normal, front) < 0.5f) continue;
                    p.x = hit.point.x; break;
                }
                if (Place("ElevatorPortal_Bank", p + front * 0.002f, Quaternion.LookRotation(front, Vector3.up), Room("05_ElevatorBank")) != null) portals++;
            }
        Log.Append($"bankPortals={portals}; ");

        // B4 격납고 문(ArenaDressing/Props/Door5): 해저드 기둥·헤더·바닥 해저드 (문 크기에 맞춤)
        int b4 = 0;
        Transform dressing = FindIn(scene, "ArenaDressing");
        if (dressing != null)
            foreach (Transform t in dressing.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Door5") continue;
                if (!DoorLocalBounds(t, out Bounds lb)) continue;
                Vector3 fwd = t.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 baseC = t.TransformPoint(new Vector3(lb.center.x, 0f, 0f)); baseC.y = t.position.y;
                float plane = WallPlane(t, baseC, fwd);
                Vector3 onWall = baseC + fwd * plane;
                float halfW = lb.extents.x, h = Mathf.Max(2.5f, lb.max.y);
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                Transform room = Room("08_B4");
                foreach (float sgn in new[] { -1f, 1f })
                {
                    GameObject post = Place("B4_DoorPost", onWall + right * sgn * (halfW + 0.2f) + fwd * 0.002f, rot, room);
                    if (post != null) { post.transform.localScale = new Vector3(1f, Mathf.Clamp(h / 4f, 0.6f, 2f), 1f); b4++; }
                }
                float span = halfW * 2f + 0.76f;
                string hdr = span > 4.5f ? "B4_DoorHeader_6" : "B4_DoorHeader_3";
                GameObject header = Place(hdr, onWall + Vector3.up * h + fwd * 0.002f, rot, room);
                if (header != null) { header.transform.localScale = new Vector3(Mathf.Clamp(span / (span > 4.5f ? 6f : 3f), 0.6f, 1.6f), 1f, 1f); b4++; }
                GameObject edge = Place("HazardFloorEdge", onWall + fwd * 0.45f + Vector3.up * 0.006f, rot, room);
                if (edge != null) { edge.transform.localScale = new Vector3(Mathf.Clamp(halfW * 2f / 2.6f, 0.5f, 2.5f), 1f, 1f); b4++; }
            }
        Log.Append($"b4DoorTrims={b4}; ");

        lights += Probes(Room("09_Lighting"), hall);
        Log.Append($"placed={placed} skipped={skipped} (");
        foreach (KeyValuePair<string, int> kv in reasons) Log.Append($"{kv.Key}:{kv.Value} ");
        Log.Append($") rigLights={lights}; route={ex.routeSource}; ");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Log.Append("done (not saved; Ctrl+Z reverts)");
        return Log.ToString();
    }

    public static string Remove()
    {
        Log.Clear();
        if (!CheckScene(out Scene scene, out Transform lfd, out string err)) return err;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Remove Lobby Detail Pass");
        int group = Undo.GetCurrentGroup();
        RemoveInternal(lfd);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        return Log.Length > 0 ? Log.ToString() : "nothing to remove";
    }

    static void RemoveInternal(Transform lfd)
    {
        Transform d = lfd.Find(PassName);
        if (d != null) { Undo.DestroyObjectImmediate(d.gameObject); Log.Append("removed old pass; "); }
    }

    static GameObject Place(string name, Vector3 pos, Quaternion rot, Transform parent, bool registerUndo = true)
    {
        Def? d = Find(name);
        GameObject pf = d.HasValue ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(d.Value)) : null;
        if (pf == null) { Log.Append($"MISSING prefab {name}; "); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        if (registerUndo) Undo.RegisterCreatedObjectUndo(go, "Lobby detail place");
        return go;
    }

    static Vector3 RendererCentre(Transform t)
    {
        var r = t.GetComponent<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    static bool DoorLocalBounds(Transform door, out Bounds b)
    {
        b = new Bounds(); bool init = false;
        foreach (MeshFilter mf in door.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = door.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
            }
        }
        return init && b.size.x > 0.5f;
    }

    /// <summary>문 뒤 벽면까지의 거리(전방 기준, 음수 = 뒤). 못 찾으면 0 (문 루트 평면).</summary>
    static float WallPlane(Transform door, Vector3 p, Vector3 fwd)
    {
        Vector3 o = p + Vector3.up * 1.2f + fwd * 1.5f;
        foreach (RaycastHit h in SortedHits(o, -fwd, 3f))
        {
            if (h.collider.transform.IsChildOf(door)) continue;
            return Vector3.Dot(h.point - p, fwd);
        }
        return 0f;
    }

    static RaycastHit[] SortedHits(Vector3 o, Vector3 dir, float dist)
    {
        RaycastHit[] hits = Physics.RaycastAll(o, dir, dist, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return hits;
    }

    // ------------------------------------------------------------------ exclusion
    class Exclusion
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<Bounds> triggers = new List<Bounds>();
        public readonly List<List<Vector3>> routes = new List<List<Vector3>>();
        public readonly List<KeyValuePair<Rect, float>> zones = new List<KeyValuePair<Rect, float>>();  // (XZ rect, floor Y)
        public readonly Transform root;
        public string routeSource = "";

        static readonly string[] PointPrefixes = { "CineAnchor_", "SpawnPoint_", "Patrol", "Mob_", "LF_Exit", "LobbyManager", "ElevatorButton", "PlayerRoot", "GuidePath" };

        public Exclusion(Scene scene, Transform root, Vector3 hall, ZoneData[] zoneData)
        {
            this.root = root;
            foreach (GameObject g in scene.GetRootGameObjects())
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                {
                    if (t.IsChildOf(root)) continue;
                    foreach (string pre in PointPrefixes)
                        if (t.name.StartsWith(pre)) { points.Add(t.position); break; }
                    foreach (Collider c in t.GetComponents<Collider>())
                        if (c.isTrigger) triggers.Add(c.bounds);
                }
            if (zoneData != null)
                foreach (ZoneData z in zoneData)
                {
                    Vector3 o = z.frame == "world" ? Vector3.zero : hall;
                    zones.Add(new KeyValuePair<Rect, float>(Rect.MinMaxRect(o.x + z.x0, o.z + z.z0, o.x + z.x1, o.z + z.z1), o.y));
                }
            // 경로: 상층(시작 → 복도 → 스카이덱 → 데스크 → 전용 엘리베이터), 하층(엘리베이터 착지 → 보스)
            Vector3? Named(string n) { Transform t = FindIn(scene, n); return t != null ? t.position : (Vector3?)null; }
            var upper = new List<Vector3?> { Named("PlayerRoot") ?? hall + new Vector3(0, 0, 29f), hall + new Vector3(0, 0, 21f), hall + new Vector3(0, 0, 12f),
                                             hall + new Vector3(0, 0, 3f), Named("Trigger_Approach"), Named("Trigger_Board") };
            var lower = new List<Vector3?> { new Vector3(0, 0, -17.5f), Named("boss") ?? new Vector3(0, 0, 15f) };
            Build(upper); Build(lower);
        }

        void Build(List<Vector3?> wps)
        {
            var pts = new List<Vector3>();
            foreach (Vector3? p in wps) if (p.HasValue) pts.Add(p.Value);
            var route = new List<Vector3>();
            int nav = 0, straight = 0;
            var path = new NavMeshPath();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                bool ok = NavMesh.SamplePosition(pts[i], out NavMeshHit a, 3f, NavMesh.AllAreas)
                          && NavMesh.SamplePosition(pts[i + 1], out NavMeshHit b, 3f, NavMesh.AllAreas)
                          && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                          && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 1;
                if (ok) { route.AddRange(path.corners); nav++; }
                else { route.Add(pts[i]); route.Add(pts[i + 1]); straight++; }
            }
            routes.Add(route);
            routeSource += $"[nav {nav}/straight {straight}]";
        }

        static float RectDist(Bounds b, Vector3 p)
        {
            float dx = Mathf.Max(b.min.x - p.x, 0f, p.x - b.max.x);
            float dz = Mathf.Max(b.min.z - p.z, 0f, p.z - b.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>null = OK, 아니면 거절 사유.</summary>
        public string Check(Bounds b, float clr, bool zoneOk, bool ground)
        {
            // clr <= 0 = 의도적으로 기준점 위에 얹는 오버레이 (예: 데스크 위 소품 ↔ CineAnchor_LobbyFrontDesk)
            float pr = clr <= 0f ? 0f : Mathf.Min(2.0f, clr + 0.6f);
            foreach (Vector3 p in points)
                if (pr > 0f && Mathf.Abs(p.y - b.center.y) < 6f && RectDist(b, p) < pr) return "gameplayPoint";
            if (ground)
                foreach (Bounds t in triggers)
                {
                    // 트리거 중심부(가로세로 50%)를 막지 않는다 — 가장자리에 걸친 난간 등은 허용
                    Bounds core = new Bounds(t.center, new Vector3(t.size.x * 0.5f, t.size.y + 2f, t.size.z * 0.5f));
                    if (core.Intersects(b)) return "trigger";
                }
            if (ground && !zoneOk)
                foreach (KeyValuePair<Rect, float> zv in zones)
                {
                    Rect z = zv.Key;  // 존은 XZ 만 있으므로 바닥 높이로 층(홀 Y≈40 / B4 Y≈0)을 구분
                    if (Mathf.Abs(b.min.y - zv.Value) < 6f && b.max.x > z.xMin && b.min.x < z.xMax && b.max.z > z.yMin && b.min.z < z.yMax) return "combatZone";
                }
            foreach (List<Vector3> route in routes)
                for (int i = 0; i + 1 < route.Count; i++)
                {
                    Vector3 p0 = route[i], p1 = route[i + 1];
                    if (Mathf.Abs(p0.y - b.min.y) > 3f && Mathf.Abs(p1.y - b.min.y) > 3f) continue;
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p0, p1) / 0.4f));
                    for (int k = 0; k <= n; k++)
                        if (RectDist(b, Vector3.Lerp(p0, p1, k / (float)n)) < clr) return "route";
                }
            return null;
        }

    }

    static Bounds WorldBounds(GameObject g)
    {
        LODGroup lg = g.GetComponent<LODGroup>();
        Renderer[] rs = lg != null && lg.lodCount > 0 ? lg.GetLODs()[0].renderers : g.GetComponentsInChildren<Renderer>();
        bool init = false; var b = new Bounds(g.transform.position, Vector3.zero);
        foreach (Renderer r in rs) { if (r == null) continue; if (!init) { b = r.bounds; init = true; } else b.Encapsulate(r.bounds); }
        return b;
    }

    static GameObject TryPlace(SlotData s, Vector3 pos, Transform parent, Exclusion ex, out string why)
    {
        why = null;
        Quaternion rot = Quaternion.Euler(0f, s.yaw, 0f);
        Vector3 front = rot * Vector3.forward;
        Def? def = Find(s.p);
        if (!def.HasValue) { why = "unknownPrefab"; return null; }
        switch (s.kind)
        {
            case "ground":
            case "decal":
                if (Ground(pos, ex.root, out float gy)) pos.y = gy;
                else if (s.kind == "ground") { why = "noGround"; return null; }
                if (s.kind == "decal") pos.y += 0.006f;
                break;
            case "wall":
                if (s.nosnap == 0)
                {
                    float mid = Mathf.Clamp(def.Value.kind == Kind.Sign ? 0.2f : 1.0f, 0.1f, 2f);
                    Vector3 o = pos + Vector3.up * mid + front * 1.0f;
                    bool hit = false;
                    foreach (RaycastHit h in SortedHits(o, -front, 2.0f))
                    {
                        if (h.collider.transform.IsChildOf(ex.root) || Vector3.Dot(h.normal, front) < 0.5f) continue;
                        pos += front * Vector3.Dot(h.point - pos, front);
                        hit = true; break;
                    }
                    if (!hit) Log.Append($"unsnapped {s.p}@{s.x:0.#},{s.z:0.#}; ");
                }
                pos += front * (s.off + 0.002f);
                break;
            case "overhead":
                if (s.nosnap == 0)
                    foreach (RaycastHit h in SortedHits(pos - Vector3.up * 0.6f, Vector3.up, 1.2f))
                    {
                        if (h.collider.transform.IsChildOf(ex.root) || h.normal.y > -0.5f) continue;
                        pos.y = h.point.y - 0.002f; break;
                    }
                break;
        }
        GameObject g = Place(s.p, pos, rot, parent, false);
        if (g == null) { why = "missingPrefab"; return null; }
        if (!Mathf.Approximately(s.sx, 1f) || !Mathf.Approximately(s.sy, 1f))
            g.transform.localScale = new Vector3(s.sx <= 0f ? 1f : s.sx, s.sy <= 0f ? 1f : s.sy, 1f);
        if (s.kind == "ground" || s.kind == "overlay")
        {
            Physics.SyncTransforms();
            Bounds b = WorldBounds(g);
            if (s.kind == "ground")
            {
                why = ex.Check(b, s.clr, s.zoneOk != 0, true);
                if (why == null) why = Overlaps(b, g, ex.root);
            }
            else
            {
                // 오버레이: 지나갈 수 있는 부분(아치 보·데스크 위 소품)은 경로를 막지 않으므로 콜라이더(기둥)만 검사
                why = ex.Check(b, 0f, true, false);
                foreach (Collider c in g.GetComponentsInChildren<Collider>())
                    if (why == null) why = ex.Check(c.bounds, s.clr, true, false);
            }
            if (why != null) { Object.DestroyImmediate(g); return null; }
        }
        Undo.RegisterCreatedObjectUndo(g, "Lobby detail place");
        return g;
    }

    static bool Ground(Vector3 p, Transform root, out float y)
    {
        y = 0f;
        foreach (RaycastHit h in SortedHits(p + Vector3.up * 1.0f, Vector3.down, 2.0f))
        {
            if (h.collider.transform.IsChildOf(root) || h.normal.y < 0.7f) continue;
            if (Mathf.Abs(h.point.y - p.y) > 0.5f) return false;
            y = h.point.y; return true;
        }
        return false;
    }

    static string Overlaps(Bounds b, GameObject self, Transform root)
    {
        Vector3 half = b.extents - new Vector3(0.03f, 0f, 0.03f);
        half.y = Mathf.Max(0.05f, half.y - 0.08f);
        foreach (Collider c in Physics.OverlapBox(b.center + Vector3.up * 0.06f, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(self.transform)) continue;
            if (c.transform.IsChildOf(root)) return "overlapDetail";
            // 바닥·천장 슬래브(넓고 얇은 콜라이더)와의 접촉은 무시
            Bounds cb = c.bounds;
            if (cb.max.y <= b.min.y + 0.1f || cb.min.y >= b.max.y - 0.1f) continue;
            if (c is MeshCollider mc && !mc.convex && cb.size.x > 15f && cb.size.z > 15f)
            {
                // 로비 쉘 같은 큰 메시 콜라이더: 상자 안에 실제 삼각형이 있는지 레이 4개로 근사 확인
                if (!ShellHits(b)) continue;
            }
            return "overlap:" + c.name;
        }
        return null;
    }

    static bool ShellHits(Bounds b)
    {
        float y = b.min.y + Mathf.Min(1.0f, b.size.y * 0.5f);
        Vector3 c = new Vector3(b.center.x, y, b.center.z);
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        foreach (Vector3 d in dirs)
        {
            float ext = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(d.x), 0, Mathf.Abs(d.z)))) - 0.03f;
            if (Physics.Raycast(c, d, Mathf.Max(0.01f, ext), ~0, QueryTriggerInteraction.Ignore)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ lighting (최소 실시간 광원 + 프로브)
    static int AddLight(string id, GameObject g, Transform parent)
    {
        Transform a = null;
        foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) if (t.name == "LightAnchor") a = t;
        var go = new GameObject("L_LFD_" + g.name);
        Undo.RegisterCreatedObjectUndo(go, "Lobby detail light");
        go.transform.SetParent(parent, false);
        go.transform.position = a != null ? a.position : g.transform.position;
        var l = go.AddComponent<Light>();
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.Auto;
        switch (id)
        {
            case "cage": l.type = LightType.Point; l.color = new Color(1f, 0.9f, 0.78f); l.intensity = 2.2f; l.range = 7f; break;
            default: l.type = LightType.Point; l.color = Color.white; l.intensity = 1f; l.range = 5f; break;
        }
        return 1;
    }

    /// <summary>
    /// 라이트 프로브 그룹(로비 홀·복도 격자; 라이팅을 굽기 전까지는 비활성 데이터) + 복도/전용 엘리베이터 구역 반사 프로브.
    /// URP 오브젝트당 추가광 제한 때문에 새 실시간 광원 대신 발광 재질 + 프로브로 밝기를 만든다.
    /// </summary>
    static int Probes(Transform parent, Vector3 hall)
    {
        var lp = new GameObject("LightProbes_Lobby");
        Undo.RegisterCreatedObjectUndo(lp, "Lobby probes");
        lp.transform.SetParent(parent, false);
        lp.transform.position = hall;
        var pos = new List<Vector3>();
        for (float x = -10.5f; x <= 10.6f; x += 3.5f)
            for (float z = -18f; z <= 18.1f; z += 4f)
            {
                bool core = Mathf.Abs(x) < 5f && z > -7f && z < -3.8f;
                bool desk = Mathf.Abs(x) < 3.8f && z > -1.4f && z < 0.4f;
                if (core || desk) continue;
                pos.Add(new Vector3(x, 0.6f, z)); pos.Add(new Vector3(x, 3.2f, z));
                if (Mathf.Abs(x) < 9f) pos.Add(new Vector3(x, 10f, z));
            }
        for (float z = 21f; z <= 33.1f; z += 3f) { pos.Add(new Vector3(0f, 1.0f, z)); pos.Add(new Vector3(0f, 2.8f, z)); }
        lp.AddComponent<LightProbeGroup>().probePositions = pos.ToArray();

        ReflectionProbe Rp(string n, Vector3 c, Vector3 size, int res)
        {
            var go = new GameObject(n);
            Undo.RegisterCreatedObjectUndo(go, "Lobby probes");
            go.transform.SetParent(parent, false);
            go.transform.position = hall + c;
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Realtime;
            rp.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            rp.boxProjection = true; rp.size = size; rp.resolution = res; rp.importance = 2;
            rp.blendDistance = 0.5f;
            return rp;
        }
        Rp("ReflectionProbe_EntryCorridor", new Vector3(0f, 1.8f, 26.6f), new Vector3(4.8f, 3.6f, 14.2f), 128);
        Rp("ReflectionProbe_ExecLift", new Vector3(0f, 2.5f, -12.6f), new Vector3(24f, 5f, 13.6f), 128);
        Log.Append($"lightProbes={pos.Count} reflectionProbes=2; ");
        return 0;
    }
}
