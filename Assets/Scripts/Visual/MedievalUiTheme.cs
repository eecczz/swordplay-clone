using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Swordplay.Visuals
{
    /// <summary>
    /// Applies a code-only medieval presentation to the authored arena UI. Nothing is
    /// written back to the scene: the original hierarchy remains fully editable.
    /// </summary>
    public static class MedievalUiTheme
    {
        private static readonly Color Parchment = new(0.82f, 0.70f, 0.47f, 0.94f);
        private static readonly Color PaleGold = new(1f, 0.88f, 0.55f, 1f);
        private static readonly Color AntiqueGold = new(0.56f, 0.36f, 0.13f, 0.94f);
        private static readonly Color Iron = new(0.075f, 0.055f, 0.045f, 0.90f);
        private static readonly Color Crimson = new(0.38f, 0.075f, 0.055f, 0.96f);
        private static readonly Color TransparentIron = new(0.075f, 0.055f, 0.045f, 0f);

        private static Font medievalFont;
        private static Sprite framedSprite;

        public static void Apply(GameObject mainPanel, GameObject settingsPanel, GameObject shopPanel,
            Text startBanner, Text currencyText)
        {
            EnsureResources();
            StylePanel(settingsPanel);
            StylePanel(shopPanel);
            StyleMainMenu(mainPanel);

            StyleText(startBanner, true, 2.2f);
            StyleText(currencyText, true, 1.4f);
        }

        private static void EnsureResources()
        {
            if (medievalFont == null)
            {
                medievalFont = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Palatino Linotype", "Book Antiqua", "Georgia", "Times New Roman", "Malgun Gothic"
                }, 42);
            }

            if (framedSprite != null) return;

            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Medieval UI Frame",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color clear = Color.white;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                bool cornerStud = (x < 7 || x >= size - 7) && (y < 7 || y >= size - 7)
                    && Mathf.Abs(x % (size - 1) - y % (size - 1)) <= 3;
                texture.SetPixel(x, y, edge < 2 || cornerStud ? PaleGold
                    : edge < 4 ? AntiqueGold : clear);
            }
            texture.Apply(false, true);
            framedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(7f, 7f, 7f, 7f));
            framedSprite.name = "Runtime Medieval UI Frame";
            framedSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        private static void StyleMainMenu(GameObject panel)
        {
            if (panel == null) return;
            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
                StyleText(text, true, 1.7f);

            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
                StyleButton(button, true);
        }

        private static void StylePanel(GameObject panel)
        {
            if (panel == null) return;

            Image panelImage = panel.GetComponent<Image>();
            if (panelImage != null)
            {
                panelImage.sprite = framedSprite;
                panelImage.type = Image.Type.Sliced;
                panelImage.color = new Color(Iron.r, Iron.g, Iron.b, 0.82f);
            }

            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
                StyleText(text, false, 1.25f);
            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
                StyleButton(button, false);
            foreach (Slider slider in panel.GetComponentsInChildren<Slider>(true))
                StyleSlider(slider);
            foreach (Toggle toggle in panel.GetComponentsInChildren<Toggle>(true))
                StyleToggle(toggle);
        }

        private static void StyleText(Text text, bool prominent, float outlineWidth)
        {
            if (text == null) return;
            if (medievalFont != null) text.font = medievalFont;
            text.fontStyle = prominent ? FontStyle.Bold : FontStyle.Normal;
            text.color = prominent ? PaleGold : Parchment;

            Outline outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.025f, 0.012f, 0.008f, 0.95f);
            outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
            outline.useGraphicAlpha = true;

            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow == null || shadow == outline)
                shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(2.5f, -3f);
            shadow.useGraphicAlpha = true;
        }

        private static void StyleButton(Button button, bool hoverOnly)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image == null) return;

            image.sprite = framedSprite;
            image.type = Image.Type.Sliced;
            Color idle = hoverOnly ? TransparentIron : new Color(Iron.r, Iron.g, Iron.b, 0.86f);
            Color hover = new(Crimson.r, Crimson.g, Crimson.b, 0.96f);
            image.color = idle;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.62f, 0.32f, 1f);
            colors.disabledColor = new Color(0.24f, 0.21f, 0.18f, 0.45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            MedievalButtonHover hoverEffect = button.GetComponent<MedievalButtonHover>();
            if (hoverEffect == null) hoverEffect = button.gameObject.AddComponent<MedievalButtonHover>();
            hoverEffect.Configure(image, idle, hover);
            foreach (Text label in button.GetComponentsInChildren<Text>(true))
                StyleText(label, true, 1.5f);
        }

        private static void StyleSlider(Slider slider)
        {
            if (slider == null) return;
            if (slider.targetGraphic is Image handle)
            {
                handle.sprite = framedSprite;
                handle.type = Image.Type.Sliced;
                handle.color = PaleGold;
            }
            if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill))
                fill.color = Crimson;
            Transform background = slider.transform.Find("Background");
            if (background != null && background.TryGetComponent(out Image backgroundImage))
                backgroundImage.color = new Color(Iron.r, Iron.g, Iron.b, 0.88f);
        }

        private static void StyleToggle(Toggle toggle)
        {
            if (toggle == null) return;
            if (toggle.targetGraphic is Image frame)
            {
                frame.sprite = framedSprite;
                frame.type = Image.Type.Sliced;
                frame.color = new Color(Iron.r, Iron.g, Iron.b, 0.9f);
            }
            if (toggle.graphic != null) toggle.graphic.color = PaleGold;
        }
    }

    [DisallowMultipleComponent]
    public sealed class MedievalButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        private Image target;
        private Color idleColor;
        private Color hoverColor;

        public void Configure(Image targetImage, Color idle, Color hover)
        {
            target = targetImage;
            idleColor = idle;
            hoverColor = hover;
            if (target != null) target.color = idleColor;
        }

        public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);
        public void OnPointerExit(PointerEventData eventData) => SetHovered(false);
        public void OnSelect(BaseEventData eventData) => SetHovered(true);
        public void OnDeselect(BaseEventData eventData) => SetHovered(false);

        private void OnDisable() => SetHovered(false);
        private void SetHovered(bool hovered)
        {
            if (target != null) target.color = hovered ? hoverColor : idleColor;
        }
    }
}
