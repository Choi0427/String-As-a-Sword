using System;
using StringAsASword.Combat;
using StringAsASword.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using static StringAsASword.Presentation.UiKit;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Battle screens from the Figma frames "Game Play" (키 가이드), "Pause" and "Game Over", laid out in 1920x1080 pixels.
    /// The game stays visible behind them, as in the mockups. Items: hover or W/S to pick, click or Enter/Space to press.
    /// </summary>
    public sealed class BattleScreens : MonoBehaviour
    {
        public enum ScreenKind { None, Pause, GameOver }
        private static readonly Color Ink = Hex(0xD9D9D9), HoverBox = Hex(0x929292), HoverText = Hex(0x0C0C0C);
        private const float ItemSize = 65, ItemSpacing = -10; // Orbit Regular 65 px, letter spacing -10%.

        private PrototypeGame game;
        private TMP_FontAsset itemFont, titleFont;
        private GameObject keyGuide;
        private Panel pause, gameOver;
        private TMP_Text gameOverTitle;
        private SettingsPanel settings;
        private Panel shown;
        public ScreenKind Showing { get; private set; }
        public int Selected => shown?.Selected ?? -1;
        public bool SettingsOpen => settings.IsOpen;
        public bool KeyGuideVisible => keyGuide != null && keyGuide.activeSelf;
        /// <summary>The bottom key guide's opacity: the tutorial dims it while its own prompt is up, so only one is emphasised.</summary>
        public float KeyGuideAlpha { get; set; } = 1;
        public string GameOverTitle => gameOverTitle.text;

        /// <summary>Builds the screens over <paramref name="parent"/> (the 1600x900 battle content), scaled from 1920x1080.</summary>
        public static BattleScreens Create(RectTransform parent, PrototypeGame game, ArtSet art)
        {
            var frame = Rect(parent, "Battle Screens (Figma 1920x1080)", Vector2.zero, new Vector2(1920, 1080));
            frame.localScale = Vector3.one * (parent.rect.width > 0 ? parent.rect.width : 1600) / 1920f;
            var screens = frame.gameObject.AddComponent<BattleScreens>();
            screens.Build(game, art);
            return screens;
        }
        private void Build(PrototypeGame owner, ArtSet art)
        {
            game = owner;
            itemFont = UiFonts.Korean(art.screenFont != null ? art.screenFont : art.menuFont);
            titleFont = art.screenTitleFont != null ? art.screenTitleFont : itemFont;
            var frame = (RectTransform)transform;
            if (art.keyGuide != null)
            {
                // Game Play / 키 가이드: 723 x 104 at (598.5, 938.7).
                var guide = Image(TopLeft(Rect(frame, "Key Guide"), 598.45f, 938.74f, 723.10f, 104.17f), Color.white);
                guide.sprite = art.keyGuide; guide.preserveAspect = true;
                keyGuide = guide.gameObject;
            }
            pause = new Panel(this, frame, "Pause", 287.59f, 504.81f, "PAUSED", 341.95f, 470,
                new Item("판으로 복귀", 810.5f, 462.76f, 300, game.ResumeSession),
                new Item("설정", 905.5f, 556.76f, 111, OpenSettings),
                new Item("메인 메뉴", 836, 650.76f, 248, game.QuitGame));
            gameOver = new Panel(this, frame, "Game Over", 327.19f, 425.63f, "FALLEN", 383.54f, 431,
                new Item("첫 수부터", 836.5f, 502.26f, 248, game.RestartFromMenu),
                new Item("메인 메뉴", 836, 596.26f, 248, game.QuitGame));
            gameOverTitle = gameOver.Title;
            settings = SettingsPanel.Create(frame, UiFonts.Korean(art.menuFont));
            settings.Closed += game.ApplySettings;
            Show(ScreenKind.None);
        }

        private void Update()
        {
            var state = game.State;
            if (keyGuide != null)
            {
                keyGuide.SetActive(state != PrototypeState.Epilogue);
                var guide = keyGuide.GetComponent<UnityEngine.UI.Image>();
                if (!Mathf.Approximately(guide.color.a, KeyGuideAlpha)) guide.color = new Color(1, 1, 1, KeyGuideAlpha);
            }
            var wanted = state == PrototypeState.Paused ? ScreenKind.Pause : state == PrototypeState.Finished ? ScreenKind.GameOver : ScreenKind.None;
            if (wanted != Showing) Show(wanted);
            if (shown == null || settings.IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            int count = shown.Items.Length;
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) shown.Select(shown.Selected <= 0 ? count - 1 : shown.Selected - 1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) shown.Select(shown.Selected < 0 || shown.Selected == count - 1 ? 0 : shown.Selected + 1);
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) && shown.Selected >= 0) shown.Press(shown.Selected);
        }
        private void Show(ScreenKind screen)
        {
            Showing = screen;
            if (screen != ScreenKind.Pause) settings.Close();
            shown = screen == ScreenKind.Pause ? pause : screen == ScreenKind.GameOver ? gameOver : null;
            pause.SetActive(shown == pause); gameOver.SetActive(shown == gameOver);
            if (screen == ScreenKind.GameOver)
                gameOverTitle.text = game.Battle.Outcome == BattleOutcome.Won ? "CLEARED" : "FALLEN";
        }
        /// <summary>Highlights an item of the screen on show (0 = top, -1 = none), as hovering would.</summary>
        public void Select(int index) => shown?.Select(index);
        /// <summary>Presses an item of the screen on show (0 = top), as a click would.</summary>
        public void Press(int index) => shown?.Press(index);
        private void OpenSettings() => settings.Open();
        /// <summary>Esc on the pause screen: closes the settings if they are open. False when there was nothing to close.</summary>
        public bool Back()
        {
            if (!settings.IsOpen) return false;
            settings.Close(); return true;
        }

        private readonly struct Item
        {
            public readonly string Text; public readonly float Left, Top, Width; public readonly Action Pressed;
            public Item(string text, float left, float top, float width, Action pressed) { Text = text; Left = left; Top = top; Width = width; Pressed = pressed; }
        }
        /// <summary>A black card with a light 5 px stroke, a title and a column of items.</summary>
        private sealed class Panel
        {
            private readonly GameObject root;
            private readonly TMP_Text[] labels;
            private readonly UnityEngine.UI.Image[] boxes;
            public readonly Item[] Items;
            public readonly TMP_Text Title;
            public int Selected { get; private set; } = -1;

            public Panel(BattleScreens owner, RectTransform frame, string name, float top, float height, string title, float titleTop, float titleWidth, params Item[] items)
            {
                Items = items;
                var card = TopLeft(Rect(frame, name), 598.18f, top, 723.63f, height);
                root = card.gameObject;
                // Figma: black at 90%, inside stroke 5 px #D9D9D9.
                var fill = Image(Stretch(Rect(card, "Fill")), CardFill);
                fill.raycastTarget = true; // Clicks on the card do not fall through to the game.
                Stroke(card, Ink, 5);
                Title = Label(TopLeft(Rect(frame, name + " Title"), 960 - titleWidth / 2, titleTop, titleWidth, 88), title, 80, Ink, TextAlignmentOptions.Center, owner.titleFont);
                Title.transform.SetParent(card, true);
                labels = new TMP_Text[items.Length]; boxes = new UnityEngine.UI.Image[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    int index = i;
                    var item = TopLeft(Rect(frame, "Item " + items[i].Text), items[i].Left, items[i].Top, items[i].Width, 94);
                    item.SetParent(card, true);
                    // Hover, as on the main menu: a #929292 box behind the label, which turns #0C0C0C.
                    boxes[i] = Image(Stretch(Rect(item, "Hover")), HoverBox);
                    boxes[i].rectTransform.offsetMin = new Vector2(-14, 14); boxes[i].rectTransform.offsetMax = new Vector2(14, -14);
                    labels[i] = Label(Stretch(Rect(item, "Label")), items[i].Text, ItemSize, Ink, TextAlignmentOptions.Center, owner.itemFont);
                    labels[i].characterSpacing = ItemSpacing;
                    var hit = Image(Stretch(Rect(item, "Hit")), new Color(0, 0, 0, 0)); hit.raycastTarget = true;
                    var button = hit.gameObject.AddComponent<MenuButton>();
                    button.Hovered = () => Select(index); button.Clicked = () => Press(index);
                }
                Select(-1);
            }
            public void SetActive(bool on)
            {
                if (on && !root.activeSelf) Select(-1);
                root.SetActive(on);
            }
            public void Select(int index)
            {
                Selected = index;
                for (int i = 0; i < labels.Length; i++) { boxes[i].enabled = i == index; labels[i].color = i == index ? HoverText : Ink; }
            }
            public void Press(int index) { Select(index); Items[index].Pressed(); }
        }
    }
}
