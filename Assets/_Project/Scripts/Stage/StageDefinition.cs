using UnityEngine;
using StringAsASword.Combat;

namespace StringAsASword.Stage
{
    [CreateAssetMenu(menuName = "String As a Sword/Stage", fileName = "FourPhasePrototype")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] private int noiseSeed = 87;
        [SerializeField, Min(1)] private int noiseSpawnInterval = 8;
        [SerializeField, Range(1, 2)] private int maxActiveNoise = 1;
        [Tooltip("Resonance length in notes: a collected noise charges the string, the first top-row attack ignites this many notes of damage. 0 turns the rule off.")]
        [SerializeField, Range(0, BattleSettings.MaxResonanceNotes)] private int resonanceNotes = 8;
        [Tooltip("Resonance damage: Perfect hit, Good hit, cadence bonus (Perfect on the last resonance note), and an attack without resonance.")]
        [SerializeField, Range(0, 20)] private int resonancePerfect = 2, resonanceGood = 1, resonanceCadence = 2, ghostDamage = 0;
        [SerializeField] private int[] healthSegments = { 6, 6, 6, 6 };
        [SerializeField, Range(45, 80)] private float cameraElevation = 60;
        [SerializeField, Min(1)] private int playerMaxHp = 3;
        [SerializeField, Min(2)] private int maxCollectionCost = 12;
        [SerializeField] private string combatVersion = "battle-3";
        [SerializeField] private PatternDefinition[] patterns = PatternDefinition.Defaults();
        [SerializeField] private RandomAttackSchedule attackSchedule = new RandomAttackSchedule();
        [Tooltip("Hand-written boss chart (.bosschart.txt). When set, it replaces Patterns and Attack Schedule (up to its 'random N' note). See Docs/BossChart.md.")]
        [SerializeField] private TextAsset bossChart;
        public string BossChartText => bossChart != null ? bossChart.text : null;
        public RandomAttackSchedule AttackSchedule => attackSchedule;
        public int PlayerMaxHp => playerMaxHp;
        public int MaxCollectionCost => maxCollectionCost;
        public string CombatVersion => combatVersion;
        public PatternDefinition[] Patterns => patterns;
        public int NoiseSeed => noiseSeed;
        public int NoiseSpawnInterval => Mathf.Max(1, noiseSpawnInterval);
        public int MaxActiveNoise => Mathf.Clamp(maxActiveNoise, 1, 2);
        public int ResonanceNotes => Mathf.Clamp(resonanceNotes, 0, BattleSettings.MaxResonanceNotes);
        public int ResonancePerfect => Mathf.Clamp(resonancePerfect, 0, 20);
        public int ResonanceGood => Mathf.Clamp(resonanceGood, 0, 20);
        public int ResonanceCadence => Mathf.Clamp(resonanceCadence, 0, 20);
        public int GhostDamage => Mathf.Clamp(ghostDamage, 0, 20);
        public float CameraElevation => Mathf.Clamp(cameraElevation, 45, 80);
        public int[] HealthSegments => new[] { Segment(0), Segment(1), Segment(2), Segment(3) };
        private int Segment(int index) => healthSegments != null && index < healthSegments.Length ? Mathf.Max(1, healthSegments[index]) : 6;
    }
}
