using System;
using StringAsASword.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using static StringAsASword.Presentation.UiKit;

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
        private GameObject storyPanel;
        private SettingsPanel settings;
        private TMP_Text storyText, storyHint;
        private UnityEngine.UI.Image storyImage;
        private CanvasGroup storyGroup;
        private int storyIndex = -1;
        private float storyAge;
        private bool leaving;
        public bool White { get; private set; }
        public bool StoryPlaying => storyIndex >= 0;
        public int StoryIndex => storyIndex;
        public bool SettingsOpen => settings != null && settings.IsOpen;
        public int Selected => selected;
        /// <summary>Developer mode: picks an option of a settings row as if it were clicked (0 = first).</summary>
        public void ChooseSetting(string row, int option) => settings.Choose(row, option);
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
            settings = SettingsPanel.Create(root, font);
            BuildStory(root);
            ApplyTheme();
            Select(-1);
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (storyIndex >= 0) { UpdateStory(keyboard); return; }
            if (keyboard == null) return;
            if (SettingsOpen) { if (keyboard.escapeKey.wasPressedThisFrame) settings.Close(); return; }
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
            else if (index == 1) settings.Open();
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
        private TMP_Text Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align) => UiKit.Label(rect, text, size, color, align, font);
    }
}
