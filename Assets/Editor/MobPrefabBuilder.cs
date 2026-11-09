using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// TSUKASA 잡몹 프리팹 빌더 — 메뉴 "Tools/Chuon/Mobs/Build All Prefabs" (로드 시 자동 실행 없음).
// 열린 씬은 건드리지 않는다: 모든 작업은 프리뷰 씬에서 하고 SaveAsPrefabAsset 으로 저장.
//  1) Mobs 텍스처/FBX 임포트 설정 + 머티리얼 (Type-D ASP StylizePBR 복제 + 트림 아틀라스, Glow/Visor 복제)
//  2) 루프 클립 복사 (.anim, loopTime) + AnimatorController (Locomotion 2D 블렌드 + 원샷 스테이트, 원샷은 자동 복귀)
//  3) 휴머노이드(Humanoid_F) 몸체에 장갑을 '구운 메시 레이캐스트'로 맞춰 본에 부착, 무기 그립은 손가락 본으로 계산,
//     방패는 가드 포즈(DefenseL_Loop)를 샘플링해 정면을 향하도록 전완에 고정
//  4) 공격 클립의 손 속도 피크 = 칼이 닿는 시각 → EnemyStrike.clipImpactTime 베이크 (찌르기는 '전방성' 최대 클립 자동 선택)
//  5) 프리팹: Assets/Prefabs/Mobs/Mob_SecurityAndroid, Mob_ShieldEnforcer, Mob_SentryTurret_Floor, Mob_SentryTurret_Wall, Mob_PatrolDrone
public static class MobPrefabBuilder
{
    const string ArtRoot = "Assets/Art/ChuonPolish/Mobs";
    const string MeshDir = ArtRoot + "/Meshes";
    const string TexDir = ArtRoot + "/Textures";
    const string MatDir = ArtRoot + "/Materials";
    const string AnimDir = ArtRoot + "/Anim";
    const string AudioDir = ArtRoot + "/Audio";
    const string PrefabDir = "Assets/Prefabs/Mobs";
    const string GhostRoot = "Assets/GhostSamurai_Animset/Animation/katana";
    const string BodyModelPath = "Assets/CombatGirlsCharacterPack-20260330T102548Z-3-001/CombatGirlsCharacterPack/Humanoid_Bot/Models/Humanoid_F.fbx";
    const string HpBarPrefabPath = "Assets/Prefab/EnemyHPBarPrefap.prefab";
    const string LobbyDronePrefabPath = "Assets/Prefab/New Folder/Cube.prefab";
    const string HitVfxPath = "Assets/잡동사니/Sword_hit.prefab";
    const string TypeDMatDir = "Assets/Art/ChuonPolish/TypeD/Materials";
    const string SfxRoot = "Assets/Art/ChuonPolish/Audio/Resources";

    static readonly List<string> s_log = new List<string>();
    static Quaternion s_kitFlip = Quaternion.identity;

    [MenuItem("Tools/Chuon/Mobs/Build All Prefabs")]
    public static void BuildAllMenu()
    {
        string report = BuildAll();
        Debug.Log(report);
    }

    public static string BuildAll()
    {
        s_log.Clear();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "[MobPrefabBuilder] refused: editor is in (or entering) Play mode.";

        EnsureFolder(PrefabDir);
        EnsureFolder(MatDir);
        EnsureFolder(AnimDir);
        ConfigureTextures();
        Materials mats = BuildMaterials();
        ConfigureModels(mats);

        bool activeWasDirty = SceneManager.GetActiveScene().isDirty;
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            BuildAndroid(scene, mats, false);
            BuildAndroid(scene, mats, true);
            BuildTurret(scene, mats, false);
            BuildTurret(scene, mats, true);
            BuildDrone(scene);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log($"active scene dirty before/after: {activeWasDirty}/{SceneManager.GetActiveScene().isDirty}");
        return "[MobPrefabBuilder]\n" + string.Join("\n", s_log);
    }

    static void Log(string s) => s_log.Add(s);

    // ================================================================ assets
    struct Materials
    {
        public Material trim, glow, visor, suit, suitPlate, suitFace, tracer, laser;
    }

    static void ConfigureTextures()
    {
        SetTexture(TexDir + "/T_Mob_Trim_Albedo.png", true, false);
        SetTexture(TexDir + "/T_Mob_Trim_Normal.png", false, true);
        SetTexture(TexDir + "/T_Mob_Trim_OSM.png", false, false);
    }

    static void SetTexture(string path, bool srgb, bool normal)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null)
        {
            Log("missing texture " + path);
            return;
        }
        bool dirty = false;
        TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; dirty = true; }
        if (ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; dirty = true; }
        if (ti.anisoLevel != 4) { ti.anisoLevel = 4; dirty = true; }
        if (dirty)
            ti.SaveAndReimport();
    }

    static Materials BuildMaterials()
    {
        var m = new Materials();
        var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_Mob_Trim_Albedo.png");
        var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_Mob_Trim_Normal.png");
        var osm = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_Mob_Trim_OSM.png");

        m.trim = CloneMaterial(TypeDMatDir + "/M_TypeD_ArmorMetal.mat", MatDir + "/M_Mob_Trim.mat", mat =>
        {
            SetTex(mat, "_BaseMap", alb);
            SetTex(mat, "_MainTex", alb);
            SetTex(mat, "_BumpMap", nrm);
            SetTex(mat, "_OSMTexture", osm);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        });
        m.glow = CloneMaterial(TypeDMatDir + "/M_TypeD_Glow.mat", MatDir + "/M_Mob_Glow.mat", mat =>
        {
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(1.35f, 0.16f, 0.2f, 1f));
        });
        m.visor = CloneMaterial(TypeDMatDir + "/M_TypeD_Visor.mat", MatDir + "/M_Mob_Visor.mat", null);

        var bodyNormal = FindTexture("Humanoid_Body_Normal");
        var bodyMs = FindTexture("Humanoid_Body_MetallicSmoothness");
        var bodyAlb = FindTexture("Humanoid_Body_Albedo");
        m.suit = LitMaterial(MatDir + "/M_Mob_Suit.mat", bodyAlb, bodyNormal, bodyMs, new Color(0.20f, 0.21f, 0.235f), 0.75f);
        m.suitPlate = LitMaterial(MatDir + "/M_Mob_SuitPlate.mat", bodyAlb, bodyNormal, bodyMs, new Color(0.72f, 0.73f, 0.76f), 0.6f);
        m.suitFace = LitMaterial(MatDir + "/M_Mob_SuitFace.mat", null, null, null, new Color(0.03f, 0.033f, 0.04f), 0.9f);

        m.tracer = UnlitAdditive(MatDir + "/M_Mob_Tracer.mat", new Color(4f, 0.55f, 0.35f, 1f));
        m.laser = UnlitAdditive(MatDir + "/M_Mob_Laser.mat", new Color(3f, 0.12f, 0.08f, 1f));
        return m;
    }

    static Material CloneMaterial(string srcPath, string dstPath, Action<Material> configure)
    {
        var src = AssetDatabase.LoadAssetAtPath<Material>(srcPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(dstPath);
        if (mat == null)
        {
            mat = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, dstPath);
        }
        else if (src != null)
        {
            mat.shader = src.shader;
            mat.CopyPropertiesFromMaterial(src);
        }
        configure?.Invoke(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetTex(Material m, string prop, Texture t)
    {
        if (m.HasProperty(prop)) m.SetTexture(prop, t);
    }

    static Material LitMaterial(string path, Texture2D alb, Texture2D nrm, Texture2D ms, Color tint, float smooth)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (mat == null)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = lit;
        mat.SetTexture("_BaseMap", alb);
        mat.SetColor("_BaseColor", tint);
        mat.SetTexture("_BumpMap", nrm);
        if (nrm != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", ms);
        if (ms != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP"); else mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Metallic", ms != null ? 1f : 0.2f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material UnlitAdditive(string path, Color hdr)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (mat == null)
        {
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = sh;
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f); // additive
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ADD");
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hdr);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", hdr);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D FindTexture(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == name)
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }

    static void ConfigureModels(Materials mats)
    {
        foreach (string file in new[] { "SM_Mob_SentryTurret.fbx", "SM_Mob_EnforcerKit.fbx", "SM_Mob_AndroidKit.fbx" })
        {
            string path = MeshDir + "/" + file;
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null)
            {
                Log("missing model " + path + " (run Tools/ArtGen/mob_*.py)");
                continue;
            }
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (string n in new[] { "Metal", "Ceramic", "Accent", "Rubber" })
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Mob_" + n), mats.trim);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Mob_Glow"), mats.glow);
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Mob_Visor"), mats.visor);
            mi.SaveAndReimport();
        }
    }

    // ================================================================ animation
    static AnimationClip FindClip(string fbxBaseName)
    {
        foreach (string guid in AssetDatabase.FindAssets(fbxBaseName, new[] { GhostRoot }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileNameWithoutExtension(p), fbxBaseName, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                if (o is AnimationClip c && !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                    return c;
            }
        }
        Log("clip not found: " + fbxBaseName);
        return null;
    }

    static AnimationClip LoopCopy(string fbxBaseName)
    {
        AnimationClip src = FindClip(fbxBaseName);
        if (src == null)
            return null;
        AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(src);
        if (s.loopTime)
            return src;
        string path = AnimDir + "/" + fbxBaseName + "_Loop.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
            return existing;
        var copy = Object.Instantiate(src);
        copy.name = fbxBaseName + "_Loop";
        s.loopTime = true;
        s.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(copy, s);
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    static AnimatorController BuildController(string path, Dictionary<string, AnimationClip> oneShots,
        AnimationClip idle, AnimationClip fwd, AnimationClip back, AnimationClip left, AnimationClip right, AnimationClip run, float walkNorm)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(path);
        ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        ac.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        AnimatorStateMachine sm = ac.layers[0].stateMachine;

        AnimatorState loco = ac.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveZ";
        tree.useAutomaticThresholds = false;
        if (idle != null) tree.AddChild(idle, Vector2.zero);
        if (fwd != null) tree.AddChild(fwd, new Vector2(0f, walkNorm));
        if (back != null) tree.AddChild(back, new Vector2(0f, -walkNorm));
        if (left != null) tree.AddChild(left, new Vector2(-walkNorm, 0f));
        if (right != null) tree.AddChild(right, new Vector2(walkNorm, 0f));
        if (run != null) tree.AddChild(run, new Vector2(0f, 1f));
        sm.defaultState = loco;

        foreach (KeyValuePair<string, AnimationClip> kv in oneShots)
        {
            if (kv.Value == null)
                continue;
            AnimatorState st = sm.AddState(kv.Key);
            st.motion = kv.Value;
            st.writeDefaultValues = true;
            if (kv.Key == "Die")
                continue;
            AnimatorStateTransition t = st.AddTransition(loco);
            t.hasExitTime = true;
            t.exitTime = 0.94f;
            t.duration = 0.12f;
            t.hasFixedDuration = true;
        }
        EditorUtility.SetDirty(ac);
        return ac;
    }

    struct ImpactInfo
    {
        public float time;
        public float forwardness;
    }

    // 손 속도 피크 = 칼이 닿는 순간 (클립 시간). 전방성 = 피크 순간 손 속도의 전방 성분 비율.
    static ImpactInfo MeasureImpact(GameObject animated, Animator animator, AnimationClip clip, HumanBodyBones hand)
    {
        var info = new ImpactInfo { time = clip != null ? clip.length * 0.45f : 0f };
        Transform h = animator.GetBoneTransform(hand);
        if (clip == null || h == null)
            return info;
        Transform root = animated.transform;
        const float step = 1f / 60f;
        int n = Mathf.Max(3, Mathf.CeilToInt(clip.length / step));
        var pos = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            clip.SampleAnimation(animated, Mathf.Min(clip.length, i * step));
            pos[i] = root.InverseTransformPoint(h.position);
        }
        float best = -1f;
        int bestI = n / 2;
        for (int i = 1; i <= n; i++)
        {
            float t = i * step;
            if (t < clip.length * 0.1f || t > clip.length * 0.85f)
                continue;
            float sp = (pos[i] - pos[i - 1]).magnitude / step;
            if (sp > best)
            {
                best = sp;
                bestI = i;
            }
        }
        Vector3 v = pos[bestI] - pos[bestI - 1];
        info.time = bestI * step;
        info.forwardness = v.sqrMagnitude > 1e-8f ? v.normalized.z : 0f;
        return info;
    }

    // ================================================================ body fitting
    sealed class BodyRay
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<int> _t = new List<int>();

        public BodyRay(GameObject body)
        {
            foreach (SkinnedMeshRenderer smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                int b = _v.Count;
                foreach (Vector3 p in baked.vertices)
                    _v.Add(m.MultiplyPoint3x4(p));
                foreach (int i in baked.triangles)
                    _t.Add(b + i);
                Object.DestroyImmediate(baked);
            }
        }

        public bool Cast(Vector3 o, Vector3 d, out Vector3 hit)
        {
            hit = Vector3.zero;
            float best = float.PositiveInfinity;
            d.Normalize();
            for (int i = 0; i < _t.Count; i += 3)
            {
                Vector3 a = _v[_t[i]], b = _v[_t[i + 1]], c = _v[_t[i + 2]];
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                Vector3 s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float w = Vector3.Dot(d, q) * inv;
                if (w < 0f || u + w > 1f) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > 0f && t < best) best = t;
            }
            if (float.IsInfinity(best)) return false;
            hit = o + d * best;
            return true;
        }

        public float Span(Vector3 center, Vector3 axis, float reach, float fallback)
        {
            bool a = Cast(center + axis * reach, -axis, out Vector3 h1);
            bool b = Cast(center - axis * reach, axis, out Vector3 h2);
            return a && b ? Vector3.Distance(h1, h2) : fallback;
        }
    }

    static Transform ChildMesh(GameObject kit, string name)
    {
        foreach (Transform t in kit.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // 접촉점 기준으로 설계된 파츠(Blender 원점 = 몸 표면 접촉점): 오브젝트 원점 오프셋까지 반영
    static readonly HashSet<string> s_contactParts = new HashSet<string> { "Mob_Back", "Mob_Chest", "Mob_PauldronL", "Mob_PauldronR" };

    static GameObject Attach(GameObject kit, string partName, Transform bone, Vector3 worldPos, Quaternion worldRot, Vector3 scale, string newName = null)
    {
        Transform src = ChildMesh(kit, partName);
        if (src == null || bone == null)
        {
            Log("attach failed: " + partName);
            return null;
        }
        GameObject go = Object.Instantiate(src.gameObject, bone, false);
        go.name = newName ?? partName;
        Quaternion rot = worldRot * s_kitFlip;
        Vector3 offset = s_contactParts.Contains(partName) ? rot * Vector3.Scale(src.localPosition, scale) : Vector3.zero;
        go.transform.SetPositionAndRotation(worldPos + offset, rot);
        Vector3 ls = bone.lossyScale;
        go.transform.localScale = new Vector3(scale.x / Mathf.Max(1e-4f, ls.x), scale.y / Mathf.Max(1e-4f, ls.y), scale.z / Mathf.Max(1e-4f, ls.z));
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
        }
        return go;
    }

    // 메시 로컬 X 방향이 FBX 변환에서 뒤집혔는지 (파츠 메시 bounds 중심 x 의 부호로 좌/우 판별)
    static float MeshCenterX(GameObject kit, string partName)
    {
        Transform t = ChildMesh(kit, partName);
        MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
        return mf != null && mf.sharedMesh != null ? t.localPosition.x + mf.sharedMesh.bounds.center.x : 0f;
    }

    struct Grip
    {
        public Vector3 pos;
        public Quaternion rot;
    }

    // 손가락 본으로 쥔 무기 방향: 칼날 = 새끼→검지 방향(엄지 쪽), 날 = 손가락 방향(주먹 앞)
    static Grip ComputeGrip(Animator a, bool right)
    {
        Transform hand = a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        Transform mid = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        Transform idx = a.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        Transform lit = a.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        if (hand == null)
            return new Grip { pos = Vector3.zero, rot = Quaternion.identity };
        Vector3 finger = mid != null ? (mid.position - hand.position).normalized : hand.right;
        Vector3 lateral = idx != null && lit != null ? (idx.position - lit.position) : hand.forward;
        Vector3 blade = (lateral - Vector3.Dot(lateral, finger) * finger).normalized;
        float handLen = mid != null ? Vector3.Distance(mid.position, hand.position) : 0.08f;
        Vector3 pos = hand.position + finger * handLen * 0.55f;
        Vector3 palm = Vector3.Cross(finger, blade);
        if (Vector3.Dot(palm, a.transform.position + Vector3.up * 1f - hand.position) < 0f)
            palm = -palm;
        pos += palm * 0.012f;
        Quaternion rot = Quaternion.LookRotation(finger, blade);
        Log($"grip {(right ? "R" : "L")} handLen={handLen:F3} blade={blade} finger={finger}");
        return new Grip { pos = pos, rot = rot };
    }

    static Dictionary<Transform, (Vector3, Quaternion, Vector3)> SnapshotPose(Transform root)
    {
        var d = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            d[t] = (t.localPosition, t.localRotation, t.localScale);
        return d;
    }

    static void RestorePose(Dictionary<Transform, (Vector3, Quaternion, Vector3)> d)
    {
        foreach (var kv in d)
        {
            if (kv.Key == null) continue;
            kv.Key.localPosition = kv.Value.Item1;
            kv.Key.localRotation = kv.Value.Item2;
            kv.Key.localScale = kv.Value.Item3;
        }
    }

    // ================================================================ android / enforcer
    static void BuildAndroid(Scene scene, Materials mats, bool enforcer)
    {
        string label = enforcer ? "Mob_ShieldEnforcer" : "Mob_SecurityAndroid";
        var bodyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BodyModelPath);
        var kitAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MeshDir + "/SM_Mob_AndroidKit.fbx");
        var enfAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MeshDir + "/SM_Mob_EnforcerKit.fbx");
        if (bodyAsset == null || kitAsset == null || (enforcer && enfAsset == null))
        {
            Log(label + ": missing body/kit asset");
            return;
        }

        var root = new GameObject(label);
        SceneManager.MoveGameObjectToScene(root, scene);
        var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyAsset, scene);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = "Visual";
        body.transform.SetParent(root.transform, false);
        float bodyScale = enforcer ? 1.08f : 1.0f;
        body.transform.localScale = Vector3.one * bodyScale;

        Animator animator = body.GetComponent<Animator>();
        if (animator == null)
            animator = body.AddComponent<Animator>();
        animator.applyRootMotion = false;
        if (body.GetComponent<MobAnimationEventSink>() == null)
            body.AddComponent<MobAnimationEventSink>(); // GhostSamurai 이동 클립 발소리 이벤트 수신 (no receiver 경고 방지)
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        if (animator.avatar == null || !animator.isHuman)
        {
            Log(label + ": body has no humanoid avatar");
        }

        // 몸체 머티리얼 (그래파이트 슈트 / 세라믹 / 어두운 얼굴)
        foreach (SkinnedMeshRenderer smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] ms = smr.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                string n = ms[i] != null ? ms[i].name : "";
                ms[i] = n.Contains("Face") ? mats.suitFace : n.Contains("Plastic") ? mats.suitPlate : mats.suit;
            }
            smr.sharedMaterials = ms;
            smr.updateWhenOffscreen = false;
        }

        var kit = (GameObject)PrefabUtility.InstantiatePrefab(kitAsset, scene);
        PrefabUtility.UnpackPrefabInstance(kit, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        GameObject enfKit = null;
        if (enforcer)
        {
            enfKit = (GameObject)PrefabUtility.InstantiatePrefab(enfAsset, scene);
            PrefabUtility.UnpackPrefabInstance(enfKit, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        // FBX 축 변환 결과 확인: 등 유닛(Blender +Y = 뒤로 자람) 메시 중심 z > 0 이면 키트 정면 = -Z → 부착 시 Y 180° 회전
        Transform backMesh = ChildMesh(kit, "Mob_Back");
        MeshFilter backMf = backMesh != null ? backMesh.GetComponent<MeshFilter>() : null;
        bool kitFlip = backMf != null && backMf.sharedMesh != null && backMesh.localPosition.z + backMf.sharedMesh.bounds.center.z > 0f;
        s_kitFlip = kitFlip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        Log($"{label}: kit faces {(kitFlip ? "-Z (flipped on attach)" : "+Z")}");
        Vector3 fwd = root.transform.forward, up = Vector3.up, rightV = root.transform.right;
        var ray = new BodyRay(body);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null)
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Transform lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        // --- helmet: fit to the head (ray-cast top / sides / front / back)
        if (head != null)
        {
            Vector3 hp = head.position;
            Vector3 top = ray.Cast(hp + up * 0.6f, -up, out Vector3 th) ? th : hp + up * 0.24f;
            float midY = (hp.y + top.y) * 0.5f + 0.01f;
            Vector3 c = new Vector3(hp.x, midY, hp.z);
            float w = ray.Span(c, rightV, 0.5f, 0.17f);
            float d = ray.Span(c, fwd, 0.5f, 0.2f);
            float h = top.y - hp.y;
            if (ray.Cast(c + fwd * 0.5f, -fwd, out Vector3 fh) && ray.Cast(c - fwd * 0.5f, fwd, out Vector3 bh))
                c.z = (fh.z + bh.z) * 0.5f;
            Vector3 nominal = enforcer ? new Vector3(0.126f, 0.15f, 0.146f) : new Vector3(0.118f, 0.142f, 0.138f); // (x, y=height, z=depth)
            var s = new Vector3(Mathf.Clamp(w * 0.5f * 1.16f / nominal.x, 0.6f, 1.8f), Mathf.Clamp(h * 0.5f * 1.2f / nominal.y, 0.6f, 1.8f), Mathf.Clamp(d * 0.5f * 1.14f / nominal.z, 0.6f, 1.8f));
            Attach(kit, enforcer ? "Mob_HelmetEnforcer" : "Mob_Helmet", head, c, Quaternion.identity, s, "Armor_Helmet");
            Log($"{label}: head w={w:F3} d={d:F3} h={h:F3} helmetScale={s}");
        }

        // --- chest plate + back unit
        float chestW = 0.3f;
        if (chest != null)
        {
            Vector3 cc = chest.position + up * 0.04f;
            chestW = ray.Span(cc, rightV, 0.6f, 0.3f);
            float sc = Mathf.Clamp(chestW * 0.98f / 0.38f, 0.6f, 1.6f) * (enforcer ? 1.08f : 1f);
            if (ray.Cast(cc + fwd * 0.6f, -fwd, out Vector3 fh))
                Attach(kit, "Mob_Chest", chest, fh + fwd * 0.004f, Quaternion.identity, Vector3.one * sc, "Armor_Chest");
            if (ray.Cast(cc - fwd * 0.6f + up * 0.03f, fwd, out Vector3 bh))
                Attach(kit, "Mob_Back", chest, bh - fwd * 0.004f, Quaternion.identity, Vector3.one * sc, "Armor_Back");
            Log($"{label}: chestW={chestW:F3} plateScale={sc:F2}");
        }

        // --- pauldrons (pick the mesh whose centre lies on the matching side after FBX axis conversion)
        float flipSign = kitFlip ? -1f : 1f;
        string rightPart = MeshCenterX(kit, "Mob_PauldronR") * flipSign > 0f ? "Mob_PauldronR" : "Mob_PauldronL";
        string leftPart = rightPart == "Mob_PauldronR" ? "Mob_PauldronL" : "Mob_PauldronR";
        float ps = Mathf.Clamp(chestW / 0.3f, 0.7f, 1.5f) * (enforcer ? 1.35f : 1.25f);
        foreach (var (bone, part, side) in new[] { (rUpper, rightPart, 1f), (lUpper, leftPart, -1f) })
        {
            if (bone == null) continue;
            Vector3 o = bone.position + rightV * side * 0.02f + up * 0.5f;
            if (ray.Cast(o, -up, out Vector3 sh))
                Attach(kit, part, bone, sh + up * 0.004f, Quaternion.identity, Vector3.one * ps, side > 0 ? "Armor_PauldronR" : "Armor_PauldronL");
        }

        // --- weapons
        Grip rg = ComputeGrip(animator, true);
        if (!enforcer)
            Attach(kit, "Mob_HFBlade", rHand, rg.pos, rg.rot, Vector3.one, "Weapon_HFBlade");
        else
            Attach(enfKit, "Mob_ShockBaton", rHand, rg.pos, rg.rot, Vector3.one, "Weapon_ShockBaton");

        // --- animator controller (+ loop copies)
        var oneShots = new Dictionary<string, AnimationClip>();
        float walkNorm;
        AnimationClip idle, f, b, l, r, run;
        if (!enforcer)
        {
            idle = FindClip("GhostSamurai_APose_Idle");
            f = LoopCopy("GhostSamurai_APose_Strafe_Walk_F_Loop_Inplace");
            b = LoopCopy("GhostSamurai_APose_Strafe_Walk_B_Inplace");
            l = LoopCopy("GhostSamurai_APose_Strafe_Walk_L_Inplace");
            r = LoopCopy("GhostSamurai_APose_Strafe_Walk_R_Inplace");
            run = LoopCopy("GhostSamurai_APose_Strafe_Run_F_Loop_Inplace");
            walkNorm = 1.6f / 3.8f;
        }
        else
        {
            idle = LoopCopy("GhostSamurai_DefenseL_Loop_Inplace");
            f = LoopCopy("GhostSamurai_DefenseL_Walk_F_Inplace");
            b = LoopCopy("GhostSamurai_DefenseL_Walk_B_Inplace");
            l = LoopCopy("GhostSamurai_DefenseL_Walk_L_Inplace");
            r = LoopCopy("GhostSamurai_DefenseL_Walk_R_Inplace");
            run = f;
            walkNorm = 1.3f / 2.9f;
        }

        oneShots["Alert"] = FindClip("GhostSamurai_APose_Equip01_Inplace");
        oneShots["Flinch"] = FindClip("GhostSamurai_APose_Hit_F_Inplace");
        oneShots["Stagger"] = FindClip("GhostSamurai_APose_Large_Hit_Inplace");
        oneShots["Die"] = FindClip(enforcer ? "GhostSamurai_APose_Die02_Inplace" : "GhostSamurai_APose_Die01_Inplace");
        AnimationClip thrustClip = null;
        if (!enforcer)
        {
            oneShots["ParryRecoil"] = FindClip("GhostSamurai_DefenseR_Rebound_Inplace");
            oneShots["Atk1"] = FindClip("GhostSamurai_APose_Attack01_1_ALL_Inplace");
            oneShots["Atk2"] = FindClip("GhostSamurai_APose_Attack01_2_Inplace");
            oneShots["Atk3"] = FindClip("GhostSamurai_APose_Attack01_3_ALL_Inplace");
            oneShots["Backstep"] = FindClip("GhostSamurai_APose_Dodge_B_Inplace");
        }
        else
        {
            oneShots["ParryRecoil"] = FindClip("GhostSamurai_DefenseL_Rebound_Inplace");
            oneShots["Bash"] = FindClip("GhostSamurai_DefenseL_Parry01_Inplace");
            oneShots["Baton"] = FindClip("GhostSamurai_APose_Attack01_1_ALL_Inplace");
            oneShots["ShieldHit"] = FindClip("GhostSamurai_DefenseL_Hit01_Inplace");
            oneShots["GuardBroken"] = FindClip("GhostSamurai_DefenseL_Broken_Inplace");
        }

        // 찌르기 클립 자동 선택 (피크 순간 손이 가장 '앞으로' 나가는 공격)
        var impacts = new Dictionary<string, float>();
        Dictionary<Transform, (Vector3, Quaternion, Vector3)> bind = SnapshotPose(body.transform);
        if (!enforcer)
        {
            float bestFw = -2f;
            foreach (string cand in new[] { "GhostSamurai_APose_Attack04_Inplace", "GhostSamurai_APose_Attack05_Inplace", "GhostSamurai_APose_Attack06_Inplace",
                         "GhostSamurai_APose_Attack07_Inplace", "GhostSamurai_APose_SPAttack01_Inplace", "GhostSamurai_APose_SPAttack02_Inplace",
                         "GhostSamurai_APose_SPAttack03_Inplace", "GhostSamurai_APose_Attack02_4_Inplace", "GhostSamurai_APose_Attack03_4_Inplace" })
            {
                AnimationClip c = FindClip(cand);
                if (c == null) continue;
                ImpactInfo ii = MeasureImpact(body, animator, c, HumanBodyBones.RightHand);
                Log($"  thrust candidate {cand}: impact={ii.time:F2}s fwd={ii.forwardness:F2} len={c.length:F2}");
                if (ii.forwardness > bestFw)
                {
                    bestFw = ii.forwardness;
                    thrustClip = c;
                    impacts["Thrust"] = ii.time;
                }
            }
            oneShots["Thrust"] = thrustClip;
            Log($"{label}: thrust clip = {(thrustClip != null ? thrustClip.name : "none")}");
        }
        foreach (string key in enforcer ? new[] { "Bash", "Baton" } : new[] { "Atk1", "Atk2", "Atk3" })
        {
            if (!oneShots.TryGetValue(key, out AnimationClip c) || c == null) continue;
            ImpactInfo ii = MeasureImpact(body, animator, c, key == "Bash" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            impacts[key] = ii.time;
            Log($"{label}: {key} {c.name} impact={ii.time:F2}s len={c.length:F2}");
        }

        // 방패: 가드 포즈에서 정면/수직이 되도록 왼 전완에 고정
        if (enforcer && lLower != null)
        {
            AnimationClip guard = FindClip("GhostSamurai_DefenseL_Loop_Inplace");
            if (guard != null)
                guard.SampleAnimation(body, Mathf.Min(0.3f, guard.length * 0.5f));
            Transform lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Vector3 mid = lHand != null ? Vector3.Lerp(lLower.position, lHand.position, 0.45f) : lLower.position;
            GameObject shield = Attach(enfKit, "Mob_RiotShield", lLower, mid + fwd * 0.24f, Quaternion.LookRotation(fwd, up), Vector3.one, "Shield");
            RestorePose(bind);
            if (shield != null)
            {
                Log($"{label}: shield local pos={shield.transform.localPosition} rot={shield.transform.localEulerAngles}");
            }
        }
        RestorePose(bind);

        string acPath = AnimDir + (enforcer ? "/AC_Mob_Enforcer.controller" : "/AC_Mob_Android.controller");
        AnimatorController ac = BuildController(acPath, oneShots, idle, f, b, l, r, run, walkNorm);
        animator.runtimeAnimatorController = ac;

        // --- gameplay components
        Transform lockPivot = new GameObject("LockPivot").transform;
        lockPivot.SetParent(root.transform, false);
        lockPivot.localPosition = new Vector3(0f, 1.2f * bodyScale, 0f);

        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var col = root.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.9f * bodyScale, 0f);
        col.radius = enforcer ? 0.42f : 0.34f;
        col.height = 1.8f * bodyScale;
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = enforcer ? 0.45f : 0.38f;
        agent.height = 1.8f * bodyScale;
        agent.speed = enforcer ? 2.9f : 3.8f;
        agent.acceleration = 18f;
        agent.angularSpeed = 0f;
        agent.stoppingDistance = 0.15f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        var health = root.AddComponent<EnemyHealth>();
        health.maxHP = enforcer ? 140 : 90;
        health.currentHP = health.maxHP;
        var perception = root.AddComponent<EnemyPerception>();
        perception.Configure(enforcer ? 14f : 16f, enforcer ? 110f : 120f, enforcer ? 10f : 12f);
        root.AddComponent<BossPatternHandTelegraph>();
        root.AddComponent<BossGroundTelegraph>();
        root.AddComponent<EnemyAttackCue>();
        EnemyHPBarPresenter presenter = AddHpBar(root, scene);

        EnemyBrain brain = enforcer ? (EnemyBrain)root.AddComponent<ShieldEnforcerBrain>() : root.AddComponent<SecurityAndroidBrain>();
        var so = new SerializedObject(brain);
        Set(so, "agent", agent);
        Set(so, "animator", animator);
        Set(so, "hpPresenter", presenter);
        Set(so, "lockPivot", lockPivot);
        Set(so, "hpBarOffset", new Vector3(0f, 0.75f, 0f));
        Set(so, "hitVfx", AssetDatabase.LoadAssetAtPath<GameObject>(HitVfxPath));
        Set(so, "hitSfx", AssetDatabase.LoadAssetAtPath<AudioClip>(SfxRoot + "/armor-impact-from-sword-393843.mp3"));
        Set(so, "swingSfx", AssetDatabase.LoadAssetAtPath<AudioClip>(SfxRoot + "/violent-sword-slice-2-393841.mp3"));
        Set(so, "alertBark", AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/SFX_Mob_Alert.wav"));
        if (!enforcer)
        {
            SetF(so, "maxPoise", 40f);
            SetF(so, "walkSpeed", 1.6f); SetF(so, "runSpeed", 3.8f); SetF(so, "strafeSpeed", 1.5f); SetF(so, "turnSpeedDeg", 420f);
            SetF(so, "ringRadius", 4.6f);
            SetImpact(so, "comboString.Array.data[0].clipImpactTime", impacts, "Atk1");
            SetImpact(so, "comboString.Array.data[1].clipImpactTime", impacts, "Atk2");
            SetImpact(so, "comboString.Array.data[2].clipImpactTime", impacts, "Atk3");
            SetImpact(so, "thrust.clipImpactTime", impacts, "Thrust");
        }
        else
        {
            SetF(so, "maxPoise", 60f);
            SetF(so, "walkSpeed", 1.3f); SetF(so, "runSpeed", 2.9f); SetF(so, "strafeSpeed", 1.2f); SetF(so, "turnSpeedDeg", 220f);
            SetF(so, "ringRadius", 4.0f);
            SetF(so, "attackCooldownMin", 1.6f); SetF(so, "attackCooldownMax", 2.6f);
            Transform shieldT = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Shield");
            Set(so, "shieldTransform", shieldT);
            Set(so, "blockSparkVfx", AssetDatabase.LoadAssetAtPath<GameObject>(HitVfxPath));
            Set(so, "blockClang", AssetDatabase.LoadAssetAtPath<AudioClip>(SfxRoot + "/sword-clashhit-393837.mp3"));
            Set(so, "guardBreakSfx", AssetDatabase.LoadAssetAtPath<AudioClip>(SfxRoot + "/Parry.wav"));
            SetImpact(so, "shieldBash.clipImpactTime", impacts, "Bash");
            SetImpact(so, "batonStrike.clipImpactTime", impacts, "Baton");
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // 장갑/무기 FBX 원본 정리
        Object.DestroyImmediate(kit);
        if (enfKit != null) Object.DestroyImmediate(enfKit);

        SetLayerAndTag(root);
        SavePrefab(root, PrefabDir + "/" + label + ".prefab");
        Object.DestroyImmediate(root);
    }

    static void SetImpact(SerializedObject so, string path, Dictionary<string, float> impacts, string key)
    {
        if (impacts.TryGetValue(key, out float t))
            SetF(so, path, t);
    }

    // ================================================================ turret
    static void BuildTurret(Scene scene, Materials mats, bool wall)
    {
        string label = wall ? "Mob_SentryTurret_Wall" : "Mob_SentryTurret_Floor";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MeshDir + "/SM_Mob_SentryTurret.fbx");
        if (asset == null)
        {
            Log(label + ": missing turret FBX");
            return;
        }
        var root = new GameObject(label);
        SceneManager.MoveGameObjectToScene(root, scene);
        var vis = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        vis.name = "Visual";
        vis.transform.SetParent(root.transform, false);

        Transform Find(string n) => vis.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
        Transform yaw = Find("Turret_Yaw"), head = Find("Turret_Head"), muzzle = Find("Muzzle"), pp = Find("PatternPivot");
        if (head != null && yaw != null) head.SetParent(yaw, true);
        if (muzzle != null && head != null) muzzle.SetParent(head, true);
        if (pp != null && head != null) pp.SetParent(head, true);
        Transform drop = Find(wall ? "Turret_MountFloor" : "Turret_MountWall");
        if (drop != null) Object.DestroyImmediate(drop.gameObject);
        if (muzzle != null && root.transform.InverseTransformPoint(muzzle.position).z < 0f)
        {
            vis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Log(label + ": FBX faced -Z, visual rotated 180");
        }
        if (yaw == null || head == null || muzzle == null)
        {
            Log(label + ": turret hierarchy incomplete");
            Object.DestroyImmediate(root);
            return;
        }
        // 피벗 회전이 루트 기준 정면이 되도록 머즐 방향 정렬 (FBX 축 변환 대비)
        muzzle.rotation = Quaternion.LookRotation(root.transform.forward, Vector3.up);
        if (pp != null) pp.rotation = muzzle.rotation;
        foreach (Renderer r in vis.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        Transform lockPivot = new GameObject("LockPivot").transform;
        lockPivot.SetParent(root.transform, false);
        lockPivot.position = head.position;

        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, wall ? 0.4f : 0.36f, wall ? -0.05f : 0f);
        col.size = wall ? new Vector3(0.5f, 0.55f, 0.75f) : new Vector3(0.6f, 0.72f, 0.6f);

        // 레이저 + 예광탄
        LineRenderer laser = MakeLine(root.transform, "Laser", mats.laser, 0.012f);
        var tracers = new LineRenderer[3];
        for (int i = 0; i < tracers.Length; i++)
            tracers[i] = MakeLine(root.transform, "Tracer" + i, mats.tracer, 0.035f);

        var health = root.AddComponent<EnemyHealth>();
        health.maxHP = 120;
        health.currentHP = 120;
        var perception = root.AddComponent<EnemyPerception>();
        perception.Configure(18f, 70f, 9f);
        var pso = new SerializedObject(perception);
        Set(pso, "eye", pp != null ? pp : muzzle);
        SetF(pso, "proximityRadius", 3f);
        pso.ApplyModifiedPropertiesWithoutUndo();
        root.AddComponent<BossPatternHandTelegraph>();
        root.AddComponent<BossGroundTelegraph>();
        root.AddComponent<EnemyAttackCue>();
        EnemyHPBarPresenter presenter = AddHpBar(root, scene);

        var brain = root.AddComponent<SentryTurretBrain>();
        var so = new SerializedObject(brain);
        Set(so, "yawPivot", yaw);
        Set(so, "pitchPivot", head);
        Set(so, "muzzle", muzzle);
        Set(so, "laser", laser);
        SetArray(so, "tracers", tracers);
        Set(so, "hpPresenter", presenter);
        Set(so, "lockPivot", lockPivot);
        Set(so, "hpBarOffset", new Vector3(0f, 0.45f, 0f));
        SetF(so, "yawLimit", wall ? 75f : 170f);
        SetF(so, "scanHalfAngle", wall ? 55f : 120f);
        SetF(so, "maxPoise", 60f);
        SetF(so, "flinchDuration", 0f);
        SetF(so, "staggerDuration", 1.6f);
        SetF(so, "leashRange", 24f);
        SetF(so, "alertDuration", 0.6f);
        SetF(so, "deathSinkDepth", 0f);
        so.FindProperty("deactivateOnDeath").boolValue = false;
        Set(so, "hitVfx", AssetDatabase.LoadAssetAtPath<GameObject>(HitVfxPath));
        Set(so, "hitSfx", AssetDatabase.LoadAssetAtPath<AudioClip>(SfxRoot + "/armor-impact-from-sword-393843.mp3"));
        Set(so, "alertBark", AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/SFX_Mob_Alert.wav"));
        Set(so, "fireSfx", AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/SFX_Mob_TurretShot.wav"));
        Set(so, "deathSfx", AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/SFX_Mob_PowerDown.wav"));
        Set(so, "deathVfx", AssetDatabase.LoadAssetAtPath<GameObject>(HitVfxPath));
        so.ApplyModifiedPropertiesWithoutUndo();

        SetLayerAndTag(root);
        SavePrefab(root, PrefabDir + "/" + label + ".prefab");
        Object.DestroyImmediate(root);
    }

    static LineRenderer MakeLine(Transform parent, string name, Material mat, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, Vector3.forward);
        lr.widthMultiplier = width;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        lr.enabled = false;
        return lr;
    }

    // ================================================================ drone
    static void BuildDrone(Scene scene)
    {
        const string label = "Mob_PatrolDrone";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyDronePrefabPath);
        if (src == null)
        {
            Log(label + ": lobby drone prefab missing");
            return;
        }
        var root = (GameObject)PrefabUtility.InstantiatePrefab(src, scene);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = label;
        // 로비 전용 컴포넌트 제거 (LobbyEnemy 는 비활성화 시 로비 처치 카운트를 올린다)
        foreach (MonoBehaviour mb in root.GetComponents<MonoBehaviour>())
        {
            if (mb is LobbyEnemy || mb is TutorialDrone || mb is ProximityBossUI)
                Object.DestroyImmediate(mb);
        }

        var drone = root.GetComponent<DroneController>();
        var dso = new SerializedObject(drone);
        dso.FindProperty("useAttackTokens").boolValue = true;
        dso.FindProperty("mirrorEnemyHealth").boolValue = true;
        dso.ApplyModifiedPropertiesWithoutUndo();
        var health = root.GetComponent<EnemyHealth>();
        if (health == null)
            health = root.AddComponent<EnemyHealth>();
        health.maxHP = drone.maxHP;
        health.currentHP = drone.maxHP;

        Transform lockPivot = root.transform.Find("LockPivot");
        if (lockPivot == null)
        {
            lockPivot = new GameObject("LockPivot").transform;
            lockPivot.SetParent(root.transform, false);
        }
        var perception = root.AddComponent<EnemyPerception>();
        perception.Configure(14f, 140f, 10f);
        var pso = new SerializedObject(perception);
        SetF(pso, "eyeHeight", 0f);
        pso.ApplyModifiedPropertiesWithoutUndo();
        EnemyHPBarPresenter presenter = AddHpBar(root, scene);
        var brain = root.AddComponent<PatrolDroneBrain>();
        var so = new SerializedObject(brain);
        Set(so, "hpPresenter", presenter);
        Set(so, "lockPivot", lockPivot);
        Set(so, "alertBark", AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/SFX_Mob_Alert.wav"));
        so.ApplyModifiedPropertiesWithoutUndo();

        SetLayerAndTag(root);
        SavePrefab(root, PrefabDir + "/" + label + ".prefab");
        Object.DestroyImmediate(root);
    }

    // ================================================================ shared helpers
    static EnemyHPBarPresenter AddHpBar(GameObject root, Scene scene)
    {
        var presenter = root.AddComponent<EnemyHPBarPresenter>();
        var barAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HpBarPrefabPath);
        if (barAsset != null)
        {
            var bar = (GameObject)PrefabUtility.InstantiatePrefab(barAsset, scene);
            bar.name = "HPBar";
            bar.transform.SetParent(root.transform, false);
            presenter.bar = bar.GetComponent<EnemyHPBar>();
            // 활성 상태로 둔다: EnemyHPBar.SetVisible 은 Awake 전 기본값(보임)을 기준으로 토글 → 두뇌 Start 의 Detach 가 숨긴다
        }
        return presenter;
    }

    static void SetLayerAndTag(GameObject root)
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        root.tag = "Enemy";
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponentInParent<EnemyHPBar>(true) != null)
                continue;
            if (enemyLayer >= 0)
                t.gameObject.layer = enemyLayer;
        }
    }

    static void SavePrefab(GameObject root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
        Log((ok ? "saved " : "FAILED ") + path);
    }

    static void Set(SerializedObject so, string prop, Object value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Log("no property " + prop + " on " + so.targetObject.GetType().Name); return; }
        p.objectReferenceValue = value;
    }

    static void Set(SerializedObject so, string prop, Vector3 value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Log("no property " + prop); return; }
        p.vector3Value = value;
    }

    static void SetF(SerializedObject so, string prop, float value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Log("no property " + prop + " on " + so.targetObject.GetType().Name); return; }
        p.floatValue = value;
    }

    static void SetArray(SerializedObject so, string prop, Object[] values)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Log("no property " + prop); return; }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static T FirstOrDefault<T>(this T[] arr, Func<T, bool> pred) where T : class
    {
        foreach (T t in arr)
            if (pred(t)) return t;
        return null;
    }
}
