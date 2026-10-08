using UnityEngine;

namespace StringAsASword.Stage
{
    [CreateAssetMenu(menuName = "String As a Sword/Stage", fileName = "FourPhasePrototype")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] private int noiseSeed = 87;
        [SerializeField, Min(1)] private int noiseSpawnInterval = 8;
        [SerializeField, Range(1, 8)] private int maxActiveNoise = 2;
        [SerializeField, Min(1)] private int initialNoiseTarget = 3;
        public int NoiseSeed => noiseSeed;
        public int NoiseSpawnInterval => Mathf.Max(1, noiseSpawnInterval);
        public int MaxActiveNoise => Mathf.Clamp(maxActiveNoise, 1, 8);
        public int InitialNoiseTarget => Mathf.Max(1, initialNoiseTarget);
    }
}
