using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>The judge line drawn as the four projected corners of a strip on the board's rim.</summary>
    internal sealed class JudgeRimImage : UnityEngine.UI.Image
    {
        private Vector2 bottomLeft, topLeft, topRight, bottomRight;

        public static JudgeRimImage Create(RectTransform parent, Vector2 bottomLeft, Vector2 topLeft,
            Vector2 topRight, Vector2 bottomRight)
        {
            Vector2 min = Vector2.Min(Vector2.Min(bottomLeft, topLeft), Vector2.Min(topRight, bottomRight));
            Vector2 max = Vector2.Max(Vector2.Max(bottomLeft, topLeft), Vector2.Max(topRight, bottomRight));
            Vector2 center = (min + max) * .5f;
            var rect = PrototypeView.MakeRect(parent, "JudgeLine", center, max - min);
            var image = rect.gameObject.AddComponent<JudgeRimImage>();
            image.raycastTarget = false;
            image.bottomLeft = bottomLeft - center;
            image.topLeft = topLeft - center;
            image.topRight = topRight - center;
            image.bottomRight = bottomRight - center;
            return image;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            mesh.AddVert(bottomLeft, color, Vector2.zero);
            mesh.AddVert(topLeft, color, Vector2.up);
            mesh.AddVert(topRight, color, Vector2.one);
            mesh.AddVert(bottomRight, color, Vector2.right);
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(0, 2, 3);
        }
    }
}
