using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>The note drawn in vector shapes (head, stem, flag) when no note sprite is assigned.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PrototypeGlyph : UnityEngine.UI.MaskableGraphic
    {
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            Quad(vh, .1f, .1f, .65f, .43f, color);
            Quad(vh, .53f, .25f, .65f, .95f, color);
            Polygon(vh, color, new Vector2(.65f,.95f), new Vector2(.96f,.74f), new Vector2(.96f,.58f), new Vector2(.65f,.78f));
        }
        private void Quad(UnityEngine.UI.VertexHelper vh, float x1, float y1, float x2, float y2, Color tint)
            => Polygon(vh, tint, new Vector2(x1,y1), new Vector2(x2,y1), new Vector2(x2,y2), new Vector2(x1,y2));
        private void Polygon(UnityEngine.UI.VertexHelper vh, Color tint, params Vector2[] points)
        {
            var rect = rectTransform.rect; int start = vh.currentVertCount;
            foreach (var point in points)
                vh.AddVert(new Vector3(rect.xMin + rect.width * point.x, rect.yMin + rect.height * point.y), tint, Vector2.zero);
            for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
