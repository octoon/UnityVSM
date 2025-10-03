Shader "Hidden/VSMPageTableHeatmap"
{
    Properties
    {
        _MainTex ("Texture", 2DArray) = "white" {}
        _CascadeIndex ("Cascade Index", Int) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
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

            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            int _CascadeIndex;

            // 热力图颜色映射
            float3 HeatmapColor(float value)
            {
                float3 color;
                if (value < 0.25)
                {
                    float t = value / 0.25;
                    color = lerp(float3(0, 0, 0.5), float3(0, 0, 1), t);
                }
                else if (value < 0.5)
                {
                    float t = (value - 0.25) / 0.25;
                    color = lerp(float3(0, 0, 1), float3(0, 1, 1), t);
                }
                else if (value < 0.75)
                {
                    float t = (value - 0.5) / 0.25;
                    color = lerp(float3(0, 1, 1), float3(0, 1, 0), t);
                }
                else
                {
                    float t = (value - 0.75) / 0.25;
                    color = lerp(float3(0, 1, 0), float3(1, 0, 0), t);
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
                // 从页表读取数据
                int pageData = (int)UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(i.uv, _CascadeIndex)).r;

                // 解析标志位
                bool allocated = (pageData & 0x80000000) != 0;
                bool visible = (pageData & 0x40000000) != 0;
                bool dirty = (pageData & 0x20000000) != 0;

                // 计算热度值（基于状态）
                float heatValue = 0.0;
                if (allocated)
                {
                    heatValue = 0.3; // 基础热度
                    if (visible) heatValue += 0.4; // 可见增加热度
                    if (dirty) heatValue += 0.3; // 脏页增加热度
                }

                // 映射到热力图颜色
                float3 color = HeatmapColor(heatValue);
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
}
