Shader "VSM/StandardSurface"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Surface shader with VSM shadows
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        // Use our custom AutoLight instead of Unity's built-in
        #define USING_VSM_SHADOWS
        #include "../Shaders/Include/VSMAutoLight.cginc"

        sampler2D _MainTex;
        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            UNITY_SHADOW_COORDS(1)
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;

            // Metallic and smoothness
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }

        // Custom lighting to apply VSM shadows
        half4 LightingStandard_VSM(SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
        {
            // Standard PBR lighting
            half4 pbr = LightingStandard(s, viewDir, gi);
            return pbr;
        }

        inline void LightingStandard_GI_VSM(
            SurfaceOutputStandard s,
            UnityGIInput data,
            inout UnityGI gi)
        {
            LightingStandard_GI(s, data, gi);

            // Apply VSM shadow attenuation
            gi.light.color *= VSM_SampleShadow(data.worldPos, 0.001);
        }

        ENDCG
    }
    FallBack "Diffuse"
}
