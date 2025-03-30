Shader "Custom/RotateUVShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Rotation ("Rotation (Degrees)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Rotation;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float2 RotateUV(float2 uv, float angle)
            {
                float2 center = float2(0.5, 0.5);
                uv -= center;
                float s = sin(angle);
                float c = cos(angle);
                uv = float2(
                    uv.x * c - uv.y * s,
                    uv.x * s + uv.y * c
                );
                return uv + center;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float radians = _Rotation * UNITY_PI / 180.0;
                o.uv = RotateUV(TRANSFORM_TEX(v.uv, _MainTex), radians);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}
