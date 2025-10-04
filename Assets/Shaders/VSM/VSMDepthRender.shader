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
            // Write depth to COLOR buffer (RFloat render target)

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
                float3 lightSpacePos : TEXCOORD0;  // Light space position (for UV calculation)
                uint cascadeIndex : TEXCOORD1;
            };

            // Virtual Page Table (read-only for validation)
            Texture2DArray<uint> _VirtualPageTable;

            // Cascade data
            StructuredBuffer<float4x4> _CascadeLightMatrices;
            StructuredBuffer<int2> _CascadeOffsets;
            uint _CurrentCascade;

            v2f vert(appdata v)
            {
                v2f o;

                // Transform to light space using cascade matrix
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                float4x4 lightMatrix = _CascadeLightMatrices[_CurrentCascade];
                o.lightSpacePos = mul(lightMatrix, worldPos).xyz;
                o.pos = mul(lightMatrix, worldPos);  // Output light space position as clip position
                o.cascadeIndex = _CurrentCascade;

                return o;
            }

            // Simplified fragment shader: output depth to COLOR target (not SV_Depth)
            // We render to RFloat color buffer, then compute shader copies to physical memory
            float frag(v2f i) : SV_Target
            {
                // Calculate NDC position from light space
                float3 ndc = i.lightSpacePos / i.pos.w;

                // Convert NDC [-1,1] to UV [0,1]
                float2 uv = ndc.xy * 0.5 + 0.5;

                // Calculate page coordinates
                int3 pageCoords = int3(
                    floor(uv * VSM_PAGE_TABLE_RESOLUTION),
                    i.cascadeIndex
                );

                // Convert to wrapped coordinates
                int2 cascadeOffset = _CascadeOffsets[i.cascadeIndex];
                int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);

                if (wrappedCoords.x < 0)
                    discard;

                // Look up page entry
                uint pageEntry = _VirtualPageTable[wrappedCoords];

                // Only render to allocated pages
                if (!GetIsAllocated(pageEntry))
                    discard;

                // Handle platform depth differences
                float fragmentDepth = ndc.z;
                #if !UNITY_REVERSED_Z
                    fragmentDepth = fragmentDepth * 0.5 + 0.5;
                #endif

                // Output depth to COLOR buffer (will be copied to physical memory by compute shader)
                return fragmentDepth;
            }
            ENDHLSL
        }
    }
}
