using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Presentation;
using StringAsASword.Stage;

namespace StringAsASword.Tests
{
    public sealed class TutorialTests
    {
        // ── The lessons ──
        [Test] public void ThreeMovesOnTheBeatPassTheFirstLesson()
        {
            var steps = new TutorialSteps(); steps.Reset();
            steps.Moved(); steps.Moved();
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Move));
            steps.Moved();
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Attack));
            Assert.That(steps.Moves, Is.Zero);
        }
        [Test] public void AHitBringsTheAttackBackAndAMissPassesIt()
        {
            var steps = new TutorialSteps(); steps.Reset();
            for (int i = 0; i < TutorialSteps.MovesNeeded; i++) steps.Moved();
            Assert.That(steps.AttackFell(hit: true), Is.False);
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Attack));
            Assert.That(steps.Misses, Is.EqualTo(1));
            Assert.That(steps.AttackFell(hit: false), Is.True, "guarded or stepped away");
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Slash));
            Assert.That(steps.AttackFell(hit: true), Is.False, "a guard against a slash breaks and hits");
            Assert.That(steps.AttackFell(hit: false), Is.True);
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Noise));
        }
        [Test] public void TheNoiseThenTheResonanceFinishTheTutorial()
        {
            var steps = new TutorialSteps(); steps.Reset();
            for (int i = 0; i < TutorialSteps.MovesNeeded; i++) steps.Moved();
            steps.AttackFell(false); steps.AttackFell(false);
            steps.Moved(); steps.ResonanceEnded();
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Noise), "other events do nothing out of their lesson");
            steps.NoiseCollected();
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Strike));
            steps.ResonanceEnded();
            Assert.That(steps.Step, Is.EqualTo(TutorialStep.Strike), "the resonance has to be set off first");
            steps.ResonanceStarted(); steps.ResonanceEnded();
            Assert.That(steps.Finished);
        }
        [Test] public void CardsAndLinesFollowTheScript()
        {
            string Card(TutorialStep step, bool ignited = false, int left = 0) => string.Join(" ", TutorialSteps.Card(step, ignited, left).Select(p => p.ToString()));
            Assert.That(Card(TutorialStep.Move), Is.EqualTo("[WASD] 이동"));
            Assert.That(Card(TutorialStep.Attack), Is.EqualTo("[K] 막기 or [WASD] 회피"));
            Assert.That(TutorialSteps.Card(TutorialStep.Attack)[2].Type, Is.EqualTo(PromptPart.Kind.Small), "the 'or' is small");
            Assert.That(Card(TutorialStep.Slash), Is.EqualTo("방어 불가 [WASD] 회피"));
            Assert.That(Card(TutorialStep.Noise), Is.EqualTo("[WASD] 잡음 수집"));
            Assert.That(Card(TutorialStep.Strike), Is.EqualTo("맨 윗줄에서 [J] 공격"));
            Assert.That(Card(TutorialStep.Strike, true, 8), Is.EqualTo("[J] 공명 1/8"));
            Assert.That(Card(TutorialStep.Strike, true, 1), Is.EqualTo("[J] 공명 8/8"));
            StringAssert.Contains("박자에 맞춰야지만", TutorialSteps.Guide(TutorialStep.Move));
            StringAssert.Contains("K로 방어가 가능합니다", TutorialSteps.Guide(TutorialStep.Attack));
            StringAssert.Contains("빗금 패턴은 방어가 불가능합니다", TutorialSteps.Guide(TutorialStep.Slash));
            StringAssert.Contains("주선율", TutorialSteps.Guide(TutorialStep.Noise));
            StringAssert.Contains("8번", TutorialSteps.Guide(TutorialStep.Strike));
        }

        [Test] public void TheClockDiesAwayFromTheEndToTheSongsStart()
        {
            Assert.That(TutorialDirector.ClockFade(12, -1, -1), Is.EqualTo(1), "Full while the lessons run.");
            Assert.That(TutorialDirector.ClockFade(9, 10, 16), Is.EqualTo(1), "Full before the fade.");
            float last = 1;
            for (int tick = 10; tick < 16; tick++)
            {
                float level = TutorialDirector.ClockFade(tick, 10, 16);
                Assert.That(level, Is.LessThan(last), $"Quieter on every tick ({tick}).");
                last = level;
            }
            Assert.That(last, Is.LessThan(.05f), "Nearly nothing on the last tick before the song.");
        }

        // ── The practice battle ──
        private static BattleSettings Practice()
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = 60, Patterns = new PatternDefinition[0], ResonanceNotes = 8, PlayerMaxHp = 5, MaxNoise = 1 };
            settings.Schedule.Enabled = false;
            return settings;
        }
        private static double Close(BattleSettings s, int note) => note * 60.0 / s.Bpm + s.GoodWindow + .01;
        [Test] public void AnInjectedAttackLandsOnTheKnightTwoNotesLaterAndIdsStayUnique()
        {
            var settings = Practice();
            var battle = new BattleSession(settings, 87);
            var shapes = BossChart.Parse(TutorialDirector.LessonShapes).Shapes;
            battle.Advance(Close(settings, 0));
            var row = battle.InjectAttack(shapes["룩가로"], 2);
            Assert.That(row.Single().Response, Is.EqualTo(AttackResponse.Either));
            Assert.That(row[0].ImpactNote, Is.EqualTo(2));
            Assert.That(row[0].Cells & (1UL << 6), Is.Not.Zero, "on the knight at g1");
            var column = battle.InjectAttack(shapes["룩세로강"], 2);
            Assert.That(column.Single().Response, Is.EqualTo(AttackResponse.DodgeOnly));
            Assert.That(column[0].Id, Is.Not.EqualTo(row[0].Id));
            Assert.That(battle.Patterns.Active.Count, Is.EqualTo(2));
            NoteRecord second = null;
            battle.NoteResolved += r => { if (r.noteIndex == 2) second = r; };
            battle.Advance(Close(settings, 2));
            Assert.That(second.hit, Is.True, "the knight stayed on g1");
            Assert.That(battle.Patterns.Active, Is.Empty);
        }
        [Test] public void AGuardPassesTheNormalAttack()
        {
            var settings = Practice();
            var battle = new BattleSession(settings, 87);
            battle.Advance(Close(settings, 0));
            battle.InjectAttack(BossChart.Parse(TutorialDirector.LessonShapes).Shapes["룩가로"], 2);
            NoteRecord record = null;
            battle.NoteResolved += r => { if (r.noteIndex == 2) record = r; };
            battle.Request(PlayerActionKind.Guard, MoveDirection.Up, 2 * 60.0 / settings.Bpm);
            battle.Advance(Close(settings, 2));
            Assert.That(record.hit, Is.False);
            Assert.That(record.guardSuccess, Is.True);
        }
        [Test] public void WithAutoSpawnOffOnlyPlacedNoiseAppears()
        {
            var settings = Practice();
            var battle = new BattleSession(settings, 87);
            battle.Noise.AutoSpawn = false;
            battle.Advance(Close(settings, 20));
            Assert.That(battle.Noise.Pickups, Is.Empty);
            var knight = battle.Session.Position;
            var cell = battle.Noise.NearestFree(new GridPosition(knight.X, knight.Y + 2), knight);
            Assert.That(cell, Is.EqualTo(new GridPosition(6, 2)));
            Assert.That(battle.Noise.SpawnAt(cell, knight), Is.True);
            Assert.That(battle.Noise.SpawnAt(new GridPosition(0, 0), knight), Is.False, "one noise at a time");
            Assert.That(battle.Noise.NearestFree(new GridPosition(6, 9), new GridPosition(6, 7)), Is.EqualTo(new GridPosition(6, 5)), "off the top: the other way");
        }
    }
}
