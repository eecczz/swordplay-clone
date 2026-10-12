#if UNITY_EDITOR
using Swordplay.Visuals;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BrokenEdgeBladeVfxInstaller
{
    private const string TargetScene = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string DungeonScene = "Assets/Fantastic Dungeon Pack/scenes/demoscene_dungeon_level_1_dungeon.unity";
    [MenuItem("Swordplay/Install Broken Edge Blade VFX")]
    private static void InstallFromMenu() => InstallInOpenScene();

    private static void InstallInOpenScene()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || (scene.path != TargetScene && scene.path != DungeonScene)) return;

        bool changed = false;
        var configuredEffects = new HashSet<BrokenEdgeBladeVfx>();
        int repairedMaterialSlots = BrokenEdgeMaterialRestorer.RestoreMissingMaterials(false);
        BrokenEdgeToonMaterialInstaller.ApplyToBrokenEdgeScene(false);
        PlayerWorker[] players = Object.FindObjectsByType<PlayerWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // Persist the texture-preserving toon variants on every player before
        // the scene-wide transient style pass. This makes freshly imported or
        // re-enabled characters match the map in both Scene view and Play mode.
        int persistedPlayerRenderers = 0;
        foreach (PlayerWorker player in players)
            persistedPlayerRenderers += BrokenEdgeToonMaterialInstaller.ApplyToHierarchy(player.gameObject);
        changed |= repairedMaterialSlots > 0 || persistedPlayerRenderers > 0;

        // Rebuild the transient toon materials whenever the player setup changes.
        // This keeps newly imported/re-enabled character renderers in the same
        // Broken Edge palette without altering their source textures or materials.
        BrokenEdgeStyle sceneStyle = Object.FindFirstObjectByType<BrokenEdgeStyle>(FindObjectsInactive.Include);
        if (sceneStyle != null && sceneStyle.isActiveAndEnabled)
            sceneStyle.ApplyStyle();

        // Upgrade already-installed weapons in the current scene first.  This also
        // covers the imported dungeon character whose weapon setup is not one of
        // the original PlayerWorker variants.
        BrokenEdgeBladeVfx[] existingEffects = Object.FindObjectsByType<BrokenEdgeBladeVfx>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (BrokenEdgeBladeVfx effect in existingEffects)
        {
            Transform owner = effect.GetComponentInParent<PlayerWorker>()?.transform;
            if (owner == null) owner = effect.transform.parent != null ? effect.transform.parent : effect.transform.root;
            bool effectChanged = effect.Configure(owner);
            effectChanged |= effect.EnsureBladeOnlyMarkers();
            configuredEffects.Add(effect);
            if (!effectChanged) continue;
            EditorUtility.SetDirty(effect);
            EditorUtility.SetDirty(effect.gameObject);
            changed = true;
        }

        // Install on inactive player variants too; whichever variant is enabled later
        // already has the edit-mode aura and the runtime trail ready.
        foreach (PlayerWorker player in players)
        {
            TargetMatching weapon = player.GetComponentInChildren<TargetMatching>(true);
            if (weapon == null) continue;

            SerializedObject playerData = new SerializedObject(player);
            SerializedProperty swordProperty = playerData.FindProperty("sword");
            if (swordProperty != null && swordProperty.objectReferenceValue == null)
            {
                swordProperty.objectReferenceValue = weapon.transform;
                playerData.ApplyModifiedProperties();
                EditorUtility.SetDirty(player);
                changed = true;
            }

            BrokenEdgeBladeVfx effect = weapon.GetComponent<BrokenEdgeBladeVfx>();
            bool added = effect == null;
            if (added) effect = Undo.AddComponent<BrokenEdgeBladeVfx>(weapon.gameObject);
            if (configuredEffects.Contains(effect)) continue;

            bool configured = effect.Configure(player.transform);
            configured |= effect.EnsureBladeOnlyMarkers();
            if (!added && !configured) continue;
            EditorUtility.SetDirty(effect);
            EditorUtility.SetDirty(weapon.gameObject);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        SceneView.RepaintAll();
        PlayerWorker activePlayer = null;
        PlayerWorker[] auditPlayers = Object.FindObjectsByType<PlayerWorker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (PlayerWorker player in auditPlayers)
        {
            if (player.gameObject.scene == scene && player.gameObject.activeInHierarchy)
            {
                activePlayer = player;
                break;
            }
        }

        int styledRenderers = 0;
        var rendererAudit = new List<string>();
        if (activePlayer != null)
        {
            foreach (Renderer renderer in activePlayer.GetComponentsInChildren<Renderer>(true))
            {
                bool usesBrokenEdgeShader = false;
                var shaderNames = new List<string>();
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        shaderNames.Add("<missing material>");
                        continue;
                    }
                    if (material.shader == null)
                    {
                        shaderNames.Add(material.name + ":<missing shader>");
                        continue;
                    }
                    string shaderName = material.shader.name;
                    shaderNames.Add(material.name + ":" + shaderName);
                    if (shaderName == "Swordplay/Broken Edge Textured Surface" ||
                        shaderName == "Swordplay/Broken Edge Dungeon")
                    {
                        usesBrokenEdgeShader = true;
                        break;
                    }
                }

                if (usesBrokenEdgeShader) styledRenderers++;
                rendererAudit.Add(renderer.name + " [" + string.Join(", ", shaderNames) + "]");
            }
        }
        int bladeEffects = activePlayer == null ? 0 : activePlayer.GetComponentsInChildren<BrokenEdgeBladeVfx>(true).Length;
        Debug.Log($"[Broken Edge] Active player '{activePlayer?.name ?? "none"}': styled renderers={styledRenderers}, blade effects={bladeEffects}; player renderers persisted={persistedPlayerRenderers}, repaired material slots={repairedMaterialSlots}.");
        if (styledRenderers == 0 && rendererAudit.Count > 0)
            Debug.LogWarning("[Broken Edge] Active player renderer audit: " + string.Join(" | ", rendererAudit));
    }
}
#endif
