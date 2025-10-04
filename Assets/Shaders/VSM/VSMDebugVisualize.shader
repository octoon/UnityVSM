Shader "VSM/DebugVisualize"
{
    Properties
    {
        _VisMode ("Visualization Mode", Float) = 0
        _CascadeLevel ("Cascade Level", Range(0, 15)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }

        Pass
        {
            Name "DEBUG"
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "../Include/VSMCommon.hlsl"

            float _VisMode;
            float _CascadeLevel;

            // VSM global variables
            Texture2DArray<uint> _VSM_VirtualPageTable;
            Texture2D<float> _VSM_PhysicalMemory;
            StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
            StructuredBuffer<int2> _VSM_CascadeOffsets;
            float _VSM_FirstCascadeSize;
            float3 _VSM_CameraPosition;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int mode = (int)_VisMode;

                // Mode 0: Physical Memory Depth (直接显示物理内存)
                if (mode == 0)
                {
                    // 将屏幕UV映射到物理内存
                    int2 physicalCoord = int2(i.uv.x * VSM_PHYSICAL_MEMORY_WIDTH,
                                               i.uv.y * VSM_PHYSICAL_MEMORY_HEIGHT);
                    float depth = _VSM_PhysicalMemory.Load(int3(physicalCoord, 0)).r;

                    // 可视化深度：1.0(远平面)=白色, 0.0(近平面)=黑色
                    return fixed4(depth, depth, depth, 1);
                }

                // Mode 1: Virtual Page Table State (显示页表状态)
                else if (mode == 1)
                {
                    int cascade = (int)_CascadeLevel;
                    int2 pageCoord = int2(i.uv.x * VSM_PAGE_TABLE_RESOLUTION,
                                          i.uv.y * VSM_PAGE_TABLE_RESOLUTION);

                    // 应用cascade offset (sliding window)
                    int2 offset = _VSM_CascadeOffsets[cascade];
                    int3 wrapped = VirtualPageCoordsToWrappedCoords(int3(pageCoord, cascade), offset);

                    if (wrapped.x < 0)
                        return fixed4(1, 0, 1, 1); // 紫色=无效坐标

                    uint pageEntry = _VSM_VirtualPageTable[wrapped];

                    bool isAllocated = GetIsAllocated(pageEntry);
                    bool isVisible = GetIsVisible(pageEntry);
                    bool isDirty = GetIsDirty(pageEntry);

                    // 颜色编码：
                    // 黑色 = 未分配
                    // 绿色 = 已分配但不可见
                    // 黄色 = 可见但不脏
                    // 红色 = 脏页(需要渲染)
                    if (!isAllocated)
                        return fixed4(0, 0, 0, 1);
                    else if (isDirty)
                        return fixed4(1, 0, 0, 1);
                    else if (isVisible)
                        return fixed4(1, 1, 0, 1);
                    else
                        return fixed4(0, 1, 0, 1);
                }

                // Mode 2: Physical Page Mapping (显示虚拟页到物理页的映射)
                else if (mode == 2)
                {
                    int cascade = (int)_CascadeLevel;
                    int2 pageCoord = int2(i.uv.x * VSM_PAGE_TABLE_RESOLUTION,
                                          i.uv.y * VSM_PAGE_TABLE_RESOLUTION);

                    int2 offset = _VSM_CascadeOffsets[cascade];
                    int3 wrapped = VirtualPageCoordsToWrappedCoords(int3(pageCoord, cascade), offset);

                    if (wrapped.x < 0)
                        return fixed4(1, 0, 1, 1);

                    uint pageEntry = _VSM_VirtualPageTable[wrapped];

                    if (!GetIsAllocated(pageEntry))
                        return fixed4(0, 0, 0, 1);

                    // 获取物理页坐标并显示为颜色
                    int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);

                    // 将物理页坐标归一化为颜色
                    float r = physicalPage.x / (float)VSM_PHYSICAL_PAGE_COUNT_X;
                    float g = physicalPage.y / (float)VSM_PHYSICAL_PAGE_COUNT_Y;

                    return fixed4(r, g, 0, 1);
                }

                // Mode 3: Sampled Depth from Virtual Pages (通过虚拟页采样深度)
                else if (mode == 3)
                {
                    int cascade = (int)_CascadeLevel;
                    int2 virtualCoord = int2(i.uv.x * VSM_VIRTUAL_TEXTURE_RESOLUTION,
                                             i.uv.y * VSM_VIRTUAL_TEXTURE_RESOLUTION);

                    // 计算页坐标和页内坐标
                    int2 pageCoord = virtualCoord / VSM_PAGE_SIZE;
                    int2 texelInPage = virtualCoord % VSM_PAGE_SIZE;

                    // 应用offset
                    int2 offset = _VSM_CascadeOffsets[cascade];
                    int3 wrapped = VirtualPageCoordsToWrappedCoords(int3(pageCoord, cascade), offset);

                    if (wrapped.x < 0)
                        return fixed4(1, 0, 1, 1);

                    uint pageEntry = _VSM_VirtualPageTable[wrapped];

                    if (!GetIsAllocated(pageEntry))
                        return fixed4(0, 0, 0, 1); // 黑色=未分配

                    // 获取物理页坐标
                    int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                    int2 physicalTexel = physicalPage * VSM_PAGE_SIZE + texelInPage;

                    // 读取深度
                    float depth = _VSM_PhysicalMemory.Load(int3(physicalTexel, 0)).r;

                    return fixed4(depth, depth, depth, 1);
                }

                // Mode 4: Cascade Coverage (显示级联覆盖范围)
                else if (mode == 4)
                {
                    // 显示每个级联的UV范围
                    int cascade = (int)_CascadeLevel;

                    // 将UV映射回虚拟纹理空间
                    float2 virtualUV = i.uv;

                    // 检查这个UV是否在当前cascade的有效范围内
                    int2 pageCoord = int2(virtualUV * VSM_PAGE_TABLE_RESOLUTION);

                    if (pageCoord.x >= 0 && pageCoord.x < VSM_PAGE_TABLE_RESOLUTION &&
                        pageCoord.y >= 0 && pageCoord.y < VSM_PAGE_TABLE_RESOLUTION)
                    {
                        return fixed4(0, 1, 0, 1); // 绿色=有效范围
                    }
                    else
                    {
                        return fixed4(1, 0, 0, 1); // 红色=超出范围
                    }
                }

                return fixed4(1, 0, 1, 1); // 默认紫色
            }
            ENDCG
        }
    }
}
