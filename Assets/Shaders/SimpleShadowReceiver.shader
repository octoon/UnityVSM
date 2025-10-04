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
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
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

            // VSM: use common includes and sampling helpers
            #include "Include/VSMCommon.hlsl"
            #include "VSM/VSMSampling.hlsl"

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

                // Sample VSM shadow (PCF for smoother result)
                float shadow = SampleVSM_PCF(i.worldPos, 2.0, 0.001);

                // Apply shadow attenuation
                float shadowAttenuation = lerp(1.0 - _ShadowStrength, 1.0, shadow);

                // Calculate final lighting
                float3 lighting = _DirectionalLightColor.rgb * ndotl * shadowAttenuation;
                float3 ambient = float3(0.3, 0.3, 0.3);

                float4 col = _Color;
                col.rgb *= lighting + ambient;
                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Diffuse"
}
