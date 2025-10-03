#ifndef VSM_SAMPLING_INCLUDED
#define VSM_SAMPLING_INCLUDED

#include "../Include/VSMCommon.hlsl"

// Virtual Page Table and Physical Memory
Texture2DArray<uint> _VSM_VirtualPageTable;
Texture2D<uint> _VSM_PhysicalMemory;  // Stored as uint for atomic operations (use Load, not Sample)

// Helper function to load and convert depth from uint to float
float LoadPhysicalMemoryDepth(int2 pixel)
{
    uint depthUint = _VSM_PhysicalMemory.Load(int3(pixel, 0)).r;
    return asfloat(depthUint);
}

// Manual bilinear filtering for uint texture
float SamplePhysicalMemoryBilinear(float2 uv)
{
    float2 texSize = float2(VSM_PHYSICAL_MEMORY_WIDTH, VSM_PHYSICAL_MEMORY_HEIGHT);
    float2 pixelCoord = uv * texSize - 0.5;
    int2 pixelInt = int2(floor(pixelCoord));
    float2 frac = pixelCoord - pixelInt;

    float d00 = LoadPhysicalMemoryDepth(pixelInt + int2(0, 0));
    float d10 = LoadPhysicalMemoryDepth(pixelInt + int2(1, 0));
    float d01 = LoadPhysicalMemoryDepth(pixelInt + int2(0, 1));
    float d11 = LoadPhysicalMemoryDepth(pixelInt + int2(1, 1));

    float d0 = lerp(d00, d10, frac.x);
    float d1 = lerp(d01, d11, frac.x);
    return lerp(d0, d1, frac.y);
}

// Cascade data
StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
StructuredBuffer<int2> _VSM_CascadeOffsets;
float _VSM_FirstCascadeSize;
float3 _VSM_CameraPosition;

// Sample VSM at world position
float SampleVSM(float3 worldPos, float bias = 0.001)
{
    // Calculate cascade level
    int cascadeLevel = CalculateCascadeLevel(worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);

    // Transform to light space
    float4x4 lightMatrix = _VSM_CascadeLightMatrices[cascadeLevel];
    float4 lightSpacePos = mul(lightMatrix, float4(worldPos, 1.0));
    float2 lightSpaceUV = lightSpacePos.xy * 0.5 + 0.5;

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
    int2 physicalPixel = int2((physicalPageCoords + pageUV) * VSM_PAGE_SIZE);

    // Load depth from physical memory
    float shadowDepth = LoadPhysicalMemoryDepth(physicalPixel);

    // Compare depth
    float currentDepth = lightSpacePos.z;
    return (currentDepth - bias) > shadowDepth ? 0.0 : 1.0;
}

// PCF filtering for softer shadows
float SampleVSM_PCF(float3 worldPos, float filterSize = 2.0, float bias = 0.001)
{
    // Calculate cascade level
    int cascadeLevel = CalculateCascadeLevel(worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);

    // Transform to light space
    float4x4 lightMatrix = _VSM_CascadeLightMatrices[cascadeLevel];
    float4 lightSpacePos = mul(lightMatrix, float4(worldPos, 1.0));
    float2 lightSpaceUV = lightSpacePos.xy * 0.5 + 0.5;

    if (any(lightSpaceUV < 0.0) || any(lightSpaceUV > 1.0))
        return 1.0;

    float shadow = 0.0;
    float totalWeight = 0.0;

    // Texel size in virtual space
    float texelSize = 1.0 / VSM_VIRTUAL_TEXTURE_RESOLUTION;

    // PCF kernel (3x3)
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y) * texelSize * filterSize;
            float2 sampleUV = lightSpaceUV + offset;

            // Calculate page coordinates for this sample
            int3 pageCoords = int3(
                floor(sampleUV * VSM_PAGE_TABLE_RESOLUTION),
                cascadeLevel
            );

            int2 cascadeOffset = _VSM_CascadeOffsets[cascadeLevel];
            int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);

            if (wrappedCoords.x < 0)
                continue;

            uint pageEntry = _VSM_VirtualPageTable[wrappedCoords];
            if (!GetIsAllocated(pageEntry))
                continue;

            int2 physicalPageCoords = UnpackPhysicalPageCoords(pageEntry);
            float2 pageUV = frac(sampleUV * VSM_PAGE_TABLE_RESOLUTION);

            int2 physicalPixel = int2((physicalPageCoords + pageUV) * VSM_PAGE_SIZE);
            float shadowDepth = LoadPhysicalMemoryDepth(physicalPixel);
            float currentDepth = lightSpacePos.z;

            shadow += (currentDepth - bias) > shadowDepth ? 0.0 : 1.0;
            totalWeight += 1.0;
        }
    }

    return totalWeight > 0.0 ? shadow / totalWeight : 1.0;
}

#endif // VSM_SAMPLING_INCLUDED
