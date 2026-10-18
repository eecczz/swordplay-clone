using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CombatHitboxDiagnostics
{
    private const string ScenePath = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string ReportPath = "Library/CombatHitboxDiagnostics.txt";

    [MenuItem("Tools/Swordplay/Report Combat Hitboxes")]
    public static void Report()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var report = new StringBuilder();
        foreach (MonoBehaviour worker in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                     .Where(value => value is PlayerWorker || value is EnemyWorker)
                     .Where(value => value.gameObject.activeInHierarchy))
        {
            Transform root = worker.transform;
            report.AppendLine($"COMBATANT {HierarchyPath(root)} type={worker.GetType().Name} tag={root.tag} layer={LayerMask.LayerToName(root.gameObject.layer)}({root.gameObject.layer})");
            var serialized = new SerializedObject(worker);
            AppendReference(report, "sword", serialized.FindProperty("sword"));
            AppendReference(report, "animatorOverride", serialized.FindProperty("animatorOverride"));

            foreach (TargetMatching target in root.GetComponentsInChildren<TargetMatching>(true))
            {
                report.AppendLine($"  TARGET {HierarchyPath(target.transform)} -> {HierarchyPath(target.Target)} activeSelf={target.gameObject.activeSelf} active={target.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(target.gameObject.layer)}({target.gameObject.layer}) colliders={target.GetComponentsInChildren<Collider>(true).Length}");
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                string lower = HierarchyPath(collider.transform).ToLowerInvariant();
                bool weaponLike = lower.Contains("weapon") || lower.Contains("sword") || lower.Contains("hitter") || lower.Contains("blade");
                if (worker is PlayerWorker || weaponLike || collider.transform == root)
                    report.AppendLine($"  COLLIDER {HierarchyPath(collider.transform)} type={collider.GetType().Name} active={collider.gameObject.activeInHierarchy} enabled={collider.enabled} trigger={collider.isTrigger} layer={LayerMask.LayerToName(collider.gameObject.layer)}({collider.gameObject.layer})");
            }
            report.AppendLine();
        }

        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log($"[Combat Hitbox Diagnostics] Wrote {ReportPath}");
    }

    [MenuItem("Tools/Swordplay/Repair Combat Hitboxes")]
    public static void Repair()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (MonoBehaviour worker in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                     .Where(value => value is PlayerWorker || value is EnemyWorker)
                     .Where(value => value.gameObject.activeInHierarchy))
        {
            bool isPlayer = worker is PlayerWorker;
            int bodyLayer = LayerMask.NameToLayer(isPlayer ? "Player" : "Enemy");
            int swordLayer = LayerMask.NameToLayer(isPlayer ? "Sword" : "EnemySword");
            worker.gameObject.layer = bodyLayer;
            foreach (Collider body in worker.GetComponents<Collider>())
                body.gameObject.layer = bodyLayer;

            foreach (TargetMatching target in worker.GetComponentsInChildren<TargetMatching>(true))
            {
                target.gameObject.layer = swordLayer;
                foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
                {
                    collider.gameObject.layer = swordLayer;
                    collider.isTrigger = true;
                    EditorUtility.SetDirty(collider);
                }
                foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    EditorUtility.SetDirty(body);
                }

                if (isPlayer)
                    SwordHitbox.Configure(target.gameObject, (PlayerWorker)worker);
                else
                    SwordHitbox.Configure(target.gameObject, (EnemyWorker)worker);
                EditorUtility.SetDirty(target.gameObject);
            }
            EditorUtility.SetDirty(worker);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Report();
    }

    private static void AppendReference(StringBuilder report, string label, SerializedProperty property)
    {
        if (property == null)
        {
            report.AppendLine($"  {label}=<missing property>");
            return;
        }
        var component = property.objectReferenceValue as Component;
        report.AppendLine($"  {label}={(component != null ? HierarchyPath(component.transform) : property.objectReferenceValue != null ? property.objectReferenceValue.name : "<null>")}");
    }

    private static string HierarchyPath(Transform value)
    {
        if (value == null) return "<null>";
        string path = value.name;
        while (value.parent != null)
        {
            value = value.parent;
            path = value.name + "/" + path;
        }
        return path;
    }
}
