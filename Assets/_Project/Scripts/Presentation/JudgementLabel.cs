using StringAsASword.Core;
using StringAsASword.Rhythm;
using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// PERFECT / GOOD / MISS over the right-hand strings, where the notes come in, so the grade reads next to the note
    /// that earned it. Same font (Chakra Petch) and ink as HIT / BLOCK, but bare text with no box behind it; a new grade
    /// replaces the one showing.
    /// </summary>
    public sealed class JudgementLabel
    {
        public const string Perfect = "PERFECT", Good = "GOOD", Miss = "MISS";
        // Gone before the next note (one beat is 0.69 s at 87 BPM); the last part fades. A new grade replaces the old.
        private const float ShowSeconds = .45f, FadeSeconds = .15f, PopSeconds = .08f, Pop = .15f;
        // Screen units (the 1600 x 900 layout): large, so the grade reads at a glance from the corner of the eye.
        private const int FontSize = 50;
        private const float Tracking = -10;
        private static readonly Vector2 Padding = new Vector2(6, 3);
        private static readonly Color Ink = Color.white;
        // The letters' bottom sits about this far over the top string.
        private const float StaffGap = 26;
        // Across the strings' visible stretch, from the judge line (0) to the screen edge (1): a little left of its middle.
        private const float AlongStaff = .48f;

        private readonly PrototypeGame game;
        private readonly RectTransform box;
        private readonly CanvasGroup group;
        private readonly TMP_Text label;
        private readonly float staffX, staffTop, screenHalf;
        private float age = ShowSeconds;

        /// <summary>The grade showing now ("PERFECT" / "GOOD" / "MISS"), or "" when none is.</summary>
        public string Active => box.gameObject.activeSelf ? label.text : "";

        /// <summary>The word for a judgement; null for None.</summary>
        public static string TextFor(Judgement grade)
            => grade == Judgement.Perfect ? Perfect : grade == Judgement.Good ? Good : grade == Judgement.Miss ? Miss : null;

        /// <param name="judgeX">The right judge line's x, in <paramref name="parent"/>'s units.</param>
        /// <param name="staffEnd">Where the right strings leave the screen.</param>
        /// <param name="staffTop">The top string's y.</param>
        public JudgementLabel(RectTransform parent, PrototypeGame game, float judgeX, float staffEnd, float staffTop, float screenHalf, TMP_FontAsset font)
        {
            this.game = game; this.staffTop = staffTop; this.screenHalf = screenHalf;
            staffX = Mathf.Lerp(judgeX, staffEnd, AlongStaff);
            box = UiKit.Rect(parent, "Judgement");
            group = box.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            // Centred on the letters' own outline, not the line box, so the tight box sits evenly around them.
            label = UiKit.Label(UiKit.Stretch(UiKit.Rect(box, "Label")), "", FontSize, Ink, TextAlignmentOptions.CenterGeoAligned, font);
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = Tracking;
            box.gameObject.SetActive(false);
        }

        public void Show(Judgement grade)
        {
            string text = TextFor(grade);
            if (text == null) return;
            label.text = text;
            // Sized to the drawn letters (capitals only, so as tall as the font's capitals), for placing and the pop.
            label.ForceMeshUpdate(true);
            Vector2 ink = label.GetRenderedValues(true);
            var face = label.font.faceInfo;
            if (face.pointSize > 0 && face.capLine > 0) ink.y = face.capLine / face.pointSize * label.fontSize;
            box.sizeDelta = ink + Padding * 2;
            float half = box.sizeDelta.x * .5f;
            box.anchoredPosition = new Vector2(Mathf.Min(staffX, screenHalf - half), staffTop + StaffGap + box.sizeDelta.y * .5f);
            age = 0; group.alpha = 1;
            box.gameObject.SetActive(true);
        }

        public void Hide()
        {
            age = ShowSeconds;
            box.gameObject.SetActive(false);
        }

        public void Render()
        {
            if (!box.gameObject.activeSelf) return;
            if (game.State != PrototypeState.Paused) age += Time.unscaledDeltaTime;
            if (age >= ShowSeconds) { box.gameObject.SetActive(false); return; }
            group.alpha = Mathf.Clamp01((ShowSeconds - age) / FadeSeconds);
            box.localScale = Vector3.one * (1 + Pop * Mathf.Clamp01(1 - age / PopSeconds));
        }
    }
}
