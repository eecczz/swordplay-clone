#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Swordplay.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BrokenEdgeSceneBuilder
{
    private const string SourceScene = "Assets/Scenes/SampleScene.unity";
    private const string TargetScene = "Assets/Scenes/BrokenEdgeLaunch.unity";
    private const string SettingsFolder = "Assets/Settings/BrokenEdge";
    private const string MeshPath = SettingsFolder + "/BrokenEdgeArenaTile.asset";
    private const string ArenaMaterialPath = SettingsFolder + "/BrokenEdgeArena.mat";
    private const string SkyMaterialPath = SettingsFolder + "/BrokenEdgeSky.mat";
    private const string VolumeProfilePath = SettingsFolder + "/BrokenEdgeVolumeProfile.asset";

    static BrokenEdgeSceneBuilder()
    {
        EditorApplication.delayCall += BuildOnce;
    }

    [MenuItem("Swordplay/Build Broken Edge Launch Scene")]
    public static void BuildFromMenu() => BuildScene(true);

    private static void BuildOnce()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += BuildOnce;
            return;
        }

        bool targetExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScene) != null;
        bool generatedAssetsExist = AssetDatabase.LoadAssetAtPath<Material>(ArenaMaterialPath) != null;
        if (targetExists && generatedAssetsExist) return;
        BuildScene(targetExists);
    }

    private static void BuildScene(bool rebuild)
    {
        try
        {
            if (!EditorSceneManager.SaveOpenScenes())
                throw new InvalidOperationException("Open scenes could not be saved before cloning.");

            SceneAsset existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScene);
            if (existing == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScene, TargetScene))
                    throw new InvalidOperationException("Unity could not copy SampleScene.");
            }
            else if (!rebuild)
            {
                return;
            }

            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);

            DisableWiiEnvironment(scene);
            EnsureAssetFolders();

            Material arenaMaterial = CreateOrUpdateArenaMaterial();
            Material skyMaterial = CreateOrUpdateSkyMaterial();
            VolumeProfile volumeProfile = CreateOrUpdateVolumeProfile();
            Mesh arenaMesh = CreateOrUpdateArenaMesh();

            RenderSettings.skybox = skyMaterial;
            CreateStyleRoot(scene, volumeProfile);
            CenterFightersOnArena();
            // The imported dungeon layout is now the playable floor.
            // Do not recreate the old prototype arena tile when rebuilding the scene.
            // CreateArena(scene, arenaMesh, arenaMaterial);
            PutSceneFirstInBuildSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.LookAt(new Vector3(0f, 1.2f, 0f), Quaternion.Euler(24f, 210f, 0f), 18f);
                view.Repaint();
            }

            Debug.Log("[Broken Edge] Created BrokenEdgeLaunch scene with Wii environment disabled and fighters grounded on the arena tile.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void DisableWiiEnvironment(Scene scene)
    {
        string[] exactNames =
        {
            "wuhu",
            "Wii Swordplay Visual Style",
            "Wii Style Clouds (Stylized Vol 07)",
            "Wii Style Sun"
        };

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!exactNames.Any(name => string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                transform.gameObject.SetActive(false);
                EditorUtility.SetDirty(transform.gameObject);
            }
        }
    }

    private static void EnsureAssetFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder(SettingsFolder)) AssetDatabase.CreateFolder("Assets/Settings", "BrokenEdge");
    }

    private static Material CreateOrUpdateArenaMaterial()
    {
        Shader shader = Shader.Find("Swordplay/Broken Edge Arena");
        if (shader == null) throw new InvalidOperationException("Broken Edge arena shader is unavailable.");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(ArenaMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Broken Edge Arena" };
            AssetDatabase.CreateAsset(material, ArenaMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetColor("_BaseColor", new Color(0.49f, 0.53f, 0.61f, 1f));
        material.SetColor("_ShadowColor", new Color(0.14f, 0.17f, 0.25f, 1f));
        material.SetColor("_HighlightColor", new Color(0.86f, 0.65f, 0.54f, 1f));
        material.SetColor("_RimColor", new Color(0.31f, 0.86f, 0.82f, 1f));
        material.SetFloat("_RimStrength", 0.18f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateOrUpdateSkyMaterial()
    {
        Shader shader = Shader.Find("Swordplay/Broken Edge Sky");
        if (shader == null) throw new InvalidOperationException("Broken Edge sky shader is unavailable.");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Broken Edge Pastel Sky" };
            AssetDatabase.CreateAsset(material, SkyMaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetColor("_TopColor", new Color(0.25f, 0.30f, 0.51f, 1f));
        material.SetColor("_HorizonColor", new Color(0.91f, 0.64f, 0.63f, 1f));
        material.SetColor("_BottomColor", new Color(0.24f, 0.22f, 0.33f, 1f));
        material.SetFloat("_HorizonSharpness", 1.65f);
        material.SetColor("_SunColor", new Color(1f, 0.78f, 0.56f, 1f));
        material.SetFloat("_SunStrength", 0.18f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static VolumeProfile CreateOrUpdateVolumeProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Broken Edge Volume Profile";
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }

        ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
        color.active = true;
        color.postExposure.Override(0.02f);
        color.contrast.Override(23f);
        color.saturation.Override(-9f);
        color.colorFilter.Override(new Color(1f, 0.94f, 0.97f, 1f));

        WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
        whiteBalance.active = true;
        whiteBalance.temperature.Override(-7f);
        whiteBalance.tint.Override(8f);

        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.active = true;
        bloom.threshold.Override(0.92f);
        bloom.intensity.Override(0.24f);
        bloom.scatter.Override(0.48f);

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.active = true;
        vignette.color.Override(new Color(0.09f, 0.10f, 0.18f, 1f));
        vignette.intensity.Override(0.13f);
        vignette.smoothness.Override(0.76f);

        Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
        tonemapping.active = true;
        tonemapping.mode.Override(TonemappingMode.Neutral);

        FilmGrain grain = GetOrAdd<FilmGrain>(profile);
        grain.active = true;
        grain.type.Override(FilmGrainLookup.Thin1);
        grain.intensity.Override(0.075f);
        grain.response.Override(0.62f);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;
        return profile.Add<T>(true);
    }

    private static Mesh CreateOrUpdateArenaMesh()
    {
        Mesh oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (oldMesh != null) AssetDatabase.DeleteAsset(MeshPath);

        const int segments = 20;
        float[] radii = { 0f, 4.25f, 8.75f, 14f };
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var triangles = new List<int>();

        Color[] palette =
        {
            new(0.90f, 0.80f, 0.78f, 1f),
            new(0.69f, 0.78f, 0.82f, 1f),
            new(0.78f, 0.70f, 0.78f, 1f),
            new(0.63f, 0.72f, 0.72f, 1f)
        };

        for (int ring = 0; ring < radii.Length - 1; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                float angle0 = segment * Mathf.PI * 2f / segments;
                float angle1 = (segment + 1) * Mathf.PI * 2f / segments;
                float irregular0 = 1f + Mathf.Sin(segment * 3.71f + ring) * 0.018f;
                float irregular1 = 1f + Mathf.Sin((segment + 1) * 3.71f + ring) * 0.018f;
                float inner = radii[ring];
                float outer0 = radii[ring + 1] * irregular0;
                float outer1 = radii[ring + 1] * irregular1;
                Vector3 i0 = new(Mathf.Cos(angle0) * inner, 0f, Mathf.Sin(angle0) * inner);
                Vector3 i1 = new(Mathf.Cos(angle1) * inner, 0f, Mathf.Sin(angle1) * inner);
                Vector3 o0 = new(Mathf.Cos(angle0) * outer0, 0f, Mathf.Sin(angle0) * outer0);
                Vector3 o1 = new(Mathf.Cos(angle1) * outer1, 0f, Mathf.Sin(angle1) * outer1);

                int start = vertices.Count;
                vertices.Add(i0);
                vertices.Add(o0);
                vertices.Add(o1);
                vertices.Add(i1);
                normals.AddRange(Enumerable.Repeat(Vector3.up, 4));
                Color tileColor = palette[(ring * 3 + segment) % palette.Length];
                colors.AddRange(Enumerable.Repeat(tileColor, 4));
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
        }

        for (int segment = 0; segment < segments; segment++)
        {
            float angle0 = segment * Mathf.PI * 2f / segments;
            float angle1 = (segment + 1) * Mathf.PI * 2f / segments;
            float radius0 = radii[^1] * (1f + Mathf.Sin(segment * 3.71f + radii.Length - 2) * 0.018f);
            float radius1 = radii[^1] * (1f + Mathf.Sin((segment + 1) * 3.71f + radii.Length - 2) * 0.018f);
            Vector3 top0 = new(Mathf.Cos(angle0) * radius0, 0f, Mathf.Sin(angle0) * radius0);
            Vector3 top1 = new(Mathf.Cos(angle1) * radius1, 0f, Mathf.Sin(angle1) * radius1);
            Vector3 bottom0 = top0 + Vector3.down * 0.42f;
            Vector3 bottom1 = top1 + Vector3.down * 0.42f;
            Vector3 normal = Vector3.Normalize(new Vector3(Mathf.Cos((angle0 + angle1) * 0.5f), 0f, Mathf.Sin((angle0 + angle1) * 0.5f)));

            int start = vertices.Count;
            vertices.Add(top0);
            vertices.Add(bottom0);
            vertices.Add(bottom1);
            vertices.Add(top1);
            normals.AddRange(Enumerable.Repeat(normal, 4));
            colors.AddRange(Enumerable.Repeat(new Color(0.31f, 0.35f, 0.44f, 1f), 4));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        var mesh = new Mesh { name = "Broken Edge Irregular Duel Tile" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, MeshPath);
        return mesh;
    }

    private static void CreateStyleRoot(Scene scene, VolumeProfile profile)
    {
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Broken Edge Launch Style");
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        var root = new GameObject("Broken Edge Launch Style");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<BrokenEdgeStyle>();
        Volume volume = root.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 200f;
        volume.sharedProfile = profile;
    }

    private static void CreateArena(Scene scene, Mesh mesh, Material material)
    {
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Broken Edge Arena Tile");
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        var arena = new GameObject("Broken Edge Arena Tile");
        SceneManager.MoveGameObjectToScene(arena, scene);
        arena.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 9f, 0f));
        arena.AddComponent<MeshFilter>().sharedMesh = mesh;
        arena.AddComponent<MeshRenderer>().sharedMaterial = material;
        arena.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private static void CenterFightersOnArena()
    {
        var fighters = new List<Transform>();
        fighters.AddRange(UnityEngine.Object.FindObjectsByType<PlayerWorker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(worker => worker.gameObject.activeInHierarchy)
            .Select(worker => worker.transform));
        fighters.AddRange(UnityEngine.Object.FindObjectsByType<EnemyWorker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(worker => worker.gameObject.activeInHierarchy)
            .Select(worker => worker.transform));

        fighters = fighters.Distinct().ToList();
        if (fighters.Count == 0) return;

        Vector3 center = Vector3.zero;
        foreach (Transform fighter in fighters) center += fighter.position;
        center /= fighters.Count;
        Vector3 horizontalShift = new(-center.x, 0f, -center.z);

        foreach (Transform fighter in fighters)
        {
            Undo.RecordObject(fighter, "Center fighter on Broken Edge arena");
            fighter.position += horizontalShift;
            float footY = FindFootY(fighter.gameObject);
            fighter.position += Vector3.up * (0.035f - footY);
            EditorUtility.SetDirty(fighter);
        }
    }

    private static float FindFootY(GameObject fighter)
    {
        Animator animator = fighter.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
        {
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (left != null || right != null)
            {
                float leftY = left != null ? left.position.y : float.PositiveInfinity;
                float rightY = right != null ? right.position.y : float.PositiveInfinity;
                return Mathf.Min(leftY, rightY);
            }
        }

        Collider[] colliders = fighter.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0) return colliders.Min(collider => collider.bounds.min.y);

        Renderer[] renderers = fighter.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0) return renderers.Min(renderer => renderer.bounds.min.y);
        return fighter.transform.position.y;
    }

    private static void PutSceneFirstInBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        scenes.RemoveAll(scene => string.Equals(scene.path, TargetScene, StringComparison.OrdinalIgnoreCase));
        scenes.Insert(0, new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
