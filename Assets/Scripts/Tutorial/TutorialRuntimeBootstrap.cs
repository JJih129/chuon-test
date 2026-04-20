using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-5000)]
public class TutorialRuntimeBootstrap : MonoBehaviour
{
    const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";

    TutorialFlowController _flowController;
    TutorialHintUIBridge _hintBridge;
    EGOGuideController _egoGuideController;
    TutorialPlayerRuntimeBridge _playerBridge;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterSceneBootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.path, TutorialScenePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (FindFirstObjectByType<TutorialFlowController>() != null || FindFirstObjectByType<TutorialRuntimeBootstrap>() != null)
            return;

        GameObject root = new GameObject("TutorialPrototypeRuntimeRoot");
        root.AddComponent<TutorialRuntimeBootstrap>();
    }

    void Awake()
    {
        if (!string.Equals(SceneManager.GetActiveScene().path, TutorialScenePath, StringComparison.OrdinalIgnoreCase))
        {
            Destroy(gameObject);
            return;
        }

        BuildRuntimePrototype();
    }

    void Start()
    {
        _flowController?.StartFlow();
    }

    void BuildRuntimePrototype()
    {
        TutorialManager legacyTutorialManager = FindFirstObjectByType<TutorialManager>();
        if (legacyTutorialManager != null)
        {
            // 湲곗〈 ?쒗넗由ъ뼹 ?ㅽ겕由쏀듃????UI 李몄“瑜??좎???梨?鍮꾪솢?깊솕留??쒕떎.
            // ?대젃寃??섎㈃ ???꾨줈?좏??낆씠 媛숈? 由ъ냼?ㅻ? ?ъ궗?⑺빐??湲곗〈 李몄“瑜?源⑥? ?딅뒗??
            TutorialManager.Instance = null;
            legacyTutorialManager.enabled = false;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Camera mainCamera = Camera.main;
        if (player == null || mainCamera == null)
        {
            Debug.LogWarning("[TutorialRuntimeBootstrap] Player or Camera missing. Prototype bootstrap aborted.", this);
            enabled = false;
            return;
        }

        GameObject questGoal = legacyTutorialManager != null ? legacyTutorialManager.movementGoal : GameObject.Find("QuestGoal");
        GameObject attackDummyObject = legacyTutorialManager != null ? legacyTutorialManager.attackDummy : GameObject.Find("enemy");
        GameObject droneObject = legacyTutorialManager != null ? legacyTutorialManager.droneEnemy : GameObject.Find("Drone");
        GameObject sceneMoveObject = GameObject.Find("SceneMove");

        TutorialDrone legacyTutorialDrone = droneObject != null ? droneObject.GetComponent<TutorialDrone>() : null;
        GameObject guardProjectilePrefab = legacyTutorialDrone != null ? legacyTutorialDrone.projectilePrefab : null;

        DisableLegacyTutorialBehaviours(legacyTutorialManager);

        _hintBridge = gameObject.AddComponent<TutorialHintUIBridge>();
        _hintBridge.BindLegacy(legacyTutorialManager);

        _egoGuideController = gameObject.AddComponent<EGOGuideController>();
        _egoGuideController.ConfigureRuntime(_hintBridge);

        ExistingLockOnAdapter lockOnAdapter = player.GetComponent<ExistingLockOnAdapter>();
        if (lockOnAdapter == null)
            lockOnAdapter = player.AddComponent<ExistingLockOnAdapter>();
        lockOnAdapter.ConfigureRuntime(player.GetComponent<PlayerLockOn>());

        _playerBridge = gameObject.AddComponent<TutorialPlayerRuntimeBridge>();
        _playerBridge.ConfigureRuntime(player, mainCamera, lockOnAdapter);

        _flowController = gameObject.AddComponent<TutorialFlowController>();
        gameObject.AddComponent<TutorialDebugController>();

        Canvas tutorialCanvas = ResolveTutorialCanvas(legacyTutorialManager);

        TutorialZoneTrigger movementGoalZone = EnsureComponent<TutorialZoneTrigger>(questGoal);
        if (movementGoalZone != null)
            movementGoalZone.ConfigureRuntime(player.transform, "Player", true);

        TrainingDummyController attackDummy = ConfigureAttackDummy(player, attackDummyObject);
        TrainingDummyController guardDummy = ConfigureGuardDummy(player, droneObject, guardProjectilePrefab);
        TutorialZoneTrigger exitZone = BuildExitZone(sceneMoveObject, player.transform);

        TutorialWorldMarker movementMarker = CreateWorldMarker(
            "TutorialMovementMarker",
            questGoal != null ? questGoal.transform : null,
            new Color(0.18f, 0.85f, 1f, 0.9f),
            new Vector3(1.15f, 0.025f, 1.15f),
            new Vector3(0f, 0.65f, 0f),
            new Vector3(0.10f, 0.35f, 0.10f),
            new Vector3(0f, 1.10f, 0f),
            new Vector3(0.28f, 0.12f, 0.28f));
        TutorialWorldMarker exitMarker = CreateWorldMarker(
            "TutorialExitMarker",
            exitZone != null ? exitZone.transform : null,
            new Color(1.00f, 0.68f, 0.18f, 0.92f),
            new Vector3(1.30f, 0.025f, 1.30f),
            new Vector3(0f, 0.78f, 0f),
            new Vector3(0.12f, 0.42f, 0.12f),
            new Vector3(0f, 1.32f, 0f),
            new Vector3(0.32f, 0.14f, 0.32f));
        TutorialWorldMarker attackDummyMarker = CreateWorldMarker(
            "TutorialAttackDummyMarker",
            attackDummyObject != null ? attackDummyObject.transform : null,
            new Color(0.24f, 0.88f, 1f, 0.92f),
            new Vector3(0.95f, 0.025f, 0.95f),
            new Vector3(0f, 0.72f, 0f),
            new Vector3(0.09f, 0.34f, 0.09f),
            new Vector3(0f, 1.22f, 0f),
            new Vector3(0.26f, 0.12f, 0.26f));
        TutorialWorldMarker guardDummyMarker = CreateWorldMarker(
            "TutorialGuardDummyMarker",
            droneObject != null ? droneObject.transform : null,
            new Color(1.00f, 0.45f, 0.18f, 0.92f),
            new Vector3(1.05f, 0.025f, 1.05f),
            new Vector3(0f, 0.78f, 0f),
            new Vector3(0.10f, 0.38f, 0.10f),
            new Vector3(0f, 1.28f, 0f),
            new Vector3(0.28f, 0.12f, 0.28f));

        if (attackDummy != null)
            attackDummy.ConfigureWorldMarker(attackDummyMarker);
        if (guardDummy != null)
            guardDummy.ConfigureWorldMarker(guardDummyMarker);

        TutorialScreenTargetIndicator focusIndicator = CreateScreenTargetIndicator(
            "TutorialFocusIndicator",
            tutorialCanvas,
            mainCamera,
            attackDummyObject != null ? attackDummyObject.transform : null,
            "\ud45c\uc801",
            new Color(0.24f, 0.88f, 1f, 0.96f));

        TutorialFocusGuidanceController focusGuidanceController = gameObject.AddComponent<TutorialFocusGuidanceController>();
        focusGuidanceController.ConfigureRuntime(
            _flowController,
            focusIndicator,
            attackDummyObject != null ? attackDummyObject.transform : null);

        TutorialPresentationController presentationController = gameObject.AddComponent<TutorialPresentationController>();
        presentationController.ConfigureRuntime(_flowController, _playerBridge, tutorialCanvas);

        TutorialDefenseFeedbackController defenseFeedbackController = gameObject.AddComponent<TutorialDefenseFeedbackController>();
        defenseFeedbackController.ConfigureRuntime(_flowController, guardDummy, tutorialCanvas);

        TutorialCombatCoachController combatCoachController = gameObject.AddComponent<TutorialCombatCoachController>();
        combatCoachController.ConfigureRuntime(_flowController, _egoGuideController, attackDummy, guardDummy);

        TutorialProgressCoachController progressCoachController = gameObject.AddComponent<TutorialProgressCoachController>();
        progressCoachController.ConfigureRuntime(
            _flowController,
            _egoGuideController,
            _playerBridge,
            attackDummyObject != null ? attackDummyObject.transform : null,
            movementGoalZone,
            exitZone);

        TutorialExitInteractableBridge exitInteractableBridge = gameObject.AddComponent<TutorialExitInteractableBridge>();
        exitInteractableBridge.ConfigureRuntime(
            _flowController,
            _egoGuideController,
            _hintBridge,
            exitZone,
            sceneMoveObject != null ? sceneMoveObject.GetComponent<SceneLoadInteractable>() : null);

        TutorialSupportFeedbackController supportFeedbackController = gameObject.AddComponent<TutorialSupportFeedbackController>();
        supportFeedbackController.ConfigureRuntime(
            _flowController,
            _playerBridge,
            tutorialCanvas,
            movementGoalZone != null ? movementGoalZone.transform : null,
            attackDummy != null ? attackDummy.transform : null,
            exitZone != null ? exitZone.transform : null);

        TutorialObjectivePanelController objectivePanelController = gameObject.AddComponent<TutorialObjectivePanelController>();
        objectivePanelController.ConfigureRuntime(_flowController, _hintBridge);

        TutorialGuideBeamController guideBeamController = gameObject.AddComponent<TutorialGuideBeamController>();
        guideBeamController.ConfigureRuntime(
            _flowController,
            player.transform,
            movementGoalZone != null ? movementGoalZone.transform : null,
            exitZone != null ? exitZone.transform : null,
            movementMarker,
            exitMarker);

        TutorialZoneHighlightController zoneHighlightController = gameObject.AddComponent<TutorialZoneHighlightController>();
        zoneHighlightController.ConfigureRuntime(_flowController, movementGoalZone, exitZone, player.transform);

        if (movementGoalZone != null)
            movementGoalZone.gameObject.SetActive(false);
        if (exitZone != null)
            exitZone.gameObject.SetActive(false);
        if (focusIndicator != null)
            focusIndicator.gameObject.SetActive(false);

        TutorialStepDefinition[] runtimeSteps = BuildRuntimeSteps(
            movementGoalZone,
            exitZone,
            attackDummy,
            guardDummy,
            focusIndicator);

        _flowController.ConfigureRuntime(
            _playerBridge,
            _egoGuideController,
            _hintBridge,
            runtimeSteps,
            new[] { attackDummy, guardDummy });
    }

    void DisableLegacyTutorialBehaviours(TutorialManager legacyTutorialManager)
    {
        if (legacyTutorialManager != null && legacyTutorialManager.comboGuidePanel != null)
            legacyTutorialManager.comboGuidePanel.SetActive(false);

        QuestTrigger questTrigger = FindFirstObjectByType<QuestTrigger>();
        if (questTrigger != null)
            questTrigger.enabled = false;

        TutorialDummy tutorialDummy = FindFirstObjectByType<TutorialDummy>();
        if (tutorialDummy != null)
            Destroy(tutorialDummy);

        TutorialDrone tutorialDrone = FindFirstObjectByType<TutorialDrone>();
        if (tutorialDrone != null)
            Destroy(tutorialDrone);

        AttackHitbox[] hitboxes = FindObjectsByType<AttackHitbox>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < hitboxes.Length; i++)
        {
            AttackHitbox hitbox = hitboxes[i];
            if (hitbox == null)
                continue;

            if (string.Equals(hitbox.gameObject.name, "Drone", StringComparison.OrdinalIgnoreCase))
            {
                hitbox.DeactivateWindow();
                hitbox.enabled = false;
            }
        }
    }

    TrainingDummyController ConfigureAttackDummy(GameObject player, GameObject dummyObject)
    {
        if (dummyObject == null)
            return null;

        TutorialEnemyVisualRig visualRig = PrepareTutorialLockOnTarget(dummyObject);
        TrainingDummyController controller = EnsureComponent<TrainingDummyController>(dummyObject);
        RemoveConflictingDamageReceivers(dummyObject, controller);
        Renderer renderer = visualRig != null && visualRig.PrimaryRenderer != null
            ? visualRig.PrimaryRenderer
            : dummyObject.GetComponentInChildren<Renderer>(true);
        controller.ConfigureRuntime(
            _playerBridge,
            TutorialDummyRole.PassiveTarget,
            dummyObject.transform,
            player.transform,
            renderer,
            null);
        controller.SetRuntimeProfiles(
            new TrainingDummyStepProfile { stepType = TutorialStepType.CameraFocus, active = true, loopAttack = false, invulnerable = true, stateColor = new Color(0.25f, 0.85f, 1f, 1f) },
            new TrainingDummyStepProfile { stepType = TutorialStepType.LockOn, active = true, loopAttack = false, invulnerable = true, stateColor = new Color(0.25f, 0.85f, 1f, 1f) },
            new TrainingDummyStepProfile { stepType = TutorialStepType.BasicAttack, active = true, loopAttack = false, invulnerable = true, stateColor = new Color(0.25f, 0.85f, 1f, 1f) },
            new TrainingDummyStepProfile { stepType = TutorialStepType.Combo, active = true, loopAttack = false, invulnerable = true, stateColor = new Color(0.25f, 0.85f, 1f, 1f) },
            new TrainingDummyStepProfile { stepType = TutorialStepType.Ultimate, active = true, loopAttack = false, invulnerable = true, stateColor = new Color(1.00f, 0.65f, 0.12f, 1f) });

        UltimateTargetSimple ultimateTarget = EnsureComponent<UltimateTargetSimple>(dummyObject);
        if (ultimateTarget != null)
        {
            ultimateTarget.ignoreDefense = true;
            ultimateTarget.maxHP = 99999;
            ultimateTarget.currentHP = 99999;
            ultimateTarget.destroyOnDeath = false;
        }

        dummyObject.SetActive(false);
        return controller;
    }

    TrainingDummyController ConfigureGuardDummy(GameObject player, GameObject dummyObject, GameObject projectilePrefab)
    {
        if (dummyObject == null)
            return null;

        TutorialEnemyVisualRig visualRig = PrepareTutorialLockOnTarget(dummyObject);
        Transform attackOrigin = dummyObject.transform;
        Transform firePoint = visualRig != null && visualRig.FirePoint != null
            ? visualRig.FirePoint
            : dummyObject.transform.Find("FirePoint");
        if (firePoint != null)
            attackOrigin = firePoint;

        TrainingDummyController controller = EnsureComponent<TrainingDummyController>(dummyObject);
        RemoveConflictingDamageReceivers(dummyObject, controller);
        Renderer renderer = visualRig != null && visualRig.PrimaryRenderer != null
            ? visualRig.PrimaryRenderer
            : dummyObject.GetComponentInChildren<Renderer>(true);
        controller.ConfigureRuntime(
            _playerBridge,
            TutorialDummyRole.GuardParry,
            attackOrigin,
            player.transform,
            renderer,
            null);
        controller.ConfigureProjectileRuntime(projectilePrefab, attackOrigin);
        controller.SetRuntimeProfiles(
            new TrainingDummyStepProfile
            {
                stepType = TutorialStepType.Guard,
                active = true,
                loopAttack = true,
                useProjectileAttack = true,
                initialDelay = 0.8f,
                attackInterval = 2.4f,
                telegraphDuration = 1.05f,
                damage = 8f,
                projectileSpeed = 10.5f,
                projectileLifeTime = 2.2f,
                countProjectileMissAsDodge = false,
                canParry = false,
                canPerfectDodge = false,
                unblockable = false,
                invulnerable = true,
                stateColor = new Color(0.10f, 0.78f, 1f, 1f),
                dangerIndicatorColor = new Color(1.00f, 0.45f, 0.18f, 0.95f),
                dangerIndicatorWidth = 0.2f,
                targetMarkerColor = new Color(1.00f, 0.42f, 0.14f, 0.72f),
                targetMarkerSize = 0.62f,
                enableAdaptiveAssist = true,
                assistStartAfterFailures = 2,
                maxAdaptiveFailureStacks = 2,
                adaptiveTelegraphBonusPerStack = 0.14f,
                adaptiveProjectileSpeedReductionPerStack = 0.12f,
                adaptiveTargetMarkerSizeBonusPerStack = 0.10f,
                adaptiveIndicatorWidthBonusPerStack = 0.025f
            },
            new TrainingDummyStepProfile
            {
                stepType = TutorialStepType.Parry,
                active = true,
                loopAttack = true,
                useProjectileAttack = true,
                initialDelay = 0.7f,
                attackInterval = 2.6f,
                telegraphDuration = 1.0f,
                damage = 8f,
                projectileSpeed = 11.5f,
                projectileLifeTime = 2.2f,
                countProjectileMissAsDodge = false,
                canParry = true,
                canPerfectDodge = false,
                unblockable = false,
                invulnerable = true,
                stateColor = new Color(0.18f, 1f, 1f, 1f),
                dangerIndicatorColor = new Color(0.22f, 0.95f, 1.00f, 0.95f),
                dangerIndicatorWidth = 0.2f,
                targetMarkerColor = new Color(0.22f, 0.95f, 1.00f, 0.72f),
                targetMarkerSize = 0.62f,
                enableAdaptiveAssist = true,
                assistStartAfterFailures = 2,
                maxAdaptiveFailureStacks = 3,
                adaptiveTelegraphBonusPerStack = 0.16f,
                adaptiveProjectileSpeedReductionPerStack = 0.14f,
                adaptiveTargetMarkerSizeBonusPerStack = 0.10f,
                adaptiveIndicatorWidthBonusPerStack = 0.025f
            },
            new TrainingDummyStepProfile
            {
                stepType = TutorialStepType.Dodge,
                active = true,
                loopAttack = true,
                useProjectileAttack = true,
                initialDelay = 0.7f,
                attackInterval = 2.4f,
                telegraphDuration = 0.9f,
                damage = 8f,
                projectileSpeed = 12.5f,
                projectileLifeTime = 2.4f,
                countProjectileMissAsDodge = true,
                canParry = false,
                canPerfectDodge = false,
                unblockable = true,
                invulnerable = true,
                stateColor = new Color(1.00f, 0.55f, 0.16f, 1f),
                dangerIndicatorColor = new Color(1.00f, 0.28f, 0.12f, 0.98f),
                dangerIndicatorWidth = 0.22f,
                targetMarkerColor = new Color(1.00f, 0.22f, 0.12f, 0.76f),
                targetMarkerSize = 0.7f,
                enableAdaptiveAssist = true,
                assistStartAfterFailures = 2,
                maxAdaptiveFailureStacks = 2,
                adaptiveTelegraphBonusPerStack = 0.12f,
                adaptiveProjectileSpeedReductionPerStack = 0.10f,
                adaptiveTargetMarkerSizeBonusPerStack = 0.10f,
                adaptiveIndicatorWidthBonusPerStack = 0.03f
            },
            new TrainingDummyStepProfile
            {
                stepType = TutorialStepType.PerfectDodge,
                active = true,
                loopAttack = true,
                useProjectileAttack = true,
                initialDelay = 0.7f,
                attackInterval = 2.5f,
                telegraphDuration = 0.95f,
                damage = 8f,
                projectileSpeed = 13.0f,
                projectileLifeTime = 2.4f,
                countProjectileMissAsDodge = true,
                canParry = false,
                canPerfectDodge = true,
                unblockable = true,
                invulnerable = true,
                stateColor = new Color(1.00f, 0.35f, 0.16f, 1f),
                dangerIndicatorColor = new Color(1.00f, 0.15f, 0.08f, 0.98f),
                dangerIndicatorWidth = 0.22f,
                targetMarkerColor = new Color(1.00f, 0.15f, 0.08f, 0.76f),
                targetMarkerSize = 0.7f,
                enableAdaptiveAssist = true,
                assistStartAfterFailures = 2,
                maxAdaptiveFailureStacks = 3,
                adaptiveTelegraphBonusPerStack = 0.14f,
                adaptiveProjectileSpeedReductionPerStack = 0.12f,
                adaptiveTargetMarkerSizeBonusPerStack = 0.12f,
                adaptiveIndicatorWidthBonusPerStack = 0.03f
            });

        dummyObject.SetActive(false);
        return controller;
    }

    TutorialWorldMarker CreateWorldMarker(
        string markerName,
        Transform target,
        Color color,
        Vector3 ringScale,
        Vector3 beamLocalPosition,
        Vector3 beamScale,
        Vector3 capLocalPosition,
        Vector3 capScale)
    {
        if (target == null)
            return null;

        Transform existing = target.Find(markerName);
        TutorialWorldMarker marker = existing != null ? existing.GetComponent<TutorialWorldMarker>() : null;
        if (marker == null)
        {
            GameObject markerObject = new GameObject(markerName);
            marker = markerObject.AddComponent<TutorialWorldMarker>();
        }

        marker.ConfigureRuntime(target, color, ringScale, beamLocalPosition, beamScale, capLocalPosition, capScale);
        marker.SetVisible(true);
        return marker;
    }

    TutorialScreenTargetIndicator CreateScreenTargetIndicator(
        string indicatorName,
        Canvas canvas,
        Camera targetCamera,
        Transform target,
        string label,
        Color color)
    {
        if (canvas == null || targetCamera == null || target == null)
            return null;

        Transform existing = canvas.transform.Find(indicatorName);
        TutorialScreenTargetIndicator indicator = existing != null ? existing.GetComponent<TutorialScreenTargetIndicator>() : null;
        if (indicator == null)
        {
            GameObject indicatorObject = new GameObject(indicatorName, typeof(RectTransform));
            indicatorObject.transform.SetParent(canvas.transform, false);
            indicator = indicatorObject.AddComponent<TutorialScreenTargetIndicator>();
        }

        indicator.ConfigureRuntime(targetCamera, canvas, target, label, color);
        return indicator;
    }

    Canvas ResolveTutorialCanvas(TutorialManager legacyTutorialManager)
    {
        if (legacyTutorialManager != null)
        {
            if (legacyTutorialManager.dialogueGroup != null)
            {
                Canvas canvas = legacyTutorialManager.dialogueGroup.GetComponentInParent<Canvas>();
                if (canvas != null)
                    return canvas;
            }

            if (legacyTutorialManager.questPanelGroup != null)
            {
                Canvas canvas = legacyTutorialManager.questPanelGroup.GetComponentInParent<Canvas>();
                if (canvas != null)
                    return canvas;
            }
        }

        return FindFirstObjectByType<Canvas>();
    }

    TutorialZoneTrigger BuildExitZone(GameObject sceneMoveObject, Transform playerRoot)
    {
        if (sceneMoveObject == null)
            return null;

        GameObject zoneObject = new GameObject("TutorialExitZone");
        zoneObject.transform.SetParent(sceneMoveObject.transform, false);
        zoneObject.transform.localPosition = Vector3.zero;
        zoneObject.transform.localRotation = Quaternion.identity;

        BoxCollider trigger = zoneObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(2.5f, 2.5f, 2.5f);

        TutorialZoneTrigger exitZone = zoneObject.AddComponent<TutorialZoneTrigger>();
        exitZone.ConfigureRuntime(playerRoot, "Player", true);
        return exitZone;
    }

    CollapseFloorTrigger BuildCollapseFloor(Transform playerTransform)
    {
        GameObject floorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floorObject.name = "TutorialCollapseFloor";
        floorObject.transform.SetParent(transform, false);
        floorObject.transform.position = playerTransform.position - playerTransform.forward * 1.6f + Vector3.down * 0.9f;
        floorObject.transform.localScale = new Vector3(4.5f, 0.4f, 4.5f);

        Renderer floorRenderer = floorObject.GetComponent<Renderer>();
        if (floorRenderer != null && floorRenderer.sharedMaterial != null)
            floorRenderer.sharedMaterial.color = new Color(0.12f, 0.55f, 0.82f, 1f);

        Rigidbody body = floorObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        CollapseFloorTrigger collapseFloor = floorObject.AddComponent<CollapseFloorTrigger>();
        collapseFloor.ConfigureRuntime(new[] { floorObject.transform }, 1.1f, 0.35f);
        return collapseFloor;
    }

    void BuildMovementFailZone(Transform playerTransform)
    {
        GameObject respawnAnchor = new GameObject("TutorialRespawnAnchor");
        respawnAnchor.transform.SetParent(transform, false);
        respawnAnchor.transform.position = playerTransform.position;
        respawnAnchor.transform.rotation = playerTransform.rotation;

        GameObject failZone = new GameObject("TutorialMovementFailZone");
        failZone.transform.SetParent(transform, false);
        failZone.transform.position = playerTransform.position - playerTransform.forward * 2.75f + Vector3.down * 1.25f;

        BoxCollider failCollider = failZone.AddComponent<BoxCollider>();
        failCollider.isTrigger = true;
        failCollider.size = new Vector3(5f, 3f, 4f);

        TutorialRespawnTrigger respawnTrigger = failZone.AddComponent<TutorialRespawnTrigger>();
        respawnTrigger.ConfigureRuntime(respawnAnchor.transform, "Player");
    }

    TutorialStepDefinition[] BuildRuntimeSteps(
        TutorialZoneTrigger movementGoalZone,
        TutorialZoneTrigger exitZone,
        TrainingDummyController attackDummy,
        TrainingDummyController guardDummy,
        TutorialScreenTargetIndicator focusIndicator)
    {
        List<TutorialStepDefinition> steps = new List<TutorialStepDefinition>();

        TutorialMovementConditionChecker movementChecker = gameObject.AddComponent<TutorialMovementConditionChecker>();
        movementChecker.ConfigureRuntime(_playerBridge.PlayerTransform, movementGoalZone, null, 0f);

        TutorialLookAtConditionChecker lookChecker = gameObject.AddComponent<TutorialLookAtConditionChecker>();
        lookChecker.ConfigureRuntime(_playerBridge.GameplayCamera, attackDummy != null ? attackDummy.transform : null);
        lookChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\ud654\uba74 \ubcf4\uc870 \ud45c\uc2dc\ub97c \ub530\ub77c \ud45c\uc801\uc744 \uc2dc\uc57c\uc5d0 \ub2f4\uc544.", 2.0f)
            },
            3f,
            true,
            6f);

        TutorialLockOnConditionChecker lockOnChecker = gameObject.AddComponent<TutorialLockOnConditionChecker>();
        lockOnChecker.ConfigureRuntime(_playerBridge, attackDummy != null ? attackDummy.transform : null);
        lockOnChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\ud45c\uc801\uc744 \ubc14\ub77c\ubcf4\uace0 \ub77d\uc628\uc744 \uc720\uc9c0\ud574.", 2.0f)
            },
            3f,
            true,
            6f);

        TutorialAttackConditionChecker attackChecker = gameObject.AddComponent<TutorialAttackConditionChecker>();
        attackChecker.ConfigureRuntime(new[] { attackDummy }, 10, 10);
        attackChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\uc88c\ud074\ub9ad\uacfc \uc6b0\ud074\ub9ad\uc744 \uac01\uac01 10\ud68c\uc529 \uc801\uc911\uc2dc\ucf1c.", 2.1f)
            },
            4f,
            false,
            6f);

        TutorialComboConditionChecker comboChecker = gameObject.AddComponent<TutorialComboConditionChecker>();
        comboChecker.ConfigureRuntime(new[] { attackDummy }, 3, 3, 1.15f);
        comboChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\ub04a\uae30\uba74 \ucc98\uc74c\ubd80\ud130\ub2e4. \ub9ac\ub4ec \uc788\uac8c \uc5f0\uc18d \uc785\ub825\ud574.", 2.1f)
            },
            4.5f,
            true,
            6f);

        TutorialGuardConditionChecker guardChecker = gameObject.AddComponent<TutorialGuardConditionChecker>();
        guardChecker.ConfigureRuntime(_playerBridge, 1);
        guardChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\ubd89\uc740 \uc120\uacfc \ub9c8\ucee4\uac00 \ubcf4\uc774\uba74 \uba3c\uc800 \ubc29\uc5b4\ud574.", 2.2f)
            },
            4f,
            true,
            6f);

        TutorialParryConditionChecker parryChecker = gameObject.AddComponent<TutorialParryConditionChecker>();
        parryChecker.ConfigureRuntime(_playerBridge, 1);
        parryChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\uc11c\ub450\ub974\uc9c0 \ub9c8. \uacbd\uace0\uac00 \uacb9\uce58\ub294 \uc21c\uac04\uc5d0 \ub9c9\uc544.", 2.2f)
            },
            4f,
            true,
            6f);

        TutorialDodgeConditionChecker dodgeChecker = gameObject.AddComponent<TutorialDodgeConditionChecker>();
        dodgeChecker.ConfigureRuntime(new[] { guardDummy }, 1, false);
        dodgeChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\ubc1b\uc544\ub0b4\uc9c0 \ub9d0\uace0 \ub9c8\ucee4 \ubc14\uae65\uc73c\ub85c \ube44\ucf1c.", 2.1f)
            },
            4f,
            true,
            6f);

        TutorialPerfectDodgeConditionChecker perfectChecker = gameObject.AddComponent<TutorialPerfectDodgeConditionChecker>();
        perfectChecker.ConfigureRuntime(_playerBridge, 1);
        perfectChecker.ConfigureRuntimeReminders(
            new[]
            {
                Guide(EGOGuideMessageType.FailureAssist, "\ud55c \ubc15\uc790 \ub2a6\uac8c. \ud22c\uc0ac\uccb4 \uc9c1\uc804\uc5d0 \ud68c\ud53c\ud574.", 2.1f)
            },
            4f,
            true,
            6f);

        TutorialHealConditionChecker healChecker = gameObject.AddComponent<TutorialHealConditionChecker>();
        healChecker.ConfigureRuntime(_playerBridge, 0.42f);

        TutorialUltimateConditionChecker ultimateChecker = gameObject.AddComponent<TutorialUltimateConditionChecker>();
        ultimateChecker.ConfigureRuntime(_playerBridge);

        TutorialZoneConditionChecker exitChecker = null;
        if (exitZone != null)
        {
            exitChecker = gameObject.AddComponent<TutorialZoneConditionChecker>();
            exitChecker.ConfigureRuntime(exitZone);
        }

        steps.Add(CreateStep(
            "movement",
            TutorialStepType.Movement,
            "\uc9c0\uc815 \uc704\uce58\ub85c \uc774\ub3d9\ud574",
            "\uc804\ubc29 \uccb4\ud06c \uc9c0\uc810\uae4c\uc9c0 \ubab8\uc744 \ud480\uc5b4",
            new[] { movementChecker },
            Guide(EGOGuideMessageType.Briefing, "\uac00\ubccd\uac8c \ubab8\uc744 \ud480\uc5b4. \uc804\ubc29 \uccb4\ud06c \uc9c0\uc810\uae4c\uc9c0 \uc774\ub3d9\ud574.", 2.2f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \ubab8\uc774 \ud480\ub9ac\uae30 \uc2dc\uc791\ud588\uc5b4.", 1.9f),
            new[] { movementGoalZone != null ? movementGoalZone.gameObject : null },
            null,
            null,
            new[] { movementGoalZone != null ? movementGoalZone.gameObject : null }));

        steps.Add(CreateStep(
            "camera",
            TutorialStepType.CameraFocus,
            "\uc801\uc744 \uc2dc\uc57c\uc5d0 \ub2f4\uc544",
            "\uc804\ud22c\ub294 \uc2dc\uc57c \ud655\ubcf4\ubd80\ud130 \uc2dc\uc791\ub3fc",
            new[] { lookChecker },
            Guide(EGOGuideMessageType.Briefing, "\ud45c\uc801\uc744 \ucc3e\uace0 \uc2dc\uc120\uc744 \ub9de\ucdb0. \uc804\ud669 \uc778\uc2dd\ubd80\ud130 \uc2dc\uc791\ud55c\ub2e4.", 2.4f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \ud45c\uc801 \uc778\uc2dd \uc644\ub8cc.", 1.7f),
            new[] { attackDummy != null ? attackDummy.gameObject : null, focusIndicator != null ? focusIndicator.gameObject : null },
            null,
            null,
            new[] { focusIndicator != null ? focusIndicator.gameObject : null }));

        steps.Add(CreateStep(
            "lockon",
            TutorialStepType.LockOn,
            "\ud45c\uc801\uc5d0 \uc9d1\uc911\ud574",
            "\ub77d\uc628\uc73c\ub85c \uc804\ud22c \ucd95\uc744 \uace0\uc815\ud574",
            new[] { lockOnChecker },
            Guide(EGOGuideMessageType.Tactical, "\uc2dc\uc120\uc744 \ubd99\uc7a1\uc544. \ud0c0\uac9f\uc744 \ub193\uce58\uc9c0 \ub9c8.", 2.0f),
            Guide(EGOGuideMessageType.Success, "\uace0\uc815 \uc88b\ub2e4. \uc774\uc81c \ubca0\uc5b4\ub0bc \uc218 \uc788\uc5b4.", 1.8f),
            new[] { attackDummy != null ? attackDummy.gameObject : null, focusIndicator != null ? focusIndicator.gameObject : null },
            null,
            null,
            new[] { focusIndicator != null ? focusIndicator.gameObject : null }));

        steps.Add(CreateStep(
            "basic_attack",
            TutorialStepType.BasicAttack,
            "\uc57d\uacf5\uacfc \uac15\uacf5\uc744 \uc11e\uc5b4",
            "\uc88c\ud074\ub9ad\uacfc \uc6b0\ud074\ub9ad\uc744 \uac01\uac01 10\ud68c \uc801\uc911\uc2dc\ucf1c",
            new[] { attackChecker },
            Guide(EGOGuideMessageType.Briefing, "\uba3c\uc800 \ubca0\uc5b4. \uc57d\uacf5\uacfc \uac15\uacf5\uc744 \uac01\uac01 10\ud68c\uc529 \ubc18\ubcf5\ud574 \uac10\uac01\uc744 \ub9de\ucdb0.", 2.5f),
            Guide(EGOGuideMessageType.Success, "\ud0c0\uaca9 \uac10\uac01 \ud655\uc778 \uc644\ub8cc.", 1.8f),
            new[] { attackDummy != null ? attackDummy.gameObject : null },
            null,
            null,
            null,
            true,
            "\uacf5\uaca9 \ubc30\uce58\ud45c",
            DefaultComboGuideText));

        steps.Add(CreateStep(
            "combo",
            TutorialStepType.Combo,
            "\ud750\ub984\uc744 \uc774\uc5b4\uac00",
            "\uc88c\uce21 \uc0c1\ub2e8 \ubc30\uce58\ud45c\ub97c \ubcf4\uace0 \uc5f0\uc18d \ud0c0\uaca9\ud574",
            new[] { comboChecker },
            Guide(EGOGuideMessageType.Briefing, "\ub04a\uc9c0 \ub9d0\uace0 \uc5f0\uc18d \uc785\ub825\uc73c\ub85c \ud750\ub984\uc744 \uc774\uc5b4.", 2.2f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \uc774 \ud15c\ud3ec\uba74 \uc2e4\uc804\uc5d0\uc11c\ub3c4 \uadf8\ub300\ub85c \uc774\uc5b4\uc9c4\ub2e4.", 2.1f),
            new[] { attackDummy != null ? attackDummy.gameObject : null },
            null,
            null,
            null,
            true,
            "\uacf5\uaca9 \ubc30\uce58\ud45c",
            DefaultComboGuideText));

        steps.Add(CreateStep(
            "guard",
            TutorialStepType.Guard,
            "\uc774\ubc88\uc5d4 \ubc1b\uc544\ub0b4",
            "\uacbd\uace0\uc120\uacfc \ub9c8\ucee4\ub97c \ubcf4\uace0 \ubc29\uc5b4\ud574",
            new[] { guardChecker },
            Guide(EGOGuideMessageType.Briefing, "\ubd89\uc740 \uacbd\uace0\uc120\uc774 \ub728\uba74 \ubc29\uc5b4\ud574. \uc774\ubc88\uc5d4 \uc548\uc815\uc801\uc73c\ub85c \ubc1b\uc544\ub0b8\ub2e4.", 2.6f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \ubc29\uc5b4 \ub9ac\ub4ec\uc774 \uc7a1\ud614\uc5b4.", 1.9f),
            new[] { guardDummy != null ? guardDummy.gameObject : null },
            new[] { attackDummy != null ? attackDummy.gameObject : null },
            null,
            null));

        steps.Add(CreateStep(
            "parry",
            TutorialStepType.Parry,
            "\uc815\ud655\ud558\uac8c \ub04a\uc5b4\ub0b4",
            "\ube5b\uacfc \ub9c8\ucee4\uac00 \uacb9\uce60 \ub54c \ub9c9\uc544",
            new[] { parryChecker },
            Guide(EGOGuideMessageType.Briefing, "\uc774\ubc88\uc5d4 \ud758\ub9ac\ub294 \uac8c \uc544\ub2c8\ub77c \ub04a\ub294 \uac70\ub2e4. \uacbd\uace0\uac00 \uacb9\uce60 \ub54c \ub9de\ucdb0 \ud299\uaca8\ub0b4.", 2.7f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc740 \ud0c0\uc774\ubc0d. \uadf8 \uac10\uac01\uc744 \uae30\uc5b5\ud574.", 1.9f),
            new[] { guardDummy != null ? guardDummy.gameObject : null },
            null,
            null,
            null));

        steps.Add(CreateStep(
            "dodge",
            TutorialStepType.Dodge,
            "\uc606\uc73c\ub85c \ud758\ub824",
            "\ub9c8\ucee4 \ubc14\uae65\uc73c\ub85c \ube60\uc838",
            new[] { dodgeChecker },
            Guide(EGOGuideMessageType.Briefing, "\uc774\uac74 \ubc1b\uc544\ub0b4\uc9c0 \ub9d0\uace0 \uacf5\uaca9 \ubc29\ud5a5\uc5d0\uc11c \ubc97\uc5b4\ub098\ub4ef \ud53c\ud574.", 2.6f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \ubc29\uc5b4\uc640 \ub2e4\ub978 \ub2f5\uc744 \uc120\ud0dd\ud588\uc5b4.", 2.0f),
            new[] { guardDummy != null ? guardDummy.gameObject : null },
            null,
            null,
            null));

        steps.Add(CreateStep(
            "perfect_dodge",
            TutorialStepType.PerfectDodge,
            "\uc9c0\uae08\uc774\uc57c",
            "\ub9c8\ucee4\uac00 \ub2ff\uae30 \uc9c1\uc804\uc5d0 \ud68c\ud53c\ud574",
            new[] { perfectChecker },
            Guide(EGOGuideMessageType.Briefing, "\ud22c\uc0ac\uccb4\uac00 \ub2ff\uae30 \uc9c1\uc804 \ud55c \ubc15\uc790 \ub2a6\uac8c \ud53c\ud574.", 2.5f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \uadf8\uac8c \ud37c\ud399\ud2b8 \ud68c\ud53c\ub2e4.", 1.8f),
            new[] { guardDummy != null ? guardDummy.gameObject : null },
            null,
            null,
            null));

        steps.Add(CreateStep(
            "heal",
            TutorialStepType.Heal,
            "\uc9c0\uae08 \ubcf4\ucda9\ud574",
            "\uc2e4\uc804\uc774\ub77c\uba74 \uc774 \ud310\ub2e8\ub3c4 \uc0dd\uc874\uc758 \uc77c\ubd80\ub2e4",
            new[] { healChecker },
            Guide(EGOGuideMessageType.Briefing, "\ube48\ud2c8\uc774 \uc0dd\uacbc\ub2e4. \uc9c0\uae08 \uc570\ud50c\uc744 \ud22c\uc785\ud574.", 2.0f),
            Guide(EGOGuideMessageType.Success, "\uc88b\uc544. \ud68c\ubcf5 \ud0c0\uc774\ubc0d \ud310\ub2e8\ub3c4 \uc804\uc220\uc774\uc57c.", 2.0f),
            null,
            new[] { guardDummy != null ? guardDummy.gameObject : null },
            null,
            null));

        steps.Add(CreateStep(
            "ultimate",
            TutorialStepType.Ultimate,
            "\uac8c\uc774\uc9c0\ub97c \ud138\uc5b4",
            "\uad81\uadf9\uae30\ub85c \uc804\ud22c \ub9ac\ub4ec\uc744 \ubc14\uafd4",
            new[] { ultimateChecker },
            Guide(EGOGuideMessageType.Briefing, "\ucda9\ubd84\ud558\ub2e4. \uc774\ubc88\uc5d4 \ud310 \uc790\uccb4\ub97c \ub4a4\uc9d1\uc790.", 2.1f),
            Guide(EGOGuideMessageType.Success, "\ud655\uc2e4\ud558\ub124. \ud074\ub77c\uc774\ub9e5\uc2a4 \uc6b4\uc6a9 \uac10\uac01\uae4c\uc9c0 \ud655\uc778\ub410\uc5b4.", 2.2f),
            new[] { attackDummy != null ? attackDummy.gameObject : null },
            null,
            null,
            null));

        if (exitChecker != null)
        {
            steps.Add(CreateStep(
                "exit",
                TutorialStepType.Exit,
                "\ud604\uc2e4\ub85c \ubcf5\uadc0\ud574",
                "\ucd9c\uad6c\ub85c \uc774\ub3d9\ud574 \ub2e4\uc74c \uad6c\uac04\uc5d0 \uc9c4\uc785\ud574",
                new[] { exitChecker },
                Guide(EGOGuideMessageType.Briefing, "\ud6c8\ub828 \uc885\ub8cc. \uc774\uc81c \ud604\uc2e4\ub85c \ub3cc\uc544\uac00.", 2.2f),
                Guide(EGOGuideMessageType.Success, "\uc900\ube44\ub294 \ub05d\ub0ac\ub2e4. \uc9c4\uc9dc\ub85c \uac04\ub2e4.", 2.0f),
                new[] { exitZone != null ? exitZone.gameObject : null },
                null,
                null,
                null));
        }

        return steps.ToArray();
    }

    TutorialStepDefinition CreateStep(
        string stepId,
        TutorialStepType stepType,
        string hint1,
        string hint2,
        TutorialConditionChecker[] checkers,
        TutorialGuideLine introLine,
        TutorialGuideLine successLine,
        GameObject[] activateOnStart,
        GameObject[] deactivateOnStart,
        GameObject[] activateOnComplete,
        GameObject[] deactivateOnComplete,
        bool showComboGuide = false,
        string comboGuideTitle = null,
        string comboGuideBody = null)
    {
        return new TutorialStepDefinition
        {
            stepId = stepId,
            stepType = stepType,
            hintLine1 = hint1,
            hintLine2 = hint2,
            introLines = new[] { introLine },
            successLines = new[] { successLine },
            showComboGuide = showComboGuide,
            comboGuideTitle = comboGuideTitle,
            comboGuideBody = comboGuideBody,
            autoAdvance = true,
            autoAdvanceDelay = 0.35f,
            requiredCheckers = checkers,
            activateOnStart = FilterNulls(activateOnStart),
            deactivateOnStart = FilterNulls(deactivateOnStart),
            activateOnComplete = FilterNulls(activateOnComplete),
            deactivateOnComplete = FilterNulls(deactivateOnComplete)
        };
    }

    TutorialGuideLine Guide(EGOGuideMessageType messageType, string text, float duration)
    {
        return new TutorialGuideLine
        {
            messageType = messageType,
            speaker = "EGO",
            text = text,
            duration = duration
        };
    }

    const string DefaultComboGuideText =
        "\uc57d\uacf5  LMB\n" +
        "\uac15\uacf5  RMB\n\n" +
        "\uae30\ubcf8  LMB > LMB > LMB > LMB\n" +
        "\ud655\uc7a5  LMB > LMB > LMB > RMB > RMB\n" +
        "\ud63c\ud569  LMB > RMB > LMB > RMB\n" +
        "\uaf2c\ub9ac  RMB > LMB > RMB\n" +
        "RMB > RMB > RMB";

    GameObject[] FilterNulls(GameObject[] values)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<GameObject>();

        List<GameObject> filtered = new List<GameObject>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != null)
                filtered.Add(values[i]);
        }

        return filtered.ToArray();
    }

    void RemoveConflictingDamageReceivers(GameObject target, TrainingDummyController keepReceiver)
    {
        if (target == null || keepReceiver == null)
            return;

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || ReferenceEquals(behaviour, keepReceiver))
                continue;

            if (behaviour is IDamageReceiver)
                Destroy(behaviour);
        }
    }

    TutorialEnemyVisualRig PrepareTutorialLockOnTarget(GameObject target)
    {
        if (target == null)
            return null;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0)
            ApplyLayerRecursively(target.transform, enemyLayer);

        TryAssignEnemyTag(target);
        TutorialEnemyVisualRig visualRig = EnsureComponent<TutorialEnemyVisualRig>(target);
        if (visualRig != null)
            visualRig.EnsureSetup();
        else
            EnsureRuntimeLockPivot(target);

        return visualRig;
    }

    void ApplyLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            ApplyLayerRecursively(root.GetChild(i), layer);
    }

    void TryAssignEnemyTag(GameObject target)
    {
        if (target == null)
            return;

        try
        {
            target.tag = "Enemy";
        }
        catch
        {
            // Tag not defined in project settings.
        }
    }

    void EnsureRuntimeLockPivot(GameObject target)
    {
        if (target == null || target.transform.Find("LockPivot") != null)
            return;

        Renderer renderer = target.GetComponentInChildren<Renderer>(true);
        Vector3 pivotPosition = target.transform.position + Vector3.up * 1.3f;
        if (renderer != null)
        {
            float centerY = renderer.bounds.center.y;
            float topY = renderer.bounds.max.y;
            float resolvedY = Mathf.Lerp(centerY, topY, 0.6f) - 0.1f;
            pivotPosition = new Vector3(renderer.bounds.center.x, resolvedY, renderer.bounds.center.z);
        }

        GameObject pivot = new GameObject("LockPivot");
        pivot.transform.SetParent(target.transform, true);
        pivot.transform.position = pivotPosition;
        pivot.transform.rotation = Quaternion.identity;
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        if (target == null)
            return null;

        T component = target.GetComponent<T>();
        if (component == null)
            component = target.AddComponent<T>();
        return component;
    }
}
