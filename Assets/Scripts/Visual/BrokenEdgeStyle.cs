using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Swordplay.Visuals
{
    /// <summary>
    /// Keeps the Broken Edge-inspired palette visible in both Scene view and Play mode.
    /// The scene builder serializes the sky and volume assets; this component maintains
    /// lighting and camera settings without touching the original Wii scene.
    /// </summary>
    [ExecuteAlways, DefaultExecutionOrder(-900)]
    public sealed class BrokenEdgeStyle : MonoBehaviour
    {
        private const int CurrentStyleVersion = 7;
        private static BrokenEdgeStyle instance;
        private static readonly Color[] SkyCycleColors =
        {
            new(0.96f, 0.78f, 0.24f, 1f), // yellow
            new(0.30f, 0.78f, 0.38f, 1f), // green
            new(0.25f, 0.52f, 0.94f, 1f), // blue
            new(1.00f, 0.43f, 0.15f, 1f), // orange
            new(0.88f, 0.18f, 0.22f, 1f)  // red
        };

        [Header("Pastel world")]
        [SerializeField] private Color skyAmbient = new(0.76f, 0.84f, 0.68f, 1f);
        [SerializeField] private Color horizonAmbient = new(0.88f, 0.76f, 0.77f, 1f);
        [SerializeField] private Color groundAmbient = new(0.58f, 0.68f, 0.62f, 1f);
        [SerializeField, Range(0f, 2f)] private float ambientIntensity = 1.2f;

        [Header("Graphic sun")]
        [SerializeField] private Color sunColor = new(1f, 0.82f, 0.76f, 1f);
        [SerializeField, Range(0f, 3f)] private float sunIntensity = 1.18f;
        [SerializeField] private Vector3 sunEulerAngles = new(48f, -38f, 0f);

        [Header("Depth")]
        [SerializeField] private Color fogColor = new(0.72f, 0.82f, 0.68f, 1f);
        [SerializeField, Min(0f)] private float fogStart = 50f;
        [SerializeField, Min(1f)] private float fogEnd = 180f;

        [Header("Generated arena")]
        [SerializeField] private Shader arenaShader;
        [SerializeField] private Shader skyShader;
        [SerializeField] private Material skyMaterial;
        [SerializeField, Min(4f)] private float arenaRadius = 14f;

        [Header("Runtime sky cycle")]
        [SerializeField, Min(1f)] private float skyColorStepDuration = 6f;

        [Header("Original texture simplification")]
        [SerializeField] private bool applyTexturedToonShader = true;
        [SerializeField] private Shader texturedSurfaceShader;
        [SerializeField, Range(0f, 1.5f)] private float retainedSaturation = 1.05f;
        [SerializeField, Range(2f, 8f)] private float textureColorSteps = 5f;
        [SerializeField, Range(0.5f, 2f)] private float textureContrast = 0.92f;
        [SerializeField] private Color textureShadowColor = new(0.20f, 0.33f, 0.38f, 1f);
        [SerializeField, Range(0f, 1f)] private float textureShadowStrength = 0.58f;
        [SerializeField, HideInInspector] private int styleVersion;

        private Light sceneSun;
        private GameObject generatedEnvironment;
        private Material generatedArenaMaterial;
        private Material generatedSkyMaterial;
        private Mesh generatedArenaMesh;
        private VolumeProfile generatedVolumeProfile;
        private bool skyCycleActive;
        private float skyCycleStartTime;
        private Color skyCycleInitialColor;
        private readonly Dictionary<Renderer, Material[]> originalStyledMaterials = new();
        private readonly List<Material> generatedStyledMaterials = new();
        private readonly List<Camera> runtimeCameras = new();
#if UNITY_EDITOR
        [System.NonSerialized] private bool validationQueued;
#endif

        private void OnEnable()
        {
            instance = this;
            UpgradeSerializedSettings();
            ApplyStyle();
        }

        private void OnDisable()
        {
            if (instance == this) instance = null;
            RestoreTexturedMaterials();
            DestroyOwned(generatedEnvironment);
            DestroyOwned(generatedArenaMaterial);
            DestroyOwned(generatedSkyMaterial);
            DestroyOwned(generatedArenaMesh);
            DestroyOwned(generatedVolumeProfile);
            generatedEnvironment = null;
            generatedArenaMaterial = null;
            generatedSkyMaterial = null;
            generatedArenaMesh = null;
            generatedVolumeProfile = null;
        }

        private void Update()
        {
            if (!Application.isPlaying || !skyCycleActive) return;
            float phase = (Time.time - skyCycleStartTime) / Mathf.Max(1f, skyColorStepDuration);
            if (phase < 1f)
            {
                float initialBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase));
                ApplyRuntimeSkyColor(Color.Lerp(skyCycleInitialColor, SkyCycleColors[0], initialBlend));
                return;
            }

            float cyclePhase = phase - 1f;
            int current = Mathf.FloorToInt(cyclePhase) % SkyCycleColors.Length;
            int next = (current + 1) % SkyCycleColors.Length;
            float blend = Mathf.SmoothStep(0f, 1f, cyclePhase - Mathf.Floor(cyclePhase));
            ApplyRuntimeSkyColor(Color.Lerp(SkyCycleColors[current], SkyCycleColors[next], blend));
        }

        public static void BeginGlobalSkyCycle()
        {
            if (instance == null) return;
            instance.ConfigureSky();
            instance.runtimeCameras.Clear();
            instance.runtimeCameras.AddRange(FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None));
            Material activeSky = RenderSettings.skybox;
            instance.skyCycleInitialColor = activeSky != null && activeSky.HasProperty("_HorizonColor")
                ? activeSky.GetColor("_HorizonColor")
                : RenderSettings.ambientSkyColor;
            instance.skyCycleStartTime = Time.time;
            instance.skyCycleActive = true;
            instance.ApplyRuntimeSkyColor(instance.skyCycleInitialColor);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (validationQueued) return;
            validationQueued = true;
            EditorApplication.delayCall += ApplyValidatedStyle;
        }

        private void ApplyValidatedStyle()
        {
            validationQueued = false;
            if (this == null || !isActiveAndEnabled || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            ApplyStyle();
            SceneView.RepaintAll();
        }

#endif

        [ContextMenu("Apply Broken Edge Style")]
        public void ApplyStyle()
        {
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowDistance = 95f;
            QualitySettings.shadowCascades = 2;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = skyAmbient;
            RenderSettings.ambientEquatorColor = horizonAmbient;
            RenderSettings.ambientGroundColor = groundAmbient;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = 0.32f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

            DisableWiiEnvironment();
            ConfigureSun();
            ConfigureCameras();
            ConfigureGeneratedEnvironment();
            // Authored renderers use persistent texture-preserving materials installed
            // in the scene. Do not rescan every renderer or allocate replacement
            // materials when play mode starts.
            RestoreTexturedMaterials();
        }

        private void UpgradeSerializedSettings()
        {
            if (styleVersion >= CurrentStyleVersion) return;

            skyAmbient = new Color(0.76f, 0.84f, 0.68f, 1f);
            horizonAmbient = new Color(0.88f, 0.76f, 0.77f, 1f);
            groundAmbient = new Color(0.58f, 0.68f, 0.62f, 1f);
            ambientIntensity = 1.2f;
            sunColor = new Color(1f, 0.82f, 0.76f, 1f);
            sunIntensity = 1.18f;
            fogColor = new Color(0.72f, 0.82f, 0.68f, 1f);
            fogStart = 50f;
            fogEnd = 180f;
            applyTexturedToonShader = true;
            retainedSaturation = 1.05f;
            textureColorSteps = 5f;
            textureContrast = 0.92f;
            textureShadowColor = new Color(0.20f, 0.33f, 0.38f, 1f);
            textureShadowStrength = 0.58f;
            styleVersion = CurrentStyleVersion;

#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(this);
#endif
        }

        private void ConfigureTexturedMaterials()
        {
            RestoreTexturedMaterials();
            Shader shader = texturedSurfaceShader != null
                ? texturedSurfaceShader
                : Shader.Find("Swordplay/Broken Edge Textured Surface");
            if (shader == null) return;

            var styledBySource = new Dictionary<Material, Material>();
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

                Material[] originals = renderer.sharedMaterials;
                Material[] replacements = new Material[originals.Length];
                bool changed = false;
                for (int i = 0; i < originals.Length; i++)
                {
                    Material source = originals[i];
                    replacements[i] = source;
                    if (source == null || ShouldKeepExistingShader(source.shader)) continue;

                    if (!styledBySource.TryGetValue(source, out Material styled))
                    {
                        styled = CreateTexturedMaterial(source, shader);
                        styledBySource.Add(source, styled);
                        generatedStyledMaterials.Add(styled);
                    }
                    replacements[i] = styled;
                    changed = true;
                }

                if (!changed) continue;
                originalStyledMaterials[renderer] = originals;
                renderer.sharedMaterials = replacements;
            }
        }

        private Material CreateTexturedMaterial(Material source, Shader shader)
        {
            var styled = new Material(shader)
            {
                name = source.name + " (Broken Edge Textured)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = source.enableInstancing,
                doubleSidedGI = source.doubleSidedGI
            };

            string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" :
                source.HasProperty("_MainTex") ? "_MainTex" : null;
            if (textureProperty != null)
            {
                styled.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                styled.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                styled.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
            }

            Color baseColor = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            styled.SetColor("_BaseColor", baseColor);
            styled.SetFloat("_Saturation", retainedSaturation);
            styled.SetFloat("_ColorSteps", textureColorSteps);
            styled.SetFloat("_TextureContrast", textureContrast);
            styled.SetColor("_ShadowColor", textureShadowColor);
            styled.SetFloat("_ShadowStrength", textureShadowStrength);
            styled.SetColor("_LightTint", new Color(1f, 0.67f, 0.63f, 1f));
            styled.SetFloat("_LightTintStrength", 0.14f);
            styled.SetColor("_RimColor", new Color(0.18f, 0.96f, 0.77f, 1f));
            styled.SetFloat("_RimStrength", 0.12f);
            styled.SetColor("_OutlineColor", new Color(0.18f, 0.06f, 0.14f, 0.95f));
            styled.SetFloat("_OutlineWidth", 0.012f);
            styled.SetFloat("_PaperStrength", 0.025f);

            bool transparent = IsTransparent(source);
            bool cutout = IsCutout(source);
            float cutoff = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : cutout ? 0.5f : 0.01f;
            styled.SetFloat("_Cutoff", cutoff);
            styled.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f);
            styled.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            styled.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            styled.SetFloat("_ZWrite", transparent ? 0f : 1f);
            styled.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            styled.renderQueue = transparent ? (int)RenderQueue.Transparent : cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            styled.SetShaderPassEnabled("ShadowCaster", !transparent);
            styled.SetShaderPassEnabled("DepthOnly", !transparent);
            return styled;
        }

        private static bool ShouldKeepExistingShader(Shader shader)
        {
            if (shader == null) return false;
            string shaderName = shader.name;
            return shaderName == "Swordplay/Broken Edge Textured Surface" ||
                   shaderName == "Swordplay/Broken Edge Dungeon" ||
                   shaderName == "Swordplay/Broken Edge Blade Aura" ||
                   shaderName == "Swordplay/Broken Edge Blade Trail" ||
                   shaderName == "Swordplay/Broken Edge Sky" ||
                   shaderName == "Swordplay/Broken Edge Arena";
        }

        private static bool IsTransparent(Material material)
        {
            return material.renderQueue >= (int)RenderQueue.Transparent ||
                   (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) ||
                   string.Equals(material.GetTag("RenderType", false), "Transparent", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCutout(Material material)
        {
            return material.IsKeywordEnabled("_ALPHATEST_ON") ||
                   (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f) ||
                   material.renderQueue == (int)RenderQueue.AlphaTest ||
                   string.Equals(material.GetTag("RenderType", false), "TransparentCutout", StringComparison.OrdinalIgnoreCase);
        }

        private void RestoreTexturedMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> pair in originalStyledMaterials)
                if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            originalStyledMaterials.Clear();

            for (int i = generatedStyledMaterials.Count - 1; i >= 0; i--)
                DestroyOwned(generatedStyledMaterials[i]);
            generatedStyledMaterials.Clear();
        }

        private void ConfigureSun()
        {
            sceneSun = RenderSettings.sun;
            if (sceneSun == null)
            {
                foreach (Light candidate in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (candidate.type != LightType.Directional) continue;
                    sceneSun = candidate;
                    break;
                }
            }

            if (sceneSun == null)
            {
                var sunObject = new GameObject("Broken Edge Sun");
                sceneSun = sunObject.AddComponent<Light>();
                sceneSun.type = LightType.Directional;
            }

            RenderSettings.sun = sceneSun;
            sceneSun.color = sunColor;
            sceneSun.intensity = sunIntensity;
            sceneSun.transform.rotation = Quaternion.Euler(sunEulerAngles);
            sceneSun.shadows = LightShadows.Hard;
            sceneSun.shadowStrength = 0.38f;
            sceneSun.shadowBias = 0.045f;
            sceneSun.shadowNormalBias = 0.28f;
        }

        private static void ConfigureCameras()
        {
            Color fallback = new(0.77f, 0.76f, 0.89f, 1f);
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                camera.allowHDR = true;
                camera.backgroundColor = fallback;
                if (!camera.TryGetComponent(out UniversalAdditionalCameraData data)) continue;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderPostProcessing = true;
            }
        }

        private void DisableWiiEnvironment()
        {
            string[] exactNames =
            {
                "wuhu",
                "Wii Swordplay Visual Style",
                "Wii Style Clouds (Stylized Vol 07)",
                "Wii Style Sun"
            };

            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate == transform) continue;
                if (exactNames.Any(name => string.Equals(candidate.name, name, StringComparison.OrdinalIgnoreCase)))
                    candidate.gameObject.SetActive(false);
            }
        }

        private void ConfigureGeneratedEnvironment()
        {
            if (generatedEnvironment == null)
            {
                Transform existing = transform.Find("Broken Edge Generated Environment");
                generatedEnvironment = existing != null ? existing.gameObject : new GameObject("Broken Edge Generated Environment");
                generatedEnvironment.transform.SetParent(transform, false);
                generatedEnvironment.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }

            ConfigureSky();
            ConfigureVolume();
            // The authored dungeon floor replaces the prototype arena tile.
            // Keep this disabled so ExecuteAlways/Play Mode cannot recreate it.
            // ConfigureArena();
        }

        private void ConfigureSky()
        {
            // Prefer the persistent scene asset. A Shader.Find-only material can be stripped
            // from a standalone build because neither the scene nor GraphicsSettings owns it.
            if (skyMaterial != null)
            {
                if (Application.isPlaying)
                {
                    if (generatedSkyMaterial == null || generatedSkyMaterial.shader != skyMaterial.shader)
                    {
                        DestroyOwned(generatedSkyMaterial);
                        generatedSkyMaterial = new Material(skyMaterial)
                        {
                            name = skyMaterial.name + " (Runtime)",
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }
                    RenderSettings.skybox = generatedSkyMaterial;
                }
                else
                    RenderSettings.skybox = skyMaterial;
                return;
            }

            Shader shader = skyShader != null ? skyShader : Shader.Find("Swordplay/Broken Edge Sky");
            if (shader == null) return;
            if (generatedSkyMaterial == null || generatedSkyMaterial.shader != shader)
            {
                DestroyOwned(generatedSkyMaterial);
                generatedSkyMaterial = new Material(shader)
                {
                    name = "Broken Edge Pastel Sky (Generated)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            generatedSkyMaterial.SetColor("_TopColor", new Color(0.48f, 0.66f, 0.42f, 1f));
            generatedSkyMaterial.SetColor("_HorizonColor", new Color(0.78f, 0.86f, 0.65f, 1f));
            generatedSkyMaterial.SetColor("_BottomColor", new Color(0.94f, 0.69f, 0.67f, 1f));
            generatedSkyMaterial.SetFloat("_HorizonSharpness", 1.15f);
            generatedSkyMaterial.SetColor("_SunColor", new Color(1f, 0.90f, 0.73f, 1f));
            generatedSkyMaterial.SetFloat("_SunStrength", 0.18f);
            RenderSettings.skybox = generatedSkyMaterial;
        }

        private void ApplyRuntimeSkyColor(Color horizon)
        {
            Color top = Color.Lerp(horizon, new Color(0.09f, 0.12f, 0.22f, 1f), 0.28f);
            Color bottom = Color.Lerp(horizon, Color.white, 0.40f);
            Material activeSky = RenderSettings.skybox;
            if (activeSky != null)
            {
                if (activeSky.HasProperty("_TopColor")) activeSky.SetColor("_TopColor", top);
                if (activeSky.HasProperty("_HorizonColor")) activeSky.SetColor("_HorizonColor", horizon);
                if (activeSky.HasProperty("_BottomColor")) activeSky.SetColor("_BottomColor", bottom);
                if (activeSky.HasProperty("_SunColor"))
                    activeSky.SetColor("_SunColor", Color.Lerp(horizon, Color.white, 0.75f));
            }

            RenderSettings.ambientSkyColor = Color.Lerp(horizon, Color.white, 0.22f);
            RenderSettings.ambientEquatorColor = Color.Lerp(horizon, Color.white, 0.42f);
            RenderSettings.ambientGroundColor = Color.Lerp(horizon, Color.black, 0.34f);
            RenderSettings.fogColor = Color.Lerp(horizon, Color.white, 0.30f);

            foreach (Camera camera in runtimeCameras)
                if (camera != null) camera.backgroundColor = horizon;
        }

        private void ConfigureVolume()
        {
            Volume volume = generatedEnvironment.GetComponent<Volume>();
            if (volume == null) volume = generatedEnvironment.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 200f;

            if (generatedVolumeProfile == null)
            {
                generatedVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                generatedVolumeProfile.name = "Broken Edge Volume (Generated)";
                generatedVolumeProfile.hideFlags = HideFlags.HideAndDontSave;
            }
            volume.sharedProfile = generatedVolumeProfile;

            ColorAdjustments color = GetOrAdd<ColorAdjustments>();
            // Preserve the flat shader's authored palette. The previous exposure boost
            // and heavy desaturation laid a milky grey veil over every material.
            color.postExposure.Override(-0.12f);
            color.contrast.Override(18f);
            color.saturation.Override(22f);
            color.colorFilter.Override(new Color(1f, 0.99f, 0.97f, 1f));

            WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>();
            whiteBalance.temperature.Override(3f);
            whiteBalance.tint.Override(4f);

            Bloom bloom = GetOrAdd<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.12f);
            bloom.scatter.Override(0.48f);

            Vignette vignette = GetOrAdd<Vignette>();
            vignette.color.Override(new Color(0.09f, 0.10f, 0.18f, 1f));
            vignette.intensity.Override(0.025f);
            vignette.smoothness.Override(0.76f);

            Tonemapping tone = GetOrAdd<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);

            FilmGrain grain = GetOrAdd<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.01f);
            grain.response.Override(0.62f);
        }

        private T GetOrAdd<T>() where T : VolumeComponent
        {
            if (generatedVolumeProfile.TryGet(out T component)) return component;
            return generatedVolumeProfile.Add<T>(true);
        }

        private void ConfigureArena()
        {
            Transform arenaTransform = generatedEnvironment.transform.Find("Broken Edge Arena Tile");
            GameObject arena = arenaTransform != null ? arenaTransform.gameObject : new GameObject("Broken Edge Arena Tile");
            arena.transform.SetParent(generatedEnvironment.transform, false);
            arena.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 9f, 0f));
            arena.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            if (generatedArenaMesh == null)
            {
                generatedArenaMesh = CreateArenaMesh(arenaRadius);
                generatedArenaMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            Shader shader = arenaShader != null ? arenaShader : Shader.Find("Swordplay/Broken Edge Arena");
            if (shader != null && (generatedArenaMaterial == null || generatedArenaMaterial.shader != shader))
            {
                DestroyOwned(generatedArenaMaterial);
                generatedArenaMaterial = new Material(shader)
                {
                    name = "Broken Edge Arena (Generated)",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (generatedArenaMaterial != null)
            {
                generatedArenaMaterial.SetColor("_BaseColor", new Color(0.49f, 0.53f, 0.61f, 1f));
                generatedArenaMaterial.SetColor("_ShadowColor", new Color(0.14f, 0.17f, 0.25f, 1f));
                generatedArenaMaterial.SetColor("_HighlightColor", new Color(0.86f, 0.65f, 0.54f, 1f));
                generatedArenaMaterial.SetColor("_RimColor", new Color(0.31f, 0.86f, 0.82f, 1f));
                generatedArenaMaterial.SetFloat("_RimStrength", 0.18f);
            }

            MeshFilter filter = arena.GetComponent<MeshFilter>();
            if (filter == null) filter = arena.AddComponent<MeshFilter>();
            filter.sharedMesh = generatedArenaMesh;
            MeshRenderer renderer = arena.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = arena.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = generatedArenaMaterial;
            MeshCollider collider = arena.GetComponent<MeshCollider>();
            if (collider == null) collider = arena.AddComponent<MeshCollider>();
            collider.sharedMesh = generatedArenaMesh;
        }

        private static Mesh CreateArenaMesh(float radius)
        {
            const int segments = 20;
            float[] radii = { 0f, radius * 0.30f, radius * 0.625f, radius };
            Color[] palette =
            {
                new(0.90f, 0.80f, 0.78f, 1f),
                new(0.69f, 0.78f, 0.82f, 1f),
                new(0.78f, 0.70f, 0.78f, 1f),
                new(0.63f, 0.72f, 0.72f, 1f)
            };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            for (int ring = 0; ring < radii.Length - 1; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle0 = segment * Mathf.PI * 2f / segments;
                    float angle1 = (segment + 1) * Mathf.PI * 2f / segments;
                    float inner = radii[ring];
                    float outer0 = radii[ring + 1] * (1f + Mathf.Sin(segment * 3.71f + ring) * 0.018f);
                    float outer1 = radii[ring + 1] * (1f + Mathf.Sin((segment + 1) * 3.71f + ring) * 0.018f);
                    Vector3 i0 = new(Mathf.Cos(angle0) * inner, 0f, Mathf.Sin(angle0) * inner);
                    Vector3 i1 = new(Mathf.Cos(angle1) * inner, 0f, Mathf.Sin(angle1) * inner);
                    Vector3 o0 = new(Mathf.Cos(angle0) * outer0, 0f, Mathf.Sin(angle0) * outer0);
                    Vector3 o1 = new(Mathf.Cos(angle1) * outer1, 0f, Mathf.Sin(angle1) * outer1);
                    int start = vertices.Count;
                    vertices.AddRange(new[] { i0, o0, o1, i1 });
                    normals.AddRange(Enumerable.Repeat(Vector3.up, 4));
                    colors.AddRange(Enumerable.Repeat(palette[(ring * 3 + segment) % palette.Length], 4));
                    triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
            }

            var mesh = new Mesh { name = "Broken Edge Irregular Duel Tile" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
#if UNITY_EDITOR
            else DestroyImmediate(target);
#endif
        }
    }
}
