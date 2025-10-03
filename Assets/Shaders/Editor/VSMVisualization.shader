Shader "Hidden/VSMDepthVisualize"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

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

            sampler2D _MainTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float depth = tex2D(_MainTex, i.uv).r;
                // 反转深度：1.0（远）显示为黑色，接近0.0显示为白色
                float visualDepth = 1.0 - depth;
                return float4(visualDepth, visualDepth, visualDepth, 1.0);
            }
            ENDCG
        }
    }
}
