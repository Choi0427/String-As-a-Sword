using StringAsASword.Core;
using StringAsASword.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Everything after the boss HP is emptied: the stunned king, the finishing triplet with light breaking through the
    /// centre of the board, the centre bursting open when the song ends, the white world where the knight walks freely
    /// toward one noise, the cutscene that touching it starts, and the return to the white menu.
    /// Rules stay in BattleSession; this only reads them.
    /// </summary>
    public sealed class FinaleDirector : MonoBehaviour
    {
        public const float BurstSeconds = 2, RevealSeconds = .8f, ExitSeconds = .6f, TouchDistance = .6f;
        // The part of the burst light's radius that is solid white.
        // The solid part of the burst light, as a share of its picture's half size. The rest is halo and rays.
        private const float BloomCore = .2f;
        private const float RoamSpeed = 2.6f, RoamBounds = 4.2f, NoiseCorner = 3.2f;
        // The song plays at 0.03, so the noise stays in the same quiet range.
        private const float NoiseFar = .002f, NoiseNear = .05f, NoiseRange = 7;

        private PrototypeGame game;
        private PrototypeView view;
        private BoardWorldPresenter board;
        private RectTransform layer;
        private UnityEngine.UI.Image flash, whiteCover, white, exitWhite;
        private UnityEngine.UI.RawImage epilogueView, bloom;
        private Texture2D bloomTexture;
        private TMP_Text banner;
        private CutscenePlayer cutscene;
        private AudioSource noiseSource;
        private AudioClip noiseClip;
        private float flashAge = 10, epilogueAge, exitAge = -1;
        private int lastFinishHit = -1, checkedSession = -1;
        private Vector3 roam, noiseAt;
        private bool placed, entered, touched, returned;
        public float EpilogueAge => epilogueAge;
        /// <summary>Board-local position of the noise in the white world.</summary>
        public Vector3 NoisePosition => noiseAt;
        public float NoiseVolume => noiseSource.volume;
        public bool InWhiteWorld => entered;
        public bool CutscenePlaying => cutscene.Playing;

        public void Initialize(PrototypeGame owner, PrototypeView prototypeView)
        {
            game = owner; view = prototypeView; board = view.BoardWorld;
            layer = PrototypeView.MakeRect(view.CanvasRect, "Finale Layer", Vector2.zero, Vector2.zero);
            Stretch(layer);
            flash = FullScreen("Strike Flash", Color.white);
            whiteCover = FullScreen("White World", Color.white);
            epilogueView = PrototypeView.MakeRect(layer, "White World View", Vector2.zero, new Vector2(1080, 990)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            epilogueView.texture = board.Texture; epilogueView.raycastTarget = false;
            // The light that covers the screen grows out of the hole like a sun seen through a lens:
            // an uneven core, a soft halo, and rays of different lengths and widths at uneven angles.
            const int size = 256; const float half = (size - 1) / 2f;
            bloomTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Burst Light", wrapMode = TextureWrapMode.Clamp };
            var shape = new System.Random(5);
            float Next() => (float)shape.NextDouble();
            // The core's edge wobbles with a few slow waves around the circle.
            float[] wave = { Next() * 6.28f, Next() * 6.28f, Next() * 6.28f };
            const int rays = 17;
            var rayAngle = new float[rays]; var rayLength = new float[rays]; var rayWidth = new float[rays];
            for (int i = 0; i < rays; i++)
            {
                rayAngle[i] = (i + Next() * .9f) / rays * 6.2832f;
                bool major = i % 3 == 0;
                rayLength[i] = major ? .8f + .2f * Next() : .4f + .35f * Next();
                rayWidth[i] = major ? .035f + .025f * Next() : .012f + .02f * Next();
            }
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x - half, y - half) / half;
                float d = p.magnitude, angle = Mathf.Atan2(p.y, p.x);
                float core = BloomCore * (1 + .16f * Mathf.Sin(2 * angle + wave[0]) + .1f * Mathf.Sin(3 * angle + wave[1]) + .07f * Mathf.Sin(5 * angle + wave[2]));
                float light = Mathf.SmoothStep(1, 0, Mathf.InverseLerp(core * .8f, core * 1.5f, d));
                float halo = Mathf.SmoothStep(1, 0, d / (core * 3)); light = Mathf.Max(light, .45f * halo * halo);
                for (int i = 0; i < rays; i++)
                {
                    // A ray is widest at the core and runs out to a point.
                    float along = d * Mathf.Cos(angle - rayAngle[i]), across = Mathf.Abs(d * Mathf.Sin(angle - rayAngle[i]));
                    if (along <= 0 || along >= rayLength[i]) continue;
                    float taper = 1 - along / rayLength[i];
                    light = Mathf.Max(light, Mathf.SmoothStep(1, 0, across / (rayWidth[i] * taper + .002f)) * Mathf.Sqrt(taper));
                }
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(light));
            }
            bloomTexture.SetPixels32(pixels); bloomTexture.Apply();
            bloom = PrototypeView.MakeRect(layer, "Burst Light", Vector2.zero, Vector2.zero).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            bloom.texture = bloomTexture; bloom.raycastTarget = false;
            white = FullScreen("White", Color.white);
            var font = UiFonts.Korean(view.Art.menuFont, view.LatinFont);
            banner = Text("Banner", font, 34, new Vector2(0, 330));
            cutscene = layer.gameObject.AddComponent<CutscenePlayer>();
            cutscene.Initialize(layer, font, view.Art.endingCutscene);
            exitWhite = FullScreen("Exit White", Color.white);
            // One second of white noise, looped. Its volume is the only thing that tells how close the knight is.
            var samples = new float[44100]; var random = new System.Random(87);
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() * 2 - 1);
            noiseClip = AudioClip.Create("Ending Noise", samples.Length, 1, 44100, false); noiseClip.SetData(samples, 0);
            noiseSource = gameObject.AddComponent<AudioSource>();
            noiseSource.clip = noiseClip; noiseSource.loop = true; noiseSource.playOnAwake = false; noiseSource.volume = 0;
            game.FinaleTap += _ => view.Pulse();
            ResetVisuals();
        }
        private void ResetVisuals()
        {
            SetAlpha(flash, 0); SetAlpha(whiteCover, 0); SetAlpha(white, 0); SetAlpha(exitWhite, 0);
            epilogueView.gameObject.SetActive(false); bloom.gameObject.SetActive(false); banner.text = "";
            flashAge = 10; epilogueAge = 0; exitAge = -1; lastFinishHit = -1;
            placed = entered = touched = returned = false;
            cutscene.Hide(); noiseSource.Stop(); noiseSource.volume = 0;
            // The board puts itself back (pieces, white world) when the next session starts.
            board.FreeRoam = false; board.BreakOverride = -1; board.SetKingDown(0);
            game.Audio.SetMusicFade(1);
        }
        private void Update()
        {
            if (game == null || game.Battle == null) return;
            var settings = game.Battle.Settings;
            if (!settings.HasFinale) return;
            // A new run of the song (it starts straight in, with no count-in) puts the board back after an ending.
            if ((game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn) && game.SessionId != checkedSession)
            {
                checkedSession = game.SessionId;
                if (lastFinishHit >= 0 || epilogueAge > 0 || board.FreeRoam) ResetVisuals();
            }
            double time = game.SongTime;
            flashAge += Time.unscaledDeltaTime;
            SetAlpha(flash, .55f * Mathf.Exp(-12 * flashAge));

            if (game.State == PrototypeState.Playing && game.Battle.KingStunned)
            {
                banner.text = "KING STAGGERED"; banner.color = new Color(.94f, .93f, .88f, .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3)));
            }
            else if (game.State == PrototypeState.Finale)
            {
                banner.text = "";
                // One strike per note of the finishing triplet, then the king goes down.
                for (int hit = 0; hit < 3; hit++)
                    if (hit > lastFinishHit && time >= game.Timeline.TimeOf(settings.FinishNote + hit))
                    { lastFinishHit = hit; flashAge = 0; view.Pulse(); }
                board.SetKingDown((float)((time - game.FinaleStrikeTime) / 1.2));
                // From the last strike to the end of the song the centre cracks and white light leaks through, brighter and brighter.
                float leak = (float)((time - game.FinaleStrikeTime) / System.Math.Max(.5, game.FinaleEndTime - game.FinaleStrikeTime));
                board.BreakOverride = 2 + Mathf.Clamp01(leak);
            }
            else if (game.State == PrototypeState.Epilogue) Epilogue();
        }
        private void Epilogue()
        {
            float dt = Time.unscaledDeltaTime;
            epilogueAge += dt;
            float a = epilogueAge;
            banner.text = "";
            board.FreeRoam = true; board.BreakOverride = 3;
            if (!placed)
            {
                placed = true;
                roam = BoardWorldPresenter.CellPosition(game.Session.Position);
                // The noise waits in the corner farthest from where the knight stands.
                noiseAt = new Vector3(roam.x > 0 ? -NoiseCorner : NoiseCorner, 0, roam.z > 0 ? -NoiseCorner : NoiseCorner);
            }
            if (a < BurstSeconds)
            {
                // The centre bursts open and its light swallows the screen.
                float burst = a / BurstSeconds;
                board.SetBurst(burst);
                // The disc starts on the hole and ends with its solid part reaching every corner of the screen.
                var boardRect = (RectTransform)board.transform;
                Vector3 hole = boardRect.TransformPoint(board.ProjectToBoard(Vector3.zero, boardRect.rect.size));
                bloom.gameObject.SetActive(true);
                bloom.rectTransform.position = hole;
                // It stays small while the pieces fly, then opens fast.
                float spread = Mathf.InverseLerp(.35f, 1, burst);
                // The rays lead: they are already out while the core is small, and turn a little as it grows.
                float grow = Mathf.Lerp(spread * spread, spread * spread * spread, spread);
                bloom.rectTransform.sizeDelta = Vector2.one * (grow * 2.2f * layer.rect.size.magnitude / BloomCore);
                bloom.rectTransform.localRotation = Quaternion.Euler(0, 0, 14 * burst);
                SetAlpha(white, Mathf.InverseLerp(.9f, 1, burst));
                game.Audio.SetMusicFade(1 - burst);
            }
            else
            {
                if (!entered)
                {
                    entered = true;
                    board.EnterWhiteWorld(noiseAt);
                    epilogueView.gameObject.SetActive(true); SetAlpha(whiteCover, 1); bloom.gameObject.SetActive(false);
                    game.Audio.SetMusicFade(0);
                    noiseSource.Play();
                }
                SetAlpha(white, 1 - Mathf.Clamp01((a - BurstSeconds) / RevealSeconds));
            }
            if (!touched)
            {
                // Free movement: no grid, no beat, no time limit. WASD (or arrows), diagonals at the same speed.
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
                roam += new Vector3(move.x, 0, move.y) * RoamSpeed * dt;
                roam.x = Mathf.Clamp(roam.x, -RoamBounds, RoamBounds); roam.z = Mathf.Clamp(roam.z, -RoamBounds, RoamBounds);
                board.PlayerTransform.localPosition = new Vector3(roam.x, .08f * Mathf.Sin(Time.unscaledTime * 2.2f), roam.z);
            }
            if (entered && !touched)
            {
                // The noise looks the same from anywhere; only its sound grows as the knight comes near.
                float distance = new Vector2(roam.x - noiseAt.x, roam.z - noiseAt.z).magnitude;
                float near = 1 - Mathf.Clamp01((distance - TouchDistance) / NoiseRange);
                noiseSource.volume = Mathf.Lerp(NoiseFar, NoiseNear, near * near) * GameFlow.MusicVolume;
                if (distance <= TouchDistance) { touched = true; cutscene.Play(() => exitAge = 0); }
            }
            else if (touched) noiseSource.volume = Mathf.MoveTowards(noiseSource.volume, 0, dt * NoiseNear / CutscenePlayer.BarSeconds);
            if (exitAge >= 0)
            {
                // Cutscene over or skipped: white again, then the white menu.
                exitAge += dt;
                SetAlpha(exitWhite, Mathf.Clamp01(exitAge / ExitSeconds));
                if (!returned && exitAge >= ExitSeconds)
                {
                    returned = true;
                    GameFlow.MarkCleared();
                    noiseSource.Stop(); game.Audio.Stop();
                    if (GameFlow.HasMenu) GameFlow.ReturnToMenu();
                }
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
        private void OnDestroy() { if (noiseClip != null) Destroy(noiseClip); if (bloomTexture != null) Destroy(bloomTexture); }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; }
        private static void SetAlpha(UnityEngine.UI.Graphic g, float alpha) { var c = g.color; c.a = alpha; g.color = c; }
    }
}
