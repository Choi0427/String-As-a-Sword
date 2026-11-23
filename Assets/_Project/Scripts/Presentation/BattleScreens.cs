using System;
using System.Collections;
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
    /// The game stays visible behind them, as in the mockups; behind the pause screen it is blurred (a still taken as the
    /// game stops, so only the pause UI is sharp). Items: hover or W/S to pick, click or Enter/Space to press.
    /// </summary>
    public sealed class BattleScreens : MonoBehaviour
    {
        public enum ScreenKind { None, Pause, GameOver }
        private static readonly Color Ink = Hex(0xD9D9D9), HoverBox = Hex(0x929292), HoverText = Hex(0x0C0C0C);
        private const float ItemSize = 65, ItemSpacing = -10; // Orbit Regular 65 px, letter spacing -10%.

        private PrototypeGame game;
        private TMP_FontAsset itemFont, titleFont;
        private GameObject keyGuide;
        private CanvasGroup jLight;
        private float jGlow;
        private const float JFadeSeconds = .12f;
        private Panel pause, gameOver;
        private TMP_Text gameOverTitle;
        private SettingsPanel settings;
        /// <summary>The settings panel.</summary>
        public SettingsPanel Settings => settings;
        private Panel shown;
        // Pause blur: a screenshot taken the frame the game pauses (before the card shows), shrunk and softened on the CPU.
        private const int BlurDownscale = 8, BlurRadius = 2, BlurPasses = 3;
        private UnityEngine.UI.RawImage blur;
        private Texture2D blurTexture;
        private Coroutine capturing;
        /// <summary>The blurred still of the game is up behind the pause screen.</summary>
        public bool BlurShown => blur != null && blur.enabled;
        public ScreenKind Showing { get; private set; }
        public int Selected => shown?.Selected ?? -1;
        public bool SettingsOpen => settings.IsOpen;
        public bool KeyGuideVisible => keyGuide != null && keyGuide.activeSelf;
        /// <summary>The bottom key guide's opacity: the tutorial dims it while its own prompt is up, so only one is emphasised.</summary>
        public float KeyGuideAlpha { get; set; } = 1;
        /// <summary>The J key is lit: a collected noise has charged the string and waits for an attack.</summary>
        public bool JLit { get; private set; }
        /// <summary>The J light's shown opacity (fades with the key guide).</summary>
        public float JLightAlpha => jLight != null ? jLight.alpha : 0;
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
                // The J key lights from behind while a charge waits for its attack: the guide's key caps are almost
                // see-through (fill alpha 3/255), so a soft halo and a fill under the picture read as a lit key.
                // Game Play / 공격 / J: 50 x 50 at (945.5, 965.8).
                var light = TopLeft(Rect(frame, "J Light"), 945.48f, 965.83f, 50, 50);
                jLight = light.gameObject.AddComponent<CanvasGroup>();
                jLight.alpha = 0; jLight.blocksRaycasts = false; jLight.interactable = false;
                var halo = Image(Rect(light, "Halo", Vector2.zero, new Vector2(104, 104)), Color.white);
                halo.sprite = PrototypeView.SoftDotSprite(); halo.color = new Color(1, 1, 1, .35f);
                var fill = Image(Stretch(Rect(light, "Fill")), new Color(Ink.r, Ink.g, Ink.b, .3f));
                // Inside the cap's rounded border.
                fill.rectTransform.offsetMin = new Vector2(2, 2); fill.rectTransform.offsetMax = new Vector2(-2, -2);
                // Game Play / 키 가이드: 723 x 104 at (598.5, 938.7).
                var guide = Image(TopLeft(Rect(frame, "Key Guide"), 598.45f, 938.74f, 723.10f, 104.17f), Color.white);
                guide.sprite = art.keyGuide; guide.preserveAspect = true;
                keyGuide = guide.gameObject;
            }
            // Under the cards, over the game and the key guide.
            blur = Rect(frame, "Pause Blur").gameObject.AddComponent<UnityEngine.UI.RawImage>();
            blur.raycastTarget = false; blur.enabled = false;
            pause = new Panel(this, art, frame, "Pause", 287.59f, 504.81f, "PAUSED", 341.95f, 470,
                new Item("판으로 복귀", 810.5f, 462.76f, 300, game.ResumeSession),
                new Item("설정", 905.5f, 556.76f, 111, OpenSettings),
                new Item("메인 메뉴", 836, 650.76f, 248, game.QuitGame));
            gameOver = new Panel(this, art, frame, "Game Over", 327.19f, 425.63f, "FALLEN", 383.54f, 431,
                new Item("첫 수부터", 836.5f, 502.26f, 248, game.RestartFromMenu),
                new Item("메인 메뉴", 836, 596.26f, 248, game.QuitGame));
            gameOverTitle = gameOver.Title;
            settings = SettingsPanel.Create(frame, art);
            settings.Closed += game.ApplySettings;
            // From the pause screen: this run is dropped for the tutorial, which then leads into the song.
            settings.TutorialRequested += game.StartTutorial;
            Show(ScreenKind.None);
        }

        private void Update()
        {
            var state = game.State;
            if (keyGuide != null)
            {
                // The death close-up darkens everything under these screens; the guide would float over it.
                keyGuide.SetActive(state != PrototypeState.Epilogue && (game.Death == null || !game.Death.Covering));
                var guide = keyGuide.GetComponent<UnityEngine.UI.Image>();
                if (!Mathf.Approximately(guide.color.a, KeyGuideAlpha)) guide.color = new Color(1, 1, 1, KeyGuideAlpha);
                bool playing = state == PrototypeState.Playing || state == PrototypeState.CountIn || state == PrototypeState.Paused ||
                    state == PrototypeState.Tutorial;
                JLit = playing && game.Battle != null && game.Battle.Resonance.Charged;
                jGlow = Mathf.MoveTowards(jGlow, JLit ? 1 : 0, Time.unscaledDeltaTime / JFadeSeconds);
                // Breathes at the charge's slow rate, never fully dark while lit.
                float breath = .78f + .22f * Mathf.Sin(Time.unscaledTime * 3.2f);
                jLight.alpha = jGlow * breath * KeyGuideAlpha;
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
            if (capturing != null) { StopCoroutine(capturing); capturing = null; }
            blur.enabled = false;
            // The still must not show the card, so the card waits for it (batch mode has no frame to capture).
            bool blurNow = screen == ScreenKind.Pause && !Application.isBatchMode &&
                SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            pause.SetActive(shown == pause && !blurNow); gameOver.SetActive(shown == gameOver);
            if (blurNow) capturing = StartCoroutine(CaptureBlur());
            if (screen == ScreenKind.GameOver)
                gameOverTitle.text = game.Battle.Outcome == BattleOutcome.Won ? "CLEARED" : "FALLEN";
        }
        private IEnumerator CaptureBlur()
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try { shot = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e) { Debug.LogWarning("[Pause] No blur: " + e.Message); }
            if (shot != null)
            {
                blurTexture = Blur(shot, blurTexture);
                Destroy(shot);
                CoverScreen((RectTransform)blur.transform);
                blur.texture = blurTexture; blur.enabled = true;
            }
            capturing = null;
            if (Showing == ScreenKind.Pause) pause.SetActive(true);
        }
        /// <summary>Lays the rect over the whole screen (the root canvas), wherever this frame sits in it.</summary>
        private void CoverScreen(RectTransform rect)
        {
            var canvas = (RectTransform)GetComponentInParent<Canvas>().rootCanvas.transform;
            var corners = new Vector3[4]; canvas.GetWorldCorners(corners);
            var parent = (RectTransform)rect.parent;
            Vector2 min = parent.InverseTransformPoint(corners[0]), max = parent.InverseTransformPoint(corners[2]);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = (min + max) / 2 - parent.rect.center; rect.sizeDelta = max - min;
        }
        /// <summary>Averages the screenshot down by <see cref="BlurDownscale"/>, then box-blurs it a few times (close to a
        /// Gaussian); drawn back at full size with bilinear filtering it reads as a soft blur.</summary>
        private static Texture2D Blur(Texture2D shot, Texture2D into)
        {
            int w = Mathf.Max(1, shot.width / BlurDownscale), h = Mathf.Max(1, shot.height / BlurDownscale);
            var src = shot.GetPixels32();
            var r = new float[w * h]; var g = new float[w * h]; var b = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sr = 0, sg = 0, sb = 0;
                    for (int dy = 0; dy < BlurDownscale; dy++)
                    {
                        int row = (y * BlurDownscale + dy) * shot.width + x * BlurDownscale;
                        for (int dx = 0; dx < BlurDownscale; dx++) { var c = src[row + dx]; sr += c.r; sg += c.g; sb += c.b; }
                    }
                    int i = y * w + x; float n = BlurDownscale * BlurDownscale * 255f;
                    r[i] = sr / n; g[i] = sg / n; b[i] = sb / n;
                }
            var tmp = new float[w * h];
            for (int pass = 0; pass < BlurPasses; pass++)
                foreach (var channel in new[] { r, g, b }) { BoxBlur(channel, tmp, w, h, 1, w); BoxBlur(tmp, channel, h, w, w, 1); }
            if (into == null || into.width != w || into.height != h)
            {
                if (into != null) Destroy(into);
                into = new Texture2D(w, h, TextureFormat.RGB24, false) { name = "Pause Blur", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            }
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(r[i], g[i], b[i]);
            into.SetPixels32(pixels); into.Apply(false);
            return into;
        }
        // One box-blur pass along lines of `length` samples (`step` apart), `lines` of them (`lineStep` apart); edges clamp.
        private static void BoxBlur(float[] from, float[] to, int length, int lines, int step, int lineStep)
        {
            float scale = 1f / (2 * BlurRadius + 1);
            for (int line = 0; line < lines; line++)
            {
                int start = line * lineStep;
                float sum = 0;
                for (int k = -BlurRadius; k <= BlurRadius; k++) sum += from[start + Mathf.Clamp(k, 0, length - 1) * step];
                for (int i = 0; i < length; i++)
                {
                    to[start + i * step] = sum * scale;
                    sum += from[start + Mathf.Min(i + BlurRadius + 1, length - 1) * step] - from[start + Mathf.Max(i - BlurRadius, 0) * step];
                }
            }
        }
        private void OnDestroy() { if (blurTexture != null) Destroy(blurTexture); }
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

            public Panel(BattleScreens owner, ArtSet art, RectTransform frame, string name, float top, float height, string title, float titleTop, float titleWidth, params Item[] items)
            {
                Items = items;
                var card = TopLeft(Rect(frame, name), 598.18f, top, 723.63f, height);
                root = card.gameObject;
                DrawMenuCard(card, Ink, art);
                Title = Label(TopLeft(Rect(frame, name + " Title"), 960 - titleWidth / 2, titleTop, titleWidth, 88), title, 80, Ink, TextAlignmentOptions.Center, owner.titleFont);
                Title.transform.SetParent(card, true);
                labels = new TMP_Text[items.Length]; boxes = new UnityEngine.UI.Image[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    int index = i;
                    var item = TopLeft(Rect(frame, "Item " + items[i].Text), items[i].Left, items[i].Top, items[i].Width, 94);
                    item.SetParent(card, true);
                    // Hover, as on the main menu: a #929292 box behind the label, which turns #0C0C0C.
                    boxes[i] = SlicedImage(Stretch(Rect(item, "Hover")), HoverBox, art.settingsRoundedFill);
                    boxes[i].rectTransform.offsetMin = new Vector2(-14, 14); boxes[i].rectTransform.offsetMax = new Vector2(14, -14);
                    labels[i] = Label(Stretch(Rect(item, "Label")), items[i].Text, ItemSize, Ink, TextAlignmentOptions.Center, owner.itemFont);
                    labels[i].characterSpacing = ItemSpacing;
                    // The pointer area is the hover box itself, so the effect shows only while the mouse is on it.
                    var hit = Image(Stretch(Rect(item, "Hit")), new Color(0, 0, 0, 0)); hit.raycastTarget = true;
                    hit.rectTransform.offsetMin = boxes[i].rectTransform.offsetMin; hit.rectTransform.offsetMax = boxes[i].rectTransform.offsetMax;
                    var button = hit.gameObject.AddComponent<MenuButton>();
                    button.Hovered = () => Select(index); button.Clicked = () => Press(index);
                    // Leaving the item clears its hover. A keyboard pick made since then (W/S) is left alone.
                    button.Exited = () => { if (Selected == index) Select(-1); };
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
