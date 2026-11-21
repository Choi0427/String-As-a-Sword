using System;

namespace StringAsASword.Presentation
{
    /// <summary>The tutorial's lessons, in order.</summary>
    public enum TutorialStep { Move, Attack, Slash, Noise, Strike, Done }

    /// <summary>
    /// What the tutorial teaches and when a lesson is passed. A lesson only moves on when the player has done it: moved on the
    /// beat, come through a normal attack (guarded or stepped away), stepped out of a slash, picked up the noise, and set off
    /// the resonance from the top row and played it out. Pure logic; the director feeds it the battle's events.
    /// </summary>
    public sealed class TutorialSteps
    {
        public const int MovesNeeded = 3;
        public TutorialStep Step { get; private set; }
        public int Moves { get; private set; }
        /// <summary>Strike lesson: the resonance has been set off and is sounding.</summary>
        public bool Ignited { get; private set; }
        /// <summary>Attacks of the current lesson that hit the knight (each one is shown again).</summary>
        public int Misses { get; private set; }
        public bool Finished => Step == TutorialStep.Done;
        public event Action<TutorialStep> Advanced;

        public void Reset() { Step = TutorialStep.Move; Moves = 0; Ignited = false; Misses = 0; }
        /// <summary>An accepted move on the beat.</summary>
        public void Moved() { if (Step == TutorialStep.Move && ++Moves >= MovesNeeded) Next(); }
        /// <summary>
        /// The lesson's attack fell. Missing the knight passes it: a guard (normal attack) or a step away. A hit, or a guard
        /// against a slash (it breaks), means the attack comes again. Returns true when the lesson is passed.
        /// </summary>
        public bool AttackFell(bool hit)
        {
            if (Step != TutorialStep.Attack && Step != TutorialStep.Slash) return false;
            if (hit) { Misses++; return false; }
            Next(); return true;
        }
        public void NoiseCollected() { if (Step == TutorialStep.Noise) Next(); }
        public void ResonanceStarted() { if (Step == TutorialStep.Strike) Ignited = true; }
        /// <summary>The resonance's last note closed.</summary>
        public void ResonanceEnded() { if (Step == TutorialStep.Strike && Ignited) Next(); }
        private void Next()
        {
            Step++; Moves = 0; Ignited = false; Misses = 0;
            Advanced?.Invoke(Step);
        }

        /// <summary>The card beside the knight (beside the noise in the noise lesson) for a lesson.</summary>
        public static PromptPart[] Card(TutorialStep step, bool ignited = false, int resonanceLeft = 0, int resonanceLength = 8)
        {
            switch (step)
            {
                case TutorialStep.Move: return new[] { PromptPart.Key("WASD"), PromptPart.Text("이동") };
                case TutorialStep.Attack: return new[] { PromptPart.Key("K"), PromptPart.Text("막기"), PromptPart.Small("or"), PromptPart.Key("WASD"), PromptPart.Text("회피") };
                case TutorialStep.Slash: return new[] { PromptPart.Text("방어 불가"), PromptPart.Key("WASD"), PromptPart.Text("회피") };
                case TutorialStep.Noise: return new[] { PromptPart.Key("WASD"), PromptPart.Text("잡음 수집") };
                case TutorialStep.Strike:
                    return ignited
                        ? new[] { PromptPart.Key("J"), PromptPart.Text($"공명 {resonanceLength - resonanceLeft + 1}/{resonanceLength}") }
                        : new[] { PromptPart.Text("맨 윗줄에서"), PromptPart.Key("J"), PromptPart.Text("공격") };
                default: return new PromptPart[0];
            }
        }
        /// <summary>The guidance line in the letterbox for a lesson. Lines 1-3 are the director's own wording.</summary>
        public static string Guide(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.Move: return "판 위에 말들은 박자에 맞춰야지만 행동할 수 있습니다. 판정선에 노트가 닿았을 때 WASD, J, K로 이동, 공격, 방어하세요.";
                case TutorialStep.Attack: return "보스의 공격은 K로 방어가 가능합니다. 방어를 통해 위치를 지키거나, 회피와 동시에 공격에 유리한 위치를 선점하세요.";
                case TutorialStep.Slash: return "빗금 패턴은 방어가 불가능합니다. WASD로 회피하세요.";
                case TutorialStep.Noise: return "왕의 주선율에서 벗어난 소리가 잡음입니다. 잡음을 밟으면 현이 울리고, 울리는 현만이 왕에게 닿습니다.";
                case TutorialStep.Strike: return "현이 울리는 동안 맨 윗줄에서 J로 베면 공명이 시작됩니다. 공명이 이어지는 8번의 공격은 강화되어 왕관을 깎습니다.";
                default: return "이제 왕의 판입니다.";
            }
        }
    }
}
