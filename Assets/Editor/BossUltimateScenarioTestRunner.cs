using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BossUltimateScenarioTestRunner
{
    const string SessionKey = "ProjectChuOn.BossUltimateScenarioTest.State";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const double TransitionTimeoutSeconds = 15d;
    const double RuntimeTimeoutSeconds = 20d;

    static PersistentState _state;

    enum RunnerState
    {
        Idle = 0,
        WaitingForEnterPlayMode = 1,
        InitializingRuntime = 2,
        WaitingForUltimateStart = 3,
        WaitingForUltimateFinish = 4,
        WaitingForBreakEnter = 5,
        WaitingForBreakExit = 6,
        WaitingForExitPlayMode = 7,
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
                details = "Boss/Ultimate scenario test has not run yet."
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
        public float initialTimeScale = 1f;
        public bool sawUltimateActive;
        public bool sawBreakEnter;
        public bool sawInputBlockDuringUltimate;
        public bool sawFrozenTimeDuringUltimate;
        public int errorCount;
        public int warningCount;
        public List<string> notableLogs = new List<string>();
        public ScenarioStatus lastStatus = ScenarioStatus.CreateIdle();
    }

    static BossUltimateScenarioTestRunner()
    {
        _state = LoadState();

        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLogMessageReceived;
        Application.logMessageReceived += HandleLogMessageReceived;
    }

    [MenuItem("Tools/Validation/Run Boss & Ultimate Scenario Test")]
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
            _state.lastStatus = BuildStatus("Failed", false, true, "Cannot start scenario test while Play Mode transition is already in progress.");
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
        _state.runnerState = (int)RunnerState.WaitingForEnterPlayMode;
        _state.pendingCleanup = false;
        _state.restoreScenePath = SceneManager.GetActiveScene().path;
        _state.currentScenePath = MainScenePath;
        _state.currentStep = "Open MainScene and enter Play Mode";
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.initialTimeScale = 1f;
        _state.sawUltimateActive = false;
        _state.sawBreakEnter = false;
        _state.sawInputBlockDuringUltimate = false;
        _state.sawFrozenTimeDuringUltimate = false;
        _state.errorCount = 0;
        _state.warningCount = 0;
        _state.notableLogs.Clear();
        _state.lastStatus = BuildStatus("Running", true, false, "Opening MainScene for boss/ultimate scenario test.");

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

            case RunnerState.WaitingForUltimateStart:
                TickWaitForUltimateStart();
                break;

            case RunnerState.WaitingForUltimateFinish:
                TickWaitForUltimateFinish();
                break;

            case RunnerState.WaitingForBreakEnter:
                TickWaitForBreakEnter();
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
        {
            FinalizeCleanup();
        }
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
        if (!TryGetRuntimeRefs(out var ultimate, out var inputBlocker, out var breakController))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Failed to resolve runtime references in MainScene.");
            return;
        }

        if (ultimate.director == null)
        {
            Abort("PlayerUltimateController.director is null in Play Mode.");
            return;
        }

        _state.initialTimeScale = Time.timeScale;
        if (ultimate.Gauge < ultimate.gaugeMax)
            ultimate.AddGauge(ultimate.gaugeMax);

        if (!ultimate.CanActivate(out var blockReason))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Ultimate never became activation-ready. Last reason: " + blockReason);
            return;
        }

        if (!ultimate.TryActivate())
        {
            Abort("PlayerUltimateController.TryActivate() returned false despite activation-ready state.");
            return;
        }

        _state.runnerState = (int)RunnerState.WaitingForUltimateStart;
        _state.currentStep = "Wait for ultimate cinematic start";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Ultimate activation requested.");
        SaveState();
    }

    static void TickWaitForUltimateStart()
    {
        if (!TryGetRuntimeRefs(out var ultimate, out var inputBlocker, out _))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost runtime references while waiting for ultimate start.");
            return;
        }

        CaptureUltimateRuntimeSignals(ultimate, inputBlocker);

        if (!ultimate.IsCinematic)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Ultimate cinematic did not start.");
            return;
        }

        if (ultimate.lockInputDuringCinematic && !_state.sawInputBlockDuringUltimate && !ultimate.DidApplyInputBlockThisCinematic)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Input blocker did not engage during ultimate cinematic.");
            return;
        }

        if (ultimate.freezeWorldTimeDuringCinematic && !_state.sawFrozenTimeDuringUltimate && !ultimate.DidFreezeWorldTimeThisCinematic)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Time.timeScale did not freeze during ultimate cinematic.");
            return;
        }

        _state.sawUltimateActive = true;
        _state.runnerState = (int)RunnerState.WaitingForUltimateFinish;
        _state.currentStep = "Wait for ultimate cinematic finish";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Ultimate cinematic started successfully.");
        SaveState();
    }

    static void TickWaitForUltimateFinish()
    {
        if (!TryGetRuntimeRefs(out var ultimate, out var inputBlocker, out var breakController))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost runtime references while waiting for ultimate finish.");
            return;
        }

        CaptureUltimateRuntimeSignals(ultimate, inputBlocker);

        if (ultimate.IsCinematic)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Ultimate cinematic did not finish in time.");
            return;
        }

        if (ultimate.lockInputDuringCinematic && !_state.sawInputBlockDuringUltimate && !ultimate.DidApplyInputBlockThisCinematic)
        {
            Abort("Input blocker never engaged during ultimate cinematic.");
            return;
        }

        if (ultimate.freezeWorldTimeDuringCinematic && !_state.sawFrozenTimeDuringUltimate && !ultimate.DidFreezeWorldTimeThisCinematic)
        {
            Abort("Time.timeScale never froze during ultimate cinematic.");
            return;
        }

        if (ultimate.lockInputDuringCinematic && inputBlocker != null && inputBlocker.IsBlocked)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Input blocker did not release after ultimate cinematic.");
            return;
        }

        if (ultimate.freezeWorldTimeDuringCinematic && !Mathf.Approximately(Time.timeScale, _state.initialTimeScale))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Time.timeScale did not restore after ultimate cinematic.");
            return;
        }

        breakController.AddBreak(breakController.maxBreak, BossBreakController.BreakSource.Parry);
        _state.runnerState = (int)RunnerState.WaitingForBreakEnter;
        _state.currentStep = "Wait for boss break enter";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Boss break trigger requested.");
        SaveState();
    }

    static void TickWaitForBreakEnter()
    {
        if (!TryGetRuntimeRefs(out _, out _, out var breakController))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost BossBreakController while waiting for break enter.");
            return;
        }

        if (!breakController.IsInBreak)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Boss did not enter break state.");
            return;
        }

        _state.sawBreakEnter = true;
        _state.runnerState = (int)RunnerState.WaitingForBreakExit;
        _state.currentStep = "Wait for boss break exit";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Boss entered break state.");
        SaveState();
    }

    static void TickWaitForBreakExit()
    {
        if (!TryGetRuntimeRefs(out _, out _, out var breakController))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Lost BossBreakController while waiting for break exit.");
            return;
        }

        if (breakController.IsInBreak)
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Boss break state did not finish in time.");
            return;
        }

        if (!Mathf.Approximately(breakController.Get01(), 0f))
        {
            if (HasTimedOut(RuntimeTimeoutSeconds))
                Abort("Boss break gauge did not reset after break exit.");
            return;
        }

        BeginShutdown("Completed", false, BuildFinalDetails(), "Scenario checks passed. Exiting Play Mode.");
    }

    static bool TryGetRuntimeRefs(
        out PlayerUltimateController ultimate,
        out SimpleInputBlocker inputBlocker,
        out BossBreakController breakController)
    {
        ultimate = null;
        inputBlocker = null;
        breakController = null;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return false;

        ultimate = player.GetComponent<PlayerUltimateController>();
        inputBlocker = player.GetComponent<SimpleInputBlocker>();
        breakController = UnityEngine.Object.FindFirstObjectByType<BossBreakController>();
        return ultimate != null && breakController != null;
    }

    static bool HasTimedOut(double timeoutSeconds)
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt > timeoutSeconds;
    }

    static void CaptureUltimateRuntimeSignals(PlayerUltimateController ultimate, SimpleInputBlocker inputBlocker)
    {
        if (ultimate == null)
            return;

        if (inputBlocker != null && inputBlocker.IsBlocked)
            _state.sawInputBlockDuringUltimate = true;

        if (ultimate.DidApplyInputBlockThisCinematic)
            _state.sawInputBlockDuringUltimate = true;

        if (Mathf.Approximately(Time.timeScale, 0f))
            _state.sawFrozenTimeDuringUltimate = true;

        if (ultimate.DidFreezeWorldTimeThisCinematic)
            _state.sawFrozenTimeDuringUltimate = true;
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
                    "Boss & Ultimate Scenario Test",
                    $"Errors: {_state.lastStatus.errorCount}\nWarnings: {_state.lastStatus.warningCount}\n\nSee Console for details.",
                    "OK");
            };
        }
    }

    static void Abort(string message)
    {
        _state.errorCount = Mathf.Max(1, _state.errorCount);
        BeginShutdown("Failed", true, BuildAbortDetails(message), "Scenario failed. Exiting Play Mode.");
    }

    static string BuildAbortDetails(string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[ScenarioTest] " + message);
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
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.restoreScenePath = string.Empty;
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
        builder.AppendLine("Boss/Ultimate scenario test completed.");
        builder.AppendLine($"Errors: {_state.errorCount}");
        builder.AppendLine($"Warnings: {_state.warningCount}");
        builder.AppendLine($"Ultimate observed: {_state.sawUltimateActive}");
        builder.AppendLine($"Ultimate input block observed: {_state.sawInputBlockDuringUltimate}");
        builder.AppendLine($"Ultimate time freeze observed: {_state.sawFrozenTimeDuringUltimate}");
        builder.AppendLine($"Break observed: {_state.sawBreakEnter}");

        for (var i = 0; i < _state.notableLogs.Count; i++)
            builder.AppendLine(_state.notableLogs[i]);

        return builder.ToString();
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
