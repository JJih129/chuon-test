using System;
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BossCombatDataValidator
{
    const string MenuPath = "Tools/ChuOn/Boss/Validate Combat Data";
    const string RebuildMenuPath = "Tools/ChuOn/Boss/Rebuild Default Combat Data";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const float MinEffectiveSwordWaveRange = 7.0f;
    const float MaxEffectiveSwordWaveRange = 13.5f;
    const float MaxEffectiveSwordWaveWeight = 1.10f;

    [MenuItem(MenuPath)]
    static void ValidateSelectedOrSceneBoss()
    {
        BossController boss = ResolveLoadedBoss();
        ValidationReport report = Validate(boss);
        report.Emit(showDialog: true);
    }

    [MenuItem(RebuildMenuPath)]
    static void RebuildDefaultCombatData()
    {
        BossController boss = ResolveLoadedBoss();
        if (boss == null)
        {
            EditorUtility.DisplayDialog("Boss Combat Data", "BossController not found in current selection or loaded scenes.", "OK");
            return;
        }

        Undo.RecordObject(boss, "Rebuild Boss Default Combat Data");
        boss.RebuildDefaultCombatDataFromPatterns();
        EditorUtility.SetDirty(boss);

        if (boss.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);

        Validate(boss).Emit(showDialog: true);
    }

    public static string ValidateSelectedForFastMcp()
    {
        return RunFromFastMcp().Details;
    }

    public static ValidationSummary RunFromFastMcp()
    {
        BossController boss = ResolveLoadedBoss();

        if (boss != null)
            return Validate(boss).ToSummary();

        return ValidateMainSceneAsset().ToSummary();
    }

    static BossController ResolveLoadedBoss()
    {
        BossController boss = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<BossController>()
            : null;

        return boss != null
            ? boss
            : UnityEngine.Object.FindObjectOfType<BossController>(includeInactive: true);
    }

    static ValidationReport ValidateMainSceneAsset()
    {
        Scene scene = default;
        bool openedForValidation = false;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene loadedScene = SceneManager.GetSceneAt(i);
            if (!string.Equals(loadedScene.path, MainScenePath, StringComparison.Ordinal))
                continue;

            scene = loadedScene;
            break;
        }

        if (!scene.IsValid())
        {
            scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            openedForValidation = true;
        }

        BossController boss = FindBossInScene(scene);
        ValidationReport report = Validate(boss);

        if (openedForValidation && scene.IsValid())
            EditorSceneManager.CloseScene(scene, removeScene: true);

        return report;
    }

    static BossController FindBossInScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            BossController boss = roots[i].GetComponentInChildren<BossController>(includeInactive: true);
            if (boss != null)
                return boss;
        }

        return null;
    }

    static ValidationReport Validate(BossController boss)
    {
        ValidationReport report = new ValidationReport();
        if (boss == null)
        {
            report.Error("BossController not found in current selection or active scene.");
            return report;
        }

        if (boss.allPatterns == null || boss.allPatterns.Count == 0)
        {
            report.Error($"{boss.name}: allPatterns is empty.");
            return report;
        }

        SerializedObject serializedBoss = new SerializedObject(boss);
        SerializedProperty timingArray = serializedBoss.FindProperty("attackTimingData");
        bool usesTimingFallback = ReadBool(serializedBoss, "useDefaultAttackTimingFallback");
        bool usesPhaseFallback = ReadBool(serializedBoss, "useDefaultPhaseModifierFallback");
        bool autoCreatesTelegraphPresenter = ReadBool(serializedBoss, "autoCreateRuntimeTelegraphVfxPresenter");
        BossAttackTimingData[] timingData = ReadTimingData(timingArray);
        ValidateTimingArrayIntegrity(boss.name, timingData, boss.allPatterns, report);
        ValidateTelegraphVisualCoverage(boss, timingData, autoCreatesTelegraphPresenter, report);
        ValidateAttackTimingCueEffectCoverage(boss.name, serializedBoss, timingData, report);
        ValidatePostActionSettings(boss.name, serializedBoss.FindProperty("postActionSettings"), report);
        ValidateCombatRecoverySettings(boss.name, serializedBoss.FindProperty("combatRecoverySettings"), report);
        ValidateDifficultyProfiles(boss.name, serializedBoss.FindProperty("difficultyProfiles"), report);
        ValidateEngageSafetySettings(boss.name, serializedBoss, report);
        ValidatePlayerStateBiasSettings(boss.name, serializedBoss, report);
        ValidateSelectorObservationBiasModel(boss.name, report);
        ValidateSpatialResponseSettings(boss.name, serializedBoss, report);
        ValidateRuntimeSafetyMonitorSettings(boss, report);
        ValidateTelemetryRecorderSettings(boss, report);
        ValidateAttackFeedbackSettings(boss, report);
        ValidatePatternFamilyRepeatPolicy(boss.name, boss.allPatterns, report);

        for (int i = 0; i < boss.allPatterns.Count; i++)
        {
            AttackPattern pattern = boss.allPatterns[i];
            if (pattern == null)
            {
                report.Error($"{boss.name}: allPatterns[{i}] is null.");
                continue;
            }

            BossPatternId patternId = ResolvePatternId(pattern);
            string label = $"{boss.name}/{pattern.patternName} ({patternId})";
            ValidateSelectorData(label, pattern, patternId, boss.allPatterns, report);
            ValidateTimingData(label, boss, pattern, patternId, timingData, usesTimingFallback, report);
            ValidatePhaseModifiers(label, boss, pattern, patternId, boss.allPatterns, usesPhaseFallback, report);
        }

        ValidateCombatCoverage(boss.name, boss.allPatterns, report);
        ValidatePhaseCombatCoverage(boss.name, boss.allPatterns, report);
        ValidatePhaseDistanceCoverage(boss.name, boss.allPatterns, report);
        ValidatePlayerStateResponseCoverage(boss.name, boss.allPatterns, ReadBool(serializedBoss, "usePlayerStatePatternBias"), report);
        ValidateSelectorRuntimeSamples(boss.name, boss.allPatterns, report);
        ValidateAttackTimingControllerCleanup(boss.name, report);

        return report;
    }

    static void ValidateCombatCoverage(string bossName, System.Collections.Generic.List<AttackPattern> patterns, ValidationReport report)
    {
        bool hasClosePattern = false;
        bool hasMidDashPressure = false;
        bool hasRangedPressure = false;
        bool hasApproachEngage = false;

        for (int i = 0; i < patterns.Count; i++)
        {
            AttackPattern pattern = patterns[i];
            if (pattern == null)
                continue;

            BossPatternId patternId = ResolvePatternId(pattern);
            BossPatternData data = pattern.selectorData;
            bool usesAutoData = data.patternId == BossPatternId.None;
            float minRange = usesAutoData ? pattern.minRange : data.minRange;
            float maxRange = usesAutoData || data.maxRange <= data.minRange ? pattern.maxRange : data.maxRange;
            BossPatternEngageMode engageMode = usesAutoData ? ResolveDefaultEngageMode(patternId) : data.engageMode;

            if (minRange <= 0.5f && maxRange >= 2f)
                hasClosePattern = true;

            if (patternId == BossPatternId.DashSlash && maxRange >= 5f)
                hasMidDashPressure = true;

            if (patternId == BossPatternId.SwordWave && maxRange >= 8f)
                hasRangedPressure = true;

            if (engageMode == BossPatternEngageMode.ChaseUntilInRange ||
                engageMode == BossPatternEngageMode.DashEngage ||
                engageMode == BossPatternEngageMode.UseRangedFallback)
            {
                hasApproachEngage = true;
            }
        }

        if (!hasClosePattern)
            report.Warn($"{bossName}: no close-range attack coverage detected.");
        if (!hasMidDashPressure)
            report.Warn($"{bossName}: no mid-range DashSlash pressure coverage detected.");
        if (!hasRangedPressure)
            report.Warn($"{bossName}: no long-range SwordWave pressure coverage detected.");
        if (!hasApproachEngage)
            report.Warn($"{bossName}: no engage mode coverage detected. Range-out behavior may fall back to Move/CombatIdle only.");
    }

    static void ValidatePhaseCombatCoverage(string bossName, System.Collections.Generic.List<AttackPattern> patterns, ValidationReport report)
    {
        for (int phase = 1; phase <= 3; phase++)
        {
            bool hasAnyPattern = false;
            bool hasFallbackPattern = false;
            bool hasClosePattern = false;
            bool hasRangedPressure = false;
            bool hasEngagePressure = false;

            for (int i = 0; i < patterns.Count; i++)
            {
                AttackPattern pattern = patterns[i];
                if (pattern == null || !pattern.IsAvailableInPhase(phase))
                    continue;

                BossPatternId patternId = ResolvePatternId(pattern);
                BossPatternData data = pattern.selectorData;
                bool usesAutoData = data.patternId == BossPatternId.None;
                int minPhase = usesAutoData || data.minPhase <= 0 ? pattern.ResolveMinPhase() : Mathf.Clamp(data.minPhase, 1, 3);
                int maxPhase = usesAutoData || data.maxPhase <= 0 ? pattern.ResolveMaxPhase() : Mathf.Clamp(data.maxPhase, minPhase, 3);
                if (phase < minPhase || phase > maxPhase)
                    continue;

                float minRange = usesAutoData ? pattern.minRange : data.minRange;
                float maxRange = usesAutoData || data.maxRange <= data.minRange ? pattern.maxRange : data.maxRange;
                BossPatternEngageMode engageMode = usesAutoData ? ResolveDefaultEngageMode(patternId) : data.engageMode;

                hasAnyPattern = true;
                hasFallbackPattern |= usesAutoData || data.isFallback;
                hasClosePattern |= minRange <= 0.5f && maxRange >= 2f;
                hasRangedPressure |= patternId == BossPatternId.SwordWave && maxRange >= 8f;
                hasEngagePressure |= engageMode == BossPatternEngageMode.ChaseUntilInRange ||
                                     engageMode == BossPatternEngageMode.DashEngage ||
                                     engageMode == BossPatternEngageMode.UseRangedFallback;
            }

            if (!hasAnyPattern)
            {
                report.Error($"{bossName}: phase {phase} has no available boss pattern.");
                continue;
            }

            if (!hasFallbackPattern)
                report.Error($"{bossName}: phase {phase} has no fallback pattern. Phase transition can leave the boss without a safe attack choice.");
            if (!hasClosePattern)
                report.Warn($"{bossName}: phase {phase} has no close-range attack coverage.");
            if (phase >= 2 && !hasRangedPressure)
                report.Warn($"{bossName}: phase {phase} has no ranged pressure coverage.");
            if (phase >= 2 && !hasEngagePressure)
                report.Warn($"{bossName}: phase {phase} has no engage pressure coverage.");
        }
    }

    static void ValidatePhaseDistanceCoverage(string bossName, System.Collections.Generic.List<AttackPattern> patterns, ValidationReport report)
    {
        ValidateDistanceBand(bossName, patterns, 1.5f, "close", requirePressure: false, report);
        ValidateDistanceBand(bossName, patterns, 5.0f, "mid", requirePressure: true, report);
        ValidateDistanceBand(bossName, patterns, 10.0f, "far", requirePressure: true, report);
    }

    static void ValidatePlayerStateResponseCoverage(
        string bossName,
        System.Collections.Generic.List<AttackPattern> patterns,
        bool usesPlayerStateBias,
        ValidationReport report)
    {
        if (!usesPlayerStateBias || patterns == null || patterns.Count == 0)
            return;

        bool hasFarPressure = false;
        bool hasChasePressure = false;
        bool hasClosePunish = false;
        bool hasGuardPunish = false;
        bool hasAttackCounter = false;
        bool hasDefenseMixup = false;

        for (int i = 0; i < patterns.Count; i++)
        {
            AttackPattern pattern = patterns[i];
            if (pattern == null)
                continue;

            BossPatternData data = ResolveValidationSelectorData(pattern);
            BossPatternId patternId = data.patternId;
            if (patternId == BossPatternId.None)
                continue;

            bool pressuresAtRange = data.maxRange >= 5f || data.engageStartRange >= 5f;
            bool isEngagePressure = data.engageMode == BossPatternEngageMode.ChaseUntilInRange ||
                                    data.engageMode == BossPatternEngageMode.DashEngage ||
                                    data.engageMode == BossPatternEngageMode.UseRangedFallback;

            hasFarPressure |= pressuresAtRange &&
                              (patternId == BossPatternId.SwordWave ||
                               patternId == BossPatternId.DashSlash ||
                               isEngagePressure);
            hasChasePressure |= isEngagePressure;
            hasClosePunish |= data.minRange <= 2.5f &&
                              data.maxRange >= 2f &&
                              (patternId == BossPatternId.HeavySlash ||
                               patternId == BossPatternId.BackstepSlash);
            hasGuardPunish |= patternId == BossPatternId.HeavySlash ||
                              patternId == BossPatternId.BackstepSlash ||
                              patternId == BossPatternId.DashSlash;
            hasAttackCounter |= data.minRange <= 3f &&
                                (patternId == BossPatternId.QuickSlash ||
                                 patternId == BossPatternId.DashSlash ||
                                 patternId == BossPatternId.BackstepSlash);
            hasDefenseMixup |= patternId == BossPatternId.HeavySlash ||
                               patternId == BossPatternId.DashSlash ||
                               patternId == BossPatternId.SwordWave;
        }

        if (!hasFarPressure)
            report.Warn($"{bossName}: player-state bias is enabled, but no far-player pressure pattern pool was detected.");
        if (!hasChasePressure)
            report.Warn($"{bossName}: player-state bias is enabled, but no chase/engage pressure pattern pool was detected.");
        if (!hasClosePunish)
            report.Warn($"{bossName}: player-state bias is enabled, but no close-linger punish pattern pool was detected.");
        if (!hasGuardPunish)
            report.Warn($"{bossName}: player-state bias is enabled, but no guard-punish pattern pool was detected.");
        if (!hasAttackCounter)
            report.Warn($"{bossName}: player-state bias is enabled, but no player-attack counter pattern pool was detected.");
        if (!hasDefenseMixup)
            report.Warn($"{bossName}: player-state bias is enabled, but no repeated-defense mixup pattern pool was detected.");
    }

    static void ValidateDistanceBand(
        string bossName,
        System.Collections.Generic.List<AttackPattern> patterns,
        float distance,
        string bandName,
        bool requirePressure,
        ValidationReport report)
    {
        for (int phase = 1; phase <= 3; phase++)
        {
            bool hasImmediatePattern = false;
            bool hasEngagePattern = false;
            bool hasFallbackPattern = false;

            for (int i = 0; i < patterns.Count; i++)
            {
                AttackPattern pattern = patterns[i];
                if (pattern == null || !pattern.IsAvailableInPhase(phase))
                    continue;

                BossPatternData data = ResolveValidationSelectorData(pattern);
                if (data.patternId == BossPatternId.None || phase < data.minPhase || phase > data.maxPhase)
                    continue;

                bool inRange = distance >= data.minRange && distance <= data.maxRange;
                hasImmediatePattern |= inRange;
                hasFallbackPattern |= inRange && data.isFallback;
                hasEngagePattern |= !inRange &&
                                    distance > data.maxRange &&
                                    distance <= data.engageStartRange &&
                                    (data.engageMode == BossPatternEngageMode.ChaseUntilInRange ||
                                     data.engageMode == BossPatternEngageMode.DashEngage ||
                                     data.engageMode == BossPatternEngageMode.UseRangedFallback);
            }

            if (phase == 1 && requirePressure)
                continue;

            if (!hasImmediatePattern && !hasEngagePattern)
                report.Warn($"{bossName}: phase {phase} has no {bandName}-range pattern or engage coverage at {distance:0.0}m.");
            if (requirePressure && !hasFallbackPattern && !hasEngagePattern)
                report.Warn($"{bossName}: phase {phase} {bandName}-range pressure has no fallback/engage safety at {distance:0.0}m.");
        }
    }

    static void ValidateSelectorRuntimeSamples(string bossName, System.Collections.Generic.List<AttackPattern> patterns, ValidationReport report)
    {
        if (patterns == null || patterns.Count == 0)
            return;

        BossPatternData[] selectorData = new BossPatternData[patterns.Count];
        BossPatternRuntimeState[] runtimeStates = new BossPatternRuntimeState[patterns.Count];

        for (int i = 0; i < patterns.Count; i++)
        {
            AttackPattern pattern = patterns[i];
            if (pattern == null)
                continue;

            selectorData[i] = ResolveValidationSelectorData(pattern);
            runtimeStates[i] = new BossPatternRuntimeState(selectorData[i].patternId);
        }

        BossPatternSelector selector = new BossPatternSelector();
        selector.Configure(selectorData, runtimeStates, new BossPatternHistory(), null);

        ValidateSelectorSample(bossName, selector, selectorData, 1, 1.5f, "phase1-close", report);
        ValidateSelectorSample(bossName, selector, selectorData, 1, 5f, "phase1-mid", report);
        ValidateSelectorSample(bossName, selector, selectorData, 2, 1.5f, "phase2-close", report);
        ValidateSelectorSample(bossName, selector, selectorData, 2, 5f, "phase2-mid", report);
        ValidateSelectorSample(bossName, selector, selectorData, 2, 10f, "phase2-far", report);
        ValidateSelectorSample(bossName, selector, selectorData, 3, 1.5f, "phase3-close", report);
        ValidateSelectorSample(bossName, selector, selectorData, 3, 5f, "phase3-mid", report);
        ValidateSelectorSample(bossName, selector, selectorData, 3, 10f, "phase3-far", report);
        ValidateSelectorObservationDistributionSamples(bossName, selector, report);
    }

    static void ValidateSelectorSample(
        string bossName,
        BossPatternSelector selector,
        BossPatternData[] selectorData,
        int phase,
        float distance,
        string label,
        ValidationReport report)
    {
        BossPatternContext context = new BossPatternContext(distance, 0f, phase, 9999f);
        if (!selector.TrySelect(context, out int selectedIndex, out BossPatternId selectedPatternId))
        {
            report.Warn($"{bossName}: selector sample {label} produced no pattern at {distance:0.0}m.");
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= selectorData.Length || selectorData[selectedIndex].patternId != selectedPatternId)
            report.Error($"{bossName}: selector sample {label} returned inconsistent index/id ({selectedIndex}, {selectedPatternId}).");
    }

    static void ValidateSelectorObservationDistributionSamples(
        string bossName,
        BossPatternSelector selector,
        ValidationReport report)
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(982451653);

        BossPlayerCombatObservation movingAway = CreateObservation(8f);
        movingAway.playerIsMovingAway = true;
        ValidateDistributionSample(
            bossName,
            selector,
            new BossPatternContext(8f, 0f, 2, 9999f, movingAway),
            "moving-away far pressure",
            MatchesFarPressure,
            report);

        BossPlayerCombatObservation closePressure = CreateObservation(1.8f);
        closePressure.playerIsApproaching = true;
        closePressure.playerRecentlyHitBoss = true;
        ValidateDistributionSample(
            bossName,
            selector,
            new BossPatternContext(1.8f, 0f, 2, 9999f, closePressure),
            "close-pressure punish",
            MatchesClosePunish,
            report);

        BossPlayerCombatObservation guarding = CreateObservation(2f);
        guarding.playerIsGuarding = true;
        ValidateDistributionSample(
            bossName,
            selector,
            new BossPatternContext(2f, 0f, 2, 9999f, guarding),
            "guard punish",
            MatchesGuardPunish,
            report);

        BossPlayerCombatObservation attacking = CreateObservation(2f);
        attacking.playerIsAttacking = true;
        ValidateDistributionSample(
            bossName,
            selector,
            new BossPatternContext(2f, 0f, 2, 9999f, attacking),
            "attack counter",
            MatchesAttackCounter,
            report);

        BossPlayerCombatObservation stunned = CreateObservation(1.6f);
        stunned.playerIsStunned = true;
        ValidateDistributionSample(
            bossName,
            selector,
            new BossPatternContext(1.6f, 0f, 2, 9999f, stunned),
            "stunned close punish",
            MatchesStunnedPunish,
            report);

        UnityEngine.Random.state = previousRandomState;
    }

    delegate bool PatternCategoryMatcher(BossPatternId patternId);

    static void ValidateDistributionSample(
        string bossName,
        BossPatternSelector selector,
        BossPatternContext context,
        string label,
        PatternCategoryMatcher matcher,
        ValidationReport report)
    {
        const int SampleCount = 48;
        int matchingCount = 0;
        int selectedCount = 0;

        for (int i = 0; i < SampleCount; i++)
        {
            if (!selector.TrySelect(context, out _, out BossPatternId selectedPatternId))
                continue;

            selectedCount++;
            if (matcher(selectedPatternId))
                matchingCount++;
        }

        if (selectedCount == 0)
        {
            report.Warn($"{bossName}: selector observation sample '{label}' produced no pattern.");
            return;
        }

        if (matchingCount == 0)
            report.Warn($"{bossName}: selector observation sample '{label}' never selected a matching response in {selectedCount} selections.");
    }

    static bool MatchesFarPressure(BossPatternId patternId)
    {
        return patternId == BossPatternId.SwordWave || patternId == BossPatternId.DashSlash;
    }

    static bool MatchesClosePunish(BossPatternId patternId)
    {
        return patternId == BossPatternId.HeavySlash || patternId == BossPatternId.BackstepSlash;
    }

    static bool MatchesGuardPunish(BossPatternId patternId)
    {
        return patternId == BossPatternId.HeavySlash ||
               patternId == BossPatternId.BackstepSlash ||
               patternId == BossPatternId.DashSlash;
    }

    static bool MatchesAttackCounter(BossPatternId patternId)
    {
        return patternId == BossPatternId.QuickSlash ||
               patternId == BossPatternId.DashSlash ||
               patternId == BossPatternId.BackstepSlash;
    }

    static bool MatchesStunnedPunish(BossPatternId patternId)
    {
        return patternId == BossPatternId.QuickSlash || patternId == BossPatternId.HeavySlash;
    }

    static void ValidateAttackTimingControllerCleanup(string bossName, ValidationReport report)
    {
        BossAttackTimingData data = new BossAttackTimingData
        {
            patternId = BossPatternId.QuickSlash,
            telegraphType = BossAttackTelegraphType.Parryable,
            useTimingDataHitboxControl = true,
            hitboxOpenTime = 0f,
            hitboxCloseTime = 0.4f,
            parryWindowStartTime = 0f,
            parryWindowEndTime = 0.35f,
            recoveryDuration = 0.45f
        };

        int hitboxOpenCount = 0;
        int hitboxCloseCount = 0;
        int parryOpenCount = 0;
        int parryCloseCount = 0;

        BossAttackTimingController controller = new BossAttackTimingController();
        IEnumerator routine = controller.Execute(
            data,
            () => true,
            (type, duration) => { },
            () => hitboxOpenCount++,
            () => hitboxCloseCount++,
            () => parryOpenCount++,
            () => parryCloseCount++,
            false,
            null);

        bool started = routine.MoveNext();
        (routine as IDisposable)?.Dispose();

        if (!started)
            report.Error($"{bossName}: attack timing cleanup probe did not start.");
        if (hitboxOpenCount != 1 || hitboxCloseCount != 1)
            report.Error($"{bossName}: attack timing cleanup probe failed hitbox close guarantee ({hitboxOpenCount}/{hitboxCloseCount}).");
        if (parryOpenCount != 1 || parryCloseCount != 1)
            report.Error($"{bossName}: attack timing cleanup probe failed parry close guarantee ({parryOpenCount}/{parryCloseCount}).");
    }

    static BossPatternData ResolveValidationSelectorData(AttackPattern pattern)
    {
        BossPatternData data = pattern.selectorData;
        bool usesAutoData = data.patternId == BossPatternId.None;
        if (usesAutoData)
            data.patternId = ResolvePatternId(pattern);

        data.minRange = usesAutoData || data.maxRange <= data.minRange ? Mathf.Max(0f, pattern.minRange) : Mathf.Max(0f, data.minRange);
        data.maxRange = usesAutoData || data.maxRange <= data.minRange ? Mathf.Max(data.minRange + 0.1f, pattern.maxRange) : Mathf.Max(data.minRange + 0.1f, data.maxRange);
        data.minAngle = Mathf.Clamp(data.minAngle, 0f, 180f);
        data.maxAngle = data.maxAngle <= data.minAngle ? 180f : Mathf.Clamp(data.maxAngle, data.minAngle, 180f);
        data.baseWeight = usesAutoData || data.baseWeight <= 0f ? Mathf.Max(0.01f, pattern.weight) : Mathf.Max(0.01f, data.baseWeight);
        data.cooldown = usesAutoData || data.cooldown <= 0f ? Mathf.Max(0f, pattern.cooldown) : Mathf.Max(0f, data.cooldown);
        data.minPhase = usesAutoData || data.minPhase <= 0 ? pattern.ResolveMinPhase() : Mathf.Clamp(data.minPhase, 1, 3);
        data.maxPhase = usesAutoData || data.maxPhase <= 0 ? pattern.ResolveMaxPhase() : Mathf.Clamp(data.maxPhase, data.minPhase, 3);
        data.recentRepeatBlockCount = data.recentRepeatBlockCount <= 0 ? 1 : Mathf.Clamp(data.recentRepeatBlockCount, 1, 8);

        if (usesAutoData)
        {
            data.canRepeat = AllowsPatternFamilyVariantRepeat(data.patternId);
            data.isFallback = true;
            data.fallbackPriority = ResolveFallbackPriority(data.patternId);
            data.postActionType = ResolveDefaultPostAction(data.patternId);
            data.engageMode = ResolveDefaultEngageMode(data.patternId);
            data.engageStartRange = ResolveDefaultEngageStartRange(data.patternId, data.maxRange);
            data.engageStopRange = ResolveDefaultEngageStopRange(data.patternId, data.minRange, data.maxRange);
            data.engageMaxDuration = ResolveDefaultEngageDuration(data.patternId);
            data.engageMoveSpeedMultiplier = ResolveDefaultEngageSpeed(data.patternId);
            data.rangedFallbackPatternId = BossPatternId.SwordWave;
            data.dashFallbackPatternId = BossPatternId.DashSlash;
        }

        data.engageStartRange = data.engageStartRange <= 0f ? ResolveDefaultEngageStartRange(data.patternId, data.maxRange) : Mathf.Max(data.maxRange, data.engageStartRange);
        data.engageStopRange = data.engageStopRange <= 0f ? ResolveDefaultEngageStopRange(data.patternId, data.minRange, data.maxRange) : Mathf.Clamp(data.engageStopRange, data.minRange, data.maxRange);
        data.engageMaxDuration = data.engageMaxDuration <= 0f ? ResolveDefaultEngageDuration(data.patternId) : Mathf.Max(0.05f, data.engageMaxDuration);
        data.engageMoveSpeedMultiplier = data.engageMoveSpeedMultiplier <= 0f ? ResolveDefaultEngageSpeed(data.patternId) : Mathf.Clamp(data.engageMoveSpeedMultiplier, 0.15f, 2.5f);
        ApplySwordWaveRuntimeTuning(ref data);
        return data;
    }

    static void ValidateSelectorData(
        string label,
        AttackPattern pattern,
        BossPatternId patternId,
        System.Collections.Generic.List<AttackPattern> allPatterns,
        ValidationReport report)
    {
        if (patternId == BossPatternId.None)
            report.Warn($"{label}: patternId is None and cannot be validated strongly.");

        if (pattern.minRange < 0f || pattern.maxRange <= pattern.minRange)
            report.Error($"{label}: invalid range min={pattern.minRange:0.00}, max={pattern.maxRange:0.00}.");

        BossPatternData data = pattern.selectorData;
        if (data.patternId != BossPatternId.None)
        {
            if (data.maxRange <= data.minRange)
                report.Error($"{label}: selectorData range is invalid.");

            if (data.baseWeight < 0f)
                report.Error($"{label}: selectorData baseWeight cannot be negative.");
            if (data.cooldown < 0f)
                report.Error($"{label}: selectorData cooldown cannot be negative.");
            if (data.minAngle < 0f || data.maxAngle > 180f || data.maxAngle < data.minAngle)
                report.Error($"{label}: selectorData angle range must be 0..180 and max >= min.");

            if (data.minPhase < 1 || data.maxPhase < data.minPhase || data.maxPhase > 3)
                report.Error($"{label}: selectorData phase range is invalid.");

            if (data.engageMode == BossPatternEngageMode.ChaseUntilInRange || data.engageMode == BossPatternEngageMode.DashEngage)
            {
                if (data.engageMaxDuration <= 0f)
                    report.Warn($"{label}: engage mode needs positive engageMaxDuration.");
                if (data.engageStartRange <= data.maxRange)
                    report.Warn($"{label}: engageStartRange should be greater than maxRange for approach pressure.");
                if (data.engageMoveSpeedMultiplier <= 0f)
                    report.Error($"{label}: engageMoveSpeedMultiplier must be positive.");
            }

            if (data.engageStopRange > 0f && (data.engageStopRange < data.minRange || data.engageStopRange > data.maxRange))
                report.Warn($"{label}: engageStopRange should stay inside selectorData min/max range.");

            if (data.rangedFallbackPatternId != BossPatternId.None && !HasPatternId(allPatterns, data.rangedFallbackPatternId))
                report.Error($"{label}: rangedFallbackPatternId {data.rangedFallbackPatternId} has no matching pattern.");

            if (data.dashFallbackPatternId != BossPatternId.None && !HasPatternId(allPatterns, data.dashFallbackPatternId))
                report.Error($"{label}: dashFallbackPatternId {data.dashFallbackPatternId} has no matching pattern.");

            if (data.engageMode == BossPatternEngageMode.UseRangedFallback && data.rangedFallbackPatternId == BossPatternId.None)
                report.Warn($"{label}: engageMode is UseRangedFallback, but rangedFallbackPatternId is None.");

            if (data.engageMode == BossPatternEngageMode.DashEngage && data.dashFallbackPatternId == BossPatternId.None)
                report.Warn($"{label}: engageMode is DashEngage, but dashFallbackPatternId is None.");
        }
    }

    static void ValidatePatternFamilyRepeatPolicy(
        string bossName,
        System.Collections.Generic.List<AttackPattern> patterns,
        ValidationReport report)
    {
        if (patterns == null || patterns.Count <= 1)
            return;

        ValidatePatternFamilyRepeatPolicy(bossName, patterns, BossPatternId.QuickSlash, report);
        ValidatePatternFamilyRepeatPolicy(bossName, patterns, BossPatternId.HeavySlash, report);
    }

    static void ValidatePatternFamilyRepeatPolicy(
        string bossName,
        System.Collections.Generic.List<AttackPattern> patterns,
        BossPatternId patternId,
        ValidationReport report)
    {
        int variantCount = 0;
        bool hasExplicitFamilyBlock = false;

        for (int i = 0; i < patterns.Count; i++)
        {
            AttackPattern pattern = patterns[i];
            if (pattern == null || ResolvePatternId(pattern) != patternId)
                continue;

            variantCount++;
            if (pattern.selectorData.patternId != BossPatternId.None && !pattern.selectorData.canRepeat)
                hasExplicitFamilyBlock = true;
        }

        if (variantCount > 1 && hasExplicitFamilyBlock)
            report.Warn($"{bossName}: {patternId} has {variantCount} variants, but explicit selectorData canRepeat=false may block sibling variants through selector history.");
    }

    static void ValidateTimingData(
        string label,
        BossController boss,
        AttackPattern pattern,
        BossPatternId patternId,
        BossAttackTimingData[] timingData,
        bool usesTimingFallback,
        ValidationReport report)
    {
        if (patternId == BossPatternId.None)
            return;

        bool found = false;
        for (int i = 0; i < timingData.Length; i++)
        {
            if (timingData[i].patternId != patternId)
                continue;

            if (found)
                report.Warn($"{label}: duplicate attackTimingData entry for {patternId}.");

            found = true;
            ValidateTimingValues(label, timingData[i], report);
        }

        if (found)
            return;

        if (!usesTimingFallback)
        {
            report.Warn($"{label}: missing attackTimingData entry. Existing animation events will be used. Run '{RebuildMenuPath}' on the boss to generate editable defaults.");
            return;
        }

        if (boss != null && boss.TryGetResolvedAttackTimingDataForValidation(pattern, out BossAttackTimingData resolvedTiming))
            ValidateTimingValues($"{label}/fallback", resolvedTiming, report);
        else
            report.Warn($"{label}: attackTimingData fallback is enabled, but no usable timing data was resolved.");
    }

    static void ValidateTimingValues(string label, BossAttackTimingData timingData, ValidationReport report)
    {
        if (timingData.telegraphStartTime < 0f)
            report.Error($"{label}: attackTimingData telegraphStartTime cannot be negative.");
        if (timingData.telegraphDuration < 0f)
            report.Error($"{label}: attackTimingData telegraphDuration cannot be negative.");
        if (timingData.hitboxOpenTime < 0f)
            report.Error($"{label}: attackTimingData hitboxOpenTime cannot be negative.");
        if (timingData.hitboxCloseTime < 0f)
            report.Error($"{label}: attackTimingData hitboxCloseTime cannot be negative.");
        if (timingData.parryWindowStartTime < 0f)
            report.Error($"{label}: attackTimingData parryWindowStartTime cannot be negative.");
        if (timingData.parryWindowEndTime < 0f)
            report.Error($"{label}: attackTimingData parryWindowEndTime cannot be negative.");
        if (timingData.recoveryStartTime < 0f)
            report.Error($"{label}: attackTimingData recoveryStartTime cannot be negative.");
        if (timingData.recoveryDuration < 0f)
            report.Error($"{label}: attackTimingData recoveryDuration cannot be negative.");

        if (timingData.hitboxCloseTime < timingData.hitboxOpenTime)
            report.Error($"{label}: attackTimingData hitboxCloseTime is earlier than hitboxOpenTime.");
        if (timingData.parryWindowEndTime < timingData.parryWindowStartTime)
            report.Error($"{label}: attackTimingData parryWindowEndTime is earlier than parryWindowStartTime.");
        if (timingData.useTimingDataHitboxControl && timingData.hitboxCloseTime <= timingData.hitboxOpenTime + 0.001f)
            report.Warn($"{label}: useTimingDataHitboxControl is enabled, but hitbox window is too short.");
        if (timingData.telegraphDuration > 0.001f && timingData.telegraphType == BossAttackTelegraphType.None)
            report.Warn($"{label}: attackTimingData has telegraph duration, but telegraphType is None.");
        if (timingData.telegraphType == BossAttackTelegraphType.Parryable &&
            timingData.parryWindowEndTime <= timingData.parryWindowStartTime + 0.001f)
            report.Warn($"{label}: Parryable telegraph has no usable parry window.");
        if (timingData.telegraphType == BossAttackTelegraphType.Heavy && timingData.telegraphDuration < 0.25f)
            report.Warn($"{label}: Heavy telegraph duration is very short for a readable heavy attack.");

        ValidateTimingFairness(label, timingData, report);
    }

    static void ValidateTimingFairness(string label, BossAttackTimingData timingData, ValidationReport report)
    {
        float readableMinTelegraph = ResolveMinimumReadableTelegraph(timingData.telegraphType);
        if (readableMinTelegraph > 0f && timingData.telegraphDuration < readableMinTelegraph)
            report.Warn($"{label}: {timingData.telegraphType} telegraphDuration is below readable minimum {readableMinTelegraph:0.00}s.");

        bool hasParryWindow = timingData.parryWindowEndTime > timingData.parryWindowStartTime + 0.001f;
        if (timingData.telegraphType == BossAttackTelegraphType.Parryable && hasParryWindow)
        {
            if (timingData.parryWindowStartTime < timingData.telegraphStartTime - 0.001f)
                report.Warn($"{label}: parry window starts before telegraph begins.");
            if (timingData.hitboxOpenTime > 0.001f && timingData.parryWindowEndTime < timingData.hitboxOpenTime - 0.12f)
                report.Warn($"{label}: parry window ends too early before hitbox opens, which can feel unfair.");
        }

        if (timingData.useTimingDataHitboxControl)
        {
            float hitboxDuration = timingData.hitboxCloseTime - timingData.hitboxOpenTime;
            if (hitboxDuration > 0.75f)
                report.Warn($"{label}: timing-controlled hitbox duration is long ({hitboxDuration:0.00}s). Verify it does not linger unfairly.");
            if (timingData.telegraphDuration > 0.001f &&
                timingData.hitboxOpenTime < timingData.telegraphStartTime + timingData.telegraphDuration - 0.001f)
                report.Warn($"{label}: timing-controlled hitbox opens before telegraph finishes.");
        }

        if (timingData.hitboxCloseTime > 0.001f &&
            timingData.recoveryStartTime > 0.001f &&
            timingData.recoveryStartTime < timingData.hitboxCloseTime - 0.001f)
        {
            report.Warn($"{label}: recovery starts before hitbox closes.");
        }

        bool dangerCueRequired =
            timingData.telegraphType == BossAttackTelegraphType.Unblockable ||
            timingData.telegraphType == BossAttackTelegraphType.Heavy;
        if (dangerCueRequired &&
            !timingData.useBodyFlash &&
            !timingData.useWarningSound &&
            !timingData.useCameraShake)
        {
            report.Warn($"{label}: {timingData.telegraphType} attack has no body flash, warning sound, or camera shake cue.");
        }
    }

    static float ResolveMinimumReadableTelegraph(BossAttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case BossAttackTelegraphType.Parryable:
                return 0.18f;
            case BossAttackTelegraphType.DodgeOnly:
                return 0.25f;
            case BossAttackTelegraphType.Unblockable:
                return 0.35f;
            case BossAttackTelegraphType.Heavy:
                return 0.35f;
            case BossAttackTelegraphType.Ranged:
                return 0.30f;
            case BossAttackTelegraphType.Normal:
                return 0.12f;
            default:
                return 0f;
        }
    }

    static void ValidateTelegraphVisualCoverage(
        BossController boss,
        BossAttackTimingData[] timingData,
        bool autoCreatesTelegraphPresenter,
        ValidationReport report)
    {
        if (boss == null || timingData == null || timingData.Length == 0)
            return;

        bool needsSplitVisualCue = false;
        for (int i = 0; i < timingData.Length; i++)
        {
            if (timingData[i].useWeaponFlash || timingData[i].useBodyFlash)
            {
                needsSplitVisualCue = true;
                break;
            }
        }

        if (!needsSplitVisualCue)
            return;

        BossTelegraphVfxPresenter presenter = boss.GetComponentInChildren<BossTelegraphVfxPresenter>(true);
        if (presenter != null)
            return;

        if (autoCreatesTelegraphPresenter && boss.patternVisuals != null)
            return;

        if (boss.patternVisuals != null)
        {
            report.Warn($"{boss.name}: attackTimingData uses weapon/body flash flags, but BossTelegraphVfxPresenter is missing. PatternVisuals fallback will be used without split weapon/body control.");
            return;
        }

        report.Warn($"{boss.name}: attackTimingData uses visual cue flags, but no BossTelegraphVfxPresenter or PatternVisuals was found.");
    }

    static void ValidateAttackTimingCueEffectCoverage(
        string bossName,
        SerializedObject serializedBoss,
        BossAttackTimingData[] timingData,
        ValidationReport report)
    {
        if (serializedBoss == null || timingData == null || timingData.Length == 0)
            return;

        bool usesWarningSound = false;
        bool usesCameraShake = false;
        for (int i = 0; i < timingData.Length; i++)
        {
            usesWarningSound |= timingData[i].useWarningSound;
            usesCameraShake |= timingData[i].useCameraShake;
        }

        if (usesWarningSound)
        {
            SerializedProperty warningClip = serializedBoss.FindProperty("attackTimingWarningClip");
            float warningVolume = ReadFloat(serializedBoss, "attackTimingWarningVolume");
            if (warningClip == null || warningClip.objectReferenceValue == null)
                report.Warn($"{bossName}: attackTimingData uses warning sound, but attackTimingWarningClip is not assigned.");
            if (warningVolume <= 0.001f)
                report.Warn($"{bossName}: attackTimingData uses warning sound, but attackTimingWarningVolume is 0.");
        }

        if (usesCameraShake)
        {
            float shakeAmplitude = ReadFloat(serializedBoss, "attackTimingCueShakeAmplitude");
            float shakeDuration = ReadFloat(serializedBoss, "attackTimingCueShakeDuration");
            if (shakeAmplitude <= 0.001f)
                report.Warn($"{bossName}: attackTimingData uses camera shake, but attackTimingCueShakeAmplitude is 0.");
            if (shakeDuration <= 0.001f)
                report.Warn($"{bossName}: attackTimingData uses camera shake, but attackTimingCueShakeDuration is 0.");
        }
    }

    static void ValidatePostActionSettings(string bossName, SerializedProperty settings, ValidationReport report)
    {
        if (settings == null)
            return;

        float combatIdleMin = ReadFloat(settings, "combatIdleMinTime");
        float combatIdleMax = ReadFloat(settings, "combatIdleMaxTime");
        float backstepDistance = ReadFloat(settings, "backstepDistance");
        float backstepDuration = ReadFloat(settings, "backstepDuration");
        float strafeDistance = ReadFloat(settings, "strafeDistance");
        float strafeDuration = ReadFloat(settings, "strafeDuration");
        float chaseDesiredDistance = ReadFloat(settings, "chaseDesiredDistance");
        float chaseStopDistance = ReadFloat(settings, "chaseStopDistance");
        float chaseMaxDuration = ReadFloat(settings, "chaseMaxDuration");
        float recenterDuration = ReadFloat(settings, "recenterDuration");
        float turnSpeed = ReadFloat(settings, "turnSpeed");
        bool keepFacingTarget = ReadBool(settings, "keepFacingTargetDuringPostAction");

        if (combatIdleMin < 0f || combatIdleMax < 0f)
            report.Error($"{bossName}: postActionSettings combat idle times cannot be negative.");
        if (combatIdleMax < combatIdleMin)
            report.Error($"{bossName}: postActionSettings combatIdleMaxTime is smaller than combatIdleMinTime.");
        if (backstepDistance < 0f || strafeDistance < 0f)
            report.Error($"{bossName}: postActionSettings move distances cannot be negative.");
        if (backstepDistance > 0.001f && backstepDuration <= 0.001f)
            report.Warn($"{bossName}: postActionSettings backstepDistance is set, but backstepDuration is too short.");
        if (strafeDistance > 0.001f && strafeDuration <= 0.001f)
            report.Warn($"{bossName}: postActionSettings strafeDistance is set, but strafeDuration is too short.");
        if (chaseStopDistance <= 0f)
            report.Error($"{bossName}: postActionSettings chaseStopDistance must be greater than 0.");
        if (chaseDesiredDistance < chaseStopDistance)
            report.Warn($"{bossName}: postActionSettings chaseDesiredDistance is smaller than chaseStopDistance.");
        if (chaseMaxDuration <= 0.001f)
            report.Error($"{bossName}: postActionSettings chaseMaxDuration must be greater than 0 to avoid invalid chase behavior.");
        if (recenterDuration <= 0.001f)
            report.Warn($"{bossName}: postActionSettings recenterDuration is too short for readable recenter behavior.");
        if (keepFacingTarget && turnSpeed <= 0.001f)
            report.Warn($"{bossName}: postActionSettings keepFacingTargetDuringPostAction is enabled, but turnSpeed is 0.");
    }

    static void ValidateCombatRecoverySettings(string bossName, SerializedProperty settings, ValidationReport report)
    {
        if (settings == null)
            return;

        float recoveryDuration = ReadFloat(settings, "recoveryDuration");
        float invulnerableDuration = ReadFloat(settings, "invulnerableDuration");
        float superArmorDuration = ReadFloat(settings, "superArmorDuration");
        float lockoutDuration = ReadFloat(settings, "lockoutDuration");
        float recenterDuration = ReadFloat(settings, "recenterDuration");
        float recenterTurnSpeed = ReadFloat(settings, "recenterTurnSpeed");
        bool returnToCombatAnchor = ReadBool(settings, "returnToCombatAnchor");
        float maxAnchorDistance = ReadFloat(settings, "maxAnchorDistance");
        float anchorReturnSpeed = ReadFloat(settings, "anchorReturnSpeed");
        float postRecoveryCombatIdleDuration = ReadFloat(settings, "postRecoveryCombatIdleDuration");

        if (recoveryDuration < 0f || invulnerableDuration < 0f || superArmorDuration < 0f || lockoutDuration < 0f ||
            recenterDuration < 0f || postRecoveryCombatIdleDuration < 0f)
        {
            report.Error($"{bossName}: combatRecoverySettings durations cannot be negative.");
        }

        if (recoveryDuration <= 0.001f && recenterDuration <= 0.001f)
            report.Warn($"{bossName}: combatRecoverySettings has no visible recovery/recenter time.");
        if (invulnerableDuration > recoveryDuration + 0.35f)
            report.Warn($"{bossName}: combatRecoverySettings invulnerableDuration is much longer than recoveryDuration.");
        if (superArmorDuration > recoveryDuration + 0.75f)
            report.Warn($"{bossName}: combatRecoverySettings superArmorDuration is much longer than recoveryDuration.");
        if (lockoutDuration > 0.75f)
            report.Warn($"{bossName}: combatRecoverySettings lockoutDuration is long enough to hide player break/parry reward.");
        if (recenterDuration > 0.001f && recenterTurnSpeed <= 0.001f)
            report.Warn($"{bossName}: combatRecoverySettings recenterDuration is set, but recenterTurnSpeed is 0.");

        if (!returnToCombatAnchor)
            return;

        if (maxAnchorDistance <= 0f)
            report.Error($"{bossName}: combatRecoverySettings maxAnchorDistance must be greater than 0 when anchor return is enabled.");
        if (anchorReturnSpeed <= 0f)
            report.Warn($"{bossName}: combatRecoverySettings anchor return is enabled, but anchorReturnSpeed is 0.");
    }

    static void ValidateDifficultyProfiles(string bossName, SerializedProperty profiles, ValidationReport report)
    {
        if (profiles == null || !profiles.isArray || profiles.arraySize == 0)
        {
            report.Warn($"{bossName}: difficultyProfiles is empty. Runtime will use built-in difficulty defaults.");
            return;
        }

        bool[] seenTiers = new bool[4];
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            int tier = ReadEnumIndex(profile, "tier");
            string label = $"{bossName}: difficultyProfiles[{i}]";
            if (tier >= 0 && tier < seenTiers.Length)
            {
                if (seenTiers[tier])
                    report.Warn($"{label}: duplicate difficulty tier entry. First matching profile is used at runtime.");
                seenTiers[tier] = true;
            }

            ValidatePositiveMultiplier(label, "cooldownMultiplier", ReadFloat(profile, "cooldownMultiplier"), report);
            ValidatePositiveMultiplier(label, "telegraphDurationMultiplier", ReadFloat(profile, "telegraphDurationMultiplier"), report);
            ValidatePositiveMultiplier(label, "hitboxActiveDurationMultiplier", ReadFloat(profile, "hitboxActiveDurationMultiplier"), report);
            ValidatePositiveMultiplier(label, "patternWeightMultiplier", ReadFloat(profile, "patternWeightMultiplier"), report);
            ValidatePositiveMultiplier(label, "engageSpeedMultiplier", ReadFloat(profile, "engageSpeedMultiplier"), report);
            ValidatePositiveMultiplier(label, "postActionDurationMultiplier", ReadFloat(profile, "postActionDurationMultiplier"), report);
            ValidatePositiveMultiplier(label, "parryWindowMultiplier", ReadFloat(profile, "parryWindowMultiplier"), report);

            float damageMultiplier = ReadFloat(profile, "damageMultiplier");
            float reactionDelay = ReadFloat(profile, "reactionDelay");
            float phaseThreshold = ReadFloat(profile, "phaseTransitionHpThreshold");
            bool overrideMaxFollowUp = ReadBool(profile, "overrideMaxFollowUpCount");
            int maxFollowUpCount = ReadInt(profile, "maxFollowUpCount");

            if (damageMultiplier <= 0f)
                report.Warn($"{label}: damageMultiplier is 0 or lower. Boss attacks may stop rewarding difficulty tuning.");
            if (reactionDelay < 0f)
                report.Error($"{label}: reactionDelay cannot be negative.");
            if (reactionDelay > 0.8f)
                report.Warn($"{label}: reactionDelay is high enough to make boss response feel delayed.");
            if (phaseThreshold > 0f && (phaseThreshold < 0.05f || phaseThreshold > 0.95f))
                report.Error($"{label}: phaseTransitionHpThreshold must stay within 0.05-0.95 when enabled.");
            if (overrideMaxFollowUp && maxFollowUpCount < 0)
                report.Error($"{label}: maxFollowUpCount cannot be negative.");
        }

        if (!seenTiers[(int)BossDifficultyTier.Normal])
            report.Warn($"{bossName}: difficultyProfiles has no Normal entry. Runtime will use built-in Normal defaults.");
    }

    static void ValidatePositiveMultiplier(string label, string fieldName, float value, ValidationReport report)
    {
        if (value <= 0f)
            report.Error($"{label}: {fieldName} must be greater than 0.");
    }

    static void ValidatePlayerStateBiasSettings(string bossName, SerializedObject serializedBoss, ValidationReport report)
    {
        if (serializedBoss == null || !ReadBool(serializedBoss, "usePlayerStatePatternBias"))
            return;

        float farPressureDistance = ReadFloat(serializedBoss, "farPressureDistance");
        float farPressureDelay = ReadFloat(serializedBoss, "farPressureDelay");
        float farPressureRangedWeightMultiplier = ReadFloat(serializedBoss, "farPressureRangedWeightMultiplier");
        float farPressureChaseWeightMultiplier = ReadFloat(serializedBoss, "farPressureChaseWeightMultiplier");
        float farPressureEngageSpeedMultiplier = ReadFloat(serializedBoss, "farPressureEngageSpeedMultiplier");
        float closeLingerDistance = ReadFloat(serializedBoss, "closeLingerDistance");
        float closeLingerDelay = ReadFloat(serializedBoss, "closeLingerDelay");
        float closeLingerPunishWeightMultiplier = ReadFloat(serializedBoss, "closeLingerPunishWeightMultiplier");
        float closeLingerRangedWeightMultiplier = ReadFloat(serializedBoss, "closeLingerRangedWeightMultiplier");
        float playerDefenseResponseMemoryTime = ReadFloat(serializedBoss, "playerDefenseResponseMemoryTime");
        float playerObservationMoveSpeedThreshold = ReadFloat(serializedBoss, "playerObservationMoveSpeedThreshold");
        float playerDodgingDelayedAttackWeightMultiplier = ReadFloat(serializedBoss, "playerDodgingDelayedAttackWeightMultiplier");
        float playerGuardingPunishWeightMultiplier = ReadFloat(serializedBoss, "playerGuardingPunishWeightMultiplier");
        float playerAttackingCounterWeightMultiplier = ReadFloat(serializedBoss, "playerAttackingCounterWeightMultiplier");
        float playerStunnedRangedWeightMultiplier = ReadFloat(serializedBoss, "playerStunnedRangedWeightMultiplier");
        float playerRecentlyHitBossCounterWeightMultiplier = ReadFloat(serializedBoss, "playerRecentlyHitBossCounterWeightMultiplier");
        float bossMissedPressureDelay = ReadFloat(serializedBoss, "bossMissedPressureDelay");
        float bossMissedPressureWeightMultiplier = ReadFloat(serializedBoss, "bossMissedPressureWeightMultiplier");
        float playerSideAngleThreshold = ReadFloat(serializedBoss, "playerSideAngleThreshold");
        float playerBackAngleThreshold = ReadFloat(serializedBoss, "playerBackAngleThreshold");
        float playerBackAngleCounterWeightMultiplier = ReadFloat(serializedBoss, "playerBackAngleCounterWeightMultiplier");
        float playerStateReactionDelay = ReadFloat(serializedBoss, "playerStateReactionDelay");
        float playerObservationDebugInterval = ReadFloat(serializedBoss, "playerObservationDebugInterval");
        int repeatedParryResponseThreshold = ReadInt(serializedBoss, "repeatedParryResponseThreshold");
        int repeatedPerfectDodgeResponseThreshold = ReadInt(serializedBoss, "repeatedPerfectDodgeResponseThreshold");

        if (farPressureDistance <= 0f)
            report.Error($"{bossName}: farPressureDistance must be greater than 0.");
        if (farPressureDelay < 0f)
            report.Error($"{bossName}: farPressureDelay cannot be negative.");
        if (farPressureRangedWeightMultiplier < 1f)
            report.Warn($"{bossName}: farPressureRangedWeightMultiplier is below 1, so far players will not be pressured by ranged attacks.");
        if (farPressureChaseWeightMultiplier < 1f)
            report.Warn($"{bossName}: farPressureChaseWeightMultiplier is below 1, so far players will not be pressured by chase attacks.");
        if (farPressureEngageSpeedMultiplier < 1f)
            report.Warn($"{bossName}: farPressureEngageSpeedMultiplier is below 1, so far-pressure engage speed will not increase.");

        if (closeLingerDistance <= 0f)
            report.Error($"{bossName}: closeLingerDistance must be greater than 0.");
        if (closeLingerDelay < 0f)
            report.Error($"{bossName}: closeLingerDelay cannot be negative.");
        if (closeLingerPunishWeightMultiplier < 1f)
            report.Warn($"{bossName}: closeLingerPunishWeightMultiplier is below 1, so close-linger punish patterns will not increase.");
        if (closeLingerRangedWeightMultiplier > 1f)
            report.Warn($"{bossName}: closeLingerRangedWeightMultiplier is above 1, so close players may still trigger ranged pressure too often.");

        if (playerDefenseResponseMemoryTime <= 0f)
            report.Error($"{bossName}: playerDefenseResponseMemoryTime must be greater than 0.");
        if (playerObservationMoveSpeedThreshold <= 0f)
            report.Error($"{bossName}: playerObservationMoveSpeedThreshold must be greater than 0.");
        if (playerDodgingDelayedAttackWeightMultiplier < 1f)
            report.Warn($"{bossName}: playerDodgingDelayedAttackWeightMultiplier is below 1, so repeated dodge behavior will not bias delayed/fakeout attacks.");
        if (playerGuardingPunishWeightMultiplier < 1f)
            report.Warn($"{bossName}: playerGuardingPunishWeightMultiplier is below 1, so guarding players will not bias punish patterns.");
        if (playerAttackingCounterWeightMultiplier < 1f)
            report.Warn($"{bossName}: playerAttackingCounterWeightMultiplier is below 1, so attacking players will not bias counter pressure.");
        if (playerStunnedRangedWeightMultiplier > 1f)
            report.Warn($"{bossName}: playerStunnedRangedWeightMultiplier is above 1, so stunned/downed players may still trigger ranged pressure.");
        if (playerRecentlyHitBossCounterWeightMultiplier < 1f)
            report.Warn($"{bossName}: playerRecentlyHitBossCounterWeightMultiplier is below 1, so player pressure will not bias counter patterns.");
        if (bossMissedPressureDelay <= 0f)
            report.Error($"{bossName}: bossMissedPressureDelay must be greater than 0.");
        if (bossMissedPressureWeightMultiplier < 1f)
            report.Warn($"{bossName}: bossMissedPressureWeightMultiplier is below 1, so long no-hit windows will not increase pressure.");
        if (playerSideAngleThreshold <= 0f || playerSideAngleThreshold > 180f)
            report.Error($"{bossName}: playerSideAngleThreshold must be in range 0..180.");
        if (playerBackAngleThreshold <= playerSideAngleThreshold || playerBackAngleThreshold > 180f)
            report.Error($"{bossName}: playerBackAngleThreshold must be greater than playerSideAngleThreshold and <= 180.");
        if (playerBackAngleCounterWeightMultiplier < 1f)
            report.Warn($"{bossName}: playerBackAngleCounterWeightMultiplier is below 1, so back-angle pressure will not increase.");
        if (playerStateReactionDelay < 0f)
            report.Error($"{bossName}: playerStateReactionDelay cannot be negative.");
        if (playerStateReactionDelay > 0.45f)
            report.Warn($"{bossName}: playerStateReactionDelay is high, so player-state responses may feel sluggish.");
        if (playerObservationDebugInterval < 0.1f)
            report.Error($"{bossName}: playerObservationDebugInterval must be at least 0.1 seconds.");
        if (repeatedParryResponseThreshold <= 0)
            report.Error($"{bossName}: repeatedParryResponseThreshold must be greater than 0.");
        if (repeatedPerfectDodgeResponseThreshold <= 0)
            report.Error($"{bossName}: repeatedPerfectDodgeResponseThreshold must be greater than 0.");
    }

    static void ValidateSelectorObservationBiasModel(string bossName, ValidationReport report)
    {
        BossPlayerCombatObservation movingAway = CreateObservation(8f);
        movingAway.playerIsMovingAway = true;
        AssertBiasGreater(bossName, "moving-away ranged pressure", movingAway, BossPatternId.SwordWave, BossPatternId.QuickSlash, report);
        AssertBiasGreater(bossName, "moving-away dash pressure", movingAway, BossPatternId.DashSlash, BossPatternId.HeavySlash, report);

        BossPlayerCombatObservation closePressure = CreateObservation(1.8f);
        closePressure.playerIsApproaching = true;
        closePressure.playerRecentlyHitBoss = true;
        AssertBiasGreater(bossName, "close-pressure punish", closePressure, BossPatternId.BackstepSlash, BossPatternId.SwordWave, report);
        AssertBiasGreater(bossName, "close-pressure heavy", closePressure, BossPatternId.HeavySlash, BossPatternId.SwordWave, report);

        BossPlayerCombatObservation guarding = CreateObservation(2f);
        guarding.playerIsGuarding = true;
        AssertBiasGreater(bossName, "guard punish", guarding, BossPatternId.HeavySlash, BossPatternId.QuickSlash, report);

        BossPlayerCombatObservation attacking = CreateObservation(2f);
        attacking.playerIsAttacking = true;
        AssertBiasGreater(bossName, "attack counter", attacking, BossPatternId.QuickSlash, BossPatternId.HeavySlash, report);

        BossPlayerCombatObservation stunned = CreateObservation(1.6f);
        stunned.playerIsStunned = true;
        AssertBiasGreater(bossName, "stunned close punish", stunned, BossPatternId.HeavySlash, BossPatternId.SwordWave, report);
    }

    static BossPlayerCombatObservation CreateObservation(float distance)
    {
        BossPlayerCombatObservation observation = BossPlayerCombatObservation.CreateInvalid();
        observation.playerDistance = distance;
        observation.playerAngle = 0f;
        observation.timeSincePlayerLastAttack = 999f;
        observation.timeSinceBossLastHitPlayer = 0f;
        return observation;
    }

    static void AssertBiasGreater(
        string bossName,
        string label,
        BossPlayerCombatObservation observation,
        BossPatternId preferredPattern,
        BossPatternId discouragedPattern,
        ValidationReport report)
    {
        BossPatternContext context = new BossPatternContext(observation.playerDistance, observation.playerAngle, 1, 9999f, observation);
        float preferred = BossPatternSelector.CalculateObservationWeightBiasForValidation(context, preferredPattern);
        float discouraged = BossPatternSelector.CalculateObservationWeightBiasForValidation(context, discouragedPattern);
        if (preferred <= discouraged)
            report.Error($"{bossName}: selector observation bias '{label}' is inverted or neutral ({preferredPattern}={preferred:0.00}, {discouragedPattern}={discouraged:0.00}).");
    }

    static void ValidateEngageSafetySettings(string bossName, SerializedObject serializedBoss, ValidationReport report)
    {
        if (serializedBoss == null)
            return;

        float stallCheckInterval = ReadFloat(serializedBoss, "engageStallCheckInterval");
        float minProgressDistance = ReadFloat(serializedBoss, "engageMinProgressDistance");
        float blockedAbortTime = ReadFloat(serializedBoss, "engageBlockedAbortTime");

        if (stallCheckInterval <= 0f)
            report.Error($"{bossName}: engageStallCheckInterval must be greater than 0.");
        if (minProgressDistance <= 0f)
            report.Error($"{bossName}: engageMinProgressDistance must be greater than 0.");
        if (blockedAbortTime <= 0f)
            report.Error($"{bossName}: engageBlockedAbortTime must be greater than 0.");
        if (blockedAbortTime < stallCheckInterval)
            report.Warn($"{bossName}: engageBlockedAbortTime is shorter than engageStallCheckInterval, so blocked engage may abort too abruptly.");
    }

    static void ValidateSpatialResponseSettings(string bossName, SerializedObject serializedBoss, ValidationReport report)
    {
        if (serializedBoss == null || !ReadBool(serializedBoss, "useSpatialPatternBias"))
            return;

        float spatialBiasProbeDistance = ReadFloat(serializedBoss, "spatialBiasProbeDistance");
        float blockedBackstepWeightMultiplier = ReadFloat(serializedBoss, "blockedBackstepWeightMultiplier");
        float cornerRangedWeightMultiplier = ReadFloat(serializedBoss, "cornerRangedWeightMultiplier");
        float cornerRecenterWeightMultiplier = ReadFloat(serializedBoss, "cornerRecenterWeightMultiplier");

        if (spatialBiasProbeDistance <= 0f)
            report.Error($"{bossName}: spatialBiasProbeDistance must be greater than 0.");
        if (blockedBackstepWeightMultiplier <= 0f || blockedBackstepWeightMultiplier > 1f)
            report.Error($"{bossName}: blockedBackstepWeightMultiplier must be in range 0..1.");
        if (cornerRangedWeightMultiplier <= 0f || cornerRangedWeightMultiplier > 1f)
            report.Error($"{bossName}: cornerRangedWeightMultiplier must be in range 0..1.");
        if (cornerRecenterWeightMultiplier < 1f)
            report.Warn($"{bossName}: cornerRecenterWeightMultiplier is below 1, so corner close-pressure bias will not increase.");
    }

    static void ValidateRuntimeSafetyMonitorSettings(BossController boss, ValidationReport report)
    {
        if (boss == null)
            return;

        BossCombatRuntimeSafetyMonitor monitor = boss.GetComponentInChildren<BossCombatRuntimeSafetyMonitor>(includeInactive: true);
        if (monitor == null)
            return;

        SerializedObject serializedMonitor = new SerializedObject(monitor);
        float hitboxGraceTime = ReadFloat(serializedMonitor, "hitboxOutsideAttackGraceTime");
        bool autoRecoverFromStuckRecovery = ReadBool(serializedMonitor, "autoRecoverFromStuckRecovery");
        float maxRecoveryStateDuration = ReadFloat(serializedMonitor, "maxRecoveryStateDuration");
        bool autoRecoverFromStuckAttack = ReadBool(serializedMonitor, "autoRecoverFromStuckAttack");
        float maxAttackStateDuration = ReadFloat(serializedMonitor, "maxAttackStateDuration");
        bool autoRecoverFromNoPatternProgress = ReadBool(serializedMonitor, "autoRecoverFromNoPatternProgress");
        float maxNoPatternProgressDuration = ReadFloat(serializedMonitor, "maxNoPatternProgressDuration");
        int maxSamePatternStreak = ReadInt(serializedMonitor, "maxSamePatternStreak");
        int maxSamePostActionStreak = ReadInt(serializedMonitor, "maxSamePostActionStreak");
        float farRangeThreshold = ReadFloat(serializedMonitor, "farRangeThreshold");
        int maxFarNoPressureStreak = ReadInt(serializedMonitor, "maxFarNoPressureStreak");
        int maxMovingAwayNoPressureStreak = ReadInt(serializedMonitor, "maxMovingAwayNoPressureStreak");
        int maxGuardNoPunishStreak = ReadInt(serializedMonitor, "maxGuardNoPunishStreak");
        int maxAttackNoCounterStreak = ReadInt(serializedMonitor, "maxAttackNoCounterStreak");
        int maxDefenseNoMixupStreak = ReadInt(serializedMonitor, "maxDefenseNoMixupStreak");

        if (hitboxGraceTime < 0f)
            report.Error($"{monitor.name}: hitboxOutsideAttackGraceTime cannot be negative.");
        if (!autoRecoverFromStuckRecovery)
            report.Warn($"{monitor.name}: autoRecoverFromStuckRecovery is disabled, so a failed recovery can leave the boss stuck.");
        if (maxRecoveryStateDuration <= 0f)
            report.Error($"{monitor.name}: maxRecoveryStateDuration must be greater than 0.");
        else if (maxRecoveryStateDuration < 0.5f)
            report.Warn($"{monitor.name}: maxRecoveryStateDuration is very short and may restart valid recovery transitions.");
        else if (maxRecoveryStateDuration > 5f)
            report.Warn($"{monitor.name}: maxRecoveryStateDuration is long enough for visible boss lockups.");
        if (!autoRecoverFromStuckAttack)
            report.Warn($"{monitor.name}: autoRecoverFromStuckAttack is disabled, so a failed attack transition can leave the boss stuck.");
        if (maxAttackStateDuration < 1f)
            report.Error($"{monitor.name}: maxAttackStateDuration must be at least 1.");
        else if (maxAttackStateDuration < 2f)
            report.Warn($"{monitor.name}: maxAttackStateDuration is short enough to interrupt valid attack animations.");
        else if (maxAttackStateDuration > 10f)
            report.Warn($"{monitor.name}: maxAttackStateDuration is long enough for visible attack-state lockups.");
        if (!autoRecoverFromNoPatternProgress)
            report.Warn($"{monitor.name}: autoRecoverFromNoPatternProgress is disabled, so no-pattern combat stalls can persist.");
        if (maxNoPatternProgressDuration < 1f)
            report.Error($"{monitor.name}: maxNoPatternProgressDuration must be at least 1.");
        else if (maxNoPatternProgressDuration < 2f)
            report.Warn($"{monitor.name}: maxNoPatternProgressDuration is short enough to interrupt normal combat pacing.");
        else if (maxNoPatternProgressDuration > 8f)
            report.Warn($"{monitor.name}: maxNoPatternProgressDuration is long enough for visible combat stalls.");
        if (maxSamePatternStreak < 2)
            report.Error($"{monitor.name}: maxSamePatternStreak must be at least 2.");
        if (maxSamePostActionStreak < 2)
            report.Error($"{monitor.name}: maxSamePostActionStreak must be at least 2.");
        if (farRangeThreshold <= 0f)
            report.Error($"{monitor.name}: farRangeThreshold must be greater than 0.");
        if (maxFarNoPressureStreak < 2)
            report.Error($"{monitor.name}: maxFarNoPressureStreak must be at least 2.");
        if (maxMovingAwayNoPressureStreak < 2)
            report.Error($"{monitor.name}: maxMovingAwayNoPressureStreak must be at least 2.");
        if (maxGuardNoPunishStreak < 2)
            report.Error($"{monitor.name}: maxGuardNoPunishStreak must be at least 2.");
        if (maxAttackNoCounterStreak < 2)
            report.Error($"{monitor.name}: maxAttackNoCounterStreak must be at least 2.");
        if (maxDefenseNoMixupStreak < 2)
            report.Error($"{monitor.name}: maxDefenseNoMixupStreak must be at least 2.");
    }

    static void ValidateTelemetryRecorderSettings(BossController boss, ValidationReport report)
    {
        if (boss == null)
            return;

        BossPatternTelemetryRecorder recorder = boss.GetComponentInChildren<BossPatternTelemetryRecorder>(includeInactive: true);
        if (recorder == null)
            return;

        SerializedObject serializedRecorder = new SerializedObject(recorder);
        int minSamplesForVerdict = ReadInt(serializedRecorder, "minSamplesForVerdict");
        float minRecoveryCompletionRatio = ReadFloat(serializedRecorder, "minRecoveryCompletionRatio");
        float maxRecoveryAbortRatio = ReadFloat(serializedRecorder, "maxRecoveryAbortRatio");
        float maxRecoveryNoTargetRatio = ReadFloat(serializedRecorder, "maxRecoveryNoTargetRatio");

        if (minSamplesForVerdict < 1)
            report.Error($"{recorder.name}: minSamplesForVerdict must be at least 1.");
        if (minRecoveryCompletionRatio < 0f || minRecoveryCompletionRatio > 1f)
            report.Error($"{recorder.name}: minRecoveryCompletionRatio must be in range 0..1.");
        if (maxRecoveryAbortRatio < 0f || maxRecoveryAbortRatio > 1f)
            report.Error($"{recorder.name}: maxRecoveryAbortRatio must be in range 0..1.");
        if (maxRecoveryNoTargetRatio < 0f || maxRecoveryNoTargetRatio > 1f)
            report.Error($"{recorder.name}: maxRecoveryNoTargetRatio must be in range 0..1.");
        if (maxRecoveryAbortRatio + maxRecoveryNoTargetRatio > 0.5f)
            report.Warn($"{recorder.name}: recovery failure thresholds are loose enough to hide unstable recovery behavior.");
    }

    static void ValidateAttackFeedbackSettings(BossController boss, ValidationReport report)
    {
        if (boss == null)
            return;

        BossAttackFeedbackPresenter presenter = boss.GetComponentInChildren<BossAttackFeedbackPresenter>(includeInactive: true);
        if (presenter == null)
        {
            report.Warn($"{boss.name}: BossAttackFeedbackPresenter is missing; boss-readable attack feedback will use legacy timing cues only.");
            return;
        }

        SerializedObject serializedPresenter = new SerializedObject(presenter);
        SerializedProperty profiles = serializedPresenter.FindProperty("profiles");
        if (profiles == null || !profiles.isArray || profiles.arraySize == 0)
        {
            report.Warn($"{presenter.name}: attack feedback profiles are empty.");
            return;
        }

        bool hasParryable = false;
        bool hasDodgeOnly = false;
        bool hasHeavy = false;
        bool hasRanged = false;
        for (int i = 0; i < profiles.arraySize; i++)
        {
            SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
            BossAttackTelegraphType type = (BossAttackTelegraphType)ReadEnumIndex(profile, "telegraphType");
            float volume = ReadFloat(profile, "volume");
            float cameraShakeAmplitude = ReadFloat(profile, "cameraShakeAmplitude");
            float cameraShakeDuration = ReadFloat(profile, "cameraShakeDuration");
            float hitStopTimeScale = ReadFloat(profile, "hitStopTimeScale");
            float hitStopDuration = ReadFloat(profile, "hitStopDuration");

            hasParryable |= type == BossAttackTelegraphType.Parryable;
            hasDodgeOnly |= type == BossAttackTelegraphType.DodgeOnly;
            hasHeavy |= type == BossAttackTelegraphType.Heavy;
            hasRanged |= type == BossAttackTelegraphType.Ranged;

            if (volume < 0f || volume > 1f)
                report.Error($"{presenter.name}: profiles[{i}].volume must be in range 0..1.");
            if (cameraShakeAmplitude < 0f || cameraShakeDuration < 0f)
                report.Error($"{presenter.name}: profiles[{i}] camera shake values cannot be negative.");
            if (cameraShakeAmplitude > 0.12f)
                report.Warn($"{presenter.name}: profiles[{i}] camera shake amplitude may be too strong for readable boss telegraphs.");
            if (hitStopTimeScale <= 0f || hitStopTimeScale > 1f)
                report.Error($"{presenter.name}: profiles[{i}].hitStopTimeScale must be in range 0..1.");
            if (hitStopDuration < 0f)
                report.Error($"{presenter.name}: profiles[{i}].hitStopDuration cannot be negative.");
            if (hitStopDuration > 0.08f)
                report.Warn($"{presenter.name}: profiles[{i}] hit stop may hide attack timing.");
        }

        if (!hasParryable)
            report.Warn($"{presenter.name}: missing Parryable feedback profile.");
        if (!hasDodgeOnly)
            report.Warn($"{presenter.name}: missing DodgeOnly feedback profile.");
        if (!hasHeavy)
            report.Warn($"{presenter.name}: missing Heavy feedback profile.");
        if (!hasRanged)
            report.Warn($"{presenter.name}: missing Ranged feedback profile.");
    }

    static void ValidateTimingArrayIntegrity(
        string bossName,
        BossAttackTimingData[] timingData,
        System.Collections.Generic.List<AttackPattern> allPatterns,
        ValidationReport report)
    {
        if (timingData == null || timingData.Length == 0)
            return;

        for (int i = 0; i < timingData.Length; i++)
        {
            BossPatternId patternId = timingData[i].patternId;
            string label = $"{bossName}/attackTimingData[{i}]";

            if (patternId == BossPatternId.None)
            {
                report.Warn($"{label}: patternId is None and will never be selected by pattern-specific timing lookup.");
                continue;
            }

            if (!HasPatternId(allPatterns, patternId))
                report.Error($"{label}: patternId {patternId} has no matching boss pattern.");

            for (int j = i + 1; j < timingData.Length; j++)
            {
                if (timingData[j].patternId == patternId)
                    report.Warn($"{label}: duplicate timing entry also exists at attackTimingData[{j}].");
            }
        }
    }

    static void ValidatePhaseModifiers(
        string label,
        BossController boss,
        AttackPattern pattern,
        BossPatternId patternId,
        System.Collections.Generic.List<AttackPattern> allPatterns,
        bool usesPhaseFallback,
        ValidationReport report)
    {
        if (pattern.phaseModifiers == null || pattern.phaseModifiers.Length == 0)
        {
            if (usesPhaseFallback)
                ValidateResolvedPhaseFallbacks(label, boss, pattern, patternId, allPatterns, report);
            else
                report.Warn($"{label}: no phaseModifiers. Phase 2/3 will use base pattern behavior. Run '{RebuildMenuPath}' on the boss to generate editable defaults.");
            return;
        }

        bool hasPhase2 = false;
        bool hasPhase3 = false;
        for (int i = 0; i < pattern.phaseModifiers.Length; i++)
        {
            BossPhasePatternModifier modifier = pattern.phaseModifiers[i];
            if (modifier.phase == 2)
                hasPhase2 = true;
            else if (modifier.phase == 3)
                hasPhase3 = true;
            else
                report.Error($"{label}: phaseModifiers[{i}] has invalid phase {modifier.phase}.");

            ValidatePhaseModifierValues(label, i, modifier, patternId, allPatterns, report);
        }

        if (!hasPhase2 && !usesPhaseFallback)
            report.Warn($"{label}: missing phase 2 modifier.");
        if (!hasPhase3 && !usesPhaseFallback)
            report.Warn($"{label}: missing phase 3 modifier.");
    }

    static void ValidateResolvedPhaseFallbacks(
        string label,
        BossController boss,
        AttackPattern pattern,
        BossPatternId patternId,
        System.Collections.Generic.List<AttackPattern> allPatterns,
        ValidationReport report)
    {
        if (boss == null)
            return;

        for (int phase = 2; phase <= 3; phase++)
        {
            if (boss.TryGetResolvedPhasePatternModifierForValidation(pattern, phase, out BossPhasePatternModifier modifier))
                ValidatePhaseModifierValues($"{label}/fallbackPhase{phase}", phase, modifier, patternId, allPatterns, report);
            else
                report.Warn($"{label}: phase {phase} fallback is enabled, but no usable phase modifier was resolved.");
        }
    }

    static void ValidatePhaseModifierValues(
        string label,
        int index,
        BossPhasePatternModifier modifier,
        BossPatternId patternId,
        System.Collections.Generic.List<AttackPattern> allPatterns,
        ValidationReport report)
    {
        if (modifier.patternId != BossPatternId.None && modifier.patternId != patternId)
            report.Warn($"{label}: phaseModifiers[{index}] targets {modifier.patternId}, but pattern resolves as {patternId}.");

        if (modifier.overridePostAction && modifier.postActionOverride == BossPatternPostActionType.None)
            report.Warn($"{label}: phaseModifiers[{index}] overrides postAction to None.");

        ValidatePhaseMultiplier(label, index, "baseWeightMultiplier", modifier.baseWeightMultiplier, report);
        ValidatePhaseMultiplier(label, index, "cooldownMultiplier", modifier.cooldownMultiplier, report);
        ValidatePhaseMultiplier(label, index, "telegraphDurationMultiplier", modifier.telegraphDurationMultiplier, report);
        ValidatePhaseMultiplier(label, index, "recoveryDurationMultiplier", modifier.recoveryDurationMultiplier, report);
        ValidatePhaseMultiplier(label, index, "damageMultiplier", modifier.damageMultiplier, report);
        ValidatePhaseMultiplier(label, index, "engageMoveSpeedMultiplier", modifier.engageMoveSpeedMultiplier, report);
        ValidatePhaseModifierHasGameplayChange(label, index, modifier, report);

        if (modifier.overrideFollowUp && modifier.allowFollowUp && modifier.followUpPatternId != BossPatternId.None &&
            !HasPatternId(allPatterns, modifier.followUpPatternId))
        {
            report.Error($"{label}: phaseModifiers[{index}] followUpPatternId {modifier.followUpPatternId} has no matching pattern.");
        }

        if (!modifier.overrideFollowUp && modifier.followUpPatternId != BossPatternId.None)
            report.Warn($"{label}: phaseModifiers[{index}] sets followUpPatternId, but overrideFollowUp is disabled so the specific follow-up id is ignored.");

        if (modifier.overrideFollowUp && modifier.allowFollowUp && modifier.maxFollowUpCount == 0)
            report.Warn($"{label}: phaseModifiers[{index}] allows follow-up but maxFollowUpCount is 0.");

        if (modifier.maxFollowUpCount < 0)
            report.Error($"{label}: phaseModifiers[{index}] maxFollowUpCount cannot be negative.");
    }

    static void ValidatePhaseModifierHasGameplayChange(
        string label,
        int index,
        BossPhasePatternModifier modifier,
        ValidationReport report)
    {
        bool hasMultiplierChange =
            HasMeaningfulMultiplierChange(modifier.baseWeightMultiplier) ||
            HasMeaningfulMultiplierChange(modifier.cooldownMultiplier) ||
            HasMeaningfulMultiplierChange(modifier.telegraphDurationMultiplier) ||
            HasMeaningfulMultiplierChange(modifier.recoveryDurationMultiplier) ||
            HasMeaningfulMultiplierChange(modifier.damageMultiplier) ||
            HasMeaningfulMultiplierChange(modifier.engageMoveSpeedMultiplier);

        bool hasActionChange =
            modifier.overridePostAction ||
            modifier.overrideFollowUp ||
            modifier.allowFollowUp ||
            (modifier.overrideFollowUp && modifier.followUpPatternId != BossPatternId.None) ||
            modifier.maxFollowUpCount > 0;

        if (!hasMultiplierChange && !hasActionChange)
            report.Warn($"{label}: phaseModifiers[{index}] does not change weight/cooldown/timing/damage/engage/postAction/followUp.");
    }

    static bool HasMeaningfulMultiplierChange(float value)
    {
        return value > 0.001f && Mathf.Abs(value - 1f) > 0.001f;
    }

    static void ValidatePhaseMultiplier(
        string label,
        int index,
        string propertyName,
        float value,
        ValidationReport report)
    {
        if (value < 0f)
        {
            report.Error($"{label}: phaseModifiers[{index}] {propertyName} cannot be negative.");
            return;
        }

        if (value > 4f)
            report.Warn($"{label}: phaseModifiers[{index}] {propertyName} is unusually high ({value:0.00}).");
    }

    static BossAttackTimingData[] ReadTimingData(SerializedProperty timingArray)
    {
        if (timingArray == null || !timingArray.isArray)
            return Array.Empty<BossAttackTimingData>();

        BossAttackTimingData[] data = new BossAttackTimingData[timingArray.arraySize];
        for (int i = 0; i < timingArray.arraySize; i++)
        {
            SerializedProperty element = timingArray.GetArrayElementAtIndex(i);
            data[i] = ReadTimingDataElement(element);
        }

        return data;
    }

    static BossAttackTimingData ReadTimingDataElement(SerializedProperty element)
    {
        BossAttackTimingData data = default;
        if (element == null)
            return data;

        SerializedProperty patternId = element.FindPropertyRelative("patternId");
        data.patternId = patternId != null ? (BossPatternId)patternId.enumValueIndex : BossPatternId.None;
        SerializedProperty telegraphType = element.FindPropertyRelative("telegraphType");
        data.telegraphType = telegraphType != null ? (BossAttackTelegraphType)telegraphType.enumValueIndex : BossAttackTelegraphType.None;
        data.telegraphStartTime = ReadFloat(element, "telegraphStartTime");
        data.telegraphDuration = ReadFloat(element, "telegraphDuration");
        data.hitboxOpenTime = ReadFloat(element, "hitboxOpenTime");
        data.hitboxCloseTime = ReadFloat(element, "hitboxCloseTime");
        data.parryWindowStartTime = ReadFloat(element, "parryWindowStartTime");
        data.parryWindowEndTime = ReadFloat(element, "parryWindowEndTime");
        data.recoveryStartTime = ReadFloat(element, "recoveryStartTime");
        data.recoveryDuration = ReadFloat(element, "recoveryDuration");
        data.useWeaponFlash = ReadBool(element, "useWeaponFlash");
        data.useBodyFlash = ReadBool(element, "useBodyFlash");
        data.useWarningSound = ReadBool(element, "useWarningSound");
        data.useCameraShake = ReadBool(element, "useCameraShake");
        data.useTimingDataHitboxControl = ReadBool(element, "useTimingDataHitboxControl");
        return data;
    }

    static float ReadFloat(SerializedProperty element, string propertyName)
    {
        SerializedProperty property = element.FindPropertyRelative(propertyName);
        return property != null ? property.floatValue : 0f;
    }

    static float ReadFloat(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.floatValue : 0f;
    }

    static int ReadEnumIndex(SerializedProperty element, string propertyName)
    {
        SerializedProperty property = element.FindPropertyRelative(propertyName);
        return property != null ? property.enumValueIndex : 0;
    }

    static int ReadInt(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.intValue : 0;
    }

    static int ReadInt(SerializedProperty element, string propertyName)
    {
        SerializedProperty property = element.FindPropertyRelative(propertyName);
        return property != null ? property.intValue : 0;
    }

    static bool ReadBool(SerializedProperty element, string propertyName)
    {
        SerializedProperty property = element.FindPropertyRelative(propertyName);
        return property != null && property.boolValue;
    }

    static bool ReadBool(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null && property.boolValue;
    }

    static bool HasPatternId(System.Collections.Generic.List<AttackPattern> patterns, BossPatternId patternId)
    {
        if (patterns == null || patternId == BossPatternId.None)
            return false;

        for (int i = 0; i < patterns.Count; i++)
        {
            if (ResolvePatternId(patterns[i]) == patternId)
                return true;
        }

        return false;
    }

    static BossPatternId ResolvePatternId(AttackPattern pattern)
    {
        if (pattern == null)
            return BossPatternId.None;

        if (pattern.selectorData.patternId != BossPatternId.None)
            return pattern.selectorData.patternId;

        if (pattern.firesSwordWaveProjectile)
            return BossPatternId.SwordWave;

        string patternName = pattern.patternName ?? string.Empty;
        string triggerName = pattern.animTriggerName ?? string.Empty;

        if (ContainsToken(patternName, "SwordWave"))
            return BossPatternId.SwordWave;
        if (ContainsToken(patternName, "Backstep") || ContainsToken(triggerName, "Quickshift"))
            return BossPatternId.BackstepSlash;
        if (ContainsToken(patternName, "Dash") || string.Equals(triggerName, "Attack_D", StringComparison.Ordinal))
            return BossPatternId.DashSlash;
        if (ContainsToken(patternName, "Heavy") ||
            string.Equals(triggerName, "Attack_C", StringComparison.Ordinal) ||
            string.Equals(triggerName, "Attack_E", StringComparison.Ordinal))
            return BossPatternId.HeavySlash;

        return BossPatternId.QuickSlash;
    }

    static bool AllowsPatternFamilyVariantRepeat(BossPatternId patternId)
    {
        return patternId == BossPatternId.QuickSlash ||
               patternId == BossPatternId.HeavySlash;
    }

    static void ApplySwordWaveRuntimeTuning(ref BossPatternData data)
    {
        if (data.patternId != BossPatternId.SwordWave)
            return;

        float minRange = MinEffectiveSwordWaveRange;
        float maxRange = MaxEffectiveSwordWaveRange;
        data.minRange = Mathf.Max(data.minRange, minRange);
        data.maxRange = Mathf.Min(Mathf.Max(data.minRange + 0.1f, data.maxRange), maxRange);
        data.baseWeight = Mathf.Min(data.baseWeight, MaxEffectiveSwordWaveWeight);
        data.engageMode = BossPatternEngageMode.None;
        data.engageStartRange = data.maxRange;
        data.engageStopRange = Mathf.Clamp(data.engageStopRange, data.minRange, data.maxRange);
    }

    static bool ContainsToken(string value, string token)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int ResolveFallbackPriority(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.SwordWave:
                return 90;
            case BossPatternId.QuickSlash:
                return 80;
            case BossPatternId.BackstepSlash:
                return 75;
            case BossPatternId.DashSlash:
                return 70;
            case BossPatternId.HeavySlash:
                return 45;
            default:
                return 0;
        }
    }

    static BossPatternPostActionType ResolveDefaultPostAction(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.BackstepSlash:
                return BossPatternPostActionType.Backstep;
            case BossPatternId.SwordWave:
                return BossPatternPostActionType.ChaseReposition;
            default:
                return BossPatternPostActionType.CombatIdle;
        }
    }

    static BossPatternEngageMode ResolveDefaultEngageMode(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.QuickSlash:
                return BossPatternEngageMode.ChaseUntilInRange;
            case BossPatternId.DashSlash:
                return BossPatternEngageMode.DashEngage;
            case BossPatternId.HeavySlash:
            case BossPatternId.BackstepSlash:
                return BossPatternEngageMode.RequireInRange;
            default:
                return BossPatternEngageMode.None;
        }
    }

    static float ResolveDefaultEngageStartRange(BossPatternId patternId, float maxRange)
    {
        switch (patternId)
        {
            case BossPatternId.QuickSlash:
                return Mathf.Max(maxRange + 1.2f, 3.2f);
            case BossPatternId.DashSlash:
                return Mathf.Max(maxRange + 1.5f, 8.5f);
            default:
                return Mathf.Max(maxRange, 0f);
        }
    }

    static float ResolveDefaultEngageStopRange(BossPatternId patternId, float minRange, float maxRange)
    {
        float inset = patternId == BossPatternId.DashSlash ? 0.35f : 0.2f;
        return Mathf.Clamp(maxRange - inset, minRange, maxRange);
    }

    static float ResolveDefaultEngageDuration(BossPatternId patternId)
    {
        switch (patternId)
        {
            case BossPatternId.DashSlash:
                return 0.8f;
            case BossPatternId.QuickSlash:
                return 0.55f;
            default:
                return 0.35f;
        }
    }

    static float ResolveDefaultEngageSpeed(BossPatternId patternId)
    {
        return patternId == BossPatternId.DashSlash ? 1.25f : 1.15f;
    }

    sealed class ValidationReport
    {
        int _errors;
        int _warnings;
        readonly StringBuilder _builder = new StringBuilder(1024);

        public void Error(string message)
        {
            _errors++;
            _builder.AppendLine("[Error] " + message);
        }

        public void Warn(string message)
        {
            _warnings++;
            _builder.AppendLine("[Warning] " + message);
        }

        public void Emit(bool showDialog)
        {
            string summary = $"Boss combat data validation: {_errors} error(s), {_warnings} warning(s).";
            string body = _builder.Length > 0 ? _builder.ToString() : "No issues found.";

            if (_errors > 0)
                Debug.LogError(summary + "\n" + body);
            else if (_warnings > 0)
                Debug.LogWarning(summary + "\n" + body);
            else
                Debug.Log(summary + "\n" + body);

            if (showDialog)
                EditorUtility.DisplayDialog("Boss Combat Data Validation", summary + "\n\n" + body, "OK");
        }

        public override string ToString()
        {
            return $"errors={_errors}, warnings={_warnings}\n{(_builder.Length > 0 ? _builder.ToString() : "No issues found.")}";
        }

        public ValidationSummary ToSummary()
        {
            return new ValidationSummary
            {
                ErrorCount = _errors,
                WarningCount = _warnings,
                Details = ToString()
            };
        }
    }

    [Serializable]
    public struct ValidationSummary
    {
        public int ErrorCount;
        public int WarningCount;
        public string Details;

        public bool HasErrors => ErrorCount > 0;
    }
}
