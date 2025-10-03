Shader "VSM/DepthRender"
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
            Name "VSMDepthPass"

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0  // Don't write color, only depth

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "../Include/VSMCommon.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                uint cascadeIndex : TEXCOORD0;
            };

            // Virtual Page Table
            Texture2DArray<uint> _VirtualPageTable;

            // Cascade data
            StructuredBuffer<float4x4> _CascadeLightMatrices;
            StructuredBuffer<int2> _CascadeOffsets;
            uint _CurrentCascade;

            // Physical memory - use uint for InterlockedMin support
            RWTexture2D<uint> _PhysicalMemory;

            v2f vert(appdata v)
            {
                v2f o;

                // Transform to light space using cascade matrix
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                float4x4 lightMatrix = _CascadeLightMatrices[_CurrentCascade];
                o.pos = mul(lightMatrix, worldPos);
                o.cascadeIndex = _CurrentCascade;

                return o;
            }

            // Paper Listing 12.3: Fragment shader that writes depth using gl_FragCoord.z
            // In Unity, we use SV_Position for fragment coordinates and depth from position
            void frag(v2f i, out float depth : SV_Depth)
            {
                // Paper Listing 12.3: Use gl_FragCoord.xy directly as virtual texel coordinates
                int2 virtualTexelCoords = int2(i.pos.xy);

                // Calculate virtual UV from texel coordinates
                float2 virtualUV = virtualTexelCoords / float(VSM_VIRTUAL_TEXTURE_RESOLUTION);

                // Calculate page coordinates
                int3 pageCoords = int3(
                    floor(virtualUV * VSM_PAGE_TABLE_RESOLUTION),
                    i.cascadeIndex
                );

                // Convert to wrapped coordinates
                int2 cascadeOffset = _CascadeOffsets[i.cascadeIndex];
                int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);

                if (wrappedCoords.x < 0)
                    discard;

                // Look up page entry
                uint pageEntry = _VirtualPageTable[wrappedCoords];

                // TEMPORARILY DISABLE dirty check for debugging
                // Only write to allocated pages (ignore dirty flag for now)
                if (!GetIsAllocated(pageEntry))
                    discard;

                // Original check (disabled for debugging):
                // if (!GetIsAllocated(pageEntry) || !GetIsDirty(pageEntry))
                //     discard;

                // Get physical page coordinates
                int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);

                // Calculate texel offset within the page (modulo operation - use uint for performance)
                uint2 inPageTexelCoords = uint2((uint)virtualTexelCoords.x % (uint)VSM_PAGE_SIZE,
                                                 (uint)virtualTexelCoords.y % (uint)VSM_PAGE_SIZE);

                // Calculate final physical memory texel coordinates
                int2 inMemoryOffset = physicalPageCoords * VSM_PAGE_SIZE;
                uint2 memoryTexelCoords = uint2(inMemoryOffset) + inPageTexelCoords;

                // Paper Listing 12.3: Use gl_FragCoord.z (hardware-interpolated depth)
                // In Unity, i.pos.z contains the clip-space depth after rasterization
                float fragmentDepth = i.pos.z;

                // Write depth using atomic min (paper Listing 12.3)
                // Convert depth to uint for atomic operations
                uint depthAsUint = asuint(fragmentDepth);
                InterlockedMin(_PhysicalMemory[memoryTexelCoords], depthAsUint);

                // Output depth to depth buffer
                depth = fragmentDepth;
            }
            ENDHLSL
        }
    }
}
