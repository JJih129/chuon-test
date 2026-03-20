using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class UIFlowScenarioTestRunner
{
    const string SessionKey = "ProjectChuOn.UIFlowScenarioTest.State";
    const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    const string BootScenePath = "Assets/Scenes/BootScene.unity";
    const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
    const double TransitionTimeoutSeconds = 20d;
    const double TutorialReadyTimeoutSeconds = 25d;

    static PersistentState _state;

    enum RunnerState
    {
        Idle = 0,
        WaitingForEnterPlayMode = 1,
        WaitingForTitleStart = 2,
        WaitingForBootScene = 3,
        WaitingForBootReady = 4,
        WaitingForTutorialScene = 5,
        WaitingForExitPlayMode = 6,
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
                details = "UI flow scenario test has not run yet."
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
        public int errorCount;
        public int warningCount;
        public List<string> notableLogs = new List<string>();
        public ScenarioStatus lastStatus = ScenarioStatus.CreateIdle();
    }

    static UIFlowScenarioTestRunner()
    {
        _state = LoadState();

        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLogMessageReceived;
        Application.logMessageReceived += HandleLogMessageReceived;
    }

    [MenuItem("Tools/Validation/Run UI Flow Scenario Test")]
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
            _state.lastStatus = BuildStatus("Failed", false, true, "Cannot start UI flow scenario while Play Mode transition is already in progress.");
            SaveState();
            return;
        }

        var validationSummary = GameplayRegressionValidator.RunFromFastMcp();
        if (validationSummary.HasErrors)
        {
            _state.lastStatus = BuildStatus("Failed", false, true, validationSummary.Details);
            SaveState();
            return;
        }

        _state.isRunning = true;
        _state.showDialog = showDialog;
        _state.pendingCleanup = false;
        _state.runnerState = (int)RunnerState.WaitingForEnterPlayMode;
        _state.restoreScenePath = SceneManager.GetActiveScene().path;
        _state.currentScenePath = TitleScenePath;
        _state.currentStep = "Open TitleScene and enter Play Mode";
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.errorCount = 0;
        _state.warningCount = 0;
        _state.notableLogs.Clear();
        _state.lastStatus = BuildStatus("Running", true, false, "Opening TitleScene for UI flow scenario test.");

        PrePlayValidationGuard.SuppressForAutomation = true;
        EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
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
                    Abort("Timed out while entering Play Mode from TitleScene.");
                break;

            case RunnerState.WaitingForTitleStart:
                TickWaitForTitleStart();
                break;

            case RunnerState.WaitingForBootScene:
                TickWaitForBootScene();
                break;

            case RunnerState.WaitingForBootReady:
                TickWaitForBootReady();
                break;

            case RunnerState.WaitingForTutorialScene:
                TickWaitForTutorialScene();
                break;

            case RunnerState.WaitingForExitPlayMode:
                if (HasTimedOut(TransitionTimeoutSeconds))
                    Abort("Timed out while exiting Play Mode after UI flow scenario.");
                break;
        }
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning && !_state.pendingCleanup)
            return;

        if ((RunnerState)_state.runnerState == RunnerState.WaitingForEnterPlayMode &&
            state == PlayModeStateChange.EnteredPlayMode)
        {
            _state.runnerState = (int)RunnerState.WaitingForTitleStart;
            _state.currentStep = "Invoke TitleScene start flow";
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            _state.lastStatus = BuildStatus("Running", true, false, "Entered Play Mode. Waiting for TitleScene UI.");
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

        if (ShouldIgnoreLog(condition, type))
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

    static bool ShouldIgnoreLog(string condition, LogType type)
    {
        if (type == LogType.Warning &&
            !string.IsNullOrEmpty(condition) &&
            condition.Contains("doesn't have an Exit Time or any condition"))
        {
            return true;
        }

        return false;
    }

    static void TickWaitForTitleStart()
    {
        if (SceneManager.GetActiveScene().path != TitleScenePath)
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("TitleScene was not active when trying to invoke start flow.");
            return;
        }

        var controller = UnityEngine.Object.FindFirstObjectByType<TitleSceneUIController>();
        if (controller == null)
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("TitleSceneUIController not found in TitleScene.");
            return;
        }

        AddNotableLog("[Title] Invoking OnClick_StartGame()");
        controller.OnClick_StartGame();
        _state.runnerState = (int)RunnerState.WaitingForBootScene;
        _state.currentStep = "Wait for BootScene activation";
        _state.currentScenePath = TitleScenePath;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "TitleScene start flow invoked.");
        SaveState();
    }

    static void TickWaitForBootScene()
    {
        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == BootScenePath)
        {
            AddNotableLog("[Boot] BootScene became active.");
            _state.runnerState = (int)RunnerState.WaitingForBootReady;
            _state.currentStep = "Wait for BootScene prompt and force continue";
            _state.currentScenePath = BootScenePath;
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            _state.lastStatus = BuildStatus("Running", true, false, "BootScene activated.");
            SaveState();
            return;
        }

        if (HasTimedOut(TransitionTimeoutSeconds))
            Abort("TitleScene did not transition to BootScene in time.");
    }

    static void TickWaitForBootReady()
    {
        if (SceneManager.GetActiveScene().path != BootScenePath)
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("BootScene lost focus before prompt became ready.");
            return;
        }

        var loader = UnityEngine.Object.FindFirstObjectByType<SystemBootLoader>();
        if (loader == null)
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("SystemBootLoader not found in BootScene.");
            return;
        }

        if (!loader.IsAwaitingPlayerInput)
        {
            if (HasTimedOut(TutorialReadyTimeoutSeconds))
                Abort("BootScene never reached the PRESS ANY KEY state.");
            return;
        }

        if (!loader.ForceProceedForAutomation())
        {
            if (HasTimedOut(TutorialReadyTimeoutSeconds))
                Abort("BootScene was ready but could not force continue.");
            return;
        }

        AddNotableLog("[Boot] Forced continue at PRESS ANY KEY state.");
        _state.runnerState = (int)RunnerState.WaitingForTutorialScene;
        _state.currentStep = "Wait for Tutorial scene activation";
        _state.currentScenePath = BootScenePath;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "BootScene continue forced.");
        SaveState();
    }

    static void TickWaitForTutorialScene()
    {
        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != TutorialScenePath)
        {
            if (HasTimedOut(TutorialReadyTimeoutSeconds))
                Abort("BootScene did not transition to Tutorial in time.");
            return;
        }

        var player = GameObject.FindGameObjectWithTag("Player");
        var gauge = UnityEngine.Object.FindFirstObjectByType<UI_UltimateGauge>();
        if (player == null || gauge == null)
        {
            if (HasTimedOut(TutorialReadyTimeoutSeconds))
                Abort("Tutorial scene loaded but required gameplay HUD/player objects were missing.");
            return;
        }

        AddNotableLog("[Tutorial] Tutorial scene active with Player and UI_UltimateGauge present.");
        BeginShutdown("Completed", false, BuildFinalDetails(), "UI flow scenario completed. Exiting Play Mode.");
    }

    static bool HasTimedOut(double timeoutSeconds)
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt > timeoutSeconds;
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
                    "UI Flow Scenario Test",
                    $"Errors: {_state.lastStatus.errorCount}\nWarnings: {_state.lastStatus.warningCount}\n\nSee Console for details.",
                    "OK");
            };
        }
    }

    static void Abort(string message)
    {
        _state.errorCount = Mathf.Max(1, _state.errorCount);
        BeginShutdown("Failed", true, BuildAbortDetails(message), "UI flow scenario failed. Exiting Play Mode.");
    }

    static string BuildAbortDetails(string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[UIFlowScenario] " + message);
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
        builder.AppendLine("UI flow scenario test completed.");
        builder.AppendLine($"Errors: {_state.errorCount}");
        builder.AppendLine($"Warnings: {_state.warningCount}");

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
