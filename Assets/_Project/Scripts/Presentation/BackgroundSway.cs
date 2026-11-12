using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Slow floating drift for a full-screen background layer. Presentation only.</summary>
    public sealed class BackgroundSway : MonoBehaviour
    {
        private RectTransform rect;
        private Vector2 sway;
        private float period, phase;

        public void Set(Vector2 distance, float seconds, int index)
        {
            rect = (RectTransform)transform; sway = distance;
            period = Mathf.Max(.5f, seconds > 0 ? seconds : 8);
            phase = index * 2.17f;
        }
        private void Update()
        {
            float t = Time.unscaledTime * Mathf.PI * 2 / period + phase;
            // y runs at a different rate than x, so the drift traces a loose loop instead of a line.
            rect.anchoredPosition = new Vector2(sway.x * Mathf.Sin(t), sway.y * Mathf.Sin(t * .77f + 1.3f));
        }
    }
}
