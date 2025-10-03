Shader "Hidden/VSMBinaryVisualize"
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
                // 二值化：深度小于1.0表示已写入（绿色），否则未使用（黑色）
                float3 color = depth < 0.999 ? float3(0, 1, 0) : float3(0, 0, 0);
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
}
