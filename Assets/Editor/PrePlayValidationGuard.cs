using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PrePlayValidationGuard
{
    const string EnabledPrefKey = "ProjectChuOn.Validation.AutoRunBeforePlay";
    static bool _isRunning;
    static bool _isCancellingPlay;

    public static bool SuppressForAutomation { get; set; }

    static PrePlayValidationGuard()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.delayCall += RefreshMenuCheckmark;
    }

    static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPrefKey, true);
        set => EditorPrefs.SetBool(EnabledPrefKey, value);
    }

    [MenuItem("Tools/Validation/Auto Run Checks Before Play")]
    static void ToggleAutoRunBeforePlay()
    {
        Enabled = !Enabled;
        RefreshMenuCheckmark();
    }

    [MenuItem("Tools/Validation/Auto Run Checks Before Play", true)]
    static bool ToggleAutoRunBeforePlayValidate()
    {
        RefreshMenuCheckmark();
        return true;
    }

    static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            RunPreflightIfNeeded();
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode)
            _isCancellingPlay = false;
    }

    static void RunPreflightIfNeeded()
    {
        if (!Enabled || SuppressForAutomation || _isRunning || _isCancellingPlay)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        _isRunning = true;
        try
        {
            var pauseSyncSummary = PauseSettingsContentSyncUtility.RunFromFastMcp();
            var syncSummary = PlayerWiringSyncUtility.RunFromFastMcp();
            var validationSummary = GameplayRegressionValidator.RunFromFastMcp();

            if (validationSummary.HasErrors)
            {
                _isCancellingPlay = true;
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += delegate
                {
                    EditorUtility.DisplayDialog(
                        "Play Mode Blocked",
                        BuildBlockedMessage(pauseSyncSummary, syncSummary, validationSummary),
                        "OK");
                };
                return;
            }

            if (validationSummary.WarningCount > 0)
            {
                Debug.LogWarning(
                    "[Validation] Pre-play checks passed with warnings.\n"
                    + validationSummary.Details
                    + "\n"
                    + pauseSyncSummary.Details
                    + "\n"
                    + syncSummary.Details);
            }
        }
        finally
        {
            _isRunning = false;
        }
    }

    static string BuildBlockedMessage(
        PauseSettingsContentSyncUtility.SyncSummary pauseSyncSummary,
        PlayerWiringSyncUtility.SyncSummary syncSummary,
        GameplayRegressionValidator.ValidationSummary validationSummary)
    {
        return "Pause settings content sync, player wiring sync, and gameplay regression checks found errors.\n\n"
            + validationSummary.Details
            + "\n"
            + pauseSyncSummary.Details
            + "\n"
            + syncSummary.Details;
    }

    static void RefreshMenuCheckmark()
    {
        Menu.SetChecked("Tools/Validation/Auto Run Checks Before Play", Enabled);
    }
}
