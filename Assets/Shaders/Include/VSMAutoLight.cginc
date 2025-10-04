// Virtual Shadow Maps - Custom AutoLight replacement
// This file replaces Unity's built-in AutoLight.cginc to use VSM instead of traditional shadow maps

#ifndef VSM_AUTOLIGHT_INCLUDED
#define VSM_AUTOLIGHT_INCLUDED

#include "UnityCG.cginc"
#include "VSMCommon.hlsl"

// =============================================
// VSM Sampling Functions
// =============================================

// Virtual Page Table and Physical Memory
Texture2DArray<uint> _VSM_VirtualPageTable;
// Physical memory for sampling is float texture (copied from atomic buffer)
Texture2D<float> _VSM_PhysicalMemory;

// Cascade data
StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
StructuredBuffer<int2> _VSM_CascadeOffsets;
float _VSM_FirstCascadeSize;
float3 _VSM_CameraPosition;

// Helper function to load depth
float LoadPhysicalMemoryDepth(int2 pixel)
{
    return _VSM_PhysicalMemory.Load(int3(pixel, 0)).r;
}

// Calculate cascade level based on world position
int VSM_CalculateCascadeLevel(float3 worldPos)
{
    // Use distance-based heuristic consistent with MarkVisiblePages
    float distance = length(worldPos - _VSM_CameraPosition);
    float cascadeRadius = _VSM_FirstCascadeSize * 0.5;
    float level = max(ceil(log2(distance / cascadeRadius)), 0);
    return min((int)level, VSM_CASCADE_COUNT - 1);
}

// Sample VSM shadow at world position
float VSM_SampleShadow(float3 worldPos, float bias)
{
    // Calculate cascade level
    int cascadeLevel = VSM_CalculateCascadeLevel(worldPos);

    // Transform to light space and compute UV via perspective divide
    float4x4 lightMatrix = _VSM_CascadeLightMatrices[cascadeLevel];
    float4 lightSpacePos = mul(lightMatrix, float4(worldPos, 1.0));
    float3 ndc = lightSpacePos.xyz / lightSpacePos.w;
    float2 lightSpaceUV = ndc.xy * 0.5 + 0.5;

    // Check bounds
    if (any(lightSpaceUV < 0.0) || any(lightSpaceUV > 1.0))
        return 1.0;  // No shadow

    // Calculate page coordinates
    int3 pageCoords = int3(
        floor(lightSpaceUV * VSM_PAGE_TABLE_RESOLUTION),
        cascadeLevel
    );

    // Convert to wrapped coordinates
    int2 cascadeOffset = _VSM_CascadeOffsets[cascadeLevel];
    int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);

    if (wrappedCoords.x < 0)
        return 1.0;

    // Look up page entry
    uint pageEntry = _VSM_VirtualPageTable[wrappedCoords];

    if (!GetIsAllocated(pageEntry))
        return 1.0;  // Page not allocated, assume no shadow

    // Get physical page coordinates
    int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);

    // Calculate UV within the page
    float2 pageUV = frac(lightSpaceUV * VSM_PAGE_TABLE_RESOLUTION);

    // Calculate physical pixel coordinates
    int2 physicalPixel = physicalPageCoords * VSM_PAGE_SIZE + int2(pageUV * VSM_PAGE_SIZE);

    // Load depth from physical memory
    float shadowDepth = LoadPhysicalMemoryDepth(physicalPixel);

    // Compare depth (handle platform differences)
    float currentDepth = ndc.z;
    #if !UNITY_REVERSED_Z
        currentDepth = currentDepth * 0.5 + 0.5;
    #endif
    return (currentDepth - bias) > shadowDepth ? 0.0 : 1.0;
}

// =============================================
// Unity Lighting Macros - VSM Version
// =============================================

// Main shadow attenuation macro
#define UNITY_SHADOW_COORDS(idx1) float3 _ShadowCoord : TEXCOORD##idx1;

#define TRANSFER_SHADOW(a) a._ShadowCoord = mul(unity_ObjectToWorld, v.vertex).xyz;

#define SHADOW_COORDS(idx1) float3 _ShadowCoord : TEXCOORD##idx1;

#define TRANSFER_VERTEX_TO_FRAGMENT(a) a._ShadowCoord = mul(unity_ObjectToWorld, v.vertex).xyz;

// Shadow attenuation using VSM
inline fixed UNITY_SAMPLE_SHADOW(float3 worldPos)
{
    return VSM_SampleShadow(worldPos, 0.001);
}

inline fixed SHADOW_ATTENUATION(Input i)
{
    return VSM_SampleShadow(i._ShadowCoord, 0.001);
}

// Light attenuation (combines shadow and distance attenuation)
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
    fixed destName = VSM_SampleShadow(worldPos, 0.001);

// For vertex-lit shaders
#define LIGHTING_COORDS(idx1,idx2) float3 _ShadowCoord : TEXCOORD##idx1;

// =============================================
// Screen-space shadow macros (for compatibility)
// =============================================

#if defined(SHADOWS_SCREEN)
    #define UNITY_SHADOW_COORDS(idx1) float4 _ShadowCoord : TEXCOORD##idx1;
    #define TRANSFER_SHADOW(a) a._ShadowCoord = ComputeScreenPos(a.pos);
    #define SHADOW_ATTENUATION(a) VSM_SampleShadow(a._ShadowCoord.xyz / a._ShadowCoord.w, 0.001)
#endif

// =============================================
// Point and Spot light shadows (VSM doesn't support these yet)
// =============================================

#if defined(SHADOWS_CUBE)
    // For point lights - not implemented in VSM
    #define UNITY_SHADOW_COORDS(idx1)
    #define TRANSFER_SHADOW(a)
    #define SHADOW_ATTENUATION(a) 1.0
#endif

#if defined(SHADOWS_DEPTH)
    // For spot lights - not implemented in VSM
    #define UNITY_SHADOW_COORDS(idx1)
    #define TRANSFER_SHADOW(a)
    #define SHADOW_ATTENUATION(a) 1.0
#endif

// =============================================
// Fallback for when shadows are disabled
// =============================================

#if !defined(SHADOWS_SCREEN) && !defined(SHADOWS_DEPTH) && !defined(SHADOWS_CUBE)
    #define UNITY_SHADOW_COORDS(idx1) float3 _ShadowCoord : TEXCOORD##idx1;
    #define TRANSFER_SHADOW(a) a._ShadowCoord = mul(unity_ObjectToWorld, v.vertex).xyz;
    #define SHADOW_ATTENUATION(a) VSM_SampleShadow(a._ShadowCoord, 0.001)
#endif

// =============================================
// Additional Unity compatibility macros
// =============================================

// For surface shaders
#define INTERNAL_DATA
#define WorldReflectionVector(data,normal) reflect(data.worldRefl, normal)
#define WorldNormalVector(data,normal) normal

// Unity built-in shadow collector pass (we override this)
#ifdef SHADOW_COLLECTOR_PASS
    #undef SHADOW_COLLECTOR_PASS
#endif

#endif // VSM_AUTOLIGHT_INCLUDED
