using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class CodexBuildAndRunOnce
{
    // File-triggered entry points support both setup builds and direct builds without resaving scenes.
    private const string RequestPath = "Library/CodexBuildAndRunOnce.request";
    private const string ResultPath = "Library/CodexBuildAndRunOnce.result";
    private const string CleanRequestPath = "Library/CodexCleanBuildAndRunOnce.request";
    private const string CleanResultPath = "Library/CodexCleanBuildAndRunOnce.result";
    private const string LaunchScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string WindowsBuildPath = "C:/Users/황선우/Desktop/Build/swordplay.exe";

    static CodexBuildAndRunOnce()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += TryRun;
        if (File.Exists(CleanRequestPath))
            EditorApplication.delayCall += TryCleanBuildAndRun;
    }

    private static void TryRun()
    {
        if (!File.Exists(RequestPath))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRun;
            return;
        }

        File.Delete(RequestPath);
        try
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            WindowsBuildAndSkyFix.BuildAndRunWindows();
            File.WriteAllText(ResultPath, $"Succeeded\n{DateTime.Now:O}\n");
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, $"Failed\n{DateTime.Now:O}\n{exception}\n");
            Debug.LogException(exception);
        }
    }

    private static void TryCleanBuildAndRun()
    {
        if (!File.Exists(CleanRequestPath))
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
        {
            EditorApplication.delayCall += TryCleanBuildAndRun;
            return;
        }

        File.Delete(CleanRequestPath);
        try
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (!scenes.Contains(LaunchScenePath, StringComparer.OrdinalIgnoreCase))
                scenes = new[] { LaunchScenePath }.Concat(scenes).ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(WindowsBuildPath) ?? ".");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = WindowsBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            var summary = report.summary;
            File.WriteAllText(CleanResultPath,
                $"{summary.result}\n{DateTime.Now:O}\n" +
                $"Errors={summary.totalErrors}\nWarnings={summary.totalWarnings}\n" +
                $"Size={summary.totalSize}\nDuration={summary.totalTime}\nPath={WindowsBuildPath}\n");

            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows build failed: {summary.result} ({summary.totalErrors} errors)");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = WindowsBuildPath,
                WorkingDirectory = Path.GetDirectoryName(WindowsBuildPath) ?? ".",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            if (!File.Exists(CleanResultPath))
                File.WriteAllText(CleanResultPath, $"Failed\n{DateTime.Now:O}\n{exception}\n");
            Debug.LogException(exception);
        }
    }
}
