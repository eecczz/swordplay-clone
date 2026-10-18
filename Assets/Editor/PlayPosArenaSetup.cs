using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayPosArenaSetup
{
    private static readonly string[] ArmorGroups =
    {
        "Head Armor Type ", "Chest Armor Type ", "Arm Armor Type ",
        "Belt Armor Type ", "Legs Armor Type ", "Feet Armor Type "
    };

    [MenuItem("Tools/Swordplay/Apply PlayPos Arena Setup")]
    public static void Apply()
    {
        Transform playPos = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(value => value.name.Equals("PlayPos", StringComparison.OrdinalIgnoreCase));
        PlayerWorker player = UnityEngine.Object.FindFirstObjectByType<PlayerWorker>(FindObjectsInactive.Include);
        ArenaGameDirector director = UnityEngine.Object.FindFirstObjectByType<ArenaGameDirector>(FindObjectsInactive.Include);
        if (playPos == null || player == null || director == null)
        {
            Debug.LogError("PlayPos arena setup requires PlayPos, PlayerWorker, and ArenaGameDirector in the open scene.");
            return;
        }

        SerializedObject directorObject = new(director);
        SerializedProperty poolProperty = directorObject.FindProperty("enemyPool");
        List<EnemyWorker> pool = new();
        for (int i = 0; i < poolProperty.arraySize; i++)
        {
            EnemyWorker enemy = poolProperty.GetArrayElementAtIndex(i).objectReferenceValue as EnemyWorker;
            if (enemy != null && !pool.Contains(enemy)) pool.Add(enemy);
        }

        Undo.RecordObject(player.transform, "Move player to PlayPos");
        Vector3 center = GroundPoint(playPos.position, player.gameObject);
        player.transform.SetPositionAndRotation(center, playPos.rotation);

        const float spawnRadius = 9f;
        for (int i = 0; i < pool.Count; i++)
        {
            EnemyWorker enemy = pool[i];
            Undo.RegisterFullObjectHierarchyUndo(enemy.gameObject, "Configure arena enemy");
            float angle = i * Mathf.PI * 2f / Mathf.Max(1, pool.Count);
            Vector3 radial = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 position = GroundPoint(center + radial * spawnRadius, enemy.gameObject);
            enemy.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation((center - position).normalized, Vector3.up));
            ConfigureUniqueArmor(enemy, i);
            enemy.gameObject.SetActive(i < 5);
        }

        SerializedProperty initialCount = directorObject.FindProperty("initialActiveEnemies");
        initialCount.intValue = Mathf.Min(5, pool.Count);
        SerializedProperty recycleDelay = directorObject.FindProperty("recycleDelay");
        recycleDelay.floatValue = 0.35f;
        directorObject.ApplyModifiedProperties();

        MatchGroundPatchMaterial(playPos, center, player.gameObject, pool.Select(value => value.gameObject));
        UnityEngine.SceneManagement.Scene activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"PlayPos arena setup applied: player + {pool.Count} pooled enemies, 5 active, unique armor combinations.");
    }

    private static void ConfigureUniqueArmor(EnemyWorker enemy, int enemyIndex)
    {
        Transform[] descendants = enemy.GetComponentsInChildren<Transform>(true);
        for (int groupIndex = 0; groupIndex < ArmorGroups.Length; groupIndex++)
        {
            string prefix = ArmorGroups[groupIndex];
            List<GameObject> options = descendants
                .Where(value => value.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(value => value.gameObject)
                .OrderBy(value => value.name)
                .ToList();
            if (options.Count == 0) continue;
            int chosen = groupIndex == 0
                ? enemyIndex % options.Count
                : (enemyIndex * (groupIndex * 2 + 1) + groupIndex * 3) % options.Count;
            for (int i = 0; i < options.Count; i++)
                options[i].SetActive(i == chosen);
        }
    }

    private static Vector3 GroundPoint(Vector3 requested, GameObject ignoredRoot)
    {
        Physics.SyncTransforms();
        RaycastHit[] hits = Physics.RaycastAll(requested + Vector3.up * 50f, Vector3.down, 120f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits.OrderBy(value => value.distance))
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(ignoredRoot.transform)) continue;
            if (hit.collider.GetComponentInParent<PlayerWorker>() != null) continue;
            if (hit.collider.GetComponentInParent<EnemyWorker>() != null) continue;
            return new Vector3(requested.x, hit.point.y + 0.03f, requested.z);
        }
        return requested;
    }

    private static void MatchGroundPatchMaterial(Transform playPos, Vector3 center,
        GameObject player, IEnumerable<GameObject> enemyRoots)
    {
        HashSet<GameObject> characters = enemyRoots.Append(player).ToHashSet();
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        Renderer patch = renderers
            .Where(value => value != null && !characters.Any(root => value.transform.IsChildOf(root.transform)))
            .Where(value => value.name.IndexOf("cube", StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(value => HorizontalDistance(value.bounds.center, center) < 18f)
            .Where(value => value.bounds.size.x >= 6f && value.bounds.size.z >= 6f)
            .OrderBy(value => HorizontalDistance(value.bounds.center, center))
            .FirstOrDefault();
        if (patch == null) return;

        Renderer source = renderers
            .Where(value => value != null && value != patch && value.sharedMaterial != null)
            .Where(value => !characters.Any(root => value.transform.IsChildOf(root.transform)))
            .Where(value => HorizontalDistance(value.bounds.center, center) < 45f)
            .Where(value => value.bounds.size.x > 4f && value.bounds.size.z > 4f)
            .OrderBy(value => HorizontalDistance(value.bounds.center, center))
            .FirstOrDefault();
        if (source == null) return;

        Undo.RecordObject(patch, "Match arena ground material");
        patch.sharedMaterials = source.sharedMaterials;
        Debug.Log($"Arena ground patch '{patch.name}' now uses surrounding material from '{source.name}'.", patch);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
        => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
}
