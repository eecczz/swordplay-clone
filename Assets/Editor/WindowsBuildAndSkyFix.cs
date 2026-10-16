using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
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
        FixStandaloneSky();
        KnightEnemyInstaller.InstallRocketOnActiveBowEnemy();
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
            throw new InvalidOperationException("No BOW enemy was found for material separation.");

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

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
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
