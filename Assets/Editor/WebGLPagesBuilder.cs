using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLPagesBuilder
{
    private const string OutputPath = "docs";
    private const int InitialMemorySizeMb = 256;

    [MenuItem("Swordplay/Build WebGL Pages %&w")]
    public static void BuildWebGLPages()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes are configured for the build.");

        WebGLCompressionFormat previousCompression = PlayerSettings.WebGL.compressionFormat;
        bool previousFallback = PlayerSettings.WebGL.decompressionFallback;
        int previousInitialMemorySize = PlayerSettings.WebGL.initialMemorySize;

        try
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            // Give the full scene enough contiguous WASM heap up front. This avoids
            // startup-time heap resizing while textures, meshes, and physics data load.
            PlayerSettings.WebGL.initialMemorySize = InitialMemorySizeMb;

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("Could not switch the active build target to WebGL.");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"WebGL build failed: {report.summary.result}");

            Debug.Log($"WebGL Pages build succeeded: {OutputPath} ({report.summary.totalSize} bytes)");
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = previousCompression;
            PlayerSettings.WebGL.decompressionFallback = previousFallback;
            PlayerSettings.WebGL.initialMemorySize = previousInitialMemorySize;
        }
    }
}
