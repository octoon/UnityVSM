# Virtual Shadow Maps (VSM) Unity Implementation

This project is an implementation of Virtual Shadow Maps based on the GPU Zen 3 paper "Virtual Shadow Maps" by Matej Sakmary, Jake Ryan, Justin Hall, and Alessio Lustri.

## Overview

Virtual Shadow Maps is a shadow mapping technique that uses virtual texturing to efficiently handle very high-resolution shadow maps with minimal memory overhead. It addresses both perspective aliasing and projective aliasing issues common in traditional shadow mapping.

## Key Features

### Core Implementation
- **Virtual Page Table (VPT)**: Maps virtual shadow map pages to physical memory
- **Physical Page Table (PPT)**: Inverse mapping from physical to virtual pages
- **16 Cascades**: 4096×4096 resolution per cascade, organized concentrically
- **Page-based Allocation**: 128×128 texel pages, allocated on-demand
- **Page Caching**: Persistent pages across frames with sliding window addressing

### Three-Phase Pipeline

#### 1. Bookkeeping Phase
- **Free Invalidated Pages**: Clears pages affected by light movement or dynamic objects
- **Mark Visible Pages**: Analyzes camera depth buffer to determine visible shadow pages
- **Page Allocation**: Assigns physical memory to visible virtual pages
- **Clear Pages**: Prepares newly allocated pages for rendering

#### 2. Drawing Phase
- **Hierarchical Page Buffer (HPB)**: Multi-level culling structure for efficient geometry processing
- **Depth Rendering**: Renders shadow casters to allocated pages
- **Atomic Min Operations**: Thread-safe depth writes to physical memory

#### 3. Sampling Phase
- **Cascade Selection**: Distance-based heuristic for optimal shadow detail
- **Virtual to Physical Mapping**: Translates sample coordinates through page tables
- **PCF Filtering**: Soft shadow support with percentage-closer filtering

## Project Structure

```
VSMUnityProject/
├── Assets/
│   ├── Scripts/
│   │   ├── VSM/
│   │   │   ├── VSMConstants.cs              # Core constants and bit packing
│   │   │   ├── VSMPageTable.cs              # Virtual page table management
│   │   │   ├── VSMPhysicalPageTable.cs      # Physical page table (in VSMPageTable.cs)
│   │   │   ├── VSMPhysicalMemory.cs         # Physical shadow memory texture
│   │   │   ├── VSMHierarchicalPageBuffer.cs # HPB for culling
│   │   │   └── VirtualShadowMapManager.cs   # Main orchestration component
│   │   └── Utils/
│   │       └── SimpleController.cs          # Camera controller for testing
│   └── Shaders/
│       ├── Include/
│       │   └── VSMCommon.hlsl               # Shared shader utilities
│       └── VSM/
│           ├── VSMFreeInvalidatedPages.compute  # Bookkeeping: free pages
│           ├── VSMMarkVisiblePages.compute      # Bookkeeping: mark visible
│           ├── VSMAllocatePages.compute         # Bookkeeping: allocation
│           ├── VSMClearPages.compute            # Bookkeeping: clear
│           ├── VSMBuildHPB.compute              # Build hierarchical culling buffer
│           ├── VSMDepthRender.shader            # Depth rendering to pages
│           ├── VSMSampling.hlsl                 # Shadow sampling functions
│           └── VSMShadowReceiver.shader         # Example lit shader with shadows
```

## Technical Details

### Memory Layout

**Virtual Space:**
- 16 cascades × 4096×4096 pixels = ~268 million virtual texels
- Each cascade is 2× the size of the previous (first cascade: 2m, last: 65km)

**Physical Memory:**
- 2048 pages maximum (64×32 grid)
- 8192×4096 physical texture (R32_Float)
- Only visible pages consume memory (~128KB per page)

**Page Entry Format (32-bit):**
- Bit 0: Allocated flag
- Bit 1: Visible flag
- Bit 2: Dirty flag
- Bits 3-13: Physical page X coordinate (11 bits)
- Bits 14-24: Physical page Y coordinate (11 bits)

### Cascade Selection Heuristics

**Distance-based (Implemented):**
```
level = max(ceil(log2(distance / firstCascadeSize)), 0)
```

**Screen-space (Alternative):**
```
level = max(ceil(log2(texelWorldSize / cascade0TexelSize)), 0)
```

### Page Caching with Sliding Window

To maintain cache coherency when the camera moves:
1. Cascade frustums snap to page grid
2. Light position slides parallel to near-plane
3. Virtual coordinates wrap around using modulo arithmetic
4. Cached pages remain valid across frames

## Setup Instructions

### Unity Version
- Unity 2021.3 or later recommended
- Requires compute shader support (Shader Model 5.0+)

### Steps to Use

1. **Import Project**: Copy the `VSMUnityProject` folder into your Unity projects directory

2. **Create Scene**:
   - Create a new scene
   - Add a Camera with `VirtualShadowMapManager` component
   - Add a Directional Light
   - Create ground plane and test objects

3. **Configure Manager**:
   - Assign Directional Light reference
   - Assign compute shaders to respective slots
   - Set first cascade size (default: 2.0m)
   - Configure shadow caster layer mask

4. **Apply Materials**:
   - Use `VSM/ShadowReceiver` shader on objects receiving shadows
   - Configure shadow strength and appearance

5. **Assign Shaders**:
   - Assign all compute shaders from `Assets/Shaders/VSM/` to the manager
   - Assign `VSMDepthRender.shader` for depth rendering

## Performance Considerations

### Optimizations
- **Page Caching**: Reduces per-frame rendering by reusing stable pages
- **HPB Culling**: Skips geometry that doesn't intersect dirty pages
- **On-demand Allocation**: Only visible pages consume memory
- **Atomic Operations**: Lock-free depth writes for parallel rendering

### Scalability
- Cascade count can be reduced for lower-end hardware
- Page size can be adjusted (64×64 or 256×256)
- Physical memory pool can be tuned based on VRAM budget

## Known Limitations

1. **Simplified Allocation**: The allocation system uses a simplified hash-based approach rather than proper buffer consumption
2. **No Mesh Shader Support**: The paper uses mesh shaders for culling; this implementation uses compute shaders
3. **Limited Dynamic Object Tracking**: Dynamic object invalidation masks are stubbed
4. **No PCSS**: Only basic PCF filtering is implemented

## Future Enhancements

- [ ] Proper physical page allocation with consume buffers
- [ ] Mesh shader-based rendering path
- [ ] Dynamic object tracking and invalidation
- [ ] PCSS (Percentage-Closer Soft Shadows)
- [ ] Multi-light support
- [ ] Debug visualization modes (cascade levels, page states)
- [ ] Performance profiling and optimization passes

## References

1. GPU Zen 3: "Virtual Shadow Maps" - Matej Sakmary, Jake Ryan, Justin Hall, Alessio Lustri
2. Unreal Engine 5 Documentation - Virtual Shadow Maps
3. "Cascaded Shadow Maps" - Wolfgang Engel, 2007
4. "Sample Distribution Shadow Maps" - Andrew Lauritzen et al., 2011
5. "Virtual Texturing" - Sean Barrett, 2008

## License

This implementation is provided for educational purposes. Please refer to the original paper for detailed algorithmic descriptions.

## Contact

For issues or questions about this implementation, please refer to the GPU Zen 3 book or Unreal Engine 5 documentation for algorithmic details.
