using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FIMSpace.FProceduralAnimation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

public static class WarriorEnemyInstaller
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string SourceModelPath = "Assets/warrior/all_warrior.FBX";
    private const string ShowcaseRootName = "Warrior_Character_Sets";
    private const string WarriorPrefix = "Warrior_Set_";
    private const string ReportPath = "Library/WarriorEnemyInstallReport.txt";
    private const string RequestPath = "Library/InstallActiveWarriorEnemies.request";

    static WarriorEnemyInstaller()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && File.Exists(RequestPath))
                EditorApplication.delayCall += TryInstallRequested;
        };
    }

    [InitializeOnLoadMethod]
    private static void QueueRequestedInstall()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += TryInstallRequested;
    }

    private static void TryInstallRequested()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        try
        {
            Install();
            File.Delete(RequestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Swordplay/Convert Active Warrior Sets To Enemies")]
    public static void Install()
    {
        EnsureHumanoidImport();

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject showcaseRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == ShowcaseRootName);
        if (showcaseRoot == null)
            throw new InvalidOperationException($"'{ShowcaseRootName}' was not found in {ScenePath}.");

        List<GameObject> targets = showcaseRoot.transform.Cast<Transform>()
            .Where(child => child.name.StartsWith(WarriorPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(child => child.gameObject.activeSelf)
            .Select(child => child.gameObject)
            .OrderBy(child => child.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targets.Count == 0)
            throw new InvalidOperationException("No active generated warrior sets were found.");
        // The user intentionally enabled only a small selection. This guard prevents a stale,
        // unsaved scene from accidentally converting the complete 49-character showcase.
        if (targets.Count > 16)
            throw new InvalidOperationException($"Found {targets.Count} active warrior sets. Refusing to convert more than 16; save the intended active subset first.");

        EnemyWorker[] enemies = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<EnemyWorker>(true)).ToArray();
        EnemyWorker template = enemies.FirstOrDefault(enemy =>
            enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase)
            && enemy.gameObject.activeInHierarchy)
            ?? enemies.FirstOrDefault(enemy => enemy.name.StartsWith("WM_Master_Unity_Bow2", StringComparison.OrdinalIgnoreCase));
        if (template == null)
            throw new InvalidOperationException("WM_Master_Unity_Bow2 reference enemy was not found.");

        Animator templateAnimator = template.GetComponent<Animator>();
        RuntimeAnimatorController controller = templateAnimator != null ? templateAnimator.runtimeAnimatorController : null;
        MultiAimConstraint sourceAim = ReadObject<MultiAimConstraint>(template, "ma");
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(SourceModelPath)
            .OfType<Avatar>().FirstOrDefault(value => value != null && value.isHuman);
        if (controller == null || sourceAim == null || avatar == null)
            throw new InvalidOperationException($"Enemy controller, head aim template, or humanoid warrior Avatar is missing (controller={controller != null}, aim={sourceAim != null}, avatar={avatar != null}).");

        var installed = new List<GameObject>();
        foreach (GameObject target in targets)
        {
            InstallEnemy(target, template, controller, avatar, sourceAim);
            installed.Add(target);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        WriteReport(installed);
        Selection.objects = installed.Cast<UnityEngine.Object>().ToArray();
        Debug.Log($"[Warrior Enemy Installer] INSTALL_COMPLETE — configured {installed.Count} active warriors. Weapon and arm IK setup intentionally omitted. Report: {Path.GetFullPath(ReportPath)}");
    }

    private static void EnsureHumanoidImport()
    {
        ModelImporter importer = AssetImporter.GetAtPath(SourceModelPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException($"Could not load ModelImporter for {SourceModelPath}.");
        if (importer.animationType == ModelImporterAnimationType.Human
            && AssetDatabase.LoadAllAssetsAtPath(SourceModelPath).OfType<Avatar>().Any(avatar => avatar.isHuman))
            return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();
    }

    private static void InstallEnemy(GameObject warrior, EnemyWorker template,
        RuntimeAnimatorController controller, Avatar avatar, MultiAimConstraint sourceAim)
    {
        RemovePreviousHeadSetup(warrior.transform);
        SetLayerRecursively(warrior, template.gameObject.layer);
        warrior.tag = template.gameObject.tag;

        foreach (Animation legacyAnimation in warrior.GetComponentsInChildren<Animation>(true))
        {
            legacyAnimation.Stop();
            legacyAnimation.playAutomatically = false;
            legacyAnimation.enabled = false;
        }

        Animator animator = GetOrAdd<Animator>(warrior);
        animator.enabled = true;
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        ApplyEditorIdlePose(animator);

        Transform head = RequiredBone(animator, HumanBodyBones.Head);
        Vector3 headRotationOffset = CalculateHeadRotationOffset(head, warrior.transform);
        Transform crosshead = new GameObject("Enemy_Crosshead").transform;
        crosshead.SetParent(warrior.transform, false);
        crosshead.position = head.position + warrior.transform.forward * 5f;

        Transform rigRoot = new GameObject("Enemy_Head_Rig").transform;
        rigRoot.SetParent(warrior.transform, false);
        Rig rig = rigRoot.gameObject.AddComponent<Rig>();
        GameObject aimObject = new("HeadAim");
        aimObject.transform.SetParent(rigRoot, false);
        MultiAimConstraint headAim = aimObject.AddComponent<MultiAimConstraint>();
        EditorUtility.CopySerialized(sourceAim, headAim);
        MultiAimConstraintData aimData = headAim.data;
        aimData.constrainedObject = head;
        var aimSources = new WeightedTransformArray(0);
        aimSources.Add(new WeightedTransform(crosshead, 1f));
        aimData.sourceObjects = aimSources;
        aimData.aimAxis = ClosestAxis(head.InverseTransformDirection(warrior.transform.forward));
        aimData.upAxis = ClosestAxis(head.InverseTransformDirection(warrior.transform.up), aimData.aimAxis);
        aimData.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;
        aimData.worldUpObject = null;
        aimData.maintainOffset = false;
        aimData.offset = Vector3.zero;
        aimData.limits = new Vector2(-85f, 85f);
        aimData.constrainedXAxis = true;
        aimData.constrainedYAxis = true;
        aimData.constrainedZAxis = true;
        headAim.data = aimData;
        headAim.weight = 1f;

        RigBuilder rigBuilder = GetOrAdd<RigBuilder>(warrior);
        rigBuilder.layers = new List<RigLayer> { new(rig, true) };
        rigBuilder.enabled = true;

        Bounds bounds = CalculateBodyBounds(warrior.transform);
        float height = Mathf.Max(1.2f, bounds.size.y);
        float radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.42f, 0.22f, 0.5f);
        Vector3 localCenter = warrior.transform.InverseTransformPoint(bounds.center);

        CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(warrior);
        capsule.direction = 1;
        capsule.center = localCenter;
        capsule.height = height;
        capsule.radius = Mathf.Min(radius, height * 0.45f);
        capsule.isTrigger = true;
        EnableProvidesContacts(capsule);

        CharacterController characterController = GetOrAdd<CharacterController>(warrior);
        characterController.center = localCenter;
        characterController.height = height;
        characterController.radius = Mathf.Min(radius, height * 0.45f);
        characterController.slopeLimit = 55f;
        characterController.stepOffset = Mathf.Min(0.45f, Mathf.Max(0f, height - characterController.radius * 2f));
        characterController.skinWidth = Mathf.Max(0.03f, characterController.radius * 0.1f);
        characterController.minMoveDistance = 0f;
        characterController.enableOverlapRecovery = true;

        NavMeshAgent agent = CopyOrAdd(template.GetComponent<NavMeshAgent>(), warrior);
        agent.radius = characterController.radius;
        agent.height = characterController.height;
        agent.baseOffset = Mathf.Max(0f, localCenter.y - height * 0.5f);
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.autoTraverseOffMeshLink = true;
        agent.autoRepath = true;

        Rigidbody body = CopyOrAdd(template.GetComponent<Rigidbody>(), warrior);
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = false;
        body.constraints = RigidbodyConstraints.FreezeAll;

        CopyOrAdd(template.GetComponent<AudioSource>(), warrior);
        LegsAnimator legs = warrior.GetComponent<LegsAnimator>() ?? warrior.AddComponent<LegsAnimator>();
        ConfigureLegsAnimator(legs, animator, warrior.transform);

        EnemyWorker enemy = warrior.GetComponent<EnemyWorker>() ?? warrior.AddComponent<EnemyWorker>();
        EditorUtility.CopySerialized(template, enemy);
        WriteObject(enemy, "crosshead", crosshead);
        WriteObject(enemy, "ma", headAim);
        WriteObject(enemy, "sword", null);
        WriteObject(enemy, "weaponBlend", null);
        WriteObject(enemy, "rightArmIK", null);
        WriteObject(enemy, "leftArmIK", null);
        WriteBool(enemy, "miiMotion", false);
        WriteVector3(enemy, "headRotOffset", headRotationOffset);
        WriteBool(enemy, "headOffsetIsRootLocal", true);

        // The requested pass intentionally does not install or configure weapon hitboxes,
        // weapon trails, hand sockets, weapon blending, or either arm IK constraint.
        foreach (TwoBoneIKConstraint constraint in warrior.GetComponentsInChildren<TwoBoneIKConstraint>(true))
            UnityEngine.Object.DestroyImmediate(constraint);
        foreach (MultiParentConstraint constraint in warrior.GetComponentsInChildren<MultiParentConstraint>(true))
            if (constraint.name.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                UnityEngine.Object.DestroyImmediate(constraint);

        try
        {
            rigBuilder.Build();
            rigBuilder.Evaluate(0f);
            float aimDot = CalculateAimDot(headAim);
            if (aimDot < 0.94f)
                throw new InvalidOperationException($"{warrior.name}: calibrated head aim is inaccurate (dot={aimDot:F4}).");
        }
        catch (Exception exception) { Debug.LogWarning($"{warrior.name}: head RigBuilder deferred build: {exception.Message}"); }

        EditorUtility.SetDirty(warrior);
        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(rigBuilder);
        EditorUtility.SetDirty(enemy);
    }

    private static void RemovePreviousHeadSetup(Transform root)
    {
        foreach (string name in new[] { "Enemy_Crosshead", "Enemy_Head_Rig" })
        {
            Transform existing = root.Find(name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }
    }

    private static Bounds CalculateBodyBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false)
            .Where(renderer => !IsUnderNamedPart(renderer.transform, root, "sword_01"))
            .Where(renderer => !IsUnderNamedPart(renderer.transform, root, "bow"))
            .Where(renderer => !IsUnderNamedPart(renderer.transform, root, "shield_01"))
            .ToArray();
        if (renderers.Length == 0)
            return new Bounds(root.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static bool IsUnderNamedPart(Transform candidate, Transform root, string partName)
    {
        for (Transform current = candidate; current != null && current != root; current = current.parent)
            if (string.Equals(current.name, partName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void ConfigureLegsAnimator(LegsAnimator legs, Animator animator, Transform root)
    {
        bool wasEnabled = legs.enabled;
        legs.enabled = false;
        legs.Mecanim = animator;
        legs._Editor_BaseTransform = root;
        legs.Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        legs.SpineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        legs.Finder_AutoFindLegsIfHuman(animator);
        legs.User_RefreshHelperVariablesOnParametersChange();
        legs.enabled = wasEnabled;
    }

    private static void ApplyEditorIdlePose(Animator animator)
    {
        if (Application.isPlaying) return;
        animator.Rebind();
        animator.Update(0f);
        for (int layer = 0; layer < animator.layerCount; layer++)
            animator.Play("Idle", layer, 0f);
        animator.Update(1f / 60f);
    }

    private static Vector3 CalculateHeadRotationOffset(Transform head, Transform root)
    {
        Vector3 value = (Quaternion.Inverse(Quaternion.Euler(0f, root.eulerAngles.y, 0f)) * head.rotation).eulerAngles;
        return new Vector3(NormalizeAngle(value.x), NormalizeAngle(value.y), NormalizeAngle(value.z));
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle > 180f ? angle - 360f : angle;
    }

    private static MultiAimConstraintData.Axis ClosestAxis(Vector3 direction,
        MultiAimConstraintData.Axis? excluded = null)
    {
        direction.Normalize();
        MultiAimConstraintData.Axis best = MultiAimConstraintData.Axis.Z;
        float bestDot = float.NegativeInfinity;
        foreach (MultiAimConstraintData.Axis axis in Enum.GetValues(typeof(MultiAimConstraintData.Axis)))
        {
            if (excluded.HasValue && SameAxisFamily(axis, excluded.Value)) continue;
            float dot = Vector3.Dot(AxisVector(axis), direction);
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = axis;
        }
        return best;
    }

    private static bool SameAxisFamily(MultiAimConstraintData.Axis first, MultiAimConstraintData.Axis second)
        => Mathf.Abs(Vector3.Dot(AxisVector(first), AxisVector(second))) > 0.99f;

    private static Vector3 AxisVector(MultiAimConstraintData.Axis axis)
    {
        return axis switch
        {
            MultiAimConstraintData.Axis.X => Vector3.right,
            MultiAimConstraintData.Axis.X_NEG => Vector3.left,
            MultiAimConstraintData.Axis.Y => Vector3.up,
            MultiAimConstraintData.Axis.Y_NEG => Vector3.down,
            MultiAimConstraintData.Axis.Z => Vector3.forward,
            _ => Vector3.back
        };
    }

    private static float CalculateAimDot(MultiAimConstraint constraint)
    {
        MultiAimConstraintData data = constraint.data;
        Transform source = data.sourceObjects.Count > 0 ? data.sourceObjects.GetTransform(0) : null;
        if (data.constrainedObject == null || source == null) return -1f;
        Vector3 worldAim = data.constrainedObject.TransformDirection(AxisVector(data.aimAxis)).normalized;
        Vector3 targetDirection = (source.position - data.constrainedObject.position).normalized;
        return Vector3.Dot(worldAim, targetDirection);
    }

    private static void WriteReport(IEnumerable<GameObject> installed)
    {
        var lines = new List<string>();
        foreach (GameObject warrior in installed)
        {
            Animator animator = warrior.GetComponent<Animator>();
            EnemyWorker enemy = warrior.GetComponent<EnemyWorker>();
            MultiAimConstraint aim = warrior.GetComponentInChildren<MultiAimConstraint>(true);
            MultiAimConstraintData aimData = aim != null ? aim.data : default;
            float aimDot = aim != null ? CalculateAimDot(aim) : -1f;
            Vector3 headOffset = enemy != null ? ReadVector3(enemy, "headRotOffset") : Vector3.zero;
            bool miiMotion = enemy != null && ReadBool(enemy, "miiMotion");
            bool localHeadOffset = enemy != null && ReadBool(enemy, "headOffsetIsRootLocal");
            lines.Add($"{warrior.name}: active={warrior.activeSelf}, enemy={enemy != null}, animatorHuman={animator != null && animator.isHuman}, controller={warrior.GetComponent<CharacterController>() != null}, nav={warrior.GetComponent<NavMeshAgent>() != null}, rigidbody={warrior.GetComponent<Rigidbody>() != null}, audio={warrior.GetComponent<AudioSource>() != null}, legs={warrior.GetComponent<LegsAnimator>() != null}, headAim={aim != null}, aimAxis={aimData.aimAxis}, upAxis={aimData.upAxis}, aimDot={aimDot:F4}, headOffset={headOffset}, localHeadOffset={localHeadOffset}, miiMotion={miiMotion}, rigBuilder={warrior.GetComponent<RigBuilder>() != null}, armIK={warrior.GetComponentsInChildren<TwoBoneIKConstraint>(true).Length}, weaponBlend={warrior.GetComponentsInChildren<MultiParentConstraint>(true).Count(value => value.name.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0)}, swordRef={ReadObject<Transform>(enemy, "sword") != null}");
        }
        File.WriteAllLines(ReportPath, lines);
    }

    private static void EnableProvidesContacts(Collider collider)
    {
        var serialized = new SerializedObject(collider);
        SerializedProperty property = serialized.FindProperty("m_ProvidesContacts");
        if (property != null) property.boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

    private static Transform RequiredBone(Animator animator, HumanBodyBones bone)
    {
        Transform value = animator.GetBoneTransform(bone);
        if (value == null) throw new InvalidOperationException($"{animator.name}: humanoid bone {bone} is missing.");
        return value;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T value = target.GetComponent<T>();
        return value != null ? value : target.AddComponent<T>();
    }

    private static T CopyOrAdd<T>(T source, GameObject target) where T : Component
    {
        T value = GetOrAdd<T>(target);
        if (source != null) EditorUtility.CopySerialized(source, value);
        return value;
    }

    private static T ReadObject<T>(Component component, string propertyName) where T : UnityEngine.Object
    {
        if (component == null) return null;
        SerializedProperty property = new SerializedObject(component).FindProperty(propertyName);
        return property?.objectReferenceValue as T;
    }

    private static void WriteObject(Component component, string propertyName, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{component.GetType().Name}.{propertyName} was not found.");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WriteBool(Component component, string propertyName, bool value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{component.GetType().Name}.{propertyName} was not found.");
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WriteVector3(Component component, string propertyName, Vector3 value)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"{component.GetType().Name}.{propertyName} was not found.");
        property.vector3Value = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Vector3 ReadVector3(Component component, string propertyName)
    {
        if (component == null) return Vector3.zero;
        SerializedProperty property = new SerializedObject(component).FindProperty(propertyName);
        return property != null ? property.vector3Value : Vector3.zero;
    }

    private static bool ReadBool(Component component, string propertyName)
    {
        if (component == null) return false;
        SerializedProperty property = new SerializedObject(component).FindProperty(propertyName);
        return property != null && property.boolValue;
    }
}
