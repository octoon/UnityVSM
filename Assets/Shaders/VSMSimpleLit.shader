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

            // VSM unified sampling
            #include "Include/VSMCommon.hlsl"
            #include "VSM/VSMSampling.hlsl"

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
                float shadow = SampleVSM_PCF(i.worldPos, 2.0, 0.001);

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
