using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class GenerateWarriorShowcase
{
    // Use the imported model prefab directly so every modular mesh and bone stays
    // under the exact hierarchy authored in the FBX.
    private const string SourcePrefabPath = "Assets/warrior/all_warrior.FBX";
    private const string TargetScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string OutputFolder = "Assets/warrior/GeneratedCharacterSets";
    private const string RootName = "Warrior_Character_Sets";
    private const string PreviewPath = "_UnityValidation/WarriorCharacterSetsPreview.png";
    private const string LitPreviewPath = "_UnityValidation/WarriorCharacterSetsLitPreview.png";
    private const int OptionCount = 7;
    private const int SetCount = OptionCount * OptionCount;

    [MenuItem("Tools/Swordplay/Build Warrior Character Sets %&j")]
    public static void Generate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
            scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);

        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        Shader shader = Shader.Find("Swordplay/Broken Edge Textured Surface");
        Shader tintedShader = Shader.Find("Swordplay/Warrior Showcase Tinted Broken Edge");
        if (sourcePrefab == null || shader == null || tintedShader == null)
        {
            Debug.LogError($"Warrior character-set build failed. Prefab found: {sourcePrefab != null}, base shader found: {shader != null}, tinted shader found: {tintedShader != null}.");
            return;
        }

        EnsureFolders();
        RemoveObsoleteSolidColorAssets();
        RepairImportedMaterials(shader);
        Dictionary<string, Material> originalMaterials = LoadOriginalMaterials();
        Dictionary<Material, Material> tintedMaterials = CreateTintedMaterialVariants(originalMaterials.Values.Distinct(), tintedShader);
        GameObject previous = GameObject.Find(RootName);
        if (previous != null)
            UnityEngine.Object.DestroyImmediate(previous);

        GameObject showcaseRoot = new(RootName);
        PlayerWorker player = UnityEngine.Object.FindFirstObjectByType<PlayerWorker>(FindObjectsInactive.Exclude);
        Vector3 origin = player != null ? player.transform.position : Vector3.zero;
        Vector3 forward = player != null ? Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        var created = new List<GameObject>();
        int totalVisibleRenderers = 0;
        int totalVertices = 0;
        for (int i = 0; i < SetCount; i++)
        {
            // Keep the imported prefab connection intact. Unpacking and sampling its legacy
            // clip in edit mode can leave the skeleton in a pose that does not match the
            // meshes' bind poses, producing the stretched "stick figure" corruption.
            GameObject instance = PrefabUtility.InstantiatePrefab(sourcePrefab, scene) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate the warrior generator prefab.");
            instance.name = $"Warrior_Set_{i + 1:00}";
            instance.transform.SetParent(showcaseRoot.transform, true);
            ConfigureSet(instance, i);
            RestoreOriginalMaterials(instance, originalMaterials, shader);
            AssignTintedMaterials(instance, tintedMaterials);
            ApplyCharacterTint(instance, i);
            PrepareCompleteCharacter(instance);
            ValidateAppearanceSetup(instance, tintedShader);

            CharacterValidation validation = ValidateCharacter(instance);
            if (!validation.IsValid)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                throw new InvalidOperationException($"Warrior set {i + 1} is not renderable (renderers={validation.RendererCount}, vertices={validation.VertexCount}, bones={validation.BoneCount}, bounds={validation.BoundsSize}).");
            }
            int column = i % OptionCount;
            int row = i / OptionCount;
            Vector3 position = origin + forward * (7f + row * 2.35f)
                               + right * ((column - (OptionCount - 1) * 0.5f) * 2.35f);
            PlaceOnGround(instance, position, Quaternion.LookRotation(-forward, Vector3.up));
            created.Add(instance);
            totalVisibleRenderers += validation.RendererCount;
            totalVertices += validation.VertexCount;
        }

        Selection.objects = created.Cast<UnityEngine.Object>().ToArray();
        if (created.Count > 0)
        {
            Selection.activeGameObject = created[0];
            EditorGUIUtility.PingObject(created[0]);
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TargetScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Warrior Generator] Built {created.Count} complete character sets with {totalVisibleRenderers} visible skinned renderers and {totalVertices:N0} rendered vertices. Original asset materials and textures are preserved with the Broken Edge shader.");
        RenderPreview(showcaseRoot);
    }

    private static void ConfigureSet(GameObject root, int setIndex)
    {
        int bodyIndex = setIndex / OptionCount;
        int helmetIndex = setIndex % OptionCount;
        SetGroup(root, "w_body_", bodyIndex);
        SetGroup(root, "w_helmet_", helmetIndex);
        SetGroup(root, "w_shoulder_", bodyIndex + helmetIndex * 2);
        SetGroup(root, "w_hand_", bodyIndex * 3 + helmetIndex);
        SetGroup(root, "w_shoes_", bodyIndex + helmetIndex * 4);
        SetGroup(root, "w_head_", setIndex % 10);
        SetExact(root, "w_pants_01", true);
        SetExact(root, "bow", false);
        SetExact(root, "shield_01", false);
        SetExact(root, "sword_01", true);
    }

    private static void ApplyCharacterTint(GameObject root, int setIndex)
    {
        float spectrumPosition = SetCount > 1 ? setIndex / (float)(SetCount - 1) : 0f;
        float hue = Mathf.Lerp(0f, 0.64f, spectrumPosition);
        // Keep the red-orange-yellow-green-blue sequence readable without the
        // heavy, saturated paint look. The dedicated shader still applies the
        // hue to dark texels, while this pastel palette keeps highlights airy.
        Color tint = Color.HSVToRGB(hue, 0.52f, 1f);
        tint.a = 1f;
        WarriorShowcaseTint controller = root.GetComponent<WarriorShowcaseTint>();
        if (controller == null) controller = root.AddComponent<WarriorShowcaseTint>();
        controller.Configure(tint, 0.92f);
    }

    private static Dictionary<Material, Material> CreateTintedMaterialVariants(IEnumerable<Material> originals, Shader tintedShader)
    {
        const string folder = OutputFolder + "/TintedMaterials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(OutputFolder, "TintedMaterials");
        var result = new Dictionary<Material, Material>();
        foreach (Material original in originals.Where(m => m != null).Distinct())
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));
            string path = $"{folder}/M_WarriorTint_{guid}.mat";
            Material tinted = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (tinted == null)
            {
                tinted = new Material(original) { name = $"M_WarriorTint_{original.name}" };
                AssetDatabase.CreateAsset(tinted, path);
            }
            else
            {
                tinted.CopyPropertiesFromMaterial(original);
            }
            Texture baseTexture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : null;
            Color baseColor = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : Color.white;
            tinted.shader = tintedShader;
            tinted.SetTexture("_BaseMap", baseTexture ?? Texture2D.whiteTexture);
            tinted.SetColor("_BaseColor", new Color(1f, 1f, 1f, baseColor.a));
            tinted.SetFloat("_WarriorTintStrength", 0.92f);
            tinted.SetFloat("_ColorSteps", 4f);
            tinted.SetFloat("_ShadowStrength", 0.52f);
            tinted.SetFloat("_LightTintStrength", 0.03f);
            tinted.SetFloat("_RimStrength", 0.04f);
            tinted.SetFloat("_PaperStrength", 0.022f);
            tinted.SetFloat("_BandWobble", 0.11f);
            tinted.SetFloat("_HatchStrength", 0.07f);
            tinted.SetFloat("_InkVariation", 0.32f);
            tinted.SetFloat("_OutlineWidth", 0.012f);
            tinted.SetFloat("_Cutoff", 0.01f);
            EditorUtility.SetDirty(tinted);
            result[original] = tinted;
        }
        return result;
    }

    private static void AssignTintedMaterials(GameObject root, Dictionary<Material, Material> tintedMaterials)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && tintedMaterials.TryGetValue(slots[i], out Material tinted)) slots[i] = tinted;
            renderer.sharedMaterials = slots;
        }
    }

    private static void ValidateAppearanceSetup(GameObject root, Shader tintedShader)
    {
        int tintedSlots = 0;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                    throw new InvalidOperationException($"'{root.name}/{renderer.name}' has a missing material.");
                if (material.shader != tintedShader)
                    throw new InvalidOperationException($"Renderer '{renderer.name}' did not receive the dedicated tint shader.");
                tintedSlots++;
            }
        }
        if (tintedSlots == 0)
            throw new InvalidOperationException($"'{root.name}' appearance validation failed (tinted slots={tintedSlots}).");

        foreach (string disabledPart in new[] { "bow", "shield_01" })
        {
            Transform part = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => string.Equals(t.name, disabledPart, StringComparison.OrdinalIgnoreCase));
            if (part != null && part.gameObject.activeSelf)
                throw new InvalidOperationException($"'{root.name}' still has active part '{disabledPart}'.");
        }
    }

    private static void SetGroup(GameObject root, string prefix, int selectedIndex)
    {
        Transform[] options = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (options.Length == 0) throw new InvalidOperationException($"Missing '{prefix}' part group.");
        for (int i = 0; i < options.Length; i++) options[i].gameObject.SetActive(i == selectedIndex % options.Length);
    }

    private static void SetExact(GameObject root, string objectName, bool active)
    {
        Transform target = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => string.Equals(t.name, objectName, StringComparison.OrdinalIgnoreCase));
        if (target != null) target.gameObject.SetActive(active);
    }

    private static void PrepareCompleteCharacter(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
                skinned.quality = SkinQuality.Auto;
            }
        }
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
        {
            animation.Stop();
            animation.playAutomatically = false;
            animation.enabled = false;
        }
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
    }

    private static CharacterValidation ValidateCharacter(GameObject root)
    {
        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(false)
            .Where(r => r.enabled && r.sharedMesh != null).ToArray();
        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : default;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return new CharacterValidation
        {
            RendererCount = renderers.Length,
            VertexCount = renderers.Sum(r => r.sharedMesh.vertexCount),
            BoneCount = renderers.Sum(r => r.bones.Count(b => b != null)),
            BoundsSize = bounds.size
        };
    }

    private static Dictionary<string, Material> LoadOriginalMaterials()
    {
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/warrior/Materials" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) continue;
            materials[Path.GetFileNameWithoutExtension(path)] = material;
            materials[material.name] = material;
        }
        if (materials.Count == 0)
            throw new InvalidOperationException("No original warrior materials were found.");
        return materials;
    }

    private static void RestoreOriginalMaterials(GameObject root, Dictionary<string, Material> originals, Shader expectedShader)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                Material original = FindOriginalMaterial(renderer.name, slots[i], originals);
                if (original == null)
                    throw new InvalidOperationException($"Could not resolve an original material for renderer '{renderer.name}' on '{root.name}'.");
                if (original.shader != expectedShader)
                    throw new InvalidOperationException($"Original material '{original.name}' has shader '{original.shader?.name ?? "null"}' instead of the repaired Broken Edge shader.");
                slots[i] = original;
            }
            renderer.sharedMaterials = slots;
        }
    }

    private static Material FindOriginalMaterial(string rendererName, Material importedSlot, Dictionary<string, Material> originals)
    {
        string cleanRendererName = rendererName.Replace("(Clone)", string.Empty).Trim();
        string importedName = importedSlot != null ? importedSlot.name.Replace("(Instance)", string.Empty).Trim() : string.Empty;
        string[] candidates =
        {
            $"all_warrior-{cleanRendererName}",
            cleanRendererName,
            $"all_warrior-{importedName}",
            importedName,
            string.Equals(cleanRendererName, "bow", StringComparison.OrdinalIgnoreCase) ? "wepons_01" : string.Empty,
            string.Equals(cleanRendererName, "DataTemplate", StringComparison.OrdinalIgnoreCase) ? "all_warrior-Standardmaterial" : string.Empty
        };
        foreach (string candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && originals.TryGetValue(candidate, out Material material))
                return material;
        return null;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder)) AssetDatabase.CreateFolder("Assets/warrior", "GeneratedCharacterSets");
    }

    private static void RemoveObsoleteSolidColorAssets()
    {
        for (int i = 1; i <= SetCount; i++)
        {
            string suffix = i.ToString("00");
            AssetDatabase.DeleteAsset($"{OutputFolder}/M_WarriorSet_{suffix}.mat");
            AssetDatabase.DeleteAsset($"{OutputFolder}/T_WarriorSet_{suffix}.png");
        }
    }

    private static void RepairImportedMaterials(Shader shader)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/warrior/Materials" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) continue;

            Texture texture = null;
            Color sourceColor = Color.white;
            if (material.HasProperty("_BaseMap")) texture = material.GetTexture("_BaseMap");
            if (texture == null && material.HasProperty("_MainTex")) texture = material.GetTexture("_MainTex");
            // The legacy source tint remains serialized in _Color even after a
            // temporary Broken Edge override changed _BaseColor.
            if (TryGetSavedColor(material, "_Color", out Color savedSourceColor)) sourceColor = savedSourceColor;
            else if (material.HasProperty("_BaseColor")) sourceColor = material.GetColor("_BaseColor");

            material.shader = shader;
            material.SetTexture("_BaseMap", texture ?? Texture2D.whiteTexture);
            material.SetColor("_BaseColor", sourceColor);
            material.SetColor("_ShadowColor", Color.Lerp(sourceColor, new Color(0.01f, 0.015f, 0.05f, 1f), 0.72f));
            material.SetColor("_LightTint", Color.Lerp(sourceColor, Color.white, 0.45f));
            material.SetColor("_RimColor", Color.Lerp(sourceColor, Color.white, 0.65f));
            material.SetColor("_OutlineColor", Color.Lerp(sourceColor, Color.black, 0.92f));
            material.SetFloat("_Saturation", 1.08f);
            material.SetFloat("_TextureContrast", 1.04f);
            material.SetFloat("_ShadowStrength", 0.66f);
            material.SetFloat("_LightTintStrength", 0.14f);
            material.SetFloat("_RimStrength", 0.12f);
            material.SetFloat("_PaperStrength", 0.035f);
            material.SetFloat("_OutlineWidth", 0.012f);
            material.SetFloat("_Cutoff", 0.01f);
            RemoveSavedFloat(material, "_TextureColorPreservation");
            EditorUtility.SetDirty(material);
        }
    }

    private static bool TryGetSavedColor(Material material, string propertyName, out Color color)
    {
        var serialized = new SerializedObject(material);
        SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
        if (colors != null)
        {
            for (int i = 0; i < colors.arraySize; i++)
            {
                SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue != propertyName) continue;
                color = entry.FindPropertyRelative("second").colorValue;
                return true;
            }
        }
        color = Color.white;
        return false;
    }

    private static void RemoveSavedFloat(Material material, string propertyName)
    {
        var serialized = new SerializedObject(material);
        SerializedProperty floats = serialized.FindProperty("m_SavedProperties.m_Floats");
        if (floats == null) return;
        for (int i = floats.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty entry = floats.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("first").stringValue == propertyName)
                floats.DeleteArrayElementAtIndex(i);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void PlaceOnGround(GameObject root, Vector3 target, Quaternion rotation)
    {
        // This is a showcase formation, not navigation placement. Per-cell raycasts can hit
        // stairs, props, or castle roofs and scatter the grid vertically, so every character
        // shares the player's foot plane and is offset by its own rendered bounds.
        float groundY = target.y;
        root.transform.SetPositionAndRotation(new Vector3(target.x, groundY, target.z), rotation);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        root.transform.position += Vector3.up * (groundY - bounds.min.y);
    }

    private static void RenderPreview(GameObject showcaseRoot)
    {
        GameObject subjectRoot = showcaseRoot;
        Renderer[] renderers = subjectRoot.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) throw new InvalidOperationException("Cannot render a warrior preview without renderers.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        var previousLayers = new Dictionary<GameObject, int>();
        foreach (Transform child in subjectRoot.GetComponentsInChildren<Transform>(true))
        {
            previousLayers[child.gameObject] = child.gameObject.layer;
            child.gameObject.layer = 31;
        }

        GameObject cameraObject = new("Warrior Preview Camera");
        GameObject lightObject = new("Warrior Preview Light");
        RenderTexture renderTexture = null;
        Texture2D image = null;
        Material diagnosticMaterial = null;
        var previousMaterials = new Dictionary<Renderer, Material[]>();
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
            camera.fieldOfView = 32f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 200f;
            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            Vector3 viewDirection = new Vector3(0f, 0.12f, -1f).normalized;
            camera.transform.position = bounds.center - viewDirection * (radius / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f);
            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.cullingMask = 1 << 31;
            light.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            renderTexture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) throw new InvalidOperationException("URP Lit shader was not found for visual diagnosis.");
            diagnosticMaterial = new Material(litShader) { color = new Color(0.72f, 0.76f, 0.82f, 1f) };
            foreach (Renderer renderer in renderers)
            {
                previousMaterials[renderer] = renderer.sharedMaterials;
                Material[] diagnosticSlots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < diagnosticSlots.Length; i++) diagnosticSlots[i] = diagnosticMaterial;
                renderer.sharedMaterials = diagnosticSlots;
            }
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            image = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
            image.Apply(false, false);
            string litAbsolutePath = Path.GetFullPath(LitPreviewPath);
            Directory.CreateDirectory(Path.GetDirectoryName(litAbsolutePath));
            File.WriteAllBytes(litAbsolutePath, image.EncodeToPNG());

            foreach (KeyValuePair<Renderer, Material[]> pair in previousMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            camera.Render();
            image.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
            image.Apply(false, false);
            RenderTexture.active = previous;

            string absolutePath = Path.GetFullPath(PreviewPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllBytes(absolutePath, image.EncodeToPNG());
            Debug.Log($"[Warrior Generator] Visual verification preview written to '{absolutePath}'. Bounds size: {bounds.size}.");
        }
        finally
        {
            foreach (KeyValuePair<GameObject, int> pair in previousLayers)
                if (pair.Key != null) pair.Key.layer = pair.Value;
            foreach (KeyValuePair<Renderer, Material[]> pair in previousMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            if (diagnosticMaterial != null) UnityEngine.Object.DestroyImmediate(diagnosticMaterial);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (renderTexture != null)
            {
                Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
                if (camera != null) camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
        }
    }

    private struct CharacterValidation
    {
        public int RendererCount;
        public int VertexCount;
        public int BoneCount;
        public Vector3 BoundsSize;
        public bool IsValid => RendererCount >= 6 && VertexCount > 1000 && BoneCount > 10
            && BoundsSize.y > 1f && BoundsSize.y < 5f && BoundsSize.x > 0.25f;
    }
}
