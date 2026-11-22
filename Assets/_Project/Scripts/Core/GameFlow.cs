using UnityEngine;
using UnityEngine.SceneManagement;

namespace StringAsASword.Core
{
    /// <summary>Player HP under the knight: a row of bar segments, or a row of knight icons.</summary>
    public enum PlayerHpStyle { Bar = 0, Knights = 1 }
    /// <summary>쉬움: each collected noise heals 1 HP (up to the stage's max). 어려움: the plain rules.</summary>
    public enum GameDifficulty { Easy = 0, Hard = 1 }

    /// <summary>
    /// Scene flow and saved player settings: main menu → 시작: prologue → tutorial → battle → ending → main menu.
    /// Esc skips the prologue and the tutorial, each on its own.
    /// </summary>
    public static class GameFlow
    {
        public const string MenuScene = "MainMenu", BattleScene = "RhythmPrototype";
        private const string ClearedKey = "StringAsASword.Cleared", VolumeKey = "StringAsASword.MusicVolume",
            InputKey = "StringAsASword.InputOffsetMs", VisualKey = "StringAsASword.VisualOffsetMs", DevKey = "StringAsASword.DevMode",
            HpStyleKey = "StringAsASword.PlayerHpStyle", DifficultyKey = "StringAsASword.Difficulty";

        /// <summary>Set by the menu so the battle starts its count-in without the ready overlay.</summary>
        public static bool AutoStartBattle { get; private set; }
        /// <summary>Set with <see cref="AutoStartBattle"/> when the battle should open with the prologue instead of the count-in.</summary>
        public static bool PlayPrologue { get; private set; }
        /// <summary>Set with <see cref="AutoStartBattle"/> when the battle should open with the tutorial (Settings > 튜토리얼).</summary>
        public static bool PlayTutorial { get; private set; }

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

        /// <summary>Developer mode (saved; Shift+D in the settings): shows the phase and invincibility switches under the settings card and turns on F1-F5 in battle.</summary>
        public static bool DevMode
        {
            get => PlayerPrefs.GetInt(DevKey, 0) == 1;
            set { PlayerPrefs.SetInt(DevKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        // Not saved: a new run of the game starts from phase 1 and can be hurt. Both count only while DevMode is on.
        private static bool devInvincible, devBossHpOff; private static int devStartPhase = 1;
        public static bool Invincible { get => DevMode && devInvincible; set => devInvincible = value; }
        /// <summary>Boss HP off: the battle is won at the finishing run even with boss HP left.</summary>
        public static bool BossHpOff { get => DevMode && devBossHpOff; set => devBossHpOff = value; }
        /// <summary>The boss phase (1..4) a battle starts in.</summary>
        public static int StartPhase { get => DevMode ? devStartPhase : 1; set => devStartPhase = Mathf.Clamp(value, 1, 4); }

        /// <summary>Saved; 어려움 (the plain rules) until the player picks 쉬움.</summary>
        public static GameDifficulty Difficulty
        {
            get => PlayerPrefs.GetInt(DifficultyKey, (int)GameDifficulty.Hard) == 0 ? GameDifficulty.Easy : GameDifficulty.Hard;
            set { PlayerPrefs.SetInt(DifficultyKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>How the player's HP is drawn under the knight.</summary>
        public static PlayerHpStyle HpStyle
        {
            get => PlayerPrefs.GetInt(HpStyleKey, 0) == 1 ? PlayerHpStyle.Knights : PlayerHpStyle.Bar;
            set { PlayerPrefs.SetInt(HpStyleKey, (int)value); PlayerPrefs.Save(); }
        }

        public static void StartBattle(bool prologue = false, bool tutorial = false)
        { AutoStartBattle = true; PlayPrologue = prologue; PlayTutorial = tutorial; SceneManager.LoadScene(BattleScene); }
        /// <summary>Called by the battle once it has consumed the request.</summary>
        public static void ConsumeAutoStart() { AutoStartBattle = false; PlayPrologue = false; PlayTutorial = false; }
        public static void ReturnToMenu() { AutoStartBattle = false; PlayPrologue = false; PlayTutorial = false; SceneManager.LoadScene(MenuScene); }
        /// <summary>The menu scene is in the build (it is absent when only the battle scene is played).</summary>
        public static bool HasMenu => Application.CanStreamedLevelBeLoaded(MenuScene);
    }
}
