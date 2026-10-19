using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Swordplay.Visuals
{
    [DisallowMultipleComponent]
    public sealed class MedievalButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        [SerializeField] private Image target;
        [SerializeField] private Color idleColor = Color.clear;
        [SerializeField] private Color hoverColor = new(1f, 1f, 1f, 0.96f);

        public void Configure(Image targetImage, Color idle, Color hover)
        {
            target = targetImage;
            idleColor = idle;
            hoverColor = hover;
            Apply(false);
        }

        private void OnEnable() => Apply(false);
        public void OnPointerEnter(PointerEventData eventData) => Apply(true);
        public void OnPointerExit(PointerEventData eventData) => Apply(false);
        public void OnSelect(BaseEventData eventData) => Apply(true);
        public void OnDeselect(BaseEventData eventData) => Apply(false);
        private void OnDisable() => Apply(false);

        private void Apply(bool hovered)
        {
            if (target != null) target.color = hovered ? hoverColor : idleColor;
        }
    }
}
