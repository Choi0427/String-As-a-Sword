// Soft outer glow around the knight in the board view (driven by KnightGlow.cs, which explains the steps).
// Pass 0 is drawn by the board camera on a quad that rides on the knight; passes 1 and 2 only run from KnightGlow's
// command buffer (their LightMode tags are ones URP never draws).
// It lives in a Resources folder so player builds include it and Shader.Find finds it.
Shader "Hidden/String As a Sword/Knight Glow"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Set by KnightGlow for every camera, just before it draws.
        TEXTURE2D(_KnightGlowMask); SAMPLER(sampler_KnightGlowMask);
        TEXTURE2D(_KnightGlowBlur); SAMPLER(sampler_KnightGlowBlur);
        float4 _KnightGlowParams;   // x: 1 in the board camera, 0 in any other; y: strength; z: exponent on the alpha
        float4 _KnightGlowTint;     // linear RGB
        // Per draw (property blocks).
        float4x4 _KnightGlowMVP;
        TEXTURE2D(_KnightGlowSource); SAMPLER(sampler_KnightGlowSource);
        float4 _KnightGlowStep;     // xy: source UV step between taps; z: sigma in taps
        float4 _KnightGlowTarget;   // zw: 1 / target size in pixels

        struct Varyings { float4 positionCS : SV_POSITION; };
        ENDHLSL

        // 0: the blurred silhouette added to the board picture, only outside the knight (Photoshop's Outer Glow).
        Pass
        {
            Name "Composite"
            // Keeping the target's alpha (One OneMinusSrcAlpha) as the attack tiles do: the board view is see-through.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // The quad's corners are the view's corners in clip space; where the quad sits does not matter.
                output.positionCS = float4(input.positionOS.xy, 0.5, 1);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The mask was drawn from this camera's view at this target's size, so pixels line up one to one.
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                half blur = SAMPLE_TEXTURE2D(_KnightGlowBlur, sampler_KnightGlowBlur, uv).r;
                half inside = SAMPLE_TEXTURE2D(_KnightGlowMask, sampler_KnightGlowMask, uv).r;
                // A blurred edge is 0.5 on the outline itself: doubled, the glow starts at full strength there.
                half glow = saturate(saturate(blur * 2) * (1 - inside) * _KnightGlowParams.y);
                half alpha = pow(glow, _KnightGlowParams.z) * _KnightGlowParams.x;
                return half4(_KnightGlowTint.rgb, alpha);
            }
            ENDHLSL
        }

        // 1: the knight's silhouette in white, from the board camera's view.
        Pass
        {
            Name "Mask"
            Tags { "LightMode" = "KnightGlowMask" }
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = mul(_KnightGlowMVP, float4(input.positionOS.xyz, 1));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target { return 1; }
            ENDHLSL
        }

        // 2: one direction of a Gaussian blur, over one triangle that covers the target.
        Pass
        {
            Name "Blur"
            Tags { "LightMode" = "KnightGlowBlur" }
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            // Taps either side of the centre; KnightGlow.BlurTaps must match.
            #define TAPS 12

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                float2 corner = float2((id << 1) & 2, id & 2);
                output.positionCS = float4(corner * 2 - 1, 0.5, 1);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy * _KnightGlowTarget.zw;
                float sigma = max(_KnightGlowStep.z, 0.001);
                float sum = 0, total = 0;
                UNITY_UNROLL
                for (int i = -TAPS; i <= TAPS; i++)
                {
                    float weight = exp(-0.5 * i * i / (sigma * sigma));
                    sum += weight * SAMPLE_TEXTURE2D_LOD(_KnightGlowSource, sampler_KnightGlowSource, uv + _KnightGlowStep.xy * i, 0).r;
                    total += weight;
                }
                return sum / total;
            }
            ENDHLSL
        }
    }
}
