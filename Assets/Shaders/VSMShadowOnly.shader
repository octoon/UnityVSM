Shader "VSM/ShadowOnly"
{
    Properties
    {
        _ShadowBias ("Shadow Bias", Range(0, 0.01)) = 0.001
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "Include/VSMCommon.hlsl"

            float _ShadowBias;

            // VSM全局变量
            Texture2DArray<uint> _VSM_VirtualPageTable;
            Texture2D<uint> _VSM_PhysicalMemory;
            StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
            StructuredBuffer<int2> _VSM_CascadeOffsets;
            float _VSM_FirstCascadeSize;
            float3 _VSM_CameraPosition;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 计算cascade
                float dist = length(i.worldPos - _VSM_CameraPosition);
                int cascade = (int)max(0, ceil(log2(dist / _VSM_FirstCascadeSize)));
                cascade = min(cascade, VSM_CASCADE_COUNT - 1);

                // 转换到光空间
                float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                float4 lightSpace = mul(lightMat, float4(i.worldPos, 1.0));
                float2 uv = lightSpace.xy * 0.5 + 0.5;

                // 边界检查
                if (any(uv < 0.0) || any(uv > 1.0))
                    return float4(1, 0, 1, 1); // 紫色 = 超出范围

                // 虚拟页坐标
                int3 pageCoords = int3(floor(uv * VSM_PAGE_TABLE_RESOLUTION), cascade);

                // 应用offset
                int2 offset = _VSM_CascadeOffsets[cascade];
                int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                if (wrapped.x < 0)
                    return float4(0, 1, 1, 1); // 青色 = wrapped失败

                // 查找页表
                uint pageEntry = _VSM_VirtualPageTable[wrapped];

                if (!GetIsAllocated(pageEntry))
                    return float4(1, 0, 0, 1); // 红色 = 页面未分配

                // 获取物理页
                int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                float2 pageUV = frac(uv * VSM_PAGE_TABLE_RESOLUTION);
                int2 physicalPixel = int2((physicalPage + pageUV) * VSM_PAGE_SIZE);

                // 采样深度
                uint depthUint = _VSM_PhysicalMemory.Load(int3(physicalPixel, 0)).r;
                float shadowDepth = asfloat(depthUint);
                float currentDepth = lightSpace.z;

                // 比较深度
                float shadow = (currentDepth - _ShadowBias) > shadowDepth ? 0.0 : 1.0;

                // ===== 直接输出阴影采样结果 =====
                // 白色 = 光照（shadow = 1.0）
                // 黑色 = 阴影（shadow = 0.0）
                return float4(shadow, shadow, shadow, 1.0);
            }
            ENDHLSL
        }
    }
}
