using StringAsASword.Core;
using StringAsASword.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Everything after the boss HP is emptied: the stunned king and the outer cells starting to fall, the finishing
    /// triplet, the board collapsing, free movement in the void, and the white ending back to the menu.
    /// Rules stay in BattleSession; this only reads them.
    /// </summary>
    public sealed class FinaleDirector : MonoBehaviour
    {
        public const float FreeRoamSeconds = 6, WhiteSeconds = 1.6f, CaptionSeconds = 2.2f;
        private const float RoamSpeed = 2.6f, RoamBounds = 4.2f;
        public static readonly string[] EndingLines = { "격자가 무너졌다.", "박자도, 흑과 백도 더는 나를 묶지 않는다.", "이제 이 잡음이 나의 노래다." };
        private static readonly Color VoidColor = new Color(.035f, .043f, .052f);

        private PrototypeGame game;
        private PrototypeView view;
        private BoardWorldPresenter board;
        private RectTransform layer;
        private UnityEngine.UI.Image flash, voidCover, white;
        private UnityEngine.UI.RawImage epilogueView;
        private TMP_Text banner, caption;
        private float flashAge = 10, epilogueAge;
        private int lastFinishHit = -1;
        private Vector3 roam;
        private bool returned;
        public float EpilogueAge => epilogueAge;
        public bool Returning => returned;

        public void Initialize(PrototypeGame owner, PrototypeView prototypeView)
        {
            game = owner; view = prototypeView; board = view.BoardWorld;
            layer = PrototypeView.MakeRect(view.CanvasRect, "Finale Layer", Vector2.zero, Vector2.zero);
            Stretch(layer);
            flash = FullScreen("Strike Flash", Color.white);
            voidCover = FullScreen("Void", VoidColor);
            epilogueView = PrototypeView.MakeRect(layer, "Void View", Vector2.zero, new Vector2(1080, 990)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            epilogueView.texture = board.Texture; epilogueView.raycastTarget = false;
            white = FullScreen("White", Color.white);
            var font = UiFonts.Korean(null, view.LatinFont);
            banner = Text("Banner", font, 34, new Vector2(0, 330));
            caption = Text("Caption", font, 40, Vector2.zero);
            caption.color = new Color(.06f, .06f, .06f, 0);
            game.FinaleTap += _ => view.Pulse();
            ResetVisuals();
        }
        private void ResetVisuals()
        {
            SetAlpha(flash, 0); SetAlpha(voidCover, 0); SetAlpha(white, 0);
            epilogueView.gameObject.SetActive(false); banner.text = ""; caption.text = "";
            flashAge = 10; epilogueAge = 0; lastFinishHit = -1; roam = Vector3.zero; returned = false;
            board.FreeRoam = false; board.SetCollapse(0); board.SetKingDown(0); board.SetVoidColor(VoidColor);
            game.Audio.SetMusicFade(1);
        }
        private void Update()
        {
            if (game == null || game.Battle == null) return;
            var settings = game.Battle.Settings;
            if (!settings.HasFinale) return;
            if (game.State == PrototypeState.CountIn && (lastFinishHit >= 0 || epilogueAge > 0 || board.FreeRoam)) ResetVisuals();
            double time = game.SongTime;
            double stunTime = game.Battle.KingStunned ? game.Timeline.TimeOf(game.Battle.StunnedNote) : double.MaxValue;
            double finishTime = game.Timeline.TimeOf(settings.FinishNote);
            flashAge += Time.unscaledDeltaTime;
            SetAlpha(flash, .55f * Mathf.Exp(-12 * flashAge));

            if (game.State == PrototypeState.Playing && game.Battle.KingStunned)
            {
                // The king staggers; the rim of the board starts to give way while the song runs to the finishing run.
                float t = (float)((time - stunTime) / System.Math.Max(.5, finishTime - stunTime));
                board.SetCollapse(.32f * Mathf.Clamp01(t));
                banner.text = "KING STAGGERED"; banner.color = new Color(.94f, .93f, .88f, .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3)));
            }
            else if (game.State == PrototypeState.Finale)
            {
                banner.text = "";
                // One strike per note of the finishing triplet, then the king goes down.
                for (int hit = 0; hit < 3; hit++)
                    if (hit > lastFinishHit && time >= game.Timeline.TimeOf(settings.FinishNote + hit))
                    { lastFinishHit = hit; flashAge = 0; view.Pulse(); }
                double third = game.Timeline.TimeOf(System.Math.Min(settings.FinishNote + 2, settings.NoteCount - 1));
                board.SetKingDown((float)((time - third) / 1.2));
                float rest = (float)((time - finishTime) / System.Math.Max(1, game.FinaleEndTime - finishTime));
                board.SetCollapse(Mathf.Lerp(.32f, .8f, Mathf.Clamp01(rest)));
            }
            else if (game.State == PrototypeState.Epilogue) Epilogue();
        }
        private void Epilogue()
        {
            float dt = Time.unscaledDeltaTime;
            epilogueAge += dt;
            float a = epilogueAge;
            // 0..1 s: the HUD gives way to the void, the rest of the board falls away.
            epilogueView.gameObject.SetActive(true);
            // Run past the point where every piece has dropped out of view (see BoardWorldPresenter.Fall).
            board.SetCollapse(Mathf.Lerp(.8f, 2.2f, Mathf.Clamp01(a / 1.6f)));
            board.FreeRoam = true;
            // Free movement: no grid, no beat. WASD (or arrows), diagonals at the same speed.
            if (a < 1 + FreeRoamSeconds + WhiteSeconds)
            {
                var keyboard = Keyboard.current;
                Vector2 move = Vector2.zero;
                if (keyboard != null)
                {
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
                }
                if (move.sqrMagnitude > 1) move.Normalize();
                if (roam == Vector3.zero) roam = BoardWorldPresenter.CellPosition(game.Session.Position) + Vector3.up * .001f;
                roam += new Vector3(move.x, 0, move.y) * RoamSpeed * dt;
                roam.x = Mathf.Clamp(roam.x, -RoamBounds, RoamBounds); roam.z = Mathf.Clamp(roam.z, -RoamBounds, RoamBounds);
                board.PlayerTransform.localPosition = new Vector3(roam.x, .08f * Mathf.Sin(Time.unscaledTime * 2.2f), roam.z);
            }
            // The void brightens while the knight roams, then everything turns white.
            float roamT = Mathf.Clamp01((a - 1) / FreeRoamSeconds);
            Color voidNow = Color.Lerp(VoidColor, new Color(.82f, .82f, .80f), roamT * roamT);
            board.SetVoidColor(voidNow);
            voidCover.color = new Color(voidNow.r, voidNow.g, voidNow.b, Mathf.Clamp01(a / 1f));
            float whiteT = Mathf.Clamp01((a - 1 - FreeRoamSeconds) / WhiteSeconds);
            SetAlpha(white, whiteT);
            game.Audio.SetMusicFade(1 - whiteT);
            // Ending lines on white, one after another.
            float captionStart = 1 + FreeRoamSeconds + WhiteSeconds;
            int line = Mathf.FloorToInt((a - captionStart) / CaptionSeconds);
            if (line >= 0 && line < EndingLines.Length)
            {
                float local = (a - captionStart) / CaptionSeconds - line;
                caption.text = EndingLines[line];
                caption.color = new Color(.06f, .06f, .06f, Mathf.Clamp01(Mathf.Min(local * 4, (1 - local) * 4)));
            }
            else caption.text = "";
            if (!returned && a >= captionStart + CaptionSeconds * EndingLines.Length + .4f)
            {
                returned = true;
                GameFlow.MarkCleared();
                game.Audio.Stop();
                if (GameFlow.HasMenu) GameFlow.ReturnToMenu();
            }
        }
        private UnityEngine.UI.Image FullScreen(string name, Color color)
        {
            var rect = PrototypeView.MakeRect(layer, name, Vector2.zero, Vector2.zero); Stretch(rect);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
        private TMP_Text Text(string name, TMP_FontAsset font, int size, Vector2 position)
        {
            var label = PrototypeView.MakeRect(layer, name, position, new Vector2(1400, 80)).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.color = PrototypeView.InkWhite;
            return label;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; }
        private static void SetAlpha(UnityEngine.UI.Graphic g, float alpha) { var c = g.color; c.a = alpha; g.color = c; }
    }
}
