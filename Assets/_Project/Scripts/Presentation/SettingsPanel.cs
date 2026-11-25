using System;
using System.Collections.Generic;
using StringAsASword.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using static StringAsASword.Presentation.UiKit;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Saved settings from the Figma frame "Game Play" / Settings (1920x1080 pixels): 음량, 입력 판정 보정, 음악 싱크 보정 and
    /// 난이도 on a 1266x951 card. W/S pick a row, A/D (or the arrow buttons) change it, Esc goes back (handled by the owner).
    /// Opened from the main menu and from the battle's pause screen. Built as "Settings/Frame/Card/&lt;row&gt;" under its parent.
    /// Hidden: Shift+D toggles developer mode, whose switches show in a strip under the card.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        // Figma values, in 1920x1080 pixels from the frame's top-left corner (y down).
        private static readonly Color Ink = Hex(0xD9D9D9), Line = Hex(0x6D6D6D), Caption = Hex(0x6F6F6E);
        // Figma's rules are #6C6C6C at 50% on the black card: #363636 once blended.
        private static readonly Color Rule = Hex(0x363636);
        // The picked row: a faint band (linear-space alpha for ~8% white over black).
        private static readonly Color RowBand = new Color(1, 1, 1, Mathf.Pow(.08f, 2.2f));
        private const float CardLeft = 326.92f, CardTop = 64.23f, CardWidth = 1266.17f, CardHeight = 950.64f;
        private const float RuleLeft = 391.69f, RuleWidth = 1136.62f;
        private static readonly float[] Rules = { 266.32f, 416.38f, 566.44f, 716.49f, 866.55f };
        private static readonly float[] RowCenters = { 341.35f, 477.06f, 627.41f, 791.88f };
        private const float LeftButton = 989.98f, RightButton = 1453.14f, ButtonSize = 65, ValueCenter = 1222.06f;
        public const int VolumeRow = 0, InputRow = 1, MusicRow = 2, DifficultyRow = 3, RowCount = 4;
        /// <summary>One A/D press or arrow click: volume in percent, the two syncs in milliseconds.</summary>
        public const int VolumeStep = 5, OffsetStep = 1;
        private const float RepeatDelay = .35f, RepeatEvery = .06f;
        private const string EasyNote = "잡음 획득 시 체력 1칸 회복", HardNote = "체력 회복 없음";

        private TMP_FontAsset font, titleFont;
        private Action refresh = () => { };
        private readonly Dictionary<string, Action<int>> choices = new Dictionary<string, Action<int>>();
        private readonly UnityEngine.UI.Image[] bands = new UnityEngine.UI.Image[RowCount];
        private readonly List<UnityEngine.UI.Image>[] rowStrokes = new List<UnityEngine.UI.Image>[RowCount];
        private RectTransform card;
        private GameObject devStrip;
        private ArtSet art;
        private int heldStep; private float nextRepeat;
        // Press feedback on the ◀ ▶ boxes (index row * 2 + side, 0 = ◀): a white fill that lights when the box steps and fades.
        private readonly UnityEngine.UI.Image[] stepFlashes = new UnityEngine.UI.Image[RowCount * 2];
        private readonly float[] flashAges = new float[RowCount * 2];
        // One short, hard blink per step: about 25% white (alpha for the Linear colour space), on at once and off at once
        // 0.08 s later. No fade: on the dark button a fade shows as visible steps instead of one clean blink.
        private static readonly float FlashPeak = Mathf.Pow(.25f, 2.2f);
        private const float FlashSeconds = .08f;
        /// <summary>Called after the panel closes, so a running battle can pick up what changed.</summary>
        public event Action Closed;
        /// <summary>Asks the owner (menu or battle) to start the tutorial, after the panel closed.</summary>
        public event Action TutorialRequested;
        public bool IsOpen => gameObject.activeSelf;
        /// <summary>The row W/S or hovering picked (0 = 음량, -1 = none).</summary>
        public int SelectedRow { get; private set; } = -1;

        /// <param name="dim">Darken whatever is behind the card. Off over the pause screen, which is already blurred.</param>
        public static SettingsPanel Create(RectTransform parent, ArtSet art, bool dim = true)
        {
            var shade = Stretch(Rect(parent, "Settings"));
            var panel = shade.gameObject.AddComponent<SettingsPanel>();
            panel.Build(shade, art, dim);
            shade.gameObject.SetActive(false);
            return panel;
        }
        // Opens with no row lit, like the menus: a row shows while hovered or once W/S picks it.
        public void Open() { refresh(); Select(-1); heldStep = 0; ResetFlashes(); gameObject.SetActive(true); }
        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false); Closed?.Invoke();
        }
        /// <summary>Picks an option of a choice row as if it were clicked (0 = first): 난이도, 개발자 모드, 무적, 보스 HP, 시작 페이즈.</summary>
        public void Choose(string row, int option) => choices[row](option);
        /// <summary>The tutorial again (then the song): closes the panel and asks the owner to start it.</summary>
        public void RequestTutorial()
        {
            Sfx.UiConfirm(art);
            Close();
            TutorialRequested?.Invoke();
        }

        /// <summary>Lights a row (0 = 음량), or none with -1. Moving onto another row plays the UI move sound.</summary>
        public void Select(int row) => Select(row, sound: true);
        private void Select(int row, bool sound)
        {
            row = row < 0 ? -1 : Mathf.Clamp(row, 0, RowCount - 1);
            if (sound && row >= 0 && row != SelectedRow && IsOpen) Sfx.UiMove(art);
            SelectedRow = row;
            for (int i = 0; i < RowCount; i++)
            {
                bands[i].color = i == SelectedRow ? RowBand : new Color(0, 0, 0, 0);
                foreach (var stroke in rowStrokes[i]) stroke.color = i == SelectedRow ? Ink : Line;
            }
        }
        /// <summary>A press of A (-1) or D (+1) on a row, as its arrow buttons do.</summary>
        public void Step(int row, int direction) => Step(row, direction, sound: true);
        private void Step(int row, int direction, bool sound)
        {
            direction = Math.Sign(direction);
            if (sound && direction != 0) { if (row == DifficultyRow) Sfx.UiConfirm(art); else Sfx.UiMove(art); }
            switch (row)
            {
                case VolumeRow: GameFlow.MusicVolume = Mathf.Clamp(VolumePercent + direction * VolumeStep, 0, 100) / 100f; break;
                case InputRow: GameFlow.InputOffsetMs += direction * OffsetStep; break;
                // 늦춤 plays the music later against the notes: the notes are drawn earlier.
                case MusicRow: GameFlow.VisualOffsetMs -= direction * OffsetStep; break;
                case DifficultyRow: GameFlow.Difficulty = direction < 0 ? GameDifficulty.Easy : GameDifficulty.Hard; break;
            }
            refresh();
            LightStepper(row, direction);
        }
        /// <summary>Blinks the ◀ (direction &lt; 0) or ▶ box of a row once. Rows without boxes: nothing.</summary>
        private void LightStepper(int row, int direction)
        {
            if (row < 0 || row >= RowCount || direction == 0) return;
            int i = row * 2 + (direction < 0 ? 0 : 1);
            if (stepFlashes[i] == null) return;
            flashAges[i] = 0;
            stepFlashes[i].color = new Color(1, 1, 1, FlashPeak);
        }
        private void AnimateFlashes()
        {
            for (int i = 0; i < stepFlashes.Length; i++)
            {
                if (stepFlashes[i] == null || flashAges[i] > FlashSeconds) continue;
                // Unscaled: the panel also opens over the paused battle.
                flashAges[i] += Time.unscaledDeltaTime;
                if (flashAges[i] > FlashSeconds) stepFlashes[i].color = new Color(1, 1, 1, 0);
            }
        }
        private void ResetFlashes()
        {
            for (int i = 0; i < stepFlashes.Length; i++)
            {
                flashAges[i] = FlashSeconds + 1;
                if (stepFlashes[i] != null) stepFlashes[i].color = new Color(1, 1, 1, 0);
            }
        }
        private static int VolumePercent => Mathf.RoundToInt(GameFlow.MusicVolume * 100);
        /// <summary>음악 싱크 보정 in milliseconds: + plays the music later. It is the old visual offset, sign flipped.</summary>
        public static int MusicSyncMs => -GameFlow.VisualOffsetMs;
        public static string Ms(int value) => value == 0 ? "0 ms" : (value < 0 ? "- " : "+ ") + Math.Abs(value) + " ms";

        private void Update()
        {
            AnimateFlashes();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            bool shift = keyboard.shiftKey.isPressed;
            if (shift && keyboard.dKey.wasPressedThisFrame) { Choose("개발자 모드", GameFlow.DevMode ? 0 : 1); return; }
            // With no row lit, W starts from the bottom and S from the top, as on the menus.
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) Select(SelectedRow <= 0 ? RowCount - 1 : SelectedRow - 1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) Select(SelectedRow < 0 || SelectedRow == RowCount - 1 ? 0 : SelectedRow + 1);
            int held = shift ? 0 : (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? -1 : 0) + (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0);
            if (held == 0) { heldStep = 0; return; }
            // A first press steps at once (one sound); held, it repeats quietly after a short wait. With no row lit, A/D works
            // on 음량 and lights it without a second sound.
            if (held != heldStep) { heldStep = held; nextRepeat = Time.unscaledTime + RepeatDelay; if (SelectedRow < 0) Select(VolumeRow, sound: false); Step(SelectedRow, held); }
            else if (Time.unscaledTime >= nextRepeat && SelectedRow >= 0 && SelectedRow != DifficultyRow) { nextRepeat = Time.unscaledTime + RepeatEvery; Step(SelectedRow, held, sound: false); }
        }

        // --- Building ------------------------------------------------------------------------------------------
        private void Build(RectTransform shade, ArtSet art, bool dim)
        {
            this.art = art;
            font = UiFonts.Korean(art.screenFont != null ? art.screenFont : art.menuFont);
            titleFont = art.screenTitleFont != null ? art.screenTitleFont : font;
            // Undimmed, the shade is clear but still catches clicks, so nothing behind the card can be pressed.
            Image(shade, new Color(0, 0, 0, dim ? .72f : 0)).raycastTarget = true;
            var frame = Rect(shade, "Frame", Vector2.zero, new Vector2(1920, 1080));
            card = TopLeft(Rect(frame, "Card"), CardLeft, CardTop, CardWidth, CardHeight);
            DrawMenuCard(card, Ink, art);
            Text(At("Title", 959.91f, 121.78f + 44, 588, 88), "SETTINGS", 80, Ink, titleFont);
            foreach (float y in Rules) Image(At("Rule", RuleLeft + RuleWidth / 2, y - 1, RuleWidth, 2), Rule);

            for (int i = 0; i < RowCount; i++)
            {
                int row = i;
                rowStrokes[i] = new List<UnityEngine.UI.Image>();
                // The row between its rules: hover picks it, and it shows faintly while picked.
                bands[i] = Image(At("Row " + i, RuleLeft + RuleWidth / 2, (Rules[i] + Rules[i + 1]) / 2, RuleWidth, Rules[i + 1] - Rules[i] - 2), Color.clear);
                bands[i].raycastTarget = true;
                HoverRow(bands[i].gameObject, row);
            }

            // 음량: ◀ 80% ───◇── ▶
            Picture(art.settingsVolume, "음량", 487.25f, RowCenters[VolumeRow], 106.11f, 57.72f, "음량");
            Steppers(art, VolumeRow, captions: false);
            var volumeText = Text(At("음량 Value", 1089.73f, 315.35f + 26, 120, 52), "", 40, Ink, font);
            volumeText.characterSpacing = -5;
            BuildSlider(VolumeRow);
            refresh += () => volumeText.text = VolumePercent + "%";

            // 입력 판정 보정 / 음악 싱크 보정: ◀ 0 ms ▶ with 앞당김 / 늦춤 under the buttons.
            Picture(art.settingsInputOffset, "입력 판정 보정", 622.39f, 491.86f, 370.19f, 95.97f, "입력 판정 보정", "키 입력을 판정하는 시점");
            Steppers(art, InputRow, captions: true);
            var input = Text(At("입력 판정 보정 Value", ValueCenter, 451.06f + 26, 200, 52), "", 40, Color.white, font);
            input.characterSpacing = -5;
            refresh += () => input.text = Ms(GameFlow.InputOffsetMs);
            Picture(art.settingsMusicOffset, "음악 싱크 보정", 620.83f, 641.91f, 373.31f, 95.27f, "음악 싱크 보정", "음악이 재생되는 시점");
            Steppers(art, MusicRow, captions: true);
            var music = Text(At("음악 싱크 보정 Value", ValueCenter, 600.33f + 26, 200, 52), "", 40, Color.white, font);
            music.characterSpacing = -5;
            refresh += () => music.text = Ms(MusicSyncMs);

            // Leave space below the buttons for the caption that belongs to the selected option.
            Picture(art.settingsDifficulty, "난이도", 514.49f, 791.52f, 153.32f, 57.72f, "난이도");
            string[] levels = { "쉬움", "어려움" };
            float[] levelCenters = { 1087.48f, 1355.64f };
            const float buttonWidth = 260, buttonHeight = 75, noteHeight = 22, noteGap = 10;
            float buttonY = RowCenters[DifficultyRow] - 12;
            float noteY = buttonY + buttonHeight / 2 + noteGap + noteHeight / 2;
            for (int i = 0; i < levels.Length; i++)
            {
                int index = i;
                var box = At("Option " + levels[i], levelCenters[i], buttonY, buttonWidth, buttonHeight);
                var fill = SlicedImage(box, Ink, art.settingsRoundedFill); fill.raycastTarget = true;
                var stroke = StrokeImages(box, art.settingsRoundedButtonBorder);
                var label = Label(Stretch(Rect(box, "Label")), levels[i], 32, Color.black, TextAlignmentOptions.Midline, font);
                label.characterSpacing = -5;
                var button = HoverRow(box.gameObject, DifficultyRow);
                button.Clicked = () => choices["난이도"](index);

                var noteSprite = i == 0 ? art.settingsDifficultyEasyNote : art.settingsDifficultyHardNote;
                float noteWidth = noteSprite == null ? buttonWidth : noteHeight * noteSprite.rect.width / noteSprite.rect.height;
                var note = At("Note " + levels[i], levelCenters[i], noteY, noteWidth, noteHeight);
                if (noteSprite != null)
                {
                    var image = Image(note, Color.white);
                    image.sprite = noteSprite;
                    image.preserveAspect = true;
                }
                else
                {
                    UiKit.Label(note, i == 0 ? EasyNote : HardNote, 20, Caption, TextAlignmentOptions.Center, font);
                }

                refresh += () =>
                {
                    bool on = (int)GameFlow.Difficulty == index;
                    fill.color = on ? Ink : new Color(0, 0, 0, 0);
                    foreach (var edge in stroke) edge.enabled = !on;
                    label.color = on ? Color.black : Color.white;
                    note.gameObject.SetActive(on);
                };
                rowStrokes[DifficultyRow].AddRange(stroke);
            }
            choices["난이도"] = i => { Sfx.UiConfirm(art); GameFlow.Difficulty = i == 0 ? GameDifficulty.Easy : GameDifficulty.Hard; refresh(); };

            // W S ↕   A D ↔   Esc 뒤로
            Picture(art.settingsKeyHints, "Key Hints", 959.91f, 931.42f, 588, 50, "W S  위/아래     A D  조절     Esc  뒤로");

            BuildDevStrip(frame);
            refresh();
            ResetFlashes();
        }

        /// <summary>Hidden developer switches under the card (Shift+D). Not part of the Figma design.</summary>
        private void BuildDevStrip(RectTransform frame)
        {
            var strip = TopLeft(Rect(frame, "Dev"), CardLeft, CardTop + CardHeight + 12, CardWidth, 44);
            devStrip = strip.gameObject;
            var title = UiKit.Label(TopLeft(Rect(strip, "Label"), 0, 0, 80, 44), "DEV", 22, Caption, TextAlignmentOptions.Left, font);
            title.characterSpacing = -5;
            DevRow(strip, 90, 80, "무적", new[] { "꺼짐", "켜짐" }, () => GameFlow.Invincible ? 1 : 0, i => GameFlow.Invincible = i == 1);
            DevRow(strip, 360, 110, "보스 HP", new[] { "꺼짐", "켜짐" }, () => GameFlow.BossHpOff ? 0 : 1, i => GameFlow.BossHpOff = i == 0);
            DevRow(strip, 660, 140, "시작 페이즈", new[] { "1", "2", "3", "4" }, () => GameFlow.StartPhase - 1, i => GameFlow.StartPhase = i + 1);
            choices["개발자 모드"] = i => { GameFlow.DevMode = i == 1; refresh(); };
            refresh += () => devStrip.SetActive(GameFlow.DevMode);
        }
        private void DevRow(RectTransform strip, float left, float nameWidth, string name, string[] options, Func<int> current, Action<int> set)
        {
            var row = TopLeft(Rect(strip, name), left, 0, nameWidth + 74 * options.Length, 44);
            UiKit.Label(TopLeft(Rect(row, "Name"), 0, 0, nameWidth, 44), name, 22, Ink, TextAlignmentOptions.Left, font);
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var box = TopLeft(Rect(row, "Option " + options[i]), nameWidth + 74 * i, 4, 66, 36);
                var fill = Image(box, Ink); fill.raycastTarget = true;
                var label = UiKit.Label(Stretch(Rect(box, "Label")), options[i], 20, Color.black, TextAlignmentOptions.Center, font);
                box.gameObject.AddComponent<MenuButton>().Clicked = () => choices[name](index);
                refresh += () => { bool on = current() == index; fill.color = on ? Ink : Hex(0x1E1E1E); label.color = on ? Color.black : Ink; };
            }
            choices[name] = i => { set(i); refresh(); };
        }

        /// <summary>The ◀ and ▶ buttons of a row (65x65, 2 px #6D6D6D stroke), optionally with 앞당김 / 늦춤 under them.</summary>
        private void Steppers(ArtSet art, int row, bool captions)
        {
            for (int side = 0; side < 2; side++)
            {
                int direction = side == 0 ? -1 : 1;
                float x = side == 0 ? LeftButton : RightButton;
                var box = At(row + (side == 0 ? " Left" : " Right"), x, RowCenters[row], ButtonSize, ButtonSize);
                Image(box, new Color(0, 0, 0, 0)).raycastTarget = true;
                rowStrokes[row].AddRange(StrokeImages(box, art.settingsRoundedButtonBorder));
                // Press flash: the box's rounded shape in white, over the border and under the arrow, clear until it steps.
                stepFlashes[row * 2 + side] = SlicedImage(Stretch(Rect(box, "Press Flash")), new Color(1, 1, 1, 0), art.settingsRoundedFill);
                // The arrow sits 2 px off centre toward where it points.
                var arrow = Rect(box, "Arrow", new Vector2(2 * direction, 0), new Vector2(18.18f, 30.19f));
                if (art.settingsArrow != null)
                {
                    Image(arrow, Color.white).sprite = art.settingsArrow;
                    if (direction > 0) arrow.localScale = new Vector3(-1, 1, 1);
                }
                else Text(Stretch(arrow), direction < 0 ? "◀" : "▶", 30, Ink, font);
                var button = HoverRow(box.gameObject, row);
                button.Clicked = () => { Select(row); Step(row, direction); };
                if (row == InputRow || row == MusicRow) button.RepeatWhileHeld(RepeatDelay, RepeatEvery);
                if (captions)
                {
                    var text = Text(At(row + (side == 0 ? " 앞당김" : " 늦춤"), x, RowCenters[row] + 50, 90, 35), side == 0 ? "앞당김" : "늦춤", 24, Caption, font);
                    text.characterSpacing = -10;
                }
            }
        }
        /// <summary>The volume line: #D9D9D9 up to the value, #6D6D6D after, and a hollow diamond to drag.</summary>
        private void BuildSlider(int row)
        {
            var area = At("음량 Slider", 1268.17f, 341.2f, 250, 24);
            var track = Rect(area, "Track"); Stretch(track); track.anchorMin = new Vector2(0, .5f); track.anchorMax = new Vector2(1, .5f); track.sizeDelta = new Vector2(0, 5);
            Image(track, Line).raycastTarget = true;
            var fillArea = Rect(area, "Fill Area"); Stretch(fillArea); fillArea.anchorMin = new Vector2(0, .5f); fillArea.anchorMax = new Vector2(1, .5f); fillArea.sizeDelta = new Vector2(0, 5);
            var fill = Stretch(Rect(fillArea, "Fill")); Image(fill, Ink);
            var handle = Rect(Stretch(Rect(area, "Handle Area")), "Handle", Vector2.zero, new Vector2(36, 12));
            var hit = Image(handle, new Color(0, 0, 0, 0)); hit.raycastTarget = true;
            // Figma: a 25 px black square with a 5 px #D9D9D9 border, turned 45 degrees.
            var diamond = Rect(handle, "Diamond", Vector2.zero, new Vector2(25, 25));
            diamond.localEulerAngles = new Vector3(0, 0, 45);
            Image(diamond, Ink);
            Image(Rect(diamond, "Hole", Vector2.zero, new Vector2(15, 15)), Color.black);
            var slider = area.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = hit; slider.transition = UnityEngine.UI.Selectable.Transition.None;
            slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            slider.wholeNumbers = true; slider.minValue = 0; slider.maxValue = 100; slider.SetValueWithoutNotify(VolumePercent);
            // The slider sits on top of its row, so it lights the row while hovered like the row itself does.
            HoverRow(area.gameObject, row);
            slider.onValueChanged.AddListener(v => { GameFlow.MusicVolume = v / 100f; refresh(); });
            // Another panel (menu or battle) may have changed the saved value since this one was built.
            refresh += () => slider.SetValueWithoutNotify(VolumePercent);
        }

        /// <summary>
        /// Hovering <paramref name="target"/> lights <paramref name="row"/>; leaving it clears the light. A row picked since
        /// then with W/S is left alone, and moving onto another part of the same row lights it again at once.
        /// </summary>
        private MenuButton HoverRow(GameObject target, int row)
        {
            var button = target.AddComponent<MenuButton>();
            button.Hovered = () => Select(row);
            button.Exited = () => { if (SelectedRow == row) Select(-1); };
            return button;
        }
        /// <summary>A Figma row title exported as a picture; plain text when the picture is not delivered.</summary>
        private void Picture(Sprite sprite, string name, float centerX, float centerY, float width, float height, string title, string caption = null)
        {
            var rect = At(name, centerX, centerY, width, height);
            if (sprite != null) { Image(rect, Color.white).sprite = sprite; return; }
            var text = UiKit.Label(Stretch(rect), caption == null ? title : title + "\n<size=40%><color=#6F6F6E>" + caption, 48, Ink, TextAlignmentOptions.Left, font);
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }
        /// <summary>A rect by its centre in Figma frame pixels, placed in the card.</summary>
        private RectTransform At(string name, float centerX, float centerY, float width, float height) =>
            Rect(card, name, new Vector2(centerX - (CardLeft + CardWidth / 2), -(centerY - (CardTop + CardHeight / 2))), new Vector2(width, height));
        private List<UnityEngine.UI.Image> StrokeImages(RectTransform box, Sprite sprite)
        {
            if (sprite != null)
            {
                var border = SlicedImage(Stretch(Rect(box, "Border")), Line, sprite);
                border.fillCenter = false;
                return new List<UnityEngine.UI.Image> { border };
            }

            int before = box.childCount;
            Stroke(box, Line, 2);
            var edges = new List<UnityEngine.UI.Image>();
            for (int i = before; i < box.childCount; i++) edges.Add(box.GetChild(i).GetComponent<UnityEngine.UI.Image>());
            return edges;
        }
        private TMP_Text Text(RectTransform rect, string text, float size, Color color, TMP_FontAsset face) =>
            Label(rect, text, size, color, TextAlignmentOptions.Center, face);
    }
}
