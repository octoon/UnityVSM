Shader "VSM/ForwardLitDebug"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _ShadowBias ("Shadow Bias", Range(0, 0.01)) = 0.001

        [Header(Debug Visualization)]
        [KeywordEnum(Off, Shadow, Cascade, PageAllocation)] _DebugMode("Debug Mode", Float) = 0
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
            #pragma multi_compile _DEBUGMODE_OFF _DEBUGMODE_SHADOW _DEBUGMODE_CASCADE _DEBUGMODE_PAGEALLOCATION
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "Include/VSMCommon.hlsl"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ShadowBias;

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

            int CalculateCascade(float3 worldPos)
            {
                float dist = length(worldPos - _VSM_CameraPosition);
                int level = (int)max(0, ceil(log2(dist / _VSM_FirstCascadeSize)));
                return min(level, VSM_CASCADE_COUNT - 1);
            }

            // 返回值: x = shadow, y = cascade, z = allocated
            float3 SampleVSMWithDebug(float3 worldPos)
            {
                int cascade = CalculateCascade(worldPos);
                float4x4 lightMat = _VSM_CascadeLightMatrices[cascade];
                float4 lightSpace = mul(lightMat, float4(worldPos, 1.0));
                float3 ndc = lightSpace.xyz / lightSpace.w;
                float2 uv = ndc.xy * 0.5 + 0.5;

                if (any(uv < 0.0) || any(uv > 1.0))
                    return float3(1, cascade, 0);

                int3 pageCoords = int3(floor(uv * VSM_PAGE_TABLE_RESOLUTION), cascade);
                int2 offset = _VSM_CascadeOffsets[cascade];
                int3 wrapped = VirtualPageCoordsToWrappedCoords(pageCoords, offset);

                if (wrapped.x < 0)
                    return float3(1, cascade, 0);

                uint pageEntry = _VSM_VirtualPageTable[wrapped];
                bool allocated = GetIsAllocated(pageEntry);

                if (!allocated)
                    return float3(1, cascade, 0);

                int2 physicalPage = UnpackPhysicalPageCoords(pageEntry);
                float2 pageUV = frac(uv * VSM_PAGE_TABLE_RESOLUTION);
                int2 physicalPixel = physicalPage * VSM_PAGE_SIZE + int2(pageUV * VSM_PAGE_SIZE);

                float shadowDepth = LoadDepth(physicalPixel);
                float currentDepth = ndc.z;
                #if !UNITY_REVERSED_Z
                    currentDepth = currentDepth * 0.5 + 0.5;
                #endif
                float shadow = (currentDepth - _ShadowBias) > shadowDepth ? 0.0 : 1.0;

                return float3(shadow, cascade, allocated ? 1 : 0);
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
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float ndotl = max(0, dot(normal, lightDir));

                // Sample VSM with debug info
                float3 vsmDebug = SampleVSMWithDebug(i.worldPos);
                float shadow = vsmDebug.x;
                int cascade = (int)vsmDebug.y;
                bool allocated = vsmDebug.z > 0.5;

                // Debug visualization
                #if defined(_DEBUGMODE_SHADOW)
                    // 显示阴影：黑色=阴影，白色=无阴影
                    return fixed4(shadow.xxx, 1);

                #elif defined(_DEBUGMODE_CASCADE)
                    // 显示Cascade层级：不同颜色
                    float3 cascadeColors[8] = {
                        float3(1, 0, 0),    // Cascade 0: 红色
                        float3(0, 1, 0),    // Cascade 1: 绿色
                        float3(0, 0, 1),    // Cascade 2: 蓝色
                        float3(1, 1, 0),    // Cascade 3: 黄色
                        float3(1, 0, 1),    // Cascade 4: 品红
                        float3(0, 1, 1),    // Cascade 5: 青色
                        float3(1, 0.5, 0),  // Cascade 6: 橙色
                        float3(0.5, 0, 1)   // Cascade 7: 紫色
                    };
                    return fixed4(cascadeColors[cascade % 8], 1);

                #elif defined(_DEBUGMODE_PAGEALLOCATION)
                    // 显示页面分配：绿色=已分配，红色=未分配
                    return allocated ? fixed4(0, 1, 0, 1) : fixed4(1, 0, 0, 1);

                #else
                    // 正常渲染
                    float3 diffuse = _LightColor0.rgb * ndotl * shadow;
                    float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;

                    fixed4 finalColor;
                    finalColor.rgb = albedo.rgb * (diffuse + ambient);
                    finalColor.a = albedo.a;
                    return finalColor;
                #endif
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
