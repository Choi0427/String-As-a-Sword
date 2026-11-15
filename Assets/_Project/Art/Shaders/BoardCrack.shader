// Cracks over the whole chessboard, drawn from one board-sized quad's UVs with no texture:
// dark cracks that spread as _Crack rises, then white light that runs along them from the centre outward (_Light).
// The centre itself is real geometry (fragments over a white floor), so _Hole hides this overlay there.
// _Mode draws the light that goes with it from the same shader: 1 a soft glow, 2 rays from the centre, 3 plain light.
Shader "String As a Sword/Board Crack"
{
    Properties
    {
        _Crack ("Cracks (0.5 = first break, 1 = second)", Range(0, 1)) = 0
        _Light ("Light along the cracks, from the centre out (modes 1-2: brightness)", Range(0, 1)) = 0
        _Hole ("Hide the overlay over the centre fragments", Range(0, 1)) = 0
        _CrackColor ("Crack Color", Color) = (0.02, 0.022, 0.028, 1)
        _LightColor ("Light Color", Color) = (1, 1, 1, 1)
        _HoleRadius ("Radius of the hidden centre (cells)", Float) = 2.7
        _Mode ("0 cracks, 1 glow, 2 rays, 3 plain light", Float) = 0
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
                half4 _CrackColor, _LightColor;
                half _Crack, _Light, _Hole, _HoleRadius, _Mode;
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
            // x: distance to the nearest cell border, yz: vector to the nearest seed.
            float3 Voronoi(float2 x)
            {
                float2 n = floor(x), f = frac(x), nearCell = 0, nearSeed = 0;
                float best = 8;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j), r = g + Hash2(n + g) - f;
                    float d = dot(r, r);
                    if (d < best) { best = d; nearSeed = r; nearCell = g; }
                }
                best = 8;
                for (int v = -2; v <= 2; v++) for (int u = -2; u <= 2; u++)
                {
                    float2 g = nearCell + float2(u, v), r = g + Hash2(n + g) - f;
                    if (dot(nearSeed - r, nearSeed - r) > 0.00001) best = min(best, dot(0.5 * (nearSeed + r), normalize(r - nearSeed)));
                }
                return float3(best, nearSeed);
            }
            float Line(float distance, float width)
            {
                float soft = fwidth(distance) + 0.004;
                return 1 - smoothstep(width, width + soft, distance);
            }

            half4 Frag(Varyings input) : SV_Target
            {
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
                float2 q = p + (float2(Noise(p * 1.7 + 3.1), Noise(p * 1.7 + 9.4)) - 0.5) * 0.35;
                float3 coarse = Voronoi(q * 0.55 + 11.3);
                float3 fine = Voronoi(q * 1.45 + 4.7);

                // Cracks reach further from the centre, patch by patch, as _Crack rises. Fine ones join in the second half.
                float reach = Noise(p * 0.45 + 20) * 0.75 + r / 5.66 * 0.45;
                float showCoarse = smoothstep(-0.08, 0.02, _Crack * 1.25 - reach);
                float showFine = smoothstep(-0.08, 0.02, (_Crack - 0.5) * 1.05 - reach);
                float crack = max(showCoarse * Line(coarse.x, 0.007 + 0.012 * _Crack), showFine * 0.6 * Line(fine.x, 0.012));

                // Light: the large cracks turn white from the centre outward, each with a soft halo.
                float front = _Light * 7.2 - (r + Noise(p * 0.8 + 40) * 1.2);
                float lit = smoothstep(0, 1.2, front) * step(0.001, _Light);
                float glow = lit * saturate(Line(coarse.x, 0.008 + 0.006 * _Light) + 0.35 * (1 - smoothstep(0, 0.12, coarse.x)));

                float alpha = saturate(max(crack * 0.92, glow));
                // The fragments in the centre move; a flat drawing over them would hang in the air.
                alpha *= 1 - _Hole * step(r, _HoleRadius);
                return half4(lerp(_CrackColor.rgb, _LightColor.rgb, glow), alpha);
            }
            ENDHLSL
        }
    }
}
