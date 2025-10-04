Shader "VSM/TestWorldReconstruction"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"

            Texture2D<float> _CameraDepthTexture;
            float4x4 _CameraInverseViewProjection;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 screenUV : TEXCOORD1;
            };

            float3 ReconstructWorldPosition(float2 uv, float depth)
            {
                #if UNITY_REVERSED_Z
                    // DX12: already correct
                #else
                    depth = depth * 2.0 - 1.0;
                #endif

                float4 clipPos = float4(uv * 2.0 - 1.0, depth, 1.0);
                clipPos.y = -clipPos.y;
                float4 worldPos = mul(_CameraInverseViewProjection, clipPos);
                return worldPos.xyz / worldPos.w;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenUV = ComputeScreenPos(o.pos).xy / ComputeScreenPos(o.pos).w;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Read depth and reconstruct
                float depth = _CameraDepthTexture.SampleLevel(sampler_CameraDepthTexture, i.screenUV, 0);
                float3 reconstructed = ReconstructWorldPosition(i.screenUV, depth);

                // Compare with actual world position
                float3 diff = reconstructed - i.worldPos;

                // Visualize error
                float error = length(diff);

                // Color: green if close, red if far
                if (error < 0.1) return fixed4(0, 1, 0, 1); // Green = correct
                if (error < 1.0) return fixed4(1, 1, 0, 1); // Yellow = small error
                return fixed4(1, 0, 0, 1); // Red = large error
            }
            ENDCG
        }
    }
}
