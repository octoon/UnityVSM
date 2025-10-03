Shader "Hidden/VSMHeatmapVisualize"
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

            // 热力图颜色映射（蓝->绿->黄->红）
            float3 HeatmapColor(float value)
            {
                float3 color;
                if (value < 0.25)
                {
                    // 蓝色到青色
                    float t = value / 0.25;
                    color = lerp(float3(0, 0, 1), float3(0, 1, 1), t);
                }
                else if (value < 0.5)
                {
                    // 青色到绿色
                    float t = (value - 0.25) / 0.25;
                    color = lerp(float3(0, 1, 1), float3(0, 1, 0), t);
                }
                else if (value < 0.75)
                {
                    // 绿色到黄色
                    float t = (value - 0.5) / 0.25;
                    color = lerp(float3(0, 1, 0), float3(1, 1, 0), t);
                }
                else
                {
                    // 黄色到红色
                    float t = (value - 0.75) / 0.25;
                    color = lerp(float3(1, 1, 0), float3(1, 0, 0), t);
                }
                return color;
            }

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
                // 将深度映射到热力图颜色
                float value = 1.0 - depth; // 反转深度
                float3 color = HeatmapColor(value);
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
}
