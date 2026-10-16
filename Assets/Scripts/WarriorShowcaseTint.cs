using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WarriorShowcaseTint : MonoBehaviour
{
    [SerializeField] private Color tintColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.92f;

    private static readonly int WarriorTintColorId = Shader.PropertyToID("_WarriorTintColor");
    private static readonly int WarriorTintStrengthId = Shader.PropertyToID("_WarriorTintStrength");
    private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
    private static readonly int LightTintId = Shader.PropertyToID("_LightTint");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    public void Configure(Color color, float strength = 0.92f)
    {
        tintColor = color;
        tintColor.a = 1f;
        tintStrength = Mathf.Clamp01(strength);
        ApplyTint();
    }

    private void OnEnable() => ApplyTint();
    private void OnValidate() => ApplyTint();

    private void ApplyTint()
    {
        var block = new MaterialPropertyBlock();
        Color shadowTint = Color.Lerp(tintColor, Color.black, 0.58f);
        Color lightTint = Color.Lerp(tintColor, Color.white, 0.48f);
        Color rimTint = Color.Lerp(tintColor, Color.white, 0.30f);
        Color outlineTint = Color.Lerp(tintColor, Color.black, 0.82f);

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            block.Clear();
            block.SetColor(WarriorTintColorId, tintColor);
            block.SetFloat(WarriorTintStrengthId, tintStrength);
            block.SetColor(ShadowColorId, shadowTint);
            block.SetColor(LightTintId, lightTint);
            block.SetColor(RimColorId, rimTint);
            block.SetColor(OutlineColorId, outlineTint);
            renderer.SetPropertyBlock(block);
        }
    }
}
