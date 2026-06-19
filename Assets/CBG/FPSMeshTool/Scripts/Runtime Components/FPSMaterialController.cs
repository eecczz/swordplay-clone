using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CBG {

    public enum FPSBodyPart {
        Body,
        Head,
        Arms,
        Legs
    }

    [System.Serializable]
    public struct FPSMaterialEntry {
        public Material material;
        public FPSBodyPart bodyPart;
        public FPSMaterialEntry(Material mat) {
            material = mat;
            bodyPart = GuessBodyPart(mat.name);
        }

        static FPSBodyPart GuessBodyPart(string name) {
            if (name.Contains(" arm")) {
                return FPSBodyPart.Arms;
            }
            if (name.Contains(" head")) {
                return FPSBodyPart.Head;
            }
            if (name.Contains(" leg")) {
                return FPSBodyPart.Legs;
            }
            return FPSBodyPart.Body;
        }
    }

    [System.Serializable]
    public struct RendererEntry {
        public SkinnedMeshRenderer renderer;
        public List<FPSMaterialEntry> materialEntries;

        public RendererEntry(SkinnedMeshRenderer renderer) {
            this.renderer = renderer;
            materialEntries = new List<FPSMaterialEntry>();
            var mats = renderer.sharedMaterials;
            foreach (var mat in mats) {
                materialEntries.Add(new FPSMaterialEntry(mat));
            }
        }
    }

    public class RendererMaterialData {
        public SkinnedMeshRenderer renderer;
        public Material[] originalMaterials;
        public Material[] firstPersonMaterials;

        public RendererMaterialData(SkinnedMeshRenderer rend, Material invisibleMaterial, List<int> hideInFirstPerson) {
            renderer = rend;
            originalMaterials = rend.sharedMaterials;
            
            firstPersonMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < originalMaterials.Length; i++) {
                firstPersonMaterials[i] = hideInFirstPerson.Contains(i) ? invisibleMaterial : originalMaterials[i];
            }
        }

        public void SetFirstPersonMaterials(bool firstPerson) {
            renderer.sharedMaterials = firstPerson ? firstPersonMaterials : originalMaterials;
        }
    }

    public class FPSMaterialController : MonoBehaviour {
        [Tooltip("The renderers found on this GameObject, and the body part each material pertains to.")]
        public List<GameObject> ignoreRenderersOn;
        public List<RendererEntry> renderers;
        public List<FPSBodyPart> hideInFirstPerson = new List<FPSBodyPart> { FPSBodyPart.Head, FPSBodyPart.Arms };
        public Material invisibleMaterial;
        List<RendererMaterialData> materialData;

#if UNITY_EDITOR
        void Reset() {
            BuildRendererList();
            FindInvisibleMaterial();
        }

        public void BuildRendererList() {
            // Clear our renderer list
            renderers = new List<RendererEntry>();
            // Find renderers
            var foundRenderers = new List<SkinnedMeshRenderer>(GetComponentsInChildren<SkinnedMeshRenderer>());
            RemoveUndesiredRenderers(foundRenderers);
            // Add all found renderers to the list
            foreach (var rend in foundRenderers) {
                renderers.Add(new RendererEntry(rend));
            }
        }

        protected virtual void FindInvisibleMaterial() {
            invisibleMaterial = GetMaterialWithName("InvisibleMaterial");
        }

        protected Material GetMaterialWithName(string matName) {
            var guids = AssetDatabase.FindAssets(matName + " t:material");
            return guids.Length > 0
                ? (Material) AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Material))
                : null;
        }

        void RemoveUndesiredRenderers(List<SkinnedMeshRenderer> foundRenderers) {
            if (ignoreRenderersOn != null) {
                List<SkinnedMeshRenderer> ignoreRenderers = new List<SkinnedMeshRenderer>();
                foreach (var ignoreObject in ignoreRenderersOn) {
                    ignoreRenderers.AddRange(ignoreObject.GetComponentsInChildren<SkinnedMeshRenderer>());
                }
                for (int i = foundRenderers.Count - 1; i >= 0; i--) {
                    if (ignoreRenderers.Contains(foundRenderers[i])) {
                        foundRenderers.RemoveAt(i);
                    }
                }
            }
        }

        public bool RendererDataIsStale() {
            var foundRenderers = new List<SkinnedMeshRenderer>(GetComponentsInChildren<SkinnedMeshRenderer>());
            RemoveUndesiredRenderers(foundRenderers);
            if (foundRenderers.Count != renderers.Count) {
                return true;
            }
            for (int i = 0; i < foundRenderers.Count; i++) {
                if (renderers[i].renderer != foundRenderers[i]) {
                    return true;
                }
                var mats = foundRenderers[i].sharedMaterials;
                if (mats.Length != renderers[i].materialEntries.Count) {
                    return true;
                }
                for (int j = 0; j < mats.Length; j++) {
                    if (renderers[i].materialEntries[j].material != mats[j]) {
                        return true;
                    }
                }
            }
            return false;
        }

#endif

        void Awake() {
            BuildMaterialData();
        }

        void BuildMaterialData() {
            materialData = new List<RendererMaterialData>();
            List<int> hideIndices = new List<int>();
            foreach (var rendEntry in renderers) {
                hideIndices.Clear();
                var rend = rendEntry.renderer;
                for (int i = 0; i < rendEntry.materialEntries.Count; i++) {
                    if (hideInFirstPerson.Contains(rendEntry.materialEntries[i].bodyPart)) {
                        hideIndices.Add(i);
                    }
                }
                // Only create a materialData entry if the renderer has materials we want to hide
                if (hideIndices.Count > 0) {
                    materialData.Add(new RendererMaterialData(rend, invisibleMaterial, hideIndices));
                }
            }
        }

        public void OnPerspectiveChanged(bool firstPerson) {
            foreach (var rendEntry in materialData) {
                rendEntry.SetFirstPersonMaterials(firstPerson);
            }
        }

        public void SetFirstPerson() {
            OnPerspectiveChanged(true);
        }

        public void SetThirdPerson() {
            OnPerspectiveChanged(false);
        }
    }
}