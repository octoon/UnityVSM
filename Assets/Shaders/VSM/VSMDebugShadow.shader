Shader "VSM/DebugShadow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _DebugMode ("Debug Mode", Float) = 0
        // 0 = Normal shadow
        // 1 = Show cascade index
        // 2 = Show shadow depth
        // 3 = Show current depth
        // 4 = Show depth difference
        // 5 = Show page allocation
        // 6 = Show UV coords
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "../Include/VSMCommon.hlsl"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _DebugMode;

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

            float LoadDepth(int2 pixel)
            {
                return _VSM_PhysicalMemory.Load(int3(pixel, 0)).r;
            }

            float4 DebugVSM(float3 worldPos, int mode)
            {
                // CRITICAL: Use same cascade selection as MarkVisiblePages
                int cascade = CalculateCascadeLevel(worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);

                // Mode 1: Show cascade index
                if (mode == 1)
                {
                    // 使用颜色编码cascade
                    float3 colors[4] = {
                        float3(1, 0, 0),  // Red
                        float3(0, 1, 0),  // Green
                        float3(0, 0, 1),  // Blue
                        float3(1, 1, 0)   // Yellow
                    };
                    return float4(colors[cascade % 4], 1);
                }

                // Transform to light space
                float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                float4 lightSpace = mul(lightMat, float4(worldPos, 1.0));
                float3 ndc = lightSpace.xyz / lightSpace.w;
                float2 uv = ndc.xy * 0.5 + 0.5;

                // Mode 6: Show UV coords
                if (mode == 6)
                {
                    return float4(uv, 0, 1);
                }

                if (any(uv < 0.0) || any(uv > 1.0))
                    return float4(1, 0, 1, 1); // Magenta = out of bounds

                int3 pageCoords = int3(
                    floor(uv * VSM_PAGE_TABLE_RESOLUTION),
                    cascade
                );

                int2 offset = _VSM_CascadeOffsets[cascade];
                int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                if (wrapped.x < 0)
                    return float4(1, 0.5, 0, 1); // Orange = wrap failure

                uint pageEntry = _VSM_VirtualPageTable[wrapped];

                // Mode 5: Show page allocation
                if (mode == 5)
                {
                    bool allocated = GetIsAllocated(pageEntry);
                    bool visible = GetIsVisible(pageEntry);
                    bool dirty = GetIsDirty(pageEntry);

                    if (!allocated) return float4(0, 0, 0, 1); // Black = not allocated
                    if (dirty) return float4(1, 0, 0, 1); // Red = dirty
                    if (visible) return float4(1, 1, 0, 1); // Yellow = visible
                    return float4(0, 1, 0, 1); // Green = allocated
                }

                if (!GetIsAllocated(pageEntry))
                    return float4(0.7, 0.7, 0.7, 1); // Gray = not allocated

                int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                float2 pageUV = frac(uv * VSM_PAGE_TABLE_RESOLUTION);
                int2 physicalPageTexelBase = physicalPage * VSM_PAGE_SIZE;
                int2 texelWithinPage = int2(pageUV * VSM_PAGE_SIZE);
                int2 physicalPixel = physicalPageTexelBase + texelWithinPage;

                float shadowDepth = LoadDepth(physicalPixel);
                float currentDepth = ndc.z;

                #if !UNITY_REVERSED_Z
                    currentDepth = currentDepth * 0.5 + 0.5;
                #endif

                // Mode 2: Show shadow depth
                if (mode == 2)
                {
                    return float4(shadowDepth, shadowDepth, shadowDepth, 1);
                }

                // Mode 3: Show current depth
                if (mode == 3)
                {
                    return float4(currentDepth, currentDepth, currentDepth, 1);
                }

                // Mode 4: Show depth difference (amplified)
                if (mode == 4)
                {
                    float diff = abs(currentDepth - shadowDepth) * 100.0;
                    return float4(diff, diff, diff, 1);
                }

                // Mode 0: Normal shadow
                float shadowFactor = (currentDepth - 0.001) > shadowDepth ? 0.0 : 1.0;
                return float4(shadowFactor, shadowFactor, shadowFactor, 1);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int mode = (int)_DebugMode;

                if (mode > 0)
                {
                    return DebugVSM(i.worldPos, mode);
                }

                // Normal rendering with shadows
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndotl = max(0, dot(normal, lightDir));

                float4 shadowDebug = DebugVSM(i.worldPos, 0);
                float shadow = shadowDebug.r;

                float3 diffuse = _LightColor0.rgb * ndotl * shadow;
                float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

                fixed4 finalColor;
                finalColor.rgb = albedo.rgb * (diffuse + ambient);
                finalColor.a = albedo.a;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
