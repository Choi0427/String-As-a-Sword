using UnityEngine;

namespace StringAsASword.Stage
{
    [CreateAssetMenu(menuName = "String As a Sword/Stage", fileName = "FourPhasePrototype")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] private int noiseSeed = 87;
        [SerializeField, Min(1)] private int noiseSpawnInterval = 8;
        [SerializeField, Range(1, 8)] private int maxActiveNoise = 2;
        [SerializeField] private int[] noiseTargets = { 3, 4, 5 };
        [SerializeField] private int[] healthSegments = { 6, 6, 6, 6 };
        [SerializeField, Min(1)] private int attackDamage = 1;
        [SerializeField, Range(45, 80)] private float cameraElevation = 60;
        public int NoiseSeed => noiseSeed;
        public int NoiseSpawnInterval => Mathf.Max(1, noiseSpawnInterval);
        public int MaxActiveNoise => Mathf.Clamp(maxActiveNoise, 1, 8);
        public int[] NoiseTargets => new[] { Target(0), Target(1), Target(2) };
        private int Target(int index) => noiseTargets != null && index < noiseTargets.Length ? Mathf.Max(1, noiseTargets[index]) : index + 3;
        public int AttackDamage => Mathf.Max(1, attackDamage);
        public float CameraElevation => Mathf.Clamp(cameraElevation, 45, 80);
        public int[] HealthSegments => new[] { Segment(0), Segment(1), Segment(2), Segment(3) };
        private int Segment(int index) => healthSegments != null && index < healthSegments.Length ? Mathf.Max(1, healthSegments[index]) : 6;
    }
}
