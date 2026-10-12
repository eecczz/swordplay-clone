#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BrokenEdgePlayerMaterialRepair
{
    private const string TargetScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private static readonly string[] BodyMaterialPaths =
    {
        "Assets/Settings/BrokenEdge/TexturedMaterials/5ae02fce3fd08f140888c4d8f1f2cfc3_2100000.mat",
        "Assets/Settings/BrokenEdge/TexturedMaterials/5ae02fce3fd08f140888c4d8f1f2cfc3_2100002.mat",
        "Assets/Settings/BrokenEdge/TexturedMaterials/5ae02fce3fd08f140888c4d8f1f2cfc3_2100004.mat"
    };
    private static readonly string[] SwordMaterialPaths =
    {
        "Assets/Settings/BrokenEdge/TexturedMaterials/1d407a6008b748b4f9fec2ed72ffde73_2100002.mat",
        "Assets/Settings/BrokenEdge/TexturedMaterials/1d407a6008b748b4f9fec2ed72ffde73_2100004.mat",
        "Assets/Settings/BrokenEdge/TexturedMaterials/1d407a6008b748b4f9fec2ed72ffde73_2100006.mat",
        "Assets/Settings/BrokenEdge/TexturedMaterials/1d407a6008b748b4f9fec2ed72ffde73_2100008.mat"
    };

    private static bool repairQueued;

    static BrokenEdgePlayerMaterialRepair()
    {
        EditorApplication.delayCall += QueueRepair;
        EditorSceneManager.sceneOpened += (_, _) => QueueRepair();
    }

    private static void QueueRepair()
    {
        if (repairQueued) return;
        repairQueued = true;
        EditorApplication.delayCall += RepairActivePlayer;
    }

    private static void RepairActivePlayer()
    {
        repairQueued = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueRepair();
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != TargetScenePath) return;

        Material[] bodyMaterials = LoadMaterials(BodyMaterialPaths);
        Material[] swordMaterials = LoadMaterials(SwordMaterialPaths);
        if (bodyMaterials == null || swordMaterials == null)
        {
            Debug.LogError("[Broken Edge] Player material repair stopped because one or more fixed material assets are missing.");
            return;
        }

        int repairedSlots = 0;
        foreach (PlayerWorker player in Object.FindObjectsByType<PlayerWorker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (player.gameObject.scene != scene || !player.gameObject.activeInHierarchy) continue;

            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
            {
                Material[] desired = renderer.name == "WM_Master_Unity" && renderer.sharedMaterials.Length == bodyMaterials.Length
                    ? bodyMaterials
                    : renderer.name == "SM_Katana01" && renderer.sharedMaterials.Length == swordMaterials.Length
                        ? swordMaterials
                        : null;
                if (desired == null) continue;

                Material[] current = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < desired.Length; i++)
                {
                    if (current[i] == desired[i]) continue;
                    current[i] = desired[i];
                    repairedSlots++;
                    changed = true;
                }

                if (!changed) continue;
                renderer.sharedMaterials = current;
                EditorUtility.SetDirty(renderer);
            }
        }

        if (repairedSlots == 0) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Broken Edge] Player material repair complete: restored {repairedSlots} body/sword material slots.");
        SceneView.RepaintAll();
    }

    private static Material[] LoadMaterials(string[] paths)
    {
        var materials = new Material[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            materials[i] = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
            if (materials[i] == null) return null;
        }
        return materials;
    }
}
#endif
