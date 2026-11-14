// Attack telegraph tile, drawn from the quad's UVs with no texture:
// a translucent fill, a glow that brightens toward the cell's border, and optional diagonal hatching.
Shader "String As a Sword/Telegraph Tile"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.28
        _GlowWidth ("Inner Glow Width (share of the cell)", Range(0.01, 0.5)) = 0.18
        _GlowAlpha ("Inner Glow Alpha", Range(0, 1)) = 0.7
        [Toggle] _Hatch ("Hatching", Float) = 0
        _HatchCount ("Hatch Lines", Float) = 7
        _HatchWidth ("Hatch Line Width (share of the spacing)", Range(0.05, 0.9)) = 0.3
        _HatchAlpha ("Hatch Alpha", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            // The board is rendered to a texture that the UI draws over the background. Keeping the target's alpha
            // (One OneMinusSrcAlpha) stops the board from turning see-through wherever a tile lies on it.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _FillAlpha, _GlowWidth, _GlowAlpha, _Hatch, _HatchCount, _HatchWidth, _HatchAlpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float edge = min(min(uv.x, 1 - uv.x), min(uv.y, 1 - uv.y));
                float glow = saturate(1 - edge / _GlowWidth);
                float alpha = _FillAlpha + _GlowAlpha * glow * glow;

                // Diagonal lines: 0 at a line's centre, 1 halfway to the next one.
                float along = (uv.x + uv.y) * _HatchCount * 0.5;
                float fromLine = abs(frac(along) - 0.5) * 2;
                float soft = fwidth(along) * 2;
                float hatch = 1 - smoothstep(_HatchWidth - soft, _HatchWidth + soft, fromLine);
                alpha += _Hatch * _HatchAlpha * hatch;

                return half4(_Color.rgb, saturate(alpha) * _Color.a);
            }
            ENDHLSL
        }
    }
}
