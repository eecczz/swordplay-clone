using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class CodexBuildAndRunOnce
{
    private const string RequestPath = "Library/CodexBuildAndRunOnce.request";
    private const string ResultPath = "Library/CodexBuildAndRunOnce.result";

    static CodexBuildAndRunOnce()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += TryRun;
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
}
