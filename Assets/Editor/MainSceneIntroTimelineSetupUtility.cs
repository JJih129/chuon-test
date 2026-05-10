using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public static class MainSceneIntroTimelineSetupUtility
{
    const string FolderPath = "Assets/Cinematics/MainScene";
    const string SignalsPath = FolderPath + "/Signals";
    const string TimelinePath = FolderPath + "/TL_MainScene_Intro.playable";
    const string ProfilePath = FolderPath + "/MainSceneIntroTimelineProfile.asset";

    static readonly string[] SignalNames =
    {
        "Sig_MainIntro_Begin",
        "Sig_MainIntro_DoorOpen",
        "Sig_MainIntro_PlayerWalkOut",
        "Sig_MainIntro_BossReveal",
        "Sig_MainIntro_CombatStart",
        "Sig_MainIntro_Line00",
        "Sig_MainIntro_Line01",
        "Sig_MainIntro_Line02",
        "Sig_MainIntro_Line03",
        "Sig_MainIntro_Line04",
        "Sig_MainIntro_Line05",
        "Sig_MainIntro_Line06",
        "Sig_MainIntro_Line07",
        "Sig_MainIntro_Line08",
        "Sig_MainIntro_Line09",
        "Sig_MainIntro_End"
    };

    [MenuItem("Tools/ChuOn/Main Scene/Rebuild Timeline Intro")]
    public static void Rebuild()
    {
        EnsureFolders();

        MainSceneArrivalController controller = Object.FindObjectOfType<MainSceneArrivalController>(true);
        if (controller == null)
        {
            GameSceneBinder binder = Object.FindObjectOfType<GameSceneBinder>(true);
            if (binder == null)
            {
                Debug.LogError("[MainSceneIntroTimelineSetup] GameSceneBinder not found.");
                return;
            }

            controller = binder.GetComponent<MainSceneArrivalController>();
            if (controller == null)
                controller = Undo.AddComponent<MainSceneArrivalController>(binder.gameObject);
        }

        PlayableDirector director = EnsureDirector(controller.transform);
        SignalReceiver receiver = director.GetComponent<SignalReceiver>();
        if (receiver == null)
            receiver = Undo.AddComponent<SignalReceiver>(director.gameObject);

        MainSceneIntroTimelineProfile profile = EnsureProfile();
        Dictionary<string, SignalAsset> signals = EnsureSignals();
        TimelineAsset timeline = RebuildTimeline(director, controller, signals, profile);
        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.None;
        director.timeUpdateMode = DirectorUpdateMode.GameTime;

        BindSignals(receiver, controller, signals);
        BindTimelineOutputs(director, timeline, receiver);
        controller.ConfigureBossIntroDirector(director);

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(receiver);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[MainSceneIntroTimelineSetup] Rebuilt TL_MainScene_Intro and bound SignalReceiver.");
    }

    static void BindTimelineOutputs(PlayableDirector director, TimelineAsset timeline, SignalReceiver receiver)
    {
        if (director == null || timeline == null || receiver == null)
            return;

        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (track is SignalTrack)
            {
                director.SetGenericBinding(track, receiver);
                continue;
            }

            if (track is CinemachineTrack)
            {
                CinemachineBrain brain = Object.FindObjectOfType<CinemachineBrain>(true);
                if (brain != null)
                    director.SetGenericBinding(track, brain);
            }
        }
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Cinematics"))
            AssetDatabase.CreateFolder("Assets", "Cinematics");
        if (!AssetDatabase.IsValidFolder(FolderPath))
            AssetDatabase.CreateFolder("Assets/Cinematics", "MainScene");
        if (!AssetDatabase.IsValidFolder(SignalsPath))
            AssetDatabase.CreateFolder(FolderPath, "Signals");
    }

    static PlayableDirector EnsureDirector(Transform parent)
    {
        Transform existing = parent.Find("MainSceneIntroDirector");
        GameObject go = existing != null ? existing.gameObject : new GameObject("MainSceneIntroDirector");
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(go, "Create MainSceneIntroDirector");
            go.transform.SetParent(parent, false);
        }

        PlayableDirector director = go.GetComponent<PlayableDirector>();
        return director != null ? director : Undo.AddComponent<PlayableDirector>(go);
    }

    static Dictionary<string, SignalAsset> EnsureSignals()
    {
        var result = new Dictionary<string, SignalAsset>(SignalNames.Length);
        for (int i = 0; i < SignalNames.Length; i++)
        {
            string name = SignalNames[i];
            string path = $"{SignalsPath}/{name}.asset";
            SignalAsset signal = AssetDatabase.LoadAssetAtPath<SignalAsset>(path);
            if (signal == null)
            {
                signal = ScriptableObject.CreateInstance<SignalAsset>();
                AssetDatabase.CreateAsset(signal, path);
            }

            result[name] = signal;
        }

        return result;
    }

    static MainSceneIntroTimelineProfile EnsureProfile()
    {
        MainSceneIntroTimelineProfile profile = AssetDatabase.LoadAssetAtPath<MainSceneIntroTimelineProfile>(ProfilePath);
        if (profile != null)
            return profile;

        profile = ScriptableObject.CreateInstance<MainSceneIntroTimelineProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        return profile;
    }

    static TimelineAsset RebuildTimeline(PlayableDirector director, MainSceneArrivalController controller, Dictionary<string, SignalAsset> signals, MainSceneIntroTimelineProfile profile)
    {
        if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) != null)
            AssetDatabase.DeleteAsset(TimelinePath);

        TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, TimelinePath);

        SignalTrack signalTrack = timeline.CreateTrack<SignalTrack>(null, "TRK_Signals_Intro");
        CinemachineTrack cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "TRK_Camera_Shots");
        timeline.CreateTrack<AnimationTrack>(null, "TRK_Player_Anim");
        timeline.CreateTrack<AnimationTrack>(null, "TRK_Boss_Anim");
        timeline.CreateTrack<ActivationTrack>(null, "TRK_VFX_Activation");
        timeline.CreateTrack<AudioTrack>(null, "TRK_Audio");

        CreateSignal(signalTrack, profile.beginTime, signals["Sig_MainIntro_Begin"]);
        CreateSignal(signalTrack, profile.doorOpenTime, signals["Sig_MainIntro_DoorOpen"]);
        CreateSignal(signalTrack, profile.playerWalkOutTime, signals["Sig_MainIntro_PlayerWalkOut"]);
        CreateSignal(signalTrack, profile.bossRevealTime, signals["Sig_MainIntro_BossReveal"]);

        CinemachineCamera elevatorCamera = EnsureCamera(director.transform, "VCam_MainIntro_ElevatorExit");
        CinemachineCamera bossCamera = EnsureCamera(director.transform, "VCam_MainIntro_BossReveal");
        ConfigureIntroCameras(elevatorCamera, bossCamera);

        CreateShotClip(cameraTrack, director, "SHOT_ElevatorExit", elevatorCamera, profile.beginTime, profile.elevatorShotDuration);
        CreateShotClip(cameraTrack, director, "SHOT_BossReveal", bossCamera, profile.elevatorShotDuration, profile.bossRevealShotDuration);

        double time = profile.firstDialogueTime;
        for (int i = 0; i < 10; i++)
        {
            CreateSignal(signalTrack, time, signals[$"Sig_MainIntro_Line{i:00}"]);
            time += profile.ResolveLineSpacing(i);
        }

        CreateSignal(signalTrack, time + profile.combatStartLead, signals["Sig_MainIntro_CombatStart"]);
        CreateSignal(signalTrack, time + profile.endLead, signals["Sig_MainIntro_End"]);
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = time + profile.timelineTailPadding;

        EditorUtility.SetDirty(timeline);
        return timeline;
    }

    static void CreateSignal(SignalTrack track, double time, SignalAsset signal)
    {
        SignalEmitter emitter = track.CreateMarker<SignalEmitter>(time);
        emitter.asset = signal;
        emitter.emitOnce = true;
    }

    static void CreateShotClip(CinemachineTrack track, PlayableDirector director, string displayName, CinemachineCamera camera, double start, double duration)
    {
        if (track == null || director == null || camera == null)
            return;

        TimelineClip clip = track.CreateClip<CinemachineShot>();
        clip.displayName = displayName;
        clip.start = start;
        clip.duration = duration;

        var shot = clip.asset as CinemachineShot;
        if (shot == null)
            return;

        PropertyName exposedName = new PropertyName($"MainIntro.{displayName}.{camera.name}");
        shot.DisplayName = displayName;
        shot.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = exposedName };
        director.SetReferenceValue(exposedName, camera);
    }

    static CinemachineCamera EnsureCamera(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, false);
        }

        CinemachineCamera camera = go.GetComponent<CinemachineCamera>();
        return camera != null ? camera : Undo.AddComponent<CinemachineCamera>(go);
    }

    static void ConfigureIntroCameras(CinemachineCamera elevatorCamera, CinemachineCamera bossCamera)
    {
        Transform player = FindSceneTransform("PlayerRoot");
        Transform boss = FindSceneTransform("boss") ?? FindSceneTransform("Boss");

        if (elevatorCamera != null)
        {
            Vector3 origin = player != null ? player.position : Vector3.zero;
            Vector3 forward = player != null ? player.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 position = origin + right * -4.8f + Vector3.up * 1.25f + forward * 12.5f;
            Vector3 lookAt = origin + Vector3.up * 1.25f + forward * 2.2f;
            elevatorCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            elevatorCamera.Priority = 0;
        }

        if (bossCamera != null)
        {
            Vector3 bossPosition = boss != null ? boss.position : new Vector3(0f, 0f, 8f);
            Vector3 playerPosition = player != null ? player.position : Vector3.zero;
            Vector3 fromBossToPlayer = playerPosition - bossPosition;
            fromBossToPlayer.y = 0f;
            if (fromBossToPlayer.sqrMagnitude <= 0.0001f)
                fromBossToPlayer = Vector3.back;
            fromBossToPlayer.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, fromBossToPlayer).normalized;
            Vector3 position = bossPosition + fromBossToPlayer * 3.4f + side * 1.45f + Vector3.up * 1.35f;
            Vector3 lookAt = bossPosition + Vector3.up * 1.25f;
            bossCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            bossCamera.Priority = 0;
        }
    }

    static Transform FindSceneTransform(string objectName)
    {
        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform transform = transforms[i];
            if (transform == null || transform.name != objectName)
                continue;

            GameObject gameObject = transform.gameObject;
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                return transform;
        }

        return null;
    }

    static void BindSignals(SignalReceiver receiver, MainSceneArrivalController controller, Dictionary<string, SignalAsset> signals)
    {
        RemoveExisting(receiver, signals.Values);

        AddReaction(receiver, signals["Sig_MainIntro_Begin"], controller.TimelineIntroBegin);
        AddReaction(receiver, signals["Sig_MainIntro_DoorOpen"], controller.TimelineElevatorDoorOpen);
        AddReaction(receiver, signals["Sig_MainIntro_PlayerWalkOut"], controller.TimelinePlayerWalkOut);
        AddReaction(receiver, signals["Sig_MainIntro_BossReveal"], controller.TimelineBossReveal);
        AddReaction(receiver, signals["Sig_MainIntro_CombatStart"], controller.TimelineCombatStart);
        AddReaction(receiver, signals["Sig_MainIntro_Line00"], controller.TimelineIntroLine00);
        AddReaction(receiver, signals["Sig_MainIntro_Line01"], controller.TimelineIntroLine01);
        AddReaction(receiver, signals["Sig_MainIntro_Line02"], controller.TimelineIntroLine02);
        AddReaction(receiver, signals["Sig_MainIntro_Line03"], controller.TimelineIntroLine03);
        AddReaction(receiver, signals["Sig_MainIntro_Line04"], controller.TimelineIntroLine04);
        AddReaction(receiver, signals["Sig_MainIntro_Line05"], controller.TimelineIntroLine05);
        AddReaction(receiver, signals["Sig_MainIntro_Line06"], controller.TimelineIntroLine06);
        AddReaction(receiver, signals["Sig_MainIntro_Line07"], controller.TimelineIntroLine07);
        AddReaction(receiver, signals["Sig_MainIntro_Line08"], controller.TimelineIntroLine08);
        AddReaction(receiver, signals["Sig_MainIntro_Line09"], controller.TimelineIntroLine09);
        AddReaction(receiver, signals["Sig_MainIntro_End"], controller.TimelineIntroEnd);
    }

    static void RemoveExisting(SignalReceiver receiver, IEnumerable<SignalAsset> signals)
    {
        foreach (SignalAsset signal in signals)
        {
            if (signal == null || receiver.GetReaction(signal) == null)
                continue;

            receiver.Remove(signal);
        }
    }

    static void AddReaction(SignalReceiver receiver, SignalAsset signal, UnityAction action)
    {
        var unityEvent = new UnityEvent();
        UnityEventTools.AddPersistentListener(unityEvent, action);
        receiver.AddReaction(signal, unityEvent);
    }
}
