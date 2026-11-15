using System;
using StringAsASword.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Main menu from the Figma frame "Main Menu" (1920x1080): background art, 시작 / 설정 / 종료, footer credit.
    /// After a clear the left side of the art is inverted to white (the chessboard stays). 시작 plays the story cards.
    /// </summary>
    public sealed class MainMenuView : MonoBehaviour
    {
        // Figma values, in 1920x1080 reference pixels from the top-left corner.
        private static readonly Color Menu = Hex(0xD9D9D9), HoverBox = Hex(0x929292), HoverText = Hex(0x0C0C0C), Footer = Hex(0x929292), Back = Hex(0x020202);
        private const float MenuLeft = 184.5f, MenuWidth = 125, MenuHeight = 107, MenuSize = 80;
        private static readonly float[] MenuTops = { 631.21f, 733.01f, 834.80f };
        private static readonly string[] MenuLabels = { "시작", "설정", "종료" };
        private const float StoryFade = .45f;

        [SerializeField] private ArtSet art;
        [SerializeField] private StoryDefinition story;
        [SerializeField] private Material invertMaterial;

        private TMP_FontAsset font;
        private UnityEngine.UI.Image background;
        private Material backgroundMaterial;
        private readonly TMP_Text[] labels = new TMP_Text[3];
        private readonly UnityEngine.UI.Image[] boxes = new UnityEngine.UI.Image[3];
        private TMP_Text footer;
        private int selected = -1;
        private GameObject settingsPanel, storyPanel;
        private TMP_Text storyText, storyHint;
        private UnityEngine.UI.Image storyImage;
        private CanvasGroup storyGroup;
        private int storyIndex = -1;
        private float storyAge;
        private bool leaving;
        public bool White { get; private set; }
        public bool StoryPlaying => storyIndex >= 0;
        public int StoryIndex => storyIndex;
        public bool SettingsOpen => settingsPanel != null && settingsPanel.activeSelf;
        public int Selected => selected;
        public string LabelColorHex(int index) => ColorUtility.ToHtmlStringRGB(labels[index].color);

        private void Awake()
        {
            if (art == null) { art = ScriptableObject.CreateInstance<ArtSet>(); art.hideFlags = HideFlags.DontSave; }
            if (story == null) story = ScriptableObject.CreateInstance<StoryDefinition>();
            font = UiFonts.Korean(art.menuFont);
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (EventSystem.current == null)
                new GameObject("MenuEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);
            var root = (RectTransform)transform;

            Image(Stretch(Rect(root, "Backdrop")), Back);
            // The art covers the screen at its own 16:9 aspect, cropping rather than stretching on other screens.
            var artRect = Rect(root, "MainMenu_Logo_BG"); Stretch(artRect);
            var fitter = artRect.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent; fitter.aspectRatio = 16f / 9f;
            background = Image(artRect, Color.white);
            background.sprite = art.mainMenuDark; background.enabled = art.mainMenuDark != null;
            if (invertMaterial != null) background.material = backgroundMaterial = new Material(invertMaterial);

            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var item = TopLeft(Rect(root, "Menu_" + MenuLabels[i]), MenuLeft, MenuTops[i], MenuWidth, MenuHeight);
                // Figma "Mouse Hover": a box behind the label, offset (4.25, 14.67) inside the 125x107 text box.
                boxes[i] = Image(TopLeft(Rect(item, "Hover"), 4.25f, 14.67f, 119.94f, 66.24f), HoverBox);
                labels[i] = Label(Stretch(Rect(item, "Label")), MenuLabels[i], MenuSize, Menu, TextAlignmentOptions.TopLeft); // child: drawn above the box
                labels[i].characterSpacing = -4; // -3.2 px at 80 px
                var hit = Image(Stretch(Rect(item, "Hit")), new Color(0, 0, 0, 0)); hit.raycastTarget = true;
                var button = hit.gameObject.AddComponent<MenuButton>();
                button.Hovered = () => Select(index); button.Clicked = () => Activate(index);
            }
            footer = Label(TopLeft(Rect(root, "Credit"), 15, 1052, 400, 21), "Inspired by 이승윤 <검을 현>", 16, Footer, TextAlignmentOptions.TopLeft);
            BuildSettings(root);
            BuildStory(root);
            ApplyTheme();
            Select(-1);
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (storyIndex >= 0) { UpdateStory(keyboard); return; }
            if (keyboard == null) return;
            if (SettingsOpen) { if (keyboard.escapeKey.wasPressedThisFrame) settingsPanel.SetActive(false); return; }
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) Select(selected <= 0 ? 2 : selected - 1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) Select(selected < 0 || selected == 2 ? 0 : selected + 1);
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) && selected >= 0) Activate(selected);
            // Hidden: Shift+R brings back the dark menu (for demos).
            if (keyboard.rKey.wasPressedThisFrame && keyboard.shiftKey.isPressed) { GameFlow.ResetCleared(); ApplyTheme(); }
        }

        public void Select(int index)
        {
            selected = index;
            Color text = White ? Invert(Menu) : Menu, hoverText = White ? Invert(HoverText) : HoverText;
            for (int i = 0; i < 3; i++)
            {
                boxes[i].enabled = i == index;
                labels[i].color = i == index ? hoverText : text;
            }
        }
        public void Activate(int index)
        {
            if (leaving) return;
            if (index == 0) BeginStory();
            else if (index == 1) settingsPanel.SetActive(true);
            else
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
        public void ApplyTheme()
        {
            White = GameFlow.Cleared;
            bool handMade = White && art.mainMenuWhite != null;
            if (handMade) background.sprite = art.mainMenuWhite;
            else if (art.mainMenuDark != null) background.sprite = art.mainMenuDark;
            if (backgroundMaterial != null) backgroundMaterial.SetFloat("_Invert", White && !handMade ? 1 : 0);
            foreach (var box in boxes) box.color = White ? Invert(HoverBox) : HoverBox;
            footer.color = White ? Invert(Footer) : Footer;
            Select(selected);
        }

        // --- Story cards ---------------------------------------------------------------------------------
        public void BeginStory()
        {
            if (story.cards == null || story.cards.Length == 0) { Leave(); return; }
            storyPanel.SetActive(true); storyIndex = 0; storyAge = 0; ShowCard();
        }
        public void NextCard()
        {
            if (storyIndex < 0) return;
            if (++storyIndex >= story.cards.Length) { Leave(); return; }
            storyAge = 0; ShowCard();
        }
        public void SkipStory() { if (storyIndex >= 0) Leave(); }
        private void ShowCard()
        {
            var card = story.cards[storyIndex];
            storyText.text = card.text;
            storyImage.sprite = card.image; storyImage.enabled = card.image != null;
            storyText.rectTransform.anchoredPosition = new Vector2(0, card.image != null ? -300 : 0);
        }
        private void UpdateStory(Keyboard keyboard)
        {
            storyAge += Time.unscaledDeltaTime;
            storyGroup.alpha = Mathf.Clamp01(storyAge / StoryFade);
            if (keyboard == null || leaving) return;
            if (keyboard.escapeKey.wasPressedThisFrame) SkipStory();
            else if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame) NextCard();
        }
        private void Leave()
        {
            if (leaving) return;
            leaving = true; storyIndex = -1;
            GameFlow.StartBattle();
        }

        // --- Building blocks -------------------------------------------------------------------------------
        private void BuildStory(RectTransform root)
        {
            var panel = Stretch(Rect(root, "Story"));
            Image(panel, Back).raycastTarget = true;
            panel.gameObject.AddComponent<MenuButton>().Clicked = NextCard;
            storyGroup = panel.gameObject.AddComponent<CanvasGroup>();
            storyImage = Image(Rect(panel, "Illustration", new Vector2(0, 90), new Vector2(960, 540)), Color.white);
            storyImage.preserveAspect = true;
            storyText = Label(Rect(panel, "Caption", Vector2.zero, new Vector2(1600, 200)), "", 44, Menu, TextAlignmentOptions.Center);
            storyText.lineSpacing = 18;
            storyHint = Label(Rect(panel, "Hint", new Vector2(0, -470), new Vector2(800, 40)), "SPACE  다음        ESC  건너뛰기", 20, Footer, TextAlignmentOptions.Center);
            storyPanel = panel.gameObject; storyPanel.SetActive(false);
        }
        private void BuildSettings(RectTransform root)
        {
            var shade = Stretch(Rect(root, "Settings"));
            Image(shade, new Color(0, 0, 0, .72f)).raycastTarget = true;
            var card = Rect(shade, "Card", Vector2.zero, new Vector2(820, 830));
            Image(card, Hex(0x0C0C0C)).raycastTarget = true;
            Label(Rect(card, "Title", new Vector2(0, 360), new Vector2(700, 80)), "설정", 56, Menu, TextAlignmentOptions.Center);
            SliderRow(card, 250, "음악 볼륨", 0, 100, Mathf.RoundToInt(GameFlow.MusicVolume * 100), v => GameFlow.MusicVolume = v / 100f, v => v + "%");
            SliderRow(card, 155, "입력 싱크", -200, 200, GameFlow.InputOffsetMs, v => GameFlow.InputOffsetMs = v, v => (v > 0 ? "+" : "") + v + " ms");
            SliderRow(card, 60, "화면 싱크", -200, 200, GameFlow.VisualOffsetMs, v => GameFlow.VisualOffsetMs = v, v => (v > 0 ? "+" : "") + v + " ms");
            Label(Rect(card, "Help", new Vector2(0, -15), new Vector2(760, 40)), "입력 싱크: 판정이 늦으면 +   /   화면 싱크: 노트가 소리보다 늦게 보이면 -", 18, Footer, TextAlignmentOptions.Center);
            // Developer mode: the two rows under it only show while it is on.
            Image(Rect(card, "Rule", new Vector2(0, -57), new Vector2(720, 2)), Hex(0x333333));
            ChoiceRow(card, -100, "개발자 모드", new[] { "꺼짐", "켜짐" }, () => GameFlow.DevMode ? 1 : 0, i => GameFlow.DevMode = i == 1);
            var invincible = ChoiceRow(card, -165, "무적", new[] { "꺼짐", "켜짐" }, () => GameFlow.Invincible ? 1 : 0, i => GameFlow.Invincible = i == 1);
            var bossHp = ChoiceRow(card, -230, "보스 HP", new[] { "꺼짐", "켜짐" }, () => GameFlow.BossHpOff ? 0 : 1, i => GameFlow.BossHpOff = i == 0);
            var phase = ChoiceRow(card, -295, "시작 페이즈", new[] { "1", "2", "3", "4" }, () => GameFlow.StartPhase - 1, i => GameFlow.StartPhase = i + 1);
            devRefresh += () => { invincible.SetActive(GameFlow.DevMode); phase.SetActive(GameFlow.DevMode); bossHp.SetActive(GameFlow.DevMode); };
            devRefresh();
            var close = Rect(card, "Close", new Vector2(0, -370), new Vector2(200, 56));
            Image(close, HoverBox).raycastTarget = true;
            Label(Stretch(Rect(close, "Label")), "닫기  ESC", 26, HoverText, TextAlignmentOptions.Center);
            close.gameObject.AddComponent<MenuButton>().Clicked = () => settingsPanel.SetActive(false);
            settingsPanel = shade.gameObject; settingsPanel.SetActive(false);
        }
        private Action devRefresh = () => { };
        /// <summary>Developer mode: picks an option of a settings row as if it were clicked (0 = first).</summary>
        public void ChooseSetting(string row, int option) => devChoices[row](option);
        private readonly System.Collections.Generic.Dictionary<string, Action<int>> devChoices = new System.Collections.Generic.Dictionary<string, Action<int>>();
        // A label and a few boxes to click; the chosen one is filled.
        private GameObject ChoiceRow(RectTransform card, float y, string name, string[] options, Func<int> current, Action<int> set)
        {
            var row = Rect(card, name, new Vector2(0, y), new Vector2(760, 50));
            Label(Rect(row, "Name", new Vector2(-250, 0), new Vector2(220, 50)), name, 30, Menu, TextAlignmentOptions.Left);
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var box = Rect(row, "Option " + options[i], new Vector2(-80 + 110 * i, 0), new Vector2(100, 46));
                var fill = Image(box, HoverBox); fill.raycastTarget = true;
                var text = Label(Stretch(Rect(box, "Label")), options[i], 24, Menu, TextAlignmentOptions.Center);
                box.gameObject.AddComponent<MenuButton>().Clicked = () => devChoices[name](index);
                devRefresh += () => { bool on = current() == index; fill.color = on ? HoverBox : Hex(0x1E1E1E); text.color = on ? HoverText : Menu; };
            }
            devChoices[name] = i => { set(i); devRefresh(); };
            return row.gameObject;
        }
        private void SliderRow(RectTransform card, float y, string name, int min, int max, int value, Action<int> set, Func<int, string> show)
        {
            Label(Rect(card, name, new Vector2(-250, y), new Vector2(220, 50)), name, 30, Menu, TextAlignmentOptions.Left);
            var valueText = Label(Rect(card, name + " Value", new Vector2(300, y), new Vector2(150, 50)), show(value), 26, Menu, TextAlignmentOptions.Right);
            var area = Rect(card, name + " Slider", new Vector2(40, y), new Vector2(360, 24));
            Image(Stretch(Rect(area, "Track")), Hex(0x333333));
            var fillArea = Stretch(Rect(area, "Fill Area"));
            var fill = Stretch(Rect(fillArea, "Fill")); Image(fill, HoverBox);
            var handleArea = Stretch(Rect(area, "Handle Area"));
            var handle = Rect(handleArea, "Handle", Vector2.zero, new Vector2(20, 40)); Image(handle, Menu).raycastTarget = true;
            var slider = area.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
            slider.wholeNumbers = true; slider.minValue = min; slider.maxValue = max; slider.value = value;
            slider.onValueChanged.AddListener(v => { set((int)v); valueText.text = show((int)v); });
        }

        private static RectTransform Rect(Transform parent, string name) => PrototypeView.MakeRect(parent, name, Vector2.zero, Vector2.zero);
        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size) => PrototypeView.MakeRect(parent, name, position, size);
        private static RectTransform Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero; return rect; }
        // Figma coordinates: (left, top) from the frame's top-left corner, y down.
        private static RectTransform TopLeft(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        private static UnityEngine.UI.Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
        private TMP_Text Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.text = text; label.fontSize = size; label.color = color; label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }
        private static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        private static Color Invert(Color c) => new Color(1 - c.r, 1 - c.g, 1 - c.b, c.a);
    }
}
