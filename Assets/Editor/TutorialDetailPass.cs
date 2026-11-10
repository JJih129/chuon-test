using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 도시 블록 디테일 패스 (반복 16).
/// 에셋: Tools/ArtGen/tutorial_detail_textures.py + tutorial_detail_blender.py → Assets/Art/ChuonPolish/Tutorial/Detail/.
///  - "Tools/Chuon/Tutorial/Detail/Rebuild Detail Prefabs": 텍스처 임포트 설정·URP Lit 재질·FBX 재질 리맵·LODGroup 프리팹 생성
///    (프리뷰 씬에서 만들기 때문에 열린 씬은 건드리지 않는다).
///  - "Tools/Chuon/Tutorial/Apply Detail Pass": 열린 Tutorial 씬에서 InfiltrationMapBuilder 의 평범한 블록(BlockA..D)을
///    디테일 건물로 교체(원본은 비활성), 가로등·볼라드는 외형만 교체(원본 콜라이더 유지), 경로 밖에 거리 소품·하역장 조명을
///    결정론적으로 배치. 전부 INF_ROOT/Detail 아래에 생기고 Undo 한 번으로 되돌릴 수 있다. 게임플레이 오브젝트는 건드리지 않는다.
///  - "Tools/Chuon/Tutorial/Detail/Remove Detail Pass": Detail 루트 삭제 + 원본 복구.
/// 자동 실행 없음 (EditorAutoFixupGate 규칙).
/// </summary>
public static class TutorialDetailPass
{
    const string ScenePath = "Assets/Scenes/Tutorial.unity";
    const string Root = "Assets/Art/ChuonPolish/Tutorial/Detail";
    const string MeshDir = Root + "/Meshes";
    const string GenDir = Root + "/Meshes/Generated";
    const string TexDir = Root + "/Textures";
    const string MatDir = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";
    const string SignsMatPath = "Assets/Art/ChuonPolish/Tutorial/Materials/M_INF_Signs.mat";
    const string DetailName = "Detail";

    enum Kind { Building, Modular, Swap, Ground, Wall, Overhead, Decal, Overlay }

    struct Def
    {
        public string name; public Kind kind; public float[] lods; public float cull;
        public Def(string n, Kind k, float cull, params float[] lods) { name = n; kind = k; this.cull = cull; this.lods = lods; }
    }

    // 화면 높이 비율 기준 LOD 전환 (QualitySettings.lodBias 0.3~2 가 곱해짐). 건물: 근경 LOD0, 원경 임포스터 박스.
    // 예) lodBias 1: BlockC(92 m) LOD0 → ~180 m, BlockD(28 m) → ~55 m.
    static readonly Def[] Defs =
    {
        new Def("BlockA_1", Kind.Building, 0.004f, 0.45f, 0.12f), new Def("BlockA_2", Kind.Building, 0.004f, 0.45f, 0.12f),
        new Def("BlockB_1", Kind.Building, 0.004f, 0.45f, 0.12f), new Def("BlockB_2", Kind.Building, 0.004f, 0.45f, 0.12f),
        new Def("BlockC_1", Kind.Building, 0.004f, 0.45f, 0.12f), new Def("BlockC_2", Kind.Building, 0.004f, 0.45f, 0.12f),
        new Def("BlockD_1", Kind.Building, 0.004f, 0.45f, 0.12f), new Def("BlockD_2", Kind.Building, 0.004f, 0.45f, 0.12f),
        new Def("FacadePanel_OFF", Kind.Modular, 0.01f), new Def("FacadePanel_CER", Kind.Modular, 0.01f),
        new Def("FacadePanel_RES", Kind.Modular, 0.01f), new Def("FacadePanel_SRV", Kind.Modular, 0.01f),
        new Def("Corner_OFF", Kind.Modular, 0.01f), new Def("Corner_CER", Kind.Modular, 0.01f),
        new Def("Corner_RES", Kind.Modular, 0.01f), new Def("Corner_SRV", Kind.Modular, 0.01f),
        new Def("StreetLamp", Kind.Swap, 0.015f, 0.12f), new Def("Bollard", Kind.Swap, 0.03f),
        new Def("CrowdBarrier", Kind.Ground, 0.02f, 0.15f), new Def("WaterBarrier", Kind.Ground, 0.02f, 0.15f),
        new Def("VendingMachine", Kind.Ground, 0.02f, 0.15f), new Def("HoloAdPylon", Kind.Ground, 0.015f, 0.15f),
        new Def("DronePad", Kind.Ground, 0.02f, 0.15f), new Def("TrashBags", Kind.Ground, 0.025f),
        new Def("TrashBin", Kind.Ground, 0.025f), new Def("CrateStack", Kind.Ground, 0.025f), new Def("PalletBoxes", Kind.Ground, 0.025f),
        new Def("LightTower", Kind.Ground, 0.012f, 0.12f), new Def("HQSecurityBooth", Kind.Ground, 0.015f, 0.15f),
        new Def("Billboard_0", Kind.Wall, 0.01f, 0.12f), new Def("Billboard_1", Kind.Wall, 0.01f, 0.12f), new Def("Billboard_4", Kind.Wall, 0.01f, 0.12f),
        new Def("WallFloodRig", Kind.Wall, 0.015f, 0.12f), new Def("BayLamp", Kind.Wall, 0.02f), new Def("BeaconAmber", Kind.Wall, 0.02f),
        new Def("WallUtilityKit", Kind.Wall, 0.015f, 0.12f),
        new Def("CablesOverhead", Kind.Overhead, 0.02f, 0.12f), new Def("DeliveryDrone", Kind.Overhead, 0.02f, 0.12f),
        new Def("Puddle_0", Kind.Decal, 0.02f), new Def("Puddle_1", Kind.Decal, 0.02f), new Def("Puddle_2", Kind.Decal, 0.02f), new Def("Puddle_3", Kind.Decal, 0.02f),
        new Def("HQPodiumDetail", Kind.Overlay, 0.008f, 0.12f), new Def("TowerBaseDetail", Kind.Overlay, 0.006f, 0.12f),
    };

    static readonly System.Text.StringBuilder Log = new System.Text.StringBuilder();
    static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

    // ================================================================== prefabs
    [MenuItem("Tools/Chuon/Tutorial/Detail/Rebuild Detail Prefabs")]
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
            if (!Path.GetFileName(p).StartsWith("T_INFD_")) continue;
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            bool ch = false;
            bool normal = p.Contains("_Normal"), mask = p.Contains("_Mask");
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ch = true; }
            if (mask && ti.sRGBTexture) { ti.sRGBTexture = false; ch = true; }
            bool alpha = p.Contains("Puddle_Albedo");
            if (alpha && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ch = true; }
            int max = p.Contains("Puddle") ? 1024 : 2048;
            if (ti.maxTextureSize != max) { ti.maxTextureSize = max; ch = true; }
            if (ti.anisoLevel < 4) { ti.anisoLevel = 4; ch = true; }
            if (ch) ti.SaveAndReimport();
        }
    }

    static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{n}.png");

    static Material Lit(string name, string tex, float smoothness, float metallic, Color emission, bool mask, bool normal, bool alphaClip = false)
    {
        Directory.CreateDirectory(MatDir);
        string path = $"{MatDir}/M_{name}.mat";
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        m.enableInstancing = true;
        m.SetFloat("_WorkflowMode", 1f);
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BaseMap", Tex(tex + "_Albedo"));
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        Texture2D n = normal ? Tex(tex + "_Normal") : null;
        m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f);
        if (n != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        Texture2D k = mask ? Tex(tex + "_Mask") : null;
        m.SetTexture("_MetallicGlossMap", k); m.SetTexture("_OcclusionMap", k);
        m.SetFloat("_OcclusionStrength", k != null ? 0.85f : 1f);
        if (k != null) { m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP"); }
        else { m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.DisableKeyword("_OCCLUSIONMAP"); }
        Texture2D e = emission.maxColorComponent > 0f ? Tex(tex + "_Emission") : null;
        if (e != null)
        {
            m.SetTexture("_EmissionMap", e); m.SetColor("_EmissionColor", emission); m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.SetTexture("_EmissionMap", null); m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        m.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        m.SetFloat("_Cutoff", 0.5f);
        if (alphaClip) { m.EnableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = (int)RenderQueue.AlphaTest; }
        else { m.DisableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "Opaque"); m.renderQueue = -1; }
        EditorUtility.SetDirty(m);
        return m;
    }

    static void BuildMaterials()
    {
        Mats.Clear();
        // 트림 시트: 발광 스트립(빨강·백색·앰버, 자판기·패널 화면)은 Emission 맵 × 4 (기존 M_INF_Red 4.5 와 비슷한 밝기)
        Mats["INFD_Trim"] = Lit("INFD_Trim", "T_INFD_Trim", 1f, 1f, new Color(1f, 0.97f, 0.94f) * 4f, true, true);
        // 창문 아틀라스: 사무실·주거 실내광 (네온 아님) — 기존 Facade 재질 1.2~1.35 대비 약간 밝게
        Mats["INFD_Windows"] = Lit("INFD_Windows", "T_INFD_Windows", 0.9f, 0f, new Color(1f, 0.96f, 0.9f) * 1.5f, false, false);
        Mats["INFD_Impostor"] = Lit("INFD_Impostor", "T_INFD_Impostor", 0.25f, 0f, new Color(1f, 0.96f, 0.9f) * 1.5f, false, false);
        Mats["INFD_Puddle"] = Lit("INFD_Puddle", "T_INFD_Puddle", 1f, 0f, Color.black, true, true, true);
        Mats["INF_Signs"] = AssetDatabase.LoadAssetAtPath<Material>(SignsMatPath);
        if (Mats["INF_Signs"] == null) Log.Append("WARN M_INF_Signs missing (run Build Infiltration Map once); ");
        AssetDatabase.SaveAssets();
    }

    static int RemapModels()
    {
        int n = 0;
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
            n++;
        }
        return n;
    }

    static string Folder(Kind k)
    {
        switch (k)
        {
            case Kind.Building: return "Buildings";
            case Kind.Modular: return "Modular";
            case Kind.Overlay: return "HQ";
            case Kind.Wall: return "Wall";
            case Kind.Decal: return "Decals";
            default: return "Props";
        }
    }

    static string PrefabPath(Def d) => $"{PrefabDir}/{Folder(d.kind)}/PF_INFD_{d.name}.prefab";

    static bool BuildPrefab(Def d, Scene preview)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/SM_INFD_{d.name}.fbx");
        if (model == null) { Log.Append($"MISSING model {d.name}; "); return false; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = "PF_INFD_" + d.name;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // LOD 분류: 이름 접미사 _LODn
        var byLod = new SortedDictionary<int, List<Renderer>>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            int lod = 0;
            int i = r.name.LastIndexOf("_LOD");
            if (i >= 0) int.TryParse(r.name.Substring(i + 4), out lod);
            if (!byLod.TryGetValue(lod, out List<Renderer> list)) byLod[lod] = list = new List<Renderer>();
            list.Add(r);
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            r.shadowCastingMode = d.kind == Kind.Decal ? ShadowCastingMode.Off : ShadowCastingMode.On;
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
        lg.fadeMode = d.kind == Kind.Building ? LODFadeMode.CrossFade : LODFadeMode.None;
        lg.animateCrossFading = d.kind == Kind.Building;
        lg.RecalculateBounds();

        if (d.kind == Kind.Ground && byLod.TryGetValue(0, out List<Renderer> l0))
        {
            Bounds b = LocalBounds(go.transform, l0);
            var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size;
            var ob = go.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box; ob.center = b.center; ob.size = b.size + new Vector3(0.1f, 0f, 0.1f);
            ob.carving = true; ob.carveOnlyStationary = true;
        }
        StaticEditorFlags f = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        if (d.kind == Kind.Building || d.kind == Kind.Overlay) f |= StaticEditorFlags.OccluderStatic;
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

    // ================================================================== apply
    enum S { Ground, Wall, Overhead, Decal }

    struct Slot
    {
        public string p; public Vector3 pos; public float yaw; public S s; public Vector3 aim;
        public Slot(string p, float x, float y, float z, float yaw, S s) { this.p = p; pos = new Vector3(x, y, z); this.yaw = yaw; this.s = s; aim = Vector3.zero; }
        public Slot Aim(float x, float y, float z) { aim = new Vector3(x, y, z); return this; }
    }

    // 월드 좌표 (북 = +Z, Docs/LevelDesign_Infiltration.md). 벽 부착 = 벽면 위치·앞면 방향 yaw. 바닥 소품은 지면 레이캐스트로 높이 보정.
    // 바닥 소품은 경로 회랑·스폰/체크포인트·차단 콜라이더·엄폐물 주변·전투 중심부에서 자동 제외된다.
    static readonly Slot[] Slots =
    {
        // ① 시작 골목 (보도 x = ±4..7, 건물면 x = ±7)
        new Slot("VendingMachine", -6.6f, 0.12f, 18.4f, 90, S.Ground), new Slot("VendingMachine", -6.6f, 0.12f, 19.45f, 90, S.Ground),
        new Slot("TrashBin", -6.65f, 0.12f, 20.9f, 90, S.Ground), new Slot("TrashBags", -6.4f, 0.12f, 33.6f, 90, S.Ground),
        new Slot("CrateStack", -6.45f, 0.12f, 47.0f, 90, S.Ground), new Slot("CrateStack", 6.45f, 0.12f, 25.4f, -90, S.Ground),
        new Slot("TrashBags", 6.35f, 0.12f, 27.7f, -90, S.Ground), new Slot("HoloAdPylon", 6.55f, 0.12f, 41.0f, -90, S.Ground),
        new Slot("TrashBin", 6.65f, 0.12f, 12.5f, -90, S.Ground), new Slot("WaterBarrier", 5.9f, 0.12f, 49.5f, 0, S.Ground),
        new Slot("Puddle_1", -1.8f, 0.01f, 12f, 10, S.Decal), new Slot("Puddle_3", 1.5f, 0.01f, 27f, -5, S.Decal),
        new Slot("Puddle_2", 2.4f, 0.01f, 44f, 0, S.Decal), new Slot("Puddle_0", -5.3f, 0.12f, 34.5f, 90, S.Decal),
        new Slot("Puddle_2", 5.4f, 0.12f, 8.5f, 0, S.Decal), new Slot("Puddle_0", -1.0f, 0.01f, 1.0f, 30, S.Decal),
        new Slot("CablesOverhead", 0f, 10.0f, 17f, 0, S.Overhead), new Slot("CablesOverhead", 0f, 9.6f, 31.6f, 0, S.Overhead),
        new Slot("CablesOverhead", 0f, 10.3f, 47.5f, 0, S.Overhead),
        new Slot("DeliveryDrone", -3.0f, 18.5f, 22f, 30, S.Overhead), new Slot("DeliveryDrone", 4.0f, 20.0f, 58f, -60, S.Overhead),
        // ② 검문소 광장 (보안벽 안쪽 면 x = ±23.55, z = 52.45 / 99.55)
        new Slot("Billboard_4", -23.5f, 2.4f, 64f, 90, S.Wall), new Slot("Billboard_1", 23.5f, 2.4f, 88f, -90, S.Wall),
        new Slot("HoloAdPylon", -22.95f, 0.02f, 84f, 90, S.Ground),
        new Slot("CrowdBarrier", -22.3f, 0.02f, 79.4f, 90, S.Ground), new Slot("CrowdBarrier", -22.3f, 0.02f, 81.5f, 90, S.Ground),
        new Slot("VendingMachine", 12.6f, 0.02f, 99.1f, 180, S.Ground), new Slot("VendingMachine", 13.65f, 0.02f, 99.1f, 180, S.Ground),
        new Slot("TrashBin", 15.3f, 0.02f, 99.15f, 180, S.Ground), new Slot("WaterBarrier", -21.3f, 0.02f, 98.9f, 0, S.Ground),
        new Slot("PalletBoxes", 22.8f, 0.02f, 86.2f, -90, S.Ground), new Slot("CrateStack", 23.0f, 0.02f, 82.5f, -90, S.Ground),
        new Slot("TrashBags", -22.9f, 0.02f, 59.5f, 90, S.Ground),
        new Slot("Puddle_3", -11f, 0.02f, 62f, 15, S.Decal), new Slot("Puddle_1", 12f, 0.02f, 70f, -20, S.Decal),
        new Slot("Puddle_0", -2.5f, 0.01f, 84f, 0, S.Decal), new Slot("Puddle_2", 16f, 0.02f, 81f, 40, S.Decal),
        // 게이트 안 마당
        new Slot("DronePad", -6.5f, 0.02f, 102.6f, 0, S.Ground), new Slot("TrashBags", -8.7f, 0.02f, 106.3f, 0, S.Ground),
        new Slot("Puddle_1", 4f, 0.02f, 103.5f, 0, S.Decal),
        // ③ 하역장 야드 (벽 안쪽 면 x = 30.45 / 67.55, 창고 앞면 z = 157)
        new Slot("WallFloodRig", 30.45f, 6.6f, 124f, 90, S.Wall).Aim(46f, 0f, 128f),
        new Slot("WallFloodRig", 67.55f, 6.6f, 138f, -90, S.Wall).Aim(52f, 0f, 140f),
        new Slot("LightTower", 64.2f, 0f, 113.5f, -45, S.Ground).Aim(50f, 0f, 126f),
        new Slot("LightTower", 32.3f, 0f, 134.5f, 90, S.Ground).Aim(44f, 0f, 138f),
        new Slot("BayLamp", 35f, 7.0f, 156.98f, 180, S.Wall).Aim(35f, 1.2f, 154.2f),
        new Slot("BayLamp", 47f, 7.0f, 156.98f, 180, S.Wall).Aim(47f, 1.2f, 154.2f),
        new Slot("BayLamp", 59f, 7.0f, 156.98f, 180, S.Wall).Aim(59f, 1.2f, 154.2f),
        new Slot("BeaconAmber", 33.5f, 6.6f, 104.45f, 0, S.Wall), new Slot("BeaconAmber", 64.5f, 6.6f, 104.45f, 0, S.Wall),
        new Slot("WallUtilityKit", 30.45f, 0f, 131f, 90, S.Wall), new Slot("WallUtilityKit", 67.55f, 0f, 125f, -90, S.Wall),
        new Slot("Billboard_0", 67.55f, 2.4f, 147.5f, -90, S.Wall),
        new Slot("PalletBoxes", 66.75f, 0f, 120.5f, -90, S.Ground), new Slot("CrateStack", 31.3f, 0f, 117f, 90, S.Ground),
        new Slot("TrashBags", 66.8f, 0f, 126.3f, -90, S.Ground), new Slot("WaterBarrier", 31.2f, 0f, 127.4f, 90, S.Ground),
        new Slot("Puddle_3", 47f, 0f, 120f, 20, S.Decal), new Slot("Puddle_1", 56f, 0f, 126f, -15, S.Decal),
        new Slot("Puddle_0", 38f, 0f, 138f, 0, S.Decal), new Slot("Puddle_2", 50.5f, 0f, 150.5f, 0, S.Decal),
        new Slot("Puddle_1", 62.5f, 0f, 151f, 80, S.Decal), new Slot("Puddle_2", 34f, 0f, 112f, 0, S.Decal),
        // ⑤ 측면 출입구 마당 (바닥 1.2 m, 포디엄 기단 z ≈ 198.9)
        new Slot("HQSecurityBooth", 33.2f, 1.2f, 197.7f, 180, S.Ground), new Slot("HQSecurityBooth", 60.8f, 1.2f, 197.7f, 180, S.Ground),
        new Slot("TrashBin", 31.3f, 1.2f, 186f, 90, S.Ground),
        new Slot("Puddle_0", 40f, 1.2f, 186f, 0, S.Decal), new Slot("Puddle_2", 55f, 1.2f, 190f, 30, S.Decal),
    };

    static readonly string[] BlockKinds = { "BlockA", "BlockB", "BlockC", "BlockD" };
    static readonly string[] RouteNames =
    {
        "PlayerStart", "Zone_Move", "Zone_ScanPassed", "CP_Plaza", "Goal_Gate", "@14,0,105.5", "Zone_DockYard", "CP_Dock",
        "Goal_Ramp", "CP_Platform", "Goal_Passage", "Zone_SideYard", "CP_Side", "Zone_Exit"
    };

    [MenuItem("Tools/Chuon/Tutorial/Apply Detail Pass")]
    public static void ApplyMenu() => Debug.Log(Apply());

    [MenuItem("Tools/Chuon/Tutorial/Detail/Remove Detail Pass")]
    public static void RemoveMenu() => Debug.Log(Remove());

    static bool CheckScene(out Scene scene, out Transform infRoot, out string err)
    {
        scene = SceneManager.GetActiveScene(); infRoot = null; err = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { err = "ABORT: play mode"; return false; }
        if (scene.path != ScenePath) { err = "ABORT: open " + ScenePath + " first (active: " + scene.path + ")"; return false; }
        foreach (GameObject g in scene.GetRootGameObjects())
            if (g.name == "INF_ROOT") infRoot = g.transform;
        if (infRoot == null) { err = "ABORT: INF_ROOT missing (run Tools/Chuon/Tutorial/Build Infiltration Map)"; return false; }
        return true;
    }

    /// <summary>MCP/메뉴 진입점. 반환 = 로그. 씬 저장은 하지 않는다 (dirty 만 표시).</summary>
    public static string Apply()
    {
        Log.Clear();
        if (!CheckScene(out Scene scene, out Transform inf, out string err)) return err;
        foreach (Def d in Defs)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(d)) == null) { Log.Append(RebuildPrefabs() + " | "); break; }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Tutorial Detail Pass");
        int group = Undo.GetCurrentGroup();
        RemoveInternal(inf);

        var detail = new GameObject(DetailName).transform;
        Undo.RegisterCreatedObjectUndo(detail.gameObject, "Detail root");
        Undo.SetTransformParent(detail, inf, "Detail root");
        detail.localPosition = Vector3.zero; detail.localRotation = Quaternion.identity; detail.localScale = Vector3.one;
        Transform tB = Child(detail, "Buildings"), tS = Child(detail, "Swaps"), tO = Child(detail, "Overlays"),
                  tD = Child(detail, "Dressing"), tL = Child(detail, "Lights"), tG = Child(detail, "GroundTiles");

        Transform gameplay = inf.Find("Gameplay"), sim = inf.Find("Simulation");
        bool Protected(Transform t) =>
            (gameplay != null && t.IsChildOf(gameplay)) || (sim != null && t.IsChildOf(sim)) || t.GetComponent<MonoBehaviour>() != null;

        // 1) 건물 교체 (원본 비활성, 같은 트랜스폼·스케일)
        int swapped = 0;
        foreach (Transform t in inf.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(BlockKinds, t.name) < 0 || !t.gameObject.activeSelf || Protected(t) || t.IsChildOf(detail)) continue;
            int h = Mathf.RoundToInt(t.position.x) * 73856093 ^ Mathf.RoundToInt(t.position.z) * 19349663 ^ t.name[5] * 83492791;
            string v = t.name + "_" + (1 + (Mathf.Abs(h) % 2));
            GameObject inst = Place(v, t.position, t.rotation, tB);
            if (inst == null) continue;
            inst.transform.localScale = t.lossyScale;
            inst.name = "D_" + t.name + "_" + v.Substring(v.Length - 1);
            Undo.RecordObject(t.gameObject, "Detail swap");
            t.gameObject.SetActive(false);
            swapped++;
        }
        Log.Append($"buildings={swapped}; ");

        // 2) 외형 교체: 가로등·볼라드 (원본 콜라이더·라이트는 유지)
        int vis = 0;
        foreach (Transform t in inf.GetComponentsInChildren<Transform>(true))
        {
            if (Protected(t) || t.IsChildOf(detail)) continue;
            string pf = t.name.StartsWith("StreetLight_") ? "StreetLamp" : t.name == "Bollard" ? "Bollard" : null;
            if (pf == null) continue;
            if (Place(pf, t.position, t.rotation, tS) == null) continue;
            foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>())
            { Undo.RecordObject(r, "Detail swap"); r.enabled = false; }
            vis++;
        }
        Log.Append($"visualSwaps={vis}; ");

        // 3) 본사 포디엄·타워 오버레이
        foreach (Transform t in inf.GetComponentsInChildren<Transform>(true))
        {
            if (t.IsChildOf(detail) || Protected(t)) continue;
            if (t.name == "HQPodium") Place("HQPodiumDetail", t.position, t.rotation, tO);
            else if (t.name == "HQTower") Place("TowerBaseDetail", t.position, t.rotation, tO);
        }

        // 4) 바닥 조명 타일 (URP Forward 오브젝트당 추가광 4개 제한 → 큰 슬래브를 타일로 나눠 새 조명이 실제로 닿게)
        int tiles = Retile(inf, "G_DockYard", 4, 4, tG) + Retile(inf, "G_RoadStreet", 1, 5, tG);
        Log.Append($"groundTiles={tiles}; ");

        // 5) 거리 소품 (결정론적 슬롯 + 경로/전투/엄폐 제외)
        Physics.SyncTransforms();
        var ex = new Exclusion(inf, gameplay, detail);
        int placed = 0, skipped = 0, lights = 0;
        var reasons = new Dictionary<string, int>();
        foreach (Slot s in Slots)
        {
            GameObject g = TryPlace(s, tD, ex, out string why);
            if (g == null) { skipped++; reasons[why] = reasons.TryGetValue(why, out int c) ? c + 1 : 1; continue; }
            placed++;
            lights += AddLight(s, g, tL);
            Physics.SyncTransforms();
        }
        Log.Append($"dressing={placed} skipped={skipped} (");
        foreach (KeyValuePair<string, int> kv in reasons) Log.Append($"{kv.Key}:{kv.Value} ");
        Log.Append($") lights={lights}; routeSource={ex.routeSource}; ");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        Log.Append("done (not saved; Ctrl+Z reverts)");
        return Log.ToString();
    }

    public static string Remove()
    {
        Log.Clear();
        if (!CheckScene(out Scene scene, out Transform inf, out string err)) return err;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Remove Tutorial Detail Pass");
        int group = Undo.GetCurrentGroup();
        RemoveInternal(inf);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
        return Log.ToString();
    }

    static void RemoveInternal(Transform inf)
    {
        Transform d = inf.Find(DetailName);
        if (d != null) { Undo.DestroyObjectImmediate(d.gameObject); Log.Append("removed old Detail; "); }
        Transform gameplay = inf.Find("Gameplay");
        int restored = 0;
        foreach (Transform t in inf.GetComponentsInChildren<Transform>(true))
        {
            if (gameplay != null && t.IsChildOf(gameplay)) continue;
            if (System.Array.IndexOf(BlockKinds, t.name) >= 0 && !t.gameObject.activeSelf)
            { Undo.RecordObject(t.gameObject, "Detail restore"); t.gameObject.SetActive(true); restored++; }
            if (t.name.StartsWith("StreetLight_") || t.name == "Bollard" || t.name == "G_DockYard" || t.name == "G_RoadStreet")
                foreach (MeshRenderer r in t.GetComponents<MeshRenderer>())
                    if (!r.enabled) { Undo.RecordObject(r, "Detail restore"); r.enabled = true; restored++; }
        }
        if (restored > 0) Log.Append($"restored={restored}; ");
    }

    static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Detail");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Place(string name, Vector3 pos, Quaternion rot, Transform parent, bool registerUndo = true)
    {
        Def d = System.Array.Find(Defs, x => x.name == name);
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(d));
        if (pf == null) { Log.Append($"MISSING prefab {name}; "); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        if (registerUndo) Undo.RegisterCreatedObjectUndo(go, "Detail place");
        return go;
    }

    // ------------------------------------------------------------------ exclusion
    class Exclusion
    {
        public readonly List<Vector3> route = new List<Vector3>();
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<Bounds> blockers = new List<Bounds>();
        public readonly List<Bounds> cover = new List<Bounds>();
        public readonly Transform detail;
        public string routeSource = "none";
        // 전투 공간 중심부 (검문소 순찰, 하역장 중앙, 측면 마당 집행관)
        public readonly Vector4[] arenas = { new Vector4(0, 78, 11, 0), new Vector4(48, 132, 7, 0), new Vector4(47, 191, 9, 0) };

        public Exclusion(Transform inf, Transform gameplay, Transform detail)
        {
            this.detail = detail;
            if (gameplay != null)
                foreach (Transform t in gameplay.GetComponentsInChildren<Transform>(true))
                {
                    if (t == gameplay) continue;
                    var col = t.GetComponent<Collider>();
                    if (col == null) points.Add(t.position);
                    else if (!col.isTrigger) blockers.Add(col.bounds);
                }
            foreach (Collider c in inf.GetComponentsInChildren<Collider>(true))
                if (c.name.StartsWith("Container") || c.name.StartsWith("Truck") || c.name.StartsWith("Jersey")) cover.Add(c.bounds);
            BuildRoute(gameplay);
        }

        Vector3? Named(Transform gameplay, string n)
        {
            if (n.StartsWith("@"))
            {
                string[] p = n.Substring(1).Split(',');
                return new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
            }
            if (gameplay == null) return null;
            foreach (Transform t in gameplay.GetComponentsInChildren<Transform>(true))
                if (t.name == n) return t.position;
            return null;
        }

        void BuildRoute(Transform gameplay)
        {
            var wps = new List<Vector3>();
            foreach (string n in RouteNames) { Vector3? p = Named(gameplay, n); if (p.HasValue) wps.Add(p.Value); }
            int nav = 0, straight = 0;
            var path = new NavMeshPath();
            for (int i = 0; i + 1 < wps.Count; i++)
            {
                bool ok = NavMesh.SamplePosition(wps[i], out NavMeshHit a, 4f, NavMesh.AllAreas)
                          && NavMesh.SamplePosition(wps[i + 1], out NavMeshHit b, 4f, NavMesh.AllAreas)
                          && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                          && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 1;
                if (ok) { route.AddRange(path.corners); nav++; }
                else { route.Add(wps[i]); route.Add(wps[i + 1]); straight++; }
            }
            routeSource = $"navmesh {nav}/straight {straight}";
        }

        static float RectDist(Bounds b, Vector3 p)
        {
            float dx = Mathf.Max(b.min.x - p.x, 0f, p.x - b.max.x);
            float dz = Mathf.Max(b.min.z - p.z, 0f, p.z - b.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public string Check(Bounds b)
        {
            foreach (Vector3 p in points) if (RectDist(b, p) < 2.5f) return "gameplayPoint";
            foreach (Bounds k in blockers) { Bounds e = k; e.Expand(1.6f); if (e.Intersects(b)) return "blocker"; }
            foreach (Bounds k in cover) { Bounds e = k; e.Expand(new Vector3(3.6f, 0f, 3.6f)); if (e.Intersects(b)) return "cover"; }
            foreach (Vector4 a in arenas) if (RectDist(b, new Vector3(a.x, 0, a.y)) < a.z) return "arena";
            for (int i = 0; i + 1 < route.Count; i++)
            {
                Vector3 p0 = route[i], p1 = route[i + 1];
                float len = Vector3.Distance(p0, p1);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
                for (int k = 0; k <= n; k++)
                    if (RectDist(b, Vector3.Lerp(p0, p1, k / (float)n)) < 1.6f) return "route";
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

    static readonly float[] Jitter = { 0f, 0.8f, -0.8f, 1.6f, -1.6f };

    static GameObject TryPlace(Slot s, Transform parent, Exclusion ex, out string why)
    {
        why = null;
        Quaternion rot = Quaternion.Euler(0f, s.yaw, 0f);
        Vector3 along = rot * Vector3.right;
        int tries = s.s == S.Ground ? Jitter.Length : 1;
        for (int i = 0; i < tries; i++)
        {
            Vector3 pos = s.pos + along * Jitter[i];
            if (s.s == S.Ground || s.s == S.Decal)
            {
                if (!Ground(pos, out float y)) { why = "noGround"; continue; }
                if (Mathf.Abs(y - s.pos.y) > 0.6f) { why = "groundHeight"; continue; }
                pos.y = y + (s.s == S.Decal ? 0.012f : 0f);
            }
            GameObject g = Place(s.p, pos, rot, parent, false);
            if (g == null) { why = "missingPrefab"; return null; }
            if (s.s == S.Ground)
            {
                Bounds b = WorldBounds(g);
                why = ex.Check(b) ?? Overlaps(b, g);
                if (why != null) { Object.DestroyImmediate(g); continue; }
            }
            Undo.RegisterCreatedObjectUndo(g, "Detail place");
            return g;
        }
        return null;
    }

    static bool Ground(Vector3 p, out float y)
    {
        y = 0f; bool hit = false;
        foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.name.StartsWith("G_") && (!hit || h.point.y > y)) { y = h.point.y; hit = true; }
        return hit;
    }

    static string Overlaps(Bounds b, GameObject self)
    {
        Vector3 half = b.extents - new Vector3(0.03f, 0.03f, 0.03f);
        half.y = Mathf.Max(0.05f, half.y - 0.05f);
        foreach (Collider c in Physics.OverlapBox(b.center + Vector3.up * 0.05f, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(self.transform) || c.name.StartsWith("G_")) continue;
            return "overlap";
        }
        return null;
    }

    // ------------------------------------------------------------------ lights (dock fix)
    static int AddLight(Slot s, GameObject g, Transform parent)
    {
        bool aimed = s.aim != Vector3.zero;
        if (!aimed && s.p != "BeaconAmber") return 0;
        Transform a = null;
        foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) if (t.name == "LightAnchor") a = t;
        Vector3 p = a != null ? a.position : g.transform.position;
        var go = new GameObject("L_Detail_" + s.p);
        Undo.RegisterCreatedObjectUndo(go, "Detail light");
        go.transform.SetParent(parent, false);
        go.transform.position = p;
        var l = go.AddComponent<Light>();
        l.lightmapBakeType = LightmapBakeType.Realtime;
        l.shadows = LightShadows.None;
        if (s.p == "BeaconAmber")
        {
            l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.15f); l.intensity = 1.6f; l.range = 4f;
            return 1;
        }
        go.transform.rotation = Quaternion.LookRotation((s.aim - p).normalized, Vector3.up);
        l.type = LightType.Spot;
        switch (s.p)
        {
            case "BayLamp": l.color = new Color(1f, 0.94f, 0.85f); l.intensity = 7f; l.range = 10f; l.spotAngle = 110f; break;
            case "LightTower": l.color = new Color(0.92f, 0.96f, 1f); l.intensity = 24f; l.range = 36f; l.spotAngle = 72f; break;
            default: l.color = new Color(0.92f, 0.96f, 1f); l.intensity = 22f; l.range = 34f; l.spotAngle = 78f; break;
        }
        l.innerSpotAngle = l.spotAngle * 0.55f;
        return 1;
    }

    // ------------------------------------------------------------------ ground tiles
    static int Retile(Transform inf, string name, int nx, int nz, Transform parent)
    {
        Transform src = null;
        foreach (Transform t in inf.GetComponentsInChildren<Transform>(true)) if (t.name == name) { src = t; break; }
        if (src == null) return 0;
        var mf = src.GetComponent<MeshFilter>(); var mr = src.GetComponent<MeshRenderer>();
        if (mf == null || mr == null || mf.sharedMesh == null || !mr.enabled) return 0;
        Mesh m = mf.sharedMesh;
        Vector3[] v = m.vertices; Vector3[] n = m.normals; Vector2[] uv = m.uv;
        // 윗면 정점 3개로 uv = A·(x,z) + b 를 맞춘다 (MetreBox / Road 모두 선형)
        var top = new List<int>();
        float maxY = float.MinValue;
        for (int i = 0; i < v.Length; i++) maxY = Mathf.Max(maxY, src.TransformPoint(v[i]).y);
        for (int i = 0; i < v.Length && top.Count < 4; i++)
            if (Mathf.Abs(src.TransformPoint(v[i]).y - maxY) < 1e-3f && (n.Length == 0 || src.TransformDirection(n[i]).y > 0.9f)) top.Add(i);
        if (top.Count < 3 || uv.Length != v.Length) { Log.Append($"retile {name}: no top face; "); return 0; }
        Vector3 w0 = src.TransformPoint(v[top[0]]), w1 = src.TransformPoint(v[top[1]]), w2 = src.TransformPoint(v[top[2]]);
        float det = (w1.x - w0.x) * (w2.z - w0.z) - (w2.x - w0.x) * (w1.z - w0.z);
        if (Mathf.Abs(det) < 1e-5f) return 0;
        Vector2 du = uv[top[1]] - uv[top[0]], dv = uv[top[2]] - uv[top[0]];
        Vector2 ax = (du * (w2.z - w0.z) - dv * (w1.z - w0.z)) / det;
        Vector2 az = (dv * (w1.x - w0.x) - du * (w2.x - w0.x)) / det;
        Vector2 UV(float x, float z) => uv[top[0]] + ax * (x - w0.x) + az * (z - w0.z);
        Bounds b = mr.bounds;
        Directory.CreateDirectory(GenDir);
        int count = 0;
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < nz; j++)
        {
            float x0 = Mathf.Lerp(b.min.x, b.max.x, i / (float)nx), x1 = Mathf.Lerp(b.min.x, b.max.x, (i + 1f) / nx);
            float z0 = Mathf.Lerp(b.min.z, b.max.z, j / (float)nz), z1 = Mathf.Lerp(b.min.z, b.max.z, (j + 1f) / nz);
            float y = maxY + 0.004f;
            var mesh = new Mesh { name = $"SM_INFD_Tile_{name}_{i}_{j}" };
            mesh.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1) };
            mesh.uv = new[] { UV(x0, z0), UV(x1, z0), UV(x0, z1), UV(x1, z1) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            string path = $"{GenDir}/{mesh.name}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Detail tile");
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            count++;
        }
        // 원본 슬래브의 윗면은 타일 아래로 가려진다. 원본은 그대로 두고(콜라이더·옆면·NavMesh) 렌더만 유지.
        return count;
    }
}
