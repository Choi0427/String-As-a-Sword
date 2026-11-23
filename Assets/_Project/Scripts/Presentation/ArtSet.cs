using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Art slots for delivered assets. Every slot is optional: an empty slot keeps the procedural prototype visual.</summary>
    [CreateAssetMenu(menuName = "String As a Sword/Art Set", fileName = "ArtSet")]
    public sealed class ArtSet : ScriptableObject
    {
        [Header("3D models (1 unit = 1 cell, pivot at bottom centre, front faces -Z)")]
        [Tooltip("Replaces the procedural knight. Animator triggers: Move, Attack, Guard, Hit, Block.")]
        public GameObject knightPrefab;
        [Tooltip("Soft outer glow around the knight in the board view, like the one painted around the boss picture. " +
            "RGB = colour; A = strength at the knight's edge, as it would look in Photoshop or Figma (0 = no glow). Can be tuned in Play mode.")]
        public Color knightGlowColor = new Color(1, 1, 1, .65f);
        [Tooltip("How far the knight's glow spreads: its blur radius (sigma) as a share of the board view's height. " +
            "It fades out at about twice this. Can be tuned in Play mode.")]
        public float knightGlowSize = .008f;
        [Tooltip("The knight's sword in the prologue (the torn string). Pivot at the grip, blade along +Y. Empty = a white line.")]
        public GameObject swordPrefab;
        [Tooltip("Placed only when assigned. Animator triggers: Telegraph, Attack, Hurt, PhaseUp.")]
        public GameObject kingPrefab;
        [Tooltip("Board-local position of the king's pivot. Row 8's far edge is z = 4.")]
        public Vector3 kingAnchor = new Vector3(0, 0, 5.5f);
        public float kingScale = 1;
        [Tooltip("2D boss picture drawn behind the board (temporary until the king model arrives). Its lower part hides behind the board.")]
        public Sprite bossImage;
        [Tooltip("Boss picture height in canvas pixels (1600x900 reference). The width follows the sprite's aspect.")]
        public float bossImageHeight = 400;
        [Tooltip("x: sideways nudge. y: how far the picture's bottom edge sits below the board's far edge. Canvas pixels.")]
        public Vector2 bossImageSink = new Vector2(0, 110);
        [Tooltip("Replaces the procedural wave. Animator bool: Dormant (noise goal reached).")]
        public GameObject noisePrefab;
        [Tooltip("Optional: replaces every material of the noise prefab once the phase goal is reached.")]
        public Material noiseDormantMaterial;
        [Tooltip("Noise pickup picture (Art/UI/Noise/Noise.png). Used when noisePrefab is empty: it floats above its cell, facing the camera, " +
            "over a soft shadow on the cell, and greys out once the phase goal is reached.")]
        public Sprite noiseSprite;
        [Tooltip("Material for the noise sprite and its shadow, on URP's Sprite-Unlit-Default (the legacy Sprites/Default draws it white in this pipeline). Set Up Noise Sprite builds it.")]
        public Material noiseMaterial;

        [Tooltip("Replaces the procedural plinth, rim, deck and cells. It is cut into one shard per cell so the board can break. " +
            "Fit its 8x8 squares to 1-unit cells centred on the origin, playing surface at y = 0.")]
        public GameObject boardPrefab;
        [Tooltip("Cracks drawn over the whole board (Board Crack shader: _Crack, _Light, _Hole). " +
            "String As a Sword > Art > Set Up Board Cracks builds it.")]
        public Material boardCrack;
        [Tooltip("Ending cutscene, played between the letterbox bars after the knight touches the noise. Empty = a short black placeholder.")]
        public UnityEngine.Video.VideoClip endingCutscene;

        [Header("Board materials (tile top faces are UV-mapped 0..1 per cell)")]
        public Material lightTile;
        public Material darkTile;
        [Tooltip("Board plinth, deck and every side face.")]
        public Material boardEdge;
        [Tooltip("Adds a directional light for URP Lit materials. Leave off for Unlit art.")]
        public bool useLighting;
        [Tooltip("Flat ambient light for the lit board. Keep it grey: the scene's default sky ambient tints the stone blue. Alpha 0 = a neutral default.")]
        public Color ambientColor;
        [Tooltip("Colour and intensity of the board's directional light.")]
        public Color lightColor = Color.white;
        public float lightIntensity = 1;

        [Header("Board camera (0 = prototype framing)")]
        [Tooltip("Orthographic size. The prototype uses 4.6; raise it to fit a king behind row 8.")]
        public float cameraSize;
        [Tooltip("Moves the view up the screen, in board units, to make room above row 8.")]
        public float cameraLift;
        [Tooltip("Degrees above the board. 0 = the stage's elevation.")]
        public float cameraElevation;
        [Tooltip("Vertical field of view for a perspective board camera. 0 = orthographic.")]
        public float cameraFieldOfView;

        [Header("Attack tiles (cell-sized quads). Two kinds, drawn by the Telegraph Tile shader")]
        [Tooltip("Normal attack tile (guard or leave). String As a Sword > Art > Set Up Telegraph Tiles builds it from the Telegraph Tile shader.")]
        public Material normalMark;
        [Tooltip("Strong attack tile: a guard does not protect.")]
        public Material strongMark;
        public Material impactMark;

        [Header("2D UI")]
        public TMP_FontAsset font;
        public Sprite background;
        [Tooltip("Full-screen layers drawn over the background, back to front. A non-zero sway makes the layer float.")]
        public BackgroundLayer[] backgroundLayers;
        [Tooltip("The prologue's \"whole board\" set (intro review A5-B), paired with backgroundLayers index by index: it shows "
            + "until the knight tears the string, then each layer gives way to its broken battle twin. Empty = a black void.")]
        public BackgroundLayer[] prologueBackgroundLayers;
        [Tooltip("Strings and frets drawn across the screen with a gap for the board. Replaces the procedural strings; " +
            "it is scaled so the gap fits the board, and the judge lines follow the board's side edges.")]
        public Sprite laneSprite;
        [Tooltip("Sprite pixels from the left where the middle string stops at the board: x = left side, y = right side.")]
        public Vector2 laneBoardGap = new Vector2(668, 1387);
        [Tooltip("Sprite pixel rows from the top of the top (x), middle (y) and bottom (z) string.")]
        public Vector3 laneStrings = new Vector3(14, 128, 257);
        [Tooltip("Sprite pixel rows from the top where the note head's tips sit: x = top tip, just over the fourth string's " +
            "upper edge; y = bottom tip, on the bottom string's lower edge. The head straddles both lines (Figma Game Play / 오선지).")]
        public Vector2 laneNoteSpace = new Vector2(184, 260);
        [Tooltip("Board rows the strings span: x = nearest row, y = farthest row. The top string sits on the far side of y, the bottom string on the near side of x.")]
        public Vector2 laneRows = new Vector2(3, 6);
        [Tooltip("Board-local x of the board's outer side edge, where the strings end; the judge strip fills from the squares (4) " +
            "to here. The stone rim's visible top ends at about 4.19 (measured in game: 0.19 of a square), though the model's bounds reach 4.38.")]
        public float laneBoardEdge = 4.19f;
        [Tooltip("Note with its stem up and the head at the bottom. Runs of two and three fast notes are joined by one and two beams; " +
            "a hold note gets a double rail to its tail.")]
        public Sprite noteSprite;
        [Tooltip("Hold note string, stretched horizontally between the head and the tail. Draw it white; it is tinted per state.")]
        public Sprite holdBodySprite;
        [Tooltip("Hold note tail peg at the string's far end. Draw it white; it is tinted per state.")]
        public Sprite holdTailSprite;
        [Tooltip("Crown over the boss picture's head; replaces the boss HP readout. Four blades = the four HP segments (right blade first), " +
            "the three seams between them = the three noise phases (right seam first). String As a Sword > Art > Set Up Boss Crown bakes crownRegions and crownMaterial.")]
        public Sprite crown;
        [Tooltip("Baked region map for the crown: R = part id, G = height within a blade, B = position along a seam.")]
        public Texture2D crownRegions;
        [Tooltip("The crown with every blade spent (an opaque dark-grey blade with soft edges, no glow), on the same canvas as crown. " +
            "Set Up Boss Crown lines it up from Art/UI/Boss/CrownStates/4.png. A hit carves the blade from crown into this picture.")]
        public Texture2D crownSpent;
        [Tooltip("Material on the Crown UI shader.")]
        public Material crownMaterial;
        [Tooltip("Crown width as a share of the boss picture's width.")]
        public float crownWidth = .6f;
        [Tooltip("Boss picture height (0 = top) where the crown's middle seam sits: the top of the mask.")]
        public float crownSeamHeight = .1f;
        [Tooltip("Main menu background (Figma 'Main Menu' / MainMenu_Logo_BG). After a clear mainMenuWhite (Figma 'Main Menu_White') " +
            "replaces it; with mainMenuWhite empty the left side of the dark art is inverted to white in code.")]
        public Sprite mainMenuDark, mainMenuWhite;
        [Tooltip("Inspired by credit shown in the bottom-left corner of the main menu.")]
        public Sprite mainMenuCredit;
        [Tooltip("Main menu and story text (Figma: Hakgyoansim Godeunjemok B). Empty = the OS Korean font at runtime.")]
        public TMP_FontAsset menuFont;
        [UnityEngine.Serialization.FormerlySerializedAs("prologueSkipKeyIcon")]
        [Tooltip("ESC key image on the prologue and tutorial skip hints.")]
        public Sprite skipKeyIcon;
        [Tooltip("J key image on the prologue's '줄을 끊는다' prompt. Empty = a plain keycap.")]
        public Sprite promptJKeyIcon;
        [Tooltip("Key guide under the board in battle (Figma 'Game Play' / 키 가이드, exported at 2x). Empty = no guide.")]
        public Sprite keyGuide;
        [Tooltip("Pause and game-over menu items (Figma: Orbit Regular). Empty = menuFont.")]
        public TMP_FontAsset screenFont;
        [Tooltip("PAUSED / FALLEN titles (Figma: Media Sans Extended Black). Empty = screenFont.")]
        public TMP_FontAsset screenTitleFont;
        [Tooltip("HIT / BLOCK beside the knight (Chakra Petch Regular). Empty = the battle's Latin font.")]
        public TMP_FontAsset outcomeFont;        [Tooltip("Settings card art (Figma 'Game Play' / Settings, exported at 2x): the row titles with their captions, the key hints " +
            "at the bottom and the left-pointing arrow of the step buttons (flipped for the right one). Empty = plain text.")]
        public Sprite settingsVolume, settingsInputOffset, settingsMusicOffset, settingsDifficulty, settingsKeyHints, settingsArrow;
        [Tooltip("Captions under the selected difficulty button on the settings card.")]
        public Sprite settingsDifficultyEasyNote, settingsDifficultyHardNote;
        [Tooltip("Nine-sliced rounded fill and outlines for menu cards and buttons.")]
        public Sprite settingsRoundedFill, settingsRoundedPanelBorder, settingsRoundedButtonBorder;
        [Tooltip("Telegraph countdown badges move away from this box around the knight, in board-view pixels.")]
        public Rect knightBadgeBox = new Rect(-26, 5, 52, 76);
        [Tooltip("Player HP under the knight, one sprite per HP: a bar segment or a knight icon, on = left, off = lost. " +
            "The style is picked in the menu settings. An empty slot draws a plain white/grey box.")]
        public Sprite playerHpBarOn, playerHpBarOff, playerHpKnightOn, playerHpKnightOff;

        [Header("Knight sword swing")]
        [Tooltip("Camera-facing slash frames in playback order, at 24 fps. Leave empty to use the procedural arc.")]
        public Sprite[] attackSlashFrames;

        [Header("VFX prefabs (destroyed after 2 seconds)")]
        public GameObject attackVfx;
        public GameObject guardVfx;
        public GameObject hitVfx;
        public GameObject blockVfx;
        public GameObject pickupVfx;
        [Tooltip("Spawned on every cell an attack fires on; keep it light.")]
        public GameObject bossFireVfx;
        public GameObject phaseVfx;

        [Header("Sound")]
        [Tooltip("Played once when the fallen knight hits the board after the last hit (a wooden piece knocked over). Empty = silent.")]
        public AudioClip knightFallSound;

        public bool HasCrown => crown != null && crownRegions != null && crownSpent != null && crownMaterial != null;
        public bool HasBackground => background != null || (backgroundLayers != null && backgroundLayers.Length > 0);
        /// <summary>The prologue's whole-board set is delivered (at least one layer has a picture).</summary>
        public bool HasPrologueBackground => prologueBackgroundLayers != null && System.Array.Exists(prologueBackgroundLayers, l => l.sprite != null);
    }

    [System.Serializable]
    public struct BackgroundLayer
    {
        public Sprite sprite;
        [Tooltip("Float distance in canvas pixels (1600x900 reference). 0 = still.")]
        public Vector2 sway;
        [Tooltip("Seconds for one float cycle.")]
        public float period;
    }
}
