#ifndef VSM_COMMON_INCLUDED
#define VSM_COMMON_INCLUDED

// Virtual Shadow Map Constants
#define VSM_PAGE_TABLE_RESOLUTION 32
#define VSM_PAGE_SIZE 128
#define VSM_VIRTUAL_TEXTURE_RESOLUTION 4096  // PAGE_TABLE_RESOLUTION * PAGE_SIZE
#define VSM_CASCADE_COUNT 16
#define VSM_PHYSICAL_MEMORY_WIDTH 8192
#define VSM_PHYSICAL_MEMORY_HEIGHT 4096

// Physical page grid dimensions (CRITICAL for coordinate mapping)
#define VSM_PHYSICAL_PAGE_COUNT_X 64  // 8192 / 128 = 64 pages wide
#define VSM_PHYSICAL_PAGE_COUNT_Y 32  // 4096 / 128 = 32 pages tall
#define VSM_MAX_PHYSICAL_PAGES 2048   // 64 * 32 = 2048 pages

// Page entry bit flags
#define VSM_ALLOCATED_BIT 0x1u
#define VSM_VISIBLE_BIT 0x2u
#define VSM_DIRTY_BIT 0x4u
#define VSM_PHYSICAL_X_SHIFT 3
#define VSM_PHYSICAL_Y_SHIFT 14
#define VSM_PHYSICAL_COORD_MASK 0x7FFu

// Page entry helper functions
bool GetIsAllocated(uint pageEntry)
{
    return (pageEntry & VSM_ALLOCATED_BIT) != 0;
}

bool GetIsVisible(uint pageEntry)
{
    return (pageEntry & VSM_VISIBLE_BIT) != 0;
}

bool GetIsDirty(uint pageEntry)
{
    return (pageEntry & VSM_DIRTY_BIT) != 0;
}

int2 UnpackPhysicalPageCoords(uint pageEntry)
{
    int x = (pageEntry >> VSM_PHYSICAL_X_SHIFT) & VSM_PHYSICAL_COORD_MASK;
    int y = (pageEntry >> VSM_PHYSICAL_Y_SHIFT) & VSM_PHYSICAL_COORD_MASK;
    return int2(x, y);
}

uint PackPageEntry(bool allocated, bool visible, bool dirty, int2 physicalCoords)
{
    uint result = 0;
    if (allocated) result |= VSM_ALLOCATED_BIT;
    if (visible) result |= VSM_VISIBLE_BIT;
    if (dirty) result |= VSM_DIRTY_BIT;
    result |= ((uint)physicalCoords.x & VSM_PHYSICAL_COORD_MASK) << VSM_PHYSICAL_X_SHIFT;
    result |= ((uint)physicalCoords.y & VSM_PHYSICAL_COORD_MASK) << VSM_PHYSICAL_Y_SHIFT;
    return result;
}

// Virtual page coordinates to wrapped coordinates (sliding window)
int3 VirtualPageCoordsToWrappedCoords(int3 pageCoords, int2 cascadeOffset)
{
    // Make sure virtual page coordinates are in bounds
    if (any(pageCoords.xy < int2(0, 0)) ||
        any(pageCoords.xy >= int2(VSM_PAGE_TABLE_RESOLUTION, VSM_PAGE_TABLE_RESOLUTION)))
    {
        return int3(-1, -1, pageCoords.z);
    }

    int2 offsetPageCoords = pageCoords.xy + cascadeOffset;

    // FIXED: Use unsigned modulo to avoid performance warning
    // Handle negative wraparound manually
    int2 wrappedPageCoords;

    // For positive values, use uint modulo (fast)
    // For negative values, add resolution until positive
    if (offsetPageCoords.x >= 0)
        wrappedPageCoords.x = ((uint)offsetPageCoords.x) % ((uint)VSM_PAGE_TABLE_RESOLUTION);
    else
        wrappedPageCoords.x = ((offsetPageCoords.x % (int)VSM_PAGE_TABLE_RESOLUTION) + VSM_PAGE_TABLE_RESOLUTION) % VSM_PAGE_TABLE_RESOLUTION;

    if (offsetPageCoords.y >= 0)
        wrappedPageCoords.y = ((uint)offsetPageCoords.y) % ((uint)VSM_PAGE_TABLE_RESOLUTION);
    else
        wrappedPageCoords.y = ((offsetPageCoords.y % (int)VSM_PAGE_TABLE_RESOLUTION) + VSM_PAGE_TABLE_RESOLUTION) % VSM_PAGE_TABLE_RESOLUTION;

    return int3(wrappedPageCoords, pageCoords.z);
}

// First heuristic from paper section 12.2.1: Pixel-perfect shadows
// Paper formula: level = max(⌈log₂(T_w / T_c₀)⌉, 0)
// where T_w is screen-space texel world size and T_c₀ is cascade 0 texel world size
int CalculateCascadeLevelPixelPerfect(float screenTexelWorldSize, float cascade0TexelWorldSize, float bias = 0.0)
{
    // Paper: "prioritizes achieving pixel-perfect shadows—a one-to-one mapping of screen pixels to shadow map texels"
    float level = max(ceil(log2(screenTexelWorldSize / cascade0TexelWorldSize) + bias), 0);
    return min((int)level, VSM_CASCADE_COUNT - 1);
}

// Second heuristic from paper section 12.2.1: Distance-based (simpler, rotationally invariant)
// Paper formula: level = max(⌈log₂(d / s_c₀)⌉, 0)
// where d is world-space distance from camera, and s_c₀ is the side length of cascade 0's frustum
int CalculateCascadeLevel(float3 worldPos, float3 cameraPos, float firstCascadeSideLength)
{
    float distance = length(worldPos - cameraPos);
    // FIXED: Cascade side length covers [-size/2, +size/2], so radius is size/2
    // A point at distance d needs cascade of size >= 2*d to be covered
    float cascadeRadius = firstCascadeSideLength * 0.5;

    // Select cascade that can contain this distance
    // Cascade k has radius = cascadeRadius * 2^k
    float level = max(ceil(log2(distance / cascadeRadius)), 0);
    return min((int)level, VSM_CASCADE_COUNT - 1);
}

// Helper to calculate screen-space texel world size for first heuristic
float GetScreenTexelWorldSize(float3 worldPos, float4x4 viewProj, float2 screenSize)
{
    // Transform world position to clip space
    float4 clipPos = mul(viewProj, float4(worldPos, 1.0));
    float3 ndc = clipPos.xyz / clipPos.w;

    // Calculate world-space size of one screen pixel at this depth
    // by unprojecting adjacent pixels
    float2 ndcOffset = float2(2.0 / screenSize.x, 0);
    float4 clipPosOffset = float4((ndc.xy + ndcOffset) * clipPos.w, ndc.z * clipPos.w, clipPos.w);

    // This is a simplification - proper implementation would unproject both positions
    float pixelWorldSize = length(clipPosOffset.xyz - clipPos.xyz) / clipPos.w;
    return pixelWorldSize;
}

// Transform world position to light space UV for a cascade
float2 WorldToLightSpaceUV(float3 worldPos, float4x4 lightMatrix)
{
    float4 lightSpacePos = mul(lightMatrix, float4(worldPos, 1.0));
    // CRITICAL: Must do perspective divide (even for ortho, w=1.0)
    float3 ndc = lightSpacePos.xyz / lightSpacePos.w;
    // NDC [-1,1] to UV [0,1]
    float2 uv = ndc.xy * 0.5 + 0.5;
    return uv;
}

// HPB Culling Functions - Paper section 12.2.2: "Hierarchical Page Culling"

// Project bounding box to light space and get min/max UV bounds
void ProjectBoundsToLightSpace(float3 boundsMin, float3 boundsMax, float4x4 lightMatrix, out float2 uvMin, out float2 uvMax)
{
    // Transform all 8 corners of the bounding box
    float3 corners[8];
    corners[0] = float3(boundsMin.x, boundsMin.y, boundsMin.z);
    corners[1] = float3(boundsMax.x, boundsMin.y, boundsMin.z);
    corners[2] = float3(boundsMin.x, boundsMax.y, boundsMin.z);
    corners[3] = float3(boundsMax.x, boundsMax.y, boundsMin.z);
    corners[4] = float3(boundsMin.x, boundsMin.y, boundsMax.z);
    corners[5] = float3(boundsMax.x, boundsMin.y, boundsMax.z);
    corners[6] = float3(boundsMin.x, boundsMax.y, boundsMax.z);
    corners[7] = float3(boundsMax.x, boundsMax.y, boundsMax.z);

    uvMin = float2(1e10, 1e10);
    uvMax = float2(-1e10, -1e10);

    for (int i = 0; i < 8; i++)
    {
        float4 lightSpacePos = mul(lightMatrix, float4(corners[i], 1.0));
        // CRITICAL: perspective divide to get NDC before mapping to [0,1]
        float3 ndc = lightSpacePos.xyz / lightSpacePos.w;
        float2 uv = ndc.xy * 0.5 + 0.5;
        uvMin = min(uvMin, uv);
        uvMax = max(uvMax, uv);
    }
}

// Calculate HPB mip level - Paper: "select the level in which the bounding-box bounds intersect exactly four texels"
// This is similar to Hi-Z culling: we want the mip level where the box covers approximately 2x2 texels
int CalculateHPBMipLevel(float2 uvMin, float2 uvMax, int hpbMaxLevel)
{
    // Convert UV bounds to texel space at base resolution (page table resolution)
    float2 texelMin = uvMin * VSM_PAGE_TABLE_RESOLUTION;
    float2 texelMax = uvMax * VSM_PAGE_TABLE_RESOLUTION;
    float2 texelSize = texelMax - texelMin;

    // Paper: "select the level in which the bounding-box bounds intersect exactly four texels"
    // We want to find mip level where box spans ~2 texels per dimension
    // At level L, resolution is (PAGE_TABLE_RES >> L), so we want:
    // texelSize / (1 << level) ≈ 2.0
    // Therefore: level = log2(texelSize / 2.0)

    float maxDimension = max(texelSize.x, texelSize.y);

    // Choose level where maxDimension at that level ≈ 2
    // If maxDimension is 8 at level 0, we want level 2 (8/4 = 2)
    int level = max(0, (int)ceil(log2(maxDimension / 2.0)));
    level = min(level, hpbMaxLevel);

    return level;
}

// Check if bounding box intersects any dirty pages in HPB
// Paper: "if any of the four intersected texels is marked as dirty, the meshlet survives culling"
bool HPBCullTest(float2 uvMin, float2 uvMax, int cascadeIndex, Texture2DArray<float> hpb, int hpbMaxLevel)
{
    // Select appropriate mip level
    int mipLevel = CalculateHPBMipLevel(uvMin, uvMax, hpbMaxLevel);

    // Calculate resolution at this mip level
    int resolution = VSM_PAGE_TABLE_RESOLUTION >> mipLevel;

    // Convert UV to texel coordinates at this mip level
    int2 texelMin = int2(floor(uvMin * resolution));
    int2 texelMax = int2(ceil(uvMax * resolution));

    // Clamp to valid range
    texelMin = clamp(texelMin, int2(0, 0), int2(resolution - 1, resolution - 1));
    texelMax = clamp(texelMax, int2(0, 0), int2(resolution - 1, resolution - 1));

    // Check all intersected texels (typically 4, but could be more)
    for (int y = texelMin.y; y <= texelMax.y; y++)
    {
        for (int x = texelMin.x; x <= texelMax.x; x++)
        {
            float dirtyFlag = hpb[int3(x, y, cascadeIndex)];
            if (dirtyFlag > 0.5)  // Page is dirty
            {
                return true;  // Survives culling
            }
        }
    }

    return false;  // Culled
}

#endif // VSM_COMMON_INCLUDED
