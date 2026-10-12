using System;
using System.Collections.Generic;
using System.Linq;
using FIMSpace.FProceduralAnimation;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

internal static class HorsemanPlayerInstaller
{
    private const string ModelPath = "Assets/Horseman/Base mesh/Base mesh.fbx";
    private const string PrefabPath = "Assets/Horseman/Prefab/Base mesh.prefab";
    private const string VisualName = "Horseman Player Visual";

    [MenuItem("Swordplay/Install Horseman On Active Player %#h")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Horseman Installer] Exit Play Mode, then run the installer again.");
            return;
        }

        try
        {
            EnsureHumanoidImport();

            Scene scene = SceneManager.GetActiveScene();
            PlayerWorker player = UnityEngine.Object
                .FindObjectsByType<PlayerWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(candidate => candidate != null && candidate.gameObject.scene == scene)
                .OrderByDescending(candidate => candidate.gameObject.activeInHierarchy)
                .FirstOrDefault();

            if (player == null)
                throw new InvalidOperationException("No PlayerWorker exists in the active scene.");

            InstallOnPlayer(player);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = player.gameObject;
            SceneView.FrameLastActiveSceneView();
            Debug.Log("[Horseman Installer] HORSEMAN_PLAYER_INSTALL_COMPLETE — existing gameplay root, camera, colliders and tuning were preserved.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void EnsureHumanoidImport()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException($"Could not find the Horseman model at {ModelPath}.");

        if (importer.animationType == ModelImporterAnimationType.Human)
            return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
    }

    private static void InstallOnPlayer(PlayerWorker player)
    {
        Transform playerRoot = player.transform;
        Animator originalAnimator = player.GetComponent<Animator>();
        if (originalAnimator == null)
            throw new InvalidOperationException("The active player has no reference Animator to copy.");

        RuntimeAnimatorController controller = originalAnimator.runtimeAnimatorController;
        if (controller == null)
            throw new InvalidOperationException("The existing player Animator Controller is missing.");

        Transform oldBody = FindDeep(playerRoot, "WM_Master_Unity");
        Bounds oldBounds = GetBounds(oldBody != null ? oldBody : playerRoot, null);

        Transform oldRightHand = originalAnimator.isHuman
            ? originalAnimator.GetBoneTransform(HumanBodyBones.RightHand)
            : null;

        Transform swordMount = ReadTransform(player, "sword");
        if (swordMount == null) swordMount = FindDeep(playerRoot, "Weapon_r");
        if (swordMount == null)
            throw new InvalidOperationException("Could not locate the existing Weapon_r sword mount.");

        Vector3 gripPosition = oldRightHand != null
            ? oldRightHand.InverseTransformPoint(swordMount.position)
            : swordMount.localPosition;
        Quaternion gripRotation = oldRightHand != null
            ? Quaternion.Inverse(oldRightHand.rotation) * swordMount.rotation
            : swordMount.localRotation;
        Vector3 swordWorldScale = swordMount.lossyScale;

        Transform existingVisual = FindDirect(playerRoot, VisualName);
        if (existingVisual != null)
            Undo.DestroyObjectImmediate(existingVisual.gameObject);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Could not load {PrefabPath}.");

        GameObject visualObject = PrefabUtility.InstantiatePrefab(prefab, player.gameObject.scene) as GameObject;
        if (visualObject == null)
            throw new InvalidOperationException("Horseman prefab instantiation failed.");

        Undo.RegisterCreatedObjectUndo(visualObject, "Install Horseman player");
        visualObject.name = VisualName;
        Transform visual = visualObject.transform;
        Undo.SetTransformParent(visual, playerRoot, "Parent Horseman visual");
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;

        Animator animator = visualObject.GetComponent<Animator>();
        if (animator == null)
            throw new InvalidOperationException("Horseman prefab has no Animator.");
        if (animator.avatar == null || !animator.avatar.isHuman)
            throw new InvalidOperationException("Horseman Avatar is not a valid Humanoid after import.");

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = originalAnimator.applyRootMotion;
        animator.updateMode = originalAnimator.updateMode;
        animator.cullingMode = originalAnimator.cullingMode;
        animator.speed = originalAnimator.speed;

        Transform builtInWeapon = FindDirect(visual, "Weapon1");
        if (builtInWeapon != null)
            builtInWeapon.gameObject.SetActive(false);

        ConvertHorsemanMaterials(visualObject);

        Bounds newBounds = GetBounds(visual, builtInWeapon);
        if (oldBounds.size.y > 0.01f && newBounds.size.y > 0.01f)
        {
            float scale = Mathf.Clamp(oldBounds.size.y / newBounds.size.y, 0.2f, 5f);
            visual.localScale = Vector3.one * scale;

            newBounds = GetBounds(visual, builtInWeapon);
            Vector3 alignment = oldBounds.center - newBounds.center;
            alignment.y = oldBounds.min.y - newBounds.min.y;
            visual.position += alignment;
        }

        Transform newRightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform newLeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform newHead = animator.GetBoneTransform(HumanBodyBones.Head);
        if (newRightHand == null || newLeftHand == null || newHead == null)
            throw new InvalidOperationException("Horseman Humanoid mapping is missing a required hand or head bone.");

        Undo.SetTransformParent(swordMount, newRightHand, "Move sword to Horseman hand");
        swordMount.position = newRightHand.TransformPoint(gripPosition);
        swordMount.rotation = newRightHand.rotation * gripRotation;
        swordMount.localScale = DivideScale(swordWorldScale, newRightHand.lossyScale);
        swordMount.gameObject.SetActive(true);

        Transform rightTarget = FindDeep(swordMount, "rh_target");
        Transform leftTarget = FindDeep(swordMount, "lh_target");
        if (rightTarget == null) rightTarget = CreateHandTarget(swordMount, "rh_target", newRightHand);
        if (leftTarget == null) leftTarget = CreateHandTarget(swordMount, "lh_target", newLeftHand);

        Transform rigLayerTransform = FindDirect(playerRoot, "RigLayer");
        if (rigLayerTransform == null)
            throw new InvalidOperationException("The existing player's RigLayer could not be found.");

        Undo.SetTransformParent(rigLayerTransform, visual, "Move rig layer to Horseman Animator");
        rigLayerTransform.localPosition = Vector3.zero;
        rigLayerTransform.localRotation = Quaternion.identity;
        rigLayerTransform.localScale = Vector3.one;

        Rig rig = rigLayerTransform.GetComponent<Rig>();
        if (rig == null) rig = rigLayerTransform.GetComponentInChildren<Rig>(true);
        if (rig == null)
            throw new InvalidOperationException("RigLayer has no Rig component.");

        RigBuilder newRigBuilder = visualObject.GetComponent<RigBuilder>();
        if (newRigBuilder == null) newRigBuilder = Undo.AddComponent<RigBuilder>(visualObject);
        newRigBuilder.layers = new List<RigLayer> { new RigLayer(rig, true) };
        newRigBuilder.enabled = true;

        RewireRig(rigLayerTransform, animator, swordMount, rightTarget, leftTarget);

        RigBuilder oldRigBuilder = player.GetComponent<RigBuilder>();
        if (oldRigBuilder != null) oldRigBuilder.enabled = false;
        originalAnimator.enabled = false;

        Transform oldSkeleton = FindDirect(playerRoot, "root");
        if (oldSkeleton != null) oldSkeleton.gameObject.SetActive(false);
        if (oldBody != null) oldBody.gameObject.SetActive(false);

        ConfigureLegsAnimator(playerRoot, animator);
        WriteObject(player, "animatorOverride", animator);
        WriteObject(player, "rigBuilderOverride", newRigBuilder);
        WriteObject(player, "sword", swordMount);
        WriteVector3(player, "swordOffset", swordMount.localPosition);

        MultiAimConstraint headAim = rigLayerTransform.GetComponentInChildren<MultiAimConstraint>(true);
        if (headAim != null) WriteObject(player, "ma", headAim);

        TargetMatching matching = playerRoot.GetComponentInChildren<TargetMatching>(true);
        if (matching != null)
        {
            BrokenEdgeBladeVfx bladeVfx = matching.GetComponent<BrokenEdgeBladeVfx>();
            if (bladeVfx == null) bladeVfx = Undo.AddComponent<BrokenEdgeBladeVfx>(matching.gameObject);
            bladeVfx.enabled = true;
            bladeVfx.Configure(playerRoot);
            EditorUtility.SetDirty(bladeVfx);
        }

        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(newRigBuilder);
        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(player.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(visualObject);
    }

    private static void RewireRig(
        Transform rigLayer,
        Animator animator,
        Transform sword,
        Transform rightTarget,
        Transform leftTarget)
    {
        Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);

        foreach (TwoBoneIKConstraint constraint in rigLayer.GetComponentsInChildren<TwoBoneIKConstraint>(true))
        {
            bool isLeft = constraint.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          constraint.name.StartsWith("L", StringComparison.OrdinalIgnoreCase);
            TwoBoneIKConstraintData data = constraint.data;
            data.root = isLeft ? leftUpperArm : rightUpperArm;
            data.mid = isLeft ? leftLowerArm : rightLowerArm;
            data.tip = isLeft ? leftHand : rightHand;
            data.target = isLeft ? leftTarget : rightTarget;
            data.hint = null;
            data.hintWeight = 0f;
            constraint.data = data;
            EditorUtility.SetDirty(constraint);
        }

        foreach (MultiParentConstraint constraint in rigLayer.GetComponentsInChildren<MultiParentConstraint>(true))
        {
            MultiParentConstraintData data = constraint.data;
            data.constrainedObject = sword;
            WeightedTransformArray sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(rightHand, 1f));
            sources.Add(new WeightedTransform(leftHand, 0f));
            data.sourceObjects = sources;
            constraint.data = data;
            EditorUtility.SetDirty(constraint);
        }

        foreach (MultiAimConstraint constraint in rigLayer.GetComponentsInChildren<MultiAimConstraint>(true))
        {
            MultiAimConstraintData data = constraint.data;
            data.constrainedObject = head;
            constraint.data = data;
            EditorUtility.SetDirty(constraint);
        }
    }

    private static void ConfigureLegsAnimator(Transform playerRoot, Animator animator)
    {
        LegsAnimator legs = playerRoot.GetComponent<LegsAnimator>();
        if (legs == null) return;

        bool wasEnabled = legs.enabled;
        legs.enabled = false;
        legs.Mecanim = animator;
        legs._Editor_BaseTransform = playerRoot;
        legs.Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        legs.SpineBone = animator.GetBoneTransform(HumanBodyBones.Spine);

        foreach (LegsAnimator.Leg leg in legs.Legs)
        {
            leg.BoneStart = null;
            leg.BoneMid = null;
            leg.BoneEnd = null;
            leg.BoneFeet = null;
        }

        legs.Finder_AutoFindLegsIfHuman(animator);
        legs.User_RefreshHelperVariablesOnParametersChange();
        legs.enabled = wasEnabled;
        EditorUtility.SetDirty(legs);
    }

    private static void ConvertHorsemanMaterials(GameObject visual)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return;

        var materials = new HashSet<Material>();
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) materials.Add(material);

        foreach (Material material in materials)
        {
            if (material.shader == urpLit) continue;

            Texture albedo = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Texture normal = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
            Texture metallic = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;
            Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
            bool alphaClip = material.renderQueue >= 2450 ||
                             material.IsKeywordEnabled("_ALPHATEST_ON");

            Undo.RecordObject(material, "Convert Horseman material to URP");
            material.shader = urpLit;
            if (albedo != null) material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", color);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            if (metallic != null)
            {
                material.SetTexture("_MetallicGlossMap", metallic);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            material.SetFloat("_Cutoff", cutoff);
            if (alphaClip)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = 2450;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = -1;
            }
            EditorUtility.SetDirty(material);
        }
    }

    private static Transform CreateHandTarget(Transform parent, string name, Transform hand)
    {
        GameObject targetObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(targetObject, $"Create {name}");
        Transform target = targetObject.transform;
        target.SetParent(parent, true);
        target.SetPositionAndRotation(hand.position, hand.rotation);
        return target;
    }

    private static Transform FindDirect(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        return null;
    }

    private static Bounds GetBounds(Transform root, Transform excludedRoot)
    {
        bool found = false;
        Bounds bounds = default;
        if (root == null) return bounds;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || renderer is TrailRenderer || renderer is LineRenderer ||
                renderer is ParticleSystemRenderer || !renderer.enabled)
                continue;
            if (excludedRoot != null && renderer.transform.IsChildOf(excludedRoot))
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private static Vector3 DivideScale(Vector3 worldScale, Vector3 parentScale)
    {
        return new Vector3(
            Mathf.Abs(parentScale.x) > 0.0001f ? worldScale.x / parentScale.x : worldScale.x,
            Mathf.Abs(parentScale.y) > 0.0001f ? worldScale.y / parentScale.y : worldScale.y,
            Mathf.Abs(parentScale.z) > 0.0001f ? worldScale.z / parentScale.z : worldScale.z);
    }

    private static Transform ReadTransform(UnityEngine.Object target, string propertyName)
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
        return property != null ? property.objectReferenceValue as Transform : null;
    }

    private static void WriteObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"Missing serialized property {propertyName} on {target.name}.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WriteVector3(UnityEngine.Object target, string propertyName, Vector3 value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"Missing serialized property {propertyName} on {target.name}.");
        property.vector3Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}

