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
    /// With a note sprite there are four looks: a single note, a run of two fast notes joined by one beam,
    /// a run of three joined by two beams, and a hold note with a double rail to its tail.
    /// </summary>
    public sealed class NotePresenter
    {
        private const int Slots = 9;
        private const float HeadRise = 7; // the string runs through the note head, not the stem
        private const float StringThickness = 3, PegWidth = 6, PegHeight = 22, SnapGap = 12;
        private const double FadeSeconds = .30, FlashSeconds = .15, VibrationHz = 14;
        // Note sprite: stem up, hollow head at the bottom. HeadPivot is the head's centre and HeadShare its tip-to-tip
        // height, both as a share of the sprite's height (measured on Note.png: head rows 343-493 of 495).
        private const float DefaultSpriteHeight = 100, HeadPivot = .152f, HeadShare = .30f, SpriteTone = .85f;
        // Resonance: a soft dot behind each head. Charged notes (a preview of the window) glow faintly, resonating notes
        // clearly, flaring on ignition; the last two blink like the gauge.
        private const float GlowScale = 2.4f, ChargedGlow = .22f, ResonantGlow = .55f, IgniteGlow = .35f;
        private const int WarningNotes = 2;
        private const float BeamThickness = 8, BeamGap = 5, BeamStub = 16, RailOffset = 5, RailPegHeight = 30;
        /// <summary>Notes closer together than this share of a beat are beamed into one run.</summary>
        private const double RunGapBeats = .75;
        private readonly Graphic[,] notes = new Graphic[2, Slots];
        private readonly Image[,] strings = new Image[2, Slots], pegs = new Image[2, Slots], barLines = new Image[2, Slots];
        private readonly Image[,] rails = new Image[2, Slots];
        private readonly Image[,,] beams = new Image[2, Slots, 2];
        private readonly Image[] sparks = new Image[2];
        private readonly Image[,] glows = new Image[2, Slots];
        private readonly bool art;
        private readonly float headHalf, stemHalf, spriteHeight, glowSize;
        /// <summary>Note heads glowing for resonance on one side this frame.</summary>
        public int GlowingNotes { get; private set; }
        // Stems lean toward the board by this angle (radians), parallel to the tilted judge lines.
        private readonly float tilt;
        private int[] beamLinks;
        private IBeatTimeline beamTimeline;
        private readonly float targetX;
        private readonly float spawnX;
        private readonly float noteY, stringY, barHeight;
        private int resolvedBeat = -1;
        private double resolvedAt;
        private bool resolvedHit;

        public NotePresenter(RectTransform root, Sprite sprite = null, Sprite holdBody = null, Sprite holdTail = null,
            float targetX = 310, float spawnX = 715, float laneY = 53, float barHeight = 0, float tilt = 0, float headHeight = 0)
        {
            this.targetX = targetX; this.spawnX = spawnX; this.tilt = tilt;
            stringY = laneY; this.barHeight = barHeight;
            art = sprite != null;
            // The sprite's head is centred on laneY; the prototype glyph is lifted so the string runs through its head.
            noteY = art ? laneY : laneY + HeadRise;
            // headHeight sizes the sprite so its head fills the given height tip to tip; 0 keeps the default size.
            spriteHeight = headHeight > 0 ? headHeight / HeadShare : DefaultSpriteHeight;
            var noteSize = art ? new Vector2(spriteHeight * sprite.rect.width / sprite.rect.height, spriteHeight) : new Vector2(23, 31);
            headHalf = noteSize.x * .5f; stemHalf = noteSize.x * .06f;
            glowSize = GlowScale * (art ? spriteHeight * HeadShare : 23);
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
                    if (!art) continue;
                    pegs[side, i].rectTransform.localRotation = Lean(side);
                    rails[side, i] = MakeImage(root, $"HoldRail_{side}_{i}", holdBody);
                    for (int n = 0; n < 2; n++) beams[side, i, n] = MakeImage(root, $"Beam_{side}_{i}_{n}", null);
                }
            // Glows over the strings, under the heads.
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Slots; i++)
                {
                    glows[side, i] = MakeImage(root, $"NoteGlow_{side}_{i}", PrototypeView.SoftDotSprite());
                    glows[side, i].rectTransform.sizeDelta = Vector2.one * glowSize;
                }
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < Slots; i++)
                {
                    var rect = PrototypeView.MakeRect(root, $"Note_{side}_{i}", Vector2.zero, noteSize);
                    if (art)
                    {
                        rect.pivot = new Vector2(.5f, HeadPivot); rect.localRotation = Lean(side);
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
        /// <param name="ignite">The ignition's flare, 1 at ignition falling to 0 (ResonanceHud.Ignite01).</param>
        public void Render(PrototypeGame game, float ignite = 0)
        {
            bool active = game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn || game.State == PrototypeState.Finale ||
                game.State == PrototypeState.Tutorial;
            // The bound battle's notes: the song's, or the tutorial's even beat.
            int count = game.Battle.Settings.NoteCount;
            var session = game.Session;
            double time = game.SongTime + game.VisualOffset;
            double approach = game.Song.NoteApproachBeats * game.Timeline.SecondsPerBeat;
            var held = session.ActiveHold;
            var last = session.LastHoldResult;
            if (last.HasValue && last.Value.BeatId != resolvedBeat)
            { resolvedBeat = last.Value.BeatId; resolvedAt = time; resolvedHit = last.Value.IsHit; }
            if (!last.HasValue) resolvedBeat = -1;
            double sinceResolve = time - resolvedAt;
            if (art && (beamLinks == null || beamLinks.Length != count || beamTimeline != game.Timeline))
            { beamLinks = BeamLinks(game.Timeline, count, session.IsHold); beamTimeline = game.Timeline; }

            // Resonance window: the running one, or while charged a preview of the notes an attack now would ignite.
            var resonance = game.Battle.Resonance;
            int nextNote = game.Battle.LastClosedNote + 1, resonating = resonance.Remaining(nextNote);
            int windowEnd = resonating > 0 ? resonance.EndNote : resonance.Charged ? nextNote + resonance.Length - 1 : -1;
            float glowAlpha = resonating > 0 ? ResonantGlow + IgniteGlow * Mathf.Clamp01(ignite) : ChargedGlow;
            if (resonating > 0 && resonating <= WarningNotes && Mathf.Repeat(Time.unscaledTime * 5, 1) < .5f) glowAlpha *= .45f;
            GlowingNotes = 0;

            // One slot before the nearest note keeps a just-passed note (or a held string) on screen.
            int first = Math.Max(0, game.Timeline.NearestBeat(time) - 1);
            for (int i = 0; i < Slots; i++)
            {
                int beat = first + i;
                bool inChart = active && beat < count;
                double remaining = game.Timeline.TimeOf(beat) - time;
                bool isHold = inChart && session.IsHold(beat);
                bool holding = isHold && held.HasValue && held.Value.Beat == beat;
                bool consumed = beat == session.Gate.LastConsumedBeat;
                bool headVisible = inChart && remaining <= approach &&
                    (holding || !consumed && remaining >= -.14);
                float headX = holding ? targetX : X(remaining, approach);
                bool glowing = headVisible && beat >= nextNote && beat <= windowEnd;
                if (glowing) GlowingNotes++;
                // A glowing note is white whatever its beat.
                Color headColor = beat % 4 == 0 || glowing ? PrototypeView.InkWhite : new Color(.60f, .65f, .69f);
                for (int side = 0; side < 2; side++)
                {
                    var glyph = notes[side, i];
                    glyph.gameObject.SetActive(headVisible);
                    var glow = glows[side, i];
                    glow.gameObject.SetActive(glowing);
                    if (!headVisible) continue;
                    var at = new Vector2(Side(side, headX), noteY);
                    glyph.rectTransform.anchoredPosition = at;
                    glyph.color = holding ? PrototypeView.Accent : headColor;
                    if (!glowing) continue;
                    glow.rectTransform.anchoredPosition = at;
                    glow.color = new Color(1, 1, 1, glowAlpha);
                }
                if (art)
                {
                    // The next note is only ever a tap here: hold notes are never beamed.
                    double nextRemaining = game.Timeline.TimeOf(beat + 1) - time;
                    bool nextVisible = active && beat + 1 < count && nextRemaining <= approach &&
                        beat + 1 != session.Gate.LastConsumedBeat && nextRemaining >= -.14;
                    RenderBeams(i, inChart ? beamLinks[beat] : 0, headVisible, nextVisible, headX, X(nextRemaining, approach), headColor);
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
        /// <summary>
        /// links[n] is the number of beams joining note n to note n + 1: 0 for none, 1 inside a run of two, 2 inside a run of three.
        /// Hold notes stand alone. Longer runs are split into threes and twos (4 = 2 + 2, 5 = 3 + 2, 7 = 3 + 2 + 2).
        /// </summary>
        public static int[] BeamLinks(IBeatTimeline timeline, int count, Func<int, bool> isHold)
        {
            var links = new int[count];
            double fast = timeline.SecondsPerBeat * RunGapBeats;
            for (int n = 0; n < count;)
            {
                int run = 1;
                while (n + run < count && !isHold(n + run - 1) && !isHold(n + run) &&
                    timeline.TimeOf(n + run) - timeline.TimeOf(n + run - 1) < fast) run++;
                while (run > 0)
                {
                    int take = run == 3 || run >= 5 ? 3 : Math.Min(run, 2);
                    for (int k = 0; k < take - 1; k++) links[n + k] = take - 1;
                    n += take; run -= take;
                }
            }
            return links;
        }
        // Beams run along the stem tops from this note to the next one in its run. Once the earlier note is gone,
        // a short stub stays on the later note so it still reads as part of a run.
        private void RenderBeams(int slot, int links, bool fromVisible, bool toVisible, float fromX, float toX, Color headColor)
        {
            bool visible = links > 0 && (fromVisible || toVisible);
            float end = Mathf.Min(toX, spawnX), start = fromVisible ? fromX : end - BeamStub;
            var color = new Color(headColor.r * SpriteTone, headColor.g * SpriteTone, headColor.b * SpriteTone, headColor.a);
            float stem = spriteHeight * (1 - HeadPivot), sin = Mathf.Sin(tilt), cos = Mathf.Cos(tilt);
            for (int side = 0; side < 2; side++)
                for (int n = 0; n < 2; n++)
                {
                    var beam = beams[side, slot, n];
                    beam.gameObject.SetActive(visible && n < links);
                    if (!visible || n >= links) continue;
                    beam.color = color;
                    beam.rectTransform.sizeDelta = new Vector2(end - start + 2 * stemHalf, BeamThickness);
                    // Each beam sits on the leaning stems: further up the stem is also further toward the board.
                    float along = stem - BeamThickness * .5f - n * (BeamThickness + BeamGap);
                    beam.rectTransform.anchoredPosition = new Vector2(Side(side, (start + end) * .5f - along * sin), stringY + along * cos);
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
            // The rails leave from the head's outer tip, not from inside the hollow head.
            if (art && startX == headX) startX += headHalf;
            float endX = Mathf.Min(X(endRemaining, approach), spawnX);
            if (endX <= startX) visible = false;
            for (int side = 0; side < 2; side++)
            {
                var body = strings[side, slot]; var peg = pegs[side, slot];
                body.gameObject.SetActive(visible); peg.gameObject.SetActive(visible);
                if (art)
                {
                    // The second rail of the hold note's double line.
                    var rail = rails[side, slot];
                    rail.gameObject.SetActive(visible);
                    rail.color = color;
                    rail.rectTransform.sizeDelta = new Vector2(endX - startX, thickness);
                    rail.rectTransform.anchoredPosition = new Vector2(Side(side, (startX + endX) * .5f), stringY - RailOffset);
                }
                if (!visible) continue;
                body.color = peg.color = color;
                body.rectTransform.sizeDelta = new Vector2(endX - startX, thickness);
                body.rectTransform.anchoredPosition = new Vector2(Side(side, (startX + endX) * .5f), art ? stringY + RailOffset : stringY);
                peg.rectTransform.sizeDelta = new Vector2(PegWidth, art ? RailPegHeight : PegHeight);
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
        private Quaternion Lean(int side) => Quaternion.Euler(0, 0, (side == 0 ? -tilt : tilt) * Mathf.Rad2Deg);
    }
}
