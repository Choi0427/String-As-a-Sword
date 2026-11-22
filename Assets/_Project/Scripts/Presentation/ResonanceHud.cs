using StringAsASword.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Resonance on screen, with no sound yet. The notes themselves glow (NotePresenter) and the J key lights
    /// (BattleScreens); here, ignition flashes the screen and an attack without resonance leaves a grey streak across
    /// the boss. Presentation only.
    /// </summary>
    public sealed class ResonanceHud : MonoBehaviour
    {
        private const float IgniteSeconds = .4f, FlashSeconds = .18f, GhostSeconds = .35f;
        private PrototypeGame game;
        private Image flash, ghost;
        private float igniteAge = 10, ghostAge = 10;
        private Vector3 ghostFrom, ghostTo;

        /// <summary>Resonance notes left; 0 when no resonance runs.</summary>
        public int ShownRemaining { get; private set; }
        public bool ShownCharged { get; private set; }
        public bool GhostVisible => ghost.gameObject.activeSelf;
        /// <summary>The ignition's flare: 1 at ignition, 0 after <see cref="IgniteSeconds"/>.</summary>
        public float Ignite01 => Mathf.Clamp01(1 - igniteAge / IgniteSeconds);

        public void Initialize(PrototypeGame owner)
        {
            game = owner;
            var root = (RectTransform)transform;
            flash = PrototypeView.MakeRect(root, "Ignition Flash", Vector2.zero, new Vector2(1600, 900)).gameObject.AddComponent<Image>();
            flash.raycastTarget = false; flash.color = Color.clear;
            ghost = PrototypeView.MakeRect(root, "Ghost Strike", Vector2.zero, new Vector2(240, 5)).gameObject.AddComponent<Image>();
            ghost.raycastTarget = false; ghost.rectTransform.localRotation = Quaternion.Euler(0, 0, -28);
            ghost.gameObject.SetActive(false);
        }
        public void ResetVisuals() { igniteAge = ghostAge = 10; ghost.gameObject.SetActive(false); Render(); }
        public void Ignite() => igniteAge = 0;
        /// <summary>The blade passes through the boss: a grey streak crosses this world point and fades.</summary>
        public void Ghost(Vector3 at)
        {
            Vector3 reach = transform.TransformVector(new Vector3(70, -37, 0));
            ghostFrom = at - reach; ghostTo = at + reach; ghostAge = 0;
            ghost.gameObject.SetActive(true);
        }
        public void Render()
        {
            if (game == null || game.Battle == null) return;
            float dt = Time.unscaledDeltaTime;
            igniteAge += dt; ghostAge += dt;
            var resonance = game.Battle.Resonance;
            bool playing = game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn || game.State == PrototypeState.Paused ||
                game.State == PrototypeState.Tutorial;
            ShownRemaining = playing ? resonance.Remaining(game.Battle.LastClosedNote + 1) : 0;
            ShownCharged = playing && resonance.Charged;

            flash.color = new Color(1, 1, 1, .22f * Mathf.Clamp01(1 - igniteAge / FlashSeconds));

            if (!ghost.gameObject.activeSelf) return;
            float t = ghostAge / GhostSeconds;
            if (t >= 1) { ghost.gameObject.SetActive(false); return; }
            ghost.rectTransform.position = Vector3.Lerp(ghostFrom, ghostTo, t * t * (3 - 2 * t));
            // White as it meets the boss, grey as it leaves: it cut nothing.
            float grey = Mathf.Lerp(1, .45f, t);
            ghost.color = new Color(grey, grey, grey, .8f * (1 - t));
        }
    }
}
