using System;
using System.IO;
using System.Linq;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class KnightSimpleWeaponFinalizer
{
    // The Library request makes this repeatable after compilation without an editor click.
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string RequestPath = "Library/ApplyKnightSimpleWeapons.request";
    private const string ReportPath = "Library/KnightSimpleWeaponReport.txt";

    static KnightSimpleWeaponFinalizer()
    {
        EditorApplication.delayCall += ApplyIfRequested;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += ApplyIfRequested;
        };
    }

    private static void ApplyIfRequested()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            Apply();
            File.Delete(RequestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Broken Edge/Apply Yellow Knight Simple Weapons And VFX")]
    public static void Apply()
    {
        // Reload every run so a failed previous pass cannot leave a half-cloned hierarchy in memory.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EnemyWorker[] knights = UnityEngine.Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(value => value.name.StartsWith("Knight_BrokenEdge_Blue_Showcase", StringComparison.Ordinal))
            .OrderBy(value => value.name, StringComparer.Ordinal).ToArray();
        EnemyWorker template = knights.FirstOrDefault(IsYellowKnight)
                               ?? throw new InvalidOperationException("Yellow knight template (M_WarriorSet_07) was not found.");
        Animator templateAnimator = template.GetComponent<Animator>();
        Transform templateRight = FindSocket(templateAnimator, HumanBodyBones.RightHand, "Weapon_r Socket");
        Transform templateLeft = FindSocket(templateAnimator, HumanBodyBones.LeftHand, "Weapon_l Socket");
        Transform templateRightWeapon = FindHandWeaponRoot(templateAnimator.GetBoneTransform(HumanBodyBones.RightHand), templateRight, "Right");
        Transform templateLeftWeapon = FindHandWeaponRoot(templateAnimator.GetBoneTransform(HumanBodyBones.LeftHand), templateLeft, "Left");
        if (templateRight == null || templateLeft == null)
            throw new InvalidOperationException($"{template.name}: right/left weapon socket is missing.");
        if (templateRightWeapon == null || templateLeftWeapon == null)
            throw new InvalidOperationException($"{template.name}: rendered right/left hand weapon root is missing.");

        Material ringMaterial = GetOrCreateVfxMaterial("Assets/Resources/VFX/M_GroundImpactRing.mat", Color.white, true);
        Material flameMaterial = GetOrCreateVfxMaterial("Assets/Resources/VFX/M_KnightRocketFlame.mat", Color.white, true);
        Color[] bodyColors = knights.Select(value => ExtractColor(FindPaletteMaterial(value.transform, null, null))).ToArray();
        string[] report = new string[knights.Length + 1];
        report[0] = $"Template={template.name}; ringMaterial={AssetDatabase.GetAssetPath(ringMaterial)}; flameMaterial={AssetDatabase.GetAssetPath(flameMaterial)}";

        for (int i = 0; i < knights.Length; i++)
        {
            Color effectColor = FindDifferentPaletteColor(bodyColors, i);
            report[i + 1] = ApplyKnight(knights[i], template, templateRight, templateLeft, templateRightWeapon, templateLeftWeapon, flameMaterial, effectColor);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllLines(ReportPath, report);
        AssetDatabase.SaveAssets();
        Debug.Log($"KNIGHT_SIMPLE_WEAPONS_COMPLETE count={knights.Length} template={template.name}");
    }

    private static string ApplyKnight(EnemyWorker enemy, EnemyWorker template, Transform sourceRight, Transform sourceLeft,
        Transform sourceRightWeapon, Transform sourceLeftWeapon, Material flameMaterial, Color effectColor)
    {
        Animator animator = enemy.GetComponent<Animator>();
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightSocket;
        Transform leftSocket;
        Transform rightWeapon;
        Transform leftWeapon;
        Material palette = FindPaletteMaterial(enemy.transform, null, null);

        if (enemy == template)
        {
            rightSocket = sourceRight;
            leftSocket = sourceLeft;
            rightWeapon = sourceRightWeapon;
            leftWeapon = sourceLeftWeapon;
        }
        else
        {
            DestroySockets(animator, "Weapon_r Socket");
            DestroySockets(animator, "Weapon_l Socket");
            rightSocket = UnityEngine.Object.Instantiate(sourceRight.gameObject, rightHand).transform;
            leftSocket = UnityEngine.Object.Instantiate(sourceLeft.gameObject, leftHand).transform;
            rightSocket.name = "Weapon_r Socket";
            leftSocket.name = "Weapon_l Socket";
            CopyLocalTransform(sourceRight, rightSocket);
            CopyLocalTransform(sourceLeft, leftSocket);
            DestroyDirectChild(rightHand, sourceRightWeapon.name);
            DestroyDirectChild(leftHand, sourceLeftWeapon.name);
            rightWeapon = UnityEngine.Object.Instantiate(sourceRightWeapon.gameObject, rightHand).transform;
            leftWeapon = UnityEngine.Object.Instantiate(sourceLeftWeapon.gameObject, leftHand).transform;
            rightWeapon.name = sourceRightWeapon.name;
            leftWeapon.name = sourceLeftWeapon.name;
            CopyLocalTransform(sourceRightWeapon, rightWeapon);
            CopyLocalTransform(sourceLeftWeapon, leftWeapon);
        }

        rightSocket.gameObject.SetActive(true);
        leftSocket.gameObject.SetActive(true);
        if (rightWeapon.parent != rightSocket) rightWeapon.SetParent(rightSocket, true);
        if (leftWeapon.parent != leftSocket) leftWeapon.SetParent(leftSocket, true);
        foreach (Transform child in DirectChildren(leftSocket)) child.gameObject.SetActive(false);
        leftWeapon.gameObject.SetActive(false);
        rightWeapon.gameObject.SetActive(true);

        Transform sword = FindSword(rightWeapon)
                          ?? throw new InvalidOperationException($"{enemy.name}: copied yellow right-hand weapon has no rendered sword.");
        EnableOnlySwordBranch(rightWeapon, sword);
        if (palette != null)
            foreach (Renderer renderer in rightWeapon.GetComponentsInChildren<Renderer>(true)
                         .Where(value => !value.name.StartsWith("Broken Edge", StringComparison.Ordinal)))
                renderer.sharedMaterials = Enumerable.Repeat(palette, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();

        foreach (Transform transform in enemy.GetComponentsInChildren<Transform>(true))
        {
            if (transform == rightSocket || transform.IsChildOf(rightSocket) || transform == leftSocket || transform.IsChildOf(leftSocket)
                || transform == rightWeapon || transform.IsChildOf(rightWeapon) || transform == leftWeapon || transform.IsChildOf(leftWeapon)) continue;
            if (transform.name == "Weapon_Blend" || transform.name == "RightHand Weapon (Inactive)" || transform.name == "LeftHand Weapon (Inactive)")
                transform.gameObject.SetActive(false);
        }

        SerializedObject serializedEnemy = new SerializedObject(enemy);
        DisableConstraint(serializedEnemy, "weaponBlend");
        DisableConstraint(serializedEnemy, "rightArmIK");
        DisableConstraint(serializedEnemy, "leftArmIK");
        serializedEnemy.FindProperty("sword").objectReferenceValue = sword;
        serializedEnemy.FindProperty("swordOffset").vector3Value = sword.localPosition;
        serializedEnemy.ApplyModifiedPropertiesWithoutUndo();

        foreach (BrokenEdgeBladeVfx old in enemy.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
            UnityEngine.Object.DestroyImmediate(old);
        foreach (Transform old in enemy.GetComponentsInChildren<Transform>(true)
                     .Where(value => value.name.StartsWith("Broken Edge Blade", StringComparison.Ordinal)).ToArray())
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        BrokenEdgeBladeVfx bladeVfx = sword.gameObject.AddComponent<BrokenEdgeBladeVfx>();
        bladeVfx.Configure(enemy.transform, sword);
        SetBladeColor(bladeVfx, effectColor);
        bladeVfx.EnsureBladeOnlyMarkers();

        TargetMatching matching = enemy.GetComponentsInChildren<TargetMatching>(true).FirstOrDefault();
        if (matching != null)
        {
            matching.Target = sword;
            SwordHitbox.Configure(matching.gameObject, enemy);
            EditorUtility.SetDirty(matching);
        }

        KnightRocketLegVfx rocket = enemy.GetComponent<KnightRocketLegVfx>() ?? enemy.gameObject.AddComponent<KnightRocketLegVfx>();
        rocket.Configure(animator, flameMaterial, effectColor);
        EditorUtility.SetDirty(rocket);
        EditorUtility.SetDirty(enemy);
        return $"{enemy.name}: right={Path(rightSocket, enemy.transform)}, leftChildrenOff={DirectChildren(leftSocket).All(value => !value.gameObject.activeSelf)}, sword={Path(sword, enemy.transform)}, bodyColor={ColorUtility.ToHtmlStringRGB(ExtractColor(palette))}, effectColor={ColorUtility.ToHtmlStringRGB(effectColor)}, bladeVfx={sword.GetComponent<BrokenEdgeBladeVfx>() != null}, rocket={rocket != null}, hitterTarget={(matching != null ? Path(matching.Target, enemy.transform) : "MISSING")}";
    }

    private static bool IsYellowKnight(EnemyWorker enemy)
    {
        Material body = FindPaletteMaterial(enemy.transform, null, null);
        return body != null && AssetDatabase.GetAssetPath(body).IndexOf("M_WarriorSet_07.mat", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Transform FindSocket(Animator animator, HumanBodyBones bone, string name)
    {
        Transform hand = animator.GetBoneTransform(bone);
        return hand != null ? hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(value => value.name == name) : null;
    }

    private static void DestroySockets(Animator animator, string name)
    {
        foreach (Transform value in animator.GetComponentsInChildren<Transform>(true).Where(value => value.name == name).ToArray())
            UnityEngine.Object.DestroyImmediate(value.gameObject);
    }

    private static Transform FindHandWeaponRoot(Transform hand, Transform emptySocket, string side)
    {
        if (hand == null) return null;
        return DirectChildren(hand)
            .Where(value => value != emptySocket && value.GetComponentInChildren<Renderer>(true) != null)
            .OrderByDescending(value => value.name.IndexOf(side + "Hand Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(value => value.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private static void DestroyDirectChild(Transform parent, string name)
    {
        foreach (Transform child in DirectChildren(parent).Where(value => value.name == name).ToArray())
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static Transform FindSword(Transform socket)
        => socket.GetComponentsInChildren<Renderer>(true)
            .Where(value => !value.name.StartsWith("Broken Edge", StringComparison.Ordinal))
            .OrderByDescending(value => value.name.IndexOf("Katana", StringComparison.OrdinalIgnoreCase) >= 0 || value.name.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(value => value.transform).FirstOrDefault();

    private static void EnableOnlySwordBranch(Transform socket, Transform sword)
    {
        Transform top = sword;
        while (top.parent != null && top.parent != socket) top = top.parent;
        foreach (Transform child in DirectChildren(socket)) child.gameObject.SetActive(child == top);
        for (Transform current = sword; current != null && current != socket; current = current.parent) current.gameObject.SetActive(true);
    }

    private static Material FindPaletteMaterial(Transform enemy, Transform rightSocket, Transform leftSocket)
    {
        Material skinned = enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .SelectMany(value => value.sharedMaterials)
            .Where(value => value != null)
            .OrderByDescending(value => AssetDatabase.GetAssetPath(value).IndexOf("GeneratedCharacterSets/M_WarriorSet_", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(value => AssetDatabase.GetAssetPath(value).IndexOf("M_Knight_BrokenEdge_Blue", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
        if (skinned != null) return skinned;
        return enemy.GetComponentsInChildren<Renderer>(true)
            .Where(value => (rightSocket == null || !value.transform.IsChildOf(rightSocket)) && (leftSocket == null || !value.transform.IsChildOf(leftSocket)))
            .SelectMany(value => value.sharedMaterials)
            .Where(value => value != null)
            .OrderByDescending(value => AssetDatabase.GetAssetPath(value).IndexOf("GeneratedCharacterSets/M_WarriorSet_", StringComparison.OrdinalIgnoreCase) >= 0)
            .ThenByDescending(value => AssetDatabase.GetAssetPath(value).IndexOf("M_Knight_BrokenEdge_Blue", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private static void DisableConstraint(SerializedObject owner, string property)
    {
        Component component = owner.FindProperty(property)?.objectReferenceValue as Component;
        if (component == null) return;
        var weight = component.GetType().GetProperty("weight");
        weight?.SetValue(component, 0f);
        if (component is Behaviour behaviour) behaviour.enabled = false;
        EditorUtility.SetDirty(component);
    }

    private static Material GetOrCreateVfxMaterial(string path, Color color, bool additive)
    {
        string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources", "VFX");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 3f);
        if (path.IndexOf("RocketFlame", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Texture2D flameTexture = GetOrCreateFlameTexture();
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", flameTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", flameTexture);
        }
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 1f : 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.renderQueue = 3000;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D GetOrCreateFlameTexture()
    {
        const string path = "Assets/Resources/VFX/T_KnightRocketFlame.asset";
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture != null) return texture;
        texture = new Texture2D(32, 64, TextureFormat.RGBA32, false)
        {
            name = "T_KnightRocketFlame",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        for (int y = 0; y < texture.height; y++)
        for (int x = 0; x < texture.width; x++)
        {
            float u = (x + 0.5f) / texture.width * 2f - 1f;
            float v = (y + 0.5f) / texture.height;
            float width = Mathf.Lerp(0.18f, 0.78f, Mathf.Sin(v * Mathf.PI));
            float side = Mathf.Clamp01(1f - Mathf.Abs(u) / Mathf.Max(0.02f, width));
            float length = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.55f);
            float alpha = Mathf.SmoothStep(0f, 1f, side) * length;
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        texture.Apply(false, true);
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    private static void SetBladeColor(BrokenEdgeBladeVfx effect, Color color)
    {
        SerializedObject data = new SerializedObject(effect);
        SerializedProperty glow = data.FindProperty("glowColor");
        SerializedProperty core = data.FindProperty("coreColor");
        if (glow != null) glow.colorValue = new Color(color.r * 2.4f, color.g * 2.4f, color.b * 2.4f, 0.95f);
        if (core != null) core.colorValue = Color.Lerp(color, Color.white, 0.68f);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Color ExtractColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty("_BaseColor"))
        {
            Color baseColor = material.GetColor("_BaseColor");
            if (Vector3.Distance(new Vector3(baseColor.r, baseColor.g, baseColor.b), Vector3.one) > 0.08f)
                return baseColor;
        }
        if (material.HasProperty("_LightTint")) return material.GetColor("_LightTint");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    private static Color FindDifferentPaletteColor(Color[] colors, int ownIndex)
    {
        Color own = colors[ownIndex];
        for (int offset = 1; offset < colors.Length; offset++)
        {
            Color candidate = colors[(ownIndex + offset) % colors.Length];
            if (Vector3.Distance(new Vector3(own.r, own.g, own.b), new Vector3(candidate.r, candidate.g, candidate.b)) > 0.12f)
                return candidate;
        }
        return new Color(0.2f, 0.9f, 1f, 1f);
    }

    private static Transform[] DirectChildren(Transform parent)
        => Enumerable.Range(0, parent.childCount).Select(parent.GetChild).ToArray();

    private static void CopyLocalTransform(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static string Path(Transform value, Transform root)
        => value == null ? "MISSING" : AnimationUtility.CalculateTransformPath(value, root);
}
