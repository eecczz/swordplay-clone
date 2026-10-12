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

public static class BrokenEdgeToonMaterialInstaller
{
    private const string TargetScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string DungeonSourceScenePath = "Assets/Fantastic Dungeon Pack/scenes/demoscene_dungeon_level_1_dungeon.unity";
    private const string DungeonContainerName = "Fantastic Dungeon Demo Layout - Broken Edge";
    private const string MaterialFolder = "Assets/Settings/BrokenEdge/TexturedMaterials";
    private const string ShaderName = "Swordplay/Broken Edge Textured Surface";
    private const string HorsemanBodyMaterialPath = "Assets/Horseman/Base mesh/Materials/Mat_Horseman_Body.mat";
    private const string HorsemanHeadMaterialPath = "Assets/Horseman/Base mesh/Materials/Mat_Horseman_Head.mat";
    private const string HorsemanHelmetMaterialPath = "Assets/Horseman/Base mesh/Materials/Mat_Horseman_Helmet.mat";
    private const string HorsemanWeaponMaterialPath = "Assets/Horseman/Base mesh/Materials/Mat_Horseman_Weapon.mat";

    [MenuItem("Swordplay/Broken Edge/Apply Texture-Preserving Toon Materials")]
    public static void ApplyFromMenu()
    {
        ApplyToBrokenEdgeScene(true);
    }

    [MenuItem("Swordplay/Broken Edge/Restore Original Materials")]
    public static void RestoreFromMenu()
    {
        Scene scene = OpenTargetScene();
        int restored = RestoreOriginalMaterials(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TargetScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Broken Edge Toon] Restored {restored} original material slots.");
    }

    public static void ApplyToBrokenEdgeScene(bool openTargetScene)
    {
        Scene scene = openTargetScene ? OpenTargetScene() : SceneManager.GetActiveScene();
        if (!scene.IsValid() || !string.Equals(scene.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BrokenEdgeLaunch must be the active scene before applying toon materials.");

        EnsureFolders();
        Shader shader = Shader.Find(ShaderName);
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException($"The required shader is unavailable or unsupported: {ShaderName}");

        int repairedDungeonSlots = RepairDungeonMaterials(scene);
        int repairedHorsemanSlots = RepairHorsemanMaterials(scene);
        var variants = new Dictionary<Material, Material>();
        var configuredToonMaterials = new HashSet<Material>();
        int rendererCount = 0;
        int slotCount = 0;
        int skipped = 0;

        foreach (Renderer renderer in GetSceneRenderers(scene))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null) continue;
                if (source.shader != null && source.shader.name == ShaderName)
                {
                    if (configuredToonMaterials.Add(source)) ConfigureToonSettings(source);
                    continue;
                }
                if (ShouldKeepExistingShader(source.shader)) continue;
                if (!variants.TryGetValue(source, out Material variant))
                {
                    variant = CreateOrUpdateVariant(source, shader);
                    if (variant != null) variants.Add(source, variant);
                }

                if (variant == null)
                {
                    skipped++;
                    continue;
                }

                materials[i] = variant;
                slotCount++;
                changed = true;
            }

            if (!changed) continue;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
            rendererCount++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TargetScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Broken Edge Toon] Restored {repairedDungeonSlots} dungeon slots and {repairedHorsemanSlots} Horseman slots, then applied persistent texture-preserving materials to {slotCount} slots on {rendererCount} renderers ({variants.Count} unique variants, {skipped} skipped). Shader supported={shader.isSupported}.");
    }

    public static int ApplyToHierarchy(GameObject root)
    {
        if (root == null) return 0;

        EnsureFolders();
        Shader shader = Shader.Find(ShaderName);
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException($"The required shader is unavailable or unsupported: {ShaderName}");

        var variants = new Dictionary<Material, Material>();
        var configuredToonMaterials = new HashSet<Material>();
        int styledRendererCount = 0;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            bool styled = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null) continue;
                if (source.shader != null && source.shader.name == ShaderName)
                {
                    if (configuredToonMaterials.Add(source)) ConfigureToonSettings(source);
                    styled = true;
                    continue;
                }
                if (ShouldKeepExistingShader(source.shader)) continue;

                if (!variants.TryGetValue(source, out Material variant))
                {
                    variant = CreateOrUpdateVariant(source, shader);
                    if (variant != null) variants.Add(source, variant);
                }
                if (variant == null) continue;

                materials[i] = variant;
                changed = true;
                styled = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }
            if (styled) styledRendererCount++;
        }

        if (styledRendererCount > 0 && root.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(root.scene);
        AssetDatabase.SaveAssets();
        return styledRendererCount;
    }

    private static int RepairHorsemanMaterials(Scene scene)
    {
        Material body = AssetDatabase.LoadAssetAtPath<Material>(HorsemanBodyMaterialPath);
        Material head = AssetDatabase.LoadAssetAtPath<Material>(HorsemanHeadMaterialPath);
        Material helmet = AssetDatabase.LoadAssetAtPath<Material>(HorsemanHelmetMaterialPath);
        Material weapon = AssetDatabase.LoadAssetAtPath<Material>(HorsemanWeaponMaterialPath);
        if (body == null || head == null || helmet == null || weapon == null)
            throw new InvalidOperationException("One or more original Horseman materials could not be loaded.");

        int repaired = 0;
        foreach (Renderer renderer in GetSceneRenderers(scene))
        {
            if (!IsUnderNamedAncestor(renderer.transform, "Horseman Player Visual")) continue;

            Material replacement = IsUnderNamedAncestor(renderer.transform, "Body1") ? body :
                IsUnderNamedAncestor(renderer.transform, "Head1") ? head :
                IsUnderNamedAncestor(renderer.transform, "Weapon1") ? weapon :
                renderer.name.StartsWith("Helmet", StringComparison.OrdinalIgnoreCase) ? helmet : null;
            if (replacement == null) continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                if (materials[slot] != null) continue;
                materials[slot] = replacement;
                repaired++;
                changed = true;
            }

            if (!changed) continue;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
        return repaired;
    }

    private static bool IsUnderNamedAncestor(Transform transform, string ancestorName)
    {
        while (transform != null)
        {
            if (string.Equals(transform.name, ancestorName, StringComparison.OrdinalIgnoreCase)) return true;
            transform = transform.parent;
        }
        return false;
    }

    private static int RepairDungeonMaterials(Scene targetScene)
    {
        GameObject container = targetScene.GetRootGameObjects().FirstOrDefault(root => root.name == DungeonContainerName);
        if (container == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(DungeonSourceScenePath) == null) return 0;

        Scene sourceScene = default;
        try
        {
            sourceScene = EditorSceneManager.OpenScene(DungeonSourceScenePath, OpenSceneMode.Additive);
            GameObject[] sourceRoots = sourceScene.GetRootGameObjects();
            Transform[] targetRoots = Enumerable.Range(0, container.transform.childCount)
                .Select(index => container.transform.GetChild(index))
                .ToArray();

            int repaired = 0;
            for (int i = 0; i < sourceRoots.Length; i++)
            {
                GameObject sourceRoot = sourceRoots[i];
                int occurrence = sourceRoots.Take(i).Count(root => root.name == sourceRoot.name);
                Transform targetRoot = targetRoots.Where(root => root.name == sourceRoot.name).Skip(occurrence).FirstOrDefault();
                if (targetRoot == null) continue;
                repaired += RepairHierarchy(sourceRoot.transform, targetRoot);
            }
            return repaired;
        }
        finally
        {
            if (sourceScene.IsValid() && sourceScene.isLoaded)
                EditorSceneManager.CloseScene(sourceScene, true);
        }
    }

    private static int RepairHierarchy(Transform source, Transform target)
    {
        int repaired = RepairRenderersOnTransform(source, target);
        Transform[] targetChildren = Enumerable.Range(0, target.childCount)
            .Select(index => target.GetChild(index))
            .ToArray();

        for (int i = 0; i < source.childCount; i++)
        {
            Transform sourceChild = source.GetChild(i);
            int occurrence = Enumerable.Range(0, i)
                .Select(index => source.GetChild(index))
                .Count(child => child.name == sourceChild.name);
            Transform targetChild = targetChildren.Where(child => child.name == sourceChild.name).Skip(occurrence).FirstOrDefault();
            if (targetChild != null) repaired += RepairHierarchy(sourceChild, targetChild);
        }
        return repaired;
    }

    private static int RepairRenderersOnTransform(Transform source, Transform target)
    {
        Renderer[] sourceRenderers = source.GetComponents<Renderer>();
        Renderer[] targetRenderers = target.GetComponents<Renderer>();
        int repaired = 0;

        foreach (IGrouping<Type, Renderer> sourceGroup in sourceRenderers.GroupBy(renderer => renderer.GetType()))
        {
            Renderer[] sourceOfType = sourceGroup.ToArray();
            Renderer[] targetOfType = targetRenderers.Where(renderer => renderer.GetType() == sourceGroup.Key).ToArray();
            int count = Mathf.Min(sourceOfType.Length, targetOfType.Length);
            for (int rendererIndex = 0; rendererIndex < count; rendererIndex++)
            {
                Material[] sourceMaterials = sourceOfType[rendererIndex].sharedMaterials;
                Material[] targetMaterials = targetOfType[rendererIndex].sharedMaterials;
                if (sourceMaterials.Length != targetMaterials.Length) continue;

                bool changed = false;
                for (int slot = 0; slot < targetMaterials.Length; slot++)
                {
                    if (targetMaterials[slot] != null || sourceMaterials[slot] == null) continue;
                    targetMaterials[slot] = sourceMaterials[slot];
                    repaired++;
                    changed = true;
                }

                if (!changed) continue;
                targetOfType[rendererIndex].sharedMaterials = targetMaterials;
                EditorUtility.SetDirty(targetOfType[rendererIndex]);
            }
        }
        return repaired;
    }

    private static Scene OpenTargetScene()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() && string.Equals(active.path, TargetScenePath, StringComparison.OrdinalIgnoreCase)
            ? active
            : EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
    }

    private static IEnumerable<Renderer> GetSceneRenderers(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true));
    }

    private static Material CreateOrUpdateVariant(Material source, Shader shader)
    {
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId) || string.IsNullOrEmpty(guid))
            return null;

        string path = $"{MaterialFolder}/{guid}_{localId}.mat";
        Material variant = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (variant == null)
        {
            variant = new Material(shader) { name = source.name + " (Broken Edge Toon)" };
            AssetDatabase.CreateAsset(variant, path);
        }
        else
        {
            variant.shader = shader;
        }

        CopySourceAppearance(source, variant);
        EditorUtility.SetDirty(variant);
        return variant;
    }

    private static void CopySourceAppearance(Material source, Material variant)
    {
        string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" :
            source.HasProperty("_MainTex") ? "_MainTex" : null;
        if (textureProperty != null)
        {
            variant.SetTexture("_BaseMap", source.GetTexture(textureProperty));
            variant.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
            variant.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
        }
        else
        {
            variant.SetTexture("_BaseMap", Texture2D.whiteTexture);
            variant.SetTextureScale("_BaseMap", Vector2.one);
            variant.SetTextureOffset("_BaseMap", Vector2.zero);
        }

        Color baseColor = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
            source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        bool transparent = IsTransparent(source);
        bool cutout = IsCutout(source);

        variant.SetColor("_BaseColor", baseColor);
        ConfigureToonSettings(variant);
        variant.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : cutout ? 0.5f : 0.01f);
        variant.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f);
        variant.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        variant.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        variant.SetFloat("_ZWrite", transparent ? 0f : 1f);
        variant.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
        variant.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        variant.enableInstancing = source.enableInstancing;
        variant.doubleSidedGI = source.doubleSidedGI;
    }

    private static void ConfigureToonSettings(Material material)
    {
        material.SetFloat("_Saturation", 0.82f);
        material.SetFloat("_TextureContrast", 0.78f);
        material.SetFloat("_ColorSteps", 5f);
        material.SetColor("_ShadowColor", new Color(0.38f, 0.42f, 0.56f, 1f));
        material.SetFloat("_ShadowStrength", 0.18f);
        material.SetColor("_LightTint", new Color(0.94f, 0.90f, 1f, 1f));
        material.SetFloat("_LightTintStrength", 0.045f);
        material.SetColor("_RimColor", new Color(0.28f, 0.78f, 0.82f, 1f));
        material.SetFloat("_RimStrength", 0.085f);
        EditorUtility.SetDirty(material);
    }

    private static int RestoreOriginalMaterials(Scene scene)
    {
        int restored = 0;
        foreach (Renderer renderer in GetSceneRenderers(scene))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || !IsGeneratedVariant(material) || !TryResolveOriginal(material, out Material original)) continue;
                materials[i] = original;
                restored++;
                changed = true;
            }

            if (!changed) continue;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
        return restored;
    }

    private static bool IsGeneratedVariant(Material material)
    {
        return AssetDatabase.GetAssetPath(material).Replace('\\', '/').StartsWith(MaterialFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveOriginal(Material variant, out Material original)
    {
        original = null;
        string fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(variant));
        int separator = fileName.LastIndexOf('_');
        if (separator <= 0 || !long.TryParse(fileName.Substring(separator + 1), out long localId)) return false;
        string guid = fileName.Substring(0, separator);
        string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(sourcePath)) return false;

        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
        {
            if (!(asset is Material candidate)) continue;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string candidateGuid, out long candidateId)) continue;
            if (candidateGuid != guid || candidateId != localId) continue;
            original = candidate;
            return true;
        }
        return false;
    }

    private static bool ShouldKeepExistingShader(Shader shader)
    {
        if (shader == null) return true;
        string name = shader.name;
        return name.StartsWith("Swordplay/Broken Edge", StringComparison.Ordinal) ||
               name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTransparent(Material material)
    {
        return material.renderQueue >= (int)RenderQueue.Transparent ||
               (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) ||
               string.Equals(material.GetTag("RenderType", false), "Transparent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCutout(Material material)
    {
        return material.IsKeywordEnabled("_ALPHATEST_ON") ||
               (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f) ||
               material.renderQueue == (int)RenderQueue.AlphaTest ||
               string.Equals(material.GetTag("RenderType", false), "TransparentCutout", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder("Assets/Settings/BrokenEdge")) AssetDatabase.CreateFolder("Assets/Settings", "BrokenEdge");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Settings/BrokenEdge", "TexturedMaterials");
    }
}
#endif
