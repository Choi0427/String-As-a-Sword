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
    /// the tutorial. With the key guide's art given, WASD, J and K are cut from it instead (WASD as the guide's ㅗ cluster),
    /// so the tutorial's cards show the very keys the HUD guide shows.
    /// </summary>
    public sealed class KeyPromptCard
    {
        private static readonly Color Ink = UiKit.Hex(0xD9D9D9), KeyInk = UiKit.Hex(0x0C0C0C), SmallInk = UiKit.Hex(0x929292);
        private const float Padding = 22, Gap = 16, SmallGap = 10, Stroke = 4, GuideMargin = 9;
        // The key guide's art (KeyGuide.png, 1447 x 209): each key's box in its pixels, from the top-left. A keycap is 100 px.
        private const float GuideWidth = 1447, GuideHeight = 209, GuideCap = 100;
        private static readonly Dictionary<string, Rect> GuideCrops = new Dictionary<string, Rect>
        {
            { "WASD", new Rect(0, 0, 314, 208) }, { "J", new Rect(694, 54, 100, 100) }, { "K", new Rect(1171, 54, 100, 100) },
        };
        private readonly TMP_FontAsset itemFont, keyFont;
        private readonly float height, keySize, keyFontSize, textFontSize;
        private readonly List<RectTransform> keys = new List<RectTransform>();
        private readonly List<TMP_Text> keyLabels = new List<TMP_Text>(), labels = new List<TMP_Text>();
        private readonly Sprite guide;
        private readonly Dictionary<string, Sprite> guideSprites = new Dictionary<string, Sprite>();
        private readonly List<UnityEngine.UI.Image> guideKeys = new List<UnityEngine.UI.Image>();
        private PromptPart[] shown = new PromptPart[0];
        public RectTransform Rect { get; }
        public Vector2 Size => Rect.sizeDelta;
        public IReadOnlyList<PromptPart> Parts => shown;
        /// <summary>Keys drawn with the key guide's art (the ㅗ cluster counts as one).</summary>
        public int GuideKeysShown { get; private set; }
        /// <summary>The key guide sprite these keys are cut from (null: plain keycaps only).</summary>
        public Sprite Guide => guide;
        /// <summary>The first key shown ("" when none).</summary>
        public string Key { get { foreach (var p in shown) if (p.Type == PromptPart.Kind.Key) return p.Value; return ""; } }
        /// <summary>All the parts as one line, keys in brackets: "[K] 막기 or [WASD] 회피".</summary>
        public string Text => string.Join(" ", System.Array.ConvertAll(shown, p => p.ToString()));
        public bool Visible => Rect.gameObject.activeSelf;

        /// <param name="height">Card height; keycaps are <paramref name="height"/> - 30 tall (wider for long keys). The card grows
        /// to fit the key guide's ㅗ cluster.</param>
        /// <param name="guideKeys">The key guide's art: WASD, J and K are cut from it. Null keeps the plain keycaps.</param>
        public KeyPromptCard(RectTransform parent, string name, ArtSet art, TMP_FontAsset latinFont, float height, float keyFontSize, float textFontSize, Sprite guideKeys = null)
        {
            guide = guideKeys;
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
            int keyCount = 0, labelCount = 0, guideCount = 0;
            // A guide key is drawn at the plain keycap's size (one 100 px cap = keySize), so the ㅗ cluster sets the card taller.
            float cardHeight = height, guideScale = keySize / GuideCap;
            foreach (var part in parts)
                if (part.Type == PromptPart.Kind.Key && GuideSprite(part.Value) != null)
                    cardHeight = Mathf.Max(cardHeight, GuideCrops[part.Value].height * guideScale + 2 * GuideMargin);
            float x = Padding;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i > 0) x += part.Type == PromptPart.Kind.Small || parts[i - 1].Type == PromptPart.Kind.Small ? SmallGap : Gap;
                var cut = part.Type == PromptPart.Kind.Key ? GuideSprite(part.Value) : null;
                if (cut != null)
                {
                    var image = GuideKeyAt(guideCount++);
                    image.sprite = cut;
                    var size = GuideCrops[part.Value].size * guideScale;
                    image.rectTransform.sizeDelta = size;
                    image.rectTransform.anchoredPosition = new Vector2(x + size.x * .5f, 0);
                    x += size.x;
                }
                else if (part.Type == PromptPart.Kind.Key)
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
                    label.rectTransform.sizeDelta = new Vector2(width, cardHeight - 20);
                    label.rectTransform.anchoredPosition = new Vector2(x, 0);
                    x += width;
                }
            }
            for (int i = 0; i < keys.Count; i++) keys[i].gameObject.SetActive(i < keyCount);
            for (int i = 0; i < labels.Count; i++) labels[i].gameObject.SetActive(i < labelCount);
            for (int i = 0; i < guideKeys.Count; i++) guideKeys[i].gameObject.SetActive(i < guideCount);
            GuideKeysShown = guideCount;
            Rect.sizeDelta = new Vector2(x + Padding + 8, cardHeight);
        }
        public void Hide() => Rect.gameObject.SetActive(false);
        /// <summary>The keycaps kick on the beat: <paramref name="beatPhase"/> is the fraction of the beat gone (0 on the beat).</summary>
        public void Pulse(float beatPhase)
        {
            float scale = 1 + .14f * Mathf.Exp(-Mathf.Repeat(beatPhase, 1) * 5);
            foreach (var key in keys) key.localScale = Vector3.one * scale;
            foreach (var key in guideKeys) key.rectTransform.localScale = Vector3.one * scale;
        }
        /// <summary>The key cut from the key guide's art, or null when there is no art or the guide has no such key.</summary>
        private Sprite GuideSprite(string key)
        {
            if (guide == null || !GuideCrops.TryGetValue(key, out var crop)) return null;
            if (guideSprites.TryGetValue(key, out var sprite)) return sprite;
            // In the sprite's own pixels (bottom-left origin), so a re-import at another size still lines up.
            var whole = guide.rect;
            float sx = whole.width / GuideWidth, sy = whole.height / GuideHeight;
            var rect = new Rect(whole.x + crop.x * sx, whole.y + (GuideHeight - crop.y - crop.height) * sy, crop.width * sx, crop.height * sy);
            sprite = Sprite.Create(guide.texture, rect, new Vector2(.5f, .5f), guide.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "Key Guide " + key;
            guideSprites[key] = sprite;
            return sprite;
        }
        private UnityEngine.UI.Image GuideKeyAt(int index)
        {
            while (guideKeys.Count <= index)
            {
                var rect = PrototypeView.MakeRect(Rect, "Guide Key", Vector2.zero, Vector2.one);
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                var image = UiKit.Image(rect, Color.white);
                image.preserveAspect = true;
                guideKeys.Add(image);
            }
            return guideKeys[index];
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
