using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public static class UltimateCinematicSetupUtility
{
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string SequenceDataPath = "Assets/Resources/Ultimate/UltimateSequence_Default.asset";
    const string CinematicsFolderPath = "Assets/Cinematics/Ultimate";
    const string SignalsFolderPath = "Assets/Cinematics/Ultimate/Signals";
    const string TimelineAssetPath = CinematicsFolderPath + "/TL_Ultimate_PlayerSword.playable";

    static readonly string[] SignalNames =
    {
        "Sig_CameraSessionBegin",
        "Sig_DrawPoseStart",
        "Sig_CloseUpStart",
        "Sig_DrawSlashRelease",
        "Sig_SlashStormStart",
        "Sig_SlashStormSustainStart",
        "Sig_PlayerHideForStorm",
        "Sig_PlayerShowForWalkout",
        "Sig_WalkoutStart",
        "Sig_GameplayCommitDamage",
        "Sig_ExplosionPrepare",
        "Sig_FinalExplosion",
        "Sig_CameraSessionEnd",
        "Sig_GameplayRestore",
    };

    [MenuItem("Tools/Cinematics/Setup Player Ultimate Cinematic")]
    static void SetupFromMenu()
    {
        RunInternal(showDialog: true);
    }

    public static SetupSummary RunFromFastMcp()
    {
        return RunInternal(showDialog: false);
    }

    static SetupSummary RunInternal(bool showDialog)
    {
        var activeScene = SceneManager.GetActiveScene();
        string activeScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
        var report = new SetupReport();

        try
        {
            EnsureFolder(CinematicsFolderPath);
            EnsureFolder(SignalsFolderPath);

            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ConfigureMainScene(scene, report);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(activeScenePath) && activeScenePath != MainScenePath)
                EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
        }

        report.Emit(showDialog);
        return report.ToSummary();
    }

    static void ConfigureMainScene(Scene scene, SetupReport report)
    {
        GameObject player = FindPlayerObject(scene, report);
        if (player == null)
        {
            report.Warn("MainScene Player(tag=Player) not found.");
            return;
        }

        GameObject boss = FindBossObject(scene);
        if (boss == null)
        {
            report.Warn("MainScene boss object not found.");
            return;
        }

        UltimateSequenceData sequenceData = AssetDatabase.LoadAssetAtPath<UltimateSequenceData>(SequenceDataPath);
        if (sequenceData == null)
            report.Warn($"Sequence data not found: {SequenceDataPath}");

        Animator playerAnimator = player.GetComponent<Animator>();
        if (playerAnimator == null)
            report.Warn("Player root Animator missing.");

        PlayerReferences playerReferences = player.GetComponent<PlayerReferences>();
        if (playerReferences == null)
            report.Warn("PlayerReferences missing on Player.");

        Transform visualRoot = playerReferences != null ? playerReferences.VisualRoot : player.transform.Find("VisualRoot");
        Transform playerVisual = ResolvePlayerVisualRoot(visualRoot);
        Renderer[] playerRenderers = playerVisual != null ? playerVisual.GetComponentsInChildren<Renderer>(true) : null;

        var skillController = EnsureComponent<UltimateSkillController>(player);
        var playerUltimate = EnsureComponent<PlayerUltimateController>(player);
        var targetBinder = EnsureComponent<UltimateTargetBinder>(player);
        var hitProcessor = EnsureComponent<UltimateHitProcessor>(player);
        var vfxPresenter = EnsureComponent<UltimateVFXPresenter>(player);
        var bindings = EnsureComponent<UltimateCinematicBindings>(player);

        Transform playerCineRoot = EnsureChild(player.transform, "Ult_CineRoot");
        Transform shot01Pos = EnsureAnchor(playerCineRoot, "Ult_Shot01_Pos", new Vector3(0.62f, 1.44f, -1.48f), new Vector3(12f, 158f, 0f));
        Transform shot01LookAt = EnsureAnchor(playerCineRoot, "Ult_Shot01_LookAt", new Vector3(0.1f, 1.18f, 0.36f), Vector3.zero);
        Transform shot02Pos = EnsureAnchor(playerCineRoot, "Ult_Shot02_Pos", new Vector3(0.24f, 1.46f, -0.58f), new Vector3(7f, 165f, 0f));
        Transform shot02LookAt = EnsureAnchor(playerCineRoot, "Ult_Shot02_LookAt", new Vector3(0.12f, 1.22f, 0.42f), Vector3.zero);
        Transform shot03Pos = EnsureAnchor(playerCineRoot, "Ult_Shot03_Pos", new Vector3(0f, 1.86f, -3.8f), new Vector3(8f, 180f, 0f));
        Transform shot03LookAt = EnsureAnchor(playerCineRoot, "Ult_Shot03_LookAt", new Vector3(0f, 1.18f, 1.75f), Vector3.zero);
        Transform shot04Pos = EnsureAnchor(playerCineRoot, "Ult_Shot04_Pos", new Vector3(0f, 1.34f, 2.28f), new Vector3(4f, 0f, 0f));
        Transform shot04LookAt = EnsureAnchor(playerCineRoot, "Ult_Shot04_LookAt", new Vector3(0f, 1.18f, 0.14f), Vector3.zero);
        Transform shot05Pos = EnsureAnchor(playerCineRoot, "Ult_Shot05_Pos", new Vector3(0.18f, 1.5f, 1.62f), new Vector3(6f, 4f, 0f));
        Transform shot05LookAt = EnsureAnchor(playerCineRoot, "Ult_Shot05_LookAt", new Vector3(0f, 1.2f, 0.18f), Vector3.zero);
        Transform slashStormCenter = EnsureAnchor(playerCineRoot, "Ult_SlashStorm_Center", new Vector3(0f, 1.08f, 1.65f), Vector3.zero);
        Transform walkoutFacingAnchor = EnsureAnchor(playerCineRoot, "Ult_Walkout_FacingAnchor", new Vector3(0f, 0f, 3.5f), Vector3.zero);
        GameObject playerGhostHelper = EnsureChild(playerCineRoot, "Ult_GhostHelper").gameObject;
        playerGhostHelper.SetActive(false);

        Transform swordTransform = playerVisual != null ? FindDescendant(playerVisual, "Object002") : null;
        Transform swordCloseAnchor = swordTransform != null
            ? EnsureAnchor(swordTransform, "Ult_Sword_CloseAnchor", new Vector3(0.04f, 0.05f, 0.18f), Vector3.zero)
            : EnsureAnchor(playerCineRoot, "Ult_Sword_CloseAnchor", new Vector3(0.24f, 1.15f, 0.52f), Vector3.zero);

        Transform targetCenter = EnsureAnchor(boss.transform, "Ult_TargetCenter", new Vector3(0f, 1.16f, 0f), Vector3.zero);
        Transform targetExplosionAnchor = EnsureAnchor(boss.transform, "Ult_ExplosionAnchor", new Vector3(0f, 1.02f, 0f), Vector3.zero);
        Transform targetCineHoldAnchor = EnsureAnchor(boss.transform, "Ult_CineHoldAnchor", Vector3.zero, Vector3.zero);

        GameObject cinematicRoot = EnsureRootObject(scene, "UltimateCinematicRoot");
        var cameraSession = EnsureComponent<UltimateCameraSessionController>(cinematicRoot);
        var enemyCinematicState = EnsureComponent<UltimateEnemyCinematicState>(cinematicRoot);

        Transform playableDirectorRoot = EnsureChild(cinematicRoot.transform, "UltimatePlayableDirector");
        var playableDirector = EnsureComponent<PlayableDirector>(playableDirectorRoot.gameObject);
        playableDirector.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        playableDirector.extrapolationMode = DirectorWrapMode.None;
        var signalReceiver = EnsureComponent<SignalReceiver>(playableDirectorRoot.gameObject);
        var cinematicController = EnsureComponent<UltimateCinematicController>(playableDirectorRoot.gameObject);

        Transform cameraRigRoot = EnsureChild(cinematicRoot.transform, "UltimateCameraRig");
        GameObject cameraRig = cameraRigRoot.gameObject;
        cameraRig.SetActive(false);

        var introCam = EnsureUltimateCamera(cameraRigRoot, "VCam_Ult_Intro", shot01Pos, shot01LookAt, 36f);
        var closeCam = EnsureUltimateCamera(cameraRigRoot, "VCam_Ult_CloseUp", shot02Pos, shot02LookAt, 28f);
        var stormCam = EnsureUltimateCamera(cameraRigRoot, "VCam_Ult_SlashStorm", shot03Pos, shot03LookAt, 42f);
        var walkoutCam = EnsureUltimateCamera(cameraRigRoot, "VCam_Ult_Walkout", shot04Pos, shot04LookAt, 30f);
        var explosionCam = EnsureUltimateCamera(cameraRigRoot, "VCam_Ult_Explosion", shot05Pos, shot05LookAt, 26f);

        Transform vfxRoot = EnsureChild(cinematicRoot.transform, "UltimateVfxRoot");
        var slashStormVfx = EnsureComponent<SlashStormVfxController>(vfxRoot.gameObject);
        var explosionVfx = EnsureComponent<ExplosionVfxController>(vfxRoot.gameObject);

        Dictionary<string, SignalAsset> signals = EnsureSignalAssets();
        TimelineAsset timelineAsset = RebuildTimelineAsset(sequenceData, playableDirector, introCam, closeCam, stormCam, walkoutCam, explosionCam, signals, report);

        if (timelineAsset != null)
            playableDirector.playableAsset = timelineAsset;

        ApplyBindings(bindings, player, playerAnimator, playerVisual, swordCloseAnchor, walkoutFacingAnchor, slashStormCenter, boss.transform, targetCenter, targetExplosionAnchor, targetCineHoldAnchor,
            shot01Pos, shot01LookAt, shot02Pos, shot02LookAt, shot03Pos, shot03LookAt, shot04Pos, shot04LookAt, shot05Pos, shot05LookAt);

        ApplyCameraSession(cameraSession, cameraRig, introCam, closeCam, stormCam, walkoutCam, explosionCam);
        ApplyEnemyState(enemyCinematicState, boss);
        ApplyVfxControllers(slashStormVfx, explosionVfx, vfxPresenter);
        ApplyCinematicController(cinematicController, playableDirector, signalReceiver, bindings, targetBinder, hitProcessor, vfxPresenter, cameraSession, slashStormVfx, explosionVfx, enemyCinematicState, playerVisual, playerRenderers, playerGhostHelper, signals);
        ApplyUltimateControllers(skillController, playerUltimate, sequenceData, cinematicController, targetBinder, hitProcessor, vfxPresenter, playableDirector);

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(bindings);
        EditorUtility.SetDirty(cameraSession);
        EditorUtility.SetDirty(enemyCinematicState);
        EditorUtility.SetDirty(cinematicController);
        EditorUtility.SetDirty(skillController);
        EditorUtility.SetDirty(playerUltimate);
        EditorUtility.SetDirty(slashStormVfx);
        EditorUtility.SetDirty(explosionVfx);
        EditorUtility.SetDirty(playableDirector);
        EditorUtility.SetDirty(signalReceiver);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.Info("Configured MainScene ultimate cinematic root, timeline, cameras, and bindings.");
    }

    static void ApplyBindings(
        UltimateCinematicBindings bindings,
        GameObject player,
        Animator playerAnimator,
        Transform playerVisual,
        Transform swordCloseAnchor,
        Transform walkoutFacingAnchor,
        Transform slashStormCenter,
        Transform targetRoot,
        Transform targetCenter,
        Transform targetExplosionAnchor,
        Transform targetCineHoldAnchor,
        Transform shot01Pos,
        Transform shot01LookAt,
        Transform shot02Pos,
        Transform shot02LookAt,
        Transform shot03Pos,
        Transform shot03LookAt,
        Transform shot04Pos,
        Transform shot04LookAt,
        Transform shot05Pos,
        Transform shot05LookAt)
    {
        SerializedObject bindingsSo = new SerializedObject(bindings);
        SetObject(bindingsSo, "playerRoot", player.transform);
        SetObject(bindingsSo, "playerAnimator", playerAnimator);
        SetObject(bindingsSo, "playerVisualRoot", playerVisual);
        SetObject(bindingsSo, "swordCloseAnchor", swordCloseAnchor);
        SetObject(bindingsSo, "walkoutFacingAnchor", walkoutFacingAnchor);
        SetObject(bindingsSo, "slashStormCenter", slashStormCenter);
        SetObject(bindingsSo, "targetRoot", targetRoot);
        SetObject(bindingsSo, "targetCenter", targetCenter);
        SetObject(bindingsSo, "targetExplosionAnchor", targetExplosionAnchor);
        SetObject(bindingsSo, "targetCineHoldAnchor", targetCineHoldAnchor);
        SetObject(bindingsSo, "shot01Pos", shot01Pos);
        SetObject(bindingsSo, "shot01LookAt", shot01LookAt);
        SetObject(bindingsSo, "shot02Pos", shot02Pos);
        SetObject(bindingsSo, "shot02LookAt", shot02LookAt);
        SetObject(bindingsSo, "shot03Pos", shot03Pos);
        SetObject(bindingsSo, "shot03LookAt", shot03LookAt);
        SetObject(bindingsSo, "shot04Pos", shot04Pos);
        SetObject(bindingsSo, "shot04LookAt", shot04LookAt);
        SetObject(bindingsSo, "shot05Pos", shot05Pos);
        SetObject(bindingsSo, "shot05LookAt", shot05LookAt);
        bindingsSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ApplyCameraSession(UltimateCameraSessionController cameraSession, GameObject cameraRig, params Object[] cameras)
    {
        SerializedObject sessionSo = new SerializedObject(cameraSession);
        SetObject(sessionSo, "cameraRigRoot", cameraRig);
        SetObject(sessionSo, "brain", Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : Object.FindFirstObjectByType<CinemachineBrain>());
        SetObjectArray(sessionSo, "sequenceCameras", cameras);
        SetBool(sessionSo, "debugLog", true);
        sessionSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ApplyEnemyState(UltimateEnemyCinematicState enemyCinematicState, GameObject boss)
    {
        SerializedObject enemySo = new SerializedObject(enemyCinematicState);
        SetObject(enemySo, "bossController", boss.GetComponent<BossController>());
        SetBool(enemySo, "debugLog", true);
        enemySo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ApplyVfxControllers(SlashStormVfxController slashStormVfx, ExplosionVfxController explosionVfx, UltimateVFXPresenter vfxPresenter)
    {
        SerializedObject slashSo = new SerializedObject(slashStormVfx);
        SetObject(slashSo, "vfxPresenter", vfxPresenter);
        SetBool(slashSo, "debugLog", true);
        slashSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject explosionSo = new SerializedObject(explosionVfx);
        SetObject(explosionSo, "vfxPresenter", vfxPresenter);
        explosionSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ApplyCinematicController(
        UltimateCinematicController cinematicController,
        PlayableDirector playableDirector,
        SignalReceiver signalReceiver,
        UltimateCinematicBindings bindings,
        UltimateTargetBinder targetBinder,
        UltimateHitProcessor hitProcessor,
        UltimateVFXPresenter vfxPresenter,
        UltimateCameraSessionController cameraSession,
        SlashStormVfxController slashStormVfx,
        ExplosionVfxController explosionVfx,
        UltimateEnemyCinematicState enemyCinematicState,
        Transform playerVisual,
        Renderer[] playerRenderers,
        GameObject playerGhostHelper,
        Dictionary<string, SignalAsset> signals)
    {
        SerializedObject controllerSo = new SerializedObject(cinematicController);
        SetObject(controllerSo, "playableDirector", playableDirector);
        SetObject(controllerSo, "signalReceiver", signalReceiver);
        SetObject(controllerSo, "bindings", bindings);
        SetObject(controllerSo, "targetBinder", targetBinder);
        SetObject(controllerSo, "hitProcessor", hitProcessor);
        SetObject(controllerSo, "vfxPresenter", vfxPresenter);
        SetObject(controllerSo, "cameraSession", cameraSession);
        SetObject(controllerSo, "slashStormVfx", slashStormVfx);
        SetObject(controllerSo, "explosionVfx", explosionVfx);
        SetObject(controllerSo, "enemyCinematicState", enemyCinematicState);
        SetObject(controllerSo, "playerVisualRoot", playerVisual != null ? playerVisual.gameObject : null);
        SetObjectArray(controllerSo, "playerRenderers", playerRenderers);
        SetObject(controllerSo, "playerGhostHelper", playerGhostHelper);
        foreach (var signalPair in signals)
            SetObject(controllerSo, ToFieldName(signalPair.Key), signalPair.Value);
        SetBool(controllerSo, "debugLog", true);
        controllerSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ApplyUltimateControllers(
        UltimateSkillController skillController,
        PlayerUltimateController playerUltimate,
        UltimateSequenceData sequenceData,
        UltimateCinematicController cinematicController,
        UltimateTargetBinder targetBinder,
        UltimateHitProcessor hitProcessor,
        UltimateVFXPresenter vfxPresenter,
        PlayableDirector playableDirector)
    {
        SerializedObject skillSo = new SerializedObject(skillController);
        SetObject(skillSo, "sequenceData", sequenceData);
        SetObject(skillSo, "cinematicController", cinematicController);
        SetObject(skillSo, "targetBinder", targetBinder);
        SetObject(skillSo, "hitProcessor", hitProcessor);
        SetObject(skillSo, "vfxPresenter", vfxPresenter);
        SetObject(skillSo, "director", playableDirector);
        SetBool(skillSo, "useCodeDrivenSequence", true);
        SetBool(skillSo, "allowLegacyDirectorFallback", false);
        SetBool(skillSo, "debugLog", true);
        skillSo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject playerUltimateSo = new SerializedObject(playerUltimate);
        SetObject(playerUltimateSo, "director", playableDirector);
        SetBool(playerUltimateSo, "useScriptedSequence", false);
        SetBool(playerUltimateSo, "allowDirectorFallbackWhenScriptedUnavailable", false);
        playerUltimateSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static TimelineAsset RebuildTimelineAsset(
        UltimateSequenceData sequenceData,
        PlayableDirector director,
        CinemachineCamera introCam,
        CinemachineCamera closeCam,
        CinemachineCamera stormCam,
        CinemachineCamera walkoutCam,
        CinemachineCamera explosionCam,
        Dictionary<string, SignalAsset> signals,
        SetupReport report)
    {
        if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineAssetPath) != null)
            AssetDatabase.DeleteAsset(TimelineAssetPath);

        var timelineAsset = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timelineAsset, TimelineAssetPath);

        var animTrack = timelineAsset.CreateTrack<AnimationTrack>(null, "TRK_Anim_Player");
        var camTrack = timelineAsset.CreateTrack<CinemachineTrack>(null, "TRK_Cam_Shots");
        var signalTrack = timelineAsset.CreateTrack<SignalTrack>(null, "TRK_Signal_Ult");

        double introPoseDuration = sequenceData != null ? Mathf.Max(0.55f, sequenceData.Timings.introPoseDuration) : 0.8f;
        double closeUpDuration = 0.36f;
        double drawReleaseDuration = sequenceData != null ? Mathf.Max(0.12f, sequenceData.Timings.dashDuration) : 0.18f;
        double slashStormDuration = sequenceData != null
            ? Mathf.Max(0.9f, sequenceData.SlashCount * sequenceData.Timings.defaultMultiSlashInterval + 0.45f)
            : 1.25f;
        double walkoutDuration = sequenceData != null ? Mathf.Max(0.85f, sequenceData.Timings.walkoutDuration) : 0.95f;
        double explosionHoldDuration = sequenceData != null ? Mathf.Max(0.28f, sequenceData.Timings.finalExplosionHoldDuration) : 0.32f;

        double shot01Start = 0d;
        double shot02Start = shot01Start + introPoseDuration * 0.55d;
        double drawReleaseTime = shot02Start + closeUpDuration * 0.65d;
        double shot03Start = drawReleaseTime + drawReleaseDuration * 0.4d;
        double stormSustainTime = shot03Start + 0.08d;
        double shot04Start = shot03Start + slashStormDuration;
        double explosionPrepareTime = shot04Start + walkoutDuration * 0.72d;
        double shot05Start = explosionPrepareTime + 0.1d;
        double finalExplosionTime = shot05Start;
        double cameraSessionEndTime = finalExplosionTime + explosionHoldDuration + 0.14d;
        double restoreTime = cameraSessionEndTime + 0.05d;

        CreateAnimationClip(animTrack, "CLIP_DrawPose", sequenceData != null ? sequenceData.CinematicAnimation.introPoseClip : null,
            shot01Start, shot02Start + closeUpDuration, sequenceData != null ? sequenceData.CinematicAnimation.introPoseClipStartNormalized : 0f,
            sequenceData != null ? sequenceData.CinematicAnimation.introPoseClipEndNormalized : 1f);

        CreateAnimationClip(animTrack, "CLIP_DrawRelease", sequenceData != null ? sequenceData.CinematicAnimation.dashSlashClip : null,
            drawReleaseTime, drawReleaseDuration, sequenceData != null ? sequenceData.CinematicAnimation.dashSlashClipStartNormalized : 0f,
            sequenceData != null ? sequenceData.CinematicAnimation.dashSlashClipEndNormalized : 1f);

        CreateAnimationClip(animTrack, "CLIP_Walkout", sequenceData != null ? sequenceData.CinematicAnimation.walkoutClip : null,
            shot04Start, walkoutDuration, 0f, 1f);

        CreateShotClip(camTrack, director, "SHOT_Intro", introCam, shot01Start, shot02Start - shot01Start);
        CreateShotClip(camTrack, director, "SHOT_CloseUp", closeCam, shot02Start, shot03Start - shot02Start);
        CreateShotClip(camTrack, director, "SHOT_SlashStorm", stormCam, shot03Start, shot04Start - shot03Start);
        CreateShotClip(camTrack, director, "SHOT_Walkout", walkoutCam, shot04Start, cameraSessionEndTime - shot04Start);

        CreateSignal(signalTrack, shot01Start, signals["Sig_CameraSessionBegin"]);
        CreateSignal(signalTrack, shot01Start + 0.02d, signals["Sig_DrawPoseStart"]);
        CreateSignal(signalTrack, shot02Start, signals["Sig_CloseUpStart"]);
        CreateSignal(signalTrack, drawReleaseTime, signals["Sig_DrawSlashRelease"]);
        CreateSignal(signalTrack, shot03Start, signals["Sig_SlashStormStart"]);
        CreateSignal(signalTrack, stormSustainTime, signals["Sig_SlashStormSustainStart"]);
        CreateSignal(signalTrack, stormSustainTime + 0.02d, signals["Sig_PlayerHideForStorm"]);
        CreateSignal(signalTrack, shot04Start - 0.02d, signals["Sig_PlayerShowForWalkout"]);
        CreateSignal(signalTrack, shot04Start, signals["Sig_WalkoutStart"]);
        CreateSignal(signalTrack, explosionPrepareTime, signals["Sig_ExplosionPrepare"]);
        CreateSignal(signalTrack, finalExplosionTime, signals["Sig_GameplayCommitDamage"]);
        CreateSignal(signalTrack, finalExplosionTime + 0.01d, signals["Sig_FinalExplosion"]);
        CreateSignal(signalTrack, cameraSessionEndTime, signals["Sig_CameraSessionEnd"]);
        CreateSignal(signalTrack, restoreTime, signals["Sig_GameplayRestore"]);

        EditorUtility.SetDirty(timelineAsset);
        report.Info("Rebuilt TL_Ultimate_PlayerSword timeline asset.");
        return timelineAsset;
    }

    static void CreateAnimationClip(AnimationTrack track, string displayName, AnimationClip sourceClip, double start, double duration, float startNormalized, float endNormalized)
    {
        if (track == null || sourceClip == null || duration <= 0d)
            return;

        TimelineClip timelineClip = track.CreateClip<AnimationPlayableAsset>();
        timelineClip.displayName = displayName;
        timelineClip.start = start;
        timelineClip.duration = duration;
        timelineClip.clipIn = sourceClip.length * Mathf.Clamp01(startNormalized);

        var playableAsset = timelineClip.asset as AnimationPlayableAsset;
        if (playableAsset == null)
            return;

        float normalizedRange = Mathf.Clamp01(endNormalized) - Mathf.Clamp01(startNormalized);
        float effectiveRange = Mathf.Max(0.05f, normalizedRange);
        playableAsset.clip = sourceClip;
        playableAsset.removeStartOffset = false;
        playableAsset.applyFootIK = false;
        timelineClip.timeScale = Mathf.Max(0.1f, (sourceClip.length * effectiveRange) / (float)duration);
    }

    static void CreateShotClip(CinemachineTrack track, PlayableDirector director, string displayName, CinemachineCamera vcam, double start, double duration)
    {
        if (track == null || director == null || vcam == null || duration <= 0d)
            return;

        TimelineClip clip = track.CreateClip<CinemachineShot>();
        clip.displayName = displayName;
        clip.start = start;
        clip.duration = duration;

        var shot = clip.asset as CinemachineShot;
        if (shot == null)
            return;

        var exposedName = new PropertyName($"Ultimate.{displayName}.{vcam.name}");
        shot.DisplayName = displayName;
        shot.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = exposedName };
        director.SetReferenceValue(exposedName, vcam);
    }

    static void CreateSignal(SignalTrack track, double time, SignalAsset signal)
    {
        if (track == null || signal == null)
            return;

        SignalEmitter emitter = track.CreateMarker<SignalEmitter>(time);
        emitter.asset = signal;
        emitter.emitOnce = true;
    }

    static Dictionary<string, SignalAsset> EnsureSignalAssets()
    {
        var results = new Dictionary<string, SignalAsset>(SignalNames.Length);
        for (int i = 0; i < SignalNames.Length; i++)
        {
            string signalName = SignalNames[i];
            string assetPath = $"{SignalsFolderPath}/{signalName}.asset";
            SignalAsset asset = AssetDatabase.LoadAssetAtPath<SignalAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SignalAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            results[signalName] = asset;
        }

        return results;
    }

    static CinemachineCamera EnsureUltimateCamera(Transform parent, string name, Transform follow, Transform lookAt, float fov)
    {
        Transform child = parent.Find(name);
        GameObject cameraObject = child != null ? child.gameObject : new GameObject(name);
        if (child == null)
            cameraObject.transform.SetParent(parent, false);

        var cmCamera = EnsureComponent<CinemachineCamera>(cameraObject);
        cmCamera.Follow = follow;
        cmCamera.LookAt = lookAt;
        LensSettings lens = cmCamera.Lens;
        lens.FieldOfView = fov;
        cmCamera.Lens = lens;
        cmCamera.Priority = new PrioritySettings { Enabled = true, Value = 100 };

        var body = EnsureComponent<CinemachineHardLockToTarget>(cameraObject);
        body.Damping = 0f;
        var aim = EnsureComponent<CinemachineHardLookAt>(cameraObject);
        aim.LookAtOffset = Vector3.zero;
        cameraObject.SetActive(false);
        return cmCamera;
    }

    static Transform ResolvePlayerVisualRoot(Transform visualRoot)
    {
        if (visualRoot == null)
            return null;

        PlayerVisualRig rig = visualRoot.GetComponentInChildren<PlayerVisualRig>(true);
        if (rig != null)
            return rig.transform;

        if (visualRoot.childCount > 0)
            return visualRoot.GetChild(0);

        return visualRoot;
    }

    static GameObject FindBossObject(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            if (root.GetComponent<BossController>() != null)
                return root;

            if (root.name == "boss")
                return root;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            BossController bossController = root.GetComponentInChildren<BossController>(true);
            if (bossController != null)
                return bossController.gameObject;
        }

        return null;
    }

    static GameObject FindPlayerObject(Scene scene, SetupReport report)
    {
        GameObject tagged = FindTaggedObject(scene, "Player");
        if (tagged != null)
            return tagged;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null)
                continue;

            if (root.name == "Player")
            {
                report.Info("Resolved Player by root name fallback.");
                return root;
            }

            PlayerUltimateController controller = root.GetComponentInChildren<PlayerUltimateController>(true);
            if (controller != null)
            {
                report.Info("Resolved Player by PlayerUltimateController fallback.");
                return controller.gameObject;
            }
        }

        return null;
    }

    static GameObject FindTaggedObject(Scene scene, string tag)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current != null && current.CompareTag(tag))
                    return current.gameObject;
            }
        }

        return null;
    }

    static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Transform EnsureAnchor(Transform parent, string name, Vector3 localPosition, Vector3 localEulerAngles)
    {
        Transform anchor = EnsureChild(parent, name);
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.Euler(localEulerAngles);
        anchor.localScale = Vector3.one;
        return anchor;
    }

    static GameObject EnsureRootObject(Scene scene, string name)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null && root.name == name)
                return root;
        }

        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    static Transform FindDescendant(Transform root, string name)
    {
        if (root == null)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current != null && current.name == name)
                return current;
        }

        return null;
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
            component = target.AddComponent<T>();
        return component;
    }

    static void SetObject(SerializedObject so, string propertyPath, Object value)
    {
        SerializedProperty property = so.FindProperty(propertyPath);
        if (property != null)
            property.objectReferenceValue = value;
    }

    static void SetObjectArray(SerializedObject so, string propertyPath, Object[] values)
    {
        SerializedProperty property = so.FindProperty(propertyPath);
        if (property == null || !property.isArray)
            return;

        property.arraySize = values != null ? values.Length : 0;
        if (values == null)
            return;

        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void SetBool(SerializedObject so, string propertyPath, bool value)
    {
        SerializedProperty property = so.FindProperty(propertyPath);
        if (property != null)
            property.boolValue = value;
    }

    static string ToFieldName(string signalName)
    {
        switch (signalName)
        {
            case "Sig_CameraSessionBegin": return "sigCameraSessionBegin";
            case "Sig_DrawPoseStart": return "sigDrawPoseStart";
            case "Sig_CloseUpStart": return "sigCloseUpStart";
            case "Sig_DrawSlashRelease": return "sigDrawSlashRelease";
            case "Sig_SlashStormStart": return "sigSlashStormStart";
            case "Sig_SlashStormSustainStart": return "sigSlashStormSustainStart";
            case "Sig_PlayerHideForStorm": return "sigPlayerHideForStorm";
            case "Sig_PlayerShowForWalkout": return "sigPlayerShowForWalkout";
            case "Sig_WalkoutStart": return "sigWalkoutStart";
            case "Sig_GameplayCommitDamage": return "sigGameplayCommitDamage";
            case "Sig_ExplosionPrepare": return "sigExplosionPrepare";
            case "Sig_FinalExplosion": return "sigFinalExplosion";
            case "Sig_CameraSessionEnd": return "sigCameraSessionEnd";
            case "Sig_GameplayRestore": return "sigGameplayRestore";
            default: return string.Empty;
        }
    }

    static void EnsureFolder(string assetPath)
    {
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(fullPath))
            Directory.CreateDirectory(fullPath);
    }

    public readonly struct SetupSummary
    {
        public SetupSummary(string details)
        {
            Details = details;
        }

        public string Details { get; }
        public override string ToString() => Details;
    }

    sealed class SetupReport
    {
        readonly List<string> infos = new List<string>();
        readonly List<string> warnings = new List<string>();

        public void Info(string message) => infos.Add("[INFO] " + message);
        public void Warn(string message) => warnings.Add("[WARN] " + message);

        public void Emit(bool showDialog)
        {
            string details = BuildDetails();
            if (warnings.Count > 0)
                Debug.LogWarning(details);
            else
                Debug.Log(details);

            if (showDialog)
                EditorUtility.DisplayDialog("Ultimate Cinematic Setup", $"Warnings: {warnings.Count}\n\nSee Console for details.", "OK");
        }

        public SetupSummary ToSummary() => new SetupSummary(BuildDetails());

        string BuildDetails()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Ultimate cinematic setup completed.");
            builder.AppendLine($"Warnings: {warnings.Count}");
            for (int i = 0; i < infos.Count; i++)
                builder.AppendLine(infos[i]);
            for (int i = 0; i < warnings.Count; i++)
                builder.AppendLine(warnings[i]);
            return builder.ToString();
        }
    }
}
