using StringAsASword.Core;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The boss as a 2D picture looming behind the board (ArtSet.bossImage), until a king model is delivered.
    /// It breathes, flinches when hit, slumps once stunned and sinks away in the finale. Presentation only.
    /// </summary>
    public sealed class BossPortrait : MonoBehaviour
    {
        private PrototypeGame game;
        private RectTransform rect;
        private UnityEngine.UI.Image image;
        // A hit fades the picture to HitAlpha at once, then back to full over HitSeconds (no white flash).
        private const float HitSeconds = .25f, HitAlpha = .2f;
        private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
        private Material flashMaterial;
        private Vector2 home;
        private float hurtAge = HitSeconds, down;
        public float Alpha => image.color.a;

        // "잡음 필요" over the boss when an attack lands without resonance (Orbit, the battle screens' font): pops in, drifts up, fades.
        private const string MissWord = "잡음 필요";
        private const float MissSeconds = .7f, MissFadeSeconds = .25f, MissPopSeconds = .1f, MissRise = 10, MissSize = 26;
        // In the darkest neck shadow, just under the chin (measured on Boss.png: darkest at 52-53% of its height).
        // The rise is small so the word stays in the shadow instead of drifting onto the face.
        private const float MissHeightShare = .5f; // up the picture from its feet, above the board's far edge
        private static readonly Color MissColor = new Color(.92f, .92f, .92f);
        private TMPro.TextMeshProUGUI missLabel;
        private float missAge = MissSeconds;

        public void Initialize(PrototypeGame owner, Sprite sprite, Vector2 bottomCentre, float height, TMPro.TMP_FontAsset missFont = null)
        {
            BuildMissLabel(missFont);
            game = owner; rect = (RectTransform)transform;
            image = gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            var flashShader = Resources.Load<Shader>("BossPortraitFlash");
            if (flashShader != null)
            {
                flashMaterial = new Material(flashShader) { hideFlags = HideFlags.DontSave };
                image.material = flashMaterial;
            }
            // Pivot at the feet so the slump and the sink turn around the ground, not the chest.
            rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
            rect.anchoredPosition = home = bottomCentre;
        }
        public void Hurt()
        {
            hurtAge = 0;
            ApplyHitEffect(1);
        }

        public void ShowMiss()
        {
            missAge = 0;
            ApplyMiss();
        }

        private void BuildMissLabel(TMPro.TMP_FontAsset font)
        {
            var missRect = PrototypeView.MakeRect(transform, "Miss", Vector2.zero, new Vector2(400, 120));
            missLabel = missRect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            if (font != null) missLabel.font = font;
            missLabel.text = MissWord;
            missLabel.fontSize = MissSize;
            missLabel.color = MissColor;
            missLabel.alignment = TMPro.TextAlignmentOptions.Center;
            missLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            missLabel.raycastTarget = false;
            missLabel.gameObject.SetActive(false);
        }

        private void ApplyMiss()
        {
            if (missLabel == null) return;
            bool visible = missAge < MissSeconds && game != null && game.State != PrototypeState.Finale && game.State != PrototypeState.Epilogue;
            if (missLabel.gameObject.activeSelf != visible) missLabel.gameObject.SetActive(visible);
            if (!visible) return;
            var missRect = missLabel.rectTransform;
            missRect.anchorMin = missRect.anchorMax = new Vector2(.5f, 0);
            float t = missAge / MissSeconds;
            missRect.anchoredPosition = new Vector2(0, rect.sizeDelta.y * MissHeightShare + MissRise * t);
            missRect.localScale = Vector3.one * (1 + .25f * Mathf.Clamp01(1 - missAge / MissPopSeconds));
            // Counter the portrait's stun tilt so the word stays level.
            missRect.localRotation = Quaternion.Inverse(rect.localRotation);
            var color = MissColor; color.a = Mathf.Clamp01((MissSeconds - missAge) / MissFadeSeconds);
            missLabel.color = color;
        }

        public void ResetHitFeedback()
        {
            hurtAge = HitSeconds;
            missAge = MissSeconds;
            ApplyHitEffect(0);
            ApplyMiss();
        }

        private void ApplyHitEffect(float strength)
        {
            float shade = game.Battle.KingStunned ? .55f : 1;
            float hit = Mathf.Lerp(1, HitAlpha, strength);
            image.color = new Color(shade, shade, shade, (1 - down) * hit);
            if (flashMaterial != null) flashMaterial.SetFloat(FlashAmount, 0);
        }

        private void OnDestroy()
        {
            if (flashMaterial != null) Destroy(flashMaterial);
        }

        // Clipped where the board's far edge is: below it the board covers the picture anyway, and when the board fades the
        // picture must not show through. A little under the edge, feathered, so the cut itself is always behind the stone.
        private const float ClipInset = 4;
        private UnityEngine.UI.RectMask2D clip;
        private RectTransform board;
        private float farEdgeOnBoard;
        /// <summary>The canvas y (centre origin) under which the picture is cut off: the board's far edge, less a few pixels.</summary>
        public float ClipLine => board != null ? board.anchoredPosition.y + farEdgeOnBoard - ClipInset : float.NegativeInfinity;
        public UnityEngine.UI.RectMask2D Clip => clip;
        /// <summary>Cuts the picture off under the far edge of <paramref name="boardRect"/> (<paramref name="farEdge"/> above its centre).</summary>
        public void ClipBehind(UnityEngine.UI.RectMask2D mask, RectTransform boardRect, float farEdge)
        {
            clip = mask; board = boardRect; farEdgeOnBoard = farEdge;
            clip.softness = new Vector2Int(0, (int)ClipInset);
            UpdateClip();
        }
        // Follows the canvas size and the board's shake.
        private void UpdateClip()
        {
            if (clip == null) return;
            float bottom = clip.rectTransform.rect.height * .5f + ClipLine;
            if (!Mathf.Approximately(clip.padding.y, bottom)) clip.padding = new Vector4(0, bottom, 0, 0);
        }
        private void LateUpdate() => UpdateClip();

        private void Update()
        {
            if (game == null || game.Battle == null) return;
            float dt = Time.unscaledDeltaTime, time = Time.unscaledTime;
            hurtAge += dt;
            if (game.State != PrototypeState.Paused) missAge += dt;
            bool gone = game.State == PrototypeState.Finale || game.State == PrototypeState.Epilogue;
            down = gone ? Mathf.Min(1, down + dt / 1.2f) : 0;
            bool stunned = game.Battle.KingStunned;
            float flinch = Mathf.Clamp01(1 - hurtAge / HitSeconds);
            Vector2 offset = new Vector2(Mathf.Sin(time * 61) * 9 * flinch, 5 * Mathf.Sin(time * 1.3f));
            if (stunned) offset.x += 4 * Mathf.Sin(time * 9);
            rect.anchoredPosition = home + offset + Vector2.down * (down * down * rect.sizeDelta.y * .5f);
            rect.localRotation = Quaternion.Euler(0, 0, stunned ? 4 * Mathf.Sin(time * 1.7f) - 3 : 0);
            ApplyHitEffect(flinch);
            ApplyMiss();
        }
    }
}
