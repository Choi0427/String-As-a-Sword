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

        private float stretch = 1;
        /// <summary>
        /// Length along the board's rim, 1 = as built. The near (bottom) and far (top) ends move apart along the rim's own
        /// axis, so the strip grows in line with the board edge and each end keeps its width.
        /// </summary>
        public float Stretch
        {
            get => stretch;
            set { if (Mathf.Approximately(stretch, value)) return; stretch = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Vector2 near = (bottomLeft + bottomRight) * .5f, far = (topLeft + topRight) * .5f, middle = (near + far) * .5f;
            Vector2 nearShift = (near - middle) * (stretch - 1), farShift = (far - middle) * (stretch - 1);
            mesh.AddVert(bottomLeft + nearShift, color, Vector2.zero);
            mesh.AddVert(topLeft + farShift, color, Vector2.up);
            mesh.AddVert(topRight + farShift, color, Vector2.one);
            mesh.AddVert(bottomRight + nearShift, color, Vector2.right);
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(0, 2, 3);
        }
    }
}
