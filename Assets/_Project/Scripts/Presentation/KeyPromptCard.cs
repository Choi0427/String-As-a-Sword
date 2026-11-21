using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>One piece of a key prompt: a key on a keycap, a line of text, or small text (such as an "or").</summary>
    public readonly struct PromptPart
    {
        public enum Kind { Key, Text, Small }
        public Kind Type { get; }
        public string Value { get; }
        private PromptPart(Kind type, string value) { Type = type; Value = value; }
        public static PromptPart Key(string key) => new PromptPart(Kind.Key, key);
        public static PromptPart Text(string text) => new PromptPart(Kind.Text, text);
        public static PromptPart Small(string text) => new PromptPart(Kind.Small, text);
        public override string ToString() => Type == Kind.Key ? "[" + Value + "]" : Value;
    }

    /// <summary>
    /// A key prompt on a card like the pause screen's (black at 90%, inside stroke #D9D9D9, Orbit text): keys on light keycaps
    /// and text, left to right, such as "[K] 막기 or [WASD] 회피". The card fits its parts. Used by the prologue's J prompt and
    /// the tutorial.
    /// </summary>
    public sealed class KeyPromptCard
    {
        private static readonly Color Ink = UiKit.Hex(0xD9D9D9), KeyInk = UiKit.Hex(0x0C0C0C), SmallInk = UiKit.Hex(0x929292);
        private const float Padding = 22, Gap = 16, SmallGap = 10, Stroke = 4;
        private readonly TMP_FontAsset itemFont, keyFont;
        private readonly float height, keySize, keyFontSize, textFontSize;
        private readonly List<RectTransform> keys = new List<RectTransform>();
        private readonly List<TMP_Text> keyLabels = new List<TMP_Text>(), labels = new List<TMP_Text>();
        private PromptPart[] shown = new PromptPart[0];
        public RectTransform Rect { get; }
        public Vector2 Size => Rect.sizeDelta;
        public IReadOnlyList<PromptPart> Parts => shown;
        /// <summary>The first key shown ("" when none).</summary>
        public string Key { get { foreach (var p in shown) if (p.Type == PromptPart.Kind.Key) return p.Value; return ""; } }
        /// <summary>All the parts as one line, keys in brackets: "[K] 막기 or [WASD] 회피".</summary>
        public string Text => string.Join(" ", System.Array.ConvertAll(shown, p => p.ToString()));
        public bool Visible => Rect.gameObject.activeSelf;

        /// <param name="height">Card height; keycaps are <paramref name="height"/> - 30 tall (wider for long keys).</param>
        public KeyPromptCard(RectTransform parent, string name, ArtSet art, TMP_FontAsset latinFont, float height, float keyFontSize, float textFontSize)
        {
            itemFont = UiFonts.Korean(art.screenFont != null ? art.screenFont : art.menuFont, latinFont);
            keyFont = art.screenTitleFont != null ? art.screenTitleFont : itemFont;
            this.height = height; keySize = height - 30; this.keyFontSize = keyFontSize; this.textFontSize = textFontSize;
            Rect = PrototypeView.MakeRect(parent, name, Vector2.zero, new Vector2(height * 3, height));
            UiKit.Image(UiKit.Stretch(UiKit.Rect(Rect, "Fill")), UiKit.CardFill);
            UiKit.Stroke(Rect, Ink, Stroke);
            Rect.gameObject.SetActive(false);
        }
        /// <summary>A key and a line of text (either may be empty).</summary>
        public void Show(string key, string text)
        {
            if (string.IsNullOrEmpty(text)) Show(PromptPart.Key(key));
            else if (string.IsNullOrEmpty(key)) Show(PromptPart.Text(text));
            else Show(PromptPart.Key(key), PromptPart.Text(text));
        }
        /// <summary>Shows the card with these parts, laying it out again when they changed.</summary>
        public void Show(params PromptPart[] parts)
        {
            Rect.gameObject.SetActive(true);
            if (Same(parts)) return;
            shown = (PromptPart[])parts.Clone();
            int keyCount = 0, labelCount = 0;
            float x = Padding;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i > 0) x += part.Type == PromptPart.Kind.Small || parts[i - 1].Type == PromptPart.Kind.Small ? SmallGap : Gap;
                if (part.Type == PromptPart.Kind.Key)
                {
                    var (key, label) = KeyAt(keyCount++);
                    label.text = part.Value;
                    float width = Mathf.Max(keySize, label.GetPreferredValues(part.Value).x + 24);
                    key.sizeDelta = new Vector2(width, keySize);
                    key.anchoredPosition = new Vector2(x + width * .5f, 0);
                    x += width;
                }
                else
                {
                    var label = LabelAt(labelCount++);
                    bool small = part.Type == PromptPart.Kind.Small;
                    label.fontSize = small ? textFontSize * .6f : textFontSize;
                    label.color = small ? SmallInk : Ink;
                    label.text = part.Value;
                    float width = label.GetPreferredValues(part.Value).x + 4;
                    label.rectTransform.sizeDelta = new Vector2(width, height - 20);
                    label.rectTransform.anchoredPosition = new Vector2(x, 0);
                    x += width;
                }
            }
            for (int i = 0; i < keys.Count; i++) keys[i].gameObject.SetActive(i < keyCount);
            for (int i = 0; i < labels.Count; i++) labels[i].gameObject.SetActive(i < labelCount);
            Rect.sizeDelta = new Vector2(x + Padding + 8, height);
        }
        public void Hide() => Rect.gameObject.SetActive(false);
        /// <summary>The keycaps kick on the beat: <paramref name="beatPhase"/> is the fraction of the beat gone (0 on the beat).</summary>
        public void Pulse(float beatPhase)
        {
            float scale = 1 + .14f * Mathf.Exp(-Mathf.Repeat(beatPhase, 1) * 5);
            foreach (var key in keys) key.localScale = Vector3.one * scale;
        }
        private bool Same(PromptPart[] parts)
        {
            if (parts.Length != shown.Length) return false;
            for (int i = 0; i < parts.Length; i++) if (parts[i].Type != shown[i].Type || parts[i].Value != shown[i].Value) return false;
            return true;
        }
        private (RectTransform, TMP_Text) KeyAt(int index)
        {
            while (keys.Count <= index)
            {
                var key = PrototypeView.MakeRect(Rect, "Key", Vector2.zero, new Vector2(keySize, keySize));
                key.anchorMin = key.anchorMax = new Vector2(0, .5f);
                UiKit.Image(key, Ink);
                keyLabels.Add(UiKit.Label(UiKit.Stretch(UiKit.Rect(key, "Key Label")), "", keyFontSize, KeyInk, TextAlignmentOptions.Center, keyFont));
                keys.Add(key);
            }
            return (keys[index], keyLabels[index]);
        }
        private TMP_Text LabelAt(int index)
        {
            while (labels.Count <= index)
            {
                var label = UiKit.Label(PrototypeView.MakeRect(Rect, "Label", Vector2.zero, new Vector2(10, height - 20)), "", textFontSize, Ink, TextAlignmentOptions.Left, itemFont);
                label.characterSpacing = -5;
                var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
                labels.Add(label);
            }
            return labels[index];
        }
    }
}
