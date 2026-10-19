using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Swordplay.Visuals
{
    /// <summary>
    /// Builds one slash ribbon from the rendered blade's real Start/Tip history.
    /// The store particle prefab is intentionally not instantiated: its looping,
    /// offsets and authored rotation cannot drift from the sword because none of
    /// those values participate in this effect.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwordSlashVfxSynchronizer : MonoBehaviour
    {
        private const string RibbonMaterialResource = "SwordSlash/SwordSlashRibbon";
        private const string RibbonObjectName = "Sword Slash Motion Ribbon";

        [SerializeField, Range(0.5f, 4f)] private float trailLengthInBladeLengths = 2.25f;
        [SerializeField, Range(0.002f, 0.08f)] private float sampleSpacingInBladeLengths = 0.018f;
        [SerializeField, Range(6, 48)] private int maxSamples = 30;
        [SerializeField, Range(0.1f, 2f)] private float intensity = 1.1f;
        [SerializeField, Range(0.25f, 1.5f)] private float bladeWidth = 1f;
        [SerializeField, Range(0f, 1f)] private float textureFlow = 0.16f;
        [SerializeField] private bool useWeaponEffectColor = true;
        [SerializeField, ColorUsage(true, true)] private Color colorOverride = new(1.2f, 0.35f, 0.08f, 0.9f);
        private Material materialOverride;

        private readonly List<BladeSample> samples = new();
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Color32> colors = new();
        private readonly List<int> triangles = new();
        private Animator animator;
        private Transform bladeStart;
        private Transform bladeTip;
        private GameObject ribbonObject;
        private Mesh ribbonMesh;
        private Material ribbonMaterial;
        private BrokenEdgeBladeVfx bladeVfx;
        private bool attackWasActive;
        private float previousNormalizedTime = -1f;
        private float accumulatedTravel;
        private float referenceBladeLength = 1f;
        private Vector3 previousTip;
        private bool hasPreviousTip;

        private struct BladeSample
        {
            public Vector3 Start;
            public Vector3 Tip;
            public float Travel;
        }

        public static SwordSlashVfxSynchronizer Configure(
            Transform sword, Animator animator, float travelInBladeLengths,
            float bladeWidth, float intensity, float textureFlow,
            bool useWeaponEffectColor, Color colorOverride, Material materialOverride)
        {
            if (sword == null || animator == null) return null;
            SwordSlashVfxSynchronizer synchronizer = sword.GetComponent<SwordSlashVfxSynchronizer>();
            if (synchronizer == null)
                synchronizer = sword.gameObject.AddComponent<SwordSlashVfxSynchronizer>();
            synchronizer.animator = animator;
            synchronizer.trailLengthInBladeLengths = Mathf.Clamp(travelInBladeLengths, 0.5f, 4f);
            synchronizer.bladeWidth = Mathf.Clamp(bladeWidth, 0.25f, 1.5f);
            synchronizer.intensity = Mathf.Clamp(intensity, 0.1f, 2f);
            synchronizer.textureFlow = Mathf.Clamp01(textureFlow);
            synchronizer.useWeaponEffectColor = useWeaponEffectColor;
            synchronizer.colorOverride = colorOverride;
            synchronizer.materialOverride = materialOverride;
            synchronizer.ResolveBladeMarkers();
            synchronizer.BuildRibbon();
            return synchronizer;
        }

        private void LateUpdate()
        {
            if (animator == null || ribbonMesh == null) return;
            bool attackActive = TryGetAttackNormalizedTime(out float normalizedTime);
            if (!attackActive)
            {
                if (attackWasActive) ClearRibbon();
                attackWasActive = false;
                previousNormalizedTime = -1f;
                return;
            }

            normalizedTime = Mathf.Clamp01(normalizedTime);
            bool newAttack = !attackWasActive || normalizedTime + 0.001f < previousNormalizedTime;
            if (newAttack) BeginAttackRibbon();
            CaptureBladeMotion();
            attackWasActive = true;
            previousNormalizedTime = normalizedTime;
        }

        private void BeginAttackRibbon()
        {
            ClearRibbon();
            ResolveBladeMarkers();
            if (bladeStart == null || bladeTip == null) return;
            referenceBladeLength = Mathf.Max(0.05f, Vector3.Distance(bladeStart.position, bladeTip.position));
            previousTip = bladeTip.position;
            hasPreviousTip = true;
            AddSample();
            ApplyCharacterColor();
        }

        private void CaptureBladeMotion()
        {
            if (bladeStart == null || bladeTip == null) return;
            Vector3 currentTip = bladeTip.position;
            if (!hasPreviousTip)
            {
                previousTip = currentTip;
                hasPreviousTip = true;
                AddSample();
                return;
            }

            float frameTravel = Vector3.Distance(previousTip, currentTip);
            previousTip = currentTip;
            accumulatedTravel += frameTravel;
            float spacing = referenceBladeLength * sampleSpacingInBladeLengths;
            if (samples.Count == 0 || accumulatedTravel - samples[0].Travel >= spacing)
                AddSample();

            float maximumTrailTravel = referenceBladeLength * trailLengthInBladeLengths;
            for (int i = samples.Count - 1; i >= 0; i--)
                if (accumulatedTravel - samples[i].Travel > maximumTrailTravel)
                    samples.RemoveAt(i);
            if (samples.Count > maxSamples)
                samples.RemoveRange(maxSamples, samples.Count - maxSamples);

            BuildMesh(maximumTrailTravel);
            if (ribbonMaterial != null)
                ribbonMaterial.SetFloat("_MotionPhase",
                    accumulatedTravel / Mathf.Max(referenceBladeLength, 0.05f) * textureFlow);
        }

        private void AddSample()
        {
            samples.Insert(0, new BladeSample
            {
                Start = Vector3.LerpUnclamped(bladeStart.position, bladeTip.position, (1f - bladeWidth) * 0.5f),
                Tip = Vector3.LerpUnclamped(bladeStart.position, bladeTip.position, (1f + bladeWidth) * 0.5f),
                Travel = accumulatedTravel
            });
        }

        private void BuildMesh(float maximumTrailTravel)
        {
            if (samples.Count < 2)
            {
                ribbonMesh.Clear(true);
                if (ribbonObject != null) ribbonObject.SetActive(false);
                return;
            }

            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                BladeSample sample = samples[i];
                float age = Mathf.Clamp01((accumulatedTravel - sample.Travel) / Mathf.Max(maximumTrailTravel, 0.001f));
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Pow(1f - age, 0.7f));
                vertices.Add(sample.Start);
                vertices.Add(sample.Tip);
                uvs.Add(new Vector2(0f, age));
                uvs.Add(new Vector2(1f, age));
                colors.Add(new Color32(255, 255, 255, alpha));
                colors.Add(new Color32(255, 255, 255, alpha));
                if (i >= samples.Count - 1) continue;
                int vertex = i * 2;
                triangles.Add(vertex);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 1);
                triangles.Add(vertex + 1);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 3);
            }

            ribbonMesh.Clear(true);
            ribbonMesh.SetVertices(vertices);
            ribbonMesh.SetUVs(0, uvs);
            ribbonMesh.SetColors(colors);
            ribbonMesh.SetTriangles(triangles, 0, true);
            ribbonMesh.RecalculateBounds();
            ribbonObject.SetActive(true);
        }

        private void BuildRibbon()
        {
            ReleaseRibbon();
            Material source = materialOverride != null
                ? materialOverride
                : Resources.Load<Material>(RibbonMaterialResource);
            if (source == null)
            {
                Debug.LogError($"{name}: slash ribbon material '{RibbonMaterialResource}' was not found.", this);
                return;
            }

            ribbonMaterial = new Material(source)
            {
                name = "Sword Slash Ribbon (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            ribbonMaterial.SetFloat("_Intensity", intensity);
            ribbonMesh = new Mesh { name = "Sword Slash Motion Ribbon Mesh" };
            ribbonMesh.MarkDynamic();
            ribbonObject = new GameObject(RibbonObjectName)
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                layer = gameObject.layer
            };
            ribbonObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ribbonObject.transform.localScale = Vector3.one;
            ribbonObject.AddComponent<MeshFilter>().sharedMesh = ribbonMesh;
            MeshRenderer renderer = ribbonObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ribbonMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ribbonObject.SetActive(false);
        }

        private void ApplyCharacterColor()
        {
            if (ribbonMaterial == null) return;
            Color color = useWeaponEffectColor && bladeVfx != null
                ? bladeVfx.GlowColor
                : colorOverride;
            ribbonMaterial.SetColor("_GlowColor", color);
        }

        private void ResolveBladeMarkers()
        {
            bladeVfx = GetComponent<BrokenEdgeBladeVfx>();
            if (bladeVfx == null) bladeVfx = GetComponentInChildren<BrokenEdgeBladeVfx>(true);
            if (bladeVfx == null) bladeVfx = GetComponentInParent<BrokenEdgeBladeVfx>();
            bladeStart = bladeVfx != null ? bladeVfx.BladeStartMarker : null;
            bladeTip = bladeVfx != null ? bladeVfx.BladeTipMarker : null;
            if (bladeStart == null) bladeStart = FindDescendant(transform, "Broken Edge Blade Start");
            if (bladeTip == null) bladeTip = FindDescendant(transform, "Broken Edge Blade Tip");
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == objectName) return child;
            return null;
        }

        private bool TryGetAttackNormalizedTime(out float normalizedTime)
        {
            normalizedTime = 0f;
            if (animator == null || !animator.isActiveAndEnabled) return false;
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (current.IsName("Attack"))
            {
                normalizedTime = current.normalizedTime;
                return true;
            }
            if (!animator.IsInTransition(0)) return false;
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (!next.IsName("Attack")) return false;
            normalizedTime = next.normalizedTime;
            return true;
        }

        private void ClearRibbon()
        {
            samples.Clear();
            accumulatedTravel = 0f;
            hasPreviousTip = false;
            if (ribbonMesh != null) ribbonMesh.Clear(true);
            if (ribbonObject != null) ribbonObject.SetActive(false);
        }

        private void OnDisable() => ClearRibbon();
        private void OnDestroy() => ReleaseRibbon();

        private void ReleaseRibbon()
        {
            DestroyOwned(ribbonObject);
            DestroyOwned(ribbonMesh);
            DestroyOwned(ribbonMaterial);
            ribbonObject = null;
            ribbonMesh = null;
            ribbonMaterial = null;
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
