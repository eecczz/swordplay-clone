using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

public static class CombatArchitectureRepair
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string ReportPath = "Library/CombatArchitectureRepair.txt";

    [MenuItem("Tools/Swordplay/Repair Combatant Root Architecture")]
    public static void ApplyAndValidate()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyToSceneAndValidate(scene);
    }

    public static void ApplyActiveSceneAndValidate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("No loaded scene is available for combat architecture repair.");
        ApplyToSceneAndValidate(scene);
    }

    private static void ApplyToSceneAndValidate(Scene scene)
    {
        var lines = new List<string>();
        int migratedAgents = 0;
        int removedJoints = 0;
        int disabledHeadConstraints = 0;
        int calibratedHeadAxes = 0;

        Component[] combatants = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Component>(true))
            .Where(component => component.gameObject.activeInHierarchy
                                && (component is PlayerWorker || component is EnemyWorker))
            .ToArray();

        foreach (Component combatant in combatants)
        {
            migratedAgents += MoveAgentToRoot(combatant.gameObject);
            ConfigureSerializedNavigation(combatant);
            calibratedHeadAxes += CalibrateHeadAxis(combatant);

            ConfigurableJoint legacyJoint = combatant.GetComponent<ConfigurableJoint>();
            if (legacyJoint != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyJoint);
                removedJoints++;
            }

            disabledHeadConstraints += DisableSerializedHeadRig(combatant);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException($"Could not save {ScenePath}.");
        AssetDatabase.SaveAssets();

        foreach (Component combatant in combatants.Where(value => value != null))
        {
            NavMeshAgent rootAgent = combatant.GetComponent<NavMeshAgent>();
            NavMeshAgent[] childAgents = combatant.GetComponentsInChildren<NavMeshAgent>(true)
                .Where(agent => agent.gameObject != combatant.gameObject)
                .ToArray();
            MultiAimConstraint aim = ReadHeadAim(combatant);
            MultiParentConstraint parent = aim != null ? aim.GetComponent<MultiParentConstraint>() : null;
            SerializedObject serialized = new SerializedObject(combatant);
            Vector3 headOffset = serialized.FindProperty("headRotOffset")?.vector3Value ?? Vector3.zero;
            lines.Add($"{GetPath(combatant.transform)}: rootAgent={rootAgent != null}, childAgents={childAgents.Length}, " +
                      $"rootJoint={combatant.GetComponent<ConfigurableJoint>() != null}, " +
                      $"headAimDisabled={aim == null || (!aim.enabled && aim.weight <= 0f)}, " +
                      $"headParentDisabled={parent == null || (!parent.enabled && parent.weight <= 0f)}, " +
                      $"headRotOffset={headOffset}");
            if (rootAgent == null || childAgents.Length != 0 || combatant.GetComponent<ConfigurableJoint>() != null)
                throw new InvalidOperationException($"Root architecture validation failed for {GetPath(combatant.transform)}.");
        }

        lines.Insert(0, $"Combatants={combatants.Length}, MigratedAgents={migratedAgents}, RemovedRootJoints={removedJoints}, DisabledHeadConstraints={disabledHeadConstraints}, CalibratedHeadAxes={calibratedHeadAxes}");
        File.WriteAllLines(ReportPath, lines);
        Debug.Log($"[Combat Architecture] COMPLETE — {lines[0]}");
    }

    private static int CalibrateHeadAxis(Component combatant)
    {
        SerializedObject serialized = new SerializedObject(combatant);
        SerializedProperty property = serialized.FindProperty("headRotOffset");
        if (property == null || property.vector3Value.sqrMagnitude >= 0.0001f)
            return 0;

        Animator animator = combatant.GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
            return 0;
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
            return 0;

        Quaternion forwardRotation = Quaternion.LookRotation(combatant.transform.forward, Vector3.up);
        Quaternion correction = Quaternion.Inverse(forwardRotation) * head.rotation;
        property.vector3Value = ToSignedEuler(correction.eulerAngles);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(combatant);
        return 1;
    }

    private static Vector3 ToSignedEuler(Vector3 value)
    {
        return new Vector3(
            Mathf.DeltaAngle(0f, value.x),
            Mathf.DeltaAngle(0f, value.y),
            Mathf.DeltaAngle(0f, value.z));
    }

    public static void ReportHierarchy()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var lines = new List<string>();
        Component[] combatants = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Component>(true))
            .Where(component => component.gameObject.activeInHierarchy
                                && (component is PlayerWorker || component is EnemyWorker))
            .ToArray();
        foreach (Component combatant in combatants)
        {
            lines.Add($"COMBATANT {combatant.GetType().Name} path={GetPath(combatant.transform)} tag={combatant.tag}");
            for (Transform current = combatant.transform; current != null; current = current.parent)
                lines.Add($"  ANCESTOR {GetPath(current)} tag={current.tag} nav={current.GetComponent<NavMeshAgent>() != null} animator={current.GetComponent<Animator>() != null} rigid={current.GetComponent<Rigidbody>() != null}");
            Transform hierarchyRoot = combatant.transform.root;
            foreach (NavMeshAgent agent in hierarchyRoot.GetComponentsInChildren<NavMeshAgent>(true))
                lines.Add($"  AGENT {GetPath(agent.transform)} enabled={agent.enabled} radius={agent.radius:F3} height={agent.height:F3}");
        }
        File.WriteAllLines(ReportPath, lines);
        Debug.Log($"[Combat Architecture] HIERARCHY REPORT — combatants={combatants.Length}");
    }

    private static int MoveAgentToRoot(GameObject root)
    {
        NavMeshAgent rootAgent = root.GetComponent<NavMeshAgent>();
        NavMeshAgent[] agents = root.GetComponentsInChildren<NavMeshAgent>(true);
        NavMeshAgent source = rootAgent ?? agents.FirstOrDefault(agent => agent.gameObject != root);
        int changed = 0;

        if (rootAgent == null)
        {
            if (source != null)
            {
                bool enabled = source.enabled;
                source.enabled = false;
                ComponentUtility.CopyComponent(source);
                if (!ComponentUtility.PasteComponentAsNew(root))
                    throw new InvalidOperationException($"Failed to copy NavMeshAgent to {GetPath(root.transform)}.");
                rootAgent = root.GetComponent<NavMeshAgent>();
                rootAgent.enabled = enabled;
            }
            else
            {
                // The active modular player never had a serialized agent. The old Balance
                // agents are siblings belonging to inactive legacy models, not a valid source.
                rootAgent = root.AddComponent<NavMeshAgent>();
                rootAgent.radius = 0.35f;
                rootAgent.height = 1.5f;
                rootAgent.baseOffset = 0f;
                rootAgent.speed = 3.5f;
                rootAgent.acceleration = 14f;
                rootAgent.angularSpeed = 0f;
                rootAgent.stoppingDistance = 0.1f;
                rootAgent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            }
            changed++;
        }

        foreach (NavMeshAgent agent in root.GetComponentsInChildren<NavMeshAgent>(true))
        {
            if (agent == rootAgent) continue;
            UnityEngine.Object.DestroyImmediate(agent);
            changed++;
        }

        EditorUtility.SetDirty(rootAgent);
        return changed;
    }

    private static void ConfigureSerializedNavigation(Component combatant)
    {
        NavMeshAgent agent = combatant.GetComponent<NavMeshAgent>();
        if (agent == null)
            throw new InvalidOperationException($"Missing root NavMeshAgent on {GetPath(combatant.transform)}.");

        SerializedObject worker = new SerializedObject(combatant);
        float speed = worker.FindProperty("movementSpeed")?.floatValue ?? 5f;
        agent.speed = speed;
        agent.acceleration = Mathf.Max(12f, speed * 4f);
        agent.angularSpeed = 0f;
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.autoTraverseOffMeshLink = true;
        agent.stoppingDistance = combatant is PlayerWorker ? 0.1f : 0.25f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        EditorUtility.SetDirty(agent);

        if (combatant is PlayerWorker)
        {
            SerializedProperty stop = worker.FindProperty("navigationStoppingDistance");
            SerializedProperty resume = worker.FindProperty("navigationResumeDistance");
            stop.floatValue = Mathf.Max(2.4f, stop.floatValue);
            resume.floatValue = Mathf.Max(stop.floatValue + 1f, resume.floatValue);
        }
        else
        {
            SerializedProperty radius = worker.FindProperty("engagementRadius");
            SerializedProperty spacing = worker.FindProperty("additionalRingSpacing");
            radius.floatValue = Mathf.Clamp(radius.floatValue, 2.7f, 3f);
            spacing.floatValue = Mathf.Min(spacing.floatValue, 1.5f);
        }
        worker.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(combatant);
    }

    private static int DisableSerializedHeadRig(Component combatant)
    {
        MultiAimConstraint aim = ReadHeadAim(combatant);
        if (aim == null) return 0;

        int changed = 0;
        if (aim.enabled || aim.weight != 0f)
        {
            aim.weight = 0f;
            aim.enabled = false;
            EditorUtility.SetDirty(aim);
            changed++;
        }

        MultiParentConstraint parent = aim.GetComponent<MultiParentConstraint>();
        if (parent != null && (parent.enabled || parent.weight != 0f))
        {
            parent.weight = 0f;
            parent.enabled = false;
            EditorUtility.SetDirty(parent);
            changed++;
        }
        return changed;
    }

    private static MultiAimConstraint ReadHeadAim(Component combatant)
    {
        SerializedObject serialized = new SerializedObject(combatant);
        return serialized.FindProperty("ma")?.objectReferenceValue as MultiAimConstraint;
    }

    private static string GetPath(Transform value)
    {
        var names = new Stack<string>();
        for (Transform current = value; current != null; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }
}
