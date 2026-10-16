#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class KnightWeaponSocketPoseMigration
{
    private const string SceneName = "BrokenEdgeLaunch";
    private static readonly string RequestPath = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../Library/RepairKnightWeaponSockets.request"));

    static KnightWeaponSocketPoseMigration()
    {
        if (File.Exists(RequestPath))
            EditorApplication.delayCall += RunWhenReady;
    }

    private static void RunWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += RunWhenReady;
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != SceneName)
        {
            Debug.LogWarning($"KNIGHT_WEAPON_SOCKET_REPAIR waiting for {SceneName}; active={scene.name}");
            return;
        }

        int repaired = 0;
        foreach (EnemyWorker enemy in UnityEngine.Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Where(value => value.gameObject.scene == scene))
        {
            SerializedObject serializedEnemy = new SerializedObject(enemy);
            Transform sword = serializedEnemy.FindProperty("sword")?.objectReferenceValue as Transform;
            Animator animator = enemy.GetComponent<Animator>();
            Transform rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (sword == null || rightHand == null) continue;

            Transform socket = rightHand.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value.name == "Weapon_r Socket");
            if (socket == null || sword.IsChildOf(socket)) continue;

            Transform weaponRoot = sword;
            while (weaponRoot.parent != null && weaponRoot.parent != rightHand)
                weaponRoot = weaponRoot.parent;
            if (weaponRoot.parent != rightHand
                || weaponRoot.name.IndexOf("RightHand Weapon", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Undo.SetTransformParent(weaponRoot, socket, "Anchor knight weapon under right-hand socket");
            DisableConstraint(serializedEnemy, "weaponBlend");
            DisableConstraint(serializedEnemy, "rightArmIK");
            DisableConstraint(serializedEnemy, "leftArmIK");
            EditorUtility.SetDirty(enemy);
            EditorUtility.SetDirty(weaponRoot);
            repaired++;
        }

        if (repaired > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        File.Delete(RequestPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"KNIGHT_WEAPON_SOCKET_REPAIR_COMPLETE repaired={repaired}");
    }

    private static void DisableConstraint(SerializedObject owner, string propertyName)
    {
        IRigConstraint constraint = owner.FindProperty(propertyName)?.objectReferenceValue as IRigConstraint;
        if (constraint != null) constraint.weight = 0f;
    }
}
#endif
