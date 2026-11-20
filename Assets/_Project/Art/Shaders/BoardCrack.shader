// Cracks over the whole chessboard, read from a crack map grown in the editor (BoardCrackBaker) on one board-sized quad:
// each crack grows from where it starts as _Crack passes the time baked into it, as a dark core with a light chipped lip
// and flakes beside it, so it shows on light and dark tiles. Then white light runs along the main ones from the centre (_Light).
// The centre itself is real geometry (fragments over a white floor), so _Hole hides this overlay there.
// _Mode draws the light that goes with it from the same shader: 1 a soft glow, 2 rays from the centre, 3 plain light,
// 4 blades of light standing in the cracks (their own mesh: the normal is how far an upper vertex is lifted).
Shader "String As a Sword/Board Crack"
{
    Properties
    {
        _Crack ("Cracks (0.5 = first break, 1 = second)", Range(0, 1)) = 0
        [NoScaleOffset] _CrackMap ("Crack Map (R core, G time, B lip, A rank)", 2D) = "black" {}
        _LipColor ("Lip Color (A strength)", Color) = (0.66, 0.67, 0.66, 0.8)
        _Light ("Light along the cracks, from the centre out (modes 1-2: brightness)", Range(0, 1)) = 0
        _Hole ("Hide the overlay over the centre fragments", Range(0, 1)) = 0
        _CrackColor ("Crack Color", Color) = (0.02, 0.022, 0.028, 1)
        _LightColor ("Light Color", Color) = (1, 1, 1, 1)
        _HoleRadius ("Radius of the hidden centre (cells)", Float) = 2.7
        _Mode ("0 cracks, 1 glow, 2 rays, 3 plain light, 4 blades", Float) = 0
        _Reach ("Blades: height (cells)", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            // Same alpha rule as the telegraph tiles: the board view is a texture drawn over the background,
            // so the target's alpha must not drop where a crack lies on the board.
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CrackColor, _LightColor, _LipColor;
                half _Crack, _Light, _Hole, _HoleRadius, _Mode, _Reach;
            CBUFFER_END
            TEXTURE2D(_CrackMap); SAMPLER(sampler_CrackMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 trait : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 blade : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = input.positionOS.xyz;
                output.blade = 0;
                if (_Mode > 3.5)
                {
                    // Blades near the centre come up first (trait.x is the distance from it, 0..1).
                    float up = saturate((_Light * 1.7 - input.trait.x) / 0.7);
                    position += input.normalOS * (_Reach * up);
                    output.blade = float3(input.trait, up);
                }
                output.positionCS = TransformObjectToHClip(position);
                output.uv = input.uv;
                return output;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash2(i).x, Hash2(i + float2(1, 0)).x, f.x),
                            lerp(Hash2(i + float2(0, 1)).x, Hash2(i + float2(1, 1)).x, f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Mode > 3.5)
                {
                    // Bright at the crack, gone at the top and at both ends, with streaks that drift so it is never still.
                    float rise = pow(saturate(1 - input.uv.y), 1.7);
                    float ends = smoothstep(0, 0.15, input.uv.x) * smoothstep(1, 0.85, input.uv.x);
                    float streaks = 0.5 + 0.5 * Noise(float2(input.uv.x * 5 + input.blade.y * 37, _Time.y * 0.9 + input.blade.y * 11));
                    float2 view = input.positionCS.xy / _ScreenParams.xy;
                    float inside = smoothstep(0, 0.14, min(min(view.x, 1 - view.x), min(view.y, 1 - view.y)));
                    float strength = rise * ends * streaks * (0.45 + 0.6 * input.blade.y) * input.blade.z;
                    return half4(_LightColor.rgb, saturate(strength) * _LightColor.a * inside);
                }
                if (_Mode > 2.5) return half4(_LightColor.rgb, 1);
                if (_Mode > 0.5)
                {
                    // Light is plain white laid over the picture, so the same blend as the cracks serves.
                    float2 c = input.uv - 0.5;
                    float d = length(c) * 2;
                    // Noise read around a circle: it depends on the direction only, with no seam.
                    float2 dir = c / max(length(c), 0.0001);
                    // The glow reaches further in some directions than in others, with a few sharp spikes: not a disc.
                    float reach = 0.5 + 0.8 * (Noise(dir * 2.3 + 3.1) * 0.65 + Noise(dir * 6 + 11.7) * 0.35);
                    float spike = pow(saturate(Noise(dir * 13 + 29.4) * 1.3), 6);
                    float v = pow(saturate(1 - d / reach), 2.2) * 1.5 + spike * pow(saturate(1 - d), 1.3) * 1.2;
                    if (_Mode > 1.5)
                    {
                        float streak = Noise(dir * 3 + 5.3) * 0.6 + Noise(dir * 9 + 17.1) * 0.4;
                        streak = pow(saturate(streak * 1.35), 4) * 3;
                        v = streak * pow(saturate(1 - d), 1.5) + smoothstep(0.3, 0, d);
                    }
                    // Fades out toward the edge of the board's view, which would cut it off in a straight line.
                    float2 screen = input.positionCS.xy / _ScreenParams.xy;
                    float edge = min(min(screen.x, 1 - screen.x), min(screen.y, 1 - screen.y));
                    return half4(_LightColor.rgb, saturate(v * _Light) * smoothstep(0, 0.14, edge));
                }
                float2 p = (input.uv - 0.5) * 8; // board units: one cell is 1
                float r = length(p);
                half4 map = SAMPLE_TEXTURE2D(_CrackMap, sampler_CrackMap, input.uv);
                // A pixel shows once the crack front has passed it; the front's own edge is soft, so the tip of a
                // growing crack fades in instead of stepping.
                float grown = saturate((_Crack - map.g) / 0.02);
                float core = map.r * grown;
                float lip = map.b * grown * _LipColor.a;
                // The dark core lies over the light lip.
                float alpha = core + lip * (1 - core);
                float3 color = (_CrackColor.rgb * core + _LipColor.rgb * lip * (1 - core)) / max(alpha, 0.0001);

                // Light: the main cracks turn white from the centre outward, each with a soft halo (a coarse mip of the map).
                float front = _Light * 7.2 - (r + Noise(p * 0.8 + 40) * 1.2);
                float lit = smoothstep(0, 1.2, front) * step(0.001, _Light);
                float main = saturate((map.a - 0.45) * 4);
                float halo = SAMPLE_TEXTURE2D_BIAS(_CrackMap, sampler_CrackMap, input.uv, 3).a;
                float glow = lit * saturate(core * main + 1.6 * halo);

                color = lerp(color, _LightColor.rgb, glow / max(max(alpha, glow), 0.0001));
                alpha = saturate(max(alpha * 0.95, glow));
                // The fragments in the centre move; a flat drawing over them would hang in the air.
                alpha *= 1 - _Hole * step(r, _HoleRadius);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
