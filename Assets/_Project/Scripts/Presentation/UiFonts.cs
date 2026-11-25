using TMPro;

namespace StringAsASword.Presentation
{
    /// <summary>Korean-capable text. A delivered font wins; otherwise the OS Korean font is loaded at runtime
    /// (nothing copied into the project, so no font file is redistributed).</summary>
    public static class UiFonts
    {
        private static TMP_FontAsset system;
        private static readonly string[] Families = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR" };

        public static TMP_FontAsset Korean(TMP_FontAsset preferred, TMP_FontAsset latinFallback = null)
        {
            if (preferred != null) return preferred;
            if (system != null) return system;
            foreach (string family in Families)
            {
                try { system = TMP_FontAsset.CreateFontAsset(family, "Regular"); }
                catch (System.Exception) { system = null; }
                if (system != null) { system.name = family + " (OS, dynamic)"; return system; }
            }
            return latinFallback;
        }
    }
}
