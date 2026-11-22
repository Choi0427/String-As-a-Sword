using StringAsASword.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The resonance state and its ignition flash. The notes glow in NotePresenter and the J key lights in BattleScreens.
    /// </summary>
    public sealed class ResonanceHud : MonoBehaviour
    {
        private const float IgniteSeconds = .4f, FlashSeconds = .18f;
        private PrototypeGame game;
        private Image flash;
        private float igniteAge = 10;

        /// <summary>Resonance notes left; 0 when no resonance runs.</summary>
        public int ShownRemaining { get; private set; }
        public bool ShownCharged { get; private set; }
        /// <summary>The ignition's flare: 1 at ignition, 0 after <see cref="IgniteSeconds"/>.</summary>
        public float Ignite01 => Mathf.Clamp01(1 - igniteAge / IgniteSeconds);

        public void Initialize(PrototypeGame owner)
        {
            game = owner;
            var root = (RectTransform)transform;
            flash = PrototypeView.MakeRect(root, "Ignition Flash", Vector2.zero, new Vector2(1600, 900)).gameObject.AddComponent<Image>();
            flash.raycastTarget = false; flash.color = Color.clear;
        }
        public void ResetVisuals() { igniteAge = 10; Render(); }
        public void Ignite() => igniteAge = 0;
        public void Render()
        {
            if (game == null || game.Battle == null) return;
            igniteAge += Time.unscaledDeltaTime;
            var resonance = game.Battle.Resonance;
            bool playing = game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn || game.State == PrototypeState.Paused ||
                game.State == PrototypeState.Tutorial;
            ShownRemaining = playing ? resonance.Remaining(game.Battle.LastClosedNote + 1) : 0;
            ShownCharged = playing && resonance.Charged;

            flash.color = new Color(1, 1, 1, .22f * Mathf.Clamp01(1 - igniteAge / FlashSeconds));
        }
    }
}
