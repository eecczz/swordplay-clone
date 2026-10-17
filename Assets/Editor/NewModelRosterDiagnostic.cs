using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Swordplay.Visuals;

public static class NewModelRosterDiagnostic
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string ReportPath = "Library/NewModelRosterDiagnostic.txt";
    private const string InstallReportPath = "Library/NewModelRosterInstall.txt";
    private static readonly string[] PartGroups =
    {
        "HEADS", "CHESTS", "ARMS", "BELTS", "LEGS", "FEET",
        "NOSES", "HAIRS", "FACE HAIRS", "EYES", "EYEBROWS", "EARS"
    };

    public static void RunFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var lines = new List<string>();
        foreach (PlayerWorker player in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerWorker>(true)))
            WriteCharacter(player.gameObject, "PLAYER", player, lines);
        foreach (EnemyWorker enemy in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true)))
            WriteCharacter(enemy.gameObject, "ENEMY", enemy, lines);
        File.WriteAllLines(ReportPath, lines);
    }

    public static void InstallFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PlayerWorker player = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<PlayerWorker>(true))
            .FirstOrDefault(value => value.gameObject.activeInHierarchy && value.name.IndexOf("GanzSe", StringComparison.OrdinalIgnoreCase) >= 0);
        EnemyWorker source = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .FirstOrDefault(value => value.gameObject.activeInHierarchy && value.name.IndexOf("GanzSe", StringComparison.OrdinalIgnoreCase) >= 0
                                     && value.name.IndexOf("Roster", StringComparison.OrdinalIgnoreCase) < 0);
        if (player == null || source == null)
            throw new InvalidOperationException($"Active modular player/enemy missing. player={player != null}, enemy={source != null}");

        foreach (EnemyWorker oldClone in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
                     .Where(value => value.name.IndexOf("GanzSe Roster", StringComparison.OrdinalIgnoreCase) >= 0)
                     .ToArray())
            UnityEngine.Object.DestroyImmediate(oldClone.gameObject);

        ConfigureHeadLook(player);
        ApplyVariant(player.gameObject, 0);
        EnablePlayerShoulderArmor(player.gameObject);
        source.gameObject.tag = "Enemy";
        ConfigureHeadLook(source);
        ApplyVariant(source.gameObject, 1);

        var roster = new List<GameObject> { source.gameObject };
        Vector3[] offsets =
        {
            new Vector3(-4f, 0f, 3f), new Vector3(-2f, 0f, 5f), new Vector3(0f, 0f, 6f),
            new Vector3(2f, 0f, 5f), new Vector3(4f, 0f, 3f)
        };
        for (int i = 0; i < offsets.Length; i++)
        {
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
            clone.name = $"GanzSe Roster Enemy {i + 2:00}";
            clone.tag = "Enemy";
            clone.transform.SetParent(source.transform.parent, true);
            Vector3 requested = source.transform.position + offsets[i];
            clone.transform.position = NavMesh.SamplePosition(requested, out NavMeshHit hit, 3f, NavMesh.AllAreas)
                ? hit.position
                : requested;
            clone.transform.rotation = source.transform.rotation;
            ConfigureHeadLook(clone.GetComponent<EnemyWorker>());
            ApplyVariant(clone, i + 2);
            clone.SetActive(true);
            roster.Add(clone);
        }

        Color[] effectPalette =
        {
            new Color(0.15f, 0.85f, 1f, 1f),
            new Color(1f, 0.24f, 0.48f, 1f),
            new Color(1f, 0.72f, 0.12f, 1f),
            new Color(0.25f, 1f, 0.42f, 1f),
            new Color(0.72f, 0.3f, 1f, 1f),
            new Color(1f, 0.38f, 0.08f, 1f)
        };
        for (int i = 0; i < roster.Count; i++)
            ApplyEffectPalette(roster[i], effectPalette[i]);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        var lines = new List<string>
        {
            $"Player={player.name} signature={VariantSignature(player.gameObject)}",
            $"EnemyCount={roster.Count}"
        };
        foreach (GameObject enemy in roster)
            lines.Add($"{enemy.name}: active={enemy.activeSelf}, tag={enemy.tag}, signature={VariantSignature(enemy)}, headAimDisabled={HeadAimDisabled(enemy)}, effects={EffectSignature(enemy)}");
        lines.Add($"DistinctEnemySignatures={roster.Select(VariantSignature).Distinct().Count()}/{roster.Count}");
        lines.Add($"PlayerShoulder={ActivePart(player.gameObject, "ARMS")}");
        lines.Add($"DistinctEffectColors={roster.Select(EffectColor).Distinct().Count()}/{roster.Count}");
        File.WriteAllLines(InstallReportPath, lines);

        if (roster.Count != 6
            || roster.Select(VariantSignature).Distinct().Count() != roster.Count
            || roster.Select(EffectColor).Distinct().Count() != roster.Count
            || roster.Any(value => !HeadAimDisabled(value) || !EffectsMatch(value))
            || ActivePart(player.gameObject, "ARMS").IndexOf("Type 5", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException($"Roster validation failed. See {InstallReportPath}");
    }

    private static void EnablePlayerShoulderArmor(GameObject player)
    {
        Transform arms = player.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(value => value.name.Equals("ARMS", StringComparison.OrdinalIgnoreCase));
        if (arms == null) return;
        SkinnedMeshRenderer[] candidates = arms.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer shoulderArmor = candidates.FirstOrDefault(value =>
            value.name.Equals("Arm Armor Type 5 Color 3", StringComparison.OrdinalIgnoreCase))
            ?? candidates.LastOrDefault();
        foreach (SkinnedMeshRenderer candidate in candidates)
            candidate.gameObject.SetActive(candidate == shoulderArmor);
    }

    private static void ApplyEffectPalette(GameObject enemy, Color color)
    {
        Animator animator = enemy.GetComponent<Animator>();
        Material flameMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/VFX/M_KnightRocketFlame.mat");
        KnightRocketLegVfx rocket = enemy.GetComponent<KnightRocketLegVfx>()
            ?? enemy.AddComponent<KnightRocketLegVfx>();
        rocket.Configure(animator, flameMaterial, color);
        EditorUtility.SetDirty(rocket);

        BrokenEdgeBladeVfx[] blades = enemy.GetComponentsInChildren<BrokenEdgeBladeVfx>(true);
        if (blades.Length == 0)
        {
            Transform sword = enemy.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.name.Equals("sword_01", StringComparison.OrdinalIgnoreCase));
            if (sword != null)
                blades = new[] { sword.gameObject.AddComponent<BrokenEdgeBladeVfx>() };
        }
        foreach (BrokenEdgeBladeVfx blade in blades)
        {
            blade.enabled = true;
            blade.Configure(enemy.transform);
            SerializedObject serialized = new SerializedObject(blade);
            Color glow = new Color(color.r * 2.4f, color.g * 2.4f, color.b * 2.4f, 0.95f);
            Color core = Color.Lerp(color, Color.white, 0.7f);
            core.a = 0.86f;
            serialized.FindProperty("glowColor").colorValue = glow;
            serialized.FindProperty("coreColor").colorValue = core;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            blade.EnsureBladeOnlyMarkers();
            EditorUtility.SetDirty(blade);
        }
    }

    private static string EffectSignature(GameObject enemy)
    {
        KnightRocketLegVfx rocket = enemy.GetComponent<KnightRocketLegVfx>();
        BrokenEdgeBladeVfx blade = enemy.GetComponentInChildren<BrokenEdgeBladeVfx>(true);
        Color rocketColor = rocket != null ? rocket.EffectColor : Color.clear;
        Color glow = blade != null
            ? new SerializedObject(blade).FindProperty("glowColor").colorValue
            : Color.clear;
        Color normalizedGlow = new Color(glow.r / 2.4f, glow.g / 2.4f, glow.b / 2.4f, 1f);
        float delta = Vector3.Distance(
            new Vector3(rocketColor.r, rocketColor.g, rocketColor.b),
            new Vector3(normalizedGlow.r, normalizedGlow.g, normalizedGlow.b));
        return $"#{ColorUtility.ToHtmlStringRGB(rocketColor)}/matchDelta={delta:F5}";
    }

    private static string EffectColor(GameObject enemy)
    {
        KnightRocketLegVfx rocket = enemy.GetComponent<KnightRocketLegVfx>();
        return rocket != null ? ColorUtility.ToHtmlStringRGB(rocket.EffectColor) : "MISSING";
    }

    private static bool EffectsMatch(GameObject enemy)
    {
        KnightRocketLegVfx rocket = enemy.GetComponent<KnightRocketLegVfx>();
        BrokenEdgeBladeVfx blade = enemy.GetComponentInChildren<BrokenEdgeBladeVfx>(true);
        if (rocket == null || blade == null) return false;
        Color glow = new SerializedObject(blade).FindProperty("glowColor").colorValue;
        Color normalized = new Color(glow.r / 2.4f, glow.g / 2.4f, glow.b / 2.4f, 1f);
        return Vector3.Distance(
            new Vector3(rocket.EffectColor.r, rocket.EffectColor.g, rocket.EffectColor.b),
            new Vector3(normalized.r, normalized.g, normalized.b)) < 0.001f;
    }

    private static string ActivePart(GameObject root, string groupName)
    {
        Transform group = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(value => value.name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        return group?.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(value => value.gameObject.activeSelf)?.name ?? "MISSING";
    }

    private static void ApplyVariant(GameObject root, int variantIndex)
    {
        for (int groupIndex = 0; groupIndex < PartGroups.Length; groupIndex++)
        {
            Transform group = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.name.Equals(PartGroups[groupIndex], StringComparison.OrdinalIgnoreCase));
            if (group == null) continue;
            SkinnedMeshRenderer[] candidates = group.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderBy(value => value.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0) continue;
            int selected = (variantIndex * (groupIndex * 2 + 1) + groupIndex * 3) % candidates.Length;
            for (int i = 0; i < candidates.Length; i++)
                candidates[i].gameObject.SetActive(i == selected);
        }
    }

    private static void ConfigureHeadLook(Component worker)
    {
        if (worker == null) return;
        SerializedObject serialized = new SerializedObject(worker);
        SerializedProperty offset = serialized.FindProperty("headRotOffset");
        if (offset != null) offset.vector3Value = Vector3.zero;
        MultiAimConstraint aim = serialized.FindProperty("ma")?.objectReferenceValue as MultiAimConstraint;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (aim != null)
        {
            aim.weight = 0f;
            aim.enabled = false;
            MultiParentConstraint parent = aim.GetComponent<MultiParentConstraint>();
            if (parent != null)
            {
                parent.weight = 0f;
                parent.enabled = false;
                EditorUtility.SetDirty(parent);
            }
            EditorUtility.SetDirty(aim);
        }
        EditorUtility.SetDirty(worker);
    }

    private static string VariantSignature(GameObject root)
        => string.Join("|", PartGroups.Select(groupName =>
        {
            Transform group = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            SkinnedMeshRenderer active = group?.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(value => value.gameObject.activeSelf);
            return active?.name ?? "NONE";
        }));

    private static bool HeadAimDisabled(GameObject root)
        => root.GetComponentsInChildren<MultiAimConstraint>(true).All(value => !value.enabled || value.weight <= 0f);

    private static void WriteCharacter(GameObject root, string kind, Component worker, List<string> lines)
    {
        Animator animator = root.GetComponent<Animator>() ?? root.GetComponentInChildren<Animator>(true);
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        SerializedObject serialized = new SerializedObject(worker);
        Vector3 offset = serialized.FindProperty("headRotOffset")?.vector3Value ?? Vector3.zero;
        MultiAimConstraint aim = serialized.FindProperty("ma")?.objectReferenceValue as MultiAimConstraint;
        lines.Add($"===== {kind} {root.name} activeSelf={root.activeSelf} activeHierarchy={root.activeInHierarchy} tag={root.tag} =====");
        lines.Add($"animator={animator?.name ?? "MISSING"} human={animator != null && animator.isHuman} head={PathOf(head, root.transform)} headLocal={head?.localEulerAngles.ToString("F2") ?? "MISSING"} headWorld={head?.eulerAngles.ToString("F2") ?? "MISSING"} offset={offset.ToString("F2")} aim={PathOf(aim?.transform, root.transform)} aimEnabled={aim != null && aim.enabled} aimWeight={(aim != null ? aim.weight : -1f):F2}");
        if (aim != null)
        {
            MultiAimConstraintData data = aim.data;
            lines.Add($"aimObject={PathOf(data.constrainedObject, root.transform)} aimAxis={data.aimAxis} upAxis={data.upAxis} maintain={data.maintainOffset} sources={data.sourceObjects.Count}");
        }

        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        lines.Add($"skinnedCount={renderers.Length}");
        foreach (IGrouping<string, SkinnedMeshRenderer> group in renderers.GroupBy(renderer => renderer.transform.parent != null ? PathOf(renderer.transform.parent, root.transform) : "ROOT"))
        {
            lines.Add($"GROUP {group.Key}");
            foreach (SkinnedMeshRenderer renderer in group.OrderBy(value => value.name, StringComparer.OrdinalIgnoreCase))
                lines.Add($"  {(renderer.gameObject.activeSelf ? "ON " : "off")} {renderer.name} mesh={renderer.sharedMesh?.name ?? "MISSING"}");
        }
        lines.Add(string.Empty);
    }

    private static string PathOf(Transform value, Transform root)
    {
        if (value == null) return "MISSING";
        string path = value.name;
        for (Transform current = value.parent; current != null && current != root; current = current.parent)
            path = current.name + "/" + path;
        return path;
    }
}
