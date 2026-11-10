using System;
using StringAsASword.Core;
using StringAsASword.Rhythm;
using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Mirrored notes on both lanes. A hold note is drawn as a string (絃) from its head to a tail peg:
    /// held, it pins to the judge line, glows and vibrates while it shortens; broken, it greys out and snaps.
    /// </summary>
    public sealed class NotePresenter
    {
        private const int Slots = 9;
        private const float HeadRise = 7; // the string runs through the note head, not the stem
        private const float StringThickness = 3, PegWidth = 6, PegHeight = 22, SnapGap = 12;
        private const double FadeSeconds = .30, FlashSeconds = .15, VibrationHz = 14;
        private readonly Graphic[,] notes = new Graphic[2, Slots];
        private readonly Image[,] strings = new Image[2, Slots], pegs = new Image[2, Slots], barLines = new Image[2, Slots];
        private readonly Image[] sparks = new Image[2];
        private readonly float targetX;
        private readonly float spawnX;
        private readonly float noteY, stringY, barHeight;
        private int resolvedBeat = -1;
        private double resolvedAt;
        private bool resolvedHit;

        public NotePresenter(RectTransform root, Sprite sprite = null, Sprite holdBody = null, Sprite holdTail = null,
            float targetX = 310, float spawnX = 715, float laneY = 53, float barHeight = 0)
        {
            this.targetX = targetX; this.spawnX = spawnX;
            stringY = laneY; noteY = laneY + HeadRise; this.barHeight = barHeight;
            // A bar line per beat scrolls with the notes across all the lane's strings; drawn first, under everything.
            if (barHeight > 0)
                for (int side = 0; side < 2; side++)
                    for (int i = 0; i < Slots; i++)
                        barLines[side, i] = MakeImage(root, $"BarLine_{side}_{i}", null);
            // Strings first so heads draw on top of them.
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Slots; i++)
                {
                    strings[side, i] = MakeImage(root, $"HoldString_{side}_{i}", holdBody);
                    pegs[side, i] = MakeImage(root, $"HoldPeg_{side}_{i}", holdTail);
                }
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Slots; i++)
                {
                    var rect = PrototypeView.MakeRect(root, $"Note_{side}_{i}", Vector2.zero, new Vector2(23, 31));
                    if (sprite != null)
                    {
                        var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite;
                        image.preserveAspect = true; image.raycastTarget = false; notes[side, i] = image;
                        continue;
                    }
                    var glyph = rect.gameObject.AddComponent<PrototypeGlyph>();
                    glyph.SetShape(PrototypeGlyph.Shape.Note); notes[side, i] = glyph;
                }
            for (int side = 0; side < 2; side++)
            {
                sparks[side] = MakeImage(root, $"HoldSpark_{side}", null);
                sparks[side].rectTransform.anchoredPosition = new Vector2(side == 0 ? -targetX : targetX, stringY);
                sparks[side].rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            }
        }
        private static Image MakeImage(RectTransform root, string name, Sprite sprite)
        {
            var image = PrototypeView.MakeRect(root, name, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.raycastTarget = false; image.gameObject.SetActive(false);
            return image;
        }
        public void Render(PrototypeGame game)
        {
            bool active = game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn || game.State == PrototypeState.Finale;
            var session = game.Session;
            double time = game.SongTime + game.VisualOffset;
            double approach = game.Song.NoteApproachBeats * game.Timeline.SecondsPerBeat;
            var held = session.ActiveHold;
            var last = session.LastHoldResult;
            if (last.HasValue && last.Value.BeatId != resolvedBeat)
            { resolvedBeat = last.Value.BeatId; resolvedAt = time; resolvedHit = last.Value.IsHit; }
            if (!last.HasValue) resolvedBeat = -1;
            double sinceResolve = time - resolvedAt;

            // One slot before the nearest note keeps a just-passed note (or a held string) on screen.
            int first = Math.Max(0, game.Timeline.NearestBeat(time) - 1);
            for (int i = 0; i < Slots; i++)
            {
                int beat = first + i;
                bool inChart = active && beat < game.Song.NoteCount;
                double remaining = game.Timeline.TimeOf(beat) - time;
                bool isHold = inChart && session.IsHold(beat);
                bool holding = isHold && held.HasValue && held.Value.Beat == beat;
                bool consumed = beat == session.Gate.LastConsumedBeat;
                bool headVisible = inChart && remaining <= approach &&
                    (holding || !consumed && remaining >= -.14);
                float headX = holding ? targetX : X(remaining, approach);
                Color headColor = beat % 4 == 0 ? PrototypeView.InkWhite : new Color(.60f, .65f, .69f);
                for (int side = 0; side < 2; side++)
                {
                    var glyph = notes[side, i];
                    glyph.gameObject.SetActive(headVisible);
                    if (!headVisible) continue;
                    glyph.rectTransform.anchoredPosition = new Vector2(Side(side, headX), noteY);
                    glyph.color = holding ? PrototypeView.Accent : headColor;
                }
                RenderBarLine(i, inChart && remaining <= approach && remaining >= 0, beat, X(remaining, approach));
                RenderString(i, isHold && remaining <= approach, beat, session.HoldEnd(beat) - time, approach,
                    holding, consumed, beat < session.ClosedNotes, sinceResolve, headX, time);
            }
            RenderSparks(held.HasValue && active, resolvedBeat >= 0 && resolvedHit && sinceResolve < FlashSeconds, time, sinceResolve);
        }
        // Every beat's bar line arrives with its note and ends at the judge line; the first beat of each bar is brighter.
        private void RenderBarLine(int slot, bool visible, int beat, float x)
        {
            if (barHeight <= 0) return;
            Color color = new Color(PrototypeView.InkWhite.r, PrototypeView.InkWhite.g, PrototypeView.InkWhite.b, beat % 4 == 0 ? .45f : .18f);
            for (int side = 0; side < 2; side++)
            {
                var line = barLines[side, slot];
                line.gameObject.SetActive(visible);
                if (!visible) continue;
                line.color = color;
                line.rectTransform.sizeDelta = new Vector2(2, barHeight);
                line.rectTransform.anchoredPosition = new Vector2(Side(side, x), stringY);
            }
        }
        private void RenderString(int slot, bool candidate, int beat, double endRemaining, double approach,
            bool holding, bool consumed, bool headClosed, double sinceResolve, float headX, double time)
        {
            bool resolvedHere = beat == resolvedBeat;
            bool fading = resolvedHere && !resolvedHit && sinceResolve < FadeSeconds;
            // A cleanly released string disappears; a broken one fades out where it snapped.
            bool visible = candidate && (holding || fading || !resolvedHere && endRemaining >= -.14);
            Color color; float thickness = StringThickness, startX = headX;
            if (holding)
            {
                color = PrototypeView.Accent;
                thickness += 2f * (float)(.5 + .5 * Math.Sin(time * 2 * Math.PI * VibrationHz));
            }
            else if (fading || consumed || headClosed)
            {
                // Released early, held too long, or the head was missed: a dead, snapped string.
                float alpha = fading ? .45f * (float)(1 - sinceResolve / FadeSeconds) : .45f;
                color = new Color(PrototypeView.Muted.r, PrototypeView.Muted.g, PrototypeView.Muted.b, alpha);
                startX = Mathf.Max(headX, targetX) + SnapGap;
            }
            else color = new Color(PrototypeView.InkWhite.r, PrototypeView.InkWhite.g, PrototypeView.InkWhite.b, .75f);
            float endX = Mathf.Min(X(endRemaining, approach), spawnX);
            if (endX <= startX) visible = false;
            for (int side = 0; side < 2; side++)
            {
                var body = strings[side, slot]; var peg = pegs[side, slot];
                body.gameObject.SetActive(visible); peg.gameObject.SetActive(visible);
                if (!visible) continue;
                body.color = peg.color = color;
                body.rectTransform.sizeDelta = new Vector2(endX - startX, thickness);
                body.rectTransform.anchoredPosition = new Vector2(Side(side, (startX + endX) * .5f), stringY);
                peg.rectTransform.sizeDelta = new Vector2(PegWidth, PegHeight);
                peg.rectTransform.anchoredPosition = new Vector2(Side(side, endX), stringY);
            }
        }
        private void RenderSparks(bool holding, bool flashing, double time, double sinceResolve)
        {
            for (int side = 0; side < 2; side++)
            {
                var spark = sparks[side];
                spark.gameObject.SetActive(holding || flashing);
                if (holding)
                {
                    float pulse = (float)(.5 + .5 * Math.Sin(time * 2 * Math.PI * VibrationHz));
                    spark.rectTransform.sizeDelta = Vector2.one * (8 + 3 * pulse);
                    spark.color = new Color(PrototypeView.Accent.r, PrototypeView.Accent.g, PrototypeView.Accent.b, .6f + .4f * pulse);
                }
                else if (flashing)
                {
                    float t = (float)(sinceResolve / FlashSeconds);
                    spark.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(12, 26, t);
                    spark.color = new Color(PrototypeView.InkWhite.r, PrototypeView.InkWhite.g, PrototypeView.InkWhite.b, 1 - t);
                }
            }
        }
        private float X(double remaining, double approach) => Mathf.LerpUnclamped(targetX, spawnX, (float)(remaining / approach));
        private static float Side(int side, float distance) => side == 0 ? -distance : distance;
    }
}
