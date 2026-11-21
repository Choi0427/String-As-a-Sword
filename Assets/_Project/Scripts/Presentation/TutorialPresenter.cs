using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Core;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The battle tutorial's prompt (intro review D2-B): one key card beside the knight, chosen each frame by
    /// <see cref="TutorialRules"/> from the battle as it stands. On until the first clear unless Settings turns it off; never
    /// in a battle started from a later phase. While the card is up the bottom key guide is dimmed so one thing leads.
    /// </summary>
    public sealed class TutorialPresenter : MonoBehaviour
    {
        // The key guide at 35% brightness. The project blends in linear space, where that look needs 0.35^2.2 (as UiKit.CardFill).
        private static readonly float GuideDimmed = Mathf.Pow(.35f, 2.2f);
        private PrototypeGame game;
        private PrototypeView view;
        private KeyPromptCard card;
        private readonly TutorialProgress progress = new TutorialProgress();
        private readonly List<GridPosition> noise = new List<GridPosition>();
        private int lastClosed = int.MaxValue;
        private GridPosition start;
        private bool moved;
        /// <summary>The prompt shown now, if any.</summary>
        public TutorialHint? Current { get; private set; }
        public KeyPromptCard Card => card;
        public TutorialProgress Progress => progress;

        public void Initialize(PrototypeGame owner, PrototypeView prototypeView)
        {
            game = owner; view = prototypeView;
            // On the board view, under the pause and settings cards (BattleScreens is built after it).
            card = new KeyPromptCard(view.BoardViewRect, "Tutorial Prompt", view.Art, view.LatinFont, 72, 34, 30);
        }
        private void LateUpdate()
        {
            Current = Decide();
            if (Current is TutorialHint hint)
            {
                card.Show(hint.Key, hint.Text);
                Place();
                var timeline = game.Timeline;
                card.Pulse((float)((game.SongTime - timeline.TimeOf(0)) / timeline.SecondsPerBeat));
            }
            else card.Hide();
            if (view.Screens != null) view.Screens.KeyGuideAlpha = Current.HasValue ? GuideDimmed : 1;
        }
        private TutorialHint? Decide()
        {
            var battle = game.Battle;
            if (battle == null) return null;
            // A new run (restart, retry, the prologue's handover) starts the lessons over.
            if (battle.LastClosedNote < lastClosed) { progress.Reset(); moved = false; start = battle.Session.Position; }
            lastClosed = battle.LastClosedNote;
            if (!GameFlow.TutorialHints || GameFlow.StartPhase != 1 || battle.KingStunned) return null;
            if (game.State != PrototypeState.CountIn && game.State != PrototypeState.Playing) return null;
            var knight = battle.Session.Position;
            moved |= !knight.Equals(start);
            noise.Clear();
            foreach (var pickup in battle.Noise.Pickups) noise.Add(pickup.Cell);
            int next = battle.LastClosedNote + 1;
            var resonance = battle.Resonance;
            return TutorialRules.Next(new TutorialState
            {
                Note = next, Knight = knight, Noise = noise,
                Charged = resonance.Enabled && resonance.Charged, ResonanceLeft = resonance.Enabled ? resonance.Remaining(next) : 0,
                ResonanceLength = resonance.Length, Telegraphs = battle.Patterns.Active, Moved = moved,
            }, progress);
        }
        // Beside the knight (where the eye is), clear of the BLOCKED/HIT flash above its head and kept on screen. It goes on
        // the side away from the step it asks for, so it never covers the cell to step into; pushed in from the screen edge
        // it may come closer, and it changes side only when it would otherwise cover the knight.
        private void Place()
        {
            var board = view.BoardWorld;
            Vector2 knight = board.ProjectToBoard(board.PlayerTransform.localPosition, PrototypeView.BoardSize);
            float width = card.Size.x, height = card.Size.y;
            var content = view.ContentRect.rect.size;
            var boardCenter = (Vector2)view.BoardViewRect.localPosition;
            float halfRoom = content.x * .5f - 24;
            float min = -halfRoom - boardCenter.x + width * .5f, max = halfRoom - boardCenter.x - width * .5f;
            float right = Mathf.Clamp(knight.x + 60 + width * .5f, min, max), left = Mathf.Clamp(knight.x - 60 - width * .5f, min, max);
            bool rightClear = right - width * .5f >= knight.x + 24, leftClear = left + width * .5f <= knight.x - 24;
            bool preferLeft = card.Key == TutorialRules.Right;
            float x = preferLeft ? (leftClear || !rightClear ? left : right) : (rightClear || !leftClear ? right : left);
            float y = knight.y + 40;
            // Notes arrive along the lanes beside the board: a card reaching past a judge line inside the lanes' band moves
            // just above or below the band instead.
            if (LaneBand(out float bandMin, out float bandMax, out float judge) &&
                y + height * .5f > bandMin - 6 && y - height * .5f < bandMax + 6 && (x + width * .5f > judge - 6 || x - width * .5f < -judge + 6))
                y = knight.y < (bandMin + bandMax) * .5f ? bandMin - height * .5f - 10 : bandMax + height * .5f + 10;
            y = Mathf.Clamp(y, -content.y * .5f - boardCenter.y + height * .5f + 16, content.y * .5f - boardCenter.y - height * .5f - 16);
            card.Rect.anchoredPosition = new Vector2(x, y);
        }
        private readonly Vector3[] corners = new Vector3[4];
        /// <summary>The lanes' height band and the judge lines' inner x, in the board view's space (its centre is the origin).</summary>
        private bool LaneBand(out float min, out float max, out float judge)
        {
            min = float.MaxValue; max = float.MinValue; judge = float.MaxValue;
            if (view.JudgeLines == null) return false;
            foreach (var line in view.JudgeLines)
            {
                if (line == null) continue;
                line.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var p = view.BoardViewRect.InverseTransformPoint(corner);
                    min = Mathf.Min(min, p.y); max = Mathf.Max(max, p.y); judge = Mathf.Min(judge, Mathf.Abs(p.x));
                }
            }
            return judge < float.MaxValue;
        }
    }
}
