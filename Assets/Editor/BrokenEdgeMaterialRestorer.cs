#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BrokenEdgeMaterialRestorer
{
    private const string TargetScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
    private static bool restoreRunning;

    [MenuItem("Swordplay/Repair Broken Edge Missing Materials")]
    public static void RestoreFromMenu()
    {
        RestoreMissingMaterials(true);
    }

    public static int RestoreMissingMaterials(bool openTargetIfNeeded)
    {
        if (restoreRunning || EditorApplication.isPlayingOrWillChangePlaymode)
            return 0;

        restoreRunning = true;
        Scene sourceScene = default;
        int repairedSlots = 0;
        try
        {
            Scene targetScene = SceneManager.GetActiveScene();
            if (!targetScene.IsValid() || !string.Equals(targetScene.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!openTargetIfNeeded) return 0;
                targetScene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            }

            sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
            Dictionary<string, Renderer> sourceRenderers = GetRenderers(sourceScene)
                .GroupBy(GetStableId)
                .ToDictionary(group => group.Key, group => group.First());

            int repairedRenderers = 0;
            foreach (Renderer targetRenderer in GetRenderers(targetScene))
            {
                Material[] targetMaterials = targetRenderer.sharedMaterials;
                if (!targetMaterials.Any(material => material == null)) continue;

                string stableId = GetStableId(targetRenderer);
                if (!sourceRenderers.TryGetValue(stableId, out Renderer sourceRenderer)) continue;
                if (sourceRenderer.GetType() != targetRenderer.GetType()) continue;
                if (!string.Equals(sourceRenderer.gameObject.name, targetRenderer.gameObject.name, StringComparison.Ordinal)) continue;

                Material[] sourceMaterials = sourceRenderer.sharedMaterials;
                if (sourceMaterials.Length != targetMaterials.Length) continue;

                bool changed = false;
                for (int i = 0; i < targetMaterials.Length; i++)
                {
                    if (targetMaterials[i] != null || sourceMaterials[i] == null) continue;
                    targetMaterials[i] = sourceMaterials[i];
                    repairedSlots++;
                    changed = true;
                }

                if (!changed) continue;
                targetRenderer.sharedMaterials = targetMaterials;
                EditorUtility.SetDirty(targetRenderer);
                repairedRenderers++;
            }

            if (repairedRenderers > 0)
            {
                EditorSceneManager.MarkSceneDirty(targetScene);
                EditorSceneManager.SaveScene(targetScene, TargetScenePath);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[Broken Edge] Material repair complete: {repairedSlots} slots restored on {repairedRenderers} renderers from SampleScene.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (sourceScene.IsValid() && sourceScene.isLoaded)
                EditorSceneManager.CloseScene(sourceScene, true);
            restoreRunning = false;
        }
        return repairedSlots;
    }

    private static IEnumerable<Renderer> GetRenderers(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true));
    }

    private static string GetStableId(Renderer renderer)
    {
        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(renderer);
        return $"{id.targetObjectId}:{id.targetPrefabId}";
    }
}
#endif
