using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StringAsASword.Combat;

namespace StringAsASword.Simulation
{
    /// <summary>A human-like player's execution: timing error, dropped inputs and unread telegraphs.
    /// Perfect reproduces <see cref="BattleSimulator.RunOne"/> exactly.</summary>
    public sealed class SkillProfile
    {
        public string Name;
        /// <summary>Standard deviation of the input time error, seconds (normal, mean <see cref="TimingBias"/>).</summary>
        public double TimingSigma;
        public double TimingBias;
        /// <summary>Chance an input is never pressed.</summary>
        public double DropRate;
        /// <summary>Chance, per boss attack, that the player never reads its telegraph.</summary>
        public double ReadMissRate;

        public static readonly SkillProfile Perfect = new SkillProfile { Name = "Perfect" };
        public static readonly SkillProfile Expert = new SkillProfile { Name = "Expert", TimingSigma = .025, DropRate = .01, ReadMissRate = .02 };
        public static readonly SkillProfile Intermediate = new SkillProfile { Name = "Intermediate", TimingSigma = .045, DropRate = .03, ReadMissRate = .08 };
        public static readonly SkillProfile Novice = new SkillProfile { Name = "Novice", TimingSigma = .070, DropRate = .06, ReadMissRate = .15 };
        public static readonly SkillProfile[] Tiers = { Perfect, Expert, Intermediate, Novice };
        public bool IsPerfect => TimingSigma == 0 && TimingBias == 0 && DropRate == 0 && ReadMissRate == 0;
    }

    /// <summary>One skilled run's summary. <see cref="Run"/> keeps the usual outcome/invariant fields.</summary>
    public sealed class SkilledRun
    {
        public BattleRun Run;
        public string Skill;
        public int BattleSeed, SkillSeed;
        public int Perfects, Goods, Misses;          // judgements of accepted/played notes (Records[].judgement)
        public int Dropped;                          // inputs never pressed
        public int UnreadTelegraphs;                 // distinct attacks hidden from the bot
        public int Ignitions, Cadences, GhostStrikes;
        public int ResonanceNotes;                   // notes inside a running resonance window
        public int DamageDealt;                      // sum of Records[].damage
        public int HitsTaken;                        // player-hit events
        public int Heals;                            // player-heal events (difficulty 쉬움)
        public bool HealOnNoise;
        public int StunnedNote = -1;                 // -1 when the king was never stunned
        public int ClosedNotes;                      // Records.Count
        public int Moves, Guards, EffectiveAttacks, OtherActions;   // Records[].category counts
        public double Uptime => ClosedNotes == 0 ? 0 : ResonanceNotes / (double)ClosedNotes;
        public double DamagePerIgnition => Ignitions == 0 ? 0 : DamageDealt / (double)Ignitions;
    }

    public static partial class SkilledSimulator
    {
        // battleSeed drives the BattleSession (noise/patterns); skillSeed drives only timing error,
        // drops and unread telegraphs. Perfect reproduces BattleSimulator.RunOne exactly.
        public static SkilledRun RunSkilled(BattleSettings settings, int battleSeed, int skillSeed,
            StrategyKind strategy, SkillProfile skill, bool keepBattle = false, bool healOnNoise = false)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            var timer = Stopwatch.StartNew();
            bool perfect = skill.IsPerfect;
            var battle = new BattleSession(settings, battleSeed, null) { HealOnNoise = healOnNoise };
            var result = new BattleRun
            {
                Seed = battleSeed, Strategy = strategy, Battle = battle,
                Phases = Enumerable.Range(1, 4).Select(p => new PhaseSample { phase = p }).ToArray()
            };
            var skilled = new SkilledRun { Run = result, Skill = skill.Name, BattleSeed = battleSeed, SkillSeed = skillSeed, HealOnNoise = healOnNoise };
            var rng = new Random(skillSeed);
            var unread = new HashSet<int>();
            double secondsPerBeat = 60.0 / settings.Bpm;
            double good = Math.Min(settings.GoodWindow, secondsPerBeat * .45);
            var timeline = settings.CreateTimeline();
            int previousPhase = 1;
            for (int note = 0; note < settings.NoteCount && battle.Outcome == BattleOutcome.Running; note++)
            {
                double noteTime = timeline.TimeOf(note) + settings.InputOffset;
                double inputTime = noteTime;
                bool dropped = false;
                if (!perfect)
                {
                    // Fixed draw order per note: drop, then two uniforms for the Box-Muller normal.
                    dropped = rng.NextDouble() < skill.DropRate;
                    double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                    double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    inputTime = noteTime + skill.TimingBias + skill.TimingSigma * z;
                }
                double observeTime = perfect ? noteTime : Math.Max(Math.Min(noteTime, inputTime), battle.AdvancedTime);
                battle.Advance(observeTime);
                var view = battle.Observe();
                if (skill.ReadMissRate > 0) view = FilterUnread(view, skillSeed, skill.ReadMissRate, unread);
                var decision = BattleStrategies.Decide(strategy, view);
                double closeTime = noteTime + good + .000001;
                bool late = !perfect && inputTime > closeTime;
                if (dropped) { skilled.Dropped++; result.InputMisses++; }
                else if (!late)
                    battle.Request(decision.Kind, decision.Direction, perfect ? noteTime : Math.Max(inputTime, battle.AdvancedTime));
                int goalsBeforeClose = battle.Observe().GoalRemaining;
                battle.Advance(closeTime);
                // A late press still happens (the judge binds it to the nearest note or rejects it).
                if (!dropped && late && battle.Outcome == BattleOutcome.Running)
                    battle.Request(decision.Kind, decision.Direction, Math.Max(inputTime, battle.AdvancedTime));
                int phase = battle.Phases.PhaseId;
                if (phase > previousPhase)
                {
                    result.Phases[previousPhase - 1].transitionNote = note;
                    result.Phases[previousPhase - 1].goalRemaining = goalsBeforeClose;
                }
                previousPhase = phase;
            }
            skilled.UnreadTelegraphs = unread.Count;
            FinishRun(settings, battle, result, healOnNoise);
            timer.Stop(); result.ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds;

            var records = battle.Records;
            skilled.ClosedNotes = records.Count;
            skilled.StunnedNote = battle.StunnedNote;
            foreach (var r in records)
            {
                if (r.judgement == "Perfect") skilled.Perfects++;
                else if (r.judgement == "Good") skilled.Goods++;
                else if (r.judgement == "Miss") skilled.Misses++;
                skilled.DamageDealt += r.damage;
                switch (r.category)
                {
                    case "Move": skilled.Moves++; break;
                    case "Guard": skilled.Guards++; break;
                    case "EffectiveAttack": skilled.EffectiveAttacks++; break;
                    default: skilled.OtherActions++; break;
                }
            }
            foreach (var e in battle.Events)
            {
                switch (e.type)
                {
                    case "resonance-ignite":
                        skilled.Ignitions++;
                        if (int.TryParse(e.detail, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out int end))
                        {
                            int last = Math.Min(end, records.Count - 1);
                            if (last >= e.noteIndex) skilled.ResonanceNotes += last - e.noteIndex + 1;
                        }
                        break;
                    case "resonance-cadence": skilled.Cadences++; break;
                    case "ghost-strike": skilled.GhostStrikes++; break;
                    case "player-hit": skilled.HitsTaken++; break;
                    case "player-heal": skilled.Heals++; break;
                }
            }
            if (!keepBattle) result.Battle = null;
            return skilled;
        }

        // Copy of the observation without the attacks the player never read.
        private static BattleObservation FilterUnread(BattleObservation view, int skillSeed, double missRate, HashSet<int> unread)
        {
            var kept = new List<BossTelegraph>(view.Telegraphs.Length);
            foreach (var t in view.Telegraphs)
            {
                if (UnitHash(skillSeed, t.Id) < missRate) unread.Add(t.Id);
                else kept.Add(t);
            }
            return new BattleObservation
            {
                Position = view.Position, NoteIndex = view.NoteIndex, RemainingNotes = view.RemainingNotes,
                PhaseId = view.PhaseId, PlayerHp = view.PlayerHp, CurrentHp = view.CurrentHp,
                GoalRemaining = view.GoalRemaining, RemainingBossHp = view.RemainingBossHp,
                Charged = view.Charged, ResonanceRemaining = view.ResonanceRemaining,
                MaxCollectionCost = view.MaxCollectionCost, SpawnInterval = view.SpawnInterval,
                Pickups = view.Pickups, Telegraphs = kept.ToArray()
            };
        }

        // Deterministic [0,1) from (seed, id), independent of the sequential skill RNG.
        private static double UnitHash(int seed, int id)
        {
            unchecked
            {
                ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ ((ulong)(uint)id + 0x632BE59BD9B4E019UL) * 0xBF58476D1CE4E5B9UL;
                x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27; x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (x >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        // Same outcome fields and invariant checks as BattleSimulator.RunOne.
        private static void FinishRun(BattleSettings settings, BattleSession battle, BattleRun result, bool healOnNoise)
        {
            foreach (var record in battle.Records)
            {
                var phase = result.Phases[record.phase - 1];
                switch (record.category)
                {
                    case "Move": phase.move++; break;
                    case "Guard": phase.guard++; break;
                    case "EffectiveAttack": phase.effectiveAttack++; break;
                    default: phase.other++; break;
                }
                if (record.attackOpportunity) phase.attackOpportunity++;
            }
            var final = battle.Observe();
            result.Outcome = battle.Outcome.ToString();
            result.FinalPhase = final.PhaseId;
            result.FinalHp = final.PlayerHp;
            result.GoalRemaining = final.GoalRemaining;
            result.BossHp = final.CurrentHp;
            int endNote = settings.HasFinale ? battle.StunnedNote : battle.LastClosedNote;
            result.LeftoverNotes = settings.HasFinale
                ? (battle.KingStunned ? Math.Max(0, settings.DecidingNote - battle.StunnedNote) : 0)
                : Math.Max(0, settings.NoteCount - battle.LastClosedNote - 1);
            result.Phases[result.FinalPhase - 1].goalRemaining = result.GoalRemaining;
            if (result.Cleared) result.Phases[3].transitionNote = endNote;
            foreach (var e in battle.Events)
                if (e.type.StartsWith("noise-", StringComparison.Ordinal) || e.type == "pattern-defer")
                    Bump(result.EventReasons, e.type);
            int previousHp = settings.PlayerMaxHp;
            for (int i = 0; i < battle.Records.Count; i++)
            {
                var record = battle.Records[i];
                if (record.noteIndex != i) Bump(result.InvariantFailures, "note-order");
                if (record.phase < 1 || record.phase > 4) Bump(result.InvariantFailures, "phase-range");
                if (record.x < 0 || record.x > 7 || record.y < 0 || record.y > 7)
                    Bump(result.InvariantFailures, "board-bounds");
                int distance = Math.Abs(record.x - record.beforeX) + Math.Abs(record.y - record.beforeY);
                if (distance != (record.category == "Move" ? 1 : 0)) Bump(result.InvariantFailures, "move-distance");
                if (record.damage < 0 || record.damage > Math.Max(1, battle.Resonance.MaxStrike) || record.damage > 0 && record.y != 7)
                    Bump(result.InvariantFailures, "attack-damage");
                if ((record.category == "EffectiveAttack") != (record.damage > 0))
                    Bump(result.InvariantFailures, "attack-category");
                // 쉬움: a noise collected on this note may heal 1 HP (never above the max).
                if (record.playerHp < 0 || record.playerHp > (healOnNoise ? Math.Min(settings.PlayerMaxHp, previousHp + 1) : previousHp))
                    Bump(result.InvariantFailures, "player-hp");
                if (record.bossHp < 0) Bump(result.InvariantFailures, "boss-hp");
                previousHp = record.playerHp;
            }
            if (result.Cleared && !battle.Health.Defeated) Bump(result.InvariantFailures, "win-before-defeat");
            if (battle.Records.Count > settings.NoteCount) Bump(result.InvariantFailures, "chart-overrun");
        }

        private static void Bump(Dictionary<string, int> counts, string key)
        { counts.TryGetValue(key, out int current); counts[key] = current + 1; }
    }
}
