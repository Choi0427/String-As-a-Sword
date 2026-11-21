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
        private readonly TMP_Text hint, subtitle;
        private string subtitleText = "";
        private float subtitleSince = -1;
        public RectTransform Layer => layer;
        public string Subtitle => subtitleText;
        /// <summary>The bar height last laid out, in canvas pixels.</summary>
        public float Bar { get; private set; }

        /// <summary>The prologue's bars: a 2.39:1 picture, never thinner than 6% of the height.</summary>
        public static float CinemaBar(Vector2 size) => Mathf.Max((size.y - size.x / CutscenePlayer.Aspect) / 2, size.y * .06f);

        public Letterbox(RectTransform parent, string name, TMP_FontAsset textFont, TMP_FontAsset hintFont, string hintText)
        {
            layer = PrototypeView.MakeRect(parent, name, Vector2.zero, Vector2.zero);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.sizeDelta = Vector2.zero;
            top = MakeBar("Letterbox Top", 1); bottom = MakeBar("Letterbox Bottom", 0);
            hint = UiKit.Label(PrototypeView.MakeRect(top, "Skip Hint", Vector2.zero, new Vector2(420, 40)), hintText, 18,
                new Color(1, 1, 1, .45f), TextAlignmentOptions.Right, hintFont);
            var hintRect = hint.rectTransform; hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1, .5f);
            hintRect.anchoredPosition = new Vector2(-40, 0);
            subtitle = UiKit.Label(PrototypeView.MakeRect(bottom, "Subtitle", Vector2.zero, new Vector2(1440, 70)), "", 22,
                new Color(1, 1, 1, .92f), TextAlignmentOptions.Center, textFont);
            subtitle.textWrappingMode = TextWrappingModes.Normal; subtitle.lineSpacing = -8;
            layer.gameObject.SetActive(false);
        }
        public bool Visible { get => layer.gameObject.activeSelf; set => layer.gameObject.SetActive(value); }
        /// <summary>Bars of height <paramref name="bar"/>, slid in by <paramref name="amount"/> (0 = gone, 1 = in place).</summary>
        public void Layout(float bar, float amount)
        {
            Bar = bar;
            top.sizeDelta = bottom.sizeDelta = new Vector2(0, bar);
            top.anchoredPosition = new Vector2(0, bar * (1 - amount));
            bottom.anchoredPosition = new Vector2(0, -bar * (1 - amount));
            hint.alpha = .45f * amount;
            float fade = subtitleSince < 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - subtitleSince) / .3f);
            subtitle.alpha = .92f * amount * fade;
            // One line where it fits (the longest guide line just does at 22 px); two when the screen is narrow.
            subtitle.rectTransform.sizeDelta = new Vector2(Mathf.Min(1480, layer.rect.width - 100), Mathf.Max(30, bar - 8));
        }
        /// <summary>The guidance line in the bottom bar; a new line fades in.</summary>
        public void SetSubtitle(string text)
        {
            text = text ?? "";
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
