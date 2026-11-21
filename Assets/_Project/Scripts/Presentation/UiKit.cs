using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Small builders for UI made in code from Figma frames (1920x1080, y down from the top-left corner).</summary>
    public static class UiKit
    {
        public static RectTransform Rect(Transform parent, string name) => PrototypeView.MakeRect(parent, name, Vector2.zero, Vector2.zero);
        public static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size) => PrototypeView.MakeRect(parent, name, position, size);
        public static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero;
            return rect;
        }
        /// <summary>Figma coordinates: (left, top) from the frame's top-left corner, y down.</summary>
        public static RectTransform TopLeft(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        public static UnityEngine.UI.Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
        public static TMP_Text Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align, TMP_FontAsset font)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text; label.fontSize = size; label.color = color; label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }
        /// <summary>
        /// The card fill of the battle screens: Figma's black at 90%. Figma blends in sRGB; the project blends in linear
        /// space, where the same look needs 1 - 0.1^2.2.
        /// </summary>
        public static readonly Color CardFill = new Color(0, 0, 0, 1 - Mathf.Pow(.1f, 2.2f));
        /// <summary>An inside stroke as four edges, so whatever is behind still shows through the card's fill.</summary>
        public static void Stroke(RectTransform card, Color color, float width)
        {
            for (int side = 0; side < 4; side++)
            {
                var edge = Image(Rect(card, "Stroke"), color).rectTransform;
                bool vertical = side >= 2;
                edge.anchorMin = new Vector2(side == 3 ? 1 : 0, side == 1 ? 1 : 0);
                edge.anchorMax = new Vector2(side == 2 ? 0 : 1, side == 0 ? 0 : 1);
                edge.pivot = new Vector2(side == 3 ? 1 : 0, side == 1 ? 1 : 0);
                edge.anchoredPosition = Vector2.zero;
                edge.sizeDelta = vertical ? new Vector2(width, 0) : new Vector2(0, width);
            }
        }
        public static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        public static Color Invert(Color c) => new Color(1 - c.r, 1 - c.g, 1 - c.b, c.a);
    }
}
