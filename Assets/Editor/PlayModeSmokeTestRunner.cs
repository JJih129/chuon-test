using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayModeSmokeTestRunner
{
    const string SessionKey = "ProjectChuOn.PlayModeSmokeTest.State";
    const double ScenePlayDurationSeconds = 1.5d;
    const double TransitionTimeoutSeconds = 15d;

    static readonly string[] TargetScenePaths =
    {
        "Assets/Scenes/MainScene.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Tutorial.unity",
    };

    static PersistentState _state;

    enum RunnerState
    {
        Idle = 0,
        PreparingNextScene = 1,
        WaitingForEnterPlayMode = 2,
        RunningScene = 3,
        WaitingForExitPlayMode = 4,
    }

    [Serializable]
    public sealed class SmokeStatus
    {
        public string state;
        public bool isRunning;
        public bool hasErrors;
        public string currentScenePath;
        public int completedScenes;
        public int totalScenes;
        public int totalErrorCount;
        public int totalWarningCount;
        public string details;

        public static SmokeStatus CreateIdle()
        {
            return new SmokeStatus
            {
                state = "Idle",
                isRunning = false,
                hasErrors = false,
                currentScenePath = string.Empty,
                completedScenes = 0,
                totalScenes = TargetScenePaths.Length,
                totalErrorCount = 0,
                totalWarningCount = 0,
                details = "Play Mode smoke test has not run yet."
            };
        }
    }

    [Serializable]
    sealed class SceneSmokeResult
    {
        public string scenePath;
        public int errorCount;
        public int warningCount;
        public List<string> notableLogs = new List<string>();
    }

    [Serializable]
    sealed class PersistentState
    {
        public bool isRunning;
        public bool showDialog;
        public int runnerState;
        public string restoreScenePath;
        public string currentScenePath;
        public int currentSceneIndex;
        public double stateStartedAt;
        public int currentSceneErrorCount;
        public int currentSceneWarningCount;
        public List<string> currentSceneNotableLogs = new List<string>();
        public List<SceneSmokeResult> results = new List<SceneSmokeResult>();
        public SmokeStatus lastStatus = SmokeStatus.CreateIdle();
    }

    static PlayModeSmokeTestRunner()
    {
        _state = LoadState();

        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLogMessageReceived;
        Application.logMessageReceived += HandleLogMessageReceived;
    }

    [MenuItem("Tools/Validation/Run Play Mode Smoke Test")]
    static void RunFromMenu()
    {
        Start(showDialog: true);
    }

    public static SmokeStatus StartFromFastMcp()
    {
        Start(showDialog: false);
        return GetStatus();
    }

    public static SmokeStatus GetStatus()
    {
        _state = LoadState();
        NormalizeState(_state);
        return _state.lastStatus ?? SmokeStatus.CreateIdle();
    }

    static void Start(bool showDialog)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (_state.isRunning)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _state.lastStatus = BuildStatus("Failed", false, true, "Cannot start smoke test while Play Mode transition is already in progress.");
            SaveState();
            return;
        }

        _state.showDialog = showDialog;
        _state.isRunning = true;
        _state.runnerState = (int)RunnerState.PreparingNextScene;
        _state.restoreScenePath = SceneManager.GetActiveScene().path;
        _state.currentScenePath = string.Empty;
        _state.currentSceneIndex = -1;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.currentSceneErrorCount = 0;
        _state.currentSceneWarningCount = 0;
        _state.currentSceneNotableLogs.Clear();
        _state.results.Clear();

        PrePlayValidationGuard.SuppressForAutomation = true;
        _state.lastStatus = BuildStatus("Running", true, false, "Starting Play Mode smoke test.");
        SaveState();
    }

    static void Update()
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning)
            return;

        switch ((RunnerState)_state.runnerState)
        {
            case RunnerState.PreparingNextScene:
                PrepareNextScene();
                break;

            case RunnerState.WaitingForEnterPlayMode:
                if (HasTimedOut())
                    Abort("Timed out while entering Play Mode for " + _state.currentScenePath);
                break;

            case RunnerState.RunningScene:
                if (EditorApplication.timeSinceStartup - _state.stateStartedAt >= ScenePlayDurationSeconds)
                {
                    _state.runnerState = (int)RunnerState.WaitingForExitPlayMode;
                    _state.stateStartedAt = EditorApplication.timeSinceStartup;
                    SaveState();
                    EditorApplication.isPlaying = false;
                }
                break;

            case RunnerState.WaitingForExitPlayMode:
                if (HasTimedOut())
                    Abort("Timed out while exiting Play Mode for " + _state.currentScenePath);
                break;
        }
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning)
            return;

        var runnerState = (RunnerState)_state.runnerState;
        if (runnerState == RunnerState.WaitingForEnterPlayMode && state == PlayModeStateChange.EnteredPlayMode)
        {
            _state.runnerState = (int)RunnerState.RunningScene;
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            _state.lastStatus = BuildStatus(
                "Running",
                true,
                false,
                $"Running Play Mode smoke test in {_state.currentScenePath} ({_state.currentSceneIndex + 1}/{TargetScenePaths.Length}).");
            SaveState();
            return;
        }

        if (runnerState == RunnerState.WaitingForExitPlayMode && state == PlayModeStateChange.EnteredEditMode)
        {
            FinalizeCurrentScene();
            _state.runnerState = (int)RunnerState.PreparingNextScene;
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            SaveState();
        }
    }

    static void HandleLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        _state = LoadState();
        NormalizeState(_state);

        if (!_state.isRunning)
            return;

        if (type != LogType.Error && type != LogType.Assert && type != LogType.Exception && type != LogType.Warning)
            return;

        if (type == LogType.Warning)
            _state.currentSceneWarningCount++;
        else
            _state.currentSceneErrorCount++;

        if (_state.currentSceneNotableLogs.Count < 8)
        {
            var message = string.IsNullOrWhiteSpace(stackTrace) ? condition : condition + "\n" + stackTrace;
            _state.currentSceneNotableLogs.Add($"[{type}] {message}");
        }

        SaveState();
    }

    static void PrepareNextScene()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        _state.currentSceneIndex++;
        if (_state.currentSceneIndex >= TargetScenePaths.Length)
        {
            Finish();
            return;
        }

        _state.currentScenePath = TargetScenePaths[_state.currentSceneIndex];
        _state.currentSceneErrorCount = 0;
        _state.currentSceneWarningCount = 0;
        _state.currentSceneNotableLogs.Clear();

        EditorSceneManager.OpenScene(_state.currentScenePath, OpenSceneMode.Single);
        _state.runnerState = (int)RunnerState.WaitingForEnterPlayMode;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus(
            "Running",
            true,
            false,
            $"Opening {_state.currentScenePath} ({_state.currentSceneIndex + 1}/{TargetScenePaths.Length}) for Play Mode smoke test.");
        SaveState();

        EditorApplication.isPlaying = true;
    }

    static void FinalizeCurrentScene()
    {
        var sceneResult = new SceneSmokeResult
        {
            scenePath = _state.currentScenePath,
            errorCount = _state.currentSceneErrorCount,
            warningCount = _state.currentSceneWarningCount,
            notableLogs = new List<string>(_state.currentSceneNotableLogs)
        };

        _state.results.Add(sceneResult);
        _state.currentSceneErrorCount = 0;
        _state.currentSceneWarningCount = 0;
        _state.currentSceneNotableLogs.Clear();

        _state.lastStatus = BuildStatus(
            "Running",
            true,
            sceneResult.errorCount > 0,
            $"Completed {_state.currentScenePath} with {sceneResult.errorCount} errors and {sceneResult.warningCount} warnings.");
    }

    static void Finish()
    {
        var totalErrors = 0;
        var totalWarnings = 0;
        for (var i = 0; i < _state.results.Count; i++)
        {
            totalErrors += _state.results[i].errorCount;
            totalWarnings += _state.results[i].warningCount;
        }

        var details = BuildFinalDetails(totalErrors, totalWarnings);
        _state.lastStatus = new SmokeStatus
        {
            state = totalErrors > 0 ? "Failed" : "Completed",
            isRunning = false,
            hasErrors = totalErrors > 0,
            currentScenePath = string.Empty,
            completedScenes = _state.results.Count,
            totalScenes = TargetScenePaths.Length,
            totalErrorCount = totalErrors,
            totalWarningCount = totalWarnings,
            details = details
        };

        CleanupAfterRun();

        if (totalErrors > 0)
            Debug.LogError(details);
        else if (totalWarnings > 0)
            Debug.LogWarning(details);
        else
            Debug.Log(details);

        if (_state.showDialog)
        {
            EditorApplication.delayCall += delegate
            {
                EditorUtility.DisplayDialog(
                    "Play Mode Smoke Test",
                    $"Errors: {totalErrors}\nWarnings: {totalWarnings}\n\nSee Console for details.",
                    "OK");
            };
        }
    }

    static void Abort(string message)
    {
        _state.lastStatus = BuildStatus("Failed", false, true, message);
        CleanupAfterRun();
        Debug.LogError("[SmokeTest] " + message);
    }

    static void CleanupAfterRun()
    {
        if (EditorApplication.isPlaying)
            EditorApplication.isPlaying = false;

        if (!string.IsNullOrWhiteSpace(_state.restoreScenePath))
            EditorSceneManager.OpenScene(_state.restoreScenePath, OpenSceneMode.Single);

        PrePlayValidationGuard.SuppressForAutomation = false;
        _state.isRunning = false;
        _state.runnerState = (int)RunnerState.Idle;
        _state.currentScenePath = string.Empty;
        _state.currentSceneIndex = -1;
        _state.stateStartedAt = 0d;
        SaveState();
    }

    static bool HasTimedOut()
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt > TransitionTimeoutSeconds;
    }

    static SmokeStatus BuildStatus(string state, bool isRunning, bool hasErrors, string details)
    {
        var totalErrors = 0;
        var totalWarnings = 0;
        for (var i = 0; i < _state.results.Count; i++)
        {
            totalErrors += _state.results[i].errorCount;
            totalWarnings += _state.results[i].warningCount;
        }

        return new SmokeStatus
        {
            state = state,
            isRunning = isRunning,
            hasErrors = hasErrors || totalErrors > 0,
            currentScenePath = _state.currentScenePath ?? string.Empty,
            completedScenes = _state.results.Count,
            totalScenes = TargetScenePaths.Length,
            totalErrorCount = totalErrors,
            totalWarningCount = totalWarnings,
            details = details
        };
    }

    static string BuildFinalDetails(int totalErrors, int totalWarnings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Play Mode smoke test completed.");
        builder.AppendLine($"Scenes: {_state.results.Count}/{TargetScenePaths.Length}");
        builder.AppendLine($"Errors: {totalErrors}");
        builder.AppendLine($"Warnings: {totalWarnings}");

        for (var i = 0; i < _state.results.Count; i++)
        {
            var result = _state.results[i];
            builder.AppendLine($"- {result.scenePath}: errors={result.errorCount}, warnings={result.warningCount}");
            for (var j = 0; j < result.notableLogs.Count; j++)
                builder.AppendLine("  " + result.notableLogs[j]);
        }

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
        if (state.currentSceneNotableLogs == null)
            state.currentSceneNotableLogs = new List<string>();
        if (state.results == null)
            state.results = new List<SceneSmokeResult>();
        if (state.lastStatus == null)
            state.lastStatus = SmokeStatus.CreateIdle();
        if (state.restoreScenePath == null)
            state.restoreScenePath = string.Empty;
        if (state.currentScenePath == null)
            state.currentScenePath = string.Empty;
    }
}
