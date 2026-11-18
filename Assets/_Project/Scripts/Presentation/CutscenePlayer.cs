using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// A cutscene in cinema proportions: black bars slide in slowly from the top and bottom, then the clip plays between them.
    /// Without a clip it holds a short black placeholder. Space, Enter or Esc skips it.
    /// </summary>
    public sealed class CutscenePlayer : MonoBehaviour
    {
        public const float BarSeconds = 1.2f, FadeSeconds = .5f, PlaceholderSeconds = 2, Aspect = 2.39f;
        private RectTransform layer, top, bottom;
        private Image backdrop;
        private RawImage screen;
        private TMP_Text hint;
        private VideoPlayer video;
        private RenderTexture target;
        private VideoClip clip;
        private System.Action finished;
        private float age;
        private bool videoStarted, videoEnded;
        public bool Playing { get; private set; }
        /// <summary>0 = bars off screen, 1 = bars in place.</summary>
        public float Bars { get; private set; }

        public void Initialize(RectTransform parent, TMP_FontAsset font, VideoClip cutscene)
        {
            layer = parent; clip = cutscene;
            backdrop = Fill("Cutscene Backdrop", Color.black);
            var holder = PrototypeView.MakeRect(layer, "Cutscene Screen", Vector2.zero, Vector2.zero);
            // The clip fills the width; anything taller than the opening is covered by the bars.
            holder.anchorMin = new Vector2(0, .5f); holder.anchorMax = new Vector2(1, .5f);
            screen = holder.gameObject.AddComponent<RawImage>(); screen.raycastTarget = false;
            holder.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            top = Bar("Letterbox Top", 1); bottom = Bar("Letterbox Bottom", 0);
            hint = PrototypeView.MakeRect(bottom, "Skip Hint", Vector2.zero, new Vector2(400, 40)).gameObject.AddComponent<TextMeshProUGUI>();
            var hintRect = hint.rectTransform; hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1, .5f);
            hintRect.anchoredPosition = new Vector2(-40, 0);
            if (font != null) hint.font = font;
            hint.fontSize = 20; hint.alignment = TextAlignmentOptions.Right; hint.raycastTarget = false;
            hint.text = "SPACE 건너뛰기"; hint.color = new Color(1, 1, 1, .55f);
            Hide();
        }
        public void Play(System.Action onFinished)
        {
            if (Playing) return;
            Playing = true; finished = onFinished; age = 0; videoStarted = videoEnded = false;
            top.gameObject.SetActive(true); bottom.gameObject.SetActive(true); backdrop.gameObject.SetActive(true);
            if (clip != null)
            {
                if (video == null)
                {
                    video = gameObject.AddComponent<VideoPlayer>();
                    video.playOnAwake = false; video.isLooping = false; video.renderMode = VideoRenderMode.RenderTexture;
                    video.loopPointReached += _ => videoEnded = true;
                    video.errorReceived += (_, message) => { Debug.LogWarning("Cutscene could not play: " + message); videoEnded = true; };
                    target = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "Cutscene" };
                    video.targetTexture = target; screen.texture = target;
                    screen.GetComponent<AspectRatioFitter>().aspectRatio = (float)clip.width / clip.height;
                }
                video.clip = clip; video.Prepare();
            }
            Layout();
        }
        /// <summary>Removes every cutscene element from the screen (a restart).</summary>
        public void Hide()
        {
            Playing = false; Bars = 0; finished = null;
            if (video != null) video.Stop();
            top.gameObject.SetActive(false); bottom.gameObject.SetActive(false);
            backdrop.gameObject.SetActive(false); screen.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (!Playing) return;
            age += Time.unscaledDeltaTime;
            Layout();
            var keyboard = Keyboard.current;
            bool skip = keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame);
            bool over;
            if (clip == null) over = age >= BarSeconds + FadeSeconds + PlaceholderSeconds;
            else
            {
                if (!videoStarted && age >= BarSeconds) { videoStarted = true; screen.gameObject.SetActive(true); video.Play(); }
                over = videoEnded;
            }
            if (skip || over) Finish();
        }
        // The last picture stays up; the caller covers it and moves on.
        private void Finish()
        {
            Playing = false;
            if (video != null) video.Pause();
            var done = finished; finished = null; done?.Invoke();
        }
        private void Layout()
        {
            Bars = Mathf.SmoothStep(0, 1, age / BarSeconds);
            var size = layer.rect.size;
            float bar = Mathf.Max(0, (size.y - size.x / Aspect) / 2);
            // A screen already wider than 2.39:1 would get no bars at all; keep a thin pair so the change still reads.
            bar = Mathf.Max(bar, size.y * .06f);
            top.sizeDelta = bottom.sizeDelta = new Vector2(0, bar);
            top.anchoredPosition = new Vector2(0, bar * (1 - Bars));
            bottom.anchoredPosition = new Vector2(0, -bar * (1 - Bars));
            var black = backdrop.color; black.a = Mathf.Clamp01((age - BarSeconds) / FadeSeconds); backdrop.color = black;
        }
        private RectTransform Bar(string name, float edge)
        {
            var rect = PrototypeView.MakeRect(layer, name, Vector2.zero, Vector2.zero);
            rect.anchorMin = new Vector2(0, edge); rect.anchorMax = new Vector2(1, edge); rect.pivot = new Vector2(.5f, edge);
            var image = rect.gameObject.AddComponent<Image>(); image.color = Color.black; image.raycastTarget = false;
            return rect;
        }
        private Image Fill(string name, Color color)
        {
            var rect = PrototypeView.MakeRect(layer, name, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
        private void OnDestroy()
        {
            if (target != null) { target.Release(); Destroy(target); }
        }
    }
}
