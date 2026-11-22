using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// A soft outer glow around the knight in the board view, like the one painted around the boss picture
    /// (ArtSet.knightGlowColor and knightGlowSize, read every frame so they can be tuned in Play mode).
    /// Just before the board camera draws, the knight's silhouette is drawn into a mask from the camera's own view and
    /// blurred at half size. A quad that rides on the knight then draws the blur into the board picture, only outside the
    /// silhouette, as Photoshop's Outer Glow does. Since the glow ends up in the board texture, every view of that texture
    /// (battle, death, epilogue) shows it. Other cameras that see the knight (the prologue's) draw no glow.
    /// </summary>
    public sealed class KnightGlow : System.IDisposable
    {
        public const string ShaderName = "Hidden/String As a Sword/Knight Glow";
        private const int MaskPass = 1, BlurPass = 2;
        // Taps either side of the centre in the shader's blur (TAPS there); wider glows space them out.
        private const int BlurTaps = 12;
        private static readonly int MaskId = Shader.PropertyToID("_KnightGlowMask"), BlurId = Shader.PropertyToID("_KnightGlowBlur"),
            ParamsId = Shader.PropertyToID("_KnightGlowParams"), TintId = Shader.PropertyToID("_KnightGlowTint"),
            MvpId = Shader.PropertyToID("_KnightGlowMVP"), SourceId = Shader.PropertyToID("_KnightGlowSource"),
            StepId = Shader.PropertyToID("_KnightGlowStep"), TargetId = Shader.PropertyToID("_KnightGlowTarget");

        private sealed class Part
        {
            public Renderer Renderer; public MeshFilter Filter; public SkinnedMeshRenderer Skin; public Mesh Posed;
            public MaterialPropertyBlock Block;
        }

        private readonly Camera camera;
        private readonly ArtSet art;
        private readonly Material material;
        private readonly List<Part> parts = new List<Part>();
        private readonly CommandBuffer commands = new CommandBuffer { name = "Knight Glow" };
        private readonly MaterialPropertyBlock across = new MaterialPropertyBlock(), down = new MaterialPropertyBlock();
        private readonly GameObject overlay;
        private readonly Mesh quad;
        private RenderTexture mask, blurAcross, blurDown;

        /// <param name="knight">The knight's root: the quad that draws the glow hangs under it, so it shows, hides and changes
        /// layer (death isolation) with the knight.</param>
        /// <param name="renderers">The knight's own renderers, whose silhouette glows (not the sword swing or the shield).</param>
        public KnightGlow(Camera camera, Transform knight, IEnumerable<Renderer> renderers, ArtSet art)
        {
            this.camera = camera; this.art = art;
            var shader = Shader.Find(ShaderName);
            // Without the shader (not imported yet) the knight simply has no glow.
            if (shader == null || !shader.isSupported) return;
            material = new Material(shader) { name = "Knight Glow", renderQueue = (int)RenderQueue.Transparent + 1 };
            foreach (var renderer in renderers)
            {
                var skin = renderer as SkinnedMeshRenderer;
                var filter = skin == null && renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
                if (skin == null && filter == null) continue;
                parts.Add(new Part { Renderer = renderer, Filter = filter, Skin = skin, Block = new MaterialPropertyBlock(),
                    Posed = skin != null ? new Mesh { name = "Knight Glow Pose" } : null });
            }
            // The quad's corners are the view's corners in clip space (the shader ignores where it is). Its bounds only keep it
            // from being culled while the knight is in view, and out of cameras that look elsewhere.
            quad = new Mesh { name = "Knight Glow Quad" };
            quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.bounds = new Bounds(new Vector3(0, .6f, 0), Vector3.one * 4);
            overlay = new GameObject("Knight Glow", typeof(MeshFilter), typeof(MeshRenderer));
            overlay.transform.SetParent(knight, false);
            overlay.layer = knight.gameObject.layer;
            overlay.GetComponent<MeshFilter>().sharedMesh = quad;
            var view = overlay.GetComponent<MeshRenderer>();
            view.sharedMaterial = material;
            view.shadowCastingMode = ShadowCastingMode.Off; view.receiveShadows = false;
            view.lightProbeUsage = LightProbeUsage.Off; view.reflectionProbeUsage = ReflectionProbeUsage.Off;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera current)
        {
            commands.Clear();
            if (current == camera && ShouldDraw(current)) Record(current);
            // Any other camera that sees the quad (the prologue's) draws it fully clear.
            else commands.SetGlobalVector(ParamsId, Vector4.zero);
            context.ExecuteCommandBuffer(commands);
            commands.Clear();
        }

        private bool ShouldDraw(Camera current)
        {
            if (overlay == null || !overlay.activeInHierarchy || art == null || art.knightGlowColor.a <= 0) return false;
            if ((current.cullingMask & (1 << overlay.layer)) == 0) return false;
            foreach (var part in parts) if (Shows(part)) return true;
            return false;
        }
        private static bool Shows(Part part) => part.Renderer != null && part.Renderer.enabled && part.Renderer.gameObject.activeInHierarchy;

        private void Record(Camera current)
        {
            var target = current.targetTexture;
            int width = target != null ? target.width : current.pixelWidth, height = target != null ? target.height : current.pixelHeight;
            EnsureTargets(width, height);

            // 1. The silhouette, from the camera's view and projected the way a camera draws into a texture, so the mask's
            // pixels line up with the board picture's.
            var viewProjection = GL.GetGPUProjectionMatrix(current.projectionMatrix, true) * current.worldToCameraMatrix;
            commands.SetRenderTarget(mask);
            commands.ClearRenderTarget(false, true, Color.clear);
            foreach (var part in parts)
            {
                if (!Shows(part)) continue;
                Mesh mesh; Matrix4x4 toWorld;
                if (part.Skin != null)
                {
                    part.Skin.BakeMesh(part.Posed, true);
                    mesh = part.Posed; toWorld = Matrix4x4.TRS(part.Skin.transform.position, part.Skin.transform.rotation, Vector3.one);
                }
                else { mesh = part.Filter.sharedMesh; toWorld = part.Renderer.transform.localToWorldMatrix; }
                if (mesh == null) continue;
                part.Block.SetMatrix(MvpId, viewProjection * toWorld);
                for (int sub = 0; sub < mesh.subMeshCount; sub++) commands.DrawMesh(mesh, Matrix4x4.identity, material, sub, MaskPass, part.Block);
            }

            // 2. Blurred at half size, across then down. The spread is a share of the view's height, so it scales with the screen.
            float sigma = Mathf.Max(.5f, art.knightGlowSize * height) * .5f;
            float spacing = Mathf.Max(1, sigma * 3 / BlurTaps);
            var size = new Vector4(blurAcross.width, blurAcross.height, 1f / blurAcross.width, 1f / blurAcross.height);
            across.SetTexture(SourceId, mask); across.SetVector(TargetId, size);
            across.SetVector(StepId, new Vector4(spacing / blurAcross.width, 0, sigma / spacing, 0));
            down.SetTexture(SourceId, blurAcross); down.SetVector(TargetId, size);
            down.SetVector(StepId, new Vector4(0, spacing / blurAcross.height, sigma / spacing, 0));
            commands.SetRenderTarget(blurAcross);
            commands.DrawProcedural(Matrix4x4.identity, material, BlurPass, MeshTopology.Triangles, 3, 1, across);
            commands.SetRenderTarget(blurDown);
            commands.DrawProcedural(Matrix4x4.identity, material, BlurPass, MeshTopology.Triangles, 3, 1, down);

            // 3. What the quad needs when the camera draws it.
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var color = art.knightGlowColor;
            var tint = linear ? color.linear : color;
            commands.SetGlobalTexture(MaskId, mask);
            commands.SetGlobalTexture(BlurId, blurDown);
            commands.SetGlobalVector(TintId, new Vector4(tint.r, tint.g, tint.b, 1));
            // Photoshop and Figma blend in sRGB. In Linear colour space the alpha is raised to 2.2 so a faint glow stays as
            // faint as it looks there (the same correction LinearGlowCorrection applies to the boss picture).
            commands.SetGlobalVector(ParamsId, new Vector4(1, color.a, linear ? 2.2f : 1, 0));
        }

        private void EnsureTargets(int width, int height)
        {
            if (mask != null && mask.width == width && mask.height == height) return;
            ReleaseTargets();
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            var description = new RenderTextureDescriptor(width, height, format, 0) { sRGB = false, msaaSamples = 4 };
            // The knight in the board picture is anti-aliased 4x. A mask that is too keeps the glow off the knight's soft edge.
            description.msaaSamples = Mathf.Clamp(SystemInfo.GetRenderTextureSupportedMSAASampleCount(description), 1, 4);
            mask = Target(description, "Knight Glow Mask");
            description.width = Mathf.Max(1, width / 2); description.height = Mathf.Max(1, height / 2); description.msaaSamples = 1;
            blurAcross = Target(description, "Knight Glow Blur Across");
            blurDown = Target(description, "Knight Glow Blur Down");
        }
        private static RenderTexture Target(RenderTextureDescriptor description, string name)
        {
            var target = new RenderTexture(description) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            target.Create();
            return target;
        }
        private void ReleaseTargets()
        {
            foreach (var target in new[] { mask, blurAcross, blurDown })
                if (target != null) { target.Release(); Object.Destroy(target); }
            mask = blurAcross = blurDown = null;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            ReleaseTargets();
            commands.Release();
            foreach (var part in parts) if (part.Posed != null) Object.Destroy(part.Posed);
            if (overlay != null) Object.Destroy(overlay);
            if (quad != null) Object.Destroy(quad);
            if (material != null) Object.Destroy(material);
        }
    }
}
