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
        [Tooltip("Strings and frets drawn across the screen with a gap for the board. Replaces the procedural strings; " +
            "it is scaled so the gap fits the board, and the judge lines follow the board's side edges.")]
        public Sprite laneSprite;
        [Tooltip("Sprite pixels from the left where the middle string stops at the board: x = left side, y = right side.")]
        public Vector2 laneBoardGap = new Vector2(668, 1387);
        [Tooltip("Sprite pixel rows from the top of the top (x), middle (y) and bottom (z) string.")]
        public Vector3 laneStrings = new Vector3(14, 128, 257);
        [Tooltip("Sprite pixel row from the top of the string the notes ride. 190 is the fourth string, on the line between rows 4 and 3.")]
        public float laneNoteString = 190;
        [Tooltip("Board rows the strings span: x = nearest row, y = farthest row. The top string sits on the far side of y, the bottom string on the near side of x.")]
        public Vector2 laneRows = new Vector2(3, 6);
        [Tooltip("Board-local x of the board's outer side edge, where the strings end. The stone board's rim is at 4.38.")]
        public float laneBoardEdge = 4.38f;
        [Tooltip("Note with its stem up and the head at the bottom. Runs of two and three fast notes are joined by one and two beams; " +
            "a hold note gets a double rail to its tail.")]
        public Sprite noteSprite;
        [Tooltip("Hold note string, stretched horizontally between the head and the tail. Draw it white; it is tinted per state.")]
        public Sprite holdBodySprite;
        [Tooltip("Hold note tail peg at the string's far end. Draw it white; it is tinted per state.")]
        public Sprite holdTailSprite;
        [Tooltip("Boss HP crown icons. Shown only when all three are assigned.")]
        public Sprite crownFull, crownLocked, crownBroken;
        [Tooltip("Main menu background (Figma 'Main Menu' / MainMenu_Logo_BG). After a clear its left side is inverted to white in code; " +
            "assign mainMenuWhite only to replace that with a hand-made white version.")]
        public Sprite mainMenuDark, mainMenuWhite;
        [Tooltip("Main menu and story text (Figma: Hakgyoansim Godeunjemok B). Empty = the OS Korean font at runtime.")]
        public TMP_FontAsset menuFont;
        [Tooltip("Telegraph countdown badges move away from this box around the knight, in board-view pixels.")]
        public Rect knightBadgeBox = new Rect(-26, 5, 52, 76);

        [Header("VFX prefabs (destroyed after 2 seconds)")]
        public GameObject attackVfx;
        public GameObject guardVfx;
        public GameObject hitVfx;
        public GameObject blockVfx;
        public GameObject pickupVfx;
        [Tooltip("Spawned on every cell an attack fires on; keep it light.")]
        public GameObject bossFireVfx;
        public GameObject phaseVfx;

        public bool HasCrown => crownFull != null && crownLocked != null && crownBroken != null;
        public bool HasBackground => background != null || (backgroundLayers != null && backgroundLayers.Length > 0);
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
