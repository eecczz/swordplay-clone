#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BrokenEdgeImportedCharacterInstaller
{
    private const string TargetScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private static readonly string[] CharacterAssetRoots =
    {
        "Assets/tt-3d/LowPolyMedievalEurope_StarterPack/Character/",
        "Assets/Angel_Knight_Anims/Meshs/Angel Knight/"
    };
    private static bool applyQueued;

    static BrokenEdgeImportedCharacterInstaller()
    {
        EditorApplication.hierarchyChanged += QueueApply;
        QueueApply();
    }

    private static void QueueApply()
    {
        if (applyQueued)
            return;

        applyQueued = true;
        EditorApplication.delayCall += ApplyQueued;
    }

    private static void ApplyQueued()
    {
        applyQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            QueueApply();
            return;
        }

        ApplyToOpenBrokenEdgeScene();
    }

    [MenuItem("Swordplay/Broken Edge/Style Imported Medieval Characters")]
    public static void ApplyToOpenBrokenEdgeScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded ||
            !string.Equals(scene.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
            return;

        HashSet<GameObject> characterRoots = FindImportedCharacterRoots(scene);
        if (characterRoots.Count == 0)
            return;

        int styledRenderers = 0;
        foreach (GameObject root in characterRoots)
            styledRenderers += BrokenEdgeToonMaterialInstaller.ApplyToHierarchy(root);

        if (styledRenderers == 0)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TargetScenePath);
        AssetDatabase.SaveAssets();

        int unresolvedSlots = CountUnresolvedCharacterMaterialSlots(scene);
        Debug.Log($"[Broken Edge] Styled {characterRoots.Count} imported medieval characters " +
                  $"({styledRenderers} renderers) with their original textures. " +
                  $"Unsupported source slots remaining: {unresolvedSlots}.");
    }

    private static HashSet<GameObject> FindImportedCharacterRoots(Scene scene)
    {
        var roots = new HashSet<GameObject>();
        foreach (Renderer renderer in GetSceneRenderers(scene))
        {
            if (!UsesImportedCharacterMaterial(renderer))
                continue;

            GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
            roots.Add(prefabRoot != null ? prefabRoot : FindTopmostCharacterObject(renderer.transform));
        }
        return roots;
    }

    private static GameObject FindTopmostCharacterObject(Transform transform)
    {
        Transform candidate = transform;
        while (candidate.parent != null && candidate.parent.gameObject.scene == transform.gameObject.scene)
            candidate = candidate.parent;
        return candidate.gameObject;
    }

    private static bool UsesImportedCharacterMaterial(Renderer renderer)
    {
        return renderer.sharedMaterials.Any(IsImportedCharacterMaterial);
    }

    private static bool IsImportedCharacterMaterial(Material material)
    {
        if (material == null)
            return false;

        string path = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
        return CharacterAssetRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountUnresolvedCharacterMaterialSlots(Scene scene)
    {
        int count = 0;
        foreach (Renderer renderer in GetSceneRenderers(scene))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (IsImportedCharacterMaterial(material))
                    count++;
            }
        }
        return count;
    }

    private static IEnumerable<Renderer> GetSceneRenderers(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .Where(renderer => !(renderer is ParticleSystemRenderer) &&
                               !(renderer is TrailRenderer) &&
                               !(renderer is LineRenderer));
    }
}
#endif
