using System;
using System.Collections.Generic;
using StringAsASword.Core;
using TMPro;
using UnityEngine;
using static StringAsASword.Presentation.UiKit;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Saved settings (music volume, input/visual sync, HP style, developer mode) on a card 820x895 in 1920x1080 pixels.
    /// Opened from the main menu and from the battle's pause screen. Built as "Settings/Card/&lt;row&gt;" under its parent.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        private static readonly Color Text = Hex(0xD9D9D9), Box = Hex(0x929292), BoxText = Hex(0x0C0C0C), Hint = Hex(0x929292);
        private TMP_FontAsset font;
        private Action refresh = () => { };
        private readonly Dictionary<string, Action<int>> choices = new Dictionary<string, Action<int>>();
        /// <summary>Called after the panel closes, so a running battle can pick up what changed.</summary>
        public event Action Closed;
        /// <summary>The 튜토리얼 button was pressed (after the panel closed).</summary>
        public event Action TutorialRequested;
        public bool IsOpen => gameObject.activeSelf;

        public static SettingsPanel Create(RectTransform parent, TMP_FontAsset font)
        {
            var shade = Stretch(Rect(parent, "Settings"));
            var panel = shade.gameObject.AddComponent<SettingsPanel>();
            panel.font = font; panel.Build(shade);
            shade.gameObject.SetActive(false);
            return panel;
        }
        public void Open() { refresh(); gameObject.SetActive(true); }
        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false); Closed?.Invoke();
        }
        /// <summary>Picks an option of a row as if it were clicked (0 = first).</summary>
        public void Choose(string row, int option) => choices[row](option);

        private void Build(RectTransform shade)
        {
            Image(shade, new Color(0, 0, 0, .72f)).raycastTarget = true;
            var card = Rect(shade, "Card", Vector2.zero, new Vector2(820, 895));
            Image(card, Hex(0x0C0C0C)).raycastTarget = true;
            Write(Rect(card, "Title", new Vector2(0, 392), new Vector2(700, 80)), "설정", 56, Text, TextAlignmentOptions.Center);
            SliderRow(card, 282, "음악 볼륨", 0, 100, () => Mathf.RoundToInt(GameFlow.MusicVolume * 100), v => GameFlow.MusicVolume = v / 100f, v => v + "%");
            SliderRow(card, 187, "입력 싱크", -200, 200, () => GameFlow.InputOffsetMs, v => GameFlow.InputOffsetMs = v, v => (v > 0 ? "+" : "") + v + " ms");
            SliderRow(card, 92, "화면 싱크", -200, 200, () => GameFlow.VisualOffsetMs, v => GameFlow.VisualOffsetMs = v, v => (v > 0 ? "+" : "") + v + " ms");
            ChoiceRow(card, 10, "체력 표시", new[] { "막대", "기사" }, () => (int)GameFlow.HpStyle, i => GameFlow.HpStyle = (PlayerHpStyle)i);
            Write(Rect(card, "Help", new Vector2(0, -50), new Vector2(760, 40)), "입력 싱크: 판정이 늦으면 +   /   화면 싱크: 노트가 소리보다 늦게 보이면 -", 18, Hint, TextAlignmentOptions.Center);
            // Developer mode: the rows under it only show while it is on.
            Image(Rect(card, "Rule", new Vector2(0, -90), new Vector2(720, 2)), Hex(0x333333));
            ChoiceRow(card, -133, "개발자 모드", new[] { "꺼짐", "켜짐" }, () => GameFlow.DevMode ? 1 : 0, i => GameFlow.DevMode = i == 1);
            var invincible = ChoiceRow(card, -198, "무적", new[] { "꺼짐", "켜짐" }, () => GameFlow.Invincible ? 1 : 0, i => GameFlow.Invincible = i == 1);
            var bossHp = ChoiceRow(card, -263, "보스 HP", new[] { "꺼짐", "켜짐" }, () => GameFlow.BossHpOff ? 0 : 1, i => GameFlow.BossHpOff = i == 0);
            var phase = ChoiceRow(card, -328, "시작 페이즈", new[] { "1", "2", "3", "4" }, () => GameFlow.StartPhase - 1, i => GameFlow.StartPhase = i + 1);
            refresh += () => { invincible.SetActive(GameFlow.DevMode); phase.SetActive(GameFlow.DevMode); bossHp.SetActive(GameFlow.DevMode); };
            // 튜토리얼 plays the tutorial again (then the song); 닫기 goes back.
            var tutorial = Rect(card, "Tutorial", new Vector2(-115, -403), new Vector2(200, 56));
            Image(tutorial, Hex(0x1E1E1E)).raycastTarget = true;
            Stroke(tutorial, Box, 2);
            Write(Stretch(Rect(tutorial, "Label")), "튜토리얼", 26, Text, TextAlignmentOptions.Center);
            tutorial.gameObject.AddComponent<MenuButton>().Clicked = RequestTutorial;
            var close = Rect(card, "Close", new Vector2(115, -403), new Vector2(200, 56));
            Image(close, Box).raycastTarget = true;
            Write(Stretch(Rect(close, "Label")), "닫기  ESC", 26, BoxText, TextAlignmentOptions.Center);
            close.gameObject.AddComponent<MenuButton>().Clicked = Close;
            refresh();
        }
        /// <summary>The 튜토리얼 button: closes the panel and asks its owner (menu or battle) to start the tutorial.</summary>
        public void RequestTutorial()
        {
            Close();
            TutorialRequested?.Invoke();
        }
        // A label and a few boxes to click; the chosen one is filled.
        private GameObject ChoiceRow(RectTransform card, float y, string name, string[] options, Func<int> current, Action<int> set)
        {
            var row = Rect(card, name, new Vector2(0, y), new Vector2(760, 50));
            Write(Rect(row, "Name", new Vector2(-250, 0), new Vector2(220, 50)), name, 30, Text, TextAlignmentOptions.Left);
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var box = Rect(row, "Option " + options[i], new Vector2(-80 + 110 * i, 0), new Vector2(100, 46));
                var fill = Image(box, Box); fill.raycastTarget = true;
                var label = Write(Stretch(Rect(box, "Label")), options[i], 24, Text, TextAlignmentOptions.Center);
                box.gameObject.AddComponent<MenuButton>().Clicked = () => choices[name](index);
                refresh += () => { bool on = current() == index; fill.color = on ? Box : Hex(0x1E1E1E); label.color = on ? BoxText : Text; };
            }
            choices[name] = i => { set(i); refresh(); };
            return row.gameObject;
        }
        private void SliderRow(RectTransform card, float y, string name, int min, int max, Func<int> value, Action<int> set, Func<int, string> show)
        {
            Write(Rect(card, name, new Vector2(-250, y), new Vector2(220, 50)), name, 30, Text, TextAlignmentOptions.Left);
            var valueText = Write(Rect(card, name + " Value", new Vector2(300, y), new Vector2(150, 50)), show(value()), 26, Text, TextAlignmentOptions.Right);
            var area = Rect(card, name + " Slider", new Vector2(40, y), new Vector2(360, 24));
            Image(Stretch(Rect(area, "Track")), Hex(0x333333));
            var fill = Stretch(Rect(Stretch(Rect(area, "Fill Area")), "Fill")); Image(fill, Box);
            var handle = Rect(Stretch(Rect(area, "Handle Area")), "Handle", Vector2.zero, new Vector2(20, 40)); Image(handle, Text).raycastTarget = true;
            var slider = area.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
            slider.wholeNumbers = true; slider.minValue = min; slider.maxValue = max; slider.SetValueWithoutNotify(value());
            slider.onValueChanged.AddListener(v => { set((int)v); valueText.text = show((int)v); });
            // Another panel (menu or battle) may have changed the saved value since this one was built.
            refresh += () => { slider.SetValueWithoutNotify(value()); valueText.text = show(value()); };
        }
        private TMP_Text Write(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align) => Label(rect, text, size, color, align, font);
    }
}
