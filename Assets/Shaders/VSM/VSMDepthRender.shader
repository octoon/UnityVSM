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

            // Physical memory
            RWTexture2D<float> _PhysicalMemory;

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

                // Only write to allocated and dirty pages
                if (!GetIsAllocated(pageEntry) || !GetIsDirty(pageEntry))
                    discard;

                // Get physical page coordinates
                int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);

                // Calculate texel offset within the page (modulo operation)
                int2 inPageTexelCoords = int2(virtualTexelCoords.x % VSM_PAGE_SIZE,
                                               virtualTexelCoords.y % VSM_PAGE_SIZE);

                // Calculate final physical memory texel coordinates
                int2 inMemoryOffset = physicalPageCoords * VSM_PAGE_SIZE;
                int2 memoryTexelCoords = inMemoryOffset + inPageTexelCoords;

                // Paper Listing 12.3: Use gl_FragCoord.z (hardware-interpolated depth)
                // In Unity, i.pos.z contains the clip-space depth after rasterization
                float fragmentDepth = i.pos.z;

                // Write depth using atomic min (paper Listing 12.3)
                InterlockedMin(_PhysicalMemory[memoryTexelCoords], asuint(fragmentDepth));

                // Output depth to depth buffer
                depth = fragmentDepth;
            }
            ENDHLSL
        }
    }
}
