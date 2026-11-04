using UnityEngine;

namespace StringAsASword.Rhythm
{
    [CreateAssetMenu(menuName = "String As a Sword/Song", fileName = "GeomEulHyeon87")]
    public sealed class SongDefinition : ScriptableObject
    {
        private const float FixedVolume = 0.03f;
        [SerializeField, Min(1)] private float bpm = 87;
        [SerializeField, Min(1)] private int noteCount = 1;
        public int NoteCount => Mathf.Max(1, noteCount);
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
