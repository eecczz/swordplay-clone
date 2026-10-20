using UnityEngine;
using UnityEngine.Rendering;

namespace Swordplay.Visuals
{
    /// <summary>
    /// Runtime-built, camera-facing combat effects. Hits use round painted blast shapes and
    /// short radial shards; guards use clean expanding ripples. No scene prefab is required.
    /// </summary>
    public sealed class BrokenEdgeCombatImpactVfx : MonoBehaviour
    {
        private enum ImpactStyle { Hit, Guard }

        private struct Stroke
        {
            public LineRenderer line;
            public Color color;
            public float delay;
            public float endScale;
        }

        private ImpactStyle style;
        private Stroke[] strokes;
        private ParticleSystem paintedBurst;
        private float age;
        private float lifetime;
        private Camera targetCamera;

        public static void SpawnHit(Transform victimRoot, Vector3 contactPoint)
        {
            if (victimRoot == null) return;

            BrokenEdgeGroundImpactVfx.Spawn(victimRoot, contactPoint);
            BrokenEdgeCombatImpactVfx effect = Create("Broken Edge Hit Explosion", contactPoint, ImpactStyle.Hit);
            effect.BuildHit();
        }

        public static void SpawnGuard(Transform defenderRoot, Vector3 contactPoint)
        {
            if (defenderRoot == null) return;

            BrokenEdgeCombatImpactVfx effect = Create("Broken Edge Guard Ripple", contactPoint, ImpactStyle.Guard);
            effect.BuildGuard(ArenaGameDirector.ImpactWaveColor);
        }

        private static BrokenEdgeCombatImpactVfx Create(string effectName, Vector3 position, ImpactStyle impactStyle)
        {
            GameObject root = new GameObject(effectName);
            root.transform.position = position;
            BrokenEdgeCombatImpactVfx effect = root.AddComponent<BrokenEdgeCombatImpactVfx>();
            effect.style = impactStyle;
            effect.targetCamera = Camera.main;
            effect.FaceCamera();
            return effect;
        }

        private void BuildHit()
        {
            lifetime = 0.5f;
            Color cream = new Color(1f, 0.76f, 0.43f, 1f);
            Color hot = new Color(1f, 0.97f, 0.82f, 1f);
            Material lineMaterial = Resources.Load<Material>("VFX/M_GroundImpactRing");

            const int blobCount = 3;
            const int shardCount = 18;
            strokes = new Stroke[blobCount + shardCount];

            // Thick, irregular closed outlines make the explosion read as a painted blast,
            // rather than a geometric ring.
            for (int blob = 0; blob < blobCount; blob++)
            {
                LineRenderer line = CreateLine($"Painted Blast {blob + 1}", lineMaterial, true, 22, 0.17f - blob * 0.025f);
                for (int i = 0; i < line.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2f / line.positionCount;
                    float wobble = 1f + Mathf.Sin(angle * (3f + blob) + blob * 1.71f) * 0.23f
                        + Mathf.Sin(angle * 7f + blob) * 0.09f;
                    float radius = (0.38f + blob * 0.09f) * wobble;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius * 1.12f, Mathf.Sin(angle) * radius, blob * 0.006f));
                }
                strokes[blob] = new Stroke
                {
                    line = line,
                    color = blob == 0 ? hot : cream,
                    delay = blob * 0.035f,
                    endScale = 1.35f + blob * 0.12f
                };
            }

            // Short tapered shards are deliberately varied and stop before becoming long lines.
            for (int i = 0; i < shardCount; i++)
            {
                float angle = i * Mathf.PI * 2f / shardCount + Mathf.Sin(i * 9.17f) * 0.13f;
                float length = 0.42f + Mathf.Repeat(i * 0.317f, 1f) * 0.58f;
                float inner = 0.31f + Mathf.Repeat(i * 0.173f, 1f) * 0.18f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 tangent = new Vector3(-direction.y, direction.x, 0f);
                LineRenderer line = CreateLine($"Blast Shard {i + 1}", lineMaterial, false, 3, 0.035f + (i % 3) * 0.012f);
                line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.28f, 1f), new Keyframe(1f, 0f));
                line.SetPosition(0, direction * inner);
                line.SetPosition(1, direction * (inner + length * 0.48f) + tangent * Mathf.Sin(i * 4.3f) * 0.045f);
                line.SetPosition(2, direction * (inner + length));
                strokes[blobCount + i] = new Stroke
                {
                    line = line,
                    color = i % 4 == 0 ? cream : Color.white,
                    delay = 0.025f + (i % 4) * 0.012f,
                    endScale = 1.2f
                };
            }

            paintedBurst = CreatePaintedBurst(cream, hot);
            paintedBurst.Play();
        }

        private void BuildGuard(Color palette)
        {
            lifetime = 0.58f;
            Material lineMaterial = Resources.Load<Material>("VFX/M_GroundImpactRing");
            strokes = new Stroke[3];
            for (int ring = 0; ring < strokes.Length; ring++)
            {
                LineRenderer line = CreateLine($"Guard Wave {ring + 1}", lineMaterial, true, 48, ring == 0 ? 0.095f : 0.052f);
                for (int i = 0; i < line.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2f / line.positionCount;
                    float ripple = 1f + Mathf.Sin(angle * 6f + ring * 1.8f) * 0.035f;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle) * ripple, Mathf.Sin(angle) * ripple, ring * 0.008f));
                }
                strokes[ring] = new Stroke
                {
                    line = line,
                    color = Color.Lerp(palette, Color.white, ring == 0 ? 0.66f : 0.28f),
                    delay = ring * 0.09f,
                    endScale = 1.45f + ring * 0.32f
                };
            }
        }

        private ParticleSystem CreatePaintedBurst(Color cream, Color hot)
        {
            GameObject child = new GameObject("Round Painted Blast Pieces");
            child.transform.SetParent(transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.82f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.34f, 0.78f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSizeZ = 0.1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.maxParticles = 40;

            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.11f;
            shape.radiusThickness = 1f;

            var color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(hot, 0f), new GradientColorKey(cream, 0.38f), new GradientColorKey(new Color(1f, 0.38f, 0.08f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.52f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.15f), new Keyframe(0.16f, 1f), new Keyframe(0.72f, 0.78f), new Keyframe(1f, 0f)));

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.YoungestInFront;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = Resources.Load<Material>("VFX/M_KnightRocketFlame");
            return system;
        }

        private LineRenderer CreateLine(string lineName, Material material, bool loop, int count, float width)
        {
            GameObject child = new GameObject(lineName);
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = count;
            line.widthMultiplier = width;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            return line;
        }

        private void LateUpdate()
        {
            FaceCamera();
            age += Time.deltaTime;
            float normalizedAge = lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;

            if (strokes != null)
            {
                for (int i = 0; i < strokes.Length; i++)
                {
                    Stroke stroke = strokes[i];
                    if (stroke.line == null) continue;
                    float local = Mathf.Clamp01((normalizedAge - stroke.delay) / Mathf.Max(0.01f, 1f - stroke.delay));
                    float eased = 1f - Mathf.Pow(1f - local, 3f);
                    float startScale = style == ImpactStyle.Hit ? 0.55f : 0.08f;
                    float scale = Mathf.Lerp(startScale, stroke.endScale, eased);
                    stroke.line.transform.localScale = Vector3.one * scale;
                    Color faded = stroke.color;
                    float fadeStart = style == ImpactStyle.Hit ? 0.48f : 0.34f;
                    faded.a = local < fadeStart ? Mathf.InverseLerp(0f, 0.08f, local) : 1f - Mathf.InverseLerp(fadeStart, 1f, local);
                    stroke.line.startColor = faded;
                    stroke.line.endColor = faded;
                }
            }

            if (age >= lifetime) Destroy(gameObject);
        }

        private void FaceCamera()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;
            Vector3 towardCamera = targetCamera.transform.position - transform.position;
            if (towardCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-towardCamera.normalized, targetCamera.transform.up);
        }
    }
}
