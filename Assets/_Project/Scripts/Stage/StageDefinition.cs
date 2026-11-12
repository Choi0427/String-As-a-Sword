using UnityEngine;
using StringAsASword.Combat;

namespace StringAsASword.Stage
{
    [CreateAssetMenu(menuName = "String As a Sword/Stage", fileName = "FourPhasePrototype")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] private int noiseSeed = 87;
        [SerializeField, Min(1)] private int noiseSpawnInterval = 8;
        [SerializeField, Range(1, 2)] private int maxActiveNoise = 2;
        [SerializeField] private int[] noiseTargets = { 3, 4, 5 };
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
        public int[] NoiseTargets => new[] { Target(0), Target(1), Target(2) };
        private int Target(int index) => noiseTargets != null && index < noiseTargets.Length ? Mathf.Max(1, noiseTargets[index]) : index + 3;
        public float CameraElevation => Mathf.Clamp(cameraElevation, 45, 80);
        public int[] HealthSegments => new[] { Segment(0), Segment(1), Segment(2), Segment(3) };
        private int Segment(int index) => healthSegments != null && index < healthSegments.Length ? Mathf.Max(1, healthSegments[index]) : 6;
    }
}
