using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Swordplay.Visuals
{
    /// <summary>
    /// Fits a living cyan flame silhouette and white-hot core directly to the weapon mesh.
    /// The swing afterimage uses the same blade span and a procedural broken flame mask,
    /// so it follows the real blade instead of looking like a detached line trail.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BrokenEdgeBladeVfx : MonoBehaviour
    {
        private const string AuraName = "Broken Edge Blade Outline";
        private const string EdgeRibbonName = "Broken Edge Blade Edge Ribbon";
        private const string TrailName = "Broken Edge Blade Trail";
        private const string AuraShaderName = "Swordplay/Broken Edge Blade Aura";
        private const string EdgeRibbonShaderName = "Swordplay/Broken Edge Blade Edge Ribbon";
        private const string TrailShaderName = "Swordplay/Broken Edge Blade Trail";
        private const string BladeStartName = "Broken Edge Blade Start";
        private const string BladeTipName = "Broken Edge Blade Tip";
        private const int CurrentPresetVersion = 8;

        [Header("Blade reference")]
        [SerializeField] private Transform ownerRoot;
        [SerializeField] private Transform bladeVisual;
        [SerializeField] private Transform bladeStartMarker;
        [SerializeField] private Transform bladeTipMarker;
        [SerializeField, Range(0f, 0.35f)] private float gripTrim = 0.13f;
        [SerializeField, Range(0f, 0.08f)] private float tipExtension = 0.01f;
        [Header("Blade cross-section")]
        [Tooltip("Glow expansion along the broad side of the blade.")]
        [SerializeField, Range(0.2f, 4f)] private float broadsideOutlineScale = 1f;
        [Tooltip("Glow expansion through the blade thickness. Keep this low for a flat, blade-shaped ellipse.")]
        [SerializeField, Range(0.05f, 1f)] private float thicknessOutlineScale = 0.18f;

        [Header("Mesh-fitted blade light")]
        [SerializeField, ColorUsage(true, true)] private Color glowColor = new(0.03f, 1.5f, 2.5f, 0.94f);
        [SerializeField, ColorUsage(true, true)] private Color coreColor = new(0.9f, 1.15f, 1.25f, 0.82f);
        [SerializeField, Range(0.002f, 0.08f)] private float auraWidth = 0.034f;
        [SerializeField, Range(0f, 2f)] private float auraIntensity = 1.4f;
        [SerializeField, Range(0f, 2f)] private float flameWrithe = 1.28f;
        [SerializeField, Range(0f, 12f)] private float flameSpeed = 6.2f;
        [SerializeField, Range(1f, 40f)] private float flameDetail = 16f;

        [Header("Broken flame afterimage")]
        [SerializeField, Range(0.04f, 0.4f)] private float trailLifetime = 0.3f;
        [SerializeField, Range(0.05f, 15f)] private float speedThreshold = 0.18f;
        [SerializeField, Range(4, 36)] private int maxSamples = 32;
        [SerializeField, Range(0f, 1f)] private float trailOpacity = 1f;
        [SerializeField, Range(0f, 15f)] private float trailFlameSpeed = 8f;
        [SerializeField, Range(1f, 60f)] private float trailFlameDetail = 24f;
        [SerializeField, Range(0f, 1f)] private float trailEdgeRoughness = 0.9f;
        [SerializeField, HideInInspector] private int presetVersion;

        private readonly List<TrailSample> samples = new();
        private Renderer sourceRenderer;
        private GameObject auraObject;
        private GameObject edgeRibbonObject;
        private GameObject trailObject;
        private Mesh auraMesh;
        private Mesh trailMesh;
        private Mesh edgeRibbonMesh;
        private Material auraMaterial;
        private Material edgeRibbonMaterial;
        private Material trailMaterial;
        private Vector3 localBase;
        private Vector3 localTip;
        private Vector3 localWideAxis;
        private Vector3 localThinAxis;
        private float bladeWideRadius;
        private float bladeThinRadius;
        private Vector3 previousTip;
        private bool hasPreviousTip;
        private bool markerRefitRequired;
#if UNITY_EDITOR
        private bool rebuildQueued;
#endif

        private struct TrailSample
        {
            public Vector3 Base;
            public Vector3 Tip;
            public float Time;
        }

        public bool Configure(Transform owner)
        {
            Transform target = null;
            TargetMatching matching = GetComponent<TargetMatching>();
            if (matching != null) target = matching.Target;
            Transform resolvedVisual = FindBladeVisual(target);

            bool upgradePreset = presetVersion < CurrentPresetVersion;
            markerRefitRequired |= upgradePreset;
            bool changed = ownerRoot != owner || bladeVisual != resolvedVisual || upgradePreset;
            ownerRoot = owner;
            if (resolvedVisual != null) bladeVisual = resolvedVisual;
            if (upgradePreset)
            {
                gripTrim = 0.13f;
                tipExtension = 0.01f;
                glowColor = new Color(0.03f, 1.5f, 2.5f, 0.94f);
                coreColor = new Color(0.9f, 1.15f, 1.25f, 0.82f);
                auraWidth = 0.034f;
                auraIntensity = 1.4f;
                flameWrithe = 1.28f;
                flameSpeed = 6.2f;
                flameDetail = 16f;
                trailLifetime = 0.3f;
                speedThreshold = 0.18f;
                maxSamples = 32;
                trailOpacity = 1f;
                trailFlameSpeed = 8f;
                trailFlameDetail = 24f;
                trailEdgeRoughness = 0.9f;
                broadsideOutlineScale = 1f;
                thicknessOutlineScale = 0.18f;
                presetVersion = CurrentPresetVersion;
            }

            QueueAuraRebuild();
            return changed;
        }

        /// <summary>
        /// Creates persistent, editable blade-only limits in the scene.  The generated
        /// rendering remains transient, but these markers let both the aura and trail
        /// use the exact same blade span and keep every effect away from the hilt.
        /// </summary>
        public bool EnsureBladeOnlyMarkers()
        {
            ResolveBladeRenderer();
            if (sourceRenderer == null || !TryGetSourceMesh(sourceRenderer, out Mesh sourceMesh)) return false;

            ResolveBladeSpan(sourceRenderer, sourceMesh, out Vector3 fallbackBase, out Vector3 fallbackTip);
            bool changed = false;
            bladeStartMarker = FindOrCreateMarker(bladeStartMarker, BladeStartName, fallbackBase, ref changed);
            bladeTipMarker = FindOrCreateMarker(bladeTipMarker, BladeTipName, fallbackTip, ref changed);
            if (markerRefitRequired || changed)
            {
                changed |= FitMarkersToBladeSubmesh(sourceMesh);
                markerRefitRequired = false;
            }
            QueueAuraRebuild();
            return changed;
        }

        private void OnEnable() => QueueAuraRebuild();

        private void OnValidate()
        {
            if (isActiveAndEnabled) QueueAuraRebuild();
        }

        private void LateUpdate()
        {
            if (auraObject == null) QueueAuraRebuild();
            if (!Application.isPlaying) return;

            EnsureTrail();
            UpdateTrail();
        }

        private void OnDisable() => ReleaseGeneratedObjects();
        private void OnDestroy() => ReleaseGeneratedObjects();

        private void QueueAuraRebuild()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (rebuildQueued) return;
                rebuildQueued = true;
                EditorApplication.delayCall += RebuildAfterValidation;
                return;
            }
#endif
            RebuildAura();
        }

#if UNITY_EDITOR
        private void RebuildAfterValidation()
        {
            rebuildQueued = false;
            if (this == null || !isActiveAndEnabled || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            RebuildAura();
            SceneView.RepaintAll();
        }
#endif

        private void RebuildAura()
        {
            ReleaseAura();
            ResolveBladeRenderer();
            if (sourceRenderer == null || !TryGetSourceMesh(sourceRenderer, out Mesh sourceMesh)) return;
            ResolveBladeSpan(sourceRenderer, sourceMesh, out localBase, out localTip);
            if (bladeStartMarker != null)
                localBase = sourceRenderer.transform.InverseTransformPoint(bladeStartMarker.position);
            if (bladeTipMarker != null)
                localTip = sourceRenderer.transform.InverseTransformPoint(bladeTipMarker.position);
            int bladeSubmesh = FindBladeSubmesh(sourceMesh, localBase, localTip);
            EstimateBladeEllipse(
                sourceMesh,
                bladeSubmesh,
                localBase,
                localTip,
                out localWideAxis,
                out localThinAxis,
                out bladeWideRadius,
                out bladeThinRadius);

            Shader shader = Shader.Find(AuraShaderName);
            Shader edgeShader = Shader.Find(EdgeRibbonShaderName);
            if (shader == null || !shader.isSupported || edgeShader == null || !edgeShader.isSupported) return;
            auraMaterial = CreateAuraMaterial(shader, true);
            auraMesh = CreateBladeOnlyMesh(sourceMesh, bladeSubmesh);

            auraObject = new GameObject(AuraName)
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                layer = sourceRenderer.gameObject.layer
            };
            auraObject.transform.SetParent(sourceRenderer.transform, false);
            CopyRenderer(sourceRenderer, auraMesh, auraObject, auraMaterial);

            edgeRibbonMaterial = CreateEdgeRibbonMaterial(edgeShader);
            edgeRibbonMesh = CreateEdgeRibbonMesh(sourceMesh, bladeSubmesh);
            edgeRibbonObject = new GameObject(EdgeRibbonName)
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                layer = sourceRenderer.gameObject.layer
            };
            edgeRibbonObject.transform.SetParent(sourceRenderer.transform, false);
            CopyStaticRenderer(edgeRibbonMesh, edgeRibbonObject, edgeRibbonMaterial);
        }

        private void ResolveBladeRenderer()
        {
            if (bladeVisual == null)
            {
                TargetMatching matching = GetComponent<TargetMatching>();
                if (matching != null) bladeVisual = FindBladeVisual(matching.Target);
            }
            if (bladeVisual == null) return;

            Renderer direct = bladeVisual.GetComponent<Renderer>();
            if (IsUsableRenderer(direct))
            {
                sourceRenderer = direct;
                return;
            }

            foreach (Renderer candidate in bladeVisual.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsUsableRenderer(candidate)) continue;
                sourceRenderer = candidate;
                return;
            }
        }

        private static Transform FindBladeVisual(Transform target)
        {
            if (target == null) return null;
            Renderer direct = target.GetComponent<Renderer>();
            if (IsUsableRenderer(direct)) return target;

            Renderer fallback = null;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsUsableRenderer(renderer)) continue;
                string lowerName = renderer.name.ToLowerInvariant();
                if (lowerName.Contains("blade") || lowerName.Contains("sword") || lowerName.Contains("katana"))
                    return renderer.transform;
                if (fallback == null) fallback = renderer;
            }
            return fallback != null ? fallback.transform : target;
        }

        private static bool IsUsableRenderer(Renderer renderer)
        {
            if (renderer == null || renderer is TrailRenderer || renderer is LineRenderer || renderer is ParticleSystemRenderer)
                return false;
            if (renderer.name == AuraName || renderer.name == TrailName)
                return false;
            return renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
        }

        private Transform FindOrCreateMarker(Transform current, string markerName, Vector3 localPosition, ref bool changed)
        {
            if (current != null) return current;
            Transform existing = sourceRenderer.transform.Find(markerName);
            if (existing != null) return existing;

            var markerObject = new GameObject(markerName);
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(markerObject, "Create blade VFX limit");
#endif
            Transform marker = markerObject.transform;
            marker.SetParent(sourceRenderer.transform, false);
            marker.localPosition = localPosition;
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;
            changed = true;
            return marker;
        }

        private static void EstimateBladeEllipse(
            Mesh mesh,
            int bladeSubmesh,
            Vector3 bladeBase,
            Vector3 bladeTip,
            out Vector3 wideAxis,
            out Vector3 thinAxis,
            out float wideRadius,
            out float thinRadius)
        {
            Vector3 span = bladeTip - bladeBase;
            float length = span.magnitude;
            if (length <= 0.0001f)
            {
                wideAxis = Vector3.right;
                thinAxis = Vector3.forward;
                wideRadius = 0.03f;
                thinRadius = 0.006f;
                return;
            }

            Vector3 axis = span / length;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.right)) < 0.8f
                ? Vector3.right
                : Vector3.up;
            Vector3 firstCrossAxis = Vector3.Cross(axis, reference).normalized;
            Vector3 secondCrossAxis = Vector3.Cross(axis, firstCrossAxis).normalized;
            float firstRadius = 0f;
            float secondRadius = 0f;
            Vector3[] vertices = mesh.vertices;
            int[] bladeIndices = mesh.GetIndices(bladeSubmesh);
            var visited = new HashSet<int>();
            foreach (int vertexIndex in bladeIndices)
            {
                if (!visited.Add(vertexIndex)) continue;
                Vector3 vertex = vertices[vertexIndex];
                Vector3 relative = vertex - bladeBase;
                float along = Vector3.Dot(relative, axis);
                if (along < length * 0.15f || along > length * 0.88f) continue;
                Vector3 radial = relative - axis * along;
                firstRadius = Mathf.Max(firstRadius, Mathf.Abs(Vector3.Dot(radial, firstCrossAxis)));
                secondRadius = Mathf.Max(secondRadius, Mathf.Abs(Vector3.Dot(radial, secondCrossAxis)));
            }

            if (firstRadius >= secondRadius)
            {
                wideAxis = firstCrossAxis;
                thinAxis = secondCrossAxis;
                wideRadius = firstRadius;
                thinRadius = secondRadius;
            }
            else
            {
                wideAxis = secondCrossAxis;
                thinAxis = firstCrossAxis;
                wideRadius = secondRadius;
                thinRadius = firstRadius;
            }

            wideRadius = Mathf.Max(0.008f, wideRadius * 1.08f);
            thinRadius = Mathf.Clamp(thinRadius * 1.12f, 0.002f, wideRadius * 0.38f);
        }

        private bool FitMarkersToBladeSubmesh(Mesh mesh)
        {
            if (bladeStartMarker == null || bladeTipMarker == null || sourceRenderer == null) return false;
            Transform rendererTransform = sourceRenderer.transform;
            Vector3 currentBase = rendererTransform.InverseTransformPoint(bladeStartMarker.position);
            Vector3 currentTip = rendererTransform.InverseTransformPoint(bladeTipMarker.position);
            Vector3 span = currentTip - currentBase;
            if (span.sqrMagnitude <= 0.0000001f) return false;

            Vector3 axis = span.normalized;
            int bladeSubmesh = FindBladeSubmesh(mesh, currentBase, currentTip, out float bladeMin, out float bladeMax);
            if (bladeSubmesh < 0 || bladeMax <= bladeMin) return false;

            Vector3 fittedBase = currentBase + axis * bladeMin;
            Vector3 fittedTip = currentBase + axis * bladeMax;
            bool changed = Vector3.SqrMagnitude(fittedBase - currentBase) > 0.00000001f
                           || Vector3.SqrMagnitude(fittedTip - currentTip) > 0.00000001f;
            if (!changed) return false;

            bladeStartMarker.position = rendererTransform.TransformPoint(fittedBase);
            bladeTipMarker.position = rendererTransform.TransformPoint(fittedTip);
            return true;
        }

        private static int FindBladeSubmesh(Mesh mesh, Vector3 bladeBase, Vector3 bladeTip)
        {
            return FindBladeSubmesh(mesh, bladeBase, bladeTip, out _, out _);
        }

        private static int FindBladeSubmesh(
            Mesh mesh,
            Vector3 bladeBase,
            Vector3 bladeTip,
            out float selectedMin,
            out float selectedMax)
        {
            Vector3 span = bladeTip - bladeBase;
            if (span.sqrMagnitude <= 0.0000001f)
            {
                selectedMin = 0f;
                selectedMax = 0f;
                return 0;
            }

            Vector3 axis = span.normalized;
            Vector3[] vertices = mesh.vertices;
            int selectedSubmesh = 0;
            selectedMin = 0f;
            selectedMax = 0f;
            float longestRange = float.NegativeInfinity;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetIndices(submesh);
                float min = float.PositiveInfinity;
                float max = float.NegativeInfinity;
                var visited = new HashSet<int>();
                foreach (int vertexIndex in indices)
                {
                    if (!visited.Add(vertexIndex)) continue;
                    float along = Vector3.Dot(vertices[vertexIndex] - bladeBase, axis);
                    min = Mathf.Min(min, along);
                    max = Mathf.Max(max, along);
                }

                float range = max - min;
                if (range <= longestRange) continue;
                longestRange = range;
                selectedSubmesh = submesh;
                selectedMin = min;
                selectedMax = max;
            }

            return selectedSubmesh;
        }

        private static Mesh CreateBladeOnlyMesh(Mesh source, int bladeSubmesh)
        {
            int[] triangles = source.GetTriangles(bladeSubmesh);
            Mesh bladeMesh = Object.Instantiate(source);
            bladeMesh.name = source.name + " - Blade Only";
            bladeMesh.subMeshCount = 1;
            bladeMesh.SetTriangles(triangles, 0, true);
            return bladeMesh;
        }

        private Mesh CreateEdgeRibbonMesh(Mesh sourceMesh, int bladeSubmesh)
        {
            // The original mesh shell remains the visible outline.  This ribbon is only
            // a small bilateral tip seam: it overlaps the last part of the real blade
            // so a thin rear edge cannot appear detached or prematurely cut off.
            const int segments = 10;
            const float startAlong01 = 0.78f;
            Vector3 axis = (localTip - localBase).normalized;
            float bladeLength = Vector3.Distance(localBase, localTip);
            Vector3[] sourceVertices = sourceMesh.vertices;
            int[] sourceIndices = sourceMesh.GetIndices(bladeSubmesh);
            var uniqueIndices = new HashSet<int>(sourceIndices);
            float sampleWindow = bladeLength / segments * 1.45f;
            var vertices = new Vector3[(segments + 1) * 4];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 12];

            int vertex = 0;
            int triangle = 0;
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                int sideStart = vertex;
                for (int i = 0; i <= segments; i++)
                {
                    float local01 = i / (float)segments;
                    float along01 = Mathf.Lerp(startAlong01, 1f, local01);
                    float tipFade = 1f - Mathf.SmoothStep(0.68f, 1f, local01);
                    float seed = Mathf.Abs(Mathf.Sin((i + 1) * 12.9898f + sideIndex * 37.719f));
                    float tongueWidth = auraWidth * (0.45f + seed * 0.30f) * tipFade;
                    Vector3 outward = localWideAxis * side;
                    Vector3 inner;
                    Vector3 outer;
                    Vector3 ribbonNormal = outward;
                    if (i == segments)
                    {
                        inner = localTip - axis * (auraWidth * 0.04f);
                        outer = localTip + axis * (auraWidth * 0.42f);
                        ribbonNormal = axis;
                    }
                    else
                    {
                        float targetAlong = bladeLength * along01;
                        float minWide = float.PositiveInfinity;
                        float maxWide = float.NegativeInfinity;
                        foreach (int index in uniqueIndices)
                        {
                            Vector3 relative = sourceVertices[index] - localBase;
                            float along = Vector3.Dot(relative, axis);
                            if (Mathf.Abs(along - targetAlong) > sampleWindow) continue;
                            float wide = Vector3.Dot(relative, localWideAxis);
                            minWide = Mathf.Min(minWide, wide);
                            maxWide = Mathf.Max(maxWide, wide);
                        }

                        if (!float.IsFinite(minWide) || !float.IsFinite(maxWide))
                        {
                            minWide = -bladeWideRadius;
                            maxWide = bladeWideRadius;
                        }

                        float measuredWidth = Mathf.Max(0.001f, maxWide - minWide);
                        float overlap = Mathf.Min(bladeWideRadius * 0.12f, measuredWidth * 0.12f);
                        float innerWide = side < 0f ? minWide + overlap : maxWide - overlap;
                        inner = localBase + axis * targetAlong + localWideAxis * innerWide;
                        outer = inner + outward * tongueWidth;
                    }
                    vertices[vertex] = inner;
                    vertices[vertex + 1] = outer;
                    normals[vertex] = normals[vertex + 1] = ribbonNormal;
                    uv[vertex] = new Vector2(along01, 0f);
                    uv[vertex + 1] = new Vector2(along01, 1f);
                    vertex += 2;

                    if (i == 0) continue;
                    int current = sideStart + i * 2;
                    int previous = current - 2;
                    triangles[triangle++] = previous;
                    triangles[triangle++] = current;
                    triangles[triangle++] = previous + 1;
                    triangles[triangle++] = previous + 1;
                    triangles[triangle++] = current;
                    triangles[triangle++] = current + 1;
                }
            }

            var mesh = new Mesh { name = "Broken Edge Blade Edge Ribbon Mesh" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool TryGetSourceMesh(Renderer renderer, out Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
                return mesh != null;
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
            return mesh != null;
        }

        private static void CopyRenderer(Renderer source, Mesh mesh, GameObject destination, Material material)
        {
            int materialCount = Mathf.Max(1, mesh.subMeshCount);
            var materials = new Material[materialCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;

            if (source is SkinnedMeshRenderer sourceSkinned)
            {
                SkinnedMeshRenderer copy = destination.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = mesh;
                copy.rootBone = sourceSkinned.rootBone;
                copy.bones = sourceSkinned.bones;
                copy.localBounds = sourceSkinned.localBounds;
                copy.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
                copy.sharedMaterials = materials;
                ConfigureOverlayRenderer(copy);
                return;
            }

            MeshFilter filter = destination.AddComponent<MeshFilter>();
            MeshRenderer renderer = destination.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = materials;
            ConfigureOverlayRenderer(renderer);
        }

        private static void CopyStaticRenderer(Mesh mesh, GameObject destination, Material material)
        {
            MeshFilter filter = destination.AddComponent<MeshFilter>();
            MeshRenderer renderer = destination.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            ConfigureOverlayRenderer(renderer);
        }

        private static void ConfigureOverlayRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void ResolveBladeSpan(Renderer renderer, Mesh mesh, out Vector3 bladeBase, out Vector3 bladeTip)
        {
            Bounds bounds = renderer is SkinnedMeshRenderer skinned ? skinned.localBounds : mesh.bounds;
            Vector3 size = bounds.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Vector3 direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            float extent = axis == 0 ? bounds.extents.x : axis == 1 ? bounds.extents.y : bounds.extents.z;
            Vector3 first = bounds.center - direction * extent;
            Vector3 second = bounds.center + direction * extent;

            Vector3 ownerPosition = ownerRoot != null ? ownerRoot.position : transform.position;
            bool firstIsBase = Vector3.SqrMagnitude(renderer.transform.TransformPoint(first) - ownerPosition)
                               <= Vector3.SqrMagnitude(renderer.transform.TransformPoint(second) - ownerPosition);
            bladeBase = firstIsBase ? first : second;
            bladeTip = firstIsBase ? second : first;

            Vector3 span = bladeTip - bladeBase;
            bladeBase += span * gripTrim;
            bladeTip -= span.normalized * Mathf.Min(tipExtension, span.magnitude * 0.05f);
        }

        private void EnsureTrail()
        {
            if (trailObject != null || sourceRenderer == null) return;
            Shader shader = Shader.Find(TrailShaderName);
            if (shader == null || !shader.isSupported) return;

            trailMaterial = CreateTrailMaterial(shader);
            trailMesh = new Mesh { name = "Broken Edge Short Blade Trail Mesh" };
            trailMesh.MarkDynamic();
            trailObject = new GameObject(TrailName)
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                layer = sourceRenderer.gameObject.layer
            };
            trailObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            trailObject.transform.localScale = Vector3.one;
            MeshFilter filter = trailObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = trailObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = trailMesh;
            renderer.sharedMaterial = trailMaterial;
            ConfigureOverlayRenderer(renderer);
        }

        private void UpdateTrail()
        {
            if (trailMesh == null || sourceRenderer == null) return;
            float now = Time.time;
            Vector3 bladeBase = bladeStartMarker != null
                ? bladeStartMarker.position
                : sourceRenderer.transform.TransformPoint(localBase);
            Vector3 bladeTip = bladeTipMarker != null
                ? bladeTipMarker.position
                : sourceRenderer.transform.TransformPoint(localTip);
            float speed = hasPreviousTip
                ? Vector3.Distance(bladeTip, previousTip) / Mathf.Max(Time.deltaTime, 0.0001f)
                : 0f;
            previousTip = bladeTip;
            hasPreviousTip = true;

            if (speed >= speedThreshold)
            {
                samples.Insert(0, new TrailSample { Base = bladeBase, Tip = bladeTip, Time = now });
                if (samples.Count > maxSamples) samples.RemoveRange(maxSamples, samples.Count - maxSamples);
            }

            for (int i = samples.Count - 1; i >= 0; i--)
                if (now - samples[i].Time > trailLifetime) samples.RemoveAt(i);
            BuildTrailMesh(now);
        }

        private void BuildTrailMesh(float now)
        {
            trailMesh.Clear(false);
            if (samples.Count < 2) return;

            int count = samples.Count;
            var vertices = new Vector3[count * 2];
            var uv = new Vector2[count * 2];
            var colors = new Color32[count * 2];
            var triangles = new int[(count - 1) * 6];

            for (int i = 0; i < count; i++)
            {
                float age01 = Mathf.Clamp01((now - samples[i].Time) / trailLifetime);
                // Hold the cyan body long enough to read as the blade outline being
                // pulled through space; the shader, rather than a quick alpha fade,
                // carves the far end into changing fire tongues.
                float stretchedFade = Mathf.Pow(1f - age01, 0.62f);
                byte alpha = (byte)Mathf.RoundToInt(255f * trailOpacity * stretchedFade);
                int vertex = i * 2;
                vertices[vertex] = samples[i].Base;
                vertices[vertex + 1] = samples[i].Tip;
                uv[vertex] = new Vector2(0f, age01);
                uv[vertex + 1] = new Vector2(1f, age01);
                colors[vertex] = colors[vertex + 1] = new Color32(255, 255, 255, alpha);

                if (i >= count - 1) continue;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }

            trailMesh.vertices = vertices;
            trailMesh.uv = uv;
            trailMesh.colors32 = colors;
            trailMesh.triangles = triangles;
            trailMesh.RecalculateBounds();
        }

        private Material CreateAuraMaterial(Shader shader, bool drawCore)
        {
            var material = new Material(shader)
            {
                name = "Broken Edge Blade Outline (Generated)",
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_GlowColor", glowColor);
            material.SetColor("_CoreColor", coreColor);
            material.SetFloat("_OutlineWidth", auraWidth);
            material.SetFloat("_Intensity", auraIntensity);
            material.SetFloat("_FlameAmplitude", flameWrithe);
            material.SetFloat("_FlameSpeed", flameSpeed);
            material.SetFloat("_FlameScale", flameDetail);
            Vector3 axis = localTip - localBase;
            float length = Mathf.Max(0.0001f, axis.magnitude);
            material.SetVector("_BladeBaseOS", localBase);
            material.SetVector("_BladeAxisOS", axis / length);
            material.SetVector("_BladeWideAxisOS", localWideAxis);
            material.SetVector("_BladeThinAxisOS", localThinAxis);
            material.SetFloat("_BladeLength", length);
            material.SetFloat("_BladeWideRadius", bladeWideRadius);
            material.SetFloat("_BladeThinRadius", bladeThinRadius);
            material.SetFloat("_BroadsideOutlineScale", broadsideOutlineScale);
            material.SetFloat("_ThicknessOutlineScale", thicknessOutlineScale);
            material.SetFloat("_BladeClipSoftness", Mathf.Min(0.01f, length * 0.01f));
            if (!drawCore) material.SetShaderPassEnabled("WhiteHotBladeCore", false);
            return material;
        }

        private Material CreateEdgeRibbonMaterial(Shader shader)
        {
            var material = new Material(shader)
            {
                name = "Broken Edge Blade Edge Ribbon (Generated)",
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_GlowColor", glowColor);
            material.SetFloat("_Intensity", auraIntensity);
            material.SetFloat("_Writhe", flameWrithe);
            material.SetFloat("_FlameSpeed", flameSpeed);
            material.SetFloat("_FlameDetail", flameDetail);
            material.SetFloat("_OutlineWidth", auraWidth * 0.35f);
            return material;
        }

        private Material CreateTrailMaterial(Shader shader)
        {
            var material = new Material(shader)
            {
                name = "Broken Edge Short Trail (Generated)",
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_GlowColor", new Color(glowColor.r, glowColor.g, glowColor.b, glowColor.a));
            material.SetFloat("_Intensity", auraIntensity);
            material.SetFloat("_FlameSpeed", trailFlameSpeed);
            material.SetFloat("_FlameScale", trailFlameDetail);
            material.SetFloat("_EdgeRoughness", trailEdgeRoughness);
            return material;
        }

        private void ReleaseGeneratedObjects()
        {
            ReleaseAura();
            DestroyOwned(trailObject);
            DestroyOwned(trailMesh);
            DestroyOwned(trailMaterial);
            trailObject = null;
            trailMesh = null;
            trailMaterial = null;
            samples.Clear();
            hasPreviousTip = false;
        }

        private void ReleaseAura()
        {
            DestroyOwned(auraObject);
            DestroyOwned(edgeRibbonObject);
            DestroyOwned(auraMesh);
            DestroyOwned(edgeRibbonMesh);
            DestroyOwned(auraMaterial);
            DestroyOwned(edgeRibbonMaterial);
            auraObject = null;
            edgeRibbonObject = null;
            auraMesh = null;
            edgeRibbonMesh = null;
            auraMaterial = null;
            edgeRibbonMaterial = null;
            sourceRenderer = null;
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
