using UnityEngine;
using UnityEngine.SceneManagement;

namespace StringAsASword.Core
{
    /// <summary>Scene flow and saved player settings: main menu → story → battle → ending → main menu.</summary>
    public static class GameFlow
    {
        public const string MenuScene = "MainMenu", BattleScene = "RhythmPrototype";
        private const string ClearedKey = "StringAsASword.Cleared", VolumeKey = "StringAsASword.MusicVolume",
            InputKey = "StringAsASword.InputOffsetMs", VisualKey = "StringAsASword.VisualOffsetMs";

        /// <summary>Set by the menu so the battle starts its count-in without the ready overlay.</summary>
        public static bool AutoStartBattle { get; private set; }

        public static bool Cleared => PlayerPrefs.GetInt(ClearedKey, 0) == 1;
        public static void MarkCleared() { PlayerPrefs.SetInt(ClearedKey, 1); PlayerPrefs.Save(); }
        public static void ResetCleared() { PlayerPrefs.DeleteKey(ClearedKey); PlayerPrefs.Save(); }

        /// <summary>Multiplier on the song's own volume, 0..1.</summary>
        public static float MusicVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1));
            set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }
        /// <summary>Added to the song's input offset (judgement sync), in milliseconds.</summary>
        public static int InputOffsetMs
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(InputKey, 0), -200, 200);
            set { PlayerPrefs.SetInt(InputKey, Mathf.Clamp(value, -200, 200)); PlayerPrefs.Save(); }
        }
        /// <summary>Added to the song's visual offset (note display sync), in milliseconds.</summary>
        public static int VisualOffsetMs
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(VisualKey, 0), -200, 200);
            set { PlayerPrefs.SetInt(VisualKey, Mathf.Clamp(value, -200, 200)); PlayerPrefs.Save(); }
        }

        public static void StartBattle() { AutoStartBattle = true; SceneManager.LoadScene(BattleScene); }
        /// <summary>Called by the battle once it has consumed the request.</summary>
        public static void ConsumeAutoStart() => AutoStartBattle = false;
        public static void ReturnToMenu() { AutoStartBattle = false; SceneManager.LoadScene(MenuScene); }
        /// <summary>The menu scene is in the build (it is absent when only the battle scene is played).</summary>
        public static bool HasMenu => Application.CanStreamedLevelBeLoaded(MenuScene);
    }
}
