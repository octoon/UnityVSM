Shader "VSM/MeshletRender"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Name "VSMMeshletDepth"

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "../Include/VSMCommon.hlsl"

            // Meshlet数据结构
            struct Meshlet
            {
                float3 boundsMin;
                float3 boundsMax;
                uint vertexOffset;
                uint vertexCount;
                uint indexOffset;
                uint triangleCount;
                uint meshletIndex;
                uint padding1;
                uint padding2;
                uint padding3;
            };

            struct MeshletVertex
            {
                float3 position;
                float3 normal;
                float2 uv;
            };

            struct MeshletTriangle
            {
                uint v0, v1, v2;
            };

            // Meshlet buffers
            StructuredBuffer<Meshlet> _Meshlets;
            StructuredBuffer<MeshletVertex> _MeshletVertices;
            StructuredBuffer<MeshletTriangle> _MeshletTriangles;
            StructuredBuffer<uint> _VisibleMeshletIndices;

            // VSM数据
            Texture2DArray<uint> _VirtualPageTable;
            RWTexture2D<float> _PhysicalMemory;
            StructuredBuffer<float4x4> _CascadeLightMatrices;
            StructuredBuffer<int2> _CascadeOffsets;

            // 当前级联和物体矩阵
            uint _CurrentCascade;
            float4x4 _ModelMatrix;

            struct appdata
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                uint cascadeIndex : TEXCOORD0;
            };

            // 论文: "After culling, a group of 32 mesh shader threads is dispatched for each of the surviving meshlets"
            // 在Unity中，我们用instance ID模拟mesh shader
            v2f vert(appdata v)
            {
                v2f o;

                // 从可见meshlet列表获取meshlet索引
                uint meshletIndex = _VisibleMeshletIndices[v.instanceID];
                Meshlet meshlet = _Meshlets[meshletIndex];

                // 计算三角形索引和顶点索引
                uint triangleIndex = v.vertexID / 3;
                uint triangleVertexIndex = v.vertexID % 3;

                if (triangleIndex >= meshlet.triangleCount)
                {
                    // 超出范围，退化三角形
                    o.pos = float4(0, 0, 0, 0);
                    o.cascadeIndex = _CurrentCascade;
                    return o;
                }

                // 获取三角形
                MeshletTriangle tri = _MeshletTriangles[meshlet.indexOffset + triangleIndex];
                uint localVertexIndex = triangleVertexIndex == 0 ? tri.v0 : (triangleVertexIndex == 1 ? tri.v1 : tri.v2);

                // 获取顶点
                MeshletVertex vertex = _MeshletVertices[meshlet.vertexOffset + localVertexIndex];

                // 变换到光空间
                float4 worldPos = mul(_ModelMatrix, float4(vertex.position, 1.0));
                float4x4 lightMatrix = _CascadeLightMatrices[_CurrentCascade];
                o.pos = mul(lightMatrix, worldPos);
                o.cascadeIndex = _CurrentCascade;

                return o;
            }

            // 论文 Listing 12.3: Fragment shader
            void frag(v2f i, out float depth : SV_Depth)
            {
                // 计算虚拟纹理坐标
                int2 virtualTexel = int2(i.pos.xy);
                float2 virtualUV = virtualTexel / float(VSM_VIRTUAL_TEXTURE_RESOLUTION);

                // 计算页坐标
                int3 pageCoords = int3(
                    floor(virtualUV * VSM_PAGE_TABLE_RESOLUTION),
                    i.cascadeIndex
                );

                // 环形缓冲转换
                int2 cascadeOffset = _CascadeOffsets[i.cascadeIndex];
                int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);

                if (wrappedCoords.x < 0) discard;

                // 查找页表
                uint pageEntry = _VirtualPageTable[wrappedCoords];

                // 只写入已分配且脏的页面
                if (!GetIsAllocated(pageEntry) || !GetIsDirty(pageEntry))
                    discard;

                // 计算物理内存坐标
                int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                int2 inPageOffset = virtualTexel % VSM_PAGE_SIZE;
                int2 physicalTexel = physicalPage * VSM_PAGE_SIZE + inPageOffset;

                // 论文: "an atomic min operation is used to store the new depth"
                float fragmentDepth = i.pos.z;
                InterlockedMin(_PhysicalMemory[physicalTexel], asuint(fragmentDepth));

                depth = fragmentDepth;
            }
            ENDHLSL
        }
    }
}
