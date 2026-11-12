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
        private Vector2 home;
        private float hurtAge = 1, down;
        public float Alpha => image.color.a;

        public void Initialize(PrototypeGame owner, Sprite sprite, Vector2 bottomCentre, float height)
        {
            game = owner; rect = (RectTransform)transform;
            image = gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            // Pivot at the feet so the slump and the sink turn around the ground, not the chest.
            rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
            rect.anchoredPosition = home = bottomCentre;
        }
        public void Hurt() => hurtAge = 0;
        private void Update()
        {
            if (game == null || game.Battle == null) return;
            float dt = Time.unscaledDeltaTime, time = Time.unscaledTime;
            hurtAge += dt;
            bool gone = game.State == PrototypeState.Finale || game.State == PrototypeState.Epilogue;
            down = gone ? Mathf.Min(1, down + dt / 1.2f) : 0;
            bool stunned = game.Battle.KingStunned;
            float flinch = Mathf.Clamp01(1 - hurtAge / .25f);
            Vector2 offset = new Vector2(Mathf.Sin(time * 61) * 9 * flinch, 5 * Mathf.Sin(time * 1.3f));
            if (stunned) offset.x += 4 * Mathf.Sin(time * 9);
            rect.anchoredPosition = home + offset + Vector2.down * (down * down * rect.sizeDelta.y * .5f);
            rect.localRotation = Quaternion.Euler(0, 0, stunned ? 4 * Mathf.Sin(time * 1.7f) - 3 : 0);
            float shade = stunned ? .55f : 1;
            image.color = new Color(shade, shade * (1 - .45f * flinch), shade * (1 - .45f * flinch), 1 - down);
        }
    }
}
