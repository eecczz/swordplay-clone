using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class BrokenEdgeNavMeshBaker
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string DataDirectory = "Assets/Scenes/BrokenEdgeLaunch";
    private const string DataPath = DataDirectory + "/NavMesh-Gameplay.asset";
    private const string SurfaceName = "Gameplay NavMesh";
    private const string StairLinksName = "Generated Stair Links";

    [MenuItem("Tools/Broken Edge/Bake Gameplay NavMesh %#g")]
    public static void BakeLaunchScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject surfaceObject = GameObject.Find(SurfaceName);
        if (surfaceObject == null)
            surfaceObject = new GameObject(SurfaceName);

        NavMeshSurface surface = surfaceObject.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = surfaceObject.AddComponent<NavMeshSurface>();

        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.All;
        // The level's stairs are modular render meshes; simplified colliders leave
        // gaps between treads in several sections of this scene.
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8) | (1 << 9));
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.05f;
        surface.overrideTileSize = true;
        surface.tileSize = 256;
        surface.minRegionArea = 0.25f;
        surface.buildHeightMesh = true;

        NavMeshData oldData = surface.navMeshData;
        surface.RemoveData();
        surface.navMeshData = null;
        if (oldData != null && AssetDatabase.Contains(oldData))
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(oldData));
        else if (AssetDatabase.LoadAssetAtPath<NavMeshData>(DataPath) != null)
            AssetDatabase.DeleteAsset(DataPath);

        surface.BuildNavMesh();
        if (surface.navMeshData == null)
            throw new System.InvalidOperationException("Gameplay NavMesh bake returned no data.");

        int stairLinkCount = BuildStairLinks(surfaceObject.transform);

        if (!Directory.Exists(DataDirectory))
            Directory.CreateDirectory(DataDirectory);
        AssetDatabase.CreateAsset(surface.navMeshData, DataPath);
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateBake();
        Debug.Log($"Gameplay NavMesh baked and validated: {DataPath} ({stairLinkCount} stair links)");
    }

    private static int BuildStairLinks(Transform surfaceRoot)
    {
        Transform oldRoot = surfaceRoot.Find(StairLinksName);
        if (oldRoot != null)
            Object.DestroyImmediate(oldRoot.gameObject);

        GameObject linksObject = new(StairLinksName);
        linksObject.transform.SetParent(surfaceRoot, false);
        int created = 0;

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (Transform stair in transforms)
        {
            if (!stair.name.StartsWith("stairs", System.StringComparison.OrdinalIgnoreCase))
                continue;

            Renderer[] renderers = stair.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                continue;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float horizontalLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (bounds.size.y < 0.3f || horizontalLength > 18f)
                continue;

            Vector3 axis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
            float extent = bounds.size.x >= bounds.size.z ? bounds.extents.x : bounds.extents.z;
            Vector3 probeA = bounds.center - axis * Mathf.Max(0.2f, extent - 0.15f);
            Vector3 probeB = bounds.center + axis * Mathf.Max(0.2f, extent - 0.15f);
            float sampleRadius = Mathf.Max(1f, bounds.size.y + 0.5f);

            if (!NavMesh.SamplePosition(probeA, out NavMeshHit hitA, sampleRadius, NavMesh.AllAreas)
                    || !NavMesh.SamplePosition(probeB, out NavMeshHit hitB, sampleRadius, NavMesh.AllAreas)
                    || Mathf.Abs(hitA.position.y - hitB.position.y) < 0.2f)
                continue;

            GameObject linkObject = new($"Stair Link {created + 1}");
            linkObject.transform.SetParent(linksObject.transform, false);
            linkObject.transform.position = Vector3.zero;
            NavMeshLink link = linkObject.AddComponent<NavMeshLink>();
            link.agentTypeID = 0;
            link.startPoint = hitA.position;
            link.endPoint = hitB.position;
            link.width = Mathf.Max(0.5f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.6f);
            link.bidirectional = true;
            link.autoUpdate = false;
            link.UpdateLink();
            created++;
        }

        return created;
    }

    private static void ValidateBake()
    {
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            throw new System.InvalidOperationException("Gameplay NavMesh contains no walkable polygons.");

        PlayerWorker player = Object.FindFirstObjectByType<PlayerWorker>(FindObjectsInactive.Exclude);
        EnemyWorker[] enemies = Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        if (player == null || enemies.Length == 0)
        {
            Debug.LogWarning("NavMesh geometry is valid, but no active player/enemy pair was available for path validation.");
            return;
        }

        if (!NavMesh.SamplePosition(player.transform.position, out NavMeshHit playerHit, 5f,
                NavMesh.AllAreas))
            throw new System.InvalidOperationException("Active player is not near the baked NavMesh.");

        int completePaths = 0;
        foreach (EnemyWorker enemy in enemies)
        {
            if (!NavMesh.SamplePosition(enemy.transform.position, out NavMeshHit enemyHit, 5f,
                    NavMesh.AllAreas))
                continue;

            NavMeshPath path = new();
            if (NavMesh.CalculatePath(enemyHit.position, playerHit.position, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                completePaths++;
        }

        if (completePaths == 0)
            throw new System.InvalidOperationException("No active enemy has a complete NavMesh path to the player.");

        Debug.Log($"NavMesh validation passed: {triangulation.vertices.Length} vertices, " +
            $"{completePaths}/{enemies.Length} active enemies have a complete path to the player.");
    }
}
