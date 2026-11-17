using StringAsASword.Combat;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// What the crown should show for the battle state, with no animation: how much of each blade is gone and how far
    /// each seam is blackened. Blades are indexed left to right, seams right to left (seam k = noise phase k + 1).
    /// </summary>
    public static class CrownState
    {
        /// <summary>Share of blade <paramref name="blade"/> (0 = leftmost) that is gone: its HP segment's lost share.</summary>
        public static float BladeDepletion(BossHealthGate health, int blade)
        {
            int segment = CrownLayout.BladeCount - 1 - blade;
            return 1 - health.Remaining(segment) / (float)health.Maximum(segment);
        }
        /// <summary>
        /// Share of seam <paramref name="seam"/> (0 = rightmost) that is black. Past phases are full, the current phase
        /// fills with its collected noise (less what is still flying), later phases are clean.
        /// </summary>
        public static float SeamFill(int seam, int phaseId, int collected, int required)
        {
            int phase = seam + 1;
            if (phaseId > phase) return 1;
            if (phaseId < phase) return 0;
            return required <= 0 ? 1 : System.Math.Max(0, System.Math.Min(collected, required)) / (float)required;
        }
        /// <summary>A seam crackles with static once its phase's noise goal is met, and keeps crackling after.</summary>
        public static bool SeamCrackles(int seam, int phaseId, bool goalReached) => phaseId > seam + 1 || (phaseId == seam + 1 && goalReached);
    }
}
