Shader "VSM/ForwardLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _ShadowBias ("Shadow Bias", Range(0, 0.01)) = 0.001
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "Include/VSMCommon.hlsl"

            // Material properties
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ShadowBias;

            // VSM global variables (set by VirtualShadowMapManager)
            Texture2DArray<uint> _VSM_VirtualPageTable;
            Texture2D<float> _VSM_PhysicalMemory;  // Float texture for sampling
            StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
            StructuredBuffer<int2> _VSM_CascadeOffsets;
            float _VSM_FirstCascadeSize;
            float3 _VSM_CameraPosition;

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
                float3 viewDir : TEXCOORD3;
            };

            // Helper: Load depth from physical memory (float texture)
            float LoadDepth(int2 pixel)
            {
                return _VSM_PhysicalMemory.Load(int3(pixel, 0)).r;
            }

            // Sample VSM shadow at world position
            float SampleVSMShadow(float3 worldPos)
            {
                // CRITICAL: Must use SAME cascade selection as MarkVisiblePages!
                // Use distance-based heuristic (second heuristic from paper)
                int cascade = CalculateCascadeLevel(worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);

                // DEBUG: Return cascade index to verify selection
                // return cascade / 15.0; // Uncomment to see cascade (0=black, 15=white)

                // Transform to light space
                float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                float4 lightSpace = mul(lightMat, float4(worldPos, 1.0));

                // Perspective division (for orthographic, w=1.0)
                float3 ndc = lightSpace.xyz / lightSpace.w;

                // NDC [-1,1] to UV [0,1]
                float2 uv = ndc.xy * 0.5 + 0.5;

                // DEBUG: Show UV coordinates
                // return uv.x; // or uv.y

                // Out of bounds check
                if (any(uv < 0.0) || any(uv > 1.0))
                    return 0.5; // Out of bounds

                // Virtual page coordinates
                int3 pageCoords = int3(
                    floor(uv * VSM_PAGE_TABLE_RESOLUTION),
                    cascade
                );

                // DEBUG: Show page coordinates
                // return pageCoords.x / 31.0; // or pageCoords.y / 31.0

                // Apply cascade offset (sliding window)
                int2 offset = _VSM_CascadeOffsets[cascade];
                int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                // DEBUG: Show wrapped coordinates
                // return wrapped.x / 31.0; // or wrapped.y / 31.0

                if (wrapped.x < 0)
                    return 0.3; // Wrap failure

                // Look up page in virtual page table
                uint pageEntry = _VSM_VirtualPageTable[wrapped];

                // DEBUG: Show raw page entry value
                // return pageEntry > 0 ? 1.0 : 0.0; // 1=has data, 0=empty

                // DEBUG: Show individual bits
                // return float(pageEntry & VSM_ALLOCATED_BIT) != 0 ? 1.0 : 0.0;
                // return float(pageEntry & VSM_VISIBLE_BIT) != 0 ? 1.0 : 0.0;
                // return float(pageEntry & VSM_DIRTY_BIT) != 0 ? 1.0 : 0.0;

                // Check if page is allocated
                if (!GetIsAllocated(pageEntry))
                    return 0.7; // Not allocated

                // Get physical page coordinates
                int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);

                // Calculate texel within page (pageUV is [0,1] within the page)
                float2 pageUV = frac(uv * VSM_PAGE_TABLE_RESOLUTION);

                // Convert page coordinates to physical memory coordinates
                // physicalPage is in page units, need to convert to texel units
                int2 physicalPageTexelBase = physicalPage * VSM_PAGE_SIZE;
                int2 texelWithinPage = int2(pageUV * VSM_PAGE_SIZE);
                int2 physicalPixel = physicalPageTexelBase + texelWithinPage;

                // Load shadow depth
                float shadowDepth = LoadDepth(physicalPixel);

                // Compare with current depth (use NDC depth, normalized to [0,1])
                float currentDepth = ndc.z;

                // CRITICAL: Handle platform depth differences (same as VSMDepthRender.shader)
                #if !UNITY_REVERSED_Z
                    // OpenGL: remap from [-1,1] to [0,1]
                    currentDepth = currentDepth * 0.5 + 0.5;
                #endif

                // DEBUG: Visualize depths to verify they're reasonable
                // return shadowDepth; // Should show gradient
                // return currentDepth; // Should show gradient based on distance
                // return abs(currentDepth - shadowDepth) * 100.0; // Amplify difference

                // Return shadow factor (0 = shadow, 1 = lit)
                // Standard comparison: If currentDepth > shadowDepth, point is behind blocker -> shadow
                float shadowFactor = (currentDepth - _ShadowBias) > shadowDepth ? 0.0 : 1.0;

                // DEBUG: If shadows are inverted, try reversing the comparison
                // shadowFactor = (currentDepth - _ShadowBias) < shadowDepth ? 0.0 : 1.0;

                return shadowFactor;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(UnityWorldSpaceViewDir(o.worldPos));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // DEBUG: Sample physical memory directly to verify it exists and is readable
                // float debugVal = _VSM_PhysicalMemory.Load(int3(0, 0, 0)).r; // Should be 1.0 if cleared
                // return fixed4(debugVal, debugVal, debugVal, 1);

                // Sample albedo texture
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                // Normalize normal
                float3 normal = normalize(i.worldNormal);

                // Light direction (directional light)
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

                // Diffuse lighting (Lambert)
                float ndotl = max(0, dot(normal, lightDir));

                // Sample VSM shadow
                float shadow = SampleVSMShadow(i.worldPos);

                // Combine lighting
                float3 diffuse = _LightColor0.rgb * ndotl * shadow;
                float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

                // Final color
                fixed4 finalColor;
                finalColor.rgb = albedo.rgb * (diffuse + ambient);
                finalColor.a = albedo.a;

                return finalColor;
            }
            ENDCG
        }

        // Shadow caster pass (for rendering to VSM)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
