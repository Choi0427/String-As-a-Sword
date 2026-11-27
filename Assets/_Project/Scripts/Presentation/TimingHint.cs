using StringAsASword.Core;
using StringAsASword.Rhythm;
using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// FAST / SLOW on the knight after a Good: which way the press (or a hold's release) missed the Perfect window, so the
    /// player knows which way to move the next one. A Perfect needs no correction and a Miss already reads as one, so neither
    /// shows anything. It sits centred across the knight at head height, above HIT / BLOCK in the knight's middle (often on
    /// the same note). Same font, ink and box as HIT / BLOCK, smaller.
    /// </summary>
    public sealed class TimingHint
    {
        public const string Fast = "FAST", Slow = "SLOW";
        // Gone well before the next note (one beat is 0.69 s at 87 BPM); the last part fades. A new hint replaces the old.
        private const float ShowSeconds = .4f, FadeSeconds = .15f, PopSeconds = .08f, Pop = .12f;
        // Board-view units, like HIT / BLOCK (21).
        private const int FontSize = 13;
        private static readonly Vector2 Padding = new Vector2(3, 1);
        private static readonly Color Ink = Color.white, Box = UiKit.Hex(0x202020);
        // The box's middle sits at this fraction of the knight's height above its feet: about where the head is.
        private const float HeadHeight = 1f;

        private readonly PrototypeGame game;
        private readonly BoardWorldPresenter board;
        private readonly Vector2 boardSize;
        private readonly RectTransform box;
        private readonly CanvasGroup group;
        private readonly TMP_Text label;
        private float age = ShowSeconds;

        /// <summary>The hint showing now ("FAST" / "SLOW"), or "" when none is.</summary>
        public string Active => box.gameObject.activeSelf ? label.text : "";

        /// <summary>FAST for an early Good, SLOW for a late one; null for every other judgement.</summary>
        public static string TextFor(Judgement grade, double errorSeconds)
            => grade != Judgement.Good ? null : errorSeconds < 0 ? Fast : Slow;

        public TimingHint(RectTransform parent, PrototypeGame game, BoardWorldPresenter board, Vector2 boardSize, TMP_FontAsset font)
        {
            this.game = game; this.board = board; this.boardSize = boardSize;
            box = UiKit.Rect(parent, "TimingHint");
            group = box.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            UiKit.Image(UiKit.Stretch(UiKit.Rect(box, "Box")), Box);
            // Centred on the letters' own outline, not the line box, so the tight box sits evenly around them.
            label = UiKit.Label(UiKit.Stretch(UiKit.Rect(box, "Label")), "", FontSize, Ink, TextAlignmentOptions.CenterGeoAligned, font);
            label.fontStyle = FontStyles.Bold;
            box.gameObject.SetActive(false);
        }

        /// <summary>A judgement on the bound battle: shows FAST / SLOW for a Good, leaves anything showing alone otherwise.</summary>
        public void Show(Judgement grade, double errorSeconds)
        {
            string text = TextFor(grade, errorSeconds);
            if (text == null) return;
            label.text = text;
            // The box hugs the drawn letters; capitals only, so it is as tall as the font's capitals (as for HIT / BLOCK).
            label.ForceMeshUpdate(true);
            Vector2 ink = label.GetRenderedValues(true);
            var face = label.font.faceInfo;
            if (face.pointSize > 0 && face.capLine > 0) ink.y = face.capLine / face.pointSize * label.fontSize;
            box.sizeDelta = ink + Padding * 2;
            age = 0; group.alpha = 1;
            Place();
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
            Place();
            group.alpha = Mathf.Clamp01((ShowSeconds - age) / FadeSeconds);
            box.localScale = Vector3.one * (1 + Pop * Mathf.Clamp01(1 - age / PopSeconds));
        }

        // Follows the knight (its hop included), so a move's hint travels with it. Centred across the knight, at head height.
        private void Place()
        {
            Vector2 head = board.ProjectKnightHead(boardSize), feet = board.ProjectToBoard(board.PlayerTransform.localPosition, boardSize);
            float half = box.sizeDelta.x * .5f;
            float x = Mathf.Clamp((head.x + feet.x) * .5f, half - boardSize.x * .5f, boardSize.x * .5f - half);
            box.anchoredPosition = new Vector2(x, Mathf.Lerp(feet.y, head.y, HeadHeight));
        }
    }
}
