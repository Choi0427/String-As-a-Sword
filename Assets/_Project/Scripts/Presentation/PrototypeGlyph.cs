using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Replaceable vector placeholders; no asset or font dependency for gameplay silhouettes.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PrototypeGlyph : UnityEngine.UI.MaskableGraphic
    {
        public enum Shape { Knight, Note, Noise, Slash }
        [SerializeField] private Shape shape;
        public void SetShape(Shape value) { shape = value; raycastTarget = false; SetVerticesDirty(); }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            if (shape == Shape.Slash)
            {
                Polygon(vh, color, new Vector2(.05f,.15f), new Vector2(.75f,.9f), new Vector2(1,.95f), new Vector2(.4f,.35f));
                return;
            }
            if (shape == Shape.Noise)
            {
                Quad(vh, 0, .43f, 1, .55f, color);
                Quad(vh, .14f, .25f, .25f, .75f, color);
                Quad(vh, .42f, .05f, .55f, .95f, color);
                Quad(vh, .74f, .2f, .85f, .8f, color);
                return;
            }
            if (shape == Shape.Note)
            {
                Quad(vh, .1f, .1f, .65f, .43f, color);
                Quad(vh, .53f, .25f, .65f, .95f, color);
                Polygon(vh, color, new Vector2(.65f,.95f), new Vector2(.96f,.74f), new Vector2(.96f,.58f), new Vector2(.65f,.78f));
                return;
            }
            Quad(vh, .10f, .04f, .92f, .16f, color);
            Quad(vh, .19f, .19f, .84f, .27f, color);
            Polygon(vh, color, new Vector2(.24f,.29f), new Vector2(.82f,.29f), new Vector2(.67f,.64f), new Vector2(.49f,.60f));
            Polygon(vh, color, new Vector2(.35f,.50f), new Vector2(.68f,.64f), new Vector2(.72f,.80f), new Vector2(.43f,.91f), new Vector2(.22f,.72f));
            Polygon(vh, color, new Vector2(.08f,.59f), new Vector2(.35f,.52f), new Vector2(.45f,.75f), new Vector2(.22f,.73f));
            Polygon(vh, color, new Vector2(.43f,.88f), new Vector2(.59f,.83f), new Vector2(.48f,1f));
            Quad(vh, .32f, .73f, .39f, .79f, new Color(.07f,.08f,.09f));
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
