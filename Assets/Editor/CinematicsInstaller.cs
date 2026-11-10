// 반복 14 시네마틱 설치기 (통합 담당자용, 메뉴로만 실행 — 자동 실행 없음, 씬 저장도 하지 않는다).
//   Tools/Chuon/Cinematics/Install Into Open Scene        : 열린 씬(Level_Facility 또는 Tutorial)에 앵커·리그·트리거 추가
//   Tools/Chuon/Cinematics/Remove From Open Scene         : 위에서 추가한 것 제거 (관측 브리지 콜라이더 복원)
//   Tools/Chuon/Cinematics/Upgrade Type-D Reveal (v2)     : MainSceneIntroDirector 에 v2 타임라인 + 샷 카메라 연결
//   Tools/Chuon/Cinematics/Revert Type-D Reveal           : 원래 TL_MainScene_Intro 로 되돌림
// 리그/타임라인은 Tools/Chuon/Cinematics/Rebuild Cinematic Assets 가 만든다. 앵커 위치는 추정값이므로 Docs/Cinematics_Shotlist.md 표대로 확인할 것.
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public static class CinematicsInstaller
{
    const string RootName = "CINEMATICS_iter14";
    const string PrefabDir = CinematicsAssetBuilder.PrefabDir;

    [MenuItem("Tools/Chuon/Cinematics/Install Into Open Scene")]
    public static void InstallMenu() => Debug.Log(Install());

    [MenuItem("Tools/Chuon/Cinematics/Remove From Open Scene")]
    public static void RemoveMenu() => Debug.Log(Remove());

    [MenuItem("Tools/Chuon/Cinematics/Upgrade Type-D Reveal (v2)")]
    public static void UpgradeTypeDMenu() => Debug.Log(UpgradeTypeDReveal());

    [MenuItem("Tools/Chuon/Cinematics/Revert Type-D Reveal")]
    public static void RevertTypeDMenu() => Debug.Log(RevertTypeDReveal());

    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "[CinematicsInstaller] refused: play mode";
        Scene scene = SceneManager.GetActiveScene();
        var log = new StringBuilder($"[CinematicsInstaller] {scene.name}: ");
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Install cinematics");
        }

        if (scene.name.Contains("Facility"))
            InstallFacility(root.transform, log);
        else if (scene.name == "Tutorial")
            InstallTutorial(root.transform, log);
        else
            log.Append("no cinematic set for this scene; ");

        EditorSceneManager.MarkSceneDirty(scene);
        log.Append("(scene marked dirty, not saved)");
        return log.ToString();
    }

    // ------------------------------------------------------------------ Level_Facility

    static void InstallFacility(Transform root, StringBuilder log)
    {
        Transform anchors = Group(root, "Anchors");

        // 무대 프리팹은 레벨 밖 (상층 Y+40, 하층 Y 0 과 겹치지 않는 곳)
        Anchor(anchors, "CineAnchor_OpeningStage", new Vector3(0f, -400f, 0f), Quaternion.identity, log);
        Anchor(anchors, "CineAnchor_ElevatorStage", new Vector3(80f, -400f, 0f), Quaternion.identity, log);

        // 프론트 데스크: 이름으로 추정 (상층 Y > 20). forward = 데스크에서 로비 입구(플레이어 시작) 쪽.
        // 피벗이 원점에 있는 그룹(FrontDesk_Reception 등)도 잡도록 하위 렌더러 바닥 중심으로 판정한다 (반복 14C).
        Transform desk = FindByTokens(new[] { "FrontDesk_Reception", "FrontDesk", "Front_Desk", "Desk" }, t => DeskBase(t).y > 20f && t.name.IndexOf("Lift", System.StringComparison.OrdinalIgnoreCase) < 0 && t.name.IndexOf("Holo", System.StringComparison.OrdinalIgnoreCase) < 0);
        Transform player = GameObject.FindWithTag("Player") != null ? GameObject.FindWithTag("Player").transform : null;
        Vector3 deskPos = desk != null ? DeskBase(desk) : (LobbyManager.Instance != null ? LobbyManager.Instance.transform.position : new Vector3(0f, 40f, 0f));
        if (desk == null)
        {
            LobbyManager lm = Object.FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);
            if (lm != null) deskPos = lm.transform.position;
            log.Append("WARN desk not found (anchor at LobbyManager; move CineAnchor_LobbyFrontDesk onto the front desk); ");
        }
        // 바닥 높이: 데스크 피벗에서 아래로 6 m 안의 가장 낮은 충돌면 (해치가 바닥에 붙게)
        float floorY = deskPos.y;
        RaycastHit[] hits = Physics.RaycastAll(deskPos + Vector3.up * 0.2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
            floorY = Mathf.Min(floorY, hits[i].point.y);
        deskPos.y = floorY;
        Quaternion deskRot = FacingToward(deskPos, player != null ? player.position : deskPos + Vector3.back);
        Anchor(anchors, "CineAnchor_LobbyFrontDesk", new Vector3(deskPos.x, Mathf.Max(deskPos.y, 0f), deskPos.z), deskRot, log);

        // 관측 브리지 유리 = 기존 LevelFacilityObservationBeat 위치
        LevelFacilityObservationBeat beat = Object.FindFirstObjectByType<LevelFacilityObservationBeat>(FindObjectsInactive.Include);
        BossController boss = Object.FindFirstObjectByType<BossController>(FindObjectsInactive.Include);
        if (beat != null)
        {
            Vector3 p = beat.transform.position;
            Anchor(anchors, "CineAnchor_SkydeckGlass", p, FacingToward(p, boss != null ? boss.transform.position : p + Vector3.forward), log);
        }
        else
        {
            log.Append("WARN ObservationBeat not found (place CineAnchor_SkydeckGlass + trigger by hand); ");
        }

        Transform rigs = Group(root, "Rigs");
        Rig(rigs, "CINE_Opening", "CineAnchor_OpeningStage", log);
        Rig(rigs, "CINE_ElevatorDescent", "CineAnchor_ElevatorStage", log);
        Rig(rigs, "CINE_LobbyAlarm", null, log);
        Rig(rigs, "CINE_SkydeckLookDown", null, log);
        Rig(rigs, "CINE_EndingTsukasa", null, log);

        // 관측 브리지: 기존 비트의 콜라이더를 복제한 트리거 + 기존 콜라이더 비활성 (Remove 로 복원)
        if (beat != null && root.Find("Triggers/CineTrigger_Skydeck") == null)
        {
            Transform triggers = Group(root, "Triggers");
            var go = new GameObject("CineTrigger_Skydeck");
            Undo.RegisterCreatedObjectUndo(go, "Skydeck trigger");
            go.transform.SetParent(triggers, false);
            go.transform.SetPositionAndRotation(beat.transform.position, beat.transform.rotation);
            go.transform.localScale = beat.transform.lossyScale;
            Collider src = beat.GetComponent<Collider>();
            if (src is BoxCollider b)
            {
                var c = go.AddComponent<BoxCollider>(); c.center = b.center; c.size = b.size; c.isTrigger = true;
            }
            else
            {
                var c = go.AddComponent<BoxCollider>(); c.size = new Vector3(6f, 3f, 6f); c.isTrigger = true;
                log.Append("WARN skydeck collider guessed 6x3x6; ");
            }
            var trig = go.AddComponent<CinematicTrigger>();
            trig.Configure(CinematicsAssetBuilder.IdSkydeck, CinematicTrigger.Mode.Volume);
            if (src != null)
            {
                Undo.RecordObject(src, "Disable observation beat collider");
                src.enabled = false;
            }
            log.Append("skydeck trigger (old ObservationBeat collider disabled); ");
        }

        // 페이즈 전환 과열 비트 (카메라·조작 점유 없음)
        if (boss != null && boss.GetComponent<CinePhaseOverheatBeat>() == null)
        {
            var overheat = Undo.AddComponent<CinePhaseOverheatBeat>(boss.gameObject);
            var so = new SerializedObject(overheat);
            so.FindProperty("boss").objectReferenceValue = boss;
            so.FindProperty("impactClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(CinematicsAssetBuilder.AudioDir + "/SFX_Cine_Steam.wav");
            so.FindProperty("steamMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(CinematicsAssetBuilder.MatDir + "/M_CINE_Steam.mat");
            so.FindProperty("emberMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(CinematicsAssetBuilder.MatDir + "/M_CINE_Embers.mat");
            so.ApplyModifiedPropertiesWithoutUndo();
            log.Append("phase overheat beat on boss; ");
        }
    }

    // ------------------------------------------------------------------ Tutorial

    static void InstallTutorial(Transform root, StringBuilder log)
    {
        Transform anchors = Group(root, "Anchors");
        InfiltrationTutorialDirector dir = Object.FindFirstObjectByType<InfiltrationTutorialDirector>(FindObjectsInactive.Include);
        Transform start = null, exit = null, zone = null;
        if (dir != null)
        {
            var so = new SerializedObject(dir);
            start = so.FindProperty("playerStart")?.objectReferenceValue as Transform;
            var exitObj = so.FindProperty("exitInteractable")?.objectReferenceValue as Component;
            exit = exitObj != null ? exitObj.transform : null;
            var zoneObj = so.FindProperty("zoneExit")?.objectReferenceValue as Component;
            zone = zoneObj != null ? zoneObj.transform : null;
        }
        if (start != null)
            Anchor(anchors, "CineAnchor_TutStart", start.position, FacingYaw(start.forward), log);
        else
            log.Append("WARN playerStart not found; ");
        if (exit != null)
        {
            // 14C: forward = 출구 구역(플레이어가 서는 곳) → 문 패널 (시작점 기준이면 실제 접근 방향과 45° 어긋남)
            Vector3 from = zone != null ? zone.position : start != null ? start.position : exit.position + Vector3.back * 10f;
            Anchor(anchors, "CineAnchor_TutExitDoor", exit.position, FacingToward(from, exit.position), log);
        }
        else
        {
            log.Append("WARN exit panel not found; ");
        }

        Transform rigs = Group(root, "Rigs");
        Rig(rigs, "CINE_TutVrBoot", null, log);
        Rig(rigs, "CINE_TutVrExit", null, log);
    }

    // ------------------------------------------------------------------ remove

    public static string Remove()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "[CinematicsInstaller] refused: play mode";
        GameObject root = GameObject.Find(RootName);
        LevelFacilityObservationBeat beat = Object.FindFirstObjectByType<LevelFacilityObservationBeat>(FindObjectsInactive.Include);
        if (beat != null && beat.GetComponent<Collider>() is Collider c && !c.enabled)
        {
            Undo.RecordObject(c, "Restore observation beat collider");
            c.enabled = true;
        }
        BossController boss = Object.FindFirstObjectByType<BossController>(FindObjectsInactive.Include);
        if (boss != null && boss.GetComponent<CinePhaseOverheatBeat>() is CinePhaseOverheatBeat ob)
            Undo.DestroyObjectImmediate(ob);
        if (root != null)
            Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return "[CinematicsInstaller] removed (scene marked dirty, not saved)";
    }

    // ------------------------------------------------------------------ Type-D reveal v2

    static PlayableDirector FindIntroDirector()
    {
        foreach (PlayableDirector d in Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.name == "MainSceneIntroDirector")
                return d;
        return null;
    }

    public static string UpgradeTypeDReveal()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "[CinematicsInstaller] refused: play mode";
        PlayableDirector d = FindIntroDirector();
        var v2 = AssetDatabase.LoadAssetAtPath<TimelineAsset>(CinematicsAssetBuilder.TypeDV2Path);
        var shotsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CINE_TypeDReveal_v2_Shots.prefab");
        if (d == null || v2 == null || shotsPrefab == null)
            return $"[CinematicsInstaller] Type-D v2: director={(d != null)} timeline={(v2 != null)} shots={(shotsPrefab != null)} — run Rebuild Cinematic Assets / open Level_Facility";

        Undo.RecordObject(d, "Upgrade Type-D reveal");
        Transform shots = d.transform.Find(shotsPrefab.name);
        if (shots == null)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(shotsPrefab, d.transform);
            Undo.RegisterCreatedObjectUndo(inst, "Type-D v2 shots");
            shots = inst.transform;
        }
        foreach (string n in CinematicsAssetBuilder.TypeDV2NewShots)
        {
            Transform cam = shots.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "TDRv2_" + n);
            if (cam != null)
                d.SetReferenceValue(new PropertyName("TDRv2." + n), cam.GetComponent<CinemachineCamera>());
        }
        d.playableAsset = v2;
        Rebind(d, v2);
        EditorUtility.SetDirty(d);
        EditorSceneManager.MarkSceneDirty(d.gameObject.scene);
        return "[CinematicsInstaller] Type-D reveal v2 installed on MainSceneIntroDirector (scene marked dirty, not saved)";
    }

    public static string RevertTypeDReveal()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "[CinematicsInstaller] refused: play mode";
        PlayableDirector d = FindIntroDirector();
        var original = AssetDatabase.LoadAssetAtPath<TimelineAsset>(CinematicsAssetBuilder.TypeDOriginal);
        if (d == null || original == null)
            return "[CinematicsInstaller] revert: director or original timeline missing";
        Undo.RecordObject(d, "Revert Type-D reveal");
        d.playableAsset = original;
        Rebind(d, original);
        Transform shots = d.transform.Find("CINE_TypeDReveal_v2_Shots");
        if (shots != null)
            Undo.DestroyObjectImmediate(shots.gameObject);
        EditorUtility.SetDirty(d);
        EditorSceneManager.MarkSceneDirty(d.gameObject.scene);
        return "[CinematicsInstaller] Type-D reveal reverted to TL_MainScene_Intro";
    }

    static void Rebind(PlayableDirector d, TimelineAsset tl)
    {
        SignalReceiver receiver = d.GetComponent<SignalReceiver>();
        CinemachineBrain brain = Object.FindFirstObjectByType<CinemachineBrain>(FindObjectsInactive.Include);
        foreach (TrackAsset t in tl.GetOutputTracks())
        {
            if (t is SignalTrack && receiver != null) d.SetGenericBinding(t, receiver);
            else if (t is CinemachineTrack && brain != null) d.SetGenericBinding(t, brain);
        }
    }

    // ------------------------------------------------------------------ helpers

    static Transform Group(Transform root, string name)
    {
        Transform t = root.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Cinematics group");
        go.transform.SetParent(root, false);
        return go.transform;
    }

    static void Anchor(Transform parent, string name, Vector3 pos, Quaternion rot, StringBuilder log)
    {
        if (CinematicAnchorExists(name))
        {
            log.Append($"{name} kept; ");
            return;
        }
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Cinematic anchor");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.AddComponent<CinematicAnchor>().Configure(name);
        log.Append($"{name} @({pos.x:0.#},{pos.y:0.#},{pos.z:0.#}) yaw {rot.eulerAngles.y:0}; ");
    }

    static bool CinematicAnchorExists(string name)
    {
        foreach (CinematicAnchor a in Object.FindObjectsByType<CinematicAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (a.AnchorName == name) return true;
        return false;
    }

    static void Rig(Transform parent, string prefabName, string anchor, StringBuilder log)
    {
        if (parent.Find(prefabName) != null)
        {
            log.Append($"{prefabName} kept; ");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{prefabName}.prefab");
        if (prefab == null)
        {
            log.Append($"MISSING {prefabName} (run Rebuild Cinematic Assets); ");
            return;
        }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(inst, "Cinematic rig");
        if (!string.IsNullOrEmpty(anchor))
        {
            Transform a = CinematicAnchorTransform(anchor);
            if (a != null) inst.transform.SetPositionAndRotation(a.position, a.rotation);
        }
        log.Append($"{prefabName}; ");
    }

    static Transform CinematicAnchorTransform(string name)
    {
        foreach (CinematicAnchor a in Object.FindObjectsByType<CinematicAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (a.AnchorName == name) return a.transform;
        return null;
    }

    static Transform FindByTokens(string[] tokens, System.Func<Transform, bool> filter)
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (string token in tokens)
            foreach (Transform t in all)
                if (t.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0 && filter(t))
                    return t;
        return null;
    }

    // 데스크 기준점: 가장 낮은 하위 렌더러에서 1.5 m 안에 시작하는 렌더러 묶음(로고·기둥·홀로 제외)의 바닥 중심. 렌더러가 없으면 피벗.
    static Vector3 DeskBase(Transform t)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        float minY = float.PositiveInfinity;
        foreach (Renderer r in renderers)
            minY = Mathf.Min(minY, r.bounds.min.y);
        bool init = false;
        Bounds b = default;
        foreach (Renderer r in renderers)
        {
            if (r.bounds.min.y > minY + 1.5f || r.bounds.size.y > 3f || r.name.IndexOf("Holo", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (!init) { b = r.bounds; init = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!init)
            return t.position;
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    static Quaternion FacingToward(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.y = 0f;
        return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
    }

    static Quaternion FacingYaw(Vector3 forward)
    {
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
    }
}
