using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FIMSpace.FProceduralAnimation;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;
using XftWeapon;

[InitializeOnLoad]
public static class KnightEnemyInstaller
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string KnightModelPath = "Assets/Knight with Sword/Meshes/Knight.fbx";
    private const string KnightPrefix = "Knight_BrokenEdge_Blue_Showcase";
    private const string ReportPath = "Library/KnightEnemyInstallReport.txt";
    // A request file lets a calibration queued during Play Mode run safely after returning to Edit Mode.
    private const string CalibrationRequestPath = "Library/ApplyKnightWeaponCalibration.request";
    private const string AddedEnemyName = "GanzSe Free Modular Character Original 1_0";
    private const string AddedEnemyReportPath = "Library/AddedEnemyInstallReport.txt";
    private const string ActiveBowRocketRequestPath = "Library/ApplyActiveBowRocket.request";
    private const string ActiveBowRocketReportPath = "Library/ActiveBowRocketReport.txt";

    static KnightEnemyInstaller()
    {
        EditorApplication.delayCall += TryApplyRequestedCalibration;
        EditorApplication.delayCall += TryApplyActiveBowRocketRequest;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryApplyRequestedCalibration;
        };
    }

    private static void TryApplyActiveBowRocketRequest()
    {
        if (!File.Exists(ActiveBowRocketRequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            InstallRocketOnActiveBowEnemy();
            File.Delete(ActiveBowRocketRequestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Swordplay/Add Rocket VFX To Active Bow Enemy")]
    public static void InstallRocketOnActiveBowEnemy()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        List<EnemyWorker> activeBows = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .Where(enemy => enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase))
            .Where(enemy => enemy.gameObject.activeInHierarchy)
            .ToList();
        if (activeBows.Count != 1)
            throw new InvalidOperationException($"Expected exactly one active BOW enemy, found {activeBows.Count}.");

        EnemyWorker bow = activeBows[0];
        Animator animator = bow.GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
            throw new InvalidOperationException($"{bow.name}: valid Humanoid Animator is missing.");

        Transform sword = ReadObject<Transform>(bow, "sword");
        Transform weaponRoot = sword != null && sword.parent != null ? sword.parent : sword;
        Color effectColor = ChooseContrastingEffectColor(bow.gameObject, weaponRoot);
        Material flameMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/VFX/M_KnightRocketFlame.mat");
        if (flameMaterial == null)
            throw new InvalidOperationException("Knight rocket flame material is missing.");

        KnightRocketLegVfx rocket = bow.GetComponent<KnightRocketLegVfx>() ?? bow.gameObject.AddComponent<KnightRocketLegVfx>();
        rocket.Configure(animator, flameMaterial, effectColor);
        EditorUtility.SetDirty(rocket);
        EditorUtility.SetDirty(bow.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        ParticleSystem[] emitters = rocket.GetComponentsInChildren<ParticleSystem>(true);
        string[] report =
        {
            $"Scene={scene.path}",
            $"Enemy={bow.name}",
            $"Active={bow.gameObject.activeInHierarchy}",
            $"RocketComponent={rocket != null}",
            $"UpperLegBones={leftLeg != null && rightLeg != null}",
            $"Emitters={emitters.Length}",
            $"EffectColor={ColorUtility.ToHtmlStringRGB(effectColor)}"
        };
        File.WriteAllLines(ActiveBowRocketReportPath, report);
        if (leftLeg == null || rightLeg == null || emitters.Length != 1)
            throw new InvalidOperationException($"{bow.name}: rocket VFX validation failed. See {ActiveBowRocketReportPath}.");

        Selection.activeGameObject = bow.gameObject;
        Debug.Log($"[Active BOW Rocket] COMPLETE — {bow.name}, emitters={emitters.Length}, color=#{ColorUtility.ToHtmlStringRGB(effectColor)}");
    }

    private static void TryApplyRequestedCalibration()
    {
        if (!File.Exists(CalibrationRequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            Install();
            Validate();
            File.Delete(CalibrationRequestPath);
            Debug.Log("[Knight Enemy Installer] Requested weapon grip/VFX calibration applied to all showcase knights.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Swordplay/Convert Showcase Knights To Enemies")]
    public static void Install()
    {
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            EnemyWorker[] oldEnemies = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
                .ToArray();
            EnemyWorker[] previousEnemies = oldEnemies
                .Where(enemy => !enemy.name.StartsWith(KnightPrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            EnemyWorker template = oldEnemies.FirstOrDefault(enemy =>
                enemy.gameObject.activeInHierarchy && enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase));
            if (template == null)
                template = oldEnemies.FirstOrDefault(enemy => enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase));
            if (template == null)
                throw new InvalidOperationException("WM_Master_Unity_Bow2 reference enemy was not found.");

            Transform sourceSword = ReadObject<Transform>(template, "sword");
            if (sourceSword == null)
                throw new InvalidOperationException("The reference enemy has no serialized sword reference.");
            Transform sourceWeaponRoot = sourceSword.parent != null ? sourceSword.parent : sourceSword;
            Transform sourceRightSocket = FindDeep(template.transform, "Weapon_r Socket");
            Transform sourceLeftSocket = FindDeep(template.transform, "Weapon_l Socket");
            Transform sourceRigLayer = FindDirect(template.transform, "RigLayer");
            Transform sourceHitter = FindDirect(template.transform, "Hitter");
            if (sourceRigLayer == null || sourceHitter == null || sourceRightSocket == null || sourceLeftSocket == null)
                throw new InvalidOperationException("The reference enemy is missing RigLayer, Hitter, or hand weapon sockets.");

            RuntimeAnimatorController controller = template.GetComponent<Animator>()?.runtimeAnimatorController;
            Avatar knightAvatar = AssetDatabase.LoadAllAssetsAtPath(KnightModelPath)
                .OfType<Avatar>().FirstOrDefault(avatar => avatar != null && avatar.isHuman);
            if (controller == null || knightAvatar == null)
                throw new InvalidOperationException("Enemy controller or Knight humanoid Avatar is missing.");

            List<GameObject> knights = FindKnightRoots(scene);
            if (knights.Count == 0)
                throw new InvalidOperationException("No showcase knight roots were found.");

            // A user-placed knight was nested below another knight.  Each enemy must be a scene root
            // so its Animator, navigation and movement controller own an independent coordinate space.
            foreach (GameObject knight in knights)
                if (knight.transform.parent != null)
                    knight.transform.SetParent(null, true);

            var installed = new List<GameObject>();
            foreach (GameObject placedKnight in knights)
            {
                GameObject knight = EnsureGameplayRoot(placedKnight);
                InstallKnight(knight, template, controller, knightAvatar, sourceWeaponRoot, sourceSword,
                    sourceRightSocket, sourceLeftSocket, sourceRigLayer, sourceHitter);
                installed.Add(knight);
            }

            foreach (EnemyWorker oldEnemy in previousEnemies)
                if (oldEnemy != null)
                    oldEnemy.gameObject.SetActive(false);

            foreach (GameObject knight in installed)
                knight.SetActive(true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            WriteValidationReport(scene, installed, previousEnemies);
            Selection.objects = installed.Cast<UnityEngine.Object>().ToArray();
            Debug.Log($"[Knight Enemy Installer] KNIGHT_ENEMY_INSTALL_COMPLETE — installed {installed.Count}, disabled {previousEnemies.Length} previous enemies. Report: {Path.GetFullPath(ReportPath)}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
    }

    [MenuItem("Tools/Swordplay/Validate Showcase Knight Enemies")]
    public static void Validate()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        List<GameObject> installed = scene.GetRootGameObjects()
            .Where(root => root.name.StartsWith(KnightPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(root => root.GetComponent<EnemyWorker>() != null)
            .OrderBy(root => root.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        EnemyWorker[] previousEnemies = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .Where(enemy => !enemy.name.StartsWith(KnightPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        WriteValidationReport(scene, installed, previousEnemies);
        if (installed.Count != 7)
            throw new InvalidOperationException($"Expected 7 installed knight enemies, found {installed.Count}.");
        if (previousEnemies.Any(enemy => enemy.gameObject.activeSelf))
            throw new InvalidOperationException("At least one previous enemy is still active.");
        Debug.Log($"[Knight Enemy Installer] VALIDATION_COMPLETE — {installed.Count} active knight enemies, all {previousEnemies.Length} previous enemies disabled.");
    }

    [MenuItem("Tools/Swordplay/Complete Newly Added Enemy")]
    public static void InstallNewlyAddedEnemy()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject enemyRoot = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == AddedEnemyName);
        if (enemyRoot == null)
            throw new InvalidOperationException($"Scene root '{AddedEnemyName}' was not found.");

        EnemyWorker template = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true))
            .FirstOrDefault(value => value.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase));
        if (template == null)
            throw new InvalidOperationException("WM_Master_Unity_Bow2 reference enemy was not found.");

        Animator animator = enemyRoot.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException($"{AddedEnemyName} does not have a valid Humanoid Animator.");

        Transform rightHand = RequiredBone(animator, HumanBodyBones.RightHand);
        Transform leftHand = RequiredBone(animator, HumanBodyBones.LeftHand);
        Transform head = RequiredBone(animator, HumanBodyBones.Head);
        Transform visibleWeaponRoot = enemyRoot.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(value => value.name == "Weapon_r" &&
                                     !value.IsChildOf(rightHand) && !value.IsChildOf(leftHand) &&
                                     FindDeep(value, "rh_target") != null && FindDeep(value, "lh_target") != null);
        if (visibleWeaponRoot == null)
            throw new InvalidOperationException($"{AddedEnemyName}: authored blend Weapon_r with rh_target/lh_target was not found.");

        // This one-off scene character was authored before the installer.  A previous validation
        // evaluation may have left the constrained result serialized, so restore the inspected
        // edit-time values before treating them as the source of truth.
        visibleWeaponRoot.localPosition = new Vector3(0.94f, 0.06f, -0.01f);
        visibleWeaponRoot.localRotation = Quaternion.Euler(272.59f, 184.72f, 155.81f);
        visibleWeaponRoot.localScale = Vector3.one;
        Vector3 authoredPosition = visibleWeaponRoot.localPosition;
        Quaternion authoredRotation = visibleWeaponRoot.localRotation;
        Vector3 authoredScale = visibleWeaponRoot.localScale;
        Transform rightTarget = FindDeep(visibleWeaponRoot, "rh_target");
        Transform leftTarget = FindDeep(visibleWeaponRoot, "lh_target");
        Transform visibleSword = visibleWeaponRoot.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            .OrderByDescending(renderer => renderer.name == "SM_Katana01")
            .Select(renderer => renderer.transform)
            .FirstOrDefault();
        if (visibleSword == null)
            throw new InvalidOperationException($"{AddedEnemyName}: no rendered sword was found below the authored blend weapon.");

        foreach (string childName in new[] { "RigLayer", "Hitter", "Crosshead", "Enemy_Crosshead" })
        {
            Transform old = FindDirect(enemyRoot.transform, childName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        SetLayerRecursively(enemyRoot, template.gameObject.layer);
        enemyRoot.tag = template.gameObject.tag;
        animator.runtimeAnimatorController = template.GetComponent<Animator>().runtimeAnimatorController;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        ApplyAddedEnemyIdlePose(animator);

        // The user already authored this transform. Installation may solve sockets, but never moves this weapon.
        visibleWeaponRoot.localPosition = authoredPosition;
        visibleWeaponRoot.localRotation = authoredRotation;
        visibleWeaponRoot.localScale = authoredScale;
        RemoveWeaponPhysics(visibleWeaponRoot);
        SetLayerRecursively(visibleWeaponRoot.gameObject, 8);
        DisableLegacyWeaponTrails(visibleWeaponRoot);

        Transform rightSocket = FindDeep(rightHand, "Weapon_r Socket") ?? new GameObject("Weapon_r Socket").transform;
        Transform leftSocket = FindDeep(leftHand, "Weapon_l Socket") ?? new GameObject("Weapon_l Socket").transform;
        SolveExistingWeaponSocket(visibleWeaponRoot, rightTarget, rightHand, rightSocket);
        SolveExistingWeaponSocket(visibleWeaponRoot, leftTarget, leftHand, leftSocket);
        DisableHandWeaponVisuals(rightHand, rightSocket);
        DisableHandWeaponVisuals(leftHand, leftSocket);

        Transform crosshead = new GameObject("Crosshead").transform;
        crosshead.SetParent(enemyRoot.transform, false);
        crosshead.position = head.position + enemyRoot.transform.forward * 5f;

        Transform sourceRigLayer = FindDirect(template.transform, "RigLayer");
        Transform sourceHitter = FindDirect(template.transform, "Hitter");
        if (sourceRigLayer == null || sourceHitter == null)
            throw new InvalidOperationException("BOW reference is missing RigLayer or Hitter.");
        Transform rigLayer = CloneHierarchy(sourceRigLayer.gameObject, enemyRoot.transform, "RigLayer").transform;
        rigLayer.localPosition = Vector3.zero;
        rigLayer.localRotation = Quaternion.identity;
        rigLayer.localScale = Vector3.one;
        Rig rig = rigLayer.GetComponent<Rig>() ?? rigLayer.GetComponentInChildren<Rig>(true) ?? rigLayer.gameObject.AddComponent<Rig>();

        MultiParentConstraint weaponBlend = null;
        MultiAimConstraint headAim = null;
        TwoBoneIKConstraint rightIk = null;
        TwoBoneIKConstraint leftIk = null;
        foreach (MultiParentConstraint constraint in rigLayer.GetComponentsInChildren<MultiParentConstraint>(true))
        {
            MultiParentConstraintData data = constraint.data;
            if (constraint.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                data.constrainedObject = visibleWeaponRoot;
                var sources = new WeightedTransformArray(0);
                sources.Add(new WeightedTransform(rightSocket, 1f));
                sources.Add(new WeightedTransform(leftSocket, 1f));
                data.sourceObjects = sources;
                data.maintainPositionOffset = false;
                data.maintainRotationOffset = false;
                constraint.data = data;
                constraint.weight = 1f;
                weaponBlend = constraint;
            }
            else
            {
                data.constrainedObject = head;
                var sources = new WeightedTransformArray(0);
                sources.Add(new WeightedTransform(head.parent != null ? head.parent : enemyRoot.transform, 1f));
                data.sourceObjects = sources;
                constraint.data = data;
                constraint.weight = 0f;
            }
        }
        foreach (TwoBoneIKConstraint constraint in rigLayer.GetComponentsInChildren<TwoBoneIKConstraint>(true))
        {
            bool left = constraint.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        constraint.name.StartsWith("L", StringComparison.OrdinalIgnoreCase);
            TwoBoneIKConstraintData data = constraint.data;
            data.root = RequiredBone(animator, left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            data.mid = RequiredBone(animator, left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            data.tip = left ? leftHand : rightHand;
            data.target = left ? leftTarget : rightTarget;
            data.hint = null;
            data.hintWeight = 0f;
            data.targetPositionWeight = 1f;
            data.targetRotationWeight = 1f;
            constraint.data = data;
            constraint.weight = 1f;
            if (left) leftIk = constraint; else rightIk = constraint;
        }
        foreach (MultiAimConstraint constraint in rigLayer.GetComponentsInChildren<MultiAimConstraint>(true))
        {
            MultiAimConstraintData data = constraint.data;
            data.constrainedObject = head;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(crosshead, 1f));
            data.sourceObjects = sources;
            data.aimAxis = AddedClosestAxis(head.InverseTransformDirection(enemyRoot.transform.forward));
            data.upAxis = AddedClosestAxis(head.InverseTransformDirection(enemyRoot.transform.up), data.aimAxis);
            data.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;
            data.worldUpObject = null;
            data.maintainOffset = false;
            data.offset = Vector3.zero;
            data.limits = new Vector2(-85f, 85f);
            constraint.data = data;
            constraint.weight = 1f;
            headAim = constraint;
        }
        if (weaponBlend == null || rightIk == null || leftIk == null || headAim == null)
            throw new InvalidOperationException($"{AddedEnemyName}: BOW rig clone did not contain every required constraint.");

        RigBuilder rigBuilder = GetOrAdd<RigBuilder>(enemyRoot);
        rigBuilder.layers = new List<RigLayer> { new RigLayer(rig, true) };
        rigBuilder.enabled = true;

        Transform hitter = CloneHierarchy(sourceHitter.gameObject, enemyRoot.transform, "Hitter").transform;
        hitter.position = visibleSword.position;
        hitter.rotation = visibleSword.rotation;
        SetLayerRecursively(hitter.gameObject, 8);
        TargetMatching matching = hitter.GetComponent<TargetMatching>() ?? hitter.gameObject.AddComponent<TargetMatching>();
        matching.Target = visibleSword;
        foreach (Collider collider in hitter.GetComponentsInChildren<Collider>(true))
        {
            collider.isTrigger = true;
            EnableProvidesContacts(collider);
        }
        DisableLegacyWeaponTrails(hitter);
        foreach (BrokenEdgeBladeVfx stale in hitter.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
            UnityEngine.Object.DestroyImmediate(stale);
        Rigidbody hitterBody = hitter.GetComponent<Rigidbody>() ?? hitter.gameObject.AddComponent<Rigidbody>();
        hitterBody.useGravity = false;
        hitterBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        hitterBody.interpolation = RigidbodyInterpolation.Interpolate;

        Bounds bounds = CalculateOwnBounds(enemyRoot.transform, visibleWeaponRoot, hitter);
        float height = Mathf.Max(1.2f, bounds.size.y);
        float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.42f, 0.22f, 0.55f);
        Vector3 center = enemyRoot.transform.InverseTransformPoint(bounds.center);
        CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(enemyRoot);
        capsule.direction = 1;
        capsule.center = center;
        capsule.height = height;
        capsule.radius = Mathf.Min(radius, height * 0.45f);
        capsule.isTrigger = true;
        EnableProvidesContacts(capsule);
        CharacterController characterController = GetOrAdd<CharacterController>(enemyRoot);
        characterController.center = center;
        characterController.height = height;
        characterController.radius = capsule.radius;
        characterController.slopeLimit = 55f;
        characterController.stepOffset = Mathf.Min(0.5f, Mathf.Max(0f, height - capsule.radius * 2f));
        characterController.skinWidth = Mathf.Max(0.035f, capsule.radius * 0.12f);
        characterController.minMoveDistance = 0f;
        characterController.enableOverlapRecovery = true;

        NavMeshAgent agent = CopyOrAdd(template.GetComponent<NavMeshAgent>(), enemyRoot);
        agent.radius = characterController.radius;
        agent.height = characterController.height;
        agent.baseOffset = Mathf.Max(0f, center.y - height * 0.5f);
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = true;
        agent.autoRepath = true;
        Rigidbody body = CopyOrAdd(template.GetComponent<Rigidbody>(), enemyRoot);
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = false;
        body.constraints = RigidbodyConstraints.FreezeAll;
        CopyOrAdd(template.GetComponent<AudioSource>(), enemyRoot);
        LegsAnimator legs = enemyRoot.GetComponent<LegsAnimator>() ?? enemyRoot.AddComponent<LegsAnimator>();
        ConfigureLegsAnimator(legs, animator, enemyRoot.transform);
        legs.enabled = false;

        EnemyWorker enemy = enemyRoot.GetComponent<EnemyWorker>() ?? enemyRoot.AddComponent<EnemyWorker>();
        EditorUtility.CopySerialized(template, enemy);
        WriteObject(enemy, "crosshead", crosshead);
        WriteObject(enemy, "sword", visibleSword);
        WriteObject(enemy, "ma", headAim);
        WriteObject(enemy, "weaponBlend", weaponBlend);
        WriteObject(enemy, "rightArmIK", rightIk);
        WriteObject(enemy, "leftArmIK", leftIk);
        WriteBool(enemy, "miiMotion", false);
        WriteVector3(enemy, "swordOffset", visibleSword.localPosition);
        WriteVector3(enemy, "headRotOffset", CalculateAddedHeadOffset(head, enemyRoot.transform));
        WriteBool(enemy, "headOffsetIsRootLocal", true);
        SwordHitbox.Configure(hitter.gameObject, enemy);

        foreach (BrokenEdgeBladeVfx stale in visibleWeaponRoot.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
            UnityEngine.Object.DestroyImmediate(stale);
        BrokenEdgeBladeVfx bladeVfx = visibleSword.gameObject.AddComponent<BrokenEdgeBladeVfx>();
        bladeVfx.Configure(enemyRoot.transform, visibleSword);
        Color effectColor = ChooseContrastingEffectColor(enemyRoot, visibleWeaponRoot);
        SetBladePalette(bladeVfx, effectColor);
        bladeVfx.EnsureBladeOnlyMarkers();
        Material flameMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/VFX/M_KnightRocketFlame.mat");
        KnightRocketLegVfx rocket = enemyRoot.GetComponent<KnightRocketLegVfx>() ?? enemyRoot.AddComponent<KnightRocketLegVfx>();
        rocket.Configure(animator, flameMaterial, effectColor);

        rigBuilder.Build();
        rigBuilder.Evaluate(0f);
        // Evaluate verifies that the graph has no invalid handles/NaNs.  Its constrained result is
        // runtime state, not the user's authored scene pose, so never serialize that result.
        visibleWeaponRoot.localPosition = authoredPosition;
        visibleWeaponRoot.localRotation = authoredRotation;
        visibleWeaponRoot.localScale = authoredScale;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        WriteAddedEnemyReport(enemyRoot, animator, visibleWeaponRoot, visibleSword, rightSocket, leftSocket,
            rightTarget, leftTarget, rightHand, leftHand, weaponBlend, rightIk, leftIk, matching, bladeVfx, rocket,
            authoredPosition, authoredRotation, authoredScale, effectColor);
        Selection.activeGameObject = enemyRoot;
        Debug.Log($"[Added Enemy Installer] COMPLETE — {AddedEnemyName}. Report: {Path.GetFullPath(AddedEnemyReportPath)}");
    }

    private static List<GameObject> FindKnightRoots(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(transform => transform.name.StartsWith(KnightPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(transform => transform.Find("RIG_GRP") != null || transform.Find("Knight Visual/RIG_GRP") != null)
            .Select(transform => transform.gameObject)
            .Distinct()
            .OrderBy(gameObject => gameObject.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GameObject EnsureGameplayRoot(GameObject placedKnight)
    {
        Transform nestedVisual = placedKnight.transform.Find("Knight Visual");
        if (nestedVisual != null && nestedVisual.Find("RIG_GRP") != null)
        {
            // Older installs wrapped the imported model in an extra gameplay root.  A
            // Humanoid Animator treats the imported model as its avatar hierarchy root,
            // so a sibling RigLayer cannot be resolved into the animation stream.  That
            // leaves invalid PropertyStreamHandles and turns Weapon_Blend into NaN on
            // the first evaluated frame.  Flatten it back to the same layout as Bow.
            string originalName = placedKnight.name;
            bool wasActive = placedKnight.activeSelf;
            nestedVisual.SetParent(null, true);
            nestedVisual.name = originalName;
            nestedVisual.gameObject.SetActive(wasActive);
            UnityEngine.Object.DestroyImmediate(placedKnight);
            return nestedVisual.gameObject;
        }

        // A model that already owns RIG_GRP is itself the correct Animator root.  Do
        // not introduce a wrapper merely because it does not have an Animator yet.
        return placedKnight;
    }

    private static void InstallKnight(
        GameObject knight,
        EnemyWorker template,
        RuntimeAnimatorController controller,
        Avatar knightAvatar,
        Transform sourceWeaponRoot,
        Transform sourceSword,
        Transform sourceRightSocket,
        Transform sourceLeftSocket,
        Transform sourceRigLayer,
        Transform sourceHitter)
    {
        RemovePreviousInstallation(knight.transform);
        SetLayerRecursively(knight, 7);
        knight.tag = template.gameObject.tag;

        Animator animator = GetOrAdd<Animator>(knight);
        animator.avatar = knightAvatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();

        Transform rightHand = RequiredBone(animator, HumanBodyBones.RightHand);
        Transform leftHand = RequiredBone(animator, HumanBodyBones.LeftHand);
        Transform head = RequiredBone(animator, HumanBodyBones.Head);
        Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                               ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                               ?? RequiredBone(animator, HumanBodyBones.Spine);

        Material characterMaterial = FindCharacterMaterial(knight);

        Transform visibleWeaponRoot = CloneHierarchy(sourceWeaponRoot.gameObject, upperChest, "Weapon_Blend").transform;
        visibleWeaponRoot.position = rightHand.position;
        visibleWeaponRoot.rotation = rightHand.rotation;
        visibleWeaponRoot.localScale = DivideScale(sourceWeaponRoot.lossyScale, upperChest.lossyScale);
        Transform visibleSword = FindDeep(visibleWeaponRoot, sourceSword.name) ?? visibleWeaponRoot;
        RemoveWeaponPhysics(visibleWeaponRoot);
        ApplyMaterial(visibleWeaponRoot, characterMaterial);
        SetLayerRecursively(visibleWeaponRoot.gameObject, 8);

        CreateInactiveHandWeapon(sourceWeaponRoot.gameObject, rightHand, "RightHand Weapon (Inactive)", characterMaterial);
        CreateInactiveHandWeapon(sourceWeaponRoot.gameObject, leftHand, "LeftHand Weapon (Inactive)", characterMaterial);

        // Match the working Bow enemy: the weapon constraint reads dedicated socket
        // transforms, never the hand bones that the following TwoBoneIK constraints write.
        // Reading the IK tips directly creates a cyclic rig dependency and can poison the
        // constrained weapon transform with NaN values during graph evaluation.
        Transform rightTarget = FindDeep(visibleWeaponRoot, "rh_target");
        Transform leftTarget = FindDeep(visibleWeaponRoot, "lh_target");
        if (rightTarget == null) rightTarget = CreateTarget(visibleWeaponRoot, "rh_target", rightHand);
        if (leftTarget == null) leftTarget = CreateTarget(visibleWeaponRoot, "lh_target", leftHand);

        // Do not copy the Bow socket's local numbers onto a different avatar.  A
        // humanoid retargets the pose, but each imported skeleton keeps its own hand
        // bone axes.  Instead derive the source pose from the inverse of the matching
        // grip target.  When the weapon root is constrained to this child of the hand,
        // rh_target/lh_target lands exactly on that hand in either blend extreme.
        // Solve each socket from the actual grip marker carried by the copied Bow
        // weapon.  This is independent of avatar hand-bone axes: at a blend extreme,
        // weaponRoot * gripMarker is exactly the corresponding Knight hand transform.
        Transform rightWeaponSocket = CreateGripSolvedWeaponSocket(
            visibleWeaponRoot, rightTarget, rightHand, "Weapon_r Socket");
        Transform leftWeaponSocket = CreateGripSolvedWeaponSocket(
            visibleWeaponRoot, leftTarget, leftHand, "Weapon_l Socket");

        Transform crosshead = new GameObject("Crosshead").transform;
        crosshead.SetParent(knight.transform, false);
        crosshead.position = head.position + knight.transform.forward * 5f;

        Transform rigLayer = CloneHierarchy(sourceRigLayer.gameObject, knight.transform, "RigLayer").transform;
        rigLayer.localPosition = Vector3.zero;
        rigLayer.localRotation = Quaternion.identity;
        rigLayer.localScale = Vector3.one;
        Rig rig = rigLayer.GetComponent<Rig>() ?? rigLayer.GetComponentInChildren<Rig>(true);
        if (rig == null) rig = rigLayer.gameObject.AddComponent<Rig>();

        MultiParentConstraint weaponBlend = null;
        MultiAimConstraint headAim = null;
        TwoBoneIKConstraint rightIk = null;
        TwoBoneIKConstraint leftIk = null;
        foreach (MultiParentConstraint constraint in rigLayer.GetComponentsInChildren<MultiParentConstraint>(true))
        {
            MultiParentConstraintData data = constraint.data;
            if (constraint.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                data.constrainedObject = visibleWeaponRoot;
                var sources = new WeightedTransformArray(0);
                sources.Add(new WeightedTransform(rightWeaponSocket, 1f));
                sources.Add(new WeightedTransform(leftWeaponSocket, 1f));
                data.sourceObjects = sources;
                data.maintainPositionOffset = false;
                data.maintainRotationOffset = false;
                constraint.data = data;
                constraint.weight = 1f;
                weaponBlend = constraint;
            }
            else
            {
                data.constrainedObject = head;
                var sources = new WeightedTransformArray(0);
                sources.Add(new WeightedTransform(head.parent != null ? head.parent : knight.transform, 1f));
                data.sourceObjects = sources;
                constraint.data = data;
                // Keep the component valid for EnemyWorker's rebinding, but do not pin the animated head.
                constraint.weight = 0f;
            }
        }

        foreach (TwoBoneIKConstraint constraint in rigLayer.GetComponentsInChildren<TwoBoneIKConstraint>(true))
        {
            bool isLeft = constraint.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0
                          || constraint.name.StartsWith("L", StringComparison.OrdinalIgnoreCase);
            TwoBoneIKConstraintData data = constraint.data;
            data.root = RequiredBone(animator, isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            data.mid = RequiredBone(animator, isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            data.tip = isLeft ? leftHand : rightHand;
            data.target = isLeft ? leftTarget : rightTarget;
            data.hint = null;
            data.hintWeight = 0f;
            data.targetPositionWeight = 1f;
            data.targetRotationWeight = 1f;
            constraint.data = data;
            constraint.weight = 1f;
            if (isLeft) leftIk = constraint; else rightIk = constraint;
        }

        foreach (MultiAimConstraint constraint in rigLayer.GetComponentsInChildren<MultiAimConstraint>(true))
        {
            MultiAimConstraintData data = constraint.data;
            data.constrainedObject = head;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(crosshead, 1f));
            data.sourceObjects = sources;
            constraint.data = data;
            headAim = constraint;
        }

        if (weaponBlend == null || rightIk == null || leftIk == null || headAim == null)
            throw new InvalidOperationException($"{knight.name}: cloned rig is missing weapon, hand IK, or head aim constraints.");

        RigBuilder rigBuilder = GetOrAdd<RigBuilder>(knight);
        rigBuilder.layers = new List<RigLayer> { new RigLayer(rig, true) };
        rigBuilder.enabled = true;

        Transform hitter = CloneHierarchy(sourceHitter.gameObject, knight.transform, "Hitter").transform;
        hitter.position = visibleSword.position;
        hitter.rotation = visibleSword.rotation;
        SetLayerRecursively(hitter.gameObject, 8);
        TargetMatching matching = hitter.GetComponent<TargetMatching>() ?? hitter.gameObject.AddComponent<TargetMatching>();
        matching.Target = visibleSword;
        foreach (Collider collider in hitter.GetComponentsInChildren<Collider>(true))
        {
            collider.isTrigger = true;
            EnableProvidesContacts(collider);
        }
        foreach (XWeaponTrail legacyTrail in hitter.GetComponentsInChildren<XWeaponTrail>(true))
            legacyTrail.enabled = false;
        foreach (BrokenEdgeBladeVfx staleVfx in hitter.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
            UnityEngine.Object.DestroyImmediate(staleVfx);

        Rigidbody hitterBody = hitter.GetComponent<Rigidbody>() ?? hitter.gameObject.AddComponent<Rigidbody>();
        hitterBody.useGravity = false;
        hitterBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        hitterBody.interpolation = RigidbodyInterpolation.Interpolate;

        Rigidbody rootBody = CopyOrAdd(template.GetComponent<Rigidbody>(), knight);
        rootBody.isKinematic = true;
        rootBody.useGravity = false;
        rootBody.detectCollisions = false;
        rootBody.constraints = RigidbodyConstraints.FreezeAll;

        Bounds bounds = CalculateOwnBounds(knight.transform, visibleWeaponRoot, hitter);
        float height = Mathf.Max(1.2f, bounds.size.y);
        float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.45f, 0.22f, 0.55f);
        Vector3 localCenter = knight.transform.InverseTransformPoint(bounds.center);

        CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(knight);
        capsule.direction = 1;
        capsule.center = localCenter;
        capsule.height = height;
        capsule.radius = Mathf.Min(radius, height * 0.45f);
        capsule.isTrigger = true;
        EnableProvidesContacts(capsule);

        CharacterController controllerComponent = GetOrAdd<CharacterController>(knight);
        controllerComponent.center = localCenter;
        controllerComponent.height = height;
        controllerComponent.radius = Mathf.Min(radius, height * 0.45f);
        controllerComponent.slopeLimit = 55f;
        controllerComponent.stepOffset = Mathf.Min(0.5f, height * 0.25f);
        controllerComponent.skinWidth = Mathf.Max(0.035f, radius * 0.12f);
        controllerComponent.minMoveDistance = 0f;

        NavMeshAgent agent = CopyOrAdd(template.GetComponent<NavMeshAgent>(), knight);
        agent.radius = controllerComponent.radius;
        agent.height = controllerComponent.height;
        agent.baseOffset = Mathf.Max(0f, localCenter.y - height * 0.5f);
        agent.updatePosition = false;
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = true;

        CopyOrAdd(template.GetComponent<AudioSource>(), knight);
        LegsAnimator legs = knight.GetComponent<LegsAnimator>() ?? knight.AddComponent<LegsAnimator>();
        ConfigureLegsAnimator(legs, animator, knight.transform);

        EnemyWorker enemy = knight.GetComponent<EnemyWorker>() ?? knight.AddComponent<EnemyWorker>();
        EditorUtility.CopySerialized(template, enemy);
        WriteObject(enemy, "crosshead", crosshead);
        WriteObject(enemy, "sword", visibleSword);
        WriteObject(enemy, "ma", headAim);
        WriteObject(enemy, "weaponBlend", weaponBlend);
        WriteObject(enemy, "rightArmIK", rightIk);
        WriteObject(enemy, "leftArmIK", leftIk);
        WriteBool(enemy, "miiMotion", false);
        WriteVector3(enemy, "swordOffset", visibleSword.localPosition);

        SwordHitbox.Configure(hitter.gameObject, enemy);
        foreach (BrokenEdgeBladeVfx staleVfx in visibleWeaponRoot.GetComponentsInChildren<BrokenEdgeBladeVfx>(true))
            UnityEngine.Object.DestroyImmediate(staleVfx);
        BrokenEdgeBladeVfx bladeVfx = visibleSword.gameObject.AddComponent<BrokenEdgeBladeVfx>();
        bladeVfx.enabled = true;
        // Hitter is only the moving trigger proxy.  Bind the visual effect explicitly
        // to the rendered sword so renderer auto-discovery can never select a collider,
        // legacy trail or another renderer from the cloned Bow Hitter hierarchy.
        bladeVfx.Configure(knight.transform, visibleSword);
        SetBladePalette(bladeVfx, ExtractPaletteColor(characterMaterial));
        bladeVfx.EnsureBladeOnlyMarkers();
        EditorUtility.SetDirty(matching);
        EditorUtility.SetDirty(bladeVfx);

        try { rigBuilder.Build(); }
        catch (Exception exception) { Debug.LogWarning($"{knight.name}: RigBuilder deferred build: {exception.Message}"); }

        EditorUtility.SetDirty(knight);
        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(enemy);
        EditorUtility.SetDirty(rigBuilder);
    }

    private static void SolveExistingWeaponSocket(Transform weaponRoot, Transform gripTarget, Transform hand, Transform socket)
    {
        socket.SetParent(hand, false);
        Matrix4x4 weaponWorld = Matrix4x4.TRS(weaponRoot.position, weaponRoot.rotation, Vector3.one);
        Matrix4x4 gripWorld = Matrix4x4.TRS(gripTarget.position, gripTarget.rotation, Vector3.one);
        Matrix4x4 gripInWeapon = weaponWorld.inverse * gripWorld;
        Matrix4x4 handWorld = Matrix4x4.TRS(hand.position, hand.rotation, Vector3.one);
        Matrix4x4 socketLocal = hand.worldToLocalMatrix * (handWorld * gripInWeapon.inverse);
        socket.localPosition = socketLocal.GetColumn(3);
        socket.localRotation = socketLocal.rotation;
        socket.localScale = Vector3.one;
        socket.gameObject.layer = hand.gameObject.layer;
    }

    private static void DisableHandWeaponVisuals(Transform hand, Transform socket)
    {
        foreach (Transform child in hand.Cast<Transform>())
        {
            if (child == socket) continue;
            if (child.GetComponentsInChildren<Renderer>(true).Length > 0 &&
                child.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                child.gameObject.SetActive(false);
        }
    }

    private static void DisableLegacyWeaponTrails(Transform root)
    {
        foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true)) trail.enabled = false;
        foreach (XWeaponTrail trail in root.GetComponentsInChildren<XWeaponTrail>(true)) trail.enabled = false;
    }

    private static void ApplyAddedEnemyIdlePose(Animator animator)
    {
        if (Application.isPlaying) return;
        animator.Rebind();
        animator.Update(0f);
        for (int layer = 0; layer < animator.layerCount; layer++) animator.Play("Idle", layer, 0f);
        animator.Update(1f / 60f);
    }

    private static Vector3 CalculateAddedHeadOffset(Transform head, Transform root)
    {
        Vector3 value = (Quaternion.Inverse(Quaternion.Euler(0f, root.eulerAngles.y, 0f)) * head.rotation).eulerAngles;
        return new Vector3(NormalizeAddedAngle(value.x), NormalizeAddedAngle(value.y), NormalizeAddedAngle(value.z));
    }

    private static float NormalizeAddedAngle(float value)
    {
        value %= 360f;
        return value > 180f ? value - 360f : value;
    }

    private static MultiAimConstraintData.Axis AddedClosestAxis(Vector3 direction,
        MultiAimConstraintData.Axis? excluded = null)
    {
        direction.Normalize();
        MultiAimConstraintData.Axis best = MultiAimConstraintData.Axis.Z;
        float bestDot = float.NegativeInfinity;
        foreach (MultiAimConstraintData.Axis axis in Enum.GetValues(typeof(MultiAimConstraintData.Axis)))
        {
            if (excluded.HasValue && Mathf.Abs(Vector3.Dot(AddedAxisVector(axis), AddedAxisVector(excluded.Value))) > 0.99f)
                continue;
            float dot = Vector3.Dot(AddedAxisVector(axis), direction);
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = axis;
        }
        return best;
    }

    private static Vector3 AddedAxisVector(MultiAimConstraintData.Axis axis)
    {
        switch (axis)
        {
            case MultiAimConstraintData.Axis.X: return Vector3.right;
            case MultiAimConstraintData.Axis.X_NEG: return Vector3.left;
            case MultiAimConstraintData.Axis.Y: return Vector3.up;
            case MultiAimConstraintData.Axis.Y_NEG: return Vector3.down;
            case MultiAimConstraintData.Axis.Z_NEG: return Vector3.back;
            default: return Vector3.forward;
        }
    }

    private static Color ChooseContrastingEffectColor(GameObject root, Transform excludedWeapon)
    {
        Color body = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.gameObject.activeInHierarchy && !renderer.transform.IsChildOf(excludedWeapon))
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Select(material => material.HasProperty("_WarriorTintColor") ? material.GetColor("_WarriorTintColor") :
                material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                material.HasProperty("_Color") ? material.GetColor("_Color") : Color.gray)
            .OrderByDescending(color => Mathf.Max(color.r, color.g, color.b) - Mathf.Min(color.r, color.g, color.b))
            .FirstOrDefault();
        Color.RGBToHSV(body, out float hue, out float saturation, out _);
        hue = Mathf.Repeat(hue + (saturation > 0.2f ? 0.5f : 0.56f), 1f);
        Color result = Color.HSVToRGB(hue, 0.72f, 1f);
        result.a = 1f;
        return result;
    }

    private static void WriteAddedEnemyReport(
        GameObject root, Animator animator, Transform weaponRoot, Transform sword,
        Transform rightSocket, Transform leftSocket, Transform rightTarget, Transform leftTarget,
        Transform rightHand, Transform leftHand, MultiParentConstraint blend,
        TwoBoneIKConstraint rightIk, TwoBoneIKConstraint leftIk, TargetMatching matching,
        BrokenEdgeBladeVfx bladeVfx, KnightRocketLegVfx rocket,
        Vector3 authoredPosition, Quaternion authoredRotation, Vector3 authoredScale, Color effectColor)
    {
        WeightedTransformArray sources = blend.data.sourceObjects;
        float rightError = CalculateGripPositionError(rightSocket, weaponRoot, rightTarget, rightHand);
        float leftError = CalculateGripPositionError(leftSocket, weaponRoot, leftTarget, leftHand);
        bool transformPreserved = Vector3.Distance(weaponRoot.localPosition, authoredPosition) < 0.0001f &&
                                  Quaternion.Angle(weaponRoot.localRotation, authoredRotation) < 0.01f &&
                                  Vector3.Distance(weaponRoot.localScale, authoredScale) < 0.0001f;
        Transform leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var lines = new List<string>
        {
            $"Scene={ScenePath}",
            $"Enemy={root.name}",
            $"Active={root.activeSelf}",
            $"Humanoid={animator.isHuman}",
            $"Components={string.Join(",", root.GetComponents<Component>().Select(value => value.GetType().Name))}",
            $"WeaponRoot={GetPath(weaponRoot, root.transform)}",
            $"Sword={GetPath(sword, root.transform)}",
            $"AuthoredBlendTransformPreserved={transformPreserved}",
            $"WeaponFinite={IsFinite(weaponRoot.position) && IsFinite(weaponRoot.rotation)}",
            $"BlendSources={sources.Count}:{(sources.Count > 0 ? sources.GetTransform(0).name : "MISSING")}+{(sources.Count > 1 ? sources.GetTransform(1).name : "MISSING")}",
            $"BlendConstrainedExact={blend.data.constrainedObject == weaponRoot}",
            $"RightIKExact={rightIk.data.tip == rightHand && rightIk.data.target == rightTarget}",
            $"LeftIKExact={leftIk.data.tip == leftHand && leftIk.data.target == leftTarget}",
            $"GripErrorRight={rightError:F6}",
            $"GripErrorLeft={leftError:F6}",
            $"HitterTargetExact={matching.Target == sword}",
            $"BladeVfxOnSword={bladeVfx != null && bladeVfx.transform == sword}",
            $"RocketInstalled={rocket != null}",
            $"LeftUpperLegScale={(leftUpperLeg != null ? leftUpperLeg.localScale.ToString("F4") : "MISSING")}",
            $"RightUpperLegScale={(rightUpperLeg != null ? rightUpperLeg.localScale.ToString("F4") : "MISSING")}",
            $"EffectColor={ColorUtility.ToHtmlStringRGB(effectColor)}"
        };
        File.WriteAllLines(AddedEnemyReportPath, lines);
        if (!transformPreserved || rightError > 0.002f || leftError > 0.002f || matching.Target != sword ||
            rightIk.data.target != rightTarget || leftIk.data.target != leftTarget)
            throw new InvalidOperationException($"{root.name}: post-install validation failed. See {AddedEnemyReportPath}.");
    }

    private static string GetPath(Transform value, Transform root)
    {
        if (value == null) return "MISSING";
        var parts = new List<string>();
        for (Transform current = value; current != null && current != root; current = current.parent) parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void RemovePreviousInstallation(Transform root)
    {
        foreach (string name in new[] { "RigLayer", "Hitter", "Crosshead" })
        {
            Transform child = FindDirect(root, name);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true).Where(t =>
                     t != root && (t.name == "Weapon_Blend" ||
                                   t.name == "RightHand Weapon (Inactive)" ||
                                   t.name == "LeftHand Weapon (Inactive)" ||
                                   t.name == "Weapon_r Socket" ||
                                   t.name == "Weapon_l Socket")).ToArray())
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static GameObject CloneHierarchy(GameObject source, Transform parent, string name)
    {
        GameObject clone = UnityEngine.Object.Instantiate(source);
        clone.name = name;
        clone.transform.SetParent(parent, false);
        clone.SetActive(true);
        return clone;
    }

    private static void CreateInactiveHandWeapon(GameObject source, Transform hand, string name, Material material)
    {
        Transform clone = CloneHierarchy(source, hand, name).transform;
        clone.localPosition = Vector3.zero;
        clone.localRotation = Quaternion.identity;
        clone.localScale = DivideScale(source.transform.lossyScale, hand.lossyScale);
        RemoveWeaponPhysics(clone);
        ApplyMaterial(clone, material);
        SetLayerRecursively(clone.gameObject, 8);
        clone.gameObject.SetActive(false);
    }

    private static Transform CreateGripSolvedWeaponSocket(
        Transform weaponRoot,
        Transform gripTarget,
        Transform hand,
        string name)
    {
        Transform socket = new GameObject(name).transform;
        socket.SetParent(hand, false);

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

    private static void RemoveWeaponPhysics(Transform weapon)
    {
        foreach (TargetMatching value in weapon.GetComponentsInChildren<TargetMatching>(true)) UnityEngine.Object.DestroyImmediate(value);
        foreach (SwordHitbox value in weapon.GetComponentsInChildren<SwordHitbox>(true)) UnityEngine.Object.DestroyImmediate(value);
        foreach (Rigidbody value in weapon.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(value);
        foreach (Collider value in weapon.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(value);
        foreach (BrokenEdgeBladeVfx value in weapon.GetComponentsInChildren<BrokenEdgeBladeVfx>(true)) UnityEngine.Object.DestroyImmediate(value);
    }

    private static Transform CreateTarget(Transform parent, string name, Transform hand)
    {
        Transform target = new GameObject(name).transform;
        target.SetParent(parent, true);
        target.position = hand.position;
        target.rotation = hand.rotation;
        target.localScale = Vector3.one;
        return target;
    }

    private static void ConfigureLegsAnimator(LegsAnimator legs, Animator animator, Transform root)
    {
        bool enabled = legs.enabled;
        legs.enabled = false;
        legs.Mecanim = animator;
        legs._Editor_BaseTransform = root;
        legs.Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        legs.SpineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        legs.Finder_AutoFindLegsIfHuman(animator);
        legs.User_RefreshHelperVariablesOnParametersChange();
        legs.enabled = enabled;
    }

    private static Material FindCharacterMaterial(GameObject knight)
    {
        return knight.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(material => material != null);
    }

    private static void ApplyMaterial(Transform root, Material material)
    {
        if (material == null) return;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    private static Color ExtractPaletteColor(Material material)
    {
        if (material == null) return new Color(0.1f, 0.75f, 1f, 1f);
        if (material.HasProperty("_BaseColor"))
        {
            Color value = material.GetColor("_BaseColor");
            if (Mathf.Max(value.r, value.g, value.b) - Mathf.Min(value.r, value.g, value.b) > 0.08f)
                return value;
        }
        if (material.HasProperty("_RimColor")) return material.GetColor("_RimColor");
        if (material.HasProperty("_LightTint")) return material.GetColor("_LightTint");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return new Color(0.1f, 0.75f, 1f, 1f);
    }

    private static void SetBladePalette(BrokenEdgeBladeVfx effect, Color color)
    {
        Color glow = new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f, 0.95f);
        Color core = Color.Lerp(color, Color.white, 0.7f);
        core.a = 0.85f;
        var serialized = new SerializedObject(effect);
        SerializedProperty glowProperty = serialized.FindProperty("glowColor");
        SerializedProperty coreProperty = serialized.FindProperty("coreColor");
        if (glowProperty != null) glowProperty.colorValue = glow;
        if (coreProperty != null) coreProperty.colorValue = core;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Bounds CalculateOwnBounds(Transform root, params Transform[] excluded)
    {
        var excludedSet = new HashSet<Transform>(excluded.Where(value => value != null));
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !excludedSet.Any(exclude => renderer.transform.IsChildOf(exclude)))
            .Where(renderer => !renderer.transform.name.StartsWith(KnightPrefix, StringComparison.OrdinalIgnoreCase) || renderer.transform == root)
            .ToArray();
        if (renderers.Length == 0) return new Bounds(root.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void EnableProvidesContacts(Collider collider)
    {
        var serialized = new SerializedObject(collider);
        SerializedProperty property = serialized.FindProperty("m_ProvidesContacts");
        if (property != null)
        {
            property.boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void WriteValidationReport(Scene scene, List<GameObject> installed, EnemyWorker[] oldEnemies)
    {
        var lines = new List<string>
        {
            $"Scene={scene.path}",
            $"Installed={installed.Count}",
            $"PreviousEnemiesDisabled={oldEnemies.Count(enemy => enemy != null && !enemy.gameObject.activeSelf)}/{oldEnemies.Length}"
        };
        foreach (GameObject knight in installed)
        {
            Animator animator = knight.GetComponent<Animator>();
            EnemyWorker enemy = knight.GetComponent<EnemyWorker>();
            RigBuilder builder = knight.GetComponent<RigBuilder>();
            MultiParentConstraint blend = knight.GetComponentsInChildren<MultiParentConstraint>(true)
                .FirstOrDefault(value => value.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0);
            WeightedTransformArray blendSources = blend != null ? blend.data.sourceObjects : default;
            Transform blendSource0 = blend != null && blendSources.Count > 0 ? blendSources.GetTransform(0) : null;
            Transform blendSource1 = blend != null && blendSources.Count > 1 ? blendSources.GetTransform(1) : null;
            string source0 = blendSource0 != null ? blendSource0.name : "MISSING";
            string source1 = blendSource1 != null ? blendSource1.name : "MISSING";
            int ikCount = knight.GetComponentsInChildren<TwoBoneIKConstraint>(true).Length;
            Transform sword = enemy == null ? null : ReadObject<Transform>(enemy, "sword");
            Transform weaponRoot = sword != null ? FindAncestorNamed(sword, "Weapon_Blend") : null;
            Transform rightTarget = weaponRoot != null ? FindDeep(weaponRoot, "rh_target") : null;
            Transform leftTarget = weaponRoot != null ? FindDeep(weaponRoot, "lh_target") : null;
            Transform rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform leftHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            float rightGripError = CalculateGripPositionError(blendSource0, weaponRoot, rightTarget, rightHand);
            float leftGripError = CalculateGripPositionError(blendSource1, weaponRoot, leftTarget, leftHand);
            Transform hitter = FindDirect(knight.transform, "Hitter");
            TargetMatching matching = hitter != null ? hitter.GetComponent<TargetMatching>() : null;
            BrokenEdgeBladeVfx bladeVfx = sword != null ? sword.GetComponent<BrokenEdgeBladeVfx>() : null;
            Transform vfxBlade = bladeVfx != null
                ? new SerializedObject(bladeVfx).FindProperty("bladeVisual")?.objectReferenceValue as Transform
                : null;
            bool exactHitterTarget = matching != null && matching.Target == sword;
            bool exactVfxBlade = sword != null && vfxBlade != null &&
                                 (vfxBlade == sword || vfxBlade.IsChildOf(sword));
            Material swordMaterial = sword == null ? null : sword.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).FirstOrDefault(material => material != null);
            bool finiteWeapon = sword != null && IsFinite(sword.position) && IsFinite(sword.rotation);
            lines.Add($"{knight.name}: active={knight.activeSelf}, animatorHuman={animator != null && animator.isHuman}, enemy={enemy != null}, rigBuilder={builder != null}, weaponBlend={blend != null}, blendSources={source0}+{source1}, gripErrorR={rightGripError:F6}, gripErrorL={leftGripError:F6}, finiteWeapon={finiteWeapon}, ik={ikCount}, sword={sword?.name ?? "MISSING"}, swordMaterial={AssetDatabase.GetAssetPath(swordMaterial)}, hitterTargetExact={exactHitterTarget}, vfxBladeExact={exactVfxBlade}, nav={knight.GetComponent<NavMeshAgent>() != null}, controller={knight.GetComponent<CharacterController>() != null}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)) ?? "Temp");
        File.WriteAllLines(ReportPath, lines);
    }

    private static bool IsFinite(Vector3 value)
        => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    private static bool IsFinite(Quaternion value)
        => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static Transform FindAncestorNamed(Transform value, string name)
    {
        for (Transform current = value; current != null; current = current.parent)
            if (current.name == name) return current;
        return null;
    }

    private static float CalculateGripPositionError(
        Transform socket,
        Transform weaponRoot,
        Transform gripTarget,
        Transform hand)
    {
        if (socket == null || weaponRoot == null || gripTarget == null || hand == null)
            return float.PositiveInfinity;
        Matrix4x4 targetInWeapon = weaponRoot.worldToLocalMatrix * gripTarget.localToWorldMatrix;
        Vector3 predictedTarget = socket.localToWorldMatrix.MultiplyPoint3x4(targetInWeapon.GetColumn(3));
        return Vector3.Distance(predictedTarget, hand.position);
    }

    private static T CopyOrAdd<T>(T source, GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (!component) component = target.AddComponent<T>();
        if (source != null) EditorUtility.CopySerialized(source, component);
        return component;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (!component) component = target.AddComponent<T>();
        return component;
    }

    private static Transform RequiredBone(Animator animator, HumanBodyBones bone)
    {
        Transform value = animator.GetBoneTransform(bone);
        if (value == null) throw new InvalidOperationException($"{animator.name}: humanoid bone {bone} is missing.");
        return value;
    }

    private static Transform FindDirect(Transform root, string name)
        => root.Cast<Transform>().FirstOrDefault(child => child.name == name);

    private static Transform FindDeep(Transform root, string name)
        => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);

    private static T ReadObject<T>(Component component, string propertyName) where T : UnityEngine.Object
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName);
        return property?.objectReferenceValue as T;
    }

    private static void WriteObject(Component component, string propertyName, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null) throw new InvalidOperationException($"{component.GetType().Name}.{propertyName} was not found.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WriteBool(Component component, string propertyName, bool value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null) property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WriteVector3(Component component, string propertyName, Vector3 value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null) property.vector3Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Vector3 DivideScale(Vector3 worldScale, Vector3 parentScale)
    {
        return new Vector3(
            Mathf.Abs(parentScale.x) < 0.0001f ? worldScale.x : worldScale.x / parentScale.x,
            Mathf.Abs(parentScale.y) < 0.0001f ? worldScale.y : worldScale.y / parentScale.y,
            Mathf.Abs(parentScale.z) < 0.0001f ? worldScale.z : worldScale.z / parentScale.z);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = layer;
    }
}
