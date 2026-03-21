using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameplayRegressionValidator
{
    const string PauseOptionsRootPrefabPath = "Assets/Prefabs/Generated/PauseOptionsRoot.prefab";

    static readonly string[] TargetScenePaths =
    {
        "Assets/Scenes/TitleScene.unity",
        "Assets/Scenes/BootScene.unity",
        "Assets/Scenes/MainScene.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Tutorial.unity",
    };

    [MenuItem("Tools/Validation/Run Gameplay Regression Checks")]
    static void RunGameplayRegressionChecks()
    {
        RunChecksInternal(showDialog: true);
    }

    public static ValidationSummary RunFromFastMcp()
    {
        return RunChecksInternal(showDialog: false);
    }

    static ValidationSummary RunChecksInternal(bool showDialog)
    {
        var activeScene = SceneManager.GetActiveScene();
        var scenesOpenedByTool = new List<Scene>();
        var report = new ValidationReport();

        try
        {
            ValidatePauseOptionsChromeAsset(report);

            foreach (var scenePath in TargetScenePaths)
            {
                var scene = SceneManager.GetSceneByPath(scenePath);
                var openedByTool = !scene.IsValid() || !scene.isLoaded;

                if (openedByTool)
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    scenesOpenedByTool.Add(scene);
                }

                ValidateScene(scene, report);
            }
        }
        finally
        {
            for (var i = scenesOpenedByTool.Count - 1; i >= 0; i--)
            {
                var scene = scenesOpenedByTool[i];
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }

            if (activeScene.IsValid() && activeScene.isLoaded)
                SceneManager.SetActiveScene(activeScene);
        }

        report.Emit(showDialog);
        return report.ToSummary();
    }

    static void ValidatePauseOptionsChromeAsset(ValidationReport report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PauseOptionsRootPrefabPath);
        if (prefab == null)
        {
            report.Error("GLOBAL", $"Pause options prefab missing: {PauseOptionsRootPrefabPath}");
            return;
        }

        ValidateNamedUiNode(prefab.transform, "_OverlayHeaderLine", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayTopGlow", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayBottomGlow", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayLeftBar", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayRightBar", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayCornerTL", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayCornerTR", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayCornerBL", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayCornerBR", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_OverlayTabs", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_TabControls", typeof(Button), report);
        ValidateNamedUiNode(prefab.transform, "_TabAudio", typeof(Button), report);
        ValidateNamedUiNode(prefab.transform, "_TabVideo", typeof(Button), report);
        ValidateNamedUiNode(prefab.transform, "_Indicator", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_ControlsColumns", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_StandardColumns", typeof(RectTransform), report);
        ValidateNamedUiNode(prefab.transform, "_ActionColumn", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_CurrentColumn", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_ChangeColumn", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_OptionColumn", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_ValueColumn", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_ControlColumn", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_FooterHint", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_RebindHintPanel", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RebindHintText", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeDropdownBlocker", typeof(Button), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeDropdownPopup", typeof(ScrollRect), report);
        ValidateNamedUiNode(prefab.transform, "_Viewport", typeof(Mask), report);
        ValidateNamedUiNode(prefab.transform, "_Content", typeof(VerticalLayoutGroup), report);
        ValidateNamedUiNode(prefab.transform, "_Scrollbar", typeof(Scrollbar), report);
        ValidateNamedUiNode(prefab.transform, "_Handle", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_OptionTemplate", typeof(Button), report);
        ValidateNamedUiNode(prefab.transform, "SelectedMarker", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeKeyValue", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeKeyValueText", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeRebindButton", typeof(Button), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeSliderValueBox", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeToggleValueBox", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeDropdownValueBox", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeSliderTrack", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeToggleBackground", typeof(Image), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeCaption", typeof(Text), report);
        ValidateNamedUiNode(prefab.transform, "_RuntimeArrow", typeof(Text), report);
    }

    static void ValidateScene(Scene scene, ValidationReport report)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            report.Error("GLOBAL", $"Failed to load scene '{scene.path}'.");
            return;
        }

        var sceneLabel = scene.path;
        ValidateUiFlow(sceneLabel, scene, report);

        if (!IsGameplayScene(scene.path))
            return;

        var player = FindTaggedObject(scene, "Player");
        if (player == null)
        {
            report.Error(sceneLabel, "Missing Player tagged object.");
            return;
        }

        ValidatePlayer(sceneLabel, player, report);
        ValidateInteraction(sceneLabel, scene, report);
        ValidateSceneSpecific(sceneLabel, scene, player, report);

        var legacyLockOn = FindFirstInScene<SimpleLockOnController>(scene);
        if (legacyLockOn != null)
            report.Warn(sceneLabel, "SimpleLockOnController is present. Prefer PlayerLockOn as the single lock-on source.");
    }

    static bool IsGameplayScene(string scenePath)
    {
        return scenePath.EndsWith("MainScene.unity")
            || scenePath.EndsWith("Lobby.unity")
            || scenePath.EndsWith("Tutorial.unity");
    }

    static void ValidateUiFlow(string sceneLabel, Scene scene, ValidationReport report)
    {
        if (scene.path.EndsWith("TitleScene.unity"))
        {
            ValidateTitleScene(sceneLabel, scene, report);
            return;
        }

        if (scene.path.EndsWith("BootScene.unity"))
        {
            ValidateBootScene(sceneLabel, scene, report);
            return;
        }

        if (IsGameplayScene(scene.path))
            ValidateGameplayHud(sceneLabel, scene, report);
    }

    static void ValidateTitleScene(string sceneLabel, Scene scene, ValidationReport report)
    {
        var titleUi = FindFirstInScene<TitleSceneUIController>(scene);
        Require(sceneLabel, titleUi, "TitleSceneUIController missing in TitleScene.", report);

        if (titleUi != null)
        {
            var serialized = new SerializedObject(titleUi);
            CheckSceneNameProperty(sceneLabel, serialized, "gameSceneName", "TitleSceneUIController.gameSceneName", report);
        }

        var titleManager = FindFirstInScene<TitleManager>(scene);
        if (titleManager != null)
        {
            var serialized = new SerializedObject(titleManager);
            CheckSceneNameProperty(sceneLabel, serialized, "nextSceneName", "TitleManager.nextSceneName", report);
        }
    }

    static void ValidateBootScene(string sceneLabel, Scene scene, ValidationReport report)
    {
        var bootLoader = FindFirstInScene<SystemBootLoader>(scene);
        Require(sceneLabel, bootLoader, "SystemBootLoader missing in BootScene.", report);
        if (bootLoader == null)
            return;

        var serialized = new SerializedObject(bootLoader);
        CheckSceneNameProperty(sceneLabel, serialized, "nextSceneName", "SystemBootLoader.nextSceneName", report);
        CheckObjectReference(sceneLabel, serialized, "logText", "SystemBootLoader.logText", report);
        CheckObjectReference(sceneLabel, serialized, "progressText", "SystemBootLoader.progressText", report);
        CheckObjectReference(sceneLabel, serialized, "progressBar", "SystemBootLoader.progressBar", report);
        CheckObjectReference(sceneLabel, serialized, "pressAnyKeyText", "SystemBootLoader.pressAnyKeyText", report);
        CheckObjectReference(sceneLabel, serialized, "readyStatusText", "SystemBootLoader.readyStatusText", report);
    }

    static void ValidateGameplayHud(string sceneLabel, Scene scene, ValidationReport report)
    {
        var gauge = FindFirstInScene<UI_UltimateGauge>(scene);
        Require(sceneLabel, gauge, "UI_UltimateGauge missing in gameplay scene.", report);
        if (gauge != null && gauge.fill == null)
            report.Error(sceneLabel, "UI_UltimateGauge.fill is null.");

        var pauseManager = FindFirstInScene<PauseManager>(scene);
        if (pauseManager != null)
        {
            var serializedPause = new SerializedObject(pauseManager);
            CheckObjectReference(sceneLabel, serializedPause, "uiView", "PauseManager.uiView", report);
            CheckSceneNameProperty(sceneLabel, serializedPause, "titleSceneName", "PauseManager.titleSceneName", report);
        }

        var pauseView = FindFirstInScene<PauseMenuView>(scene);
        if (pauseView != null)
        {
            var serializedView = new SerializedObject(pauseView);
            CheckObjectReference(sceneLabel, serializedView, "menuRoot", "PauseMenuView.menuRoot", report);
            CheckObjectReference(sceneLabel, serializedView, "backgroundGroup", "PauseMenuView.backgroundGroup", report);
            CheckObjectReference(sceneLabel, serializedView, "menuContainer", "PauseMenuView.menuContainer", report);
            CheckObjectReference(sceneLabel, serializedView, "settingsPanel", "PauseMenuView.settingsPanel", report);
            CheckObjectReference(sceneLabel, serializedView, "settingsGroup", "PauseMenuView.settingsGroup", report);
            CheckObjectReference(sceneLabel, serializedView, "settingsContentHostOverride", "PauseMenuView.settingsContentHostOverride", report);
            CheckObjectReference(sceneLabel, serializedView, "settingsContentPrefab", "PauseMenuView.settingsContentPrefab", report);

        }

        if (scene.path.EndsWith("Lobby.unity"))
        {
            var lobbyManager = FindFirstInScene<LobbyManager>(scene);
            if (lobbyManager != null)
            {
                var serializedLobby = new SerializedObject(lobbyManager);
                CheckSceneNameProperty(sceneLabel, serializedLobby, "nextSceneName", "LobbyManager.nextSceneName", report);
                CheckObjectReference(sceneLabel, serializedLobby, "questTitleText", "LobbyManager.questTitleText", report, false);
                CheckObjectReference(sceneLabel, serializedLobby, "questDescriptionText", "LobbyManager.questDescriptionText", report, false);
                CheckObjectReference(sceneLabel, serializedLobby, "elevatorPanel", "LobbyManager.elevatorPanel", report, false);
            }
        }
    }

    static void ValidatePlayer(string sceneLabel, GameObject player, ValidationReport report)
    {
        var refs = player.GetComponent<PlayerReferences>();
        var combatState = player.GetComponent<SimpleCombatStateReader>();
        var move = player.GetComponent<PlayerMoveController>();
        var lockOn = player.GetComponent<PlayerLockOn>();
        var ultimate = player.GetComponent<PlayerUltimateController>();
        var inputBlocker = player.GetComponent<SimpleInputBlocker>();
        var combat = player.GetComponent<PlayerCombatController>();
        var guard = player.GetComponent<PlayerGuardController>();
        var dodge = player.GetComponent<PlayerDodgeController>();

        Require(sceneLabel, refs, "PlayerReferences missing on Player.", report);
        Require(sceneLabel, combatState, "SimpleCombatStateReader missing on Player.", report);
        Require(sceneLabel, move, "PlayerMoveController missing on Player.", report);
        Require(sceneLabel, lockOn, "PlayerLockOn missing on Player.", report);
        Require(sceneLabel, ultimate, "PlayerUltimateController missing on Player.", report);
        Require(sceneLabel, inputBlocker, "SimpleInputBlocker missing on Player.", report);
        Require(sceneLabel, combat, "PlayerCombatController missing on Player.", report);
        Require(sceneLabel, guard, "PlayerGuardController missing on Player.", report);
        Require(sceneLabel, dodge, "PlayerDodgeController missing on Player.", report);

        if (move != null)
            ValidateMoveController(sceneLabel, move, report);
        if (lockOn != null)
            ValidateLockOnController(sceneLabel, lockOn, report);
        if (ultimate != null)
            ValidateUltimateController(sceneLabel, ultimate, report);
        if (combat != null)
            ValidateCombatController(sceneLabel, combat, report);
        if (guard != null)
            ValidateGuardController(sceneLabel, guard, report);
        if (dodge != null)
            ValidateDodgeController(sceneLabel, dodge, report);

        if (lockOn != null && !(lockOn is ILockOnController))
            report.Error(sceneLabel, "PlayerLockOn no longer satisfies ILockOnController.");

        if (refs == null)
            return;

        if (refs.PlayerRoot == null)
            report.Error(sceneLabel, "PlayerReferences.PlayerRoot is null.");
        if (refs.VisualRoot == null)
            report.Error(sceneLabel, "PlayerReferences.VisualRoot is null.");
        if (refs.VisualRig == null)
            report.Error(sceneLabel, "PlayerReferences.VisualRig is null.");
        if (refs.MainAnimator == null)
            report.Error(sceneLabel, "PlayerReferences.MainAnimator is null.");
        if (refs.LockPivot == null)
            report.Error(sceneLabel, "PlayerReferences.LockPivot is null.");
        if (refs.UltimateSpawnRoot == null)
            report.Error(sceneLabel, "PlayerReferences.UltimateSpawnRoot is null.");
        if (refs.PrimaryAttackHitbox == null)
            report.Error(sceneLabel, "PlayerReferences.PrimaryAttackHitbox is null.");

        if (refs.AttackHitboxes == null || refs.AttackHitboxes.Length == 0)
            report.Error(sceneLabel, "PlayerReferences.AttackHitboxes is empty.");

        if (refs.VisualRig != null && refs.VisualRig.PrimaryAttackHitbox == null)
            report.Warn(sceneLabel, "PlayerVisualRig.PrimaryAttackHitbox is null.");

        if (ultimate != null && !Mathf.Approximately(ultimate.gaugePerH, 2f))
            report.Warn(sceneLabel, $"PlayerUltimateController.gaugePerH is {ultimate.gaugePerH}, expected 2.");

        if (ultimate != null)
        {
            if (ultimate.allowInAir)
                report.Warn(sceneLabel, "PlayerUltimateController.allowInAir is enabled.");
            if (!ultimate.blockWhenStaggered)
                report.Warn(sceneLabel, "PlayerUltimateController.blockWhenStaggered is disabled.");
            if (!ultimate.blockWhenAttacking)
                report.Warn(sceneLabel, "PlayerUltimateController.blockWhenAttacking is disabled.");
            if (!ultimate.blockWhenGuarding)
                report.Warn(sceneLabel, "PlayerUltimateController.blockWhenGuarding is disabled.");
            if (!ultimate.blockWhenDodging)
                report.Warn(sceneLabel, "PlayerUltimateController.blockWhenDodging is disabled.");
        }
    }

    static void ValidateInteraction(string sceneLabel, Scene scene, ValidationReport report)
    {
        var interaction = FindFirstInScene<InteractionManager>(scene);
        if (interaction == null)
        {
            report.Warn(sceneLabel, "InteractionManager not found.");
            return;
        }

        if (interaction.lockOnReader != null)
            report.Error(sceneLabel, "InteractionManager.lockOnReader should be null and resolve PlayerLockOn directly.");

        if (interaction.inputRouter != null)
            report.Warn(sceneLabel, "InteractionManager.inputRouter is still assigned. Legacy fallback remains active.");
    }

    static void ValidateSceneSpecific(string sceneLabel, Scene scene, GameObject player, ValidationReport report)
    {
        if (scene.path.EndsWith("MainScene.unity"))
        {
            var breakController = FindFirstInScene<BossBreakController>(scene);
            var bossUiController = FindFirstInScene<BossUIController>(scene);
            var bossBreakHud = FindFirstInScene<BossBreakHUD>(scene);

            Require(sceneLabel, breakController, "BossBreakController missing in MainScene.", report);
            Require(sceneLabel, bossUiController, "BossUIController missing in MainScene.", report);
            Require(sceneLabel, bossBreakHud, "BossBreakHUD missing in MainScene.", report);

            if (breakController != null)
            {
                var serializedBreak = new SerializedObject(breakController);
                CheckPositiveFloat(sceneLabel, serializedBreak, "maxBreak", "BossBreakController.maxBreak", report);
                CheckPositiveFloat(sceneLabel, serializedBreak, "baseBreakPerHit", "BossBreakController.baseBreakPerHit", report);
                CheckPositiveFloat(sceneLabel, serializedBreak, "breakDuration", "BossBreakController.breakDuration", report);
                CheckNonEmptyString(sceneLabel, serializedBreak, "breakBoolName", "BossBreakController.breakBoolName", report);
                CheckObjectReference(sceneLabel, serializedBreak, "bossHealth", "BossBreakController.bossHealth", report, false);
            }

            if (bossUiController != null)
            {
                if (bossUiController.topHudRoot == null)
                    report.Error(sceneLabel, "BossUIController.topHudRoot is null.");
                if (bossUiController.hpHud == null)
                    report.Error(sceneLabel, "BossUIController.hpHud is null.");
                if (bossUiController.breakHud == null)
                    report.Error(sceneLabel, "BossUIController.breakHud is null.");
            }

            if (bossBreakHud != null)
            {
                if (bossBreakHud.breakFillImage == null)
                    report.Error(sceneLabel, "BossBreakHUD.breakFillImage is null.");
                if (bossBreakHud.hudRoot == null)
                    report.Error(sceneLabel, "BossBreakHUD.hudRoot is null.");
            }
        }

        var legacyUltimate = player.GetComponent<UltimateSkillController>();
        if (legacyUltimate != null)
        {
            var serialized = new SerializedObject(legacyUltimate);
            var primaryController = serialized.FindProperty("primaryController");
            if (primaryController == null || primaryController.objectReferenceValue == null)
                report.Error(sceneLabel, "UltimateSkillController.primaryController is not assigned.");
        }
    }

    static void Require<T>(string sceneLabel, T component, string message, ValidationReport report) where T : Object
    {
        if (component == null)
            report.Error(sceneLabel, message);
    }

    static void CheckPositiveFloat(string sceneLabel, SerializedObject serializedObject, string propertyName, string label, ValidationReport report)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            report.Warn(sceneLabel, $"{label} property not found.");
            return;
        }

        if (property.floatValue <= 0f)
            report.Error(sceneLabel, $"{label} must be greater than 0.");
    }

    static void ValidateMoveController(string sceneLabel, PlayerMoveController move, ValidationReport report)
    {
        var serialized = new SerializedObject(move);
        CheckObjectReference(sceneLabel, serialized, "playerRoot", "PlayerMoveController.playerRoot", report, false);
        CheckObjectReference(sceneLabel, serialized, "cameraTransform", "PlayerMoveController.cameraTransform", report, false);
        CheckObjectReference(sceneLabel, serialized, "playerLockOn", "PlayerMoveController.playerLockOn", report, false);
        CheckObjectReference(sceneLabel, serialized, "guardController", "PlayerMoveController.guardController", report, false);
        CheckObjectReference(sceneLabel, serialized, "animator", "PlayerMoveController.animator", report, false);
        CheckPositiveFloat(sceneLabel, serialized, "runSpeed", "PlayerMoveController.runSpeed", report);
        CheckPositiveFloat(sceneLabel, serialized, "accel", "PlayerMoveController.accel", report);
        CheckPositiveFloat(sceneLabel, serialized, "decel", "PlayerMoveController.decel", report);
        CheckPositiveFloat(sceneLabel, serialized, "gravity", "PlayerMoveController.gravity", report);
        CheckNonEmptyString(sceneLabel, serialized, "p_Speed", "PlayerMoveController.p_Speed", report);
    }

    static void ValidateCombatController(string sceneLabel, PlayerCombatController combat, ValidationReport report)
    {
        var serialized = new SerializedObject(combat);
        CheckObjectReference(sceneLabel, serialized, "firstLight", "PlayerCombatController.firstLight", report);
        CheckObjectReference(sceneLabel, serialized, "firstHeavy", "PlayerCombatController.firstHeavy", report);
        CheckObjectReference(sceneLabel, serialized, "animator", "PlayerCombatController.animator", report, false);
        CheckPositiveFloat(sceneLabel, serialized, "inputBufferTime", "PlayerCombatController.inputBufferTime", report);
        CheckPositiveFloat(sceneLabel, serialized, "comboResetTime", "PlayerCombatController.comboResetTime", report);
        CheckPositiveFloat(sceneLabel, serialized, "minClickInterval", "PlayerCombatController.minClickInterval", report);
        CheckNonEmptyString(sceneLabel, serialized, "speedParam", "PlayerCombatController.speedParam", report);
    }

    static void ValidateGuardController(string sceneLabel, PlayerGuardController guard, ValidationReport report)
    {
        var serialized = new SerializedObject(guard);
        CheckObjectReference(sceneLabel, serialized, "move", "PlayerGuardController.move", report, false);
        CheckObjectReference(sceneLabel, serialized, "ultimate", "PlayerGuardController.ultimate", report, false);
        CheckObjectReference(sceneLabel, serialized, "animator", "PlayerGuardController.animator", report, false);
        CheckPositiveFloat(sceneLabel, serialized, "frontArcDegrees", "PlayerGuardController.frontArcDegrees", report);
        CheckPositiveFloat(sceneLabel, serialized, "perfectGuardWindow", "PlayerGuardController.perfectGuardWindow", report);
        CheckNonEmptyString(sceneLabel, serialized, "guardBoolParam", "PlayerGuardController.guardBoolParam", report);
        CheckNonEmptyString(sceneLabel, serialized, "parrySuccessTrigger", "PlayerGuardController.parrySuccessTrigger", report);
    }

    static void ValidateDodgeController(string sceneLabel, PlayerDodgeController dodge, ValidationReport report)
    {
        var serialized = new SerializedObject(dodge);
        CheckObjectReference(sceneLabel, serialized, "playerRoot", "PlayerDodgeController.playerRoot", report, false);
        CheckObjectReference(sceneLabel, serialized, "cameraTransform", "PlayerDodgeController.cameraTransform", report, false);
        CheckObjectReference(sceneLabel, serialized, "move", "PlayerDodgeController.move", report, false);
        CheckObjectReference(sceneLabel, serialized, "playerLockOn", "PlayerDodgeController.playerLockOn", report, false);
        CheckObjectReference(sceneLabel, serialized, "animator", "PlayerDodgeController.animator", report, false);
        CheckObjectReference(sceneLabel, serialized, "moveLocker", "PlayerDodgeController.moveLocker", report, false);
        CheckPositiveFloat(sceneLabel, serialized, "dodgeDuration", "PlayerDodgeController.dodgeDuration", report);
        CheckPositiveFloat(sceneLabel, serialized, "dodgeSpeed", "PlayerDodgeController.dodgeSpeed", report);
        CheckNonEmptyString(sceneLabel, serialized, "p_IsDodging", "PlayerDodgeController.p_IsDodging", report);
    }

    static void ValidateLockOnController(string sceneLabel, PlayerLockOn lockOn, ValidationReport report)
    {
        var serialized = new SerializedObject(lockOn);
        CheckPositiveFloat(sceneLabel, serialized, "lockOnRange", "PlayerLockOn.lockOnRange", report);
        CheckPositiveFloat(sceneLabel, serialized, "lockOnFOV", "PlayerLockOn.lockOnFOV", report);
        CheckNonEmptyString(sceneLabel, serialized, "pivotName", "PlayerLockOn.pivotName", report);
        CheckObjectReference(sceneLabel, serialized, "cameraMgr", "PlayerLockOn.cameraMgr", report, false);

        var enemyMask = serialized.FindProperty("enemyMask");
        if (enemyMask != null && enemyMask.intValue == 0)
            report.Error(sceneLabel, "PlayerLockOn.enemyMask must not be empty.");
    }

    static void ValidateUltimateController(string sceneLabel, PlayerUltimateController ultimate, ValidationReport report)
    {
        if (ultimate.gaugeMax <= 0f)
            report.Error(sceneLabel, "PlayerUltimateController.gaugeMax must be greater than 0.");
        if (ultimate.gaugePerParry <= 0f)
            report.Error(sceneLabel, "PlayerUltimateController.gaugePerParry must be greater than 0.");
        if (ultimate.gaugePerPerfectDodge <= 0f)
            report.Error(sceneLabel, "PlayerUltimateController.gaugePerPerfectDodge must be greater than 0.");
        if (ultimate.finisherFixedDamage <= 0)
            report.Error(sceneLabel, "PlayerUltimateController.finisherFixedDamage must be greater than 0.");
        if (ultimate.multihitFixedDamage <= 0)
            report.Error(sceneLabel, "PlayerUltimateController.multihitFixedDamage must be greater than 0.");
        if (ultimate.finisherFixedDamage < ultimate.multihitFixedDamage)
            report.Warn(sceneLabel, "PlayerUltimateController.finisherFixedDamage is lower than multihitFixedDamage.");
        if (!ultimate.useLegacyHotkey && ultimate.activateAction == null)
            report.Error(sceneLabel, "PlayerUltimateController has no activation input path.");
        if (!ultimate.lockInputDuringCinematic)
            report.Warn(sceneLabel, "PlayerUltimateController.lockInputDuringCinematic is disabled.");
        if (!ultimate.invulnerableDuringCinematic)
            report.Warn(sceneLabel, "PlayerUltimateController.invulnerableDuringCinematic is disabled.");
        if (!ultimate.freezeWorldTimeDuringCinematic)
            report.Warn(sceneLabel, "PlayerUltimateController.freezeWorldTimeDuringCinematic is disabled.");
    }

    static void CheckObjectReference(
        string sceneLabel,
        SerializedObject serializedObject,
        string propertyName,
        string label,
        ValidationReport report,
        bool errorIfNull = true)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            report.Warn(sceneLabel, $"{label} property not found.");
            return;
        }

        if (property.objectReferenceValue != null)
            return;

        if (errorIfNull)
            report.Error(sceneLabel, $"{label} is null.");
        else
            report.Warn(sceneLabel, $"{label} is null and relies on runtime auto-wiring.");
    }

    static void CheckNonEmptyString(string sceneLabel, SerializedObject serializedObject, string propertyName, string label, ValidationReport report)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            report.Warn(sceneLabel, $"{label} property not found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(property.stringValue))
            report.Error(sceneLabel, $"{label} must not be empty.");
    }

    static void CheckSceneNameProperty(string sceneLabel, SerializedObject serializedObject, string propertyName, string label, ValidationReport report)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            report.Warn(sceneLabel, $"{label} property not found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(property.stringValue))
        {
            report.Error(sceneLabel, $"{label} must not be empty.");
            return;
        }

        if (!SceneExistsInBuildSettings(property.stringValue))
            report.Error(sceneLabel, $"{label} references missing build scene '{property.stringValue}'.");
    }

    static bool SceneExistsInBuildSettings(string sceneName)
    {
        var scenes = EditorBuildSettings.scenes;
        for (var i = 0; i < scenes.Length; i++)
        {
            var path = scenes[i].path;
            if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static GameObject FindTaggedObject(Scene scene, string tag)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var current in transforms)
            {
                if (current.CompareTag(tag))
                    return current.gameObject;
            }
        }

        return null;
    }

    static T FindFirstInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        return null;
    }

    static void ValidateNamedUiNode(Transform root, string nodeName, System.Type requiredComponentType, ValidationReport report)
    {
        var node = FindDeepChild(root, nodeName);
        if (node == null)
        {
            report.Error("GLOBAL", $"PauseOptionsRoot missing '{nodeName}'.");
            return;
        }

        if (requiredComponentType == typeof(RectTransform))
        {
            if (!(node is RectTransform))
                report.Error("GLOBAL", $"PauseOptionsRoot '{nodeName}' is missing RectTransform.");
            return;
        }

        if (node.GetComponent(requiredComponentType) == null)
            report.Error("GLOBAL", $"PauseOptionsRoot '{nodeName}' is missing {requiredComponentType.Name}.");
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null)
            return null;

        foreach (Transform child in root)
        {
            if (child.name == name)
                return child;

            var nested = FindDeepChild(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }

    public readonly struct ValidationSummary
    {
        public ValidationSummary(int errorCount, int warningCount, string details)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            Details = details;
        }

        public int ErrorCount { get; }
        public int WarningCount { get; }
        public string Details { get; }
        public bool HasErrors => ErrorCount > 0;
        public override string ToString() => Details;
    }

    sealed class ValidationReport
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> warnings = new List<string>();

        public void Error(string sceneLabel, string message)
        {
            errors.Add($"[ERROR] {sceneLabel}: {message}");
        }

        public void Warn(string sceneLabel, string message)
        {
            warnings.Add($"[WARN] {sceneLabel}: {message}");
        }

        public void Emit(bool showDialog)
        {
            var details = BuildDetails();
            if (errors.Count > 0)
                Debug.LogError(details);
            else if (warnings.Count > 0)
                Debug.LogWarning(details);
            else if (showDialog)
                Debug.Log(details);

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Gameplay Regression Checks",
                    $"Errors: {errors.Count}\nWarnings: {warnings.Count}\n\nSee Console for details.",
                    "OK");
            }
        }

        public ValidationSummary ToSummary()
        {
            return new ValidationSummary(errors.Count, warnings.Count, BuildDetails());
        }

        string BuildDetails()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Gameplay regression validation completed.");
            builder.AppendLine($"Errors: {errors.Count}");
            builder.AppendLine($"Warnings: {warnings.Count}");

            foreach (var error in errors)
                builder.AppendLine(error);

            foreach (var warning in warnings)
                builder.AppendLine(warning);

            return builder.ToString();
        }
    }
}
