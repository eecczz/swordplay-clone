using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CombatTriggerMigration
{
    [MenuItem("Tools/Swordplay/Convert Swords To Trigger Hitboxes")]
    public static void Apply()
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled) continue;
                Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                int changed = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (TargetMatching weapon in root.GetComponentsInChildren<TargetMatching>(true))
                    {
                        foreach (Collider value in weapon.GetComponentsInChildren<Collider>(true))
                        {
                            if (value.isTrigger) continue;
                            value.isTrigger = true;
                            EditorUtility.SetDirty(value);
                            changed++;
                        }

                        foreach (Rigidbody body in weapon.GetComponentsInChildren<Rigidbody>(true))
                        {
                            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                            EditorUtility.SetDirty(body);
                        }
                    }
                }

                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Debug.Log($"Combat trigger migration: {buildScene.path}, changed colliders: {changed}");
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            AssetDatabase.SaveAssets();
        }
    }
}
