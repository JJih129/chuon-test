// 반복 16: Type-D 개편 에셋 빌더 (수동 실행 전용, 자동 실행 없음).
//  BuildAssets(): Blender 모델(SM_OH_*.fbx) 임포트·재질 리맵, VFX 프리팹 5종(URP 파티클), 효과음, Resources/TypeD/TypeD_OverhaulAssets.asset
//  BuildAnimators(): BossAnimator / PlayerAnimatorController / AC_Mob_Android·Enforcer 에 OH_* 상태 추가 (GhostSamurai 휴머노이드 클립)
//  ClipReport(): 처형 짝 클립의 길이·루트 시작/끝(정렬 규칙 확인용)
// 실행: Unity MCP execute_code → return TypeDOverhaulBuilder.BuildAll();  또는 메뉴 ChuOn/Type-D Overhaul/Build All
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class TypeDOverhaulBuilder
{
    const string Root = "Assets/Art/ChuonPolish/TypeD/Overhaul";
    const string MeshDir = Root + "/Meshes";
    const string AudioDir = Root + "/Audio";
    const string VfxDir = Root + "/VFX";
    const string AnimDir = Root + "/Anim";
    const string AssetPath = "Assets/Art/ChuonPolish/TypeD/Resources/TypeD/TypeD_OverhaulAssets.asset";
    const string InfMatDir = "Assets/Art/ChuonPolish/Tutorial/Materials";
    const string GhostRoot = "Assets/GhostSamurai_Animset/Animation/katana";
    const string BossController = "Assets/Anime/Animator/BossAnimator.controller";
    const string PlayerController = "Assets/Anime/Animator/PlayerAnimatorController.controller";
    static readonly string[] MobControllers = { "Assets/Art/ChuonPolish/Mobs/Anim/AC_Mob_Android.controller", "Assets/Art/ChuonPolish/Mobs/Anim/AC_Mob_Enforcer.controller" };

    // 상태 → 클립 (FBX 파일 이름, Inplace 우선)
    static readonly (string state, string clip, bool loop, float speed)[] BossStates =
    {
        ("OH_Flinch", "GhostSamurai_APose_Hit_F", false, 1.1f),
        ("OH_Stagger", "GhostSamurai_APose_Large_Hit", false, 1f),
        ("OH_Knockback", "GhostSamurai_APose_Large_Hit_2", false, 1f),
        ("OH_WallSplat", "GhostSamurai_APose_Hit_B", false, 0.8f),
        ("OH_DownStart", "GhostSamurai_APose_Falldown_F_Start", false, 1f),
        ("OH_DownLoop", "GhostSamurai_APose_Falldown_F_Loop", true, 1f),
        ("OH_GetUp", "GhostSamurai_APose_Falldown_F_End", false, 1.15f),
        ("OH_Roar", "GhostSamurai_APose_Equip01", false, 0.85f),
        ("OH_Grab", "GhostSamurai_APose_Attack05", false, 1f),
        ("OH_Vent", "GhostSamurai_DefenseL_Loop", true, 0.6f),
        ("OH_DespCharge", "GhostSamurai_DefenseL_Loop", true, 0.45f),
        ("OH_DespRelease", "GhostSamurai_APose_SPAttack01", false, 1.2f),
        ("OH_ExecutedFront", "GhostSamurai_Executed01", false, 1f),
        ("OH_ExecutedBack", "GhostSamurai_Executed04", false, 1.35f),
        ("OH_ExecutedFinal", "GhostSamurai_Executed07", false, 1f),
    };

    static readonly (string state, string clip, bool loop, float speed)[] PlayerStates =
    {
        ("OH_ExecFront", "GhostSamurai_Execution01", false, 1f),
        ("OH_ExecBack", "GhostSamurai_Execution04", false, 1.35f),
        ("OH_ExecFinal", "GhostSamurai_Execution07", false, 1f),
        ("OH_ExecMob", "GhostSamurai_Execution06", false, 2.0f),
        ("OH_Takedown", "GhostSamurai_Ambush01", false, 1.6f),
    };

    static readonly (string state, string clip, bool loop, float speed)[] MobStates =
    {
        ("OH_Executed", "GhostSamurai_Executed06", false, 2.0f),
        ("OH_Ambushed", "GhostSamurai_Ambushed01", false, 1.6f),
    };

    static readonly StringBuilder Log = new StringBuilder();

    [MenuItem("ChuOn/Type-D Overhaul/Build All")]
    static void BuildAllMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        string a = BuildAssets();
        string b = BuildAnimators();
        return a + "\n" + b;
    }

    // ======================================================================= assets

    public static string BuildAssets()
    {
        Log.Clear();
        EnsureFolder(Root);
        EnsureFolder(VfxDir);
        EnsureFolder(AnimDir);
        ConfigureModels();
        var asset = AssetDatabase.LoadAssetAtPath<TypeDOverhaulAssets>(AssetPath);
        if (asset == null)
        {
            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<TypeDOverhaulAssets>();
            AssetDatabase.CreateAsset(asset, AssetPath);
        }

        asset.powerNodeModel = Model("PowerNode");
        asset.powerNodeBrokenModel = Model("PowerNodeBroken");
        asset.shutterModel = Model("Shutter");
        asset.coverModel = Model("Cover");
        asset.visorShardModel = Model("VisorShard");
        asset.armBladeModel = Model("ArmBlade");
        asset.bladeShardModel = Model("BladeShard");
        asset.sentryTurretPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Mob_SentryTurret_Wall.prefab");
        asset.patrolDronePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Mob_PatrolDrone.prefab");

        Material add = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/ChuonPolish/VFX/BossVfx_EmberAdditive.mat");
        Material soft = SoftAlphaMaterial(add);
        asset.sparkVfx = BuildBurst("VFX_OH_Sparks", add, new Color(1f, 0.55f, 0.35f) * 2.2f, 26, 7f, 0.04f, 0.35f, 0.6f, 0f, true);
        asset.dustVfx = BuildBurst("VFX_OH_Dust", soft, new Color(0.55f, 0.53f, 0.5f, 0.55f), 18, 2.2f, 0.6f, 1.3f, 1.2f, 0.6f, false);
        asset.steamVfx = BuildBurst("VFX_OH_Steam", soft, new Color(0.92f, 0.9f, 0.88f, 0.42f), 30, 3.5f, 0.9f, 2.0f, 1.8f, -0.3f, false);
        asset.coreBurstVfx = BuildBurst("VFX_OH_CoreBurst", add, new Color(1f, 0.22f, 0.12f) * 3.5f, 60, 9f, 0.05f, 0.7f, 1.0f, 0.2f, true);
        asset.blinkVfx = BuildBurst("VFX_OH_Blink", add, new Color(0.95f, 0.93f, 0.9f) * 2f, 34, 3f, 0.06f, 0.4f, 0.5f, 0f, true);

        asset.wallSplat = Sfx("WallSplat");
        asset.impactHeavy = Sfx("ImpactHeavy");
        asset.nodeHit = Sfx("NodeHit");
        asset.nodeBreak = Sfx("NodeBreak");
        asset.shutterDrop = Sfx("ShutterDrop");
        asset.shutterRise = Sfx("ShutterRise");
        asset.lockdownAlarm = Sfx("LockdownAlarm");
        asset.ventCharge = Sfx("VentCharge");
        asset.ventBlast = Sfx("VentBlast");
        asset.despCharge = Sfx("DespCharge");
        asset.despBlast = Sfx("DespBlast");
        asset.strongParry = Sfx("StrongParry");
        asset.blink = Sfx("Blink");
        asset.execSlash = Sfx("ExecSlash");
        asset.execFinal = Sfx("ExecFinal");
        asset.partBreak = Sfx("PartBreak");
        asset.armourPurge = Sfx("ArmourPurge");
        asset.grabWhoosh = Sfx("GrabWhoosh");
        asset.coverRise = Sfx("CoverRise");
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Log.Append("asset ok; ");
        return "[TypeDOverhaulBuilder] " + Log;
    }

    static void ConfigureModels()
    {
        string[] slots = { "Graphite", "Ceramic", "Concrete", "Glass", "Red", "Amber", "White", "Rubber", "Hazard", "FacadeA", "FacadeB", "Container", "Shutter", "Steel" };
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MeshDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null)
                continue;
            bool changed = false;
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> map = mi.GetExternalObjectMap();
            foreach (string s in slots)
            {
                Material target = AssetDatabase.LoadAssetAtPath<Material>($"{InfMatDir}/M_INF_{s}.mat");
                if (target == null)
                    continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "INF_" + s);
                if (!map.TryGetValue(id, out Object cur) || cur != target)
                {
                    mi.AddRemap(id, target);
                    changed = true;
                }
            }
            if (mi.importCameras || mi.importLights || mi.importAnimation || mi.animationType != ModelImporterAnimationType.None)
            {
                mi.importCameras = false;
                mi.importLights = false;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                changed = true;
            }
            if (changed)
            {
                mi.SaveAndReimport();
                n++;
            }
        }
        Log.Append($"models remapped {n}; ");
    }

    static GameObject Model(string name)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/SM_OH_{name}.fbx");
        if (go == null)
            Log.Append($"missing SM_OH_{name}; ");
        return go;
    }

    static AudioClip Sfx(string name)
    {
        string p = $"{AudioDir}/SFX_OH_{name}.wav";
        var imp = AssetImporter.GetAtPath(p) as AudioImporter;
        if (imp != null && !imp.forceToMono)
        {
            imp.forceToMono = true;
            imp.loadInBackground = false;
            AudioImporterSampleSettings s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
        if (clip == null)
            Log.Append($"missing {p}; ");
        return clip;
    }

    static Material SoftAlphaMaterial(Material additive)
    {
        string p = VfxDir + "/M_OH_SoftAlpha.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m != null)
            return m;
        if (additive == null)
            return null;
        m = new Material(additive);
        m.SetFloat("_Blend", 0f); // alpha
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = 3000;
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    static GameObject BuildBurst(string name, Material mat, Color color, int count, float speed, float size, float lifetime, float duration, float gravity, bool stretch)
    {
        string p = $"{VfxDir}/{name}.prefab";
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.3f);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count * 2;
        main.stopAction = ParticleSystemStopAction.None;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = stretch ? 0.08f : 0.35f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, stretch ? AnimationCurve.Linear(0f, 1f, 1f, 0.2f) : AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch)
        {
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
        }
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, p);
        Object.DestroyImmediate(go);
        return saved;
    }

    // ======================================================================= animators

    public static string BuildAnimators()
    {
        Log.Clear();
        EnsureFolder(AnimDir);
        AddStates(BossController, 0, BossStates, "Locomotion", true);
        AddStates(PlayerController, 0, PlayerStates, "Locomotion", false);
        foreach (string mc in MobControllers)
            AddStates(mc, 0, MobStates, null, false);
        AssetDatabase.SaveAssets();
        return "[TypeDOverhaulBuilder] animators: " + Log;
    }

    static void AddStates(string controllerPath, int layer, (string state, string clip, bool loop, float speed)[] states, string exitTo, bool boss)
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (ac == null)
        {
            Log.Append($"missing controller {controllerPath}; ");
            return;
        }
        AnimatorStateMachine sm = ac.layers[layer].stateMachine;
        AnimatorState exitState = exitTo != null ? sm.states.Select(s => s.state).FirstOrDefault(s => s.name == exitTo) : null;
        bool hasBreak = ac.parameters.Any(p => p.name == "IsBreak");
        bool hasSpecialSpeed = ac.parameters.Any(p => p.name == "SpecialSpeed");
        int added = 0, updated = 0;
        for (int i = 0; i < states.Length; i++)
        {
            (string stateName, string clipName, bool loop, float speed) = states[i];
            AnimationClip clip = FindClip(clipName, preferInplace: true);
            if (clip == null)
            {
                Log.Append($"no clip {clipName}; ");
                continue;
            }
            if (loop)
                clip = LoopCopy(clip, stateName);
            AnimatorState st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
            if (st == null)
            {
                st = sm.AddState(stateName, new Vector3(900f, 60f * i, 0f));
                added++;
            }
            else
            {
                updated++;
                foreach (AnimatorStateTransition t in st.transitions.ToArray())
                    st.RemoveTransition(t);
            }
            st.motion = clip;
            st.speed = speed;
            st.writeDefaultValues = true;
            st.iKOnFeet = false;
            bool special = boss && (stateName == "OH_Grab" || stateName == "OH_Vent" || stateName == "OH_DespCharge" || stateName == "OH_DespRelease" || stateName == "OH_Roar");
            st.speedParameterActive = special && hasSpecialSpeed;
            if (st.speedParameterActive)
                st.speedParameter = "SpecialSpeed";

            bool executed = stateName.StartsWith("OH_Executed") || stateName == "OH_Ambushed";
            if (exitState != null && !loop && !executed && stateName != "OH_DownStart")
            {
                AnimatorStateTransition t = st.AddTransition(exitState);
                t.hasExitTime = true;
                t.exitTime = 0.94f;
                t.duration = 0.18f;
                t.hasFixedDuration = true;
            }
            if (boss && stateName == "OH_DownStart")
            {
                AnimatorState loopSt = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "OH_DownLoop");
                if (loopSt != null)
                {
                    AnimatorStateTransition t = st.AddTransition(loopSt);
                    t.hasExitTime = true;
                    t.exitTime = 0.95f;
                    t.duration = 0.1f;
                    t.hasFixedDuration = true;
                }
            }
            if (boss && executed && exitState != null && hasBreak)
            {
                // 처형 뒤 브레이크가 풀리면 일어선다
                AnimatorStateTransition t = st.AddTransition(exitState);
                t.hasExitTime = false;
                t.duration = 0.35f;
                t.hasFixedDuration = true;
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsBreak");
            }
        }
        EditorUtility.SetDirty(ac);
        Log.Append($"{Path.GetFileNameWithoutExtension(controllerPath)} +{added} ~{updated}; ");
    }

    static AnimationClip FindClip(string baseName, bool preferInplace)
    {
        string[] guids = AssetDatabase.FindAssets(baseName + " t:Model", new[] { GhostRoot });
        string inplace = null, root = null;
        foreach (string g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string f = Path.GetFileNameWithoutExtension(p);
            if (f == baseName + "_Inplace") inplace = p;
            else if (f == baseName + "_Root") root = p;
            else if (f == baseName && root == null) root = p;
        }
        string pick = preferInplace && inplace != null ? inplace : root ?? inplace;
        if (pick == null)
            return null;
        return AssetDatabase.LoadAllAssetsAtPath(pick).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }

    static AnimationClip LoopCopy(AnimationClip src, string stateName)
    {
        string p = $"{AnimDir}/{stateName}_{src.name}_Loop.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (existing != null)
            return existing;
        var copy = new AnimationClip();
        EditorUtility.CopySerialized(src, copy);
        AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(copy);
        s.loopTime = true;
        s.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(copy, s);
        AssetDatabase.CreateAsset(copy, p);
        return copy;
    }

    // ======================================================================= report

    public static string ClipReport()
    {
        var sb = new StringBuilder();
        string[] names =
        {
            "GhostSamurai_Execution01", "GhostSamurai_Executed01", "GhostSamurai_Execution02", "GhostSamurai_Executed02",
            "GhostSamurai_Execution03", "GhostSamurai_Executed03", "GhostSamurai_Execution04", "GhostSamurai_Executed04",
            "GhostSamurai_Execution05", "GhostSamurai_Executed05", "GhostSamurai_Execution06", "GhostSamurai_Executed06",
            "GhostSamurai_Execution07", "GhostSamurai_Executed07", "GhostSamurai_Ambush01", "GhostSamurai_Ambushed01",
            "GhostSamurai_APose_Large_Hit", "GhostSamurai_APose_Large_Hit_2", "GhostSamurai_APose_Hit_F", "GhostSamurai_APose_Hit_B",
            "GhostSamurai_APose_Falldown_F_Start", "GhostSamurai_APose_Falldown_F_End", "GhostSamurai_APose_Equip01",
            "GhostSamurai_APose_Attack05", "GhostSamurai_APose_SPAttack01"
        };
        foreach (string n in names)
        {
            foreach (bool inplace in new[] { false, true })
            {
                AnimationClip c = FindClip(n, inplace);
                if (c == null)
                {
                    sb.Append(n).Append(inplace ? " [in]" : " [root]").Append(" missing\n");
                    continue;
                }
                sb.Append(n).Append(inplace ? " [in] " : " [root] ").Append("len=").Append(c.length.ToString("F2"));
                sb.Append(" T0=").Append(Sample(c, "RootT", 0f)).Append(" T1=").Append(Sample(c, "RootT", c.length));
                sb.Append(" yaw0=").Append(Yaw(c, 0f).ToString("F0")).Append(" yaw1=").Append(Yaw(c, c.length).ToString("F0"));
                AnimationEvent[] ev = AnimationUtility.GetAnimationEvents(c);
                if (ev.Length > 0)
                    sb.Append(" ev=").Append(string.Join(",", ev.Select(e => e.functionName + "@" + e.time.ToString("F2"))));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    static string Sample(AnimationClip c, string prop, float t)
    {
        float x = Curve(c, prop + ".x", t), y = Curve(c, prop + ".y", t), z = Curve(c, prop + ".z", t);
        return $"({x:F2},{y:F2},{z:F2})";
    }

    static float Yaw(AnimationClip c, float t)
    {
        var q = new Quaternion(Curve(c, "RootQ.x", t), Curve(c, "RootQ.y", t), Curve(c, "RootQ.z", t), Curve(c, "RootQ.w", t));
        if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f)
            return 0f;
        return q.normalized.eulerAngles.y;
    }

    static float Curve(AnimationClip c, string prop, float t)
    {
        EditorCurveBinding b = EditorCurveBinding.FloatCurve("", typeof(Animator), prop);
        AnimationCurve curve = AnimationUtility.GetEditorCurve(c, b);
        return curve != null ? curve.Evaluate(t) : 0f;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
