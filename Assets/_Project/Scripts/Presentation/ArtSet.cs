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
        [Tooltip("Replaces the procedural wave. Animator bool: Dormant (noise goal reached).")]
        public GameObject noisePrefab;
        [Tooltip("Optional: replaces every material of the noise prefab once the phase goal is reached.")]
        public Material noiseDormantMaterial;

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

        [Header("Telegraph marks (cell-sized quads; duplicate Art/Materials/ArtCutout.mat and set the texture)")]
        public Material eitherMark;
        public Material guardMark;
        public Material dodgeMark;
        public Material impactMark;

        [Header("2D UI")]
        public TMP_FontAsset font;
        public Sprite background;
        public Sprite noteSprite;
        [Tooltip("Boss HP crown icons. Shown only when all three are assigned.")]
        public Sprite crownFull, crownLocked, crownBroken;
        [Tooltip("Reserved for the main menu (dark before the first clear, white after).")]
        public Sprite mainMenuDark, mainMenuWhite;
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
    }
}
