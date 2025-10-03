Shader "VSM/SimpleShadowReceiver"
{
    Properties
    {
        _Color ("Color", Color) = (0.8,0.8,0.8,1)
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            float4 _Color;
            float _ShadowStrength;
            float3 _DirectionalLightDir;
            float4 _DirectionalLightColor;

            // VSM globals
            Texture2DArray<uint> _VSM_VirtualPageTable;
            Texture2D<uint> _VSM_PhysicalMemory;  // Stored as uint (use Load)
            StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
            StructuredBuffer<int2> _VSM_CascadeOffsets;
            float _VSM_FirstCascadeSize;
            float3 _VSM_CameraPosition;

            #define VSM_PAGE_TABLE_RESOLUTION 32
            #define VSM_PAGE_SIZE 128
            #define VSM_PHYSICAL_MEMORY_WIDTH 4096
            #define VSM_PHYSICAL_MEMORY_HEIGHT 4096
            #define VSM_CASCADE_COUNT 16

            // Helper functions
            int CalculateCascadeLevel(float3 worldPos, float3 cameraPos, float firstCascadeSize)
            {
                float distance = length(worldPos - cameraPos);
                int cascade = 0;
                float cascadeSize = firstCascadeSize;

                for (int i = 0; i < VSM_CASCADE_COUNT - 1; i++)
                {
                    if (distance < cascadeSize)
                        break;
                    cascade = i + 1;
                    cascadeSize *= 2.0;
                }

                return min(cascade, VSM_CASCADE_COUNT - 1);
            }

            int3 VirtualPageCoordsToWrappedCoords(int3 pageCoords, int2 cascadeOffset)
            {
                int wrappedX = (pageCoords.x - cascadeOffset.x) & (VSM_PAGE_TABLE_RESOLUTION - 1);
                int wrappedY = (pageCoords.y - cascadeOffset.y) & (VSM_PAGE_TABLE_RESOLUTION - 1);
                return int3(wrappedX, wrappedY, pageCoords.z);
            }

            bool GetIsAllocated(uint pageEntry)
            {
                return (pageEntry & 0x80000000u) != 0;
            }

            int2 UnpackPhysicalPageCoords(uint pageEntry)
            {
                uint x = (pageEntry >> 16) & 0x7FFF;
                uint y = pageEntry & 0xFFFF;
                return int2(x, y);
            }

            float SampleVSM(float3 worldPos, float bias)
            {
                int cascadeLevel = CalculateCascadeLevel(worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);

                float4x4 lightMatrix = _VSM_CascadeLightMatrices[cascadeLevel];
                float4 lightSpacePos = mul(lightMatrix, float4(worldPos, 1.0));
                float2 lightSpaceUV = lightSpacePos.xy * 0.5 + 0.5;

                if (any(lightSpaceUV < 0.0) || any(lightSpaceUV > 1.0))
                    return 1.0;

                int3 pageCoords = int3(
                    floor(lightSpaceUV * VSM_PAGE_TABLE_RESOLUTION),
                    cascadeLevel
                );

                int2 cascadeOffset = _VSM_CascadeOffsets[cascadeLevel];
                int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);

                uint pageEntry = _VSM_VirtualPageTable[wrappedCoords];

                if (!GetIsAllocated(pageEntry))
                    return 1.0;

                int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);
                float2 pageUV = frac(lightSpaceUV * VSM_PAGE_TABLE_RESOLUTION);

                // Calculate pixel coordinates (cannot sample uint textures, must use Load)
                int2 physicalPixel = int2((physicalPageCoords + pageUV) * VSM_PAGE_SIZE);

                uint shadowDepthUint = _VSM_PhysicalMemory.Load(int3(physicalPixel, 0)).r;
                float shadowDepth = asfloat(shadowDepthUint);
                float currentDepth = lightSpacePos.z;

                return (currentDepth - bias) > shadowDepth ? 0.0 : 1.0;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(-_DirectionalLightDir);
                float ndotl = max(0, dot(normal, lightDir));

                // Sample VSM shadow
                float shadow = SampleVSM(i.worldPos, 0.001);

                // Apply shadow attenuation
                float shadowAttenuation = lerp(1.0 - _ShadowStrength, 1.0, shadow);

                // Calculate final lighting
                float3 lighting = _DirectionalLightColor.rgb * ndotl * shadowAttenuation;
                float3 ambient = float3(0.3, 0.3, 0.3);

                float4 col = _Color;
                col.rgb *= lighting + ambient;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
