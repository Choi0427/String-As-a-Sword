using StringAsASword.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Resonance on screen, with no sound yet. A charged string glows faintly along both lanes; ignition flashes the
    /// lanes and the screen; a segmented gauge beside each judge line counts the resonance notes left and blinks on the
    /// last two; an attack without resonance leaves a grey streak across the boss. Presentation only.
    /// </summary>
    public sealed class ResonanceHud : MonoBehaviour
    {
        private const float IgniteSeconds = .4f, FlashSeconds = .18f, GhostSeconds = .35f, TickWidth = 6, GaugeOffset = 18;
        private const int WarningNotes = 2;
        private PrototypeGame game;
        private readonly Image[] bands = new Image[2];
        private Image[][] ticks;
        private Image flash, ghost;
        private float igniteAge = 10, ghostAge = 10;
        private Vector3 ghostFrom, ghostTo;

        /// <summary>Notes the gauge shows as left; 0 when no resonance runs.</summary>
        public int ShownRemaining { get; private set; }
        public bool ShownCharged { get; private set; }
        public bool GhostVisible => ghost.gameObject.activeSelf;

        /// <param name="judgeX">Distance of the judge lines from the canvas centre.</param>
        /// <param name="tilt">The judge lines' lean in radians (mirrored on the left).</param>
        public void Initialize(PrototypeGame owner, Sprite glow, float judgeX, float judgeY, float length, float tilt, float screenEdge)
        {
            game = owner;
            var root = (RectTransform)transform;
            int count = Mathf.Max(1, game.Battle.Resonance.Length);
            ticks = new Image[2][];
            for (int side = -1; side <= 1; side += 2)
            {
                int index = (side + 1) / 2;
                // A soft band over the strings from the judge line out to the screen edge (the glow sprite fades across its width).
                float laneLength = screenEdge - judgeX;
                var band = PrototypeView.MakeRect(root, "Resonance Band", new Vector2(side * (judgeX + screenEdge) * .5f, judgeY), new Vector2(length * 1.15f, laneLength));
                band.localRotation = Quaternion.Euler(0, 0, 90);
                bands[index] = band.gameObject.AddComponent<Image>();
                bands[index].sprite = glow; bands[index].raycastTarget = false; bands[index].color = Color.clear;
                // The gauge lies along the judge line, on its outer side; segment 0 is the first to go out (the top).
                var gauge = PrototypeView.MakeRect(root, "Resonance Gauge", new Vector2(side * judgeX, judgeY), Vector2.zero);
                gauge.localRotation = Quaternion.Euler(0, 0, side * tilt * Mathf.Rad2Deg);
                ticks[index] = new Image[count];
                float step = length / count;
                for (int i = 0; i < count; i++)
                {
                    var tick = PrototypeView.MakeRect(gauge, "Tick", new Vector2(side * GaugeOffset, length * .5f - step * (i + .5f)), new Vector2(TickWidth, step - 4));
                    ticks[index][i] = tick.gameObject.AddComponent<Image>();
                    ticks[index][i].raycastTarget = false; ticks[index][i].color = Color.clear;
                }
            }
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
            float dt = Time.unscaledDeltaTime, time = Time.unscaledTime;
            igniteAge += dt; ghostAge += dt;
            var resonance = game.Battle.Resonance;
            bool playing = game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn || game.State == PrototypeState.Paused ||
                game.State == PrototypeState.Tutorial;
            int remaining = playing ? resonance.Remaining(game.Battle.LastClosedNote + 1) : 0;
            bool charged = playing && resonance.Charged;
            ShownRemaining = remaining; ShownCharged = charged;

            float ignite = Mathf.Clamp01(1 - igniteAge / IgniteSeconds);
            float steady = remaining > 0 ? .42f : charged ? .14f + .05f * Mathf.Sin(time * 3.2f) : 0;
            float bandAlpha = Mathf.Max(steady, remaining > 0 ? Mathf.Lerp(steady, .95f, ignite) : 0);
            foreach (var band in bands) band.color = new Color(1, 1, 1, bandAlpha);
            flash.color = new Color(1, 1, 1, .22f * Mathf.Clamp01(1 - igniteAge / FlashSeconds));

            // Lit segments are the notes left; the last two blink as a warning. A charge shows the whole gauge dimly.
            bool blink = remaining > 0 && remaining <= WarningNotes && Mathf.Repeat(time * 5, 1) < .5f;
            foreach (var side in ticks)
                for (int i = 0; i < side.Length; i++)
                {
                    bool lit = remaining > 0 && i >= side.Length - remaining;
                    float alpha = remaining > 0 ? (lit ? (blink ? .35f : 1) : .12f) : charged ? .28f : 0;
                    side[i].color = new Color(1, 1, 1, alpha);
                }

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
