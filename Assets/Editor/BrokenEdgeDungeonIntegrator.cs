#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class BrokenEdgeDungeonIntegrator
{
    private const string PackagePath = "Assets/FANTASTIC - Dungeon Pack/URP_FANTASTIC_Dungeon_Pack_2021_3_20.unitypackage";
    private const string DemoScenePath = "Assets/Fantastic Dungeon Pack/scenes/demoscene_dungeon_level_1_dungeon.unity";
    private const string TargetScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string ContainerName = "Fantastic Dungeon Demo Layout - Broken Edge";
    private const string MaterialFolder = "Assets/Settings/BrokenEdge/DungeonMaterials";
    private static bool importRequested;
    private static bool integrationRunning;

    [MenuItem("Swordplay/Import and Integrate Fantastic Dungeon Demo")]
    public static void RunFromMenu()
    {
        importRequested = false;
        ContinueImportAndIntegration();
    }

    private static void ContinueImportAndIntegration()
    {
        if (integrationRunning || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += ContinueImportAndIntegration;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            string absolutePackagePath = string.IsNullOrEmpty(projectRoot)
                ? PackagePath
                : Path.Combine(projectRoot, PackagePath.Replace('/', Path.DirectorySeparatorChar));

            if (!importRequested && File.Exists(absolutePackagePath))
            {
                importRequested = true;
                Debug.Log("[Broken Edge] Importing the URP Fantastic Dungeon package.");
                AssetDatabase.ImportPackage(absolutePackagePath, false);
            }
            EditorApplication.delayCall += ContinueImportAndIntegration;
            return;
        }

        IntegrateDemoScene();
    }

    private static void IntegrateDemoScene()
    {
        integrationRunning = true;
        try
        {
            if (!EditorSceneManager.SaveOpenScenes())
                throw new InvalidOperationException("Open scenes could not be saved before dungeon integration.");

            Scene target = SceneManager.GetActiveScene();
            if (!target.IsValid() || !string.Equals(target.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
                target = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);

            GameObject oldContainer = target.GetRootGameObjects().FirstOrDefault(root => root.name == ContainerName);
            if (oldContainer != null)
            {
                RestoreOriginalDungeonMaterials(oldContainer);
                LimitLocalLightShadows(oldContainer);
                EditorSceneManager.MarkSceneDirty(target);
                EditorSceneManager.SaveScene(target, TargetScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Broken Edge] Restored the dungeon renderers to their original URP materials and textures.");
                return;
            }

            Scene demo = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
            GameObject[] demoRoots = demo.GetRootGameObjects();

            var container = new GameObject(ContainerName);
            SceneManager.MoveGameObjectToScene(container, target);
            SceneManager.MergeScenes(demo, target);
            foreach (GameObject root in demoRoots)
                root.transform.SetParent(container.transform, true);

            RemoveConflictingDemoSystems(container);
            CenterDungeonLayout(container);
            // Preserve the pack's authored URP materials.  The scene-wide pastel
            // treatment comes from BrokenEdgeStyle, never from material replacement.
            LimitLocalLightShadows(container);

            EditorSceneManager.MarkSceneDirty(target);
            EditorSceneManager.SaveScene(target, TargetScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Broken Edge] Integrated the full Fantastic Dungeon level 1 demo layout with its original materials.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            integrationRunning = false;
        }
    }

    private static void RemoveConflictingDemoSystems(GameObject container)
    {
        foreach (Camera camera in container.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in container.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (Volume volume in container.GetComponentsInChildren<Volume>(true)) volume.enabled = false;
        foreach (ReflectionProbe probe in container.GetComponentsInChildren<ReflectionProbe>(true)) probe.enabled = false;
        foreach (Light light in container.GetComponentsInChildren<Light>(true))
            if (light.type == LightType.Directional) light.enabled = false;
    }

    private static void CenterDungeonLayout(GameObject container)
    {
        Renderer[] renderers = container.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled && !(renderer is ParticleSystemRenderer) && !(renderer is TrailRenderer))
            .ToArray();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        container.transform.position += new Vector3(-bounds.center.x, 0f, -bounds.center.z);
    }

    private static void ApplyBrokenEdgeMaterials(GameObject container)
    {
        EnsureFolder("Assets/Settings", "BrokenEdge");
        EnsureFolder("Assets/Settings/BrokenEdge", "DungeonMaterials");
        Shader shader = Shader.Find("Swordplay/Broken Edge Dungeon");
        if (shader == null) throw new InvalidOperationException("Broken Edge dungeon shader is unavailable.");

        var variants = new Dictionary<Material, Material>();
        foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null || IsTransparent(source)) continue;
                if (!variants.TryGetValue(source, out Material variant))
                {
                    variant = CreateOrUpdateVariant(source, shader);
                    variants[source] = variant;
                }
                materials[i] = variant;
                changed = true;
            }

            if (changed) renderer.sharedMaterials = materials;
        }
    }

    private static Material CreateOrUpdateVariant(Material source, Shader shader)
    {
        string guid;
        long localId;
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out guid, out localId) || string.IsNullOrEmpty(guid))
        {
            guid = Hash128.Compute(source.name + source.shader.name).ToString();
            localId = 0;
        }

        string path = $"{MaterialFolder}/{guid}_{localId}.mat";
        Material variant = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (variant == null)
        {
            variant = new Material(shader) { name = source.name + " (Broken Edge)" };
            AssetDatabase.CreateAsset(variant, path);
        }
        else
        {
            variant.shader = shader;
        }

        CopyOriginalTextureProperties(source, variant);
        ApplyTextureShaderDefaults(variant);
        EditorUtility.SetDirty(variant);
        return variant;
    }

    private static void RefreshExistingBrokenEdgeMaterials(GameObject container)
    {
        Shader shader = Shader.Find("Swordplay/Broken Edge Dungeon");
        if (shader == null) return;

        var refreshed = new HashSet<Material>();
        foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader != shader || !refreshed.Add(material)) continue;
                if (TryResolveOriginalMaterial(material, out Material original))
                    CopyOriginalTextureProperties(original, material);
                ApplyTextureShaderDefaults(material);
                EditorUtility.SetDirty(material);
            }
        }
    }

    private static void RestoreOriginalDungeonMaterials(GameObject container)
    {
        foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;

                string materialPath = AssetDatabase.GetAssetPath(material).Replace('\\', '/');
                bool isGeneratedVariant = materialPath.StartsWith(MaterialFolder + "/", StringComparison.OrdinalIgnoreCase) ||
                                          (material.shader != null && material.shader.name == "Swordplay/Broken Edge Dungeon");
                if (!isGeneratedVariant || !TryResolveOriginalMaterial(material, out Material original)) continue;

                materials[i] = original;
                changed = true;
            }

            if (!changed) continue;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
    }

    private static void CopyOriginalTextureProperties(Material source, Material destination)
    {
        string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" :
            source.HasProperty("_MainTex") ? "_MainTex" : null;
        Texture texture = textureProperty != null ? source.GetTexture(textureProperty) : null;
        Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
            source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        Vector2 scale = textureProperty != null ? source.GetTextureScale(textureProperty) : Vector2.one;
        Vector2 offset = textureProperty != null ? source.GetTextureOffset(textureProperty) : Vector2.zero;

        destination.SetTexture("_BaseMap", texture);
        destination.SetTextureScale("_BaseMap", scale);
        destination.SetTextureOffset("_BaseMap", offset);
        destination.SetColor("_BaseColor", color);
        destination.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.01f);
    }

    private static bool TryResolveOriginalMaterial(Material variant, out Material original)
    {
        original = null;
        string path = AssetDatabase.GetAssetPath(variant);
        string fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(fileName) || fileName.Length < 34) return false;

        int separator = fileName.LastIndexOf('_');
        if (separator <= 0 || separator >= fileName.Length - 1) return false;
        string guid = fileName.Substring(0, separator);
        if (!long.TryParse(fileName.Substring(separator + 1), out long localId)) return false;

        string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(sourcePath)) return false;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
        {
            if (!(asset is Material candidate)) continue;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string candidateGuid, out long candidateId))
                continue;
            if (candidateGuid == guid && candidateId == localId)
            {
                original = candidate;
                return true;
            }
        }
        return false;
    }

    private static void ApplyTextureShaderDefaults(Material material)
    {
        material.SetFloat("_Saturation", 0.42f);
        material.SetFloat("_TextureContrast", 1.08f);
        material.SetFloat("_ColorSteps", 4f);
        material.SetColor("_ShadowColor", new Color(0.16f, 0.19f, 0.28f, 1f));
        material.SetFloat("_ShadowStrength", 0.54f);
        material.SetColor("_LightTint", new Color(0.92f, 0.72f, 0.60f, 1f));
        material.SetFloat("_LightTintStrength", 0.12f);
        material.SetColor("_RimColor", new Color(0.28f, 0.78f, 0.82f, 1f));
        material.SetFloat("_RimStrength", 0.07f);
    }

    private static bool IsTransparent(Material material)
    {
        return material.renderQueue >= (int)RenderQueue.Transparent ||
               string.Equals(material.GetTag("RenderType", false), "Transparent", StringComparison.OrdinalIgnoreCase);
    }

    private static void TuneLocalLights(GameObject container)
    {
        int index = 0;
        foreach (Light light in container.GetComponentsInChildren<Light>(true))
        {
            if (!light.enabled || light.type == LightType.Directional) continue;
            light.intensity *= 0.42f;
            light.range *= 1.18f;
            light.color = index++ % 2 == 0
                ? new Color(1f, 0.48f, 0.28f, 1f)
                : new Color(0.26f, 0.78f, 0.83f, 1f);
            light.shadows = LightShadows.None;
        }
    }

    private static void LimitLocalLightShadows(GameObject container)
    {
        Light[] localLights = container.GetComponentsInChildren<Light>(true)
            .Where(light => light.enabled && light.type != LightType.Directional)
            .OrderBy(light => light.transform.position.sqrMagnitude)
            .ThenByDescending(light => light.intensity)
            .ToArray();

        const int shadowedLightBudget = 2;
        for (int i = 0; i < localLights.Length; i++)
            localLights[i].shadows = i < shadowedLightBudget ? LightShadows.Hard : LightShadows.None;
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
