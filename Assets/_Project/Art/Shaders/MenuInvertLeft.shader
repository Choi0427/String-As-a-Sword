// Main menu after a clear: invert only the left part of the image (margin, logo, strings) so it turns white,
// leaving the knight and the chessboard on the right as they are. The boundary leans with the board's edge.
Shader "StringAsASword/UI/MenuInvertLeft"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Invert ("Invert Amount", Range(0, 1)) = 0
        _EdgeBottom ("Edge at Bottom (uv.x)", Range(0, 1)) = 0.36
        _EdgeTop ("Edge at Top (uv.x)", Range(0, 1)) = 0.45
        _Feather ("Feather (uv.x)", Range(0.001, 0.2)) = 0.035
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            sampler2D _MainTex; fixed4 _Color; float _Invert, _EdgeBottom, _EdgeTop, _Feather;
            v2f vert (appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color; return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                float edge = lerp(_EdgeBottom, _EdgeTop, i.uv.y);
                float left = 1 - smoothstep(edge - _Feather, edge + _Feather, i.uv.x);
                c.rgb = lerp(c.rgb, 1 - c.rgb, left * _Invert);
                return c;
            }
            ENDCG
        }
    }
}
