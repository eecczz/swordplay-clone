using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Swordplay.Visuals
{
    /// <summary>
    /// Applies the bright, soft and lightly stylised Wii Sports Resort look in Edit Mode and Play Mode.
    /// It deliberately keeps the original textures and only unifies lighting and response.
    /// </summary>
    [ExecuteAlways, DefaultExecutionOrder(-1000)]
    public sealed class WiiSwordplayStyle : MonoBehaviour
    {
        private static WiiSwordplayStyle instance;
        private readonly Dictionary<Renderer, MaterialPropertyBlock[]> originalBlocks = new();
        private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
        private readonly List<Material> generatedMaterials = new();
        private Volume styleVolume;

        [Header("Sun")]
        [SerializeField] private Color sunlight = new(1f, 0.95f, 0.86f, 1f);
        [SerializeField, Range(0f, 3f)] private float sunIntensity = 1.22f;
        [SerializeField] private Vector3 sunEulerAngles = new(42f, -32f, 0f);

        [Header("World")]
        [SerializeField] private Color skyTint = new(0.82f, 0.84f, 0.83f, 1f);
        [SerializeField] private Color horizonTint = new(0.69f, 0.72f, 0.68f, 1f);
        [SerializeField] private Color groundBounce = new(0.38f, 0.38f, 0.33f, 1f);
        [SerializeField] private Color fogColor = new(0.66f, 0.82f, 0.91f, 1f);
        [SerializeField, Min(1f)] private float fogStart = 90f;
        [SerializeField, Min(1f)] private float fogEnd = 360f;

        [Header("Material response")]
        [SerializeField, Range(0f, 1f)] private float environmentSmoothness = 0.22f;
        [SerializeField, Range(0f, 1f)] private float characterSmoothness = 0.34f;
        [SerializeField, Range(0f, 1f)] private float saturationLift = 0.08f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            WiiSwordplayStyle existing = FindFirstObjectByType<WiiSwordplayStyle>(FindObjectsInactive.Include);
            if (existing != null)
            {
                instance = existing;
                existing.ApplyStyle();
                return;
            }

            var go = new GameObject("Wii Swordplay Visual Style");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WiiSwordplayStyle>();
        }

        private void OnEnable()
        {
            if (Application.isPlaying && instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyStyle();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RestoreMaterialBlocks();
            RestoreCharacterMaterials();
            generatedMaterials.Clear();
            if (instance == this) instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyStyle();

        [ContextMenu("Apply Wii Swordplay Style")]
        public void ApplyStyle()
        {
            ConfigurePipeline();
            ConfigureEnvironment();
            ConfigureSun();
            ConfigureCameras();
            ConfigurePostProcessing();
            ConfigureCharacterShaders();
            ConfigureMaterials();
        }

        private static void ConfigurePipeline()
        {
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowDistance = 150f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
            QualitySettings.softParticles = true;

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = 1.08f;
                urp.msaaSampleCount = 4;
                urp.shadowDistance = 150f;
                urp.shadowCascadeCount = 4;
                urp.supportsCameraDepthTexture = true;
                urp.supportsCameraOpaqueTexture = true;
            }
        }

        private void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyTint;
            RenderSettings.ambientEquatorColor = horizonTint;
            RenderSettings.ambientGroundColor = groundBounce;
            // Keep the scene's original bright blue skybox visible, but decouple it from object lighting.
            // Neutral tri-light and weak reflections prevent the sky texture from dyeing skin and whites blue.
            RenderSettings.ambientIntensity = 0.76f;
            RenderSettings.reflectionIntensity = 0.08f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

        }

        private void ConfigureSun()
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional) { sun = light; break; }
                }
            }

            if (sun == null)
            {
                var go = new GameObject("Wii Style Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            RenderSettings.sun = sun;
            sun.color = sunlight;
            sun.intensity = sunIntensity;
            sun.transform.rotation = Quaternion.Euler(sunEulerAngles);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.68f;
            sun.shadowBias = 0.055f;
            sun.shadowNormalBias = 0.32f;
        }

        private static void ConfigureCameras()
        {
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                camera.allowHDR = true;
                camera.backgroundColor = new Color(0.53f, 0.78f, 0.92f, 1f);
                if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                {
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                    data.renderPostProcessing = true;
                }
            }
        }

        private void ConfigurePostProcessing()
        {
            if (styleVolume == null) styleVolume = GetComponent<Volume>();
            if (styleVolume == null)
            {
                styleVolume = gameObject.AddComponent<Volume>();
            }
            styleVolume.isGlobal = true;
            styleVolume.priority = 100f;

            VolumeProfile profile = styleVolume.profile;
            if (!profile.TryGet(out ColorAdjustments color)) color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.08f);
            color.contrast.Override(2f);
            color.saturation.Override(14f);
            color.colorFilter.Override(Color.white);

            if (!profile.TryGet(out WhiteBalance balance)) balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(2f);
            balance.tint.Override(0f);

            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.08f);
            bloom.intensity.Override(0.11f);
            bloom.scatter.Override(0.62f);

            if (!profile.TryGet(out Tonemapping tone)) tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.None);

            if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>(true);
            vignette.color.Override(new Color(0.16f, 0.25f, 0.28f, 1f));
            vignette.intensity.Override(0.015f);
            vignette.smoothness.Override(0.7f);

        }

        private void ConfigureMaterials()
        {
            RestoreMaterialBlocks();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                var materials = renderer.sharedMaterials;
                var blocks = new MaterialPropertyBlock[materials.Length];
                bool character = IsCharacter(renderer.transform);

                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;
                    var original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original, i);
                    blocks[i] = original;
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, i);

                    if (material.HasProperty("_BaseColor"))
                        block.SetColor("_BaseColor", LiftColor(material.GetColor("_BaseColor")));
                    else if (material.HasProperty("_Color"))
                        block.SetColor("_Color", LiftColor(material.GetColor("_Color")));

                    if (material.HasProperty("_Metallic")) block.SetFloat("_Metallic", 0f);
                    if (material.HasProperty("_Smoothness"))
                        block.SetFloat("_Smoothness", character ? characterSmoothness : environmentSmoothness);
                    if (material.HasProperty("_SpecularHighlights")) block.SetFloat("_SpecularHighlights", 1f);
                    renderer.SetPropertyBlock(block, i);
                }

                originalBlocks[renderer] = blocks;
            }
        }

        private void ConfigureCharacterShaders()
        {
            RestoreCharacterMaterials();
            Shader characterShader = Shader.Find("Swordplay/Wii Soft Character");
            if (characterShader == null) return;

            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsCharacter(renderer.transform) || renderer is ParticleSystemRenderer || renderer is TrailRenderer) continue;
                Material[] originals = renderer.sharedMaterials;
                Material[] replacements = new Material[originals.Length];
                bool changed = false;

                for (int i = 0; i < originals.Length; i++)
                {
                    Material source = originals[i];
                    replacements[i] = source;
                    if (source == null || source.renderQueue >= 3000 || source.GetTag("RenderType", false) == "Transparent") continue;

                    Material styled = GetOrCreateCharacterMaterial(source, characterShader);
                    Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") :
                                      source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
                    Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                                  source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                    if (texture != null) styled.SetTexture("_BaseMap", texture);
                    styled.SetColor("_BaseColor", color);
                    styled.SetColor("_ShadowTint", new Color(0.58f, 0.68f, 0.70f, 1f));
                    styled.SetColor("_HighlightColor", new Color(1f, 0.985f, 0.94f, 1f));
                    styled.SetFloat("_HighlightSize", IsHead(renderer.transform) ? 0.32f : 0.48f);
                    styled.SetFloat("_HighlightStrength", IsHead(renderer.transform) ? 1.15f : 0.62f);
                    styled.SetFloat("_RimStrength", 0.18f);
                    replacements[i] = styled;
                    if (!IsPersistentAsset(styled)) generatedMaterials.Add(styled);
                    changed = true;
                }

                if (!changed) continue;
                originalMaterials[renderer] = originals;
                renderer.sharedMaterials = replacements;
            }
        }

        private Color LiftColor(Color source)
        {
            Color.RGBToHSV(source, out float h, out float s, out float v);
            s = Mathf.Clamp01(s + saturationLift);
            v = Mathf.Lerp(v, Mathf.Sqrt(v), 0.14f);
            var result = Color.HSVToRGB(h, s, v);
            result.a = source.a;
            return result;
        }

        private static bool IsCharacter(Transform target)
        {
            for (var current = target; current != null; current = current.parent)
            {
                string n = current.name.ToLowerInvariant();
                if (n.Contains("player") || n.Contains("enemy") || n.Contains("unitychan") || n.Contains("character"))
                    return true;
            }
            return false;
        }

        private static bool IsHead(Transform target)
        {
            for (var current = target; current != null; current = current.parent)
            {
                string n = current.name.ToLowerInvariant();
                if (n.Contains("head") || n.Contains("face") || n.Contains("hair")) return true;
            }
            return false;
        }

        private void RestoreCharacterMaterials()
        {
            foreach (var pair in originalMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            originalMaterials.Clear();

            for (int i = generatedMaterials.Count - 1; i >= 0; i--)
            {
                Material material = generatedMaterials[i];
                if (material != null && !IsPersistentAsset(material)) DestroyOwnedObject(material);
            }
            generatedMaterials.Clear();
        }

        private void RestoreMaterialBlocks()
        {
            foreach (var pair in originalBlocks)
            {
                if (pair.Key == null) continue;
                for (int i = 0; i < pair.Value.Length; i++)
                    pair.Key.SetPropertyBlock(pair.Value[i], i);
            }
            originalBlocks.Clear();
        }

        private static Material GetOrCreateCharacterMaterial(Material source, Shader shader)
        {
            if (source.shader == shader) return source;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                const string folder = "Assets/Settings/WiiStyleMaterials";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Settings", "WiiStyleMaterials");

                string guid;
                long localId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out guid, out localId) || string.IsNullOrEmpty(guid))
                {
                    guid = Hash128.Compute(source.name + source.shader.name).ToString();
                    localId = 0;
                }

                string path = $"{folder}/{guid}_{localId}.mat";
                Material asset = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (asset == null)
                {
                    asset = new Material(shader) { name = source.name + " (Wii Soft)" };
                    AssetDatabase.CreateAsset(asset, path);
                }
                else if (asset.shader != shader) asset.shader = shader;
                EditorUtility.SetDirty(asset);
                return asset;
            }
#endif
            return new Material(shader) { name = source.name + " (Wii Soft)" };
        }

        private static bool IsPersistentAsset(Object target)
        {
#if UNITY_EDITOR
            return target != null && EditorUtility.IsPersistent(target);
#else
            return false;
#endif
        }

        private static void DestroyOwnedObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
#if UNITY_EDITOR
            else DestroyImmediate(target);
#endif
        }
    }
}
