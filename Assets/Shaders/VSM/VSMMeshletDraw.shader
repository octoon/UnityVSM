Shader "VSM/MeshletDraw"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "VSMMeshletPass"

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0  // Don't write color

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma require 2darray

            #include "UnityCG.cginc"
            #include "../Include/VSMCommon.hlsl"

            // Meshlet data structures
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

            // Buffers set by VSMMeshletRenderer
            StructuredBuffer<Meshlet> _Meshlets;
            StructuredBuffer<float3> _MeshletVertices;
            StructuredBuffer<uint> _MeshletTriangles;
            StructuredBuffer<uint> _VisibleMeshletIndices;

            // VSM global data
            Texture2DArray<uint> _VirtualPageTable;
            RWStructuredBuffer<uint> _PhysicalMemory;  // DX12: Use StructuredBuffer for InterlockedMin support
            StructuredBuffer<float4x4> _CascadeLightMatrices;
            StructuredBuffer<int2> _CascadeOffsets;
            uint _CurrentCascade;
            float4x4 _ModelMatrix;
            uint _PhysicalMemoryWidth;  // For 2D to 1D index conversion

            struct appdata
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                uint cascadeIndex : TEXCOORD0;
                nointerpolation int2 cascadeOffset : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;

                // instanceID = index into _VisibleMeshletIndices
                uint visibleMeshletIndex = v.instanceID;
                uint meshletIndex = _VisibleMeshletIndices[visibleMeshletIndex];
                Meshlet meshlet = _Meshlets[meshletIndex];

                // vertexID = triangle index within meshlet (0 to triangleCount*3-1)
                uint triangleVertexIndex = v.vertexID;
                if (triangleVertexIndex >= meshlet.triangleCount * 3)
                {
                    // Out of range - discard
                    o.pos = float4(0, 0, 0, 1);
                    return o;
                }

                // Get vertex index from index buffer
                uint globalTriangleIndex = meshlet.indexOffset + triangleVertexIndex;
                uint localVertexIndex = _MeshletTriangles[globalTriangleIndex];
                uint globalVertexIndex = meshlet.vertexOffset + localVertexIndex;

                // Get vertex position (in model space)
                float3 modelPos = _MeshletVertices[globalVertexIndex];

                // Transform to world space
                float4 worldPos = mul(_ModelMatrix, float4(modelPos, 1.0));

                // Transform to light space
                float4x4 lightMatrix = _CascadeLightMatrices[_CurrentCascade];
                o.pos = mul(lightMatrix, worldPos);

                o.cascadeIndex = _CurrentCascade;
                o.cascadeOffset = _CascadeOffsets[_CurrentCascade];

                return o;
            }

            // Paper Listing 12.3: Fragment shader with imageAtomicMin
            void frag(v2f i)
            {
                // Calculate NDC position
                float3 ndc = i.pos.xyz / i.pos.w;

                // Convert NDC to UV [0,1]
                float2 uv = ndc.xy * 0.5 + 0.5;

                // Paper: "const vec2 virtual_uv = gl_FragCoord.xy / VSM_TEXTURE_RESOLUTION"
                // In our case, we use projected UV directly
                float2 virtualUV = uv;

                // Paper: "const ivec3 page_coords = ivec3(floor(virtual_uv * VSM_PAGE_TABLE_RESOLUTION), cascade_index)"
                int3 pageCoords = int3(
                    floor(virtualUV * VSM_PAGE_TABLE_RESOLUTION),
                    i.cascadeIndex
                );

                // Paper: "const ivec3 wrapped_page_coords = virtual_page_coords_to_wrapped_coords(page_coords, cascade_offset)"
                int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, i.cascadeOffset);

                if (wrappedCoords.x < 0)
                    discard;

                // Paper: "const uint page_entry = imageLoad(virtual_page_table, wrapped_page_coords).r"
                uint pageEntry = _VirtualPageTable[wrappedCoords];

                // Paper: "if(get_is_allocated(page_entry) && get_is_dirty(page_entry))"
                if (!GetIsAllocated(pageEntry) || !GetIsDirty(pageEntry))
                    discard;

                // Paper: "const ivec2 physical_page_coords = unpack_physical_page_coords(page_entry)"
                int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);

                // Paper: "const ivec2 virtual_texel_coords = ivec2(gl_FragCoord.xy)"
                // We need to calculate texel coords from UV
                int2 virtualTexelCoords = int2(virtualUV * VSM_VIRTUAL_TEXTURE_RESOLUTION);

                // Paper: "const ivec2 in_page_texel_coords = ivec2(mod(virtual_texel_coord, VSM_PAGE_SIZE))"
                int2 inPageTexelCoords = int2(virtualTexelCoords.x % VSM_PAGE_SIZE,
                                               virtualTexelCoords.y % VSM_PAGE_SIZE);

                // Paper: "const ivec2 in_memory_offset = physical_page_coords * VSM_PAGE_SIZE"
                int2 inMemoryOffset = physicalPageCoords * VSM_PAGE_SIZE;

                // Paper: "const ivec2 memory_texel_coords = in_memory_offset + in_page_texel_coord"
                int2 memoryTexelCoords = inMemoryOffset + inPageTexelCoords;

                // Convert 2D coordinates to 1D index for StructuredBuffer
                uint memoryIndex = memoryTexelCoords.y * _PhysicalMemoryWidth + memoryTexelCoords.x;

                // Handle platform depth differences
                float fragmentDepth = ndc.z;
                #if !UNITY_REVERSED_Z
                    fragmentDepth = fragmentDepth * 0.5 + 0.5;
                #endif

                // Paper: "imageAtomicMin(vsm_memory, memory_texel_coords, floatBitsToUint(gl_FragCoord.z))"
                // DX12: Use InterlockedMin on RWStructuredBuffer
                InterlockedMin(_PhysicalMemory[memoryIndex], asuint(fragmentDepth));
            }
            ENDHLSL
        }
    }
}
