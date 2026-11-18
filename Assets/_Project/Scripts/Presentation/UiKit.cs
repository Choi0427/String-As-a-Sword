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
        public static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        public static Color Invert(Color c) => new Color(1 - c.r, 1 - c.g, 1 - c.b, c.a);
    }
}
