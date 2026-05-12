#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot editor bridge for running the static sword trail installer in an already-open Unity Editor.
/// Delete or ignore Temp/SwordTrailStaticInstaller.request to disable the automatic run.
/// </summary>
[InitializeOnLoad]
public static class SwordTrailStaticInstallerAutoRunner
{
    const string RequestPath = "Temp/SwordTrailStaticInstaller.request";

    static SwordTrailStaticInstallerAutoRunner()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    static void RunIfRequested()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunIfRequested;
            return;
        }

        if (!File.Exists(RequestPath))
            return;

        try
        {
            File.Delete(RequestPath);
            SwordTrailStaticInstaller.InstallAllFromAutomation();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
#endif
