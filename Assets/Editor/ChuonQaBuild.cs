using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Unattended dev/QA player build (menu or MCP: ChuonQaBuild.Build("QA_IterX")). Never runs by itself.
/// Saves the open scenes first, so a scene that Unity merely flagged dirty (Canvas/CanvasRenderer after a domain
/// reload, no content change) can never pop the blocking "Scene(s) Have Been Modified" dialog, then builds the
/// enabled build-settings scenes as a Development player into Builds/&lt;name&gt;/ChuOn.exe and writes
/// Temp/qa/build_report.txt.
/// </summary>
public static class ChuonQaBuild
{
    const string MenuPath = "Tools/Chuon/Build/QA Dev Build (save scenes + build)";

    [MenuItem(MenuPath)]
    static void BuildFromMenu()
    {
        Debug.Log("[ChuonQaBuild] " + Build("QA_Latest"));
    }

    public static string Build(string folderName)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "PLAYING (exit Play mode first)";

        bool saved = EditorSceneManager.SaveOpenScenes();

        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s.enabled)
                scenes.Add(s.path);
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(projectRoot, "Builds", folderName, "ChuOn.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        BuildReport report = BuildPipeline.BuildPlayer(options);
        watch.Stop();

        var sb = new StringBuilder();
        sb.Append("scenesSaved=").Append(saved)
          .Append(" result=").Append(report.summary.result)
          .Append(" size=").Append((report.summary.totalSize / 1048576.0).ToString("F1")).Append("MB")
          .Append(" time=").Append(watch.Elapsed.TotalSeconds.ToString("F0")).Append("s")
          .Append(" errors=").Append(report.summary.totalErrors)
          .Append(" warnings=").Append(report.summary.totalWarnings).Append('\n');
        int lines = 0;
        foreach (BuildStep step in report.steps)
        {
            foreach (BuildStepMessage m in step.messages)
            {
                if (m.type == LogType.Log || lines++ > 60)
                    continue;
                string content = m.content.Length > 300 ? m.content.Substring(0, 300) : m.content;
                sb.Append('[').Append(m.type).Append("] ").Append(content).Append('\n');
            }
        }

        string qaDir = Path.Combine(projectRoot, "Temp", "qa");
        Directory.CreateDirectory(qaDir);
        File.WriteAllText(Path.Combine(qaDir, "build_report.txt"), sb.ToString());
        return sb.ToString();
    }
}
