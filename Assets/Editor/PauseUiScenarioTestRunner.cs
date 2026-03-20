using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PauseUiScenarioTestRunner
{
    const string SessionKey = "ProjectChuOn.PauseUiScenarioTest.State";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    const double TransitionTimeoutSeconds = 20d;
    const double UiSettleSeconds = 0.5d;

    static PersistentState _state;
    static bool _pauseRequested;

    enum RunnerState
    {
        Idle = 0,
        WaitingForEnterPlayMode = 1,
        WaitingForPauseOpen = 2,
        WaitingForSettingsOpen = 3,
        WaitingForSettingsClose = 4,
        WaitingForResume = 5,
        WaitingForPauseReopen = 6,
        WaitingForTitleScene = 7,
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
                details = "Pause UI scenario test has not run yet."
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

    static PauseUiScenarioTestRunner()
    {
        _state = LoadState();

        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        Application.logMessageReceived -= HandleLogMessageReceived;
        Application.logMessageReceived += HandleLogMessageReceived;
    }

    [MenuItem("Tools/Validation/Run Pause UI Scenario Test")]
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
            _state.lastStatus = BuildStatus("Failed", false, true, "Cannot start pause UI scenario while Play Mode transition is already in progress.");
            SaveState();
            return;
        }

        PauseSettingsContentSyncUtility.RunFromFastMcp();
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
        _state.currentScenePath = MainScenePath;
        _state.currentStep = "Open MainScene and enter Play Mode";
        _state.finalState = string.Empty;
        _state.finalDetails = string.Empty;
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.errorCount = 0;
        _state.warningCount = 0;
        _state.notableLogs.Clear();
        _pauseRequested = false;
        _state.lastStatus = BuildStatus("Running", true, false, "Opening MainScene for pause UI scenario test.");

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
                    Abort("Timed out while entering Play Mode from MainScene.");
                break;

            case RunnerState.WaitingForPauseOpen:
                TickWaitForPauseOpen();
                break;

            case RunnerState.WaitingForSettingsOpen:
                TickWaitForSettingsOpen();
                break;

            case RunnerState.WaitingForSettingsClose:
                TickWaitForSettingsClose();
                break;

            case RunnerState.WaitingForResume:
                TickWaitForResume();
                break;

            case RunnerState.WaitingForPauseReopen:
                TickWaitForPauseReopen();
                break;

            case RunnerState.WaitingForTitleScene:
                TickWaitForTitleScene();
                break;

            case RunnerState.WaitingForExitPlayMode:
                if (HasTimedOut(TransitionTimeoutSeconds))
                    Abort("Timed out while exiting Play Mode after pause UI scenario.");
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
            _state.runnerState = (int)RunnerState.WaitingForPauseOpen;
            _state.currentStep = "Request pause after gameplay initialization";
            _state.currentScenePath = MainScenePath;
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            _state.lastStatus = BuildStatus("Running", true, false, "Entered Play Mode. Waiting for gameplay initialization before pause.");
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

    static void TickWaitForPauseOpen()
    {
        if (!TryGetPauseRefs(out var pauseManager, out var pauseView))
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("Pause UI references were lost while opening menu.");
            return;
        }

        if (!_pauseRequested)
        {
            if (!HasElapsed(UiSettleSeconds))
                return;

            AddNotableLog("[Pause] Opening pause menu.");
            pauseManager.PauseGame();
            _pauseRequested = true;
            _state.stateStartedAt = EditorApplication.timeSinceStartup;
            SaveState();
            return;
        }

        if (!HasElapsed(UiSettleSeconds))
            return;

        if (Time.timeScale != 0f)
        {
            Abort("Pause menu did not freeze time.");
            return;
        }

        if (pauseView.menuRoot == null || !pauseView.menuRoot.activeInHierarchy)
        {
            Abort("Pause menu root did not become active.");
            return;
        }

        AddNotableLog("[Pause] Menu opened with timeScale=0.");
        pauseManager.OnClick_Settings();
        _state.runnerState = (int)RunnerState.WaitingForSettingsOpen;
        _state.currentStep = "Wait for settings panel open";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Settings panel requested.");
        SaveState();
    }

    static void TickWaitForSettingsOpen()
    {
        if (!TryGetPauseRefs(out _, out var pauseView))
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("Pause UI references were lost while opening settings.");
            return;
        }

        if (!HasElapsed(UiSettleSeconds))
            return;

        if (pauseView.settingsPanel == null || !pauseView.settingsPanel.activeInHierarchy)
        {
            Abort("Settings panel did not become active.");
            return;
        }

        if (pauseView.menuContainer != null && pauseView.menuContainer.gameObject.activeSelf)
        {
            Abort("Menu container did not hide when settings opened.");
            return;
        }

        var optionsManager = pauseView.settingsPanel.GetComponentInChildren<OptionsManagerAdvanced>(true);
        if (optionsManager == null)
        {
            Abort("OptionsManagerAdvanced is missing from the settings panel.");
            return;
        }

        if (optionsManager.cameraShakeSlider == null)
        {
            Abort("Camera shake slider was not prepared when settings opened.");
            return;
        }

        if (!ValidateSettingsNavigation(pauseView.settingsPanel, optionsManager, out var navigationError))
        {
            Abort(navigationError);
            return;
        }

        AddNotableLog("[Pause] Settings panel opened with camera shake slider ready.");
        AddNotableLog("[Pause] Keyboard/gamepad navigation path verified.");
        var pauseManager = UnityEngine.Object.FindFirstObjectByType<PauseManager>();
        pauseManager.CloseSettings();

        _state.runnerState = (int)RunnerState.WaitingForSettingsClose;
        _state.currentStep = "Wait for settings panel close";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Settings close requested.");
        SaveState();
    }

    static void TickWaitForSettingsClose()
    {
        if (!TryGetPauseRefs(out var pauseManager, out var pauseView))
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("Pause UI references were lost while closing settings.");
            return;
        }

        if (!HasElapsed(UiSettleSeconds))
            return;

        if (pauseView.settingsPanel != null && pauseView.settingsPanel.activeSelf)
        {
            Abort("Settings panel did not close.");
            return;
        }

        if (pauseView.menuContainer == null || !pauseView.menuContainer.gameObject.activeSelf)
        {
            Abort("Menu container did not return after closing settings.");
            return;
        }

        AddNotableLog("[Pause] Settings panel closed.");
        pauseManager.OnClick_Resume();

        _state.runnerState = (int)RunnerState.WaitingForResume;
        _state.currentStep = "Wait for resume";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Resume requested.");
        SaveState();
    }

    static void TickWaitForResume()
    {
        if (!TryGetPauseRefs(out var pauseManager, out var pauseView))
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("Pause UI references were lost while resuming.");
            return;
        }

        if (!HasElapsed(UiSettleSeconds))
            return;

        if (Time.timeScale != 1f)
        {
            Abort("Resume did not restore timeScale to 1.");
            return;
        }

        if (pauseView.menuRoot != null && pauseView.menuRoot.activeSelf)
        {
            Abort("Pause menu root stayed active after resume.");
            return;
        }

        AddNotableLog("[Pause] Resume restored gameplay.");
        pauseManager.PauseGame();

        _state.runnerState = (int)RunnerState.WaitingForPauseReopen;
        _state.currentStep = "Reopen pause and return to title";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Pause requested again for title return.");
        SaveState();
    }

    static void TickWaitForPauseReopen()
    {
        if (!TryGetPauseRefs(out var pauseManager, out var pauseView))
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("Pause UI references were lost while reopening pause.");
            return;
        }

        if (!HasElapsed(UiSettleSeconds))
            return;

        if (Time.timeScale != 0f || pauseView.menuRoot == null || !pauseView.menuRoot.activeInHierarchy)
        {
            Abort("Pause menu did not reopen for title return test.");
            return;
        }

        AddNotableLog("[Pause] Pause reopened. Returning to TitleScene.");
        pauseManager.OnClick_ToTitle();

        _state.runnerState = (int)RunnerState.WaitingForTitleScene;
        _state.currentStep = "Wait for TitleScene activation";
        _state.stateStartedAt = EditorApplication.timeSinceStartup;
        _state.lastStatus = BuildStatus("Running", true, false, "Return to title requested.");
        SaveState();
    }

    static void TickWaitForTitleScene()
    {
        var activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != TitleScenePath)
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("Pause return-to-title did not activate TitleScene in time.");
            return;
        }

        var titleUi = UnityEngine.Object.FindFirstObjectByType<TitleSceneUIController>();
        if (titleUi == null)
        {
            if (HasTimedOut(TransitionTimeoutSeconds))
                Abort("TitleScene loaded after pause return, but TitleSceneUIController was missing.");
            return;
        }

        AddNotableLog("[Pause] TitleScene activated successfully.");
        BeginShutdown("Completed", false, BuildFinalDetails(), "Pause UI scenario completed. Exiting Play Mode.");
    }

    static bool TryGetPauseRefs(out PauseManager pauseManager, out PauseMenuView pauseView)
    {
        pauseManager = UnityEngine.Object.FindFirstObjectByType<PauseManager>();
        pauseView = pauseManager != null ? pauseManager.uiView : null;
        return pauseManager != null && pauseView != null;
    }

    static bool ValidateSettingsNavigation(GameObject settingsPanel, OptionsManagerAdvanced optionsManager, out string error)
    {
        error = string.Empty;
        if (settingsPanel == null)
        {
            error = "Settings navigation validation failed because settingsPanel was null.";
            return false;
        }

        if (optionsManager == null)
        {
            error = "Settings navigation validation failed because OptionsManagerAdvanced was missing.";
            return false;
        }

        var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            error = "Settings navigation validation failed because EventSystem was missing.";
            return false;
        }

        var tabControls = FindButton(settingsPanel.transform, "_TabControls");
        var tabAudio = FindButton(settingsPanel.transform, "_TabAudio");
        var tabVideo = FindButton(settingsPanel.transform, "_TabVideo");
        var saveButton = FindButton(settingsPanel.transform, "Button_Save");
        var cancelButton = FindButton(settingsPanel.transform, "Button_Cancel");
        var firstControl = FindSelectable(settingsPanel.transform, "_RuntimeRebindButton")
            ?? FindSelectable(settingsPanel.transform, "Button_MoveForward")
            ?? FindSelectable(settingsPanel.transform, "Dropdown_Resolution")
            ?? FindSelectable(settingsPanel.transform, "Slider_Master");
        var audioFirst = optionsManager.masterSlider != null ? optionsManager.masterSlider : FindSelectable(settingsPanel.transform, "Slider_Master");
        var videoFirst = optionsManager.resolutionDropdown != null ? optionsManager.resolutionDropdown : FindSelectable(settingsPanel.transform, "Dropdown_Resolution");

        if (tabControls == null || tabAudio == null || tabVideo == null || saveButton == null || cancelButton == null || firstControl == null || audioFirst == null || videoFirst == null)
        {
            error = "Settings navigation validation failed because one or more navigation targets were missing.";
            return false;
        }

        if (!MoveSelection(eventSystem, tabControls.gameObject, MoveDirection.Right, tabAudio.gameObject))
        {
            error = "Settings navigation validation failed: Controls tab did not move right to Audio tab.";
            return false;
        }

        if (!MoveSelection(eventSystem, tabAudio.gameObject, MoveDirection.Right, tabVideo.gameObject))
        {
            error = "Settings navigation validation failed: Audio tab did not move right to Video tab.";
            return false;
        }

        tabAudio.onClick.Invoke();
        if (eventSystem.currentSelectedGameObject != audioFirst.gameObject)
        {
            error = "Settings navigation validation failed: Audio tab activation did not focus the first audio control.";
            return false;
        }

        tabVideo.onClick.Invoke();
        if (eventSystem.currentSelectedGameObject != videoFirst.gameObject)
        {
            error = "Settings navigation validation failed: Video tab activation did not focus the first video control.";
            return false;
        }

        tabControls.onClick.Invoke();
        if (eventSystem.currentSelectedGameObject != firstControl.gameObject)
        {
            error = "Settings navigation validation failed: Controls tab activation did not focus the first controls entry.";
            return false;
        }

        var current = firstControl.gameObject;
        var guardCounter = 0;
        while (current != saveButton.gameObject && guardCounter < 32)
        {
            var before = current;
            if (!MoveSelection(eventSystem, current, MoveDirection.Down, null))
            {
                error = "Settings navigation validation failed: content navigation could not move down toward footer buttons.";
                return false;
            }

            current = eventSystem.currentSelectedGameObject;
            if (current == before)
                break;

            guardCounter++;
        }

        if (current != saveButton.gameObject)
        {
            error = "Settings navigation validation failed: down navigation did not reach APPLY button.";
            return false;
        }

        if (!MoveSelection(eventSystem, saveButton.gameObject, MoveDirection.Right, cancelButton.gameObject))
        {
            error = "Settings navigation validation failed: APPLY button did not move right to CLOSE button.";
            return false;
        }

        if (!MoveSelection(eventSystem, saveButton.gameObject, MoveDirection.Down, tabControls.gameObject))
        {
            error = "Settings navigation validation failed: APPLY button did not loop back down to the active tab.";
            return false;
        }

        return true;
    }

    static bool MoveSelection(EventSystem eventSystem, GameObject start, MoveDirection direction, GameObject expected)
    {
        if (eventSystem == null || start == null)
            return false;

        eventSystem.SetSelectedGameObject(start);
        var axis = new AxisEventData(eventSystem) { moveDir = direction };
        ExecuteEvents.Execute(start, axis, ExecuteEvents.moveHandler);
        var current = eventSystem.currentSelectedGameObject;

        if (expected == null)
            return current != null && current != start;

        return current == expected;
    }

    static UnityEngine.UI.Button FindButton(Transform root, string name)
    {
        var selectable = FindSelectable(root, name);
        return selectable != null ? selectable.GetComponent<UnityEngine.UI.Button>() : null;
    }

    static UnityEngine.UI.Selectable FindSelectable(Transform root, string name)
    {
        if (root == null)
            return null;

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null || transform.name != name)
                continue;

            var selectable = transform.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable != null)
                return selectable;
        }

        return null;
    }

    static bool HasTimedOut(double timeoutSeconds)
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt > timeoutSeconds;
    }

    static bool HasElapsed(double seconds)
    {
        return EditorApplication.timeSinceStartup - _state.stateStartedAt >= seconds;
    }

    static bool ShouldIgnoreLog(string condition, LogType type)
    {
        if (type == LogType.Warning &&
            !string.IsNullOrEmpty(condition) &&
            condition.Contains("doesn't have an Exit Time or any condition"))
        {
            return true;
        }

        if (type == LogType.Warning &&
            !string.IsNullOrEmpty(condition) &&
            condition.Contains("Parameter 'Hit' does not exist"))
        {
            return true;
        }

        return false;
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
                    "Pause UI Scenario Test",
                    $"Errors: {_state.lastStatus.errorCount}\nWarnings: {_state.lastStatus.warningCount}\n\nSee Console for details.",
                    "OK");
            };
        }
    }

    static void Abort(string message)
    {
        _state.errorCount = Mathf.Max(1, _state.errorCount);
        BeginShutdown("Failed", true, BuildAbortDetails(message), "Pause UI scenario failed. Exiting Play Mode.");
    }

    static string BuildAbortDetails(string message)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[PauseUiScenario] " + message);
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
        _pauseRequested = false;
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
        builder.AppendLine("Pause UI scenario test completed.");
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
