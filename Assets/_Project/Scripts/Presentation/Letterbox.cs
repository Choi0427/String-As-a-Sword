using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Black bars over the top and bottom of the screen, with a skip hint in the top bar and a subtitle line in the bottom one.
    /// The bars slide in and out (<see cref="Layout"/>) and can change height, so the prologue's 2.39:1 frame can narrow to
    /// the tutorial's without a cut.
    /// </summary>
    public sealed class Letterbox
    {
        private readonly RectTransform layer, top, bottom;
        private readonly CanvasGroup hint;
        private readonly TMP_Text subtitle;
        internal const float SkipHintOpacity = .45f;
        private string subtitleText = "";
        private float subtitleSince = -1;
        /// <summary>The subtitle's usual size, in canvas pixels.</summary>
        public const float SubtitleSize = 22;
        public string Subtitle => subtitleText;
        public float SubtitleFontSize => subtitle.fontSize;
        /// <summary>The bar height last laid out, in canvas pixels.</summary>
        public float Bar { get; private set; }
        /// <summary>How far the bars are slid in, as last laid out (0 = gone, 1 = in place).</summary>
        public float Amount { get; private set; }

        /// <summary>The prologue's bars: a 2.39:1 picture, never thinner than 6% of the height.</summary>
        public static float CinemaBar(Vector2 size) => Mathf.Max((size.y - size.x / CutscenePlayer.Aspect) / 2, size.y * .06f);

        public Letterbox(RectTransform parent, string name, TMP_FontAsset textFont, Sprite skipKeyIcon)
        {
            layer = PrototypeView.MakeRect(parent, name, Vector2.zero, Vector2.zero);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.sizeDelta = Vector2.zero;
            top = MakeBar("Letterbox Top", 1); bottom = MakeBar("Letterbox Bottom", 0);
            hint = MakeSkipHint(top, textFont, skipKeyIcon);
            subtitle = UiKit.Label(PrototypeView.MakeRect(bottom, "Subtitle", Vector2.zero, new Vector2(1440, 70)), "", SubtitleSize,
                new Color(1, 1, 1, .92f), TextAlignmentOptions.Center, textFont);
            subtitle.textWrappingMode = TextWrappingModes.Normal; subtitle.lineSpacing = -8;
            layer.gameObject.SetActive(false);
        }
        internal static CanvasGroup MakeSkipHint(RectTransform parent, TMP_FontAsset font, Sprite keySprite)
        {
            var rect = UiKit.Rect(parent, "Skip Hint", Vector2.zero, new Vector2(400, 40));
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(-40, 0);
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = SkipHintOpacity;
            var caption = UiKit.Label(rect, "건너뛰기", 18, Color.white, TextAlignmentOptions.Right, font);

            if (keySprite != null)
            {
                const float keySize = 32, gap = 8;
                var iconRect = UiKit.Rect(rect, "Esc Key", Vector2.zero, Vector2.one * keySize);
                iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(1, .5f);
                iconRect.anchoredPosition = Vector2.left * (caption.preferredWidth + gap);
                var icon = UiKit.Image(iconRect, Color.white);
                icon.sprite = keySprite;
                icon.preserveAspect = true;
            }

            return group;
        }

        public bool Visible { get => layer.gameObject.activeSelf; set => layer.gameObject.SetActive(value); }
        /// <summary>Bars of height <paramref name="bar"/>, slid in by <paramref name="amount"/> (0 = gone, 1 = in place).</summary>
        public void Layout(float bar, float amount)
        {
            Bar = bar; Amount = amount;
            top.sizeDelta = bottom.sizeDelta = new Vector2(0, bar);
            top.anchoredPosition = new Vector2(0, bar * (1 - amount));
            bottom.anchoredPosition = new Vector2(0, -bar * (1 - amount));
            hint.alpha = SkipHintOpacity * amount;
            float fade = subtitleSince < 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - subtitleSince) / .3f);
            subtitle.alpha = .92f * amount * fade;
            // One line where it fits (the longest guide line just does at 22 px); two when the screen is narrow.
            subtitle.rectTransform.sizeDelta = new Vector2(Mathf.Min(1480, layer.rect.width - 100), Mathf.Max(30, bar - 8));
        }
        /// <summary>The guidance line in the bottom bar, at <paramref name="size"/> px; a new line fades in.</summary>
        public void SetSubtitle(string text, float size = SubtitleSize)
        {
            text = text ?? "";
            if (!Mathf.Approximately(subtitle.fontSize, size)) subtitle.fontSize = size;
            if (text == subtitleText) return;
            subtitleText = text; subtitle.text = text; subtitleSince = Time.unscaledTime;
        }
        public Vector2 Size => layer.rect.size;
        private RectTransform MakeBar(string name, float edge)
        {
            var bar = PrototypeView.MakeRect(layer, name, Vector2.zero, Vector2.zero);
            bar.anchorMin = new Vector2(0, edge); bar.anchorMax = new Vector2(1, edge); bar.pivot = new Vector2(.5f, edge);
            UiKit.Image(bar, Color.black);
            return bar;
        }
    }
}
