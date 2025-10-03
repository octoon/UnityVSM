Shader "Hidden/VSMPageTableAllocation"
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

                // 检查分配标志
                bool allocated = (pageData & 0x80000000) != 0;
                bool visible = (pageData & 0x40000000) != 0;
                bool dirty = (pageData & 0x20000000) != 0;

                if (!allocated)
                {
                    // 未分配：黑色
                    return float4(0, 0, 0, 1.0);
                }
                else if (dirty)
                {
                    // 脏页（需要重新渲染）：黄色
                    return float4(1, 1, 0, 1.0);
                }
                else if (visible)
                {
                    // 可见页：绿色
                    return float4(0, 1, 0, 1.0);
                }
                else
                {
                    // 已分配但不可见：蓝色
                    return float4(0, 0.5, 1, 1.0);
                }
            }
            ENDCG
        }
    }
}
