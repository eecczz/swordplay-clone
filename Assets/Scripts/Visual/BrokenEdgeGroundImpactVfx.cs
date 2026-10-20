using UnityEngine;

namespace Swordplay.Visuals
{
    public sealed class BrokenEdgeGroundImpactVfx : MonoBehaviour
    {
        private const int Segments = 72;
        private LineRenderer[] rings;
        private float age;
        private Color effectColor = new Color(1.2f, 0.35f, 0.08f, 1f);
        private const float Lifetime = 0.72f;

        public static void Spawn(Transform victimRoot, Vector3 contactPoint)
        {
            if (victimRoot == null) return;
            Vector3 ground = new Vector3(contactPoint.x, victimRoot.position.y + 0.035f, contactPoint.z);
            GameObject effect = new GameObject("Broken Edge Ground Impact Ring");
            effect.transform.position = ground;
            BrokenEdgeGroundImpactVfx instance = effect.AddComponent<BrokenEdgeGroundImpactVfx>();
            instance.effectColor = ArenaGameDirector.ImpactWaveColor;
            instance.Build();
        }

        private void Build()
        {
            Material source = Resources.Load<Material>("VFX/M_GroundImpactRing");
            rings = new LineRenderer[3];
            for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                GameObject child = new GameObject($"Shock Ring {ringIndex + 1}");
                child.transform.SetParent(transform, false);
                LineRenderer line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = Segments;
                line.widthMultiplier = ringIndex == 0 ? 0.085f : 0.045f;
                line.numCornerVertices = 3;
                line.numCapVertices = 3;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                if (source != null) line.material = new Material(source);
                for (int i = 0; i < Segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / Segments;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle), ringIndex * 0.008f, Mathf.Sin(angle)));
                }
                rings[ringIndex] = line;
            }
        }

        private void Update()
        {
            if (rings == null) Build();
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Lifetime);
            for (int i = 0; i < rings.Length; i++)
            {
                float delayed = Mathf.Clamp01((t - i * 0.09f) / (1f - i * 0.09f));
                float radius = Mathf.Lerp(0.18f + i * 0.06f, 2.25f + i * 0.25f, 1f - Mathf.Pow(1f - delayed, 3f));
                rings[i].transform.localScale = new Vector3(radius, 1f, radius);
                Color color = Color.Lerp(Color.Lerp(effectColor, Color.white, 0.68f), new Color(effectColor.r, effectColor.g, effectColor.b, 0f), delayed);
                color.a = (1f - delayed) * (i == 0 ? 0.95f : 0.65f);
                rings[i].startColor = color;
                rings[i].endColor = color;
            }
            if (age >= Lifetime) Destroy(gameObject);
        }
    }
}
