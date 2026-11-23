using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Soft glows painted into art (white at low opacity) blend far brighter in this Linear colour space project than in
    /// Photoshop or Figma: white at 10% over black shows as 89/255 instead of 26/255. For the art listed here the alpha is
    /// taken from sRGB to linear on import, so the glow blends in game the way it looks in the art file. Opaque and fully
    /// clear pixels are untouched, and the PNG on disk stays as delivered: a re-exported file is corrected on its next import.
    /// (The same correction the code applies to Figma's translucent fills, see UiKit.CardFill.)
    /// To add art: put its path in <see cref="Paths"/>, then right-click the file in Unity > Reimport.
    /// </summary>
    public sealed class LinearGlowCorrection : AssetPostprocessor
    {
        private static readonly string[] Paths =
        {
            "Assets/_Project/Art/UI/Boss/Boss.png", // ArtSet.bossImage: battle, tutorial and prologue
        };
        // Raise when the correction changes, so Unity imports the listed art again.
        public override uint GetVersion() => 1;

        // 4x4 ordered dither, so the faintest edge of a glow fades out smoothly instead of in steps.
        private static readonly float[] Dither =
        {
            0 / 16f, 8 / 16f, 2 / 16f, 10 / 16f,
            12 / 16f, 4 / 16f, 14 / 16f, 6 / 16f,
            3 / 16f, 11 / 16f, 1 / 16f, 9 / 16f,
            15 / 16f, 7 / 16f, 13 / 16f, 5 / 16f,
        };

        private void OnPostprocessTexture(Texture2D texture)
        {
            if (System.Array.IndexOf(Paths, assetPath) < 0) return;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int width = Mathf.Max(1, texture.width >> mip);
                var pixels = texture.GetPixels32(mip);
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte a = pixels[i].a;
                    if (a == 0 || a == 255) continue;
                    float linear = Mathf.GammaToLinearSpace(a / 255f) * 255f;
                    int x = i % width, y = i / width;
                    pixels[i].a = (byte)Mathf.Clamp(Mathf.FloorToInt(linear + Dither[(y & 3) * 4 + (x & 3)]), 0, 255);
                }
                texture.SetPixels32(pixels, mip);
            }
        }
    }
}
