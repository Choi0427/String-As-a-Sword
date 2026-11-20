// The boss crown on the UI canvas (Art/UI/Boss/Crown.png). The picture's opaque lines are the crown, its soft alpha is the glow.
// A baked region map (CrownLayout.Bake) says which part every pixel belongs to:
//   blades (boss HP): carved from the tip down by _Blade into the artist's spent picture (_SpentTex: black inside,
//                     thin white outline, no glow), with a short white flash on the fresh cut (_BladeFlash);
//   seams:            fade out from the right end by _SeamFill and crackle with static by _SeamNoise;
//   body (ring, hooks): goes black with _Body.x (the crown keeps it at 0: the art's last state keeps them lit).
// Only white, greys and the knight's black are ever drawn.
Shader "String As a Sword/Crown UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Crown", 2D) = "white" {}
        [NoScaleOffset] _RegionTex ("Region Map", 2D) = "black" {}
        [NoScaleOffset] _SpentTex ("Crown With Every Blade Spent", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _KnightBlack ("Knight Black", Color) = (0.09, 0.086, 0.078, 1)
        _RimColor ("Dark Outline", Color) = (0.23, 0.23, 0.23, 1)
        _RimAlpha ("Dark Outline Alpha", Range(0, 1)) = 0.8
        _GlowBoost ("Glow", Range(0, 2)) = 1
        _Blade ("Blade Depletion (left to right)", Vector) = (0, 0, 0, 0)
        _BladeGlow ("Blade Glow", Vector) = (1, 1, 1, 1)
        _BladeFlash ("Blade Flash", Vector) = (0, 0, 0, 0)
        _SeamFill ("Seam Fill (right, middle, left)", Vector) = (0, 0, 0, 0)
        _SeamNoise ("Seam Static (right, middle, left)", Vector) = (0, 0, 0, 0)
        _Body ("Body (x dark, y glow)", Vector) = (0, 1, 0, 0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _RegionTex, _SpentTex;
            fixed4 _Color, _KnightBlack, _RimColor;
            half _RimAlpha, _GlowBoost;
            float4 _Blade, _BladeGlow, _BladeFlash, _SeamFill, _SeamNoise, _Body;

            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float Pick(float4 values, int index) { return index == 0 ? values.x : index == 1 ? values.y : index == 2 ? values.z : values.w; }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float4 region = tex2D(_RegionTex, uv);
                int id = (int)floor(region.r * 255.0 / 32.0 + 0.5);
                float time = _Time.y;

                float dark = 0, glow = 1, flash = 0, crackle = 0, fade = 0;
                if (id >= 1 && id <= 4)
                {
                    int blade = id - 1;
                    float gone = Pick(_Blade, blade);
                    // From the tip (0) down to the base (1); overshoot so a spent blade is spent to its last pixel.
                    float front = gone > 0 ? gone * 1.03 : -1;
                    float carved = saturate((front - region.g) / 0.02 + 0.5);
                    // The carved part shows the artist's spent blade (black inside, thin white outline, no glow);
                    // the rest is the whole crown, whose glow breathes.
                    fixed4 whole = tex2D(_MainTex, uv);
                    float wholeCore = smoothstep(0.80, 0.95, whole.a);
                    whole.a = saturate(wholeCore + whole.a * (1 - wholeCore) * _GlowBoost);
                    fixed4 spent = tex2D(_SpentTex, uv);
                    fixed4 bladeColour = lerp(whole, spent, carved);
                    // The freshly cut slice just above the front flashes white before it settles.
                    float cut = Pick(_BladeFlash, blade) * carved * saturate(1 - (front - region.g) / 0.12);
                    bladeColour.rgb = lerp(bladeColour.rgb, float3(1, 1, 1), saturate(cut));
                    return fixed4(bladeColour.rgb * input.color.rgb, bladeColour.a * input.color.a);
                }
                else if (id >= 5)
                {
                    int seam = id - 5;
                    float fill = Pick(_SeamFill, seam);
                    float front = fill > 0 ? fill * 1.03 : -1;
                    // A spent seam just fades out: painting it black with an outline left smudges over the boss's hood,
                    // and the art keeps its seams unchanged. Its static still shows as grey specks.
                    fade = saturate((front - region.b) / 0.03 + 0.5);
                    crackle = Pick(_SeamNoise, seam);
                    if (crackle > 0)
                    {
                        // Static: now and then a few rows of the seam slip sideways, like a torn signal.
                        float tick = floor(time * 14);
                        float burst = step(0.62 - 0.3 * saturate(crackle - 0.6), Hash(float2(tick, seam)));
                        float row = floor(uv.y * 90);
                        uv.x += (Hash(float2(row, tick)) - 0.5) * 0.014 * crackle * burst;
                    }
                }
                else
                {
                    dark = _Body.x;
                    glow = _Body.y;
                }

                fixed4 picture = tex2D(_MainTex, uv);
                float a = picture.a;
                float halo = a * (1 - smoothstep(0.80, 0.95, a));
                float core = smoothstep(0.80, 0.95, a) * (1 - fade);
                // A thin dark-grey edge keeps a blackened part readable against the dark background.
                float rim = smoothstep(0.45, 0.8, a) * (1 - smoothstep(0.80, 0.95, a)) * dark * _RimAlpha;
                float haloAlpha = halo * glow * (1 - dark) * (1 - fade) * _GlowBoost;

                float3 coreColour = lerp(float3(1, 1, 1), _KnightBlack.rgb, dark);
                coreColour = lerp(coreColour, float3(1, 1, 1), saturate(flash));
                if (crackle > 0)
                {
                    float speck = Hash(floor(input.uv * float2(420, 220)) + floor(time * 18));
                    float grey = lerp(0.18, 0.62, Hash(floor(input.uv * float2(170, 90)) + floor(time * 9) + 3.1));
                    coreColour = lerp(coreColour, grey.xxx, step(0.55, speck) * saturate(crackle) * 0.7);
                    // Faint grey specks in the air around the seam.
                    haloAlpha += halo * step(0.92, speck) * 0.6 * saturate(crackle);
                }
                float alpha = saturate(core + haloAlpha + rim);
                float3 colour = (core * coreColour + haloAlpha * float3(1, 1, 1) + rim * _RimColor.rgb) / max(core + haloAlpha + rim, 1e-4);
                return fixed4(colour * input.color.rgb, alpha * input.color.a);
            }
            ENDCG
        }
    }
}
