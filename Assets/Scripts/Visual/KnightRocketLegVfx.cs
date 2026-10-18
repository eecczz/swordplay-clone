using UnityEngine;

namespace Swordplay.Visuals
{
    [DisallowMultipleComponent]
    public sealed class KnightRocketLegVfx : MonoBehaviour
    {
        [SerializeField] private Transform leftUpperLeg;
        [SerializeField] private Transform rightUpperLeg;
        [SerializeField] private Material flameMaterial;
        [SerializeField] private Color effectColor = new Color(1f, 0.35f, 0.03f, 1f);
        private ParticleSystem centerFlame;
        private static Mesh roundedParticleMesh;

        public Color EffectColor => effectColor;

        public void SetEffectColor(Color color)
        {
            effectColor = color;
            Rebuild();
        }

        public void Configure(Animator animator, Material material, Color color)
        {
            leftUpperLeg = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) : null;
            rightUpperLeg = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
            flameMaterial = material;
            effectColor = color;
            Rebuild();
        }

        private void Awake()
        {
            if (centerFlame == null) Rebuild();
        }

        private void Rebuild()
        {
            Transform existing = transform.Find("Knight Rocket Flames");
            if (existing != null) DestroyImmediateSafe(existing.gameObject);
            Transform holder = new GameObject("Knight Rocket Flames").transform;
            holder.SetParent(transform, false);
            centerFlame = CreateFlame(holder, "Center Rocket Flame");
            UpdateEmitterPositions();
        }

        private ParticleSystem CreateFlame(Transform parent, string objectName)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.45f);
            main.startSize3D = true;
            // Different X/Y/Z sizes turn the shared round mesh into varied flame ellipsoids.
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.13f, 0.27f);
            main.startSizeZ = 0.08f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.22f, 0.22f);
            main.maxParticles = 160;
            var emission = system.emission;
            // One emitter, but enough normal-sized ellipses to fill the span that the old
            // left/right emitters occupied. Individual particles deliberately stay small.
            emission.rateOverTime = 118f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Rectangle;
            shape.scale = new Vector3(0.42f, 0.13f, 1f);
            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(effectColor, Color.white, 0.78f), 0f), new GradientColorKey(effectColor, 0.45f), new GradientColorKey(effectColor * 0.42f, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.16f, 1f), new Keyframe(0.72f, 0.62f), new Keyframe(1f, 0f)));
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 2.4f;
            noise.scrollSpeed = 1.8f;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetRoundedParticleMesh();
            renderer.sortMode = ParticleSystemSortMode.Distance;
            // A white shared additive material is tinted by the particle gradient above.
            // Do not instantiate editor-only material copies into every scene object.
            if (Application.isPlaying && flameMaterial != null)
            {
                Material runtimeMaterial = new Material(flameMaterial);
                runtimeMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                renderer.sharedMaterial = runtimeMaterial;
            }
            else
            {
                renderer.sharedMaterial = flameMaterial;
            }
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return system;
        }

        private void LateUpdate() => UpdateEmitterPositions();

        private void UpdateEmitterPositions()
        {
            if (centerFlame == null || leftUpperLeg == null || rightUpperLeg == null) return;

            Vector3 left = leftUpperLeg.position;
            Vector3 right = rightUpperLeg.position;
            Vector3 lateral = right - left;
            float width = Mathf.Clamp(lateral.magnitude * 1.15f, 0.34f, 0.72f);
            if (lateral.sqrMagnitude < 0.0001f) lateral = transform.right;

            centerFlame.transform.position = (left + right) * 0.5f + Vector3.down * 0.04f;
            centerFlame.transform.rotation = Quaternion.LookRotation(Vector3.down, lateral.normalized);
            var shape = centerFlame.shape;
            shape.scale = new Vector3(width, 0.13f, 1f);
            if (Application.isPlaying && !centerFlame.isPlaying) centerFlame.Play();
        }

        private static void DestroyImmediateSafe(GameObject target)
        {
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }

        private static Mesh GetRoundedParticleMesh()
        {
            if (roundedParticleMesh != null) return roundedParticleMesh;

            const int longitude = 10;
            const int latitude = 6;
            Vector3[] vertices = new Vector3[(latitude + 1) * (longitude + 1)];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[latitude * longitude * 6];

            int vertexIndex = 0;
            for (int y = 0; y <= latitude; y++)
            {
                float v = y / (float)latitude;
                float polar = v * Mathf.PI;
                float ring = Mathf.Sin(polar) * 0.5f;
                float vertical = Mathf.Cos(polar) * 0.5f;
                for (int x = 0; x <= longitude; x++)
                {
                    float u = x / (float)longitude;
                    float azimuth = u * Mathf.PI * 2f;
                    Vector3 point = new Vector3(Mathf.Cos(azimuth) * ring, vertical, Mathf.Sin(azimuth) * ring);
                    vertices[vertexIndex] = point;
                    normals[vertexIndex] = point.normalized;
                    uv[vertexIndex] = new Vector2(u, v);
                    vertexIndex++;
                }
            }

            int triangleIndex = 0;
            for (int y = 0; y < latitude; y++)
            {
                for (int x = 0; x < longitude; x++)
                {
                    int current = y * (longitude + 1) + x;
                    int next = current + longitude + 1;
                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            roundedParticleMesh = new Mesh { name = "Knight Rocket Rounded Particle" };
            roundedParticleMesh.hideFlags = HideFlags.HideAndDontSave;
            roundedParticleMesh.vertices = vertices;
            roundedParticleMesh.normals = normals;
            roundedParticleMesh.uv = uv;
            roundedParticleMesh.triangles = triangles;
            roundedParticleMesh.RecalculateBounds();
            return roundedParticleMesh;
        }
    }
}
