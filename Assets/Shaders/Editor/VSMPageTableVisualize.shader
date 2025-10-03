Shader "Hidden/VSMPageTableCoords"
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
                // 从页表读取数据（存储为整数）
                int pageData = (int)UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(i.uv, _CascadeIndex)).r;

                // 解析页表数据
                // Bit layout: [allocated(1)] [visible(1)] [dirty(1)] [reserved(5)] [physicalY(8)] [physicalX(8)] [flags(8)]
                int physicalX = (pageData >> 8) & 0xFF;
                int physicalY = (pageData >> 16) & 0xFF;
                bool allocated = (pageData & 0x80000000) != 0;

                if (!allocated)
                {
                    // 未分配：显示为深蓝色
                    return float4(0, 0, 0.2, 1.0);
                }

                // 将物理坐标映射为颜色
                float r = physicalX / 255.0;
                float g = physicalY / 255.0;
                return float4(r, g, 0.5, 1.0);
            }
            ENDCG
        }
    }
}
