using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Data for the prologue cutscene (Resources/Prologue): its sound clips and their level. The timing lives in
    /// <see cref="IntroTimeline"/> and <see cref="IntroDirector"/>. The clips are placeholders rendered from the previz
    /// synthesizer until the prologue cue is composed (decision S1); they are looked up by name.
    /// </summary>
    [CreateAssetMenu(menuName = "String As a Sword/Prologue", fileName = "Prologue")]
    public sealed class PrologueDefinition : ScriptableObject
    {
        public const string ResourceName = "Prologue";
        [Tooltip("Placeholder one-shots, found by clip name (clock, king_D4, tear, lane_A2, ...).")]
        public AudioClip[] clips;
        [Tooltip("Level of the clips at full music volume. The song plays at 0.03; the clips are mastered louder than it.")]
        [Range(0, 1)] public float volume = .12f;

        public AudioClip Clip(string clipName)
        {
            if (clips == null) return null;
            foreach (var clip in clips) if (clip != null && clip.name == clipName) return clip;
            return null;
        }
    }
}
