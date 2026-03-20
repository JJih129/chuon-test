using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GuardParryBreakScenarioTestRunner
{
    const string SessionKey = "ProjectChuOn.GuardParryBreakScenarioTest.State";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const double TransitionTimeoutSeconds = 15d;
    const double RuntimeTimeoutSeconds = 20d;
    const double GuardBlockSettleSeconds = 0.35d;
    const double ParrySettleSeconds = 0.15d;
    const double NoDecayObserveSeconds = 1.0d;
    const double ParryRepeatIntervalSeconds = 0.15d;
    const int MaxRepeatedParries = 20;
    const float HitDamage = 20f;
    const float BreakEpsilon = 0.0001f;
    const float GaugeEpsilon = 0.01f;
    const float DamageEpsilon = 0.01f;

    static PersistentState _state;
    static int _runtimeHpBeforeParry;
    static float _runtimeGaugeBeforeParry;
    static float _runtimeBreakBeforeParry;
    static float _runtimeBreakAfterSingleParry;

    enum RunnerState
    {
        Idle = 0,
        WaitingForEnterPlayMode = 1,
        InitializingRuntime = 2,
        WaitingForGuardBlockValidation = 3,
        WaitingForParryValidation = 4,
        WaitingForNoDecayValidation = 5,
        FeedingParriesToBreak = 6,
        WaitingForBreakExit = 7,
        WaitingForExitPlayMode = 8,
    }

    [Serializable]
    public sealed class ScenarioStatus
    {
        public string state;
        public bool isRunning;
        public bool hasErrors;
        public string currentStep;
        public string currentScenePath;
        public int errorCount;
        public int warningCount;
        public string details;

        public static ScenarioStatus CreateIdle()
        {
            return new ScenarioStatus
            {
                state = "Idle",
                isRunning = false,
                hasErrors = false,
                currentStep = string.Empty,
                currentScenePath = string.Empty,
                errorCount = 0,
                warningCount = 0,
                details = "Guard/Parry/Break scenario test has not run yet."
            };
        }
    }

    [Serializable]
    sealed class PersistentState
    {
        public bool isRunning;
        public bool showDialog;
        public bool pendingCleanup;
        public int runnerState;
        public string restoreScenePath;
        public string currentScenePath;
        public string currentStep;
        public string finalState;
        public string finalDetails;
        public double stateStartedAt;
        public double lastActionAt;
        public int initialHp;
        public int hpAfterGuardBlock;
        public int hpBeforeParry;
        public float initialGauge;
        public float gaugeBeforeParry;
        public float initialBreak;
        public float breakAfterGuardBlock;
        public float breakBeforeParry;
        public float breakAfterSingleParry;
        public int repeatedParryCount;
        public bool sawGuardBlock;
        public bool sawParrySuccess;
        public bool sawBreakEnter;
        public int errorCount;
        public int warningCount;
        public List<string> notableLogs = new List<string>();
        public ScenarioStatus lastStatus = ScenarioStatus.CreateIdle();
    }

    static GuardParryBreakScenarioTestRunner()
    {
        _state = LoadState();

        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLogMessageReceived;
        Application.logMessageReceived += HandleLogMessageReceived;
    }

    [MenuItem("Tools/Validation/Run Guard Parry Break Scenario Test")]
    static void RunFromMenu()
    {
        Start(showDialog: true);
    }

    public static ScenarioStatus StartFromFastMcp()
    {
        Start(showDialog: false);
        return GetStatus();
    }

    public static ScenarioStatus GetStatus()
    {
        _state = LoadState();
        NormalizeState(_state);
        return _state.lastStatus ?? ScenarioStatus.CreateIdle();
    }

    static void Start(bool showDialog)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (_state.isRunning)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _state.lastStatus = BuildStatus("Failed", false, true, "Cannot start guard/parry scenario test while Play Mode transition is already in progress.");
            SaveState();
            return;
        }

        var syncSummary = PlayerWiringSyncUtility.RunFromFastMcp();
        var validationSummary = GameplayRegressionValidator.RunFromFastMcp();
        if (validationSummary.HasErrors)
        {
            _state.lastStatus = BuildStatus(
                "Failed",
                false,
                true,
                validationSummary.Details + "\n" + syncSummary.Details);
            SaveState();
            return;
        }

        _state.isRunning = true;
        _state.showDialog = showDialog;
        _state.pendingCleanup = false;
        _state.runnerState = (int)RunnerState.WaitingForEnterPlayMode;
        _state.restoreScenePath = SceneManager.GetActiveScene().path;
        _state.currentScenePath = MainScenePath;
        _state.currentStep = "Open MainScene and enter Play Mode";
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastActionAt = 0d;
        _state.initialHp = 0;
        _state.hpAfterGuardBlock = 0;
        _state.hpBeforeParry = 0;
        _state.initialGauge = 0f;
        _state.gaugeBeforeParry = 0f;
        _state.initialBreak = 0f;
        _state.breakAfterGuardBlock = 0f;
        _state.breakBeforeParry = 0f;
        _state.breakAfterSingleParry = 0f;
        _runtimeHpBeforeParry = 0;
        _runtimeGaugeBeforeParry = 0f;
        _runtimeBreakBeforeParry = 0f;
        _runtimeBreakAfterSingleParry = 0f;
        _state.repeatedParryCount = 0;
        _state.sawGuardBlock = false;
        _state.sawParrySuccess = false;
        _state.sawBreakEnter = false;
        _state.errorCount = 0;
        _state.warningCount = 0;
        _state.notableLogs.Clear();
        _state.lastStatus = BuildStatus("Running", true, false, "Opening MainScene for guard/parry/break scenario test.");

        PrePlayValidationGuard.SuppressForAutomation = true;
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        SaveState();
        EditorApplication.isPlaying = true;
    }

    static void Update()
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning)
            return;

        switch ((RunnerState)_state.runnerState)
        {
            case RunnerState.WaitingForEnterPlayMode:
                if (HasTimedOut(TransitionTimeoutSeconds))
                    Abort("Timed out while entering Play Mode.");
                break;

            case RunnerState.InitializingRuntime:
                TickInitializeRuntime();
                break;

            case RunnerState.WaitingForGuardBlockValidation:
                TickWaitForGuardBlockValidation();
                break;

            case RunnerState.WaitingForParryValidation:
                TickWaitForParryValidation();
                break;

            case RunnerState.WaitingForNoDecayValidation:
                TickWaitForNoDecayValidation();
                break;

            case RunnerState.FeedingParriesToBreak:
                TickFeedingParriesToBreak();
                break;

            case RunnerState.WaitingForBreakExit:
                TickWaitForBreakExit();
                break;

            case RunnerState.WaitingForExitPlayMode:
                if (HasTimedOut(TransitionTimeoutSeconds))
                    Abort("Timed out while exiting Play Mode.");
                break;
        }
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning && !_state.pendingCleanup)
            return;

        var runnerState = (RunnerState)_state.runnerState;
        if (runnerState == RunnerState.WaitingForEnterPlayMode && state == PlayModeStateChange.EnteredPlayMode)
        {
            _state.runnerState = (int)RunnerState.InitializingRuntime;
            _state.currentStep = "Initialize runtime references";
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            _state.lastStatus = BuildStatus("Running", true, false, "Entered Play Mode. Initializing runtime references.");
            SaveState();
            return;
        }

        if (_state.pendingCleanup && state == PlayModeStateChange.EnteredEditMode)
            FinalizeCleanup();
    }

    static void HandleLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning)
            return;

        if (type != LogType.Warning && type != LogType.Error && type != LogType.Assert && type != LogType.Exception)
            return;

        if (type == LogType.Warning)
            _state.warningCount++;
        else
            _state.errorCount++;

        if (_state.notableLogs.Count < 10)
        {
            var message = string.IsNullOrWhiteSpace(stackTrace) ? condition : condition + "\n" + stackTrace;
            _state.notableLogs.Add($"[{type}] {message}");
        }

        SaveState();
    }

    static void TickInitializeRuntime()
    {
        if (!TryGetRuntimeRefs(out var player, out var damageReceiver, out var guard, out var ultimate, out var breakController, out var health))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Failed to resolve runtime references in MainScene.");
            return;
        }

        if (breakController.IsInBreak)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("BossBreakController started in break state unexpectedly.");
            return;
        }

        if (health.IsDead)
        {
            Abort("PlayerHealth started in dead state unexpectedly.");
            return;
        }

        _state.initialHp = health.CurrentHP;
        _state.initialGauge = ultimate.Gauge;
        _state.initialBreak = breakController.Get01();
        AddNotableLog($"[Init] hp={health.CurrentHP}/{health.MaxHP}, gauge={ultimate.Gauge:0.##}, break={breakController.Get01():0.###}");

        SimulateGuardBlock(player.transform, damageReceiver, guard, breakController.transform);

        _state.runnerState = (int)RunnerState.WaitingForGuardBlockValidation;
        _state.currentStep = "Validate guard block";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Guard block path injected.");
        SaveState();
    }

    static void TickWaitForGuardBlockValidation()
    {
        if (!TryGetRuntimeRefs(out var player, out var damageReceiver, out var guard, out var ultimate, out var breakController, out var health))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost runtime references while validating guard block.");
            return;
        }

        if (!HasStateElapsed(GuardBlockSettleSeconds))
            return;

        AddNotableLog($"[Guard] hp={health.CurrentHP}/{health.MaxHP}, gauge={ultimate.Gauge:0.##}, break={breakController.Get01():0.###}, guarding={guard.IsGuarding}, parryOpen={guard.IsParryWindowOpen}");

        if (breakController.Get01() > _state.initialBreak + BreakEpsilon)
        {
            Abort("Guard block increased boss break gauge.");
            return;
        }

        if (health.IsDead)
        {
            Abort("Guard block left the player dead unexpectedly.");
            return;
        }

        if (_state.initialHp > 0)
        {
            int guardDamageTaken = _state.initialHp - health.CurrentHP;
            if (guardDamageTaken >= Mathf.RoundToInt(HitDamage - DamageEpsilon))
            {
                Abort("Guard block did not mitigate incoming damage.");
                return;
            }

            if (guardDamageTaken < 0 && _state.notableLogs.Count < 10)
                _state.notableLogs.Add("[Info] Guard block HP baseline shifted during startup; skipped strict HP delta assertion.");
        }

        _state.sawGuardBlock = true;
        _state.hpAfterGuardBlock = health.CurrentHP;
        _state.breakAfterGuardBlock = breakController.Get01();
        _state.hpBeforeParry = health.CurrentHP;
        _state.gaugeBeforeParry = ultimate.Gauge;
        _state.breakBeforeParry = breakController.Get01();
        _runtimeHpBeforeParry = health.CurrentHP;
        _runtimeGaugeBeforeParry = ultimate.Gauge;
        _runtimeBreakBeforeParry = breakController.Get01();
        SaveState();

        SimulateParryHit(player.transform, damageReceiver, guard, breakController.transform);

        _state.runnerState = (int)RunnerState.WaitingForParryValidation;
        _state.currentStep = "Validate parry success";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Parry path injected through PlayerDamageReceiver.");
        SaveState();
    }

    static void TickWaitForParryValidation()
    {
        if (!TryGetRuntimeRefs(out _, out _, out _, out var ultimate, out var breakController, out var health))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost runtime references while validating parry.");
            return;
        }

        if (!HasStateElapsed(ParrySettleSeconds))
            return;

        var breakNow = breakController.Get01();
        AddNotableLog($"[Parry] hp={health.CurrentHP}/{health.MaxHP}, gauge={ultimate.Gauge:0.##}, break={breakNow:0.###}");
        if (breakNow <= _runtimeBreakBeforeParry + BreakEpsilon)
        {
            Abort("Parry did not increase boss break gauge.");
            return;
        }

        if (_runtimeGaugeBeforeParry < ultimate.gaugeMax - GaugeEpsilon && ultimate.Gauge <= _runtimeGaugeBeforeParry + GaugeEpsilon)
        {
            Abort("Parry did not increase ultimate gauge.");
            return;
        }

        if (health.CurrentHP != _runtimeHpBeforeParry)
        {
            Abort("Parry still changed player HP.");
            return;
        }

        _state.sawParrySuccess = true;
        _state.breakAfterSingleParry = breakNow;
        _runtimeBreakAfterSingleParry = breakNow;
        SaveState();
        _state.runnerState = (int)RunnerState.WaitingForNoDecayValidation;
        _state.currentStep = "Validate break gauge stability";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Parry success observed. Watching break gauge for natural decay.");
        SaveState();
    }

    static void TickWaitForNoDecayValidation()
    {
        if (!TryGetRuntimeRefs(out _, out _, out _, out _, out var breakController, out _))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost BossBreakController while validating break gauge stability.");
            return;
        }

        if (!HasStateElapsed(NoDecayObserveSeconds))
            return;

        if (Mathf.Abs(breakController.Get01() - _runtimeBreakAfterSingleParry) > BreakEpsilon)
        {
            Abort("Boss break gauge changed without an additional parry.");
            return;
        }

        _state.runnerState = (int)RunnerState.FeedingParriesToBreak;
        _state.currentStep = "Repeat parries until break state";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastActionAt = 0d;
        _state.lastStatus = BuildStatus("Running", true, false, "Single parry gauge remained stable. Repeating parries toward full break.");
        SaveState();
    }

    static void TickFeedingParriesToBreak()
    {
        if (!TryGetRuntimeRefs(out var player, out var damageReceiver, out var guard, out _, out var breakController, out _))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost runtime references while repeating parries.");
            return;
        }

        if (breakController.IsInBreak)
        {
            _state.sawBreakEnter = true;
            _state.runnerState = (int)RunnerState.WaitingForBreakExit;
            _state.currentStep = "Wait for boss break exit";
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            _state.lastStatus = BuildStatus("Running", true, false, "Boss entered break state through repeated parries.");
            SaveState();
            return;
        }

        if (_state.repeatedParryCount >= MaxRepeatedParries)
        {
            Abort("Boss did not enter break state after repeated parry injections.");
            return;
        }

        if (HasStateElapsed(RuntimeTimeoutSeconds))
        {
            Abort("Timed out while feeding repeated parries toward break.");
            return;
        }

        if (_state.lastActionAt <= 0d || EditorApplication.timeSinceStartup - _state.lastActionAt >= ParryRepeatIntervalSeconds)
        {
            SimulateParryHit(player.transform, damageReceiver, guard, breakController.transform);
            _state.repeatedParryCount++;
            _state.lastActionAt = EditorApplication.timeSinceStartup;
            SaveState();
        }
    }

    static void TickWaitForBreakExit()
    {
        if (!TryGetRuntimeRefs(out _, out _, out _, out _, out var breakController, out _))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds + 5d))
                Abort("Lost BossBreakController while waiting for break exit.");
            return;
        }

        if (breakController.IsInBreak)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds + 5d))
                Abort("Boss break state did not finish in time.");
            return;
        }

        if (!Mathf.Approximately(breakController.Get01(), 0f))
        {
            Abort("Boss break gauge did not reset to 0 after break exit.");
            return;
        }

        BeginShutdown("Completed", false, BuildFinalDetails(), "Guard/parry/break scenario checks passed. Exiting Play Mode.");
    }

    static bool TryGetRuntimeRefs(
        out GameObject player,
        out PlayerDamageReceiver damageReceiver,
        out PlayerGuardController guard,
        out PlayerUltimateController ultimate,
        out BossBreakController breakController,
        out PlayerHealth health)
    {
        player = GameObject.FindGameObjectWithTag("Player");
        damageReceiver = null;
        guard = null;
        ultimate = null;
        breakController = null;
        health = null;

        if (player == null)
            return false;

        damageReceiver = player.GetComponent<PlayerDamageReceiver>();
        guard = player.GetComponent<PlayerGuardController>();
        ultimate = player.GetComponent<PlayerUltimateController>();
        breakController = UnityEngine.Object.FindFirstObjectByType<BossBreakController>();
        health = player.GetComponent<PlayerHealth>();
        return damageReceiver != null && guard != null && ultimate != null && breakController != null && health != null;
    }

    static void SimulateGuardBlock(Transform player, PlayerDamageReceiver damageReceiver, PlayerGuardController guard, Transform attacker)
    {
        guard.SetParryWindow(false);
        guard.SetGuarding(true);
        damageReceiver.ReceiveHit(HitDamage, attacker, GetFrontHitPoint(player), false);
        guard.SetGuarding(false);
    }

    static void SimulateParryHit(Transform player, PlayerDamageReceiver damageReceiver, PlayerGuardController guard, Transform attacker)
    {
        guard.SetGuarding(true);
        guard.SetParryWindow(true);
        damageReceiver.ReceiveHit(HitDamage, attacker, GetFrontHitPoint(player), false);
        guard.SetParryWindow(false);
        guard.SetGuarding(false);
    }

    static Vector3 GetFrontHitPoint(Transform player)
    {
        var forward = player != null ? player.forward : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return player.position + forward.normalized * 0.6f;
    }

    static bool HasTimedOut(double timeoutSeconds)
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt > timeoutSeconds;
    }

    static bool HasStateElapsed(double seconds)
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt >= seconds;
    }

    static void BeginShutdown(string finalState, bool hasErrors, string finalDetails, string inProgressDetails)
    {
        _state.pendingCleanup = true;
        _state.finalState = finalState;
        _state.finalDetails = finalDetails;
        _state.runnerState = (int)RunnerState.WaitingForExitPlayMode;
        _state.currentStep = "Exit Play Mode";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, hasErrors, inProgressDetails);
        SaveState();

        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        FinalizeCleanup();
    }

    static void FinalizeCleanup()
    {
        var finalState = string.IsNullOrWhiteSpace(_state.finalState) ? "Completed" : _state.finalState;
        var finalDetails = string.IsNullOrWhiteSpace(_state.finalDetails) ? BuildFinalDetails() : _state.finalDetails;
        var hasErrors = finalState == "Failed" || _state.errorCount > 0;
        if (hasErrors)
            finalState = "Failed";

        var showDialog = _state.showDialog;
        var restoreScenePath = _state.restoreScenePath;

        _state.lastStatus = new ScenarioStatus
        {
            state = finalState,
            isRunning = false,
            hasErrors = hasErrors,
            currentStep = string.Empty,
            currentScenePath = string.Empty,
            errorCount = _state.errorCount,
            warningCount = _state.warningCount,
            details = finalDetails
        };

        var completedStatus = _state.lastStatus;

        ResetStateForIdle();
        SaveState();

        if (!string.IsNullOrWhiteSpace(restoreScenePath) && !EditorApplication.isPlayingOrWillChangePlaymode)
            EditorSceneManager.OpenScene(restoreScenePath, OpenSceneMode.Single);

        if (hasErrors)
            Debug.LogError(finalDetails);
        else if (_state.warningCount > 0)
            Debug.LogWarning(finalDetails);
        else
            Debug.Log(finalDetails);

        if (showDialog)
        {
            EditorApplication.delayCall += delegate
            {
                EditorUtility.DisplayDialog(
                    "Guard Parry Break Scenario Test",
                    $"Errors: {completedStatus.errorCount}\nWarnings: {completedStatus.warningCount}\n\nSee Console for details.",
                    "OK");
            };
        }
    }

    static void Abort(string message)
    {
        _state.errorCount = Mathf.Max(1, _state.errorCount);
        BeginShutdown("Failed", true, BuildAbortDetails(message), "Guard/parry/break scenario failed. Exiting Play Mode.");
    }

    static string BuildAbortDetails(string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[GuardParryScenario] " + message);
        builder.Append(BuildFinalDetails());
        return builder.ToString();
    }

    static void ResetStateForIdle()
    {
        PrePlayValidationGuard.SuppressForAutomation = false;
        _state.isRunning = false;
        _state.pendingCleanup = false;
        _state.runnerState = (int)RunnerState.Idle;
        _state.currentScenePath = string.Empty;
        _state.currentStep = string.Empty;
        _state.stateStartedAt = 0d;
        _state.lastActionAt = 0d;
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.restoreScenePath = string.Empty;
        _runtimeHpBeforeParry = 0;
        _runtimeGaugeBeforeParry = 0f;
        _runtimeBreakBeforeParry = 0f;
        _runtimeBreakAfterSingleParry = 0f;
    }

    static ScenarioStatus BuildStatus(string state, bool isRunning, bool hasErrors, string details)
    {
        return new ScenarioStatus
        {
            state = state,
            isRunning = isRunning,
            hasErrors = hasErrors || _state.errorCount > 0,
            currentStep = _state.currentStep ?? string.Empty,
            currentScenePath = _state.currentScenePath ?? string.Empty,
            errorCount = _state.errorCount,
            warningCount = _state.warningCount,
            details = details
        };
    }

    static string BuildFinalDetails()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Guard/Parry/Break scenario test completed.");
        builder.AppendLine($"Errors: {_state.errorCount}");
        builder.AppendLine($"Warnings: {_state.warningCount}");
        builder.AppendLine($"Guard block observed: {_state.sawGuardBlock}");
        builder.AppendLine($"Parry success observed: {_state.sawParrySuccess}");
        builder.AppendLine($"Break observed: {_state.sawBreakEnter}");
        builder.AppendLine($"Initial HP: {_state.initialHp}");
        builder.AppendLine($"HP after guard block: {_state.hpAfterGuardBlock}");
        builder.AppendLine($"HP before parry: {_state.hpBeforeParry}");
        builder.AppendLine($"Initial gauge: {_state.initialGauge:0.##}");
        builder.AppendLine($"Gauge before parry: {_state.gaugeBeforeParry:0.##}");
        builder.AppendLine($"Initial break: {_state.initialBreak:0.###}");
        builder.AppendLine($"Break after guard block: {_state.breakAfterGuardBlock:0.###}");
        builder.AppendLine($"Break before parry: {_state.breakBeforeParry:0.###}");
        builder.AppendLine($"Break after single parry: {_state.breakAfterSingleParry:0.###}");
        builder.AppendLine($"Repeated parries injected: {_state.repeatedParryCount}");

        for (var i = 0; i < _state.notableLogs.Count; i++)
            builder.AppendLine(_state.notableLogs[i]);

        return builder.ToString();
    }

    static void AddNotableLog(string message)
    {
        if (_state.notableLogs.Count >= 10)
            return;

        _state.notableLogs.Add(message);
        SaveState();
    }

    static PersistentState LoadState()
    {
        var json = SessionState.GetString(SessionKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return CreateDefaultState();

        var loaded = JsonUtility.FromJson<PersistentState>(json);
        if (loaded == null)
            return CreateDefaultState();

        NormalizeState(loaded);
        return loaded;
    }

    static void SaveState()
    {
        NormalizeState(_state);
        SessionState.SetString(SessionKey, JsonUtility.ToJson(_state));
    }

    static PersistentState CreateDefaultState()
    {
        return new PersistentState();
    }

    static void NormalizeState(PersistentState state)
    {
        if (state.notableLogs == null)
            state.notableLogs = new List<string>();
        if (state.lastStatus == null)
            state.lastStatus = ScenarioStatus.CreateIdle();
        if (state.restoreScenePath == null)
            state.restoreScenePath = string.Empty;
        if (state.currentScenePath == null)
            state.currentScenePath = string.Empty;
        if (state.currentStep == null)
            state.currentStep = string.Empty;
        if (state.finalState == null)
            state.finalState = string.Empty;
        if (state.finalDetails == null)
            state.finalDetails = string.Empty;
    }
}
