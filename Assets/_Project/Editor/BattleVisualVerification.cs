using System;
using System.Collections.Generic;
using System.Reflection;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Player;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>Play Mode screenshot fixture. Uses the live view without saving scenes or assets.</summary>
    public static class BattleVisualVerification
    {
        private static PrototypeGame game;
        private static PrototypeView view;
        private static BattleSession battle;
        private static readonly Dictionary<string, object> originals = new Dictionary<string, object>();
        private static readonly string[] aliases = { "Battle", "Session", "Noise", "Attacks", "Health", "Combat", "Phases", "SessionId", "CurrentBeat", "State" };
        private static bool originalEnabled, originalFocusPause;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        // Call once after entering Play Mode. The world camera continues rendering the frozen fixture.
        public static string Prepare()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before preparing visual verification.");
            if (game == null)
            {
                game = UnityEngine.Object.FindAnyObjectByType<PrototypeGame>();
                if (game == null) throw new InvalidOperationException("The live RhythmPrototype game is missing.");
                view = game.GetComponentInChildren<PrototypeView>(true);
                foreach (string name in aliases) originals[name] = Property(game, name).GetValue(game);
                originalEnabled = game.enabled;
                originalFocusPause = (bool)Field(game, "pauseOnFocusLoss").GetValue(game);
                ((SongClock)Field(game, "clock").GetValue(game)).Stop();
                game.GetComponent<ScheduledSongAudio>().Stop();
                game.enabled = false;
                Field(game, "pauseOnFocusLoss").SetValue(game, false);
            }
            var settings = BattleSettings.From(game.Song, game.Stage);
            settings.NoteCount = 512; settings.SpawnInterval = 1000;
            settings.Patterns = new[] {
                new PatternDefinition { Name = "ROW 4", Kind = PatternKind.Row, Coordinate = 3, FirstImpact = 4, TelegraphNotes = 3, RepeatNotes = 1000 },
                new PatternDefinition { Name = "COLUMN D", Kind = PatternKind.Column, Coordinate = 3, FirstImpact = 5, TelegraphNotes = 4, RepeatNotes = 1000 }
            };
            // Find an actual seeded spawn inside the published row/column; never inject fake pickups or warnings.
            battle = null;
            for (int seed = 0; seed < 512; seed++)
            {
                var candidate = new BattleSession(settings, seed);
                candidate.Advance(settings.GoodWindow + settings.InputOffset + .001);
                if (candidate.Patterns.Active.Count != 2) continue;
                foreach (var pickup in candidate.Noise.Pickups)
                    if (pickup.Cell.X == 3 || pickup.Cell.Y == 3) { battle = candidate; break; }
                if (battle != null) break;
            }
            if (battle == null) throw new InvalidOperationException("Could not find a seeded overlap fixture.");
            Set("Battle", battle); Set("Session", battle.Session); Set("Noise", battle.Noise);
            Set("Attacks", battle.Attacks); Set("Health", battle.Health); Set("Combat", battle.Combat); Set("Phases", battle.Phases);
            Set("SessionId", (int)originals["SessionId"] + 1); Set("State", PrototypeState.Playing);
            view.ResetFeedback();
            Render(settings.GoodWindow + settings.InputOffset + .001);
            return Describe("player / noise / overlap / light and dark tiles / 3 notes");
        }

        // Prepare, then Step three times for 3 -> 2 -> 1 -> X impact. A fourth step fires the column.
        public static string Step()
        {
            EnsurePrepared();
            if (battle.LastClosedNote >= 4) return Describe("sequence complete");
            double time = game.Timeline.TimeOf(battle.LastClosedNote + 1) + battle.Settings.GoodWindow + battle.Settings.InputOffset + .001;
            battle.Advance(time); Render(time);
            return Describe("countdown step");
        }

        // Each named scene begins at the same deterministic public telegraphs and pickup positions.
        public static string Show(string scenario)
        {
            string name = (scenario ?? "player").ToLowerInvariant();
            if (name != "player" && name != "moving" && name != "noise" && name != "overlap" && name != "tiles" && name != "impact")
                throw new ArgumentException("Use player, moving, noise, overlap, tiles, or impact.", nameof(scenario));
            Prepare();
            if (name == "moving")
            {
                double time = game.Timeline.TimeOf(1) + battle.Settings.InputOffset;
                var action = battle.Request(PlayerActionKind.Move, MoveDirection.Right, time);
                if (!action.Accepted) throw new InvalidOperationException("The movement fixture was rejected.");
                view.ShowAction(action); Render(time);
                var board = game.GetComponentInChildren<BoardWorldPresenter>(true);
                Field(board, "moveAge").SetValue(board, .065f - Time.unscaledDeltaTime);
                view.Render(); Canvas.ForceUpdateCanvases();
            }
            else if (name == "impact") { Step(); Step(); Step(); }
            return Describe(name);
        }

        // Return the real session to Ready or Paused. Exiting Play Mode also discards the fixture.
        public static string Restore()
        {
            if (game == null) return "No live visual fixture.";
            foreach (string name in aliases) Set(name, originals[name]);
            var state = (PrototypeState)originals["State"];
            if (state == PrototypeState.Playing || state == PrototypeState.CountIn) Set("State", PrototypeState.Paused);
            Field(game, "pauseOnFocusLoss").SetValue(game, originalFocusPause);
            view.ResetFeedback(); view.Render(); game.enabled = originalEnabled;
            game = null; view = null; battle = null; originals.Clear();
            return "Original session restored; a previously running song remains paused.";
        }

        private static void Render(double time)
        {
            Set("CurrentBeat", battle.CurrentMusicBeat);
            var clock = (SongClock)Field(game, "clock").GetValue(game);
            Field(clock, "frozenTime").SetValue(clock, time);
            view.Render(); Canvas.ForceUpdateCanvases();
        }
        private static string Describe(string scenario)
        {
            var text = $"{scenario}; seed={battle.Seed}; closedNote={battle.LastClosedNote}; player={battle.Session.Position}; hp={battle.PlayerHp}";
            foreach (var pattern in battle.Patterns.Active) text += $"; {pattern.Name}={pattern.Remaining(battle.LastClosedNote)} notes";
            foreach (var pickup in battle.Noise.Pickups) text += $"; noise={pickup.Cell}";
            foreach (var fired in battle.Patterns.LastFired) text += $"; IMPACT={fired.Name}";
            return text;
        }
        private static void EnsurePrepared()
        { if (!Application.isPlaying || game == null || battle == null) throw new InvalidOperationException("Call Prepare() in Play Mode first."); }
        private static FieldInfo Field(object target, string name)
            => target.GetType().GetField(name, Fields) ?? throw new MissingFieldException(target.GetType().Name, name);
        private static PropertyInfo Property(object target, string name)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public) ?? throw new MissingMemberException(target.GetType().Name, name);
        private static void Set(string name, object value) => Property(game, name).SetValue(game, value);
    }
}
