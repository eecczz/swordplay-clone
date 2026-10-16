using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WarriorRocketBladeInstaller
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string FlameMaterialPath = "Assets/Resources/VFX/M_KnightRocketFlame.mat";
    private const string ReportPath = "Library/WarriorRocketBladeReport.txt";

    [MenuItem("Tools/Swordplay/Apply Active Warrior Rocket Legs And Blade Outline")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        EnemyWorker[] warriors = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .Where(enemy => enemy.name.StartsWith("Warrior_Set_", StringComparison.OrdinalIgnoreCase))
            .Where(enemy => enemy.gameObject.activeInHierarchy)
            .OrderBy(enemy => enemy.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (warriors.Length == 0)
            throw new InvalidOperationException("No active generated warrior enemies were found.");
        if (warriors.Length > 16)
            throw new InvalidOperationException($"Refusing to modify {warriors.Length} warriors; the active-subset guard is 16.");

        Material flameMaterial = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath);
        if (flameMaterial == null)
            throw new InvalidOperationException($"Rocket flame material is missing: {FlameMaterialPath}");

        Color[] bodyColors = warriors.Select(ReadBodyColor).ToArray();
        var report = new List<string>();
        for (int i = 0; i < warriors.Length; i++)
        {
            Color effectColor = FarthestPaletteColor(bodyColors, i);
            report.Add(ApplyOne(warriors[i], flameMaterial, bodyColors[i], effectColor));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        File.WriteAllLines(ReportPath, report);
        Debug.Log($"[Warrior Rocket Blade] APPLY_COMPLETE — configured {warriors.Length} active warriors. Report: {Path.GetFullPath(ReportPath)}");
    }

    private static string ApplyOne(EnemyWorker enemy, Material flameMaterial, Color bodyColor, Color effectColor)
    {
        Animator animator = enemy.GetComponent<Animator>()
            ?? throw new InvalidOperationException($"{enemy.name}: Animator is missing.");
        Transform leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg)
            ?? throw new InvalidOperationException($"{enemy.name}: LeftUpperLeg is missing.");
        Transform rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg)
            ?? throw new InvalidOperationException($"{enemy.name}: RightUpperLeg is missing.");

        // Scaling the thigh roots collapses every descendant leg bone and its skinned
        // vertices at the hip/thruster point while leaving the torso animation intact.
        leftThigh.localScale = Vector3.zero;
        rightThigh.localScale = Vector3.zero;

        KnightRocketLegVfx rocket = enemy.GetComponent<KnightRocketLegVfx>()
            ?? enemy.gameObject.AddComponent<KnightRocketLegVfx>();
        rocket.Configure(animator, flameMaterial, effectColor);

        Transform sword = enemy.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(value => string.Equals(value.name, "sword_01", StringComparison.OrdinalIgnoreCase));
        if (sword == null)
            throw new InvalidOperationException($"{enemy.name}: sword_01 was not found.");

        foreach (BrokenEdgeBladeVfx previous in enemy.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
            if (previous.gameObject != sword.gameObject)
                UnityEngine.Object.DestroyImmediate(previous);
        BrokenEdgeBladeVfx blade = sword.GetComponent<BrokenEdgeBladeVfx>()
            ?? sword.gameObject.AddComponent<BrokenEdgeBladeVfx>();
        blade.enabled = true;
        blade.Configure(enemy.transform, sword);
        SetBladeColor(blade, effectColor);
        if (!blade.EnsureBladeOnlyMarkers())
        {
            // Returning false can also mean the existing markers were already correct.
            // Validate their references directly below rather than treating it as failure.
        }

        SerializedObject bladeData = new(blade);
        Transform bladeVisual = bladeData.FindProperty("bladeVisual")?.objectReferenceValue as Transform;
        Transform start = bladeData.FindProperty("bladeStartMarker")?.objectReferenceValue as Transform;
        Transform tip = bladeData.FindProperty("bladeTipMarker")?.objectReferenceValue as Transform;
        if (bladeVisual == null || start == null || tip == null)
            throw new InvalidOperationException($"{enemy.name}: blade outline did not resolve the sword renderer or blade markers.");
        float bladeLength = Vector3.Distance(start.position, tip.position);
        if (float.IsNaN(bladeLength) || float.IsInfinity(bladeLength) || bladeLength < 0.08f)
            throw new InvalidOperationException($"{enemy.name}: invalid fitted blade length {bladeLength}.");
        Renderer swordRenderer = bladeVisual.GetComponent<Renderer>() ?? bladeVisual.GetComponentInChildren<Renderer>(true);
        Mesh swordMesh = swordRenderer is SkinnedMeshRenderer skinned
            ? skinned.sharedMesh
            : swordRenderer != null ? swordRenderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (swordRenderer == null || swordMesh == null)
            throw new InvalidOperationException($"{enemy.name}: sword renderer mesh is missing.");
        float localBladeLength = Vector3.Distance(
            swordRenderer.transform.InverseTransformPoint(start.position),
            swordRenderer.transform.InverseTransformPoint(tip.position));
        float fullMeshLength = Mathf.Max(swordMesh.bounds.size.x, swordMesh.bounds.size.y, swordMesh.bounds.size.z);
        float bladeRatio = localBladeLength / Mathf.Max(0.0001f, fullMeshLength);
        if (bladeRatio < 0.55f || bladeRatio > 0.82f)
            throw new InvalidOperationException($"{enemy.name}: blade-only span ratio {bladeRatio:F4} still includes the grip/guard or misses the blade.");

        Color rocketColor = rocket.EffectColor;
        Color bladeColor = bladeData.FindProperty("glowColor").colorValue;
        Color normalizedBladeColor = new(bladeColor.r / 2.4f, bladeColor.g / 2.4f, bladeColor.b / 2.4f, 1f);
        float paletteDistance = RgbDistance(bodyColor, effectColor);
        float effectMatch = RgbDistance(rocketColor, normalizedBladeColor);
        if (paletteDistance < 0.12f || effectMatch > 0.01f)
            throw new InvalidOperationException($"{enemy.name}: effect palette validation failed (body distance={paletteDistance:F4}, rocket/blade delta={effectMatch:F4}).");

        EditorUtility.SetDirty(leftThigh);
        EditorUtility.SetDirty(rightThigh);
        EditorUtility.SetDirty(rocket);
        EditorUtility.SetDirty(blade);
        return $"{enemy.name}: body=#{ColorUtility.ToHtmlStringRGB(bodyColor)}, effect=#{ColorUtility.ToHtmlStringRGB(effectColor)}, leftThighScale={leftThigh.localScale}, rightThighScale={rightThigh.localScale}, rocketMatchDelta={effectMatch:F4}, sword={GetPath(sword, enemy.transform)}, bladeVisual={GetPath(bladeVisual, enemy.transform)}, bladeLength={bladeLength:F4}, bladeRatio={bladeRatio:F4}";
    }

    private static Color ReadBodyColor(EnemyWorker enemy)
    {
        WarriorShowcaseTint tint = enemy.GetComponent<WarriorShowcaseTint>();
        if (tint != null)
        {
            SerializedProperty property = new SerializedObject(tint).FindProperty("tintColor");
            if (property != null) return property.colorValue;
        }
        Material material = enemy.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(value => value != null && value.HasProperty("_WarriorTintColor"));
        return material != null ? material.GetColor("_WarriorTintColor") : Color.white;
    }

    private static Color FarthestPaletteColor(IReadOnlyList<Color> colors, int ownIndex)
    {
        if (colors.Count < 2)
            throw new InvalidOperationException("At least two active warrior colors are required to choose a different effect palette.");
        Color own = colors[ownIndex];
        int bestIndex = -1;
        float bestDistance = float.NegativeInfinity;
        for (int i = 0; i < colors.Count; i++)
        {
            if (i == ownIndex) continue;
            float distance = RgbDistance(own, colors[i]);
            if (distance <= bestDistance) continue;
            bestDistance = distance;
            bestIndex = i;
        }
        if (bestIndex < 0 || bestDistance < 0.12f)
            throw new InvalidOperationException($"Could not find a sufficiently different palette color for warrior {ownIndex}.");
        Color result = colors[bestIndex];
        result.a = 1f;
        return result;
    }

    private static float RgbDistance(Color first, Color second)
        => Vector3.Distance(new Vector3(first.r, first.g, first.b), new Vector3(second.r, second.g, second.b));

    private static void SetBladeColor(BrokenEdgeBladeVfx effect, Color color)
    {
        SerializedObject data = new(effect);
        SerializedProperty glow = data.FindProperty("glowColor");
        SerializedProperty core = data.FindProperty("coreColor");
        if (glow != null) glow.colorValue = new Color(color.r * 2.4f, color.g * 2.4f, color.b * 2.4f, 0.95f);
        if (core != null)
        {
            Color value = Color.Lerp(color, Color.white, 0.68f);
            value.a = 0.86f;
            core.colorValue = value;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string GetPath(Transform value, Transform root)
    {
        if (value == null) return "MISSING";
        var names = new Stack<string>();
        for (Transform current = value; current != null && current != root; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }
}
