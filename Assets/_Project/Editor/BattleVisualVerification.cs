using System;
using System.Collections.Generic;
using System.Reflection;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Player;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using StringAsASword.Simulation;
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
            CaptureLiveSession();
            var settings = BattleSettings.From(game.Song, game.Stage).Copy();
            settings.Schedule.Enabled = false;
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
            BindBattle();
            Render(settings.GoodWindow + settings.InputOffset + .001);
            return Describe("player / noise / overlap / light and dark tiles / 3 notes");
        }

        // Real sessions with a telegraph per response. mixed: fixed rows with the player on the guard row.
        // guard: three-hit full board. move: two-hit checker. Step() continues from there.
        public static string PrepareResponses(string scenario)
        {
            CaptureLiveSession();
            var settings = BattleSettings.From(game.Song, game.Stage).Copy();
            settings.NoteCount = 512; settings.SpawnInterval = 1000;
            if (scenario == "mixed")
            {
                settings.Schedule.Enabled = false;
                settings.Patterns = new[] {
                    new PatternDefinition { Name = "ROW 4", Kind = PatternKind.Row, Coordinate = 3, Response = AttackResponse.GuardOnly, FirstImpact = 4, TelegraphNotes = 3, RepeatNotes = 1000 },
                    new PatternDefinition { Name = "ROW 2", Kind = PatternKind.Row, Coordinate = 1, FirstImpact = 5, TelegraphNotes = 4, RepeatNotes = 1000 },
                    new PatternDefinition { Name = "ROW 6", Kind = PatternKind.Row, Coordinate = 5, Response = AttackResponse.DodgeOnly, FirstImpact = 6, TelegraphNotes = 5, RepeatNotes = 1000 }
                };
            }
            else if (scenario == "guard" || scenario == "move")
            {
                settings.Schedule.Enabled = true;
                settings.Patterns = new[] { scenario == "guard"
                    ? new PatternDefinition { Name = "Tremolo", Kind = PatternKind.FullBoard, Response = AttackResponse.GuardOnly, TelegraphNotes = 3, Hits = 3 }
                    : new PatternDefinition { Name = "Checker", Kind = PatternKind.Checker, Response = AttackResponse.DodgeOnly, TelegraphNotes = 3, Hits = 2 } };
            }
            else throw new ArgumentException("Use mixed, guard or move.", nameof(scenario));
            battle = new BattleSession(settings, 2);
            for (int note = 0; note < 64 && battle.Patterns.Active.Count == 0; note++)
                battle.Advance(game.Timeline.TimeOf(note) + settings.GoodWindow + settings.InputOffset + .001);
            if (battle.Patterns.Active.Count == 0) throw new InvalidOperationException("The response fixture published nothing.");
            BindBattle();
            Render(battle.AdvancedTime);
            return Describe("response fixture / " + scenario);
        }

        // Plays the next note with one action, then renders it: "guard" or "move" (away, up) or anything else to wait.
        public static string Respond(string action)
        {
            EnsurePrepared();
            int note = battle.LastClosedNote + 1;
            double start = game.Timeline.TimeOf(note) + battle.Settings.InputOffset, end = start + battle.Settings.GoodWindow + .001;
            if (action == "guard") battle.Request(PlayerActionKind.Guard, MoveDirection.Up, start);
            else if (action == "move") battle.Request(PlayerActionKind.Move, MoveDirection.Up, start);
            battle.Advance(end); Render(end);
            return Describe("note response / " + action);
        }

        // Searches real public-information play; only the presentation clock freezes after selection.
        public static string PrepareRandom(int warningNotes)
        {
            if (warningNotes < 1 || warningNotes > 3) throw new ArgumentOutOfRangeException(nameof(warningNotes));
            CaptureLiveSession();
            var settings = BattleSettings.From(game.Song, game.Stage).Copy();
            settings.Schedule.Enabled = true;
            BattleSession fallback = null;
            int fallbackSeed = -1;
            battle = null;
            for (int seed = 0; seed < 128; seed++)
            {
                var candidate = new BattleSession(settings, seed);
                for (int note = 0; note < settings.NoteCount && candidate.Outcome == BattleOutcome.Running; note++)
                {
                    double time = note * 60.0 / settings.Bpm + settings.InputOffset;
                    candidate.Advance(time);
                    var decision = BattleStrategies.Decide(StrategyKind.MovementUse, candidate.Observe());
                    candidate.Request(decision.Kind, decision.Direction, time);
                    candidate.Advance(time + settings.GoodWindow + .001);
                    bool found = false;
                    foreach (var attack in candidate.Patterns.Active)
                    {
                        if (attack.WarningNotes != warningNotes || attack.Remaining(candidate.LastClosedNote) != warningNotes ||
                            (attack.Cells & SafeRoutePlanner.Bit(candidate.Session.Position)) == 0) continue;
                        if (fallback == null) { fallback = candidate; fallbackSeed = seed; }
                        foreach (var pickup in candidate.Noise.Pickups)
                            if ((attack.Cells & SafeRoutePlanner.Bit(pickup.Cell)) != 0) { battle = candidate; break; }
                        found = true; break;
                    }
                    // Keep the fallback session unchanged while trying other seeds for a pickup overlap.
                    if (found) break;
                }
                if (battle != null || fallback != null && seed - fallbackSeed >= 15) break;
            }
            battle = battle ?? fallback;
            if (battle == null) throw new InvalidOperationException("No player-danger warning found within 128 seeds for " + warningNotes + " notes.");
            BindBattle(); Render(battle.AdvancedTime);
            return Describe("random runtime / " + warningNotes + "-note warning / public MovementUse strategy");
        }

        // Prepare, then Step three times for 3 -> 2 -> 1 -> X impact. A fourth step fires the column.
        public static string Step()
        {
            EnsurePrepared();
            if (battle.Outcome != BattleOutcome.Running) return Describe("sequence complete: " + battle.Outcome);
            double time = game.Timeline.TimeOf(battle.LastClosedNote + 1) + battle.Settings.GoodWindow + battle.Settings.InputOffset + .001;
            battle.Advance(time); Render(time);
            return Describe("countdown step");
        }

        public static string MoveStep()
        {
            EnsurePrepared();
            if (battle.Outcome != BattleOutcome.Running) return Describe("sequence complete: " + battle.Outcome);
            ulong danger = 0;
            foreach (var attack in battle.Patterns.Active) danger |= attack.Cells;
            var board = new BoardState();
            foreach (MoveDirection direction in Enum.GetValues(typeof(MoveDirection)))
            {
                if (!board.TryGetDestination(battle.Session.Position, direction, out var destination) ||
                    (danger & SafeRoutePlanner.Bit(destination)) != 0) continue;
                double time = game.Timeline.TimeOf(battle.LastClosedNote + 1) + battle.Settings.InputOffset;
                var action = battle.Request(PlayerActionKind.Move, direction, time);
                if (!action.Accepted) throw new InvalidOperationException("The dodge fixture was rejected: " + action.Rejection);
                RenderMovement(action, time);
                return Describe("accepted dodge / movement midpoint / " + direction);
            }
            throw new InvalidOperationException("This snapshot has no one-step exit from all published ranges; use PrepareRandom again.");
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
                RenderMovement(action, time);
            }
            else if (name == "impact") { Step(); Step(); Step(); }
            return Describe(name);
        }

        // Shows a session played elsewhere (Phase1PatternVerification) on the live view, frozen at its last advanced time.
        public static string Present(BattleSession session, string scenario)
        {
            CaptureLiveSession();
            battle = session;
            BindBattle(); Render(battle.AdvancedTime);
            return Describe(scenario);
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
        private static void RenderMovement(ActionResult action, double time)
        {
            view.ShowAction(action); Render(time);
            var board = game.GetComponentInChildren<BoardWorldPresenter>(true);
            Field(board, "moveAge").SetValue(board, .065f - Time.unscaledDeltaTime);
            view.Render(); Canvas.ForceUpdateCanvases();
        }
        private static void CaptureLiveSession()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before preparing visual verification.");
            if (game != null) return;
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
        private static void BindBattle()
        {
            Set("Battle", battle); Set("Session", battle.Session); Set("Noise", battle.Noise);
            Set("Attacks", battle.Attacks); Set("Health", battle.Health); Set("Combat", battle.Combat); Set("Phases", battle.Phases);
            Set("SessionId", (int)originals["SessionId"] + 1); Set("State", PrototypeState.Playing);
            view.ResetFeedback();
        }
        private static string Describe(string scenario)
        {
            var text = $"{scenario}; seed={battle.Seed}; closedNote={battle.LastClosedNote}; phase={battle.Phases.PhaseId}; player={battle.Session.Position}; hp={battle.PlayerHp}";
            foreach (var pattern in battle.Patterns.Active)
            {
                var cells = new List<string>();
                for (int cell = 0; cell < 64; cell++) if ((pattern.Cells & (1UL << cell)) != 0)
                    cells.Add(((char)('A' + cell % 8)).ToString() + (cell / 8 + 1));
                text += $"; {pattern.Name}/{pattern.Kind}={pattern.Remaining(battle.LastClosedNote)} notes, impactIndex={pattern.ImpactNote}, cells=[{string.Join(",", cells)}]";
            }
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
