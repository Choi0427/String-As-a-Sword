using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// A key prompt on a card like the pause screen's (black at 90%, inside stroke #D9D9D9, Orbit text): the key on a light
    /// keycap, then a line of text. The card fits its text; with no text it is the keycap alone. Used by the prologue's J
    /// prompt and the battle tutorial.
    /// </summary>
    public sealed class KeyPromptCard
    {
        private static readonly Color Ink = UiKit.Hex(0xD9D9D9), KeyInk = UiKit.Hex(0x0C0C0C);
        private const float Padding = 22, Gap = 20, Stroke = 4;
        private readonly RectTransform key;
        private readonly TMP_Text keyLabel, label;
        private readonly float height, keySize;
        private string shownKey, shownText;
        public RectTransform Rect { get; }
        public Vector2 Size => Rect.sizeDelta;
        public string Key => shownKey;
        public string Text => shownText;
        public bool Visible => Rect.gameObject.activeSelf;

        /// <param name="height">Card height; the keycap is <paramref name="height"/> - 30 square (wider for long keys).</param>
        public KeyPromptCard(RectTransform parent, string name, ArtSet art, TMP_FontAsset latinFont, float height, float keyFontSize, float textFontSize)
        {
            var itemFont = UiFonts.Korean(art.screenFont != null ? art.screenFont : art.menuFont, latinFont);
            var keyFont = art.screenTitleFont != null ? art.screenTitleFont : itemFont;
            this.height = height; keySize = height - 30;
            Rect = PrototypeView.MakeRect(parent, name, Vector2.zero, new Vector2(height * 3, height));
            UiKit.Image(UiKit.Stretch(UiKit.Rect(Rect, "Fill")), UiKit.CardFill);
            UiKit.Stroke(Rect, Ink, Stroke);
            key = PrototypeView.MakeRect(Rect, "Key", Vector2.zero, new Vector2(keySize, keySize));
            key.anchorMin = key.anchorMax = new Vector2(0, .5f);
            UiKit.Image(key, Ink);
            keyLabel = UiKit.Label(UiKit.Stretch(UiKit.Rect(key, "Key Label")), "", keyFontSize, KeyInk, TextAlignmentOptions.Center, keyFont);
            label = UiKit.Label(PrototypeView.MakeRect(Rect, "Label", Vector2.zero, new Vector2(10, height - 20)), "", textFontSize, Ink, TextAlignmentOptions.Left, itemFont);
            label.characterSpacing = -5;
            var labelRect = label.rectTransform; labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0, .5f);
            Rect.gameObject.SetActive(false);
        }
        /// <summary>Shows the card with this key and text, resizing it when either changed.</summary>
        public void Show(string keyText, string text)
        {
            Rect.gameObject.SetActive(true);
            if (keyText == shownKey && text == shownText) return;
            shownKey = keyText; shownText = text ?? "";
            keyLabel.text = keyText;
            float keyWidth = Mathf.Max(keySize, keyLabel.GetPreferredValues(keyText).x + 24);
            key.sizeDelta = new Vector2(keyWidth, keySize);
            key.anchoredPosition = new Vector2(Padding + keyWidth * .5f, 0);
            label.text = shownText;
            float textWidth = shownText.Length > 0 ? label.GetPreferredValues(shownText).x + 4 : 0;
            label.gameObject.SetActive(textWidth > 0);
            label.rectTransform.sizeDelta = new Vector2(textWidth, height - 20);
            label.rectTransform.anchoredPosition = new Vector2(Padding + keyWidth + Gap, 0);
            Rect.sizeDelta = new Vector2(Padding + keyWidth + (textWidth > 0 ? Gap + textWidth + 8 : 0) + Padding, height);
        }
        public void Hide() => Rect.gameObject.SetActive(false);
        /// <summary>The keycap kicks on the beat: <paramref name="beatPhase"/> is the fraction of the beat gone (0 on the beat).</summary>
        public void Pulse(float beatPhase) => key.localScale = Vector3.one * (1 + .14f * Mathf.Exp(-Mathf.Repeat(beatPhase, 1) * 5));
    }
}
