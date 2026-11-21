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
    /// After a clear the left side of the art is inverted to white (the chessboard stays).
    /// 시작 always opens with the prologue; the first time the tutorial follows it, later the song does.
    /// Settings > 튜토리얼 plays the tutorial again, then the song.
    /// </summary>
    public sealed class MainMenuView : MonoBehaviour
    {
        // Figma values, in 1920x1080 reference pixels from the top-left corner.
        private static readonly Color Menu = Hex(0xD9D9D9), HoverBox = Hex(0x929292), HoverText = Hex(0x0C0C0C), Footer = Hex(0x929292), Back = Hex(0x020202);
        private const float MenuLeft = 184.5f, MenuHeight = 107, MenuSize = 80;
        private static readonly float[] MenuTops = { 631.21f, 733.01f, 834.80f };
        private static readonly string[] MenuLabels = { "시작", "설정", "종료" };
        public const int StartItem = 0, SettingsItem = 1, QuitItem = 2;
        // Figma sizes are for a two-letter label: the text box is 125 wide and its hover box 119.94; longer labels grow by letter.
        private const float TwoLetterWidth = 125, TwoLetterBox = 119.94f;

        [SerializeField] private ArtSet art;
        [SerializeField] private Material invertMaterial;

        private TMP_FontAsset font;
        private UnityEngine.UI.Image background;
        private Material backgroundMaterial;
        private readonly TMP_Text[] labels = new TMP_Text[MenuLabels.Length];
        private readonly UnityEngine.UI.Image[] boxes = new UnityEngine.UI.Image[MenuLabels.Length];
        private TMP_Text footer;
        private int selected = -1;
        private SettingsPanel settings;
        /// <summary>The settings panel (with its 튜토리얼 button).</summary>
        public SettingsPanel Settings => settings;
        private bool leaving;
        public bool White { get; private set; }
        public int ItemCount => MenuLabels.Length;
        public bool SettingsOpen => settings != null && settings.IsOpen;
        public int Selected => selected;
        /// <summary>Developer mode: picks an option of a settings row as if it were clicked (0 = first).</summary>
        public void ChooseSetting(string row, int option) => settings.Choose(row, option);
        public string LabelColorHex(int index) => ColorUtility.ToHtmlStringRGB(labels[index].color);

        private void Awake()
        {
            if (art == null) { art = ScriptableObject.CreateInstance<ArtSet>(); art.hideFlags = HideFlags.DontSave; }
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

            for (int i = 0; i < MenuLabels.Length; i++)
            {
                int index = i;
                float letters = MenuLabels[i].Length / 2f;
                var item = TopLeft(Rect(root, "Menu_" + MenuLabels[i]), MenuLeft, MenuTops[i], TwoLetterWidth * letters, MenuHeight);
                // Figma "Mouse Hover": a box behind the label, offset (4.25, 14.67) inside the 125x107 text box.
                boxes[i] = Image(TopLeft(Rect(item, "Hover"), 4.25f, 14.67f, TwoLetterBox * letters, 66.24f), HoverBox);
                labels[i] = Label(Stretch(Rect(item, "Label")), MenuLabels[i], MenuSize, Menu, TextAlignmentOptions.TopLeft); // child: drawn above the box
                labels[i].characterSpacing = -4; // -3.2 px at 80 px
                var hit = Image(Stretch(Rect(item, "Hit")), new Color(0, 0, 0, 0)); hit.raycastTarget = true;
                var button = hit.gameObject.AddComponent<MenuButton>();
                button.Hovered = () => Select(index); button.Clicked = () => Activate(index);
            }
            footer = Label(TopLeft(Rect(root, "Credit"), 15, 1052, 400, 21), "Inspired by 이승윤 <검을 현>", 16, Footer, TextAlignmentOptions.TopLeft);
            settings = SettingsPanel.Create(root, font);
            settings.TutorialRequested += () => Leave(prologue: false, tutorial: true);
            ApplyTheme();
            Select(-1);
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || leaving) return;
            if (SettingsOpen) { if (keyboard.escapeKey.wasPressedThisFrame) settings.Close(); return; }
            int last = MenuLabels.Length - 1;
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) Select(selected <= 0 ? last : selected - 1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) Select(selected < 0 || selected == last ? 0 : selected + 1);
            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) && selected >= 0) Activate(selected);
            // Hidden: Shift+R brings back the dark menu (for demos).
            if (keyboard.rKey.wasPressedThisFrame && keyboard.shiftKey.isPressed) { GameFlow.ResetCleared(); ApplyTheme(); }
        }

        public void Select(int index)
        {
            selected = index;
            Color text = White ? Invert(Menu) : Menu, hoverText = White ? Invert(HoverText) : HoverText;
            for (int i = 0; i < MenuLabels.Length; i++)
            {
                boxes[i].enabled = i == index;
                labels[i].color = i == index ? hoverText : text;
            }
        }
        public void Activate(int index)
        {
            if (leaving) return;
            // The story cards are retired: the prologue tells the same story every time 시작 is pressed.
            if (index == StartItem) Leave(prologue: true);
            else if (index == SettingsItem) settings.Open();
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

        private void Leave(bool prologue, bool tutorial = false)
        {
            if (leaving) return;
            leaving = true;
            GameFlow.StartBattle(prologue, tutorial);
        }

        // --- Building blocks -------------------------------------------------------------------------------
        private TMP_Text Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align) => UiKit.Label(rect, text, size, color, align, font);
    }
}
