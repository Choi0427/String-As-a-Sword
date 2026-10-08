using StringAsASword.Stage;
using UnityEngine;

namespace StringAsASword.Presentation
{
    public sealed class NoisePresenter
    {
        private readonly PrototypeGlyph[] glyphs;
        public NoisePresenter(RectTransform board, int capacity)
        {
            glyphs = new PrototypeGlyph[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var rect = PrototypeView.MakeRect(board, $"Noise_{i}", Vector2.zero, new Vector2(35, 32));
                glyphs[i] = rect.gameObject.AddComponent<PrototypeGlyph>();
                glyphs[i].SetShape(PrototypeGlyph.Shape.Noise);
            }
        }
        public void Render(NoisePickupSystem noise)
        {
            for (int i = 0; i < glyphs.Length; i++)
            {
                glyphs[i].gameObject.SetActive(i < noise.Pickups.Count);
                if (i >= noise.Pickups.Count) continue;
                var cell = noise.Pickups[i].Cell;
                glyphs[i].rectTransform.anchoredPosition = new Vector2((cell.X - 3.5f) * 66, (cell.Y - 3.5f) * 66);
                glyphs[i].color = noise.GoalReached ? new Color(.6f, .7f, .7f, .35f) : new Color(.67f, 1f, .86f);
            }
        }
    }
}
