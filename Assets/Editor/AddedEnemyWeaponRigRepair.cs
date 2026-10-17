using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AddedEnemyWeaponRigRepair
{
    // Rebuilds the authored two-hand weapon rig with bindings owned by the target Animator.
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string RecoveryScenePath = "Assets/Scenes/BrokenEdgeLaunch_Recovered.unity";
    private const string EnemyName = "GanzSe Free Modular Character Original 1_0";
    private const string RequestPath = "Library/RepairAddedEnemyWeaponRig.request";
    private const string ReportPath = "Library/AddedEnemyWeaponRigRepairReport.txt";

    static AddedEnemyWeaponRigRepair()
    {
        EditorApplication.delayCall += ApplyIfRequested;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += ApplyIfRequested;
        };
    }

    private static void ApplyIfRequested()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            Repair();
            File.Delete(RequestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Swordplay/Repair Added Enemy Weapon Blend And Hand IK")]
    public static void Repair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before repairing the authored weapon rig.");

        Scene scene = SceneManager.GetActiveScene();
        EnemyWorker enemy = FindTargetEnemy(scene);
        if (enemy == null && File.Exists(RecoveryScenePath))
        {
            scene = EditorSceneManager.OpenScene(RecoveryScenePath, OpenSceneMode.Single);
            enemy = FindTargetEnemy(scene);
        }
        if (enemy == null)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            enemy = FindTargetEnemy(scene);
        }
        if (enemy == null)
            throw new InvalidOperationException($"Blended added enemy '{EnemyName}' was not found in the active, recovered, or saved scene.");

        GameObject root = enemy.gameObject;
        Animator animator = root.GetComponent<Animator>();
        RigBuilder builder = root.GetComponent<RigBuilder>();
        if (enemy == null || animator == null || !animator.isHuman || builder == null)
            throw new InvalidOperationException($"{EnemyName}: EnemyWorker, Humanoid Animator, or RigBuilder is missing.");

        MultiParentConstraint blend = ReadReference<MultiParentConstraint>(enemy, "weaponBlend")
                                      ?? root.GetComponentsInChildren<MultiParentConstraint>(true)
                                          .FirstOrDefault(value => value.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0);
        TwoBoneIKConstraint rightIk = ReadReference<TwoBoneIKConstraint>(enemy, "rightArmIK");
        TwoBoneIKConstraint leftIk = ReadReference<TwoBoneIKConstraint>(enemy, "leftArmIK");
        if (blend == null || rightIk == null || leftIk == null)
            throw new InvalidOperationException($"{EnemyName}: weapon blend or hand IK reference is missing.");

        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform weaponRoot = blend.data.constrainedObject;
        Transform rightTarget = rightIk.data.target;
        Transform leftTarget = leftIk.data.target;
        if (rightHand == null || leftHand == null || weaponRoot == null || rightTarget == null || leftTarget == null)
            throw new InvalidOperationException($"{EnemyName}: incomplete weapon rig bindings.");

        // Grip markers describe the sword, not the avatar. Copy the known-good marker
        // transforms from Bow's identical weapon hierarchy. Reorienting them to the new
        // avatar's current hand pose twists the hands and destroys the physical grip.
        EnemyWorker bow = FindWorkingBow(scene);
        TwoBoneIKConstraint bowRightIk = ReadReference<TwoBoneIKConstraint>(bow, "rightArmIK");
        TwoBoneIKConstraint bowLeftIk = ReadReference<TwoBoneIKConstraint>(bow, "leftArmIK");
        if (bowRightIk == null || bowLeftIk == null || bowRightIk.data.target == null || bowLeftIk.data.target == null)
            throw new InvalidOperationException("Working Bow hand targets were not found.");
        CopyLocalPose(bowRightIk.data.target, rightTarget);
        CopyLocalPose(bowLeftIk.data.target, leftTarget);

        // Animation Rigging does not reliably bind inactive GameObjects as stream sources.
        // Bow uses active empty sockets, while this enemy was accidentally constrained to
        // the disabled visual weapon clones. Create the same active socket structure here.
        Transform rightSource = EnsureGripSolvedSocket(weaponRoot, rightTarget, rightHand, "Weapon_r Socket");
        Transform leftSource = EnsureGripSolvedSocket(weaponRoot, leftTarget, leftHand, "Weapon_l Socket");

        // Do not retain cloned constraint components. Their serialized references can
        // look correct while their stream bindings still belong to the source avatar.
        // Recreate a minimal rig owned by this Animator, exactly in execution order:
        // weapon follows the hand sockets first, then each hand follows the weapon grip.
        CreateFreshWeaponRig(root, animator, builder, weaponRoot, rightSource, leftSource,
            rightTarget, leftTarget, out blend, out rightIk, out leftIk);
        WriteReference(enemy, "weaponBlend", blend);
        WriteReference(enemy, "rightArmIK", rightIk);
        WriteReference(enemy, "leftArmIK", leftIk);

        // A stale cloned head reference can prevent the entire RigBuilder graph from building,
        // including the otherwise-correct weapon layer. Rebind it to this avatar before rebuilding.
        EditorUtility.SetDirty(blend);
        EditorUtility.SetDirty(rightIk);
        EditorUtility.SetDirty(leftIk);
        EditorUtility.SetDirty(rightTarget);
        EditorUtility.SetDirty(leftTarget);
        EditorUtility.SetDirty(builder);

        builder.Clear();
        builder.Build();

        float rightPositionError = GripPositionError(rightSource, rightTarget, rightHand);
        float leftPositionError = GripPositionError(leftSource, leftTarget, leftHand);
        float rightRotationError = GripRotationError(rightSource, rightTarget, rightHand);
        float leftRotationError = GripRotationError(leftSource, leftTarget, leftHand);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
            EditorSceneManager.SaveScene(scene, ScenePath);
        else
            EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        string[] report =
        {
            $"Scene={scene.path}",
            $"Enemy={root.name}",
            $"Weapon={PathOf(weaponRoot, root.transform)}",
            $"RightSource={PathOf(rightSource, root.transform)}",
            $"LeftSource={PathOf(leftSource, root.transform)}",
            $"MaintainPositionOffset={blend.data.maintainPositionOffset}",
            $"MaintainRotationOffset={blend.data.maintainRotationOffset}",
            $"RightGripPositionError={rightPositionError:F6}",
            $"LeftGripPositionError={leftPositionError:F6}",
            $"RightGripRotationError={rightRotationError:F4}",
            $"LeftGripRotationError={leftRotationError:F4}",
            $"ConstraintOrder={string.Join(" > ", builder.layers.SelectMany(layer => layer.rig.GetComponentsInChildren<MonoBehaviour>(true)).Where(value => value is MultiParentConstraint || value is TwoBoneIKConstraint).Select(value => value.GetType().Name + ":" + value.name))}"
        };
        File.WriteAllLines(ReportPath, report);

        if (blend.data.maintainPositionOffset || blend.data.maintainRotationOffset ||
            rightPositionError > 0.0005f || leftPositionError > 0.0005f ||
            rightRotationError > 0.05f || leftRotationError > 0.05f)
            throw new InvalidOperationException($"{EnemyName}: weapon rig validation failed. See {ReportPath}.");

        Selection.activeGameObject = root;
        Debug.Log($"[Added Enemy Weapon Rig] COMPLETE — Bow grip markers, active hand sockets, and local-only rig bindings. Report: {Path.GetFullPath(ReportPath)}");
    }

    private static void CreateFreshWeaponRig(
        GameObject root, Animator animator, RigBuilder builder, Transform weapon,
        Transform rightSource, Transform leftSource, Transform rightTarget, Transform leftTarget,
        out MultiParentConstraint blend, out TwoBoneIKConstraint rightIk, out TwoBoneIKConstraint leftIk)
    {
        builder.Clear();
        foreach (Rig oldRig in root.GetComponentsInChildren<Rig>(true))
            oldRig.enabled = false;

        Transform previous = root.transform.Find("WeaponHandRig_Rebuilt");
        if (previous != null)
            UnityEngine.Object.DestroyImmediate(previous.gameObject);

        GameObject rigObject = new GameObject("WeaponHandRig_Rebuilt");
        rigObject.transform.SetParent(root.transform, false);
        Rig rig = rigObject.AddComponent<Rig>();
        rig.weight = 1f;

        GameObject blendObject = new GameObject("Weapon Blend");
        blendObject.transform.SetParent(rigObject.transform, false);
        blend = blendObject.AddComponent<MultiParentConstraint>();
        var sources = new WeightedTransformArray(0);
        sources.Add(new WeightedTransform(rightSource, 1f));
        sources.Add(new WeightedTransform(leftSource, 0f));
        MultiParentConstraintData blendData = blend.data;
        blendData.constrainedObject = weapon;
        blendData.sourceObjects = sources;
        blendData.maintainPositionOffset = false;
        blendData.maintainRotationOffset = false;
        blendData.constrainedPositionXAxis = true;
        blendData.constrainedPositionYAxis = true;
        blendData.constrainedPositionZAxis = true;
        blendData.constrainedRotationXAxis = true;
        blendData.constrainedRotationYAxis = true;
        blendData.constrainedRotationZAxis = true;
        blend.data = blendData;
        blend.weight = 1f;

        rightIk = CreateIk(rigObject.transform, "Right Hand IK", animator,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rightTarget);
        leftIk = CreateIk(rigObject.transform, "Left Hand IK", animator,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, leftTarget);

        builder.layers = new List<RigLayer> { new RigLayer(rig, true) };
        builder.enabled = true;
        EditorUtility.SetDirty(builder);
    }

    private static TwoBoneIKConstraint CreateIk(
        Transform rigRoot, string name, Animator animator,
        HumanBodyBones rootBone, HumanBodyBones midBone, HumanBodyBones tipBone, Transform target)
    {
        GameObject ikObject = new GameObject(name);
        ikObject.transform.SetParent(rigRoot, false);
        TwoBoneIKConstraint constraint = ikObject.AddComponent<TwoBoneIKConstraint>();
        TwoBoneIKConstraintData data = constraint.data;
        data.root = animator.GetBoneTransform(rootBone);
        data.mid = animator.GetBoneTransform(midBone);
        data.tip = animator.GetBoneTransform(tipBone);
        data.target = target;
        data.hint = null;
        data.targetPositionWeight = 1f;
        data.targetRotationWeight = 1f;
        data.hintWeight = 0f;
        data.maintainTargetPositionOffset = false;
        data.maintainTargetRotationOffset = false;
        constraint.data = data;
        constraint.weight = 1f;
        return constraint;
    }

    private static void ConfigureIk(TwoBoneIKConstraint constraint, Transform hand, Transform target)
    {
        TwoBoneIKConstraintData data = constraint.data;
        data.tip = hand;
        data.target = target;
        data.targetPositionWeight = 1f;
        data.targetRotationWeight = 1f;
        constraint.data = data;
        constraint.weight = 1f;
    }

    private static void CopyLocalPose(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static Transform EnsureGripSolvedSocket(
        Transform weaponRoot, Transform gripTarget, Transform hand, string name)
    {
        Transform socket = hand.Cast<Transform>().FirstOrDefault(child => child.name == name);
        if (socket == null)
        {
            socket = new GameObject(name).transform;
            socket.SetParent(hand, false);
        }
        socket.gameObject.SetActive(true);

        Matrix4x4 weaponWorld = Matrix4x4.TRS(weaponRoot.position, weaponRoot.rotation, Vector3.one);
        Matrix4x4 gripWorld = Matrix4x4.TRS(gripTarget.position, gripTarget.rotation, Vector3.one);
        Matrix4x4 gripInWeapon = weaponWorld.inverse * gripWorld;
        Matrix4x4 handWorld = Matrix4x4.TRS(hand.position, hand.rotation, Vector3.one);
        Matrix4x4 solvedWeaponWorld = handWorld * gripInWeapon.inverse;
        Matrix4x4 socketLocal = hand.worldToLocalMatrix * solvedWeaponWorld;
        socket.localPosition = socketLocal.GetColumn(3);
        socket.localRotation = socketLocal.rotation;
        socket.localScale = Vector3.one;
        socket.gameObject.layer = hand.gameObject.layer;
        return socket;
    }

    private static float GripPositionError(Transform sourceWeapon, Transform target, Transform hand)
        => Vector3.Distance(sourceWeapon.TransformPoint(target.localPosition), hand.position);

    private static float GripRotationError(Transform sourceWeapon, Transform target, Transform hand)
        => Quaternion.Angle(sourceWeapon.rotation * target.localRotation, hand.rotation);

    private static bool IsChildOrSelf(Transform value, Transform parent)
        => value != null && (value == parent || value.IsChildOf(parent));

    private static EnemyWorker FindTargetEnemy(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        return scene.GetRootGameObjects()
            .SelectMany(value => value.GetComponentsInChildren<EnemyWorker>(true))
            .Where(value => value.name == EnemyName ||
                            (!value.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase) &&
                             !value.name.StartsWith("Knight_BrokenEdge", StringComparison.OrdinalIgnoreCase)))
            .Where(value => ReadReference<MultiParentConstraint>(value, "weaponBlend") != null)
            .OrderByDescending(value => value.name == EnemyName)
            .ThenByDescending(value => value.gameObject.activeInHierarchy)
            .FirstOrDefault();
    }

    private static EnemyWorker FindWorkingBow(Scene scene)
    {
        EnemyWorker bow = scene.GetRootGameObjects()
            .SelectMany(value => value.GetComponentsInChildren<EnemyWorker>(true))
            .FirstOrDefault(value => value.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase)
                                     && ReadReference<MultiParentConstraint>(value, "weaponBlend") != null);
        if (bow == null) throw new InvalidOperationException("Working Bow enemy was not found.");
        return bow;
    }

    private static T ReadReference<T>(EnemyWorker enemy, string propertyName) where T : UnityEngine.Object
    {
        var serialized = new SerializedObject(enemy);
        return serialized.FindProperty(propertyName)?.objectReferenceValue as T;
    }

    private static void WriteReference(EnemyWorker enemy, string propertyName, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(enemy);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"{enemy.name}: serialized field '{propertyName}' is missing.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(enemy);
    }

    private static string PathOf(Transform value, Transform root)
    {
        if (value == null) return "MISSING";
        string result = value.name;
        for (Transform current = value.parent; current != null && current != root; current = current.parent)
            result = current.name + "/" + result;
        return result;
    }
}
