using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

public static class WeaponRigPoseValidator
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string ReportPath = "Library/WeaponRigPoseValidation.txt";
    private const string RequestPath = "Library/WeaponRigPoseValidation.request";
    private const string RuntimeRequestPath = "Library/WeaponRigRuntimeValidation.request";
    private const string RuntimeReportPath = "Library/WeaponRigRuntimeValidation.txt";
    private static int runtimeFrame;
    private static EnemyWorker runtimeEnemy;
    private static MultiParentConstraint runtimeBlend;
    private static readonly List<string> runtimeLines = new List<string>();

    [InitializeOnLoadMethod]
    private static void InstallRequestedValidation()
    {
        if (File.Exists(RuntimeRequestPath))
        {
            EditorApplication.playModeStateChanged -= OnRuntimePlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnRuntimePlayModeStateChanged;
            string phase = File.ReadAllText(RuntimeRequestPath).Trim();
            if (phase == "done" && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(RuntimeRequestPath);
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
                return;
            }
            if (EditorApplication.isPlaying)
            {
                runtimeFrame = 0;
                runtimeEnemy = null;
                runtimeBlend = null;
                runtimeLines.Clear();
                EditorApplication.update -= RunRuntimeValidationFrame;
                EditorApplication.update += RunRuntimeValidationFrame;
            }
        }
        if (!File.Exists(RequestPath)) return;
        EditorApplication.update -= RunRequestedValidation;
        EditorApplication.update += RunRequestedValidation;
    }

    private static void OnRuntimePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!File.Exists(RuntimeRequestPath)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            runtimeFrame = 0;
            runtimeLines.Clear();
            EditorApplication.update -= RunRuntimeValidationFrame;
            EditorApplication.update += RunRuntimeValidationFrame;
        }
    }

    public static void BeginRuntimeValidationFromCommandLine()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        File.WriteAllText(RuntimeRequestPath, "enter");
        EditorApplication.isPlaying = true;
    }

    private static void RunRuntimeValidationFrame()
    {
        if (!EditorApplication.isPlaying) return;
        runtimeFrame++;
        if (runtimeFrame == 2)
        {
            runtimeEnemy = UnityEngine.Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(value => value.name == "GanzSe Free Modular Character Original 1_0");
            if (runtimeEnemy == null) throw new InvalidOperationException("Runtime validation enemy missing.");
            runtimeEnemy.gameObject.SetActive(true);
            runtimeBlend = Read<MultiParentConstraint>(runtimeEnemy, "weaponBlend");
            if (runtimeBlend == null) throw new InvalidOperationException("Runtime weapon blend missing.");
            runtimeEnemy.enabled = false;
            runtimeLines.Add($"graphValid={runtimeEnemy.GetComponent<RigBuilder>().graph.IsValid()} blend={PathOf(runtimeBlend.transform, runtimeEnemy.transform)}");
        }
        if (runtimeFrame == 8) SetRuntimeBlend(0f);
        if (runtimeFrame == 14) CaptureRuntimePose(0f);
        if (runtimeFrame == 18) SetRuntimeBlend(0.5f);
        if (runtimeFrame == 24) CaptureRuntimePose(0.5f);
        if (runtimeFrame == 28) SetRuntimeBlend(1f);
        if (runtimeFrame == 34)
        {
            CaptureRuntimePose(1f);
            Vector3 p0 = ParsePosition(runtimeLines[1]);
            Vector3 p1 = ParsePosition(runtimeLines[2]);
            Vector3 p2 = ParsePosition(runtimeLines[3]);
            float d01 = Vector3.Distance(p0, p1);
            float d12 = Vector3.Distance(p1, p2);
            runtimeLines.Add($"delta0toHalf={d01:F5} deltaHalfTo1={d12:F5} passed={d01 > 0.01f && d12 > 0.01f}");
            File.WriteAllLines(RuntimeReportPath, runtimeLines);
            File.WriteAllText(RuntimeRequestPath, "done");
            EditorApplication.update -= RunRuntimeValidationFrame;
            EditorApplication.isPlaying = false;
        }
    }

    private static void SetRuntimeBlend(float weight)
    {
        MultiParentConstraintData data = runtimeBlend.data;
        WeightedTransformArray sources = data.sourceObjects;
        sources.SetWeight(0, 1f - weight);
        sources.SetWeight(1, weight);
        data.sourceObjects = sources;
        runtimeBlend.data = data;
    }

    private static void CaptureRuntimePose(float weight)
    {
        Transform weapon = runtimeBlend.data.constrainedObject;
        runtimeLines.Add($"weight={weight:F2} finite={IsFinite(weapon.position) && IsFinite(weapon.rotation)} pos={weapon.position.x:R},{weapon.position.y:R},{weapon.position.z:R} rot={weapon.eulerAngles.ToString("F2")}");
    }

    private static Vector3 ParsePosition(string line)
    {
        string value = line.Substring(line.IndexOf("pos=", StringComparison.Ordinal) + 4).Split(' ')[0];
        string[] parts = value.Split(',');
        return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
    }

    private static void RunRequestedValidation()
    {
        if (File.Exists("Library/RepairAddedEnemyWeaponRig.request")) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        EditorApplication.update -= RunRequestedValidation;
        try
        {
            EditorSceneManager.SaveOpenScenes();
            ValidateFromCommandLine();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            File.Delete(RequestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public static void ValidateFromCommandLine()
    {
        var lines = new List<string>();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EnemyWorker[] enemies = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .ToArray();

        EnemyWorker bow = enemies.FirstOrDefault(enemy =>
            enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase) &&
            Read<MultiParentConstraint>(enemy, "weaponBlend") != null);
        EnemyWorker added = enemies.FirstOrDefault(enemy =>
            enemy.name == "GanzSe Free Modular Character Original 1_0");

        if (bow == null || added == null)
            throw new InvalidOperationException($"Validation subjects missing. bow={bow != null}, added={added != null}");

        ValidateEnemy(bow, "BOW", lines);
        ValidateEnemy(added, "ADDED", lines);
        File.WriteAllLines(ReportPath, lines);
        Debug.Log($"Weapon rig pose report: {Path.GetFullPath(ReportPath)}");
    }

    private static void ValidateEnemy(EnemyWorker enemy, string label, List<string> lines)
    {
        GameObject root = enemy.gameObject;
        root.SetActive(true);
        enemy.enabled = false;
        Animator animator = root.GetComponent<Animator>();
        RigBuilder builder = root.GetComponent<RigBuilder>();
        MultiParentConstraint blend = Read<MultiParentConstraint>(enemy, "weaponBlend");
        TwoBoneIKConstraint rightIk = Read<TwoBoneIKConstraint>(enemy, "rightArmIK");
        TwoBoneIKConstraint leftIk = Read<TwoBoneIKConstraint>(enemy, "leftArmIK");
        bool miiMotion = ReadBool(enemy, "miiMotion");
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform weapon = blend.data.constrainedObject;
        Transform rightTarget = rightIk.data.target;
        Transform leftTarget = leftIk.data.target;
        WeightedTransformArray sources = blend.data.sourceObjects;

        lines.Add($"===== {label}: {root.name} =====");
        lines.Add($"active={root.activeSelf} human={animator.isHuman} miiMotion={miiMotion} builderEnabled={builder.enabled} layers={builder.layers.Count}");
        lines.Add($"avatar={AssetDatabase.GetAssetPath(animator.avatar)} controller={AssetDatabase.GetAssetPath(animator.runtimeAnimatorController)}");
        lines.Add($"weapon={PathOf(weapon, root.transform)} scale={weapon.lossyScale.ToString("F5")}");
        lines.Add($"source0={PathOf(sources.GetTransform(0), root.transform)} w={sources.GetWeight(0):F3}");
        lines.Add($"source1={PathOf(sources.GetTransform(1), root.transform)} w={sources.GetWeight(1):F3}");
        lines.Add($"rightTarget={PathOf(rightTarget, root.transform)} leftTarget={PathOf(leftTarget, root.transform)}");
        lines.Add($"rightChain={PathOf(rightIk.data.root, root.transform)}>{PathOf(rightIk.data.mid, root.transform)}>{PathOf(rightIk.data.tip, root.transform)}");
        lines.Add($"leftChain={PathOf(leftIk.data.root, root.transform)}>{PathOf(leftIk.data.mid, root.transform)}>{PathOf(leftIk.data.tip, root.transform)}");
        lines.Add($"blendOffsets=pos:{blend.data.maintainPositionOffset},rot:{blend.data.maintainRotationOffset} weights=blend:{blend.weight:F2},R:{rightIk.weight:F2},L:{leftIk.weight:F2}");
        lines.Add($"sourceActive=R:{sources.GetTransform(0).gameObject.activeInHierarchy}/L:{sources.GetTransform(1).gameObject.activeInHierarchy} sourceGap={Vector3.Distance(sources.GetTransform(0).position, sources.GetTransform(1).position):F5}");
        // Keep this in the report: serialized references alone do not prove that the
        // playable graph was actually created.
        lines.Add($"constraintActive=blend:{blend.isActiveAndEnabled}/R:{rightIk.isActiveAndEnabled}/L:{leftIk.isActiveAndEnabled} animatorInitialized={animator.isInitialized}");
        lines.Add($"rigLayers={string.Join(" | ", builder.layers.Select(layer => $"{PathOf(layer.rig.transform, root.transform)} active={layer.active} rigEnabled={layer.rig.enabled} rigWeight={layer.rig.weight:F2}"))}");

        var controller = animator.runtimeAnimatorController as AnimatorController;
        string[] states = controller == null
            ? new[] { "" }
            : controller.layers[0].stateMachine.states
                .Select(child => child.state.name)
                .Where(name => name.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               name.IndexOf("Block", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct().Take(8).ToArray();
        if (states.Length == 0) states = new[] { controller.layers[0].stateMachine.defaultState.name };

        bool allBuildsSucceeded = true;
        foreach (string state in states)
        foreach (float time in new[] { 0f, 0.25f, 0.5f, 0.75f })
        foreach (float weight in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            animator.Rebind();
            animator.Update(0f);
            if (!string.IsNullOrEmpty(state))
            {
                animator.Play(state, 0, time);
                animator.Update(0f);
            }
            WeightedTransformArray current = blend.data.sourceObjects;
            current.SetWeight(0, 1f - weight);
            current.SetWeight(1, weight);
            MultiParentConstraintData blendData = blend.data;
            blendData.sourceObjects = current;
            blend.data = blendData;

            // Rebind invalidates transform stream handles. Build only after the
            // animator pose and the source weights have been reset so newly
            // created rigs are tested under the same conditions as runtime.
            builder.Clear();
            bool buildResult = builder.Build();
            allBuildsSucceeded &= buildResult && builder.graph.IsValid();
            animator.Update(0f);
            builder.Evaluate(1f / 60f);

            float rp = Vector3.Distance(rightHand.position, rightTarget.position);
            float lp = Vector3.Distance(leftHand.position, leftTarget.position);
            float rr = Quaternion.Angle(rightHand.rotation, rightTarget.rotation);
            float lr = Quaternion.Angle(leftHand.rotation, leftTarget.rotation);
            float elbowDistance = Vector3.Distance(rightLower.position, leftLower.position);
            bool finite = IsFinite(weapon.position) && IsFinite(weapon.rotation) && IsFinite(rightHand.position) && IsFinite(leftHand.position);
            lines.Add($"pose={state}@{time:F2} blend={weight:F2} finite={finite} gripPos=R{rp:F5}/L{lp:F5} gripRot=R{rr:F2}/L{lr:F2} elbowGap={elbowDistance:F4} weaponPos={weapon.position.ToString("F3")} weaponRot={weapon.eulerAngles.ToString("F2")}");
        }

        builder.Clear();
        lines.Add($"allPoseBuildsSucceeded={allBuildsSucceeded}");
        lines.Add(string.Empty);
    }

    private static T Read<T>(EnemyWorker enemy, string field) where T : UnityEngine.Object
        => new SerializedObject(enemy).FindProperty(field)?.objectReferenceValue as T;

    private static bool ReadBool(EnemyWorker enemy, string field)
        => new SerializedObject(enemy).FindProperty(field)?.boolValue ?? false;

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

    private static string PathOf(Transform value, Transform root)
    {
        if (value == null) return "MISSING";
        string result = value.name;
        for (Transform current = value.parent; current != null && current != root; current = current.parent)
            result = current.name + "/" + result;
        return result;
    }
}
