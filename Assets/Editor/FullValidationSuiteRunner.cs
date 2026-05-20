using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FullValidationSuiteRunner
{
    const string SessionKey = "ProjectChuOn.FullValidationSuite.State";
    const int TotalSteps = 6;

    static PersistentState _state;

    enum RunnerState
    {
        Idle = 0,
        WaitingForSmoke = 1,
        WaitingForBossUltimate = 2,
        WaitingForGuardParryBreak = 3,
        WaitingForUiFlow = 4,
        WaitingForPauseUi = 5,
    }

    [Serializable]
    public sealed class SuiteStatus
    {
        public string state;
        public bool isRunning;
        public bool hasErrors;
        public string currentStep;
        public string currentScenario;
        public int completedSteps;
        public int totalSteps;
        public int errorCount;
        public int warningCount;
        public string details;

        public static SuiteStatus CreateIdle()
        {
            return new SuiteStatus
            {
                state = "Idle",
                isRunning = false,
                hasErrors = false,
                currentStep = string.Empty,
                currentScenario = string.Empty,
                completedSteps = 0,
                totalSteps = TotalSteps,
                errorCount = 0,
                warningCount = 0,
                details = "Full validation suite has not run yet."
            };
        }
    }

    [Serializable]
    sealed class PersistentState
    {
        public bool isRunning;
        public bool showDialog;
        public int runnerState;
        public string currentScenario;
        public string currentStep;
        public string finalState;
        public string finalDetails;
        public int completedSteps;
        public int errorCount;
        public int warningCount;
        public List<string> stepSummaries = new List<string>();
        public SuiteStatus lastStatus = SuiteStatus.CreateIdle();
    }

    static FullValidationSuiteRunner()
    {
        _state = LoadState();
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
    }

    [MenuItem("Tools/Validation/Run Full Validation Suite")]
    static void RunFromMenu()
    {
        Start(showDialog: true);
    }

    public static SuiteStatus StartFromFastMcp()
    {
        Start(showDialog: false);
        return GetStatus();
    }

    public static SuiteStatus GetStatus()
    {
        _state = LoadState();
        NormalizeState(_state);
        return _state.lastStatus ?? SuiteStatus.CreateIdle();
    }

    static void Start(bool showDialog)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (_state.isRunning)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _state.lastStatus = BuildStatus("Failed", false, true, "Cannot start full validation suite while Play Mode transition is already in progress.");
            SaveState();
            return;
        }

        _state.isRunning = true;
        _state.showDialog = showDialog;
        _state.runnerState = (int)RunnerState.Idle;
        _state.currentScenario = "GameplayRegressionChecks";
        _state.currentStep = "Run sync and gameplay regression checks";
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.completedSteps = 0;
        _state.errorCount = 0;
        _state.warningCount = 0;
        _state.stepSummaries.Clear();

        var pauseSyncSummary = PauseSettingsContentSyncUtility.RunFromFastMcp();
        var playerSyncSummary = PlayerWiringSyncUtility.RunFromFastMcp();
        var regressionSummary = GameplayRegressionValidator.RunFromFastMcp();

        AppendStepSummary("PauseSettingsSync", pauseSyncSummary.Details);
        AppendStepSummary("PlayerWiringSync", playerSyncSummary.Details);
        CompleteStep("GameplayRegressionChecks", regressionSummary.ErrorCount, regressionSummary.WarningCount, regressionSummary.Details);

        if (regressionSummary.HasErrors)
        {
            Finish("Failed", true, BuildFinalDetails());
            return;
        }

        _state.lastStatus = BuildStatus("Running", true, false, "Gameplay regression checks passed. Starting Play Mode smoke test.");
        SaveState();
        StartPlayModeSmoke();
    }

    static void Update()
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning)
            return;

        switch ((RunnerState)_state.runnerState)
        {
            case RunnerState.WaitingForSmoke:
                HandlePlayModeSmokeStatus(PlayModeSmokeTestRunner.GetStatus());
                break;

            case RunnerState.WaitingForBossUltimate:
                HandleScenarioStatus(
                    "BossUltimateScenario",
                    BossUltimateScenarioTestRunner.GetStatus().state,
                    BossUltimateScenarioTestRunner.GetStatus().isRunning,
                    BossUltimateScenarioTestRunner.GetStatus().hasErrors,
                    BossUltimateScenarioTestRunner.GetStatus().errorCount,
                    BossUltimateScenarioTestRunner.GetStatus().warningCount,
                    BossUltimateScenarioTestRunner.GetStatus().details,
                    StartGuardParryBreakScenario);
                break;

            case RunnerState.WaitingForGuardParryBreak:
                HandleScenarioStatus(
                    "GuardParryBreakScenario",
                    GuardParryBreakScenarioTestRunner.GetStatus().state,
                    GuardParryBreakScenarioTestRunner.GetStatus().isRunning,
                    GuardParryBreakScenarioTestRunner.GetStatus().hasErrors,
                    GuardParryBreakScenarioTestRunner.GetStatus().errorCount,
                    GuardParryBreakScenarioTestRunner.GetStatus().warningCount,
                    GuardParryBreakScenarioTestRunner.GetStatus().details,
                    StartUiFlowScenario);
                break;

            case RunnerState.WaitingForUiFlow:
                HandleScenarioStatus(
                    "UiFlowScenario",
                    UIFlowScenarioTestRunner.GetStatus().state,
                    UIFlowScenarioTestRunner.GetStatus().isRunning,
                    UIFlowScenarioTestRunner.GetStatus().hasErrors,
                    UIFlowScenarioTestRunner.GetStatus().errorCount,
                    UIFlowScenarioTestRunner.GetStatus().warningCount,
                    UIFlowScenarioTestRunner.GetStatus().details,
                    StartPauseUiScenario);
                break;

            case RunnerState.WaitingForPauseUi:
                HandleScenarioStatus(
                    "PauseUiScenario",
                    PauseUiScenarioTestRunner.GetStatus().state,
                    PauseUiScenarioTestRunner.GetStatus().isRunning,
                    PauseUiScenarioTestRunner.GetStatus().hasErrors,
                    PauseUiScenarioTestRunner.GetStatus().errorCount,
                    PauseUiScenarioTestRunner.GetStatus().warningCount,
                    PauseUiScenarioTestRunner.GetStatus().details,
                    FinishSuccess);
                break;
        }
    }

    static void StartPlayModeSmoke()
    {
        _state.runnerState = (int)RunnerState.WaitingForSmoke;
        _state.currentScenario = "PlayModeSmokeTest";
        _state.currentStep = "Run Play Mode smoke test";
        _state.lastStatus = BuildStatus("Running", true, false, "Starting Play Mode smoke test.");
        SaveState();
        HandlePlayModeSmokeStatus(PlayModeSmokeTestRunner.StartFromFastMcp());
    }

    static void StartBossUltimateScenario()
    {
        _state.runnerState = (int)RunnerState.WaitingForBossUltimate;
        _state.currentScenario = "BossUltimateScenario";
        _state.currentStep = "Run boss and ultimate scenario";
        _state.lastStatus = BuildStatus("Running", true, false, "Starting boss and ultimate scenario test.");
        SaveState();
        HandleScenarioStatus(
            "BossUltimateScenario",
            BossUltimateScenarioTestRunner.StartFromFastMcp().state,
            BossUltimateScenarioTestRunner.GetStatus().isRunning,
            BossUltimateScenarioTestRunner.GetStatus().hasErrors,
            BossUltimateScenarioTestRunner.GetStatus().errorCount,
            BossUltimateScenarioTestRunner.GetStatus().warningCount,
            BossUltimateScenarioTestRunner.GetStatus().details,
            StartGuardParryBreakScenario);
    }

    static void StartGuardParryBreakScenario()
    {
        _state.runnerState = (int)RunnerState.WaitingForGuardParryBreak;
        _state.currentScenario = "GuardParryBreakScenario";
        _state.currentStep = "Run guard, parry, and break scenario";
        _state.lastStatus = BuildStatus("Running", true, false, "Starting guard, parry, and break scenario test.");
        SaveState();
        HandleScenarioStatus(
            "GuardParryBreakScenario",
            GuardParryBreakScenarioTestRunner.StartFromFastMcp().state,
            GuardParryBreakScenarioTestRunner.GetStatus().isRunning,
            GuardParryBreakScenarioTestRunner.GetStatus().hasErrors,
            GuardParryBreakScenarioTestRunner.GetStatus().errorCount,
            GuardParryBreakScenarioTestRunner.GetStatus().warningCount,
            GuardParryBreakScenarioTestRunner.GetStatus().details,
            StartUiFlowScenario);
    }

    static void StartUiFlowScenario()
    {
        _state.runnerState = (int)RunnerState.WaitingForUiFlow;
        _state.currentScenario = "UiFlowScenario";
        _state.currentStep = "Run title, boot, and tutorial UI flow scenario";
        _state.lastStatus = BuildStatus("Running", true, false, "Starting UI flow scenario test.");
        SaveState();
        HandleScenarioStatus(
            "UiFlowScenario",
            UIFlowScenarioTestRunner.StartFromFastMcp().state,
            UIFlowScenarioTestRunner.GetStatus().isRunning,
            UIFlowScenarioTestRunner.GetStatus().hasErrors,
            UIFlowScenarioTestRunner.GetStatus().errorCount,
            UIFlowScenarioTestRunner.GetStatus().warningCount,
            UIFlowScenarioTestRunner.GetStatus().details,
            StartPauseUiScenario);
    }

    static void StartPauseUiScenario()
    {
        _state.runnerState = (int)RunnerState.WaitingForPauseUi;
        _state.currentScenario = "PauseUiScenario";
        _state.currentStep = "Run pause and settings UI scenario";
        _state.lastStatus = BuildStatus("Running", true, false, "Starting pause UI scenario test.");
        SaveState();
        HandleScenarioStatus(
            "PauseUiScenario",
            PauseUiScenarioTestRunner.StartFromFastMcp().state,
            PauseUiScenarioTestRunner.GetStatus().isRunning,
            PauseUiScenarioTestRunner.GetStatus().hasErrors,
            PauseUiScenarioTestRunner.GetStatus().errorCount,
            PauseUiScenarioTestRunner.GetStatus().warningCount,
            PauseUiScenarioTestRunner.GetStatus().details,
            FinishSuccess);
    }

    static void HandlePlayModeSmokeStatus(PlayModeSmokeTestRunner.SmokeStatus status)
    {
        if (status == null)
            return;

        if (status.isRunning)
            return;

        if (status.hasErrors || string.Equals(status.state, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            CompleteStep("PlayModeSmokeTest", status.totalErrorCount, status.totalWarningCount, status.details);
            Finish("Failed", true, BuildFinalDetails());
            return;
        }

        if (!string.Equals(status.state, "Completed", StringComparison.OrdinalIgnoreCase))
            return;

        CompleteStep("PlayModeSmokeTest", status.totalErrorCount, status.totalWarningCount, status.details);
        StartBossUltimateScenario();
    }

    static void HandleScenarioStatus(
        string label,
        string state,
        bool isRunning,
        bool hasErrors,
        int errorCount,
        int warningCount,
        string details,
        Action onCompleted)
    {
        if (isRunning)
            return;

        if (string.IsNullOrWhiteSpace(state) || string.Equals(state, "Idle", StringComparison.OrdinalIgnoreCase))
            return;

        CompleteStep(label, errorCount, warningCount, details);

        if (hasErrors || string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            Finish("Failed", true, BuildFinalDetails());
            return;
        }

        if (!string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase))
            return;

        onCompleted?.Invoke();
    }

    static void CompleteStep(string label, int errorCount, int warningCount, string details)
    {
        _state.completedSteps = Mathf.Min(TotalSteps, _state.completedSteps + 1);
        _state.errorCount += Mathf.Max(0, errorCount);
        _state.warningCount += Mathf.Max(0, warningCount);
        AppendStepSummary(label, details);
        SaveState();
    }

    static void AppendStepSummary(string label, string details)
    {
        if (_state.stepSummaries.Count >= 16)
            return;

        var normalized = string.IsNullOrWhiteSpace(details) ? "No details." : details.Trim();
        _state.stepSummaries.Add($"[{label}]\n{normalized}");
    }

    static void FinishSuccess()
    {
        Finish("Completed", false, BuildFinalDetails());
    }

    static void Finish(string finalState, bool hasErrors, string details)
    {
        _state.finalState = finalState;
        _state.finalDetails = details;
        _state.lastStatus = new SuiteStatus
        {
            state = finalState,
            isRunning = false,
            hasErrors = hasErrors || _state.errorCount > 0,
            currentStep = string.Empty,
            currentScenario = string.Empty,
            completedSteps = _state.completedSteps,
            totalSteps = TotalSteps,
            errorCount = _state.errorCount,
            warningCount = _state.warningCount,
            details = details
        };

        var showDialog = _state.showDialog;
        ResetStateForIdle();
        SaveState();

        if (_state.lastStatus.hasErrors)
            Debug.LogError(details);
        else if (_state.lastStatus.warningCount > 0)
            Debug.LogWarning(details);
        else
            Debug.Log(details);

        if (showDialog)
        {
            EditorApplication.delayCall += delegate
            {
                EditorUtility.DisplayDialog(
                    "Full Validation Suite",
                    $"Errors: {_state.lastStatus.errorCount}\nWarnings: {_state.lastStatus.warningCount}\nCompleted Steps: {_state.lastStatus.completedSteps}/{_state.lastStatus.totalSteps}\n\nSee Console for details.",
                    "OK");
            };
        }
    }

    static string BuildFinalDetails()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Full validation suite completed.");
        builder.AppendLine($"Errors: {_state.errorCount}");
        builder.AppendLine($"Warnings: {_state.warningCount}");
        builder.AppendLine($"Completed Steps: {_state.completedSteps}/{TotalSteps}");

        foreach (var summary in _state.stepSummaries)
        {
            builder.AppendLine();
            builder.AppendLine(summary);
        }

        return builder.ToString();
    }

    static SuiteStatus BuildStatus(string state, bool isRunning, bool hasErrors, string details)
    {
        return new SuiteStatus
        {
            state = state,
            isRunning = isRunning,
            hasErrors = hasErrors || _state.errorCount > 0,
            currentStep = _state.currentStep ?? string.Empty,
            currentScenario = _state.currentScenario ?? string.Empty,
            completedSteps = _state.completedSteps,
            totalSteps = TotalSteps,
            errorCount = _state.errorCount,
            warningCount = _state.warningCount,
            details = details
        };
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

    static void ResetStateForIdle()
    {
        _state.isRunning = false;
        _state.showDialog = false;
        _state.runnerState = (int)RunnerState.Idle;
        _state.currentScenario = string.Empty;
        _state.currentStep = string.Empty;
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.completedSteps = 0;
        _state.errorCount = 0;
        _state.warningCount = 0;
        _state.stepSummaries.Clear();
    }

    static void NormalizeState(PersistentState state)
    {
        if (state.stepSummaries == null)
            state.stepSummaries = new List<string>();
        if (state.lastStatus == null)
            state.lastStatus = SuiteStatus.CreateIdle();
        if (state.currentScenario == null)
            state.currentScenario = string.Empty;
        if (state.currentStep == null)
            state.currentStep = string.Empty;
        if (state.finalState == null)
            state.finalState = string.Empty;
        if (state.finalDetails == null)
            state.finalDetails = string.Empty;
    }
}
