Shader "VSM/ShadowReceiver"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "ForwardBase"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "VSMSampling.hlsl"

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

            float3 _DirectionalLightDir;
            float4 _DirectionalLightColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Sample base texture
                float4 col = tex2D(_MainTex, i.uv) * _Color;

                // Calculate lighting
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_DirectionalLightDir);
                float ndotl = max(0, dot(normal, -lightDir));

                // Sample VSM shadow with PCF
                float shadow = SampleVSM_PCF(i.worldPos, 2.0, 0.001);

                // Apply shadow
                float shadowAttenuation = lerp(1.0 - _ShadowStrength, 1.0, shadow);

                // Final color
                float3 lighting = _DirectionalLightColor.rgb * ndotl * shadowAttenuation;
                col.rgb *= lighting + float3(0.2, 0.2, 0.2);  // Add ambient

                return col;
            }
            ENDHLSL
        }
    }
}
