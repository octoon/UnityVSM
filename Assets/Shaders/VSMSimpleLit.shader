Shader "VSM/SimpleLit"
{
    Properties
    {
        _Color ("Color", Color) = (0.8, 0.8, 0.8, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "Include/VSMCommon.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _ShadowStrength;

            // VSM globals
            Texture2DArray<uint> _VSM_VirtualPageTable;
            Texture2D<uint> _VSM_PhysicalMemory;
            StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
            StructuredBuffer<int2> _VSM_CascadeOffsets;
            float _VSM_FirstCascadeSize;
            float3 _VSM_CameraPosition;

            // Helper function
            float LoadPhysicalMemoryDepth(int2 pixel)
            {
                uint depthUint = _VSM_PhysicalMemory.Load(int3(pixel, 0)).r;
                return asfloat(depthUint);
            }

            int VSM_CalculateCascadeLevel(float3 worldPos)
            {
                float distance = length(worldPos - _VSM_CameraPosition);
                float level = max(ceil(log2(distance / _VSM_FirstCascadeSize)), 0);
                return min((int)level, VSM_CASCADE_COUNT - 1);
            }

            float VSM_SampleShadow(float3 worldPos, float bias)
            {
                int cascadeLevel = VSM_CalculateCascadeLevel(worldPos);

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

                if (wrappedCoords.x < 0)
                    return 1.0;

                uint pageEntry = _VSM_VirtualPageTable[wrappedCoords];

                if (!GetIsAllocated(pageEntry))
                    return 1.0;

                int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);
                float2 pageUV = frac(lightSpaceUV * VSM_PAGE_TABLE_RESOLUTION);
                int2 physicalPixel = int2((physicalPageCoords + pageUV) * VSM_PAGE_SIZE);

                float shadowDepth = LoadPhysicalMemoryDepth(physicalPixel);
                float currentDepth = lightSpacePos.z;

                return (currentDepth - bias) > shadowDepth ? 0.0 : 1.0;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample texture
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;

                // Simple Lambert lighting
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndotl = max(0, dot(i.worldNormal, lightDir));

                // Sample VSM shadow
                float shadow = VSM_SampleShadow(i.worldPos, 0.001);

                // Apply lighting and shadows
                float3 lighting = _LightColor0.rgb * ndotl;
                lighting = lerp(lighting * (1.0 - _ShadowStrength), lighting, shadow);

                col.rgb *= lighting + UNITY_LIGHTMODEL_AMBIENT.rgb;

                return col;
            }
            ENDCG
        }
    }

    Fallback "VertexLit"
}
