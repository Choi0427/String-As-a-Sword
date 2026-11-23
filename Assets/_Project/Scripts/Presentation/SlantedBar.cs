using UnityEngine;
using UnityEngine.UI;

namespace StringAsASword.Presentation
{
    /// <summary>A horizontal UI image with independently sloped ends.</summary>
    public sealed class SlantedBar : Image
    {
        private Vector2 slants;

        public void SetSpan(float bottomStart, float topStart, float bottomEnd, float topEnd, float centerY, float height)
        {
            float start = (bottomStart + topStart) * .5f, end = (bottomEnd + topEnd) * .5f;
            rectTransform.anchoredPosition = new Vector2((start + end) * .5f, centerY);
            rectTransform.sizeDelta = new Vector2(Mathf.Abs(end - start), height);
            // The left lane runs from right to left. Store slopes in mesh order on either lane.
            var next = new Vector2(topStart - bottomStart, topEnd - bottomEnd);
            if (start > end) next = new Vector2(next.y, next.x);
            if (next == slants) return;
            slants = next;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            base.OnPopulateMesh(mesh);
            var rect = GetPixelAdjustedRect();
            UIVertex vertex = default;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                var p = vertex.position;
                float across = Mathf.InverseLerp(rect.xMin, rect.xMax, p.x);
                float up = Mathf.InverseLerp(rect.yMin, rect.yMax, p.y);
                vertex.position.x += (up - .5f) * Mathf.Lerp(slants.x, slants.y, across);
                mesh.SetUIVertex(vertex, i);
            }
        }
    }
}
