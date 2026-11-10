// 처형 카메라 미리보기 / 라이브러리 에셋 생성 (에디터 전용, 자동 실행 없음 — 메뉴 또는 MCP 로만 호출).
// RenderSheets: 임시 추가 씬(저장 안 함, 프로젝트 씬과 멀리 떨어진 좌표)에 플레이어·피격자 모델을 놓고
//               처형 애니메이션을 샘플링해 종류×변형마다 8 프레임 시트를 Temp/iexec/ 에 PNG 로 쓴다.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExecutionCamPreviewTool
{
    public const string LibraryAssetPath = "Assets/Settings/Camera/ExecutionCam/Resources/ExecutionCam/ExecutionCamLibrary.asset";
    static readonly Vector3 StageOrigin = new Vector3(4000f, -600f, 4000f);

    [MenuItem("Chuon/Camera/Execution Cam/Write Default Library Asset")]
    public static void WriteDefaultLibraryMenu() => Debug.Log(WriteDefaultLibrary());

    public static string WriteDefaultLibrary()
    {
        string dir = Path.GetDirectoryName(LibraryAssetPath).Replace('\\', '/');
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var existing = AssetDatabase.LoadAssetAtPath<ExecutionCamLibrary>(LibraryAssetPath);
        if (existing != null)
        {
            existing.BuildDefaults();
            EditorUtility.SetDirty(existing);
        }
        else
        {
            var lib = ScriptableObject.CreateInstance<ExecutionCamLibrary>();
            lib.BuildDefaults();
            AssetDatabase.CreateAsset(lib, LibraryAssetPath);
        }
        AssetDatabase.SaveAssets();
        return "ExecutionCamLibrary written: " + LibraryAssetPath;
    }

    [MenuItem("Chuon/Camera/Execution Cam/Render Preview Sheets (Temp_iexec)")]
    public static void RenderSheetsMenu() => Debug.Log(RenderSheets("Temp/iexec", -1, true));

    struct Timing { public float strike, duration, distance, slowScale, slowRealtime; public bool facing; }

    static Timing TimingFor(ExecCamType t)
    {
        switch (t)
        {
            case ExecCamType.BossFront: return new Timing { strike = 1.15f, duration = 2.5f, distance = 1.55f, slowScale = 0.2f, slowRealtime = 0.32f, facing = true };
            case ExecCamType.BossBack: return new Timing { strike = 1.0f, duration = 2.3f, distance = 1.35f, slowScale = 0.2f, slowRealtime = 0.3f, facing = false };
            case ExecCamType.BossFinal: return new Timing { strike = 1.6f, duration = 3.4f, distance = 1.5f, slowScale = 0.15f, slowRealtime = 0.45f, facing = true };
            case ExecCamType.Takedown: return new Timing { strike = 1.0f, duration = 1.5f, distance = 0.9f, slowScale = 1f, slowRealtime = 0f, facing = false };
            default: return new Timing { strike = 0.7f, duration = 1.2f, distance = 1.2f, slowScale = 0.25f, slowRealtime = 0.12f, facing = true };
        }
    }

    /// <summary>typeFilter: −1 = 전부, 아니면 ExecCamType 값. wallTest: 오른쪽 벽 추가 시트(대체 각도 확인).</summary>
    public static string RenderSheets(string outDir, int typeFilter, bool wallTest)
    {
        var log = new StringBuilder();
        Scene prevActive = SceneManager.GetActiveScene();
        bool prevDirty = prevActive.isDirty;
        Scene stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        bool animMode = false;
        RenderTexture rt = null;
        Texture2D sheet = null;
        try
        {
            SceneManager.SetActiveScene(stage);
            Directory.CreateDirectory(outDir);
            ExecutionCamLibrary lib = AssetDatabase.LoadAssetAtPath<ExecutionCamLibrary>(LibraryAssetPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<ExecutionCamLibrary>();
                lib.BuildDefaults();
            }

            BuildStage();
            GameObject player = SpawnModel(LoadAnimatorRoot("Assets/Prefabs/Generated/PlayerRoot.prefab"), "PV_Player");
            GameObject boss = SpawnModel(FindBossAnimatorRoot(), "PV_Boss");
            GameObject android = SpawnModel(LoadAnimatorRoot("Assets/Prefabs/Mobs/Mob_SecurityAndroid.prefab"), "PV_Android");
            GameObject enforcer = SpawnModel(LoadAnimatorRoot("Assets/Prefabs/Mobs/Mob_ShieldEnforcer.prefab"), "PV_Enforcer");
            log.AppendLine($"models player={(player != null)} boss={(boss != null)} android={(android != null)} enforcer={(enforcer != null)}");
            if (player == null)
                return log.Append("no player model").ToString();

            var camGo = new GameObject("PV_Cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            const int W = 480, H = 270, Cols = 4, Rows = 2;
            cam.aspect = (float)W / H;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            sheet = new Texture2D(W * Cols, H * Rows, TextureFormat.RGB24, false);

            AnimationMode.StartAnimationMode();
            animMode = true;

            var jobs = new List<(ExecCamType type, int variant, bool wall)>();
            for (int t = 0; t <= (int)ExecCamType.Takedown; t++)
            {
                if (typeFilter >= 0 && t != typeFilter)
                    continue;
                ExecCamTypeEntry e = lib.Get((ExecCamType)t);
                for (int v = 0; v < e.variants.Count; v++)
                    jobs.Add(((ExecCamType)t, v, false));
                jobs.Add(((ExecCamType)t, -1, false));   // 움직임 줄이기
            }
            if (wallTest && (typeFilter < 0 || typeFilter == 0))
                jobs.Add((ExecCamType.BossFront, 0, true));

            GameObject wall = null;
            foreach (var job in jobs)
            {
                ExecCamTypeEntry entry = lib.Get(job.type);
                bool bossType = job.type <= ExecCamType.BossFinal;
                GameObject victim = bossType ? boss : job.type == ExecCamType.Enforcer ? enforcer : android;
                if (victim == null)
                {
                    log.AppendLine($"{job.type}: no victim model");
                    continue;
                }
                SetOnly(player, victim, boss, android, enforcer);
                Timing tm = TimingFor(job.type);
                // ExecutionDirector 정렬 재현: 피격자 원점, 플레이어가 거리만큼 떨어져 마주 봄 / 등 뒤
                Vector3 vpos = StageOrigin;
                victim.transform.SetPositionAndRotation(vpos, tm.facing ? Quaternion.LookRotation(Vector3.back) : Quaternion.LookRotation(Vector3.forward));
                float dist = tm.distance + (bossType ? 0.6f : 0f);
                Vector3 ppos = vpos + Vector3.back * dist;
                player.transform.SetPositionAndRotation(ppos, Quaternion.LookRotation(Vector3.forward));

                if (wall != null) Object.DestroyImmediate(wall);
                if (job.wall)
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = "PV_Wall";
                    wall.transform.position = vpos + new Vector3(2.4f, 2f, -dist * 0.5f);
                    wall.transform.localScale = new Vector3(0.4f, 4f, 9f);
                }
                Physics.SyncTransforms();

                Transform focus = ResolveFocus(victim, bossType);
                bool calm = job.variant < 0;
                ExecCamVariant variant = calm ? entry.calm : entry.variants[job.variant];
                float pre = tm.strike;
                float post = ExecutionCamChoreographer.PredictPostRealtime(entry, tm.strike, tm.duration, tm.slowScale, tm.slowRealtime, calm);
                float total = pre + post;

                // 첫 프레임 포즈로 컨텍스트(키·바운드) 측정
                SamplePose(player, victim, job.type, 0f);
                ExecCamContext ctx = ExecutionCamChoreographer.BuildContext(lib, job.type, player.transform, victim.transform, focus, cam.aspect);
                ctx.side = 1f;
                int idx = job.variant;
                if (!calm)
                {
                    ExecutionCamChoreographer.QaForceVariant = job.variant;
                    variant = ExecutionCamChoreographer.PickVariant(lib, entry, ref ctx, out idx);
                }
                var tl = new ExecCamTimeline();
                ExecCamOutput gp;
                var initial = new ExecCamOutput();
                tl.Begin(lib, variant, ctx, initial);
                gp = tl.GameplayPose();
                tl.Begin(lib, variant, ctx, gp);

                // 60 fps 로 진행하며 8 프레임 캡처 (타격 직전/직후 포함)
                float[] caps = { 0.05f, pre * 0.35f, pre * 0.7f, pre - 0.03f, pre + 0.06f, pre + post * 0.35f, pre + post * 0.7f, total - 0.02f };
                int ci = 0;
                const float dt = 1f / 60f;
                var shotNames = new StringBuilder();
                for (float r = 0f; r <= total + 0.0001f && ci < caps.Length; r += dt)
                {
                    bool impacted = r >= pre;
                    ExecCamPhase ph = impacted ? ExecCamPhase.PostImpact : ExecCamPhase.PreImpact;
                    float u = impacted ? Mathf.Clamp01((r - pre) / post) : Mathf.Clamp01(r / pre);
                    ExecCamOutput o = tl.Evaluate(ph, u, dt);
                    if (r + dt * 0.5f >= caps[ci])
                    {
                        float gameT = r < pre ? r : Mathf.Min(tm.duration, pre + (r - pre) * (tm.duration - pre) / Mathf.Max(0.01f, post));
                        SamplePose(player, victim, job.type, gameT);
                        if (focus != null) tl.UpdateImpactPoint(focus.position);
                        cam.transform.SetPositionAndRotation(o.position, o.rotation * Quaternion.Euler(0f, 0f, o.dutch));
                        cam.fieldOfView = o.fov;
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture.active = rt;
                        int cx = ci % Cols, cy = Rows - 1 - ci / Cols;
                        sheet.ReadPixels(new Rect(0, 0, W, H), cx * W, cy * H);
                        RenderTexture.active = null;
                        shotNames.Append($"[{ci}] t={r:0.00} {tl.CurrentShotName} fov={o.fov:0} dutch={o.dutch:0.0} camY={o.position.y - StageOrigin.y:0.00}; ");
                        ci++;
                    }
                }
                sheet.Apply();
                string name = calm ? $"{job.type}_calm" : $"{job.type}_{variant.name}{(job.wall ? "_WALLR" : "")}";
                string file = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(file, sheet.EncodeToPNG());
                log.AppendLine($"{name}: pre={pre:0.00} post={post:0.00} total={total:0.00} side={(ctx.side >= 0 ? "R" : "L")} fallbacks={tl.FallbackCount} safe={tl.SafeShotCount} vH={ctx.victimHeight:0.00} aH={ctx.attackerHeight:0.00} -> {file}");
                log.AppendLine("   " + shotNames);
            }
        }
        catch (System.Exception ex)
        {
            log.AppendLine("EXCEPTION " + ex);
        }
        finally
        {
            if (animMode)
                AnimationMode.StopAnimationMode();
            if (rt != null) Object.DestroyImmediate(rt);
            if (sheet != null) Object.DestroyImmediate(sheet);
            if (prevActive.IsValid())
                SceneManager.SetActiveScene(prevActive);
            EditorSceneManager.CloseScene(stage, true);
            log.AppendLine($"stage closed; '{prevActive.name}' dirty before={prevDirty} after={prevActive.isDirty}");
        }
        return log.ToString();
    }

    static void BuildStage()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "PV_Floor";
        floor.transform.position = StageOrigin;
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        // 거리감용 기둥 (얇은 소품: 충돌 무시 대상)
        for (int i = 0; i < 6; i++)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            p.name = "PV_Pillar" + i;
            float a = i * 60f * Mathf.Deg2Rad;
            p.transform.position = StageOrigin + new Vector3(Mathf.Cos(a) * 11f, 2f, Mathf.Sin(a) * 11f);
            p.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
        }
        var lightGo = new GameObject("PV_Light");
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
    }

    static GameObject LoadAnimatorRoot(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return null;
        Animator a = prefab.GetComponentInChildren<Animator>(true);
        return a != null ? a.gameObject : null;
    }

    static GameObject FindBossAnimatorRoot()
    {
        BossController bc = Object.FindObjectOfType<BossController>(true);
        if (bc == null)
            return null;
        Animator a = bc.GetComponentInChildren<Animator>(true);
        return a != null ? a.gameObject : null;
    }

    static GameObject SpawnModel(GameObject source, string name)
    {
        if (source == null)
            return null;
        GameObject go = Object.Instantiate(source);
        go.name = name;
        go.SetActive(true);
        // 미리보기용: 스크립트 끄기 (에디터에서 아무것도 돌지 않게)
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) mb.enabled = false;
        return go;
    }

    static void SetOnly(GameObject player, GameObject victim, params GameObject[] all)
    {
        foreach (GameObject g in all)
            if (g != null) g.SetActive(g == victim);
        player.SetActive(true);
    }

    static Transform ResolveFocus(GameObject victim, bool boss)
    {
        Animator a = victim.GetComponent<Animator>();
        if (a != null && a.isHuman)
        {
            Transform chest = a.GetBoneTransform(HumanBodyBones.Chest);
            if (chest != null)
                return chest;
        }
        return null;
    }

    static void SamplePose(GameObject player, GameObject victim, ExecCamType type, float t)
    {
        string ps = type == ExecCamType.BossFront ? ExecutionDirector.PlayerExecFront
            : type == ExecCamType.BossBack ? ExecutionDirector.PlayerExecBack
            : type == ExecCamType.BossFinal ? ExecutionDirector.PlayerExecFinal
            : type == ExecCamType.Takedown ? ExecutionDirector.PlayerTakedown : ExecutionDirector.PlayerExecMob;
        string vs = type == ExecCamType.BossFront ? BossController.OverhaulStateExecutedFront
            : type == ExecCamType.BossBack ? BossController.OverhaulStateExecutedBack
            : type == ExecCamType.BossFinal ? BossController.OverhaulStateExecutedFinal
            : type == ExecCamType.Takedown ? ExecutionDirector.MobAmbushed : ExecutionDirector.MobExecuted;
        AnimationMode.BeginSampling();
        Sample(player, ps, t);
        Sample(victim, vs, t);
        AnimationMode.EndSampling();
    }

    static void Sample(GameObject go, string state, float t)
    {
        Animator a = go.GetComponent<Animator>();
        if (a == null)
            return;
        AnimationClip clip = FindStateClip(a.runtimeAnimatorController, state);
        if (clip == null)
            return;
        Vector3 p = go.transform.position;
        Quaternion r = go.transform.rotation;
        AnimationMode.SampleAnimationClip(go, clip, Mathf.Clamp(t, 0f, clip.length));
        go.transform.SetPositionAndRotation(p, r);   // 인플레이스 유지
    }

    static readonly Dictionary<string, AnimationClip> s_clipCache = new Dictionary<string, AnimationClip>();

    static AnimationClip FindStateClip(RuntimeAnimatorController rac, string state)
    {
        if (rac == null)
            return null;
        string key = rac.GetInstanceID() + "/" + state;
        if (s_clipCache.TryGetValue(key, out AnimationClip c) && c != null)
            return c;
        AnimatorController ac = rac as AnimatorController;
        if (ac == null && rac is AnimatorOverrideController oc)
            ac = oc.runtimeAnimatorController as AnimatorController;
        if (ac == null)
            return null;
        foreach (AnimatorControllerLayer layer in ac.layers)
        {
            c = FindIn(layer.stateMachine, state);
            if (c != null)
                break;
        }
        s_clipCache[key] = c;
        return c;
    }

    static AnimationClip FindIn(AnimatorStateMachine sm, string state)
    {
        foreach (ChildAnimatorState s in sm.states)
            if (s.state.name == state)
                return s.state.motion as AnimationClip ?? FirstClip(s.state.motion as BlendTree);
        foreach (ChildAnimatorStateMachine sub in sm.stateMachines)
        {
            AnimationClip c = FindIn(sub.stateMachine, state);
            if (c != null)
                return c;
        }
        return null;
    }

    static AnimationClip FirstClip(BlendTree bt)
    {
        if (bt == null)
            return null;
        foreach (ChildMotion m in bt.children)
            if (m.motion is AnimationClip ac)
                return ac;
        return null;
    }
}
