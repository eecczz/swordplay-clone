using System.Collections.Generic;
using UnityEngine;

namespace Swordplay.Visuals
{
    /// <summary>
    /// Compatibility component for scenes saved with the retired per-material palette.
    /// It only clears those old overrides; BrokenEdgeStyle now handles the look by shader.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BrokenEdgeCharacterPalette : MonoBehaviour
    {
        private void OnEnable() => ClearLegacyOverrides();

        private void OnValidate()
        {
            if (isActiveAndEnabled) ClearLegacyOverrides();
        }

        [ContextMenu("Apply Broken Edge Character Palette")]
        public void ApplyPalette()
        {
            ClearLegacyOverrides();
        }

        private void ClearLegacyOverrides()
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    renderer.SetPropertyBlock(null, i);
            }
        }
    }
}
