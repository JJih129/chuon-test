#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AssetUsageAuditUtility
{
    public readonly struct AuditSummary
    {
        public AuditSummary(int rootCount, int dependencyCount, int candidateCount, string reportPath)
        {
            RootCount = rootCount;
            DependencyCount = dependencyCount;
            CandidateCount = candidateCount;
            ReportPath = reportPath ?? string.Empty;
        }

        public int RootCount { get; }
        public int DependencyCount { get; }
        public int CandidateCount { get; }
        public string ReportPath { get; }
        public string Details => $"Asset usage audit complete. roots={RootCount}, used={DependencyCount}, candidates={CandidateCount}, report={ReportPath}";
    }

    const string ReportPath = "Assets/__AssetUsageAuditReport.txt";
    static readonly string[] RootSceneFolders = { "Assets/Scenes" };
    static readonly string[] RootAlwaysIncludeFolders = { "Assets/Resources" };
    static readonly string[] ProtectedFolders =
    {
        "Assets/Editor",
        "Assets/Plugins",
        "Assets/TextMesh Pro",
        "Assets/JuiceBits",
        "Assets/QuickOutline"
    };

    static readonly HashSet<string> ProtectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".dll", ".asmdef", ".asmref", ".rsp", ".shader", ".shadergraph", ".hlsl", ".cginc",
        ".compute", ".json", ".txt", ".md", ".uss", ".uxml", ".meta"
    };

    [MenuItem("Tools/Project/Generate Asset Usage Audit")]
    public static void GenerateReportFromMenu()
    {
        AuditSummary summary = Run();
        EditorUtility.DisplayDialog("Asset Usage Audit", summary.Details, "OK");
    }

    public static void RunFromCommandLine()
    {
        AuditSummary summary = Run();
        Debug.Log(summary.Details);
    }

    public static AuditSummary RunFromFastMcp()
    {
        AuditSummary summary = Run();
        Debug.Log(summary.Details);
        return summary;
    }

    static AuditSummary Run()
    {
        HashSet<string> rootAssets = CollectRootAssets();
        HashSet<string> usedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rootPath in rootAssets)
        {
            string[] dependencies = AssetDatabase.GetDependencies(rootPath, true);
            for (int i = 0; i < dependencies.Length; i++)
                usedAssets.Add(NormalizeAssetPath(dependencies[i]));
        }

        List<string> candidates = new List<string>(512);
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        for (int i = 0; i < allAssetPaths.Length; i++)
        {
            string path = NormalizeAssetPath(allAssetPaths[i]);
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (AssetDatabase.IsValidFolder(path))
                continue;

            if (rootAssets.Contains(path) || usedAssets.Contains(path))
                continue;

            if (IsProtectedAsset(path))
                continue;

            candidates.Add(path);
        }

        candidates.Sort(StringComparer.OrdinalIgnoreCase);
        WriteReport(rootAssets, usedAssets, candidates);
        AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
        return new AuditSummary(rootAssets.Count, usedAssets.Count, candidates.Count, ReportPath);
    }

    static HashSet<string> CollectRootAssets()
    {
        HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        for (int i = 0; i < allAssetPaths.Length; i++)
        {
            string path = NormalizeAssetPath(allAssetPaths[i]);
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (AssetDatabase.IsValidFolder(path))
                continue;

            if (IsInsideAnyFolder(path, RootSceneFolders) && path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(path);
                continue;
            }

            if (IsInsideAnyFolder(path, RootAlwaysIncludeFolders))
            {
                roots.Add(path);
                continue;
            }
        }

        return roots;
    }

    static bool IsProtectedAsset(string assetPath)
    {
        if (IsInsideAnyFolder(assetPath, ProtectedFolders))
            return true;

        string extension = Path.GetExtension(assetPath);
        if (!string.IsNullOrEmpty(extension) && ProtectedExtensions.Contains(extension))
            return true;

        return false;
    }

    static bool IsInsideAnyFolder(string assetPath, string[] folders)
    {
        for (int i = 0; i < folders.Length; i++)
        {
            string folder = folders[i];
            if (assetPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void WriteReport(HashSet<string> rootAssets, HashSet<string> usedAssets, List<string> candidates)
    {
        StringBuilder sb = new StringBuilder(64 * 1024);
        sb.AppendLine("Asset Usage Audit");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Root assets: {rootAssets.Count}");
        sb.AppendLine($"Used assets: {usedAssets.Count}");
        sb.AppendLine($"Unused candidates: {candidates.Count}");
        sb.AppendLine();

        AppendFolderSummary(sb, candidates);

        sb.AppendLine();
        sb.AppendLine("[Unused Candidates]");
        for (int i = 0; i < candidates.Count; i++)
            sb.AppendLine(candidates[i]);

        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
    }

    static void AppendFolderSummary(StringBuilder sb, List<string> candidates)
    {
        Dictionary<string, int> folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < candidates.Count; i++)
        {
            string folder = Path.GetDirectoryName(candidates[i])?.Replace('\\', '/') ?? "Assets";
            if (string.IsNullOrEmpty(folder))
                folder = "Assets";

            folderCounts.TryGetValue(folder, out int count);
            folderCounts[folder] = count + 1;
        }

        sb.AppendLine("[Folder Summary]");
        foreach (KeyValuePair<string, int> entry in folderCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"{entry.Value,4}  {entry.Key}");
    }

    static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath) ? string.Empty : assetPath.Replace('\\', '/');
    }
}
#endif
