using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Swordplay.Visuals.Editor
{
    /// <summary>One-time project import tuning for the soft, clean Resort-era texture response.</summary>
    public sealed class WiiStyleTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!IsStyleTexture(assetPath)) return;
            Configure((TextureImporter)assetImporter);
        }

        internal static bool IsStyleTexture(string path)
        {
            string p = path.Replace('\\', '/');
            string lower = p.ToLowerInvariant();
            return (lower.Contains("/wii_assets/") && lower.Contains("/textures/")) ||
                   (lower.Contains("/unity-chan!/") && lower.Contains("/art/"));
        }

        internal static void Configure(TextureImporter importer)
        {
            string lower = importer.assetPath.ToLowerInvariant();
            bool normal = lower.Contains("_nrm") || lower.Contains("normal") || lower.Contains("_nor.");
            bool data = normal || lower.Contains("_spec") || lower.Contains("alphamask") ||
                        lower.Contains("_mask") || lower.Contains("metallic") || lower.Contains("roughness") ||
                        lower.Contains("occlusion") || lower.Contains("_ao.") || lower.Contains("fo_rim") ||
                        lower.Contains("fo_skin") || lower.Contains("fo_cloth");

            // Albedo/emission artwork is authored for display and needs sRGB decoding in Linear space.
            // Normal/specular/mask maps contain numeric data and must never receive gamma conversion.
            importer.sRGBTexture = !data;
            if (normal) importer.textureType = TextureImporterType.NormalMap;
            else if (importer.textureType == TextureImporterType.NormalMap) importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.alphaIsTransparency = !data && importer.DoesSourceTextureHaveAlpha();
        }
    }

    [InitializeOnLoad]
    internal static class WiiStyleProjectSetup
    {
        private const string SetupVersion = "wii-resort-style-v3";
        private static readonly string SetupKey = "Swordplay." + SetupVersion + "." + Application.dataPath;
        private static readonly string CloudBackdropKey = "Swordplay.CloudBackdrop.v1." + Application.dataPath;
        private static readonly string EditModeStyleKey = "Swordplay.EditModeStyle.v4." + Application.dataPath;
        private const string CloudPackRoot = "Assets/DuNguyn/Clouds Pack v07";
        private const string OriginalSkyboxPath = "Assets/Materials/New Material 1.mat";

        static WiiStyleProjectSetup()
        {
            EditorApplication.delayCall += TryAutomaticSetup;
            EditorApplication.delayCall += TryInstallCloudBackdrop;
            EditorApplication.delayCall += EnsureEditModeStyle;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        [InitializeOnLoadMethod]
        private static void ScheduleEditModeStyle()
        {
            EditorApplication.delayCall += EnsureEditModeStyle;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.path.Replace('\\', '/') == "Assets/Scenes/SampleScene.unity")
                EditorApplication.delayCall += EnsureEditModeStyle;
        }

        private static void EnsureEditModeStyle()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += EnsureEditModeStyle;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureEditModeStyle;
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path.Replace('\\', '/') != "Assets/Scenes/SampleScene.unity") return;

            Swordplay.Visuals.WiiSwordplayStyle style = UnityEngine.Object.FindFirstObjectByType<Swordplay.Visuals.WiiSwordplayStyle>(FindObjectsInactive.Include);
            bool created = style == null;
            if (created)
            {
                var go = new GameObject("Wii Swordplay Visual Style");
                SceneManager.MoveGameObjectToScene(go, scene);
                style = go.AddComponent<Swordplay.Visuals.WiiSwordplayStyle>();
            }
            else if (!style.isActiveAndEnabled)
            {
                return;
            }

            Material originalSkybox = AssetDatabase.LoadAssetAtPath<Material>(OriginalSkyboxPath);
            if (originalSkybox != null) RenderSettings.skybox = originalSkybox;
            style.ApplyStyle();
            DynamicGI.UpdateEnvironment();
            SceneView.RepaintAll();

            if (created || !EditorPrefs.GetBool(EditModeStyleKey, false))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                EditorPrefs.SetBool(EditModeStyleKey, true);
            }
        }

        [MenuItem("Swordplay/Visuals/Apply Style To Scene View")]
        private static void ApplyStyleToSceneView()
        {
            EditorPrefs.SetBool(EditModeStyleKey, false);
            EnsureEditModeStyle();
        }

        [MenuItem("Swordplay/Visuals/Open My Assets")]
        private static void OpenMyAssetsFromMenu() => EditorApplication.ExecuteMenuItem("Window/My Assets");

        private static void TryInstallCloudBackdrop()
        {
            if (EditorPrefs.GetBool(CloudBackdropKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryInstallCloudBackdrop;
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{CloudPackRoot}/Prefabs/Clouds_v07_02.prefab") == null)
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path.Replace('\\', '/') != "Assets/Scenes/SampleScene.unity")
                return;

            InstallCloudBackdrop(scene, false);
            EditorPrefs.SetBool(CloudBackdropKey, true);
        }

        [MenuItem("Swordplay/Visuals/Install Stylized Cloud Backdrop")]
        private static void InstallCloudBackdropFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            InstallCloudBackdrop(scene, true);
        }

        private static void InstallCloudBackdrop(Scene scene, bool showResult)
        {
            GameObject existing = GameObject.Find("Wii Style Clouds (Stylized Vol 07)");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject("Wii Style Clouds (Stylized Vol 07)");
            SceneManager.MoveGameObjectToScene(root, scene);

            string[] names =
            {
                "Clouds_v07_02", "Clouds_v07_04", "Clouds_v07_06", "Clouds_v07_09",
                "Clouds_v07_11", "Clouds_v07_13", "Clouds_v07_15"
            };
            Vector3[] positions =
            {
                new(-15f, 9.5f, 24f), new(16f, 11.5f, 29f), new(-28f, 13f, 38f),
                new(30f, 8.5f, 18f), new(3f, 17f, 46f), new(-36f, 10f, 9f),
                new(38f, 15f, 43f)
            };
            float[] scales = { 5.2f, 6.1f, 7.5f, 4.8f, 8.2f, 5.8f, 7.1f };
            float[] yaw = { -18f, 24f, 8f, -32f, 17f, 45f, -11f };

            int installed = 0;
            for (int i = 0; i < names.Length; i++)
            {
                string path = $"{CloudPackRoot}/Prefabs/{names[i]}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var cloud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                cloud.name = $"Wii Cloud {i + 1:00} - {names[i]}";
                cloud.transform.SetParent(root.transform, false);
                cloud.transform.localPosition = positions[i];
                cloud.transform.localRotation = Quaternion.Euler(0f, yaw[i], 0f);
                cloud.transform.localScale = Vector3.one * scales[i];

                foreach (Renderer renderer in cloud.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                installed++;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>($"{CloudPackRoot}/Materials/Clouds_v07_M.mat");
            if (material != null)
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(1f, 0.985f, 0.965f, 1f));
                if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(1f, 0.985f, 0.965f, 1f));
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
                if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", new Color(0.10f, 0.115f, 0.14f, 1f));
                material.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"Wii Resort cloud backdrop installed: {installed} stylized cloud prefabs.");

            if (showResult)
                EditorUtility.DisplayDialog("Wii Resort Visuals", $"Placed {installed} distant stylized clouds.", "OK");
        }

        private static void TryAutomaticSetup()
        {
            if (EditorPrefs.GetBool(SetupKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutomaticSetup;
                return;
            }
            ApplyProjectSettings(false);
            EditorPrefs.SetBool(SetupKey, true);
        }

        [MenuItem("Swordplay/Visuals/Apply Wii Resort Project Settings")]
        private static void ApplyFromMenu() => ApplyProjectSettings(true);

        private static void ApplyProjectSettings(bool showResult)
        {
            int pipelineCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                asset.renderScale = 1.08f;
                asset.msaaSampleCount = 4;
                asset.shadowDistance = 150f;
                asset.shadowCascadeCount = 4;
                asset.supportsCameraDepthTexture = true;
                asset.supportsCameraOpaqueTexture = true;
                EditorUtility.SetDirty(asset);
                pipelineCount++;
            }

            string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[]
            {
                "Assets/Wii_Assets/wuhu-island-wii-sports-resort/textures",
                "Assets/unity-chan!"
            });

            int textureCount = 0;
            foreach (string guid in textureGuids.Distinct())
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!WiiStyleTexturePostprocessor.IsStyleTexture(path)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                WiiStyleTexturePostprocessor.Configure(importer);
                importer.SaveAndReimport();
                textureCount++;
            }
            AssetDatabase.SaveAssets();

            Debug.Log($"Wii Resort visual setup applied: {pipelineCount} URP assets, {textureCount} textures.");
            if (showResult)
                EditorUtility.DisplayDialog("Wii Resort Visuals", $"Updated {pipelineCount} render profiles and {textureCount} textures.", "OK");
        }
    }
}
