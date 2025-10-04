Shader "VSM/ShowUV"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "../Include/VSMCommon.hlsl"

            StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
            StructuredBuffer<int2> _VSM_CascadeOffsets;
            float _VSM_FirstCascadeSize;
            float3 _VSM_CameraPosition;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Use same cascade selection as MarkVisiblePages
                int cascade = CalculateCascadeLevel(i.worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);

                // Method 1: Using WorldToLightSpaceUV (what MarkVisiblePages uses)
                float4x4 lightMatrix = _VSM_CascadeLightMatrices[cascade];
                float2 uvMethod1 = WorldToLightSpaceUV(i.worldPos, lightMatrix);

                // Method 2: Manual calculation (what shaders should use)
                float4 lightSpace = mul(lightMatrix, float4(i.worldPos, 1.0));
                float3 ndc = lightSpace.xyz / lightSpace.w;
                float2 uvMethod2 = ndc.xy * 0.5 + 0.5;

                // Show difference
                float2 diff = abs(uvMethod1 - uvMethod2);

                // Visualize: Red channel = uvMethod1.x, Green = uvMethod2.x
                // If they match, should see yellow (red+green)
                return fixed4(uvMethod1.x, uvMethod2.x, diff.x * 100, 1);
            }
            ENDCG
        }
    }
}
