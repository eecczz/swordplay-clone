using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlaceBlueBrokenEdgeKnight
{
    // Keeps the imported showcase reproducible without changing its source prefab asset.
    private const string KnightPrefabPath = "Assets/Knight with Sword/Prefabs/Knight.prefab";
    private const string TexturePath = "Assets/Knight with Sword/Textures/T_Knight_SolidBlue.png";
    private const string MaterialPath = "Assets/Knight with Sword/Materials/M_Knight_BrokenEdge_Blue.mat";
    private const string InstanceName = "Knight_BrokenEdge_Blue_Showcase";

    [MenuItem("Tools/Swordplay/Place Blue Broken Edge Knight")]
    public static void PlaceKnight()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"Knight prefab was not found at '{KnightPrefabPath}'.");
            return;
        }

        PlayerWorker player = Object.FindFirstObjectByType<PlayerWorker>(FindObjectsInactive.Exclude);
        if (player == null)
        {
            Debug.LogError("No active PlayerWorker was found in the current scene.");
            return;
        }

        Material material = CreateOrUpdateBlueMaterial();
        if (material == null)
            return;

        GameObject instance = GameObject.Find(InstanceName);
        if (instance == null)
        {
            instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                Debug.LogError("Could not instantiate the Knight prefab.");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Place blue Broken Edge knight");
            instance.name = InstanceName;
        }
        else
        {
            Undo.RecordObject(instance.transform, "Reposition blue Broken Edge knight");
        }

        ApplyMaterial(instance, material);
        PlaceOnGroundInFrontOfPlayer(instance, player.transform);

        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);
        EditorSceneManager.MarkSceneDirty(instance.scene);
        EditorSceneManager.SaveScene(instance.scene);
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log($"Placed '{InstanceName}' in front of '{player.name}' with '{material.name}'.");
    }

    private static Material CreateOrUpdateBlueMaterial()
    {
        Shader shader = Shader.Find("Swordplay/Broken Edge Textured Surface");
        if (shader == null)
        {
            Debug.LogError("Shader 'Swordplay/Broken Edge Textured Surface' was not found.");
            return null;
        }

        Texture2D blueTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (blueTexture == null)
        {
            var generated = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "T_Knight_SolidBlue"
            };
            generated.SetPixel(0, 0, new Color(0.055f, 0.27f, 0.95f, 1f));
            generated.Apply(false, false);
            File.WriteAllBytes(TexturePath, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            blueTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "M_Knight_BrokenEdge_Blue" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseMap", blueTexture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Saturation", 1.15f);
        material.SetFloat("_TextureContrast", 1.05f);
        material.SetFloat("_ColorSteps", 5f);
        material.SetColor("_ShadowColor", new Color(0.015f, 0.055f, 0.22f, 1f));
        material.SetFloat("_ShadowStrength", 0.68f);
        material.SetColor("_LightTint", new Color(0.38f, 0.66f, 1f, 1f));
        material.SetFloat("_LightTintStrength", 0.18f);
        material.SetColor("_RimColor", new Color(0.25f, 0.95f, 1f, 1f));
        material.SetFloat("_RimStrength", 0.22f);
        material.SetColor("_OutlineColor", new Color(0.012f, 0.02f, 0.09f, 1f));
        material.SetFloat("_OutlineWidth", 0.012f);
        material.SetFloat("_PaperStrength", 0.025f);
        material.SetFloat("_Cutoff", 0.01f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void ApplyMaterial(GameObject instance, Material material)
    {
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                continue;

            Undo.RecordObject(renderer, "Apply blue Broken Edge material");
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = material;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
    }

    private static void PlaceOnGroundInFrontOfPlayer(GameObject instance, Transform player)
    {
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        Vector3 position = player.position + forward * 2.5f;
        if (Physics.Raycast(position + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 20f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y;
        else
            position.y = player.position.y;

        instance.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(Vector3.ProjectOnPlane(player.position - position, Vector3.up), Vector3.up));

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        instance.transform.position += Vector3.up * (position.y - bounds.min.y);
    }
}
