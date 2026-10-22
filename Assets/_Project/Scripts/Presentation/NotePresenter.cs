using System;
using StringAsASword.Core;
using UnityEngine;

namespace StringAsASword.Presentation
{
    public sealed class NotePresenter
    {
        private readonly PrototypeGlyph[,] notes = new PrototypeGlyph[2, 9];
        private readonly float targetX;
        private readonly float spawnX;
        public NotePresenter(RectTransform root, float targetX = 310, float spawnX = 715)
        {
            this.targetX = targetX; this.spawnX = spawnX;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 9; i++)
                {
                    var rect = PrototypeView.MakeRect(root, $"Note_{side}_{i}", Vector2.zero, new Vector2(23, 31));
                    notes[side, i] = rect.gameObject.AddComponent<PrototypeGlyph>();
                    notes[side, i].SetShape(PrototypeGlyph.Shape.Note);
                }
        }
        public void Render(PrototypeGame game)
        {
            bool active = game.State == PrototypeState.Playing || game.State == PrototypeState.CountIn;
            double time = game.SongTime + game.Song.VisualOffset;
            double approach = game.Song.NoteApproachBeats * game.Timeline.SecondsPerBeat;
            int first = Math.Max(0, (int)Math.Floor(time / game.Timeline.SecondsPerBeat));
            for (int i = 0; i < 9; i++)
            {
                int beat = first + i;
                double remaining = game.Timeline.TimeOf(beat) - time;
                bool visible = active && beat < game.Song.NoteCount && remaining >= -.14 && remaining <= approach && beat != game.Session.Gate.LastConsumedBeat;
                float distance = Mathf.LerpUnclamped(targetX, spawnX, (float)(remaining / approach));
                for (int side = 0; side < 2; side++)
                {
                    var glyph = notes[side, i];
                    glyph.gameObject.SetActive(visible);
                    if (!visible) continue;
                    glyph.rectTransform.anchoredPosition = new Vector2(side == 0 ? -distance : distance, 60);
                    glyph.color = beat % 4 == 0 ? PrototypeView.InkWhite : new Color(.60f, .65f, .69f);
                }
            }
        }
    }
}
