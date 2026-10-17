using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class WindowsBuildAndSkyFix
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string SkyMaterialPath = "Assets/Settings/BrokenEdge/BrokenEdgeSky.mat";
    private const string BuildPath = "C:/Users/황선우/Desktop/Build/swordplay.exe";
    private const string ReportPath = "Library/WindowsBuildAndSkyFixReport.txt";
    private const string EnemyMaterialFolder = "Assets/Materials/Enemy/Bow";

    [MenuItem("Tools/Swordplay/Fix Standalone Sky")]
    public static void FixStandaloneSky()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Shader shader = Shader.Find("Swordplay/Broken Edge Sky");
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
        if (shader == null || sky == null)
            throw new InvalidOperationException("Persistent Broken Edge sky shader/material is missing.");

        sky.shader = shader;
        // Match the palette that was previously visible only on the generated editor material.
        sky.SetColor("_TopColor", new Color(0.48f, 0.66f, 0.42f, 1f));
        sky.SetColor("_HorizonColor", new Color(0.78f, 0.86f, 0.65f, 1f));
        sky.SetColor("_BottomColor", new Color(0.94f, 0.69f, 0.67f, 1f));
        sky.SetFloat("_HorizonSharpness", 1.15f);
        sky.SetColor("_SunColor", new Color(1f, 0.90f, 0.73f, 1f));
        sky.SetFloat("_SunStrength", 0.18f);
        EditorUtility.SetDirty(sky);

        RenderSettings.skybox = sky;
        BrokenEdgeStyle style = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<BrokenEdgeStyle>(true))
            .FirstOrDefault();
        if (style == null)
            throw new InvalidOperationException("BrokenEdgeStyle was not found in the launch scene.");
        var serialized = new SerializedObject(style);
        serialized.FindProperty("skyShader").objectReferenceValue = shader;
        serialized.FindProperty("skyMaterial").objectReferenceValue = sky;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        foreach (Camera camera in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
        {
            if (camera.TryGetComponent(out UniversalAdditionalCameraData data) && data.renderType == CameraRenderType.Overlay)
                continue;
            camera.clearFlags = CameraClearFlags.Skybox;
            EditorUtility.SetDirty(camera);
        }

        EditorUtility.SetDirty(style);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        string report =
            $"Scene={scene.path}\n" +
            $"Skybox={AssetDatabase.GetAssetPath(RenderSettings.skybox)}\n" +
            $"Shader={RenderSettings.skybox?.shader?.name}\n" +
            $"StyleSkyMaterialExact={serialized.FindProperty("skyMaterial").objectReferenceValue == sky}\n" +
            $"BaseCamerasSkybox={scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Count(camera => camera.clearFlags == CameraClearFlags.Skybox)}\n";
        File.WriteAllText(ReportPath, report);
        Debug.Log("[Standalone Sky Fix] Persistent skybox serialized into scene and runtime style.");
    }

    [MenuItem("Tools/Swordplay/Build And Run Windows")]
    public static void BuildAndRunWindows()
    {
        GroundActivePlayerToSurface();
        BrokenEdgeNavMeshBaker.BakeLaunchScene();
        FixStandaloneSky();
        Scene launchScene = SceneManager.GetActiveScene();
        bool hasActiveBowEnemy = launchScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .Any(enemy => enemy.gameObject.activeInHierarchy &&
                          enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase));
        if (hasActiveBowEnemy)
            KnightEnemyInstaller.InstallRocketOnActiveBowEnemy();
        else
            Debug.Log("[Windows Build] No active BOW enemy; preserving the modular roster's existing rocket VFX.");
        CreateIndependentBowEnemyMaterials();

        string[] scenes = EditorBuildSettings.scenes
            .Where(value => value.enabled)
            .Select(value => value.path)
            .ToArray();
        if (!scenes.Contains(ScenePath))
            scenes = new[] { ScenePath }.Concat(scenes).Distinct().ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(BuildPath) ?? throw new InvalidOperationException("Invalid build path."));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = BuildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.AutoRunPlayer
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Windows build failed: {report.summary.result}, errors={report.summary.totalErrors}");

        File.AppendAllText(ReportPath,
            $"BuildResult={report.summary.result}\n" +
            $"BuildPath={BuildPath}\n" +
            $"BuildSize={report.summary.totalSize}\n" +
            $"BuildErrors={report.summary.totalErrors}\n");
        Debug.Log($"[Windows Build] COMPLETE — {BuildPath}, {report.summary.totalSize} bytes");
    }

    [MenuItem("Tools/Swordplay/Ground Active Player")]
    public static void GroundActivePlayerToSurface()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        PlayerWorker[] activePlayers = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PlayerWorker>(true))
            .Where(value => value.gameObject.activeInHierarchy)
            .ToArray();
        PlayerWorker player = activePlayers.FirstOrDefault(value => value.CompareTag("Player"))
            ?? activePlayers.FirstOrDefault();
        if (player == null)
            throw new InvalidOperationException("No active PlayerWorker was found in the launch scene.");

        Transform playerTransform = player.transform;
        CapsuleCollider movementCapsule = player.GetComponent<CapsuleCollider>();
        CharacterController controller = player.GetComponent<CharacterController>();
        float currentBottom;
        if (movementCapsule != null)
        {
            Vector3 worldCenter = playerTransform.TransformPoint(movementCapsule.center);
            float verticalScale = Mathf.Abs(playerTransform.lossyScale.y);
            currentBottom = worldCenter.y - movementCapsule.height * verticalScale * 0.5f;
        }
        else if (controller != null)
        {
            Vector3 worldCenter = playerTransform.TransformPoint(controller.center);
            float verticalScale = Mathf.Abs(playerTransform.lossyScale.y);
            currentBottom = worldCenter.y - controller.height * verticalScale * 0.5f;
        }
        else
        {
            currentBottom = playerTransform.position.y;
        }

        Collider[] ownColliders = player.GetComponentsInChildren<Collider>(true);
        bool[] enabledStates = ownColliders.Select(value => value.enabled).ToArray();
        for (int index = 0; index < ownColliders.Length; index++)
            ownColliders[index].enabled = false;
        Physics.SyncTransforms();

        Vector3 rayOrigin = new Vector3(playerTransform.position.x, currentBottom + 500f, playerTransform.position.z);
        RaycastHit[] candidates = Physics.RaycastAll(rayOrigin, Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit? groundHit = candidates
            .Where(hit => hit.normal.y >= Mathf.Cos(60f * Mathf.Deg2Rad) && IsWalkableEnvironmentSurface(hit.collider))
            .OrderBy(hit => Mathf.Abs(hit.point.y - currentBottom))
            .Cast<RaycastHit?>()
            .FirstOrDefault();

        // Some edited showcase scenes leave the active player completely outside the floor
        // footprint, so changing Y alone can never find ground. In that case, locate the
        // nearest upward-facing collider surface and move XZ to that surface as well.
        if (!groundHit.HasValue)
        {
            HashSet<Collider> ownSet = new HashSet<Collider>(ownColliders);
            var nearbySurfaces = new List<(RaycastHit hit, float score)>();
            foreach (Collider candidate in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<Collider>(true)))
            {
                if (candidate == null || ownSet.Contains(candidate) || !candidate.enabled || candidate.isTrigger ||
                    !candidate.gameObject.activeInHierarchy ||
                    candidate.GetComponentInParent<PlayerWorker>() != null ||
                    candidate.GetComponentInParent<EnemyWorker>() != null ||
                    !IsWalkableEnvironmentSurface(candidate))
                    continue;

                Bounds bounds = candidate.bounds;
                if (bounds.size.x * bounds.size.z < 0.5f)
                    continue;
                Vector3 sample = new Vector3(
                    Mathf.Clamp(playerTransform.position.x, bounds.min.x + 0.01f, bounds.max.x - 0.01f),
                    bounds.max.y + 1f,
                    Mathf.Clamp(playerTransform.position.z, bounds.min.z + 0.01f, bounds.max.z - 0.01f));
                if (!candidate.Raycast(new Ray(sample, Vector3.down), out RaycastHit hit, bounds.size.y + 2f) ||
                    hit.normal.y < Mathf.Cos(60f * Mathf.Deg2Rad))
                    continue;

                float horizontalDistance = Vector2.Distance(
                    new Vector2(playerTransform.position.x, playerTransform.position.z),
                    new Vector2(hit.point.x, hit.point.z));
                float score = horizontalDistance + Mathf.Abs(hit.point.y - currentBottom) * 0.05f;
                nearbySurfaces.Add((hit, score));
            }

            if (nearbySurfaces.Count > 0)
                groundHit = nearbySurfaces.OrderBy(value => value.score).First().hit;
        }

        for (int index = 0; index < ownColliders.Length; index++)
            ownColliders[index].enabled = enabledStates[index];

        if (!groundHit.HasValue)
            throw new InvalidOperationException($"No walkable ground was found below/above active player '{player.name}'.");

        Vector3 before = playerTransform.position;
        float skin = controller != null ? Mathf.Max(0.02f, controller.skinWidth) : 0.03f;
        Vector3 groundedPosition = playerTransform.position;
        groundedPosition.x = groundHit.Value.point.x;
        groundedPosition.z = groundHit.Value.point.z;
        groundedPosition.y += groundHit.Value.point.y - currentBottom + skin;
        if (NavMesh.SamplePosition(groundedPosition, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
        {
            float rootToBottom = playerTransform.position.y - currentBottom;
            groundedPosition.x = navHit.position.x;
            groundedPosition.z = navHit.position.z;
            groundedPosition.y = navHit.position.y + rootToBottom + skin;
        }
        playerTransform.position = groundedPosition;
        Physics.SyncTransforms();
        EditorUtility.SetDirty(playerTransform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string line =
            $"Player={player.name}\n" +
            $"PlayerBefore={before.x:F4},{before.y:F4},{before.z:F4}\n" +
            $"PlayerAfter={playerTransform.position.x:F4},{playerTransform.position.y:F4},{playerTransform.position.z:F4}\n" +
            $"GroundY={groundHit.Value.point.y:F4}\n" +
            $"GroundCollider={groundHit.Value.collider.name}\n";
        File.WriteAllText("Library/PlayerGroundingReport.txt", line);
        Debug.Log($"[Player Grounding] {player.name}: {before} -> {playerTransform.position}, ground={groundHit.Value.collider.name}");
    }

    private static bool IsWalkableEnvironmentSurface(Collider candidate)
    {
        for (Transform current = candidate.transform; current != null; current = current.parent)
        {
            string name = current.name.ToLowerInvariant();
            if (name.Contains("column") || name.Contains("pillar") || name.Contains("wall"))
                return false;
            if (name.Contains("floor") || name.Contains("ground") || name.Contains("stair") ||
                name.Contains("bridge") || name.Contains("platform") || name.Contains("walk") ||
                name.Contains("ramp"))
                return true;
        }
        return false;
    }

    [MenuItem("Tools/Swordplay/Separate BOW Enemy Materials")]
    public static void CreateIndependentBowEnemyMaterials()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Materials/Enemy");
        EnsureFolder(EnemyMaterialFolder);

        EnemyWorker[] bows = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .Where(enemy => enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (bows.Length == 0)
        {
            File.AppendAllText(ReportPath, "EnemyMaterialRoots=0 (modular roster; skipped BOW material separation)\n");
            Debug.Log("[BOW Enemy Materials] No BOW enemy exists in the scene; nothing to separate.");
            return;
        }

        var copies = new Dictionary<Material, Material>();
        int rendererCount = 0;
        int slotCount = 0;
        foreach (EnemyWorker bow in bows)
        {
            foreach (Renderer renderer in bow.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (IsGameplayHelper(renderer.transform, bow.transform)) continue;
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null || IsEffectMaterial(source)) continue;
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    if (sourcePath.StartsWith(EnemyMaterialFolder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!copies.TryGetValue(source, out Material enemyCopy))
                    {
                        string identity = string.IsNullOrEmpty(sourcePath)
                            ? Mathf.Abs(source.GetInstanceID()).ToString()
                            : AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8);
                        string safeName = SanitizeFileName(source.name);
                        string destination = $"{EnemyMaterialFolder}/{safeName}_Enemy_{identity}.mat";
                        enemyCopy = AssetDatabase.LoadAssetAtPath<Material>(destination);
                        if (enemyCopy == null)
                        {
                            enemyCopy = new Material(source) { name = source.name + " Enemy" };
                            AssetDatabase.CreateAsset(enemyCopy, destination);
                        }
                        else
                        {
                            string preservedName = enemyCopy.name;
                            EditorUtility.CopySerialized(source, enemyCopy);
                            enemyCopy.name = preservedName;
                            EditorUtility.SetDirty(enemyCopy);
                        }
                        copies.Add(source, enemyCopy);
                    }
                    materials[index] = enemyCopy;
                    changed = true;
                    slotCount++;
                }
                if (!changed) continue;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
                rendererCount++;
            }
        }

        if (rendererCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        File.AppendAllText(ReportPath,
            $"EnemyMaterialRoots={bows.Length}\n" +
            $"EnemyMaterialAssets={copies.Count}\n" +
            $"EnemyMaterialRenderers={rendererCount}\n" +
            $"EnemyMaterialSlots={slotCount}\n");
        Debug.Log($"[BOW Enemy Materials] COMPLETE — roots={bows.Length}, copies={copies.Count}, renderers={rendererCount}, slots={slotCount}");
    }

    private static bool IsGameplayHelper(Transform value, Transform root)
    {
        for (Transform current = value; current != null && current != root; current = current.parent)
            if (current.name == "Hitter" || current.name == "RigLayer" ||
                current.name.StartsWith("Broken Edge Blade", StringComparison.OrdinalIgnoreCase) ||
                current.name == "Knight Rocket Flames")
                return true;
        return false;
    }

    private static bool IsEffectMaterial(Material material)
    {
        string path = AssetDatabase.GetAssetPath(material);
        return path.StartsWith("Assets/Resources/VFX/", StringComparison.OrdinalIgnoreCase) ||
               material.shader == null || material.shader.name.IndexOf("Blade Aura", StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.shader.name.IndexOf("Blade Trail", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string name = path.Substring(path.LastIndexOf('/') + 1);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "Material" : value;
    }
}
