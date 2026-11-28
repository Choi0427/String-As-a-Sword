// Shows the noise camera's render texture over the battle UI. The texture is already premultiplied (sprites blended
// onto a clear target), so it is composited with One / OneMinusSrcAlpha; the default UI blend would multiply by alpha
// a second time and darken the soft edges of the glow and backing.
Shader "StringAsASword/UI/NoiseOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Premultiplied: the tint's alpha fades colour and coverage together.
                half4 color = tex2D(_MainTex, input.uv);
                return half4(color.rgb * input.color.rgb, color.a) * input.color.a;
            }
            ENDCG
        }
    }
}
