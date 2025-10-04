Shader "VSM/Test"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        [KeywordEnum(Color, Shadow, PageAlloc, Depth)] _TestMode("Test Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _TESTMODE_COLOR _TESTMODE_SHADOW _TESTMODE_PAGEALLOC _TESTMODE_DEPTH
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "Include/VSMCommon.hlsl"

            float4 _Color;

            // VSM全局变量
            Texture2DArray<uint> _VSM_VirtualPageTable;
            Texture2D<float> _VSM_PhysicalMemory;
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
                #ifdef _TESTMODE_COLOR
                    // 测试1: 只显示颜色
                    return _Color;

                #elif defined(_TESTMODE_SHADOW)
                    // 测试2: 显示阴影采样结果
                    // 计算cascade
                    float dist = length(i.worldPos - _VSM_CameraPosition);
                    int cascade = (int)max(0, ceil(log2(dist / _VSM_FirstCascadeSize)));
                    cascade = min(cascade, VSM_CASCADE_COUNT - 1);

                    // 转换到光空间
                    float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                    float4 lightSpace = mul(lightMat, float4(i.worldPos, 1.0));
                    float3 ndc = lightSpace.xyz / lightSpace.w;
                    float2 uv = ndc.xy * 0.5 + 0.5;

                    // 边界检查
                    if (any(uv < 0.0) || any(uv > 1.0))
                        return float4(1, 0, 1, 1); // 紫色=超出范围

                    // 虚拟页坐标
                    int3 pageCoords = int3(floor(uv * VSM_PAGE_TABLE_RESOLUTION), cascade);

                    // 应用offset
                    int2 offset = _VSM_CascadeOffsets[cascade];
                    int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                    if (wrapped.x < 0)
                        return float4(0, 1, 1, 1); // 青色=wrapped失败

                    // 查找页表
                    uint pageEntry = _VSM_VirtualPageTable[wrapped];

                    if (!GetIsAllocated(pageEntry))
                        return float4(1, 1, 0, 1); // 黄色=页面未分配

                    // 获取物理页
                    int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                    float2 pageUV = frac(uv * VSM_PAGE_TABLE_RESOLUTION);
                    int2 physicalPixel = physicalPage * VSM_PAGE_SIZE + int2(pageUV * VSM_PAGE_SIZE);

                    // 采样深度
                    float shadowDepth = _VSM_PhysicalMemory.Load(int3(physicalPixel, 0)).r;
                    float currentDepth = ndc.z;
                    #if !UNITY_REVERSED_Z
                        currentDepth = currentDepth * 0.5 + 0.5;
                    #endif

                    // 比较深度
                    float shadow = (currentDepth - 0.001) > shadowDepth ? 0.0 : 1.0;

                    // 显示阴影：黑色=阴影，白色=光照
                    return float4(shadow, shadow, shadow, 1);

                #elif defined(_TESTMODE_PAGEALLOC)
                    // 测试3: 显示页面分配状态
                    float dist = length(i.worldPos - _VSM_CameraPosition);
                    int cascade = (int)max(0, ceil(log2(dist / _VSM_FirstCascadeSize)));
                    cascade = min(cascade, VSM_CASCADE_COUNT - 1);

                    float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                    float4 lightSpace = mul(lightMat, float4(i.worldPos, 1.0));
                    float3 ndc2 = lightSpace.xyz / lightSpace.w;
                    float2 uv = ndc2.xy * 0.5 + 0.5;

                    if (any(uv < 0.0) || any(uv > 1.0))
                        return float4(0.5, 0, 0, 1); // 暗红色

                    int3 pageCoords = int3(floor(uv * VSM_PAGE_TABLE_RESOLUTION), cascade);
                    int2 offset = _VSM_CascadeOffsets[cascade];
                    int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                    if (wrapped.x < 0)
                        return float4(0, 0, 0.5, 1); // 暗蓝色

                    uint pageEntry = _VSM_VirtualPageTable[wrapped];

                    // 绿色=已分配，红色=未分配
                    bool allocated = GetIsAllocated(pageEntry);
                    return allocated ? float4(0, 1, 0, 1) : float4(1, 0, 0, 1);

                #else // _TESTMODE_DEPTH
                    // 测试4: 显示深度值
                    float dist = length(i.worldPos - _VSM_CameraPosition);
                    int cascade = (int)max(0, ceil(log2(dist / _VSM_FirstCascadeSize)));
                    cascade = min(cascade, VSM_CASCADE_COUNT - 1);

                    float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                    float4 lightSpace = mul(lightMat, float4(i.worldPos, 1.0));
                    float3 ndc3 = lightSpace.xyz / lightSpace.w;
                    float2 uv = ndc3.xy * 0.5 + 0.5;

                    if (any(uv < 0.0) || any(uv > 1.0))
                        return float4(0, 0, 0, 1);

                    int3 pageCoords = int3(floor(uv * VSM_PAGE_TABLE_RESOLUTION), cascade);
                    int2 offset = _VSM_CascadeOffsets[cascade];
                    int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                    if (wrapped.x < 0)
                        return float4(0, 0, 0, 1);

                    uint pageEntry = _VSM_VirtualPageTable[wrapped];

                    if (!GetIsAllocated(pageEntry))
                        return float4(0, 0, 0, 1);

                    int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                    float2 pageUV = frac(uv * VSM_PAGE_TABLE_RESOLUTION);
                    int2 physicalPixel = physicalPage * VSM_PAGE_SIZE + int2(pageUV * VSM_PAGE_SIZE);

                    float depth = _VSM_PhysicalMemory.Load(int3(physicalPixel, 0)).r;

                    // 显示深度（反转以便可见）
                    return float4(1.0 - depth, 1.0 - depth, 1.0 - depth, 1);
                #endif
            }
            ENDHLSL
        }
    }
}
