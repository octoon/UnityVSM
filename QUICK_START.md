# Virtual Shadow Maps - Quick Start Guide

## What You Need

- Unity 2021.3 or later
- GPU with compute shader support (DX11+, Vulkan, Metal)
- Basic understanding of Unity and C#

## Setup (5 minutes)

### 1. Create a New Scene

```
File > New Scene > 3D (URP or Built-in)
```

### 2. Setup Camera

1. Select Main Camera
2. Add Component: `Virtual Shadow Map Manager`
3. Add Component: `Simple Controller` (for movement)

### 3. Setup Light

1. Create Directional Light (if not exists)
2. Drag into `Directional Light` field on VSM Manager
3. Rotate to desired angle (e.g., 45 degrees)

### 4. Assign Compute Shaders

In VSM Manager component, assign:
- **Free Invalidated Pages**: `VSMFreeInvalidatedPages.compute`
- **Mark Visible Pages**: `VSMMarkVisiblePages.compute`
- **Allocate Pages**: `VSMAllocatePages.compute`
- **Clear Pages**: `VSMClearPages.compute`
- **Build HPB**: `VSMBuildHPB.compute`
- **VSM Depth Shader**: `VSMDepthRender.shader`

### 5. Create Test Scene

#### Ground Plane
```
GameObject > 3D Object > Plane
Scale: (10, 1, 10)
Material: Create new material with VSM/ShadowReceiver shader
```

#### Test Objects
```
GameObject > 3D Object > Cube
Position: (0, 0.5, 0)
Material: Create new material with VSM/ShadowReceiver shader

GameObject > 3D Object > Sphere
Position: (2, 1, 0)
Material: Use same VSM shadow receiver material
```

### 6. Configure Materials

For each material using VSM shadows:
1. Shader: `VSM/ShadowReceiver`
2. Set base color/texture
3. Adjust Shadow Strength (0.5 default)

### 7. Test

1. Press Play
2. Use WASD to move, QE for up/down
3. Hold right mouse button and move mouse to look around
4. Observe real-time shadows updating

## Expected Results

✓ Smooth, high-quality shadows
✓ No visible cascades (seamless transition)
✓ Minimal aliasing artifacts
✓ Good performance (3-7ms overhead)

## Troubleshooting

### No Shadows Visible

**Problem**: Scene is lit but no shadows appear

**Solutions**:
1. Check Directional Light is assigned in VSM Manager
2. Ensure materials use `VSM/ShadowReceiver` shader
3. Verify all compute shaders are assigned
4. Check Console for errors

### Black/Pink Materials

**Problem**: Objects render as black or pink

**Solutions**:
1. Verify shader compiles without errors
2. Check all texture samplers are correctly named
3. Ensure compute shaders have required kernels
4. Try reimporting shaders

### Poor Performance

**Problem**: Frame rate drops significantly

**Solutions**:
1. Reduce cascade count in `VSMConstants.cs`
2. Increase page size (256×256 instead of 128×128)
3. Reduce physical page pool size
4. Check GPU compute capability

### Shadow Artifacts

**Problem**: Flickering or incorrect shadows

**Solutions**:
1. Increase shadow bias in sampling (default 0.001)
2. Adjust first cascade size (try 1.0 or 4.0)
3. Enable debug visualization to check cascade selection
4. Verify page allocation is working correctly

## Controls

- **W/A/S/D**: Move forward/left/back/right
- **Q/E**: Move down/up
- **Right Mouse + Move**: Look around
- **Left Shift**: Sprint (faster movement)

## Next Steps

### Experiment with Settings

1. **First Cascade Size** (VSM Manager):
   - Smaller (0.5-1.0): Better close-up detail
   - Larger (4.0-8.0): Better distant shadows

2. **Shadow Strength** (Material):
   - 0.0: No shadows
   - 1.0: Fully black shadows
   - 0.5: Realistic soft shadows

3. **Filter Size** (Edit `VSMSampling.hlsl`):
   - Increase for softer shadows
   - Decrease for sharper shadows

### Add Dynamic Objects

1. Create objects with Rigidbodies
2. Watch shadows update in real-time
3. Observe page caching efficiency

### Debug Visualization

1. Enable `Debug Visualization` in VSM Manager
2. See memory usage and cascade info
3. Create custom visualizations:
   - Cascade selection overlay
   - Page state heatmap
   - Physical memory view

## Performance Tuning

### For Lower-End GPUs

Edit `VSMConstants.cs`:
```csharp
public const int CASCADE_COUNT = 8;  // Reduce from 16
public const int PAGE_SIZE = 256;    // Increase from 128
public const int MAX_PHYSICAL_PAGES = 1024;  // Reduce from 2048
```

### For Higher-End GPUs

Edit `VSMConstants.cs`:
```csharp
public const int PAGE_TABLE_RESOLUTION = 64;  // Increase from 32
public const int FIRST_CASCADE_SIZE = 1.0f;   // Decrease for more detail
```

## Common Customizations

### Change Shadow Color

In `VSMShadowReceiver.shader`:
```hlsl
// Replace shadow attenuation line with:
float3 shadowColor = float3(0.3, 0.3, 0.5);  // Blueish shadows
float shadowAttenuation = lerp(shadowColor, 1.0, shadow);
```

### Add Ambient Occlusion

Combine VSM with screen-space AO for enhanced depth perception.

### Multi-Pass Rendering

Use VSM for primary directional light, traditional shadow maps for additional lights.

## Architecture Overview

```
VirtualShadowMapManager (Main Component)
├── VSMPageTable (Virtual page tracking)
├── VSMPhysicalPageTable (Physical page tracking)
├── VSMPhysicalMemory (Shadow depth storage)
└── VSMHierarchicalPageBuffer (Culling optimization)

Pipeline:
1. Bookkeeping → Mark visible pages, allocate memory
2. Drawing     → Render depth to allocated pages
3. Sampling    → Read shadows in materials
```

## Learn More

- Read `README.md` for complete technical details
- Study `IMPLEMENTATION_NOTES.md` for paper analysis
- Examine shader code for low-level implementation
- Check GPU Zen 3 book for original algorithm

## Support

For issues with this implementation:
1. Check console for errors
2. Verify all assets are properly assigned
3. Review shader compilation logs
4. Test on different hardware if possible

For questions about the algorithm:
1. Refer to GPU Zen 3 paper
2. Check Unreal Engine 5 documentation
3. Study cascaded shadow map techniques

## Credits

Implementation based on GPU Zen 3 "Virtual Shadow Maps" paper by:
- Matej Sakmary
- Jake Ryan
- Justin Hall
- Alessio Lustri

Inspired by Unreal Engine 5's VSM implementation.

---

**Ready to explore Virtual Shadow Maps? Press Play and enjoy high-quality real-time shadows!**
