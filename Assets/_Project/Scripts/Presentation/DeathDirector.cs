using StringAsASword.Core;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The last hit: the board freezes, the song fades, everything but the knight goes dark and the board camera closes in.
    /// Then the knight tips over the way a real chess piece does: knocked, a slow lean, a breath on the edge of its base,
    /// and the fall, a bounce and a little roll. The game-over card follows. Rules stay in BattleSession; this only reads them.
    /// </summary>
    public sealed class DeathDirector : MonoBehaviour
    {
        // Seconds from the last hit.
        private const float MusicFade = .4f, DarkSeconds = .9f, ZoomSeconds = 1.4f;
        private const float KnockAt = .5f, KnockPeak = .68f, KnockSettle = 1.1f, CreepEnd = 2.6f, FallAt = 3f, LandAt = 3.45f,
            HopEnd = 3.65f, SecondHopEnd = 3.78f, RollEnd = 3.9f;
        public const float TotalSeconds = 4.7f;
        // Degrees from upright: the knock, what it leans back to, the tipping point (centre of mass over the rim), lying down.
        private const float KnockDegrees = 9, SettleDegrees = 4, TipDegrees = 30, LieDegrees = 82, Tremble = .6f,
            HopDegrees = 7, SecondHopDegrees = 2, RollDegrees = 14;
        // The knight's base radius and the point the camera and the light centre on, both board units.
        private const float BaseRadius = .3f, FocusHeight = .45f, FocusSide = .3f, CloseDistance = 6;
        // Darkness as it looks, and the light's clear radius on the canvas (start, end).
        private const float DarkAlpha = .93f, SpotStart = 1400, SpotEnd = 230, HoleShare = .15f, HaloAlpha = .08f;

        private PrototypeGame game;
        private PrototypeView view;
        private BoardWorldPresenter board;
        private RectTransform layer;
        private UnityEngine.UI.RawImage dark;
        private UnityEngine.UI.Image halo, flash;
        private Texture2D holeTexture;
        private AudioSource fallSource;
        private float age, side = 1;
        private int session = -1;
        private bool active, landed;
        private Vector3 basePosition;

        /// <summary>The close-up is on screen (the fall, then the game-over card over its darkness).</summary>
        public bool Covering => active;
        /// <summary>Seconds since the last hit.</summary>
        public float Age => age;
        /// <summary>+1: the knight falls toward the right of the board, -1: toward the left (away from the king above the middle).</summary>
        public float Side => side;

        public void Initialize(PrototypeGame owner, PrototypeView prototypeView)
        {
            game = owner; view = prototypeView; board = view.BoardWorld;
            // Under the game-over card, over the board and the strings.
            var screens = view.Screens.transform;
            layer = PrototypeView.MakeRect((RectTransform)screens.parent, "Death Layer", Vector2.zero, Vector2.zero);
            Stretch(layer);
            layer.SetSiblingIndex(screens.GetSiblingIndex());
            halo = PrototypeView.MakeRect(layer, "Spot Halo", Vector2.zero, Vector2.zero).gameObject.AddComponent<UnityEngine.UI.Image>();
            halo.sprite = PrototypeView.SoftDotSprite(); halo.color = new Color(1, 1, 1, 0); halo.raycastTarget = false;
            // Black with a soft round hole: clear inside HoleShare of the half size, fully dark by 1.8 times that.
            const int size = 512; const float half = (size - 1) / 2f;
            holeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Death Spot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x - half, y - half).magnitude / half;
                pixels[y * size + x] = new Color(0, 0, 0, Shade(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(HoleShare, HoleShare * 1.8f, d))));
            }
            holeTexture.SetPixels32(pixels); holeTexture.Apply();
            dark = PrototypeView.MakeRect(layer, "Darkness", Vector2.zero, Vector2.zero).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            dark.texture = holeTexture; dark.raycastTarget = false;
            var flashRect = PrototypeView.MakeRect(layer, "Hit Flash", Vector2.zero, Vector2.zero); Stretch(flashRect);
            flash = flashRect.gameObject.AddComponent<UnityEngine.UI.Image>(); flash.color = new Color(1, 1, 1, 0); flash.raycastTarget = false;
            fallSource = gameObject.AddComponent<AudioSource>();
            fallSource.playOnAwake = false; fallSource.spatialBlend = 0; fallSource.ignoreListenerPause = true;
            ResetVisuals();
        }
        private void ResetVisuals()
        {
            bool was = active;
            active = landed = false; age = 0;
            layer.gameObject.SetActive(false);
            fallSource.Stop();
            // The board puts the knight and its camera back when the next session starts.
            if (was) game.Audio.SetMusicFade(1);
        }
        private void Begin()
        {
            active = true; landed = false; age = 0; session = game.SessionId;
            // Where the knight stands now (a step may be in the air: it falls from the board under it).
            var at = board.PlayerTransform.localPosition;
            basePosition = new Vector3(at.x, 0, at.z);
            side = basePosition.x < -.01f ? -1 : 1;
            board.FreeRoam = true;
            layer.gameObject.SetActive(true);
        }
        private void Update()
        {
            if (game == null || game.Battle == null) return;
            bool dying = game.State == PrototypeState.Dying;
            bool card = game.State == PrototypeState.Finished && active && game.SessionId == session;
            if (!dying && !card) { if (active) ResetVisuals(); return; }
            if (dying && active && game.SessionId != session) ResetVisuals();
            if (!active) Begin();
            if (dying) age = Mathf.Min(age + Time.unscaledDeltaTime, TotalSeconds);
            Pose(age);
            if (dying && age >= TotalSeconds) game.FinishDeath();
        }
        /// <summary>Esc, Space or Enter during the fall: the knight is down at once and the game-over card shows.</summary>
        public void Skip()
        {
            if (game.State != PrototypeState.Dying) return;
            if (!active) Begin();
            landed = true; age = TotalSeconds;
            Pose(age);
            game.FinishDeath();
        }
        private void Pose(float a)
        {
            board.FreeRoam = true;
            game.Audio.SetMusicFade(1 - Mathf.Clamp01(a / MusicFade));
            Vector3 focus = basePosition + new Vector3(side * FocusSide, FocusHeight, 0);
            board.FocusCamera(focus, CloseDistance, Smooth01(a / ZoomSeconds));
            float roll = side * RollDegrees * Smooth01((a - LandAt) / (RollEnd - LandAt));
            board.SetKnightDown(basePosition, side, Tilt(a), roll, BaseRadius);
            if (!landed && a >= LandAt)
            {
                landed = true;
                board.Jolt();
                var clip = view.Art.knightFallSound;
                if (clip != null) fallSource.PlayOneShot(clip, GameFlow.MusicVolume);
            }

            // The light: the dark closes in from the screen's edge to a pool around the knight.
            float darkness = Smooth01(a / DarkSeconds);
            float radius = Mathf.Lerp(SpotStart, SpotEnd, darkness);
            var boardRect = (RectTransform)board.transform;
            Vector3 spot = boardRect.TransformPoint(board.ProjectToBoard(focus, boardRect.rect.size));
            dark.rectTransform.position = spot;
            dark.rectTransform.sizeDelta = Vector2.one * (2 * radius / HoleShare);
            dark.color = new Color(1, 1, 1, Shade(DarkAlpha * darkness));
            halo.rectTransform.position = spot;
            halo.rectTransform.sizeDelta = Vector2.one * radius * 2.6f;
            SetAlpha(halo, HaloAlpha * darkness);
            float strike = .35f * Mathf.Exp(-10 * a), land = a >= LandAt ? .12f * Mathf.Exp(-12 * (a - LandAt)) : 0;
            SetAlpha(flash, strike + land);
        }
        /// <summary>Degrees from upright at <paramref name="a"/> seconds after the hit.</summary>
        public static float Tilt(float a)
        {
            if (a < KnockAt) return 0;
            if (a < KnockPeak) return KnockDegrees * EaseOut((a - KnockAt) / (KnockPeak - KnockAt));
            if (a < KnockSettle) return Mathf.Lerp(KnockDegrees, SettleDegrees, Smooth01((a - KnockPeak) / (KnockSettle - KnockPeak)));
            // Slower and slower toward the edge of the base, as if it might still come back.
            if (a < CreepEnd) return Mathf.Lerp(SettleDegrees, TipDegrees, EaseOut((a - KnockSettle) / (CreepEnd - KnockSettle)));
            // On the edge: it trembles there, then goes.
            if (a < FallAt) return TipDegrees + Tremble * Mathf.Sin((a - CreepEnd) * 38) * Mathf.Sin(Mathf.PI * (a - CreepEnd) / (FallAt - CreepEnd));
            // The fall gathers speed, like a piece let go past its balance.
            if (a < LandAt) return Mathf.Lerp(TipDegrees, LieDegrees, Mathf.Pow((a - FallAt) / (LandAt - FallAt), 2.6f));
            // The head hits the board and hops twice, smaller the second time.
            if (a < HopEnd) return LieDegrees - HopDegrees * Mathf.Sin(Mathf.PI * (a - LandAt) / (HopEnd - LandAt));
            if (a < SecondHopEnd) return LieDegrees - SecondHopDegrees * Mathf.Sin(Mathf.PI * (a - HopEnd) / (SecondHopEnd - HopEnd));
            return LieDegrees;
        }
        // The project blends in linear space: black at alpha a leaves (1 - a) of the light, which looks like (1 - a)^(1/2.2).
        // This is the alpha that darkens by <paramref name="look"/> as the eye sees it.
        private static float Shade(float look) => 1 - Mathf.Pow(1 - Mathf.Clamp01(look), 2.2f);
        private static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t); }
        private static float Smooth01(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        private void OnDestroy() { if (holeTexture != null) Destroy(holeTexture); }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; }
        private static void SetAlpha(UnityEngine.UI.Graphic g, float alpha) { var c = g.color; c.a = alpha; g.color = c; }
    }
}
