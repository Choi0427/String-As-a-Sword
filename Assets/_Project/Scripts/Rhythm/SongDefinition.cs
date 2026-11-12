using UnityEngine;

namespace StringAsASword.Rhythm
{
    [CreateAssetMenu(menuName = "String As a Sword/Song", fileName = "GeomEulHyeon87")]
    public sealed class SongDefinition : ScriptableObject
    {
        private const float FixedVolume = 0.03f;
        [SerializeField, Min(1)] private float bpm = 87;
        [SerializeField, Min(1)] private int noteCount = 1;
        [Header("Authored chart (filled by String As a Sword > Import RCM Chart)")]
        [SerializeField] private string chartName;
        [SerializeField, Min(1)] private int ticksPerBeat = 480;
        [SerializeField] private int[] noteTicks = new int[0];
        // Parallel to noteTicks: 0 for a tap, otherwise the hold's end tick. Held for the later hold rule; not played yet.
        [SerializeField] private int[] noteHoldEndTicks = new int[0];
        [Tooltip("First note of the finishing run (the last sixteenth triplet). The boss must be emptied before it; -1 = no finale.")]
        [SerializeField] private int finishNote = -1;
        public int FinishNote => HasChart && finishNote > 0 && finishNote < NoteCount ? finishNote : -1;
        public bool HasChart => noteTicks != null && noteTicks.Length > 0;
        public string ChartName => HasChart && !string.IsNullOrEmpty(chartName) ? chartName : "EvenNotes";
        public int NoteCount => HasChart ? noteTicks.Length : Mathf.Max(1, noteCount);
        public int TicksPerBeat => Mathf.Max(1, ticksPerBeat);
        public int NoteTick(int index) => noteTicks[index];
        public int NoteHoldEndTick(int index) => noteHoldEndTicks != null && index < noteHoldEndTicks.Length ? noteHoldEndTicks[index] : 0;
        /// <summary>Note times in seconds, or null when the song uses evenly spaced notes.</summary>
        public double[] NoteTimes()
        {
            if (!HasChart) return null;
            double secondsPerTick = 60.0 / Bpm / TicksPerBeat;
            var times = new double[noteTicks.Length];
            for (int i = 0; i < times.Length; i++) times[i] = noteTicks[i] * secondsPerTick;
            return times;
        }
        /// <summary>Hold end time in seconds per note (0 for a tap), or null when the song has no chart.</summary>
        public double[] HoldEndTimes()
        {
            if (!HasChart) return null;
            double secondsPerTick = 60.0 / Bpm / TicksPerBeat;
            var ends = new double[noteTicks.Length];
            for (int i = 0; i < ends.Length; i++) { int end = NoteHoldEndTick(i); ends[i] = end > 0 ? end * secondsPerTick : 0; }
            return ends;
        }
        [SerializeField] private AudioClip music;
        [SerializeField, Min(0)] private float audioStartOffset;
        [SerializeField, Range(-.2f, .2f)] private float inputOffset;
        [SerializeField, Range(-.2f, .2f)] private float visualOffset;
        [SerializeField, Min(0)] private float perfectWindow = .070f;
        [SerializeField, Min(0)] private float goodWindow = .140f;
        [SerializeField, Range(2, 8)] private int countInBeats = 4;
        [SerializeField, Range(2, 6)] private int noteApproachBeats = 4;
        public float Bpm => Mathf.Max(1, bpm);
        public AudioClip Music => music;
        public float Volume => FixedVolume;
        public double AudioStartOffset => audioStartOffset;
        public double InputOffset => inputOffset;
        public double VisualOffset => visualOffset;
        public double PerfectWindow => perfectWindow;
        public double GoodWindow => Mathf.Max(perfectWindow, goodWindow);
        public int CountInBeats => countInBeats;
        public int NoteApproachBeats => noteApproachBeats;
    }
}
