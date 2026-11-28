using System.Collections.Generic;
using StringAsASword.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The crown over the boss picture's head, which replaces the boss HP readout. Each hit carves the active blade from
    /// the tip down (rightmost blade first) into the artist's spent blade (ArtSet.crownSpent: an opaque dark-grey
    /// blade, no glow), with a short white flash, a small shake and a few falling chips. A seam fades out and crackles
    /// with static once the blade before it is spent. The ring and the hooks stay lit to the end, as in the art's last
    /// state.
    /// Greys and the knight's black only. A child of the boss picture, so it breathes, flinches and sinks with it.
    /// </summary>
    public sealed class BossCrownView : MonoBehaviour
    {
        private const float FlashSeconds = .25f, GlowOutSeconds = .6f, BurstSeconds = .3f;
        private static readonly int BladeId = Shader.PropertyToID("_Blade"), BladeGlowId = Shader.PropertyToID("_BladeGlow"),
            BladeFlashId = Shader.PropertyToID("_BladeFlash"), SeamFillId = Shader.PropertyToID("_SeamFill"),
            SeamNoiseId = Shader.PropertyToID("_SeamNoise"), BodyId = Shader.PropertyToID("_Body"), GlowBoostId = Shader.PropertyToID("_GlowBoost"),
            RegionId = Shader.PropertyToID("_RegionTex"), SpentId = Shader.PropertyToID("_SpentTex");

        private PrototypeGame game;
        private BossPortrait portrait;
        private RectTransform rect;
        private Image image;
        private Material material;
        private Vector2 home;
        private readonly float[] blade = new float[4], bladeTarget = new float[4], bladeGlow = new float[4], bladeFlash = new float[4];
        // Last frame's targets, to spot a new cut or a restart.
        private readonly float[] bladeBefore = new float[4], seamBefore = new float[3];
        private readonly float[] seam = new float[3], seamTarget = new float[3], seamBurst = new float[3];
        private readonly bool[] crackling = new bool[3];
        private float shakeAge = 1, shakeSeconds = 1, shakeSize;
        private readonly List<Chip> chips = new List<Chip>();

        public float Blade(int index) => blade[index];
        public float BladeGlow(int index) => bladeGlow[index];
        public float Seam(int index) => seam[index];
        public Material Material => material;

        private sealed class Chip { public RectTransform rect; public Image image; public Vector2 velocity; public float age; }

        public void Initialize(PrototypeGame owner, BossPortrait boss, ArtSet art)
        {
            game = owner; portrait = boss; rect = (RectTransform)transform;
            var picture = (RectTransform)boss.transform;
            var sprite = art.crown;
            float width = picture.sizeDelta.x * art.crownWidth, height = width * sprite.rect.height / sprite.rect.width;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            // The middle seam rests on the top of the mask; the blades rise around the hood.
            float middleSeam = CrownLayout.SeamPoint(1, .5f).y;
            home = new Vector2(0, picture.sizeDelta.y * (1 - art.crownSeamHeight) + (middleSeam - .5f) * height);
            image = gameObject.AddComponent<Image>();
            image.sprite = sprite; image.raycastTarget = false;
            material = new Material(art.crownMaterial) { name = "Crown (instance)", hideFlags = HideFlags.DontSave };
            material.SetTexture(RegionId, art.crownRegions); material.SetTexture(SpentId, art.crownSpent);
            // The ring and the hooks stay lit even when the king falls: the art's last state (CrownStates/4.png) keeps them.
            material.SetVector(BodyId, new Vector4(0, 1, 0, 0));
            image.material = material;
            Snap();
        }
        /// <summary>Back to the battle's start: no chips, every part at its current value without animation.</summary>
        public void ResetVisuals()
        {
            foreach (var chip in chips) Destroy(chip.rect.gameObject);
            chips.Clear();
            Snap();
        }

        private void Snap()
        {
            if (game == null || game.Battle == null) return;
            ReadTargets();
            for (int i = 0; i < 4; i++) { blade[i] = bladeTarget[i]; bladeGlow[i] = bladeTarget[i] >= 1 ? 0 : 1; bladeFlash[i] = 0; }
            for (int k = 0; k < 3; k++)
            {
                seam[k] = seamTarget[k]; seamBurst[k] = 0;
                crackling[k] = CrownState.SeamCrackles(k, game.Phases.PhaseId, false);
            }
            shakeAge = shakeSeconds;
            Apply(Time.unscaledTime);
        }
        // The crown is boss HP only: a seam goes black once the blade before it is spent.
        private static float SeamTarget(int seam, int phase) => CrownState.SeamFill(seam, phase, 0, 1);
        private void ReadTargets()
        {
            for (int i = 0; i < 4; i++) bladeTarget[i] = CrownState.BladeDepletion(game.Health, i);
            int phase = game.Phases.PhaseId;
            for (int k = 0; k < 3; k++)
                seamTarget[k] = SeamTarget(k, phase);
        }

        private void Update()
        {
            if (game == null || game.Battle == null) return;
            float dt = Time.unscaledDeltaTime, time = Time.unscaledTime;
            System.Array.Copy(bladeTarget, bladeBefore, bladeTarget.Length);
            System.Array.Copy(seamTarget, seamBefore, seamTarget.Length);
            ReadTargets();
            for (int i = 0; i < 4; i++)
            {
                float target = bladeTarget[i];
                if (target < bladeBefore[i] - 1e-4f) { Snap(); return; } // a restart refilled the crown
                if (target > bladeBefore[i] + 1e-4f) Cut(i, target >= 1);
                blade[i] += (target - blade[i]) * (1 - Mathf.Exp(-18 * dt));
                bladeGlow[i] = Mathf.MoveTowards(bladeGlow[i], target >= 1 ? 0 : 1, dt / GlowOutSeconds);
                bladeFlash[i] = Mathf.Max(0, bladeFlash[i] - dt / FlashSeconds);
            }
            int phase = game.Phases.PhaseId;
            for (int k = 0; k < 3; k++)
            {
                if (seamTarget[k] < seamBefore[k] - 1e-4f) seam[k] = seamTarget[k];
                seam[k] += (seamTarget[k] - seam[k]) * (1 - Mathf.Exp(-10 * dt));
                bool crackle = CrownState.SeamCrackles(k, phase, false);
                if (crackle && !crackling[k]) seamBurst[k] = 1;
                crackling[k] = crackle;
                seamBurst[k] = Mathf.Max(0, seamBurst[k] - dt / BurstSeconds);
            }
            shakeAge += dt;
            float shake = shakeSize * Mathf.Clamp01(1 - shakeAge / shakeSeconds);
            rect.anchoredPosition = home + Random.insideUnitCircle * shake;
            image.color = new Color(1, 1, 1, portrait != null ? portrait.Alpha : 1);
            UpdateChips(dt);
            Apply(time);
        }
        private void Apply(float time)
        {
            material.SetVector(BladeId, new Vector4(blade[0], blade[1], blade[2], blade[3]));
            material.SetVector(BladeGlowId, new Vector4(bladeGlow[0], bladeGlow[1], bladeGlow[2], bladeGlow[3]));
            material.SetVector(BladeFlashId, new Vector4(bladeFlash[0], bladeFlash[1], bladeFlash[2], bladeFlash[3]));
            material.SetVector(SeamFillId, new Vector4(seam[0], seam[1], seam[2], 0));
            var noise = new Vector4();
            for (int k = 0; k < 3; k++) noise[k] = crackling[k] ? .55f + .45f * seamBurst[k] : 0;
            material.SetVector(SeamNoiseId, noise);
            // The glow breathes with the boss picture (BossPortrait sways at the same rate).
            material.SetFloat(GlowBoostId, .3f + .08f * Mathf.Sin(time * 1.3f));
        }
        // A hit took a slice off blade i: flash it, shake the crown and drop a few chips from the cut.
        private void Cut(int index, bool spent)
        {
            bladeFlash[index] = 1;
            Shake(spent ? 5 : 2.5f, spent ? .28f : .12f);
            Vector2 front = Local(CrownLayout.BladeFront(index, bladeTarget[index]));
            for (int i = 0, count = spent ? 6 : 3; i < count; i++)
            {
                float size = Random.Range(2.5f, 4.5f);
                var chip = PrototypeView.MakeRect(rect, "Chip", front + new Vector2(Random.Range(-4f, 4f), 0), new Vector2(size, size));
                chip.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 90f));
                var picture = chip.gameObject.AddComponent<Image>(); picture.raycastTarget = false;
                chips.Add(new Chip { rect = chip, image = picture, velocity = new Vector2(Random.Range(-45f, 45f), Random.Range(20f, 70f)) });
            }
        }
        private void Shake(float size, float seconds)
        {
            if (shakeAge < shakeSeconds && shakeSize > size) return;
            shakeSize = size; shakeSeconds = seconds; shakeAge = 0;
        }
        private Vector2 Local(Vector2 uv) => new Vector2((uv.x - .5f) * rect.sizeDelta.x, (.5f - uv.y) * rect.sizeDelta.y);

        private void UpdateChips(float dt)
        {
            for (int c = chips.Count - 1; c >= 0; c--)
            {
                var chip = chips[c];
                chip.age += dt;
                chip.velocity.y -= 520 * dt;
                chip.rect.anchoredPosition += chip.velocity * dt;
                float t = chip.age / .5f;
                // White as it breaks off, then the knight's black, fading out as it falls.
                float grey = Mathf.Lerp(1, .09f, Mathf.Clamp01(t * 2.5f));
                chip.image.color = new Color(grey, grey, grey, Mathf.Clamp01(1 - t) * (portrait != null ? portrait.Alpha : 1));
                if (t < 1) continue;
                Destroy(chip.rect.gameObject);
                chips.RemoveAt(c);
            }
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
