Shader "StringAsASword/UI/BossPortraitFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _FlashAmount ("Flash Amount", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half _FlashAmount;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;

            struct Attributes
            {
                float4 position : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 mask : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;

                // Match the soft edge of the portrait's RectMask2D.
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                float2 pixelSize = output.position.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float2 softness = float2(_UIMaskSoftnessX, _UIMaskSoftnessY);
                output.mask = float4(input.position.xy * 2 - rect.xy - rect.zw,
                    0.25 / (0.25 * softness + abs(pixelSize)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = tex2D(_MainTex, input.uv) * input.color;
                color.rgb = lerp(color.rgb, half3(1, 1, 1), _FlashAmount);

                #ifdef UNITY_UI_CLIP_RECT
                half2 clip = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color.a *= clip.x * clip.y;
                #endif

                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
