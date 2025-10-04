# Virtual Shadow Maps (VSM) 空间映射公式整理

基于 GPU Zen 3 论文 "Virtual Shadow Maps" 的公式整理

## 1. Cascade级联选择启发式

### 1.1 第一种启发式: 像素完美（Pixel-Perfect）

**公式:**
```
level = max(⌈log₂(T_w / T_c₀)⌉, 0)
```

**参数说明:**
- `T_w`: 屏幕空间纹素的世界空间尺寸
- `T_c₀`: cascade 0 纹素的世界空间尺寸
- `level`: 选中的cascade级别

**目标:** 实现屏幕像素到阴影贴图纹素的一对一映射

**优点:** 理论上最优的cascade选择
**缺点:** 可能会选择超出cascade frustum的像素，需要bias偏移

**代码实现位置:** `VSMCommon.hlsl:86-91`

### 1.2 第二种启发式: 距离基础（Distance-Based, 旋转不变）

**公式:**
```
level = max(⌈log₂(d / s_c₀)⌉, 0)
```

**参数说明:**
- `d`: 世界空间中点到相机的距离
- `s_c₀`: cascade 0 frustum的边长
- `level`: 选中的cascade级别

**优点:**
- 旋转不变性（相机旋转时不会使缓存页失效）
- 更简单，不会越界

**代码实现位置:** `VSMCommon.hlsl:96-103`

---

## 2. 空间坐标转换公式

### 2.1 世界空间 → 光源空间UV

**公式:**
```
lightSpacePos = lightMatrix × worldPos
ndc = lightSpacePos.xyz / lightSpacePos.w
uv = ndc.xy × 0.5 + 0.5
```

**参数说明:**
- `worldPos`: 世界空间位置 (vec4, w=1)
- `lightMatrix`: 光源的ViewProjection矩阵
- `ndc`: 规范化设备坐标 (Normalized Device Coordinates, [-1,1])
- `uv`: 光源空间UV坐标 [0,1]

**代码实现位置:** `VSMCommon.hlsl:123-131`

### 2.2 光源空间UV → 虚拟页坐标

**公式:**
```
pageCoords = ⌊uv × PAGE_TABLE_RESOLUTION⌋
```

**参数说明:**
- `uv`: 光源空间UV [0,1]
- `PAGE_TABLE_RESOLUTION`: 页表分辨率 (通常为32)
- `pageCoords`: 虚拟页坐标 (int2, 范围[0, 31])

**代码实现位置:** `VSMSampling.hlsl:57-60`, `VSMMarkVisiblePages.compute:93`

### 2.3 虚拟页坐标 → 包裹坐标（Sliding Window）

**公式:**
```
offsetPageCoords = pageCoords + cascadeOffset
wrappedPageCoords.x = offsetPageCoords.x mod PAGE_TABLE_RESOLUTION
wrappedPageCoords.y = offsetPageCoords.y mod PAGE_TABLE_RESOLUTION
```

**处理负数:**
```
if (wrappedPageCoords.x < 0) wrappedPageCoords.x += PAGE_TABLE_RESOLUTION
if (wrappedPageCoords.y < 0) wrappedPageCoords.y += PAGE_TABLE_RESOLUTION
```

**参数说明:**
- `cascadeOffset`: 每个cascade的滑动窗口偏移量
- `wrappedPageCoords`: 包裹后的坐标（用于查找VPT）

**目的:** 实现页面缓存的滑动窗口机制

**代码实现位置:** `VSMCommon.hlsl:60-81`

### 2.4 页内UV → 物理纹素坐标

**公式:**
```
pageUV = frac(lightSpaceUV × PAGE_TABLE_RESOLUTION)
texelCoordsInPage = pageUV × PAGE_SIZE
physicalTexelCoords = physicalPageCoords × PAGE_SIZE + texelCoordsInPage
```

**参数说明:**
- `pageUV`: 页内UV坐标 [0,1)
- `PAGE_SIZE`: 页尺寸（128×128 texels）
- `physicalPageCoords`: 从VPT读取的物理页坐标
- `physicalTexelCoords`: 最终物理内存纹素坐标

**代码实现位置:** `VSMSampling.hlsl:79-82`, `VSMCopyDepth.compute:64-68`

---

## 3. 层次页缓冲（HPB）剔除公式

### 3.1 投影包围盒到光源空间

**步骤:**
1. 变换包围盒的8个角点到光源空间
2. 计算所有角点的UV最小/最大值

**公式:**
```
for each corner in boundingBox:
    lightSpacePos = lightMatrix × corner
    uv = lightSpacePos.xy × 0.5 + 0.5
    uvMin = min(uvMin, uv)
    uvMax = max(uvMax, uv)
```

**代码实现位置:** `VSMCommon.hlsl:136-159`

### 3.2 选择HPB Mip级别

**公式:**
```
texelMin = uvMin × PAGE_TABLE_RESOLUTION
texelMax = uvMax × PAGE_TABLE_RESOLUTION
texelSize = texelMax - texelMin
maxDimension = max(texelSize.x, texelSize.y)
level = max(0, ⌈log₂(maxDimension / 2.0)⌉)
```

**目标:** 选择包围盒恰好覆盖4个纹素的mip级别

**代码实现位置:** `VSMCommon.hlsl:163-184`

### 3.3 HPB剔除测试

**公式:**
```
resolution = PAGE_TABLE_RESOLUTION >> mipLevel
texelMin = ⌊uvMin × resolution⌋
texelMax = ⌈uvMax × resolution⌉

for each texel in [texelMin, texelMax]:
    if (HPB[texel] > 0.5):  // 页面是dirty
        return true  // 通过剔除
return false  // 被剔除
```

**代码实现位置:** `VSMCommon.hlsl:188-218`

---

## 4. Cascade Frustum 快照与约束

### 4.1 Cascade尺寸计算

**公式:**
```
cascadeSize[i] = firstCascadeSize × 2^i
```

**参数说明:**
- `firstCascadeSize`: 第0级cascade的边长（世界空间单位）
- `i`: cascade索引 [0, CASCADE_COUNT-1]

**代码实现位置:** `VirtualShadowMapManager.cs:161`

### 4.2 页世界空间尺寸

**公式:**
```
pageWorldSize = cascadeSize / PAGE_TABLE_RESOLUTION
```

**代码实现位置:** `VirtualShadowMapManager.cs:164`

### 4.3 Cascade快照到页网格

**公式:**
```
snappedCameraPos.x = ⌊cameraPos.x / pageWorldSize⌋ × pageWorldSize
snappedCameraPos.y = ⌊cameraPos.y / pageWorldSize⌋ × pageWorldSize
snappedCameraPos.z = ⌊cameraPos.z / pageWorldSize⌋ × pageWorldSize
```

**目的:** 使cascade frustum对齐到页网格，保证缓存页在平移时仍然有效

**代码实现位置:** `VirtualShadowMapManager.cs:165-169`

### 4.4 光源位置约束

论文描述: "光源位置被约束为沿着与光源矩阵近平面平行的平面滑动"

**实现:**
1. 计算垂直于光源方向的右/上向量
2. 将光源位置投影到垂直平面
3. 在该平面上快照到网格

**公式:**
```
right = normalize(cross(lightDir, up))
up = normalize(cross(right, lightDir))
rightOffset = ⌊dot(lightPos, right) / pageWorldSize⌋ × pageWorldSize
upOffset = ⌊dot(lightPos, up) / pageWorldSize⌋ × pageWorldSize
lightPos = right × rightOffset + up × upOffset + lightDir × depthOffset
```

**代码实现位置:** `VirtualShadowMapManager.cs:181-194`

---

## 5. Cascade偏移量计算（Sliding Window）

### 5.1 Frustum底左角计算

**公式:**
```
frustumBottomLeft = lightPos - right × (cascadeSize / 2) - up × (cascadeSize / 2)
originX = dot(frustumBottomLeft, right)
originY = dot(frustumBottomLeft, up)
```

**代码实现位置:** `VirtualShadowMapManager.cs:205-209`

### 5.2 页坐标偏移量

**公式:**
```
offsetX = ⌊originX / pageWorldSize⌋
offsetY = ⌊originY / pageWorldSize⌋
cascadeOffset = (offsetX, offsetY)
```

**代码实现位置:** `VirtualShadowMapManager.cs:212-213`

### 5.3 帧间偏移变化（Shift）

**公式:**
```
cascadeShift = currentOffset - previousOffset
```

**用途:** 计算需要失效的滑动窗口边缘页面

**代码实现位置:** `VirtualShadowMapManager.cs:229-237`

---

## 6. 物理页打包/解包

### 6.1 Page Entry位结构

```
Bit 0:      ALLOCATED 标志
Bit 1:      VISIBLE 标志
Bit 2:      DIRTY 标志
Bit 3-13:   物理页X坐标 (11 bits, 最大2047)
Bit 14-24:  物理页Y坐标 (11 bits, 最大2047)
```

### 6.2 打包公式

**公式:**
```
entry = (allocated ? 1 : 0) |
        (visible ? 2 : 0) |
        (dirty ? 4 : 0) |
        (physicalX & 0x7FF) << 3 |
        (physicalY & 0x7FF) << 14
```

**代码实现位置:** `VSMCommon.hlsl:48-57`

### 6.3 解包公式

**公式:**
```
physicalX = (entry >> 3) & 0x7FF
physicalY = (entry >> 14) & 0x7FF
```

**代码实现位置:** `VSMCommon.hlsl:41-46`

---

## 7. 深度比较与阴影测试

### 7.1 基本阴影测试

**公式:**
```
shadowDepth = PhysicalMemory[physicalPixel]
currentDepth = lightSpacePos.z
inShadow = (currentDepth - bias) > shadowDepth ? 0.0 : 1.0
```

**参数说明:**
- `bias`: 深度偏移，防止阴影痤疮（shadow acne）
- `inShadow`: 0.0 = 阴影中, 1.0 = 无阴影

**代码实现位置:** `VSMSampling.hlsl:88-89`

### 7.2 PCF滤波（3×3）

**公式:**
```
texelSize = 1.0 / VIRTUAL_TEXTURE_RESOLUTION
shadow = 0.0
totalWeight = 0.0

for dy in [-1, 1]:
    for dx in [-1, 1]:
        offset = (dx, dy) × texelSize × filterSize
        sampleUV = lightSpaceUV + offset
        shadowSample = SampleVSM(sampleUV)
        shadow += shadowSample
        totalWeight += 1.0

finalShadow = shadow / totalWeight
```

**代码实现位置:** `VSMSampling.hlsl:93-151`

---

## 8. 关键常量定义

```c++
PAGE_TABLE_RESOLUTION = 32      // 页表分辨率（每维）
PAGE_SIZE = 128                 // 每页128×128纹素
VIRTUAL_TEXTURE_RESOLUTION = 4096  // 32 × 128 = 4096
CASCADE_COUNT = 16              // 级联数量
PHYSICAL_MEMORY_WIDTH = 8192    // 物理内存宽度
PHYSICAL_MEMORY_HEIGHT = 4096   // 物理内存高度
PHYSICAL_PAGE_COUNT_X = 64      // 8192 / 128
PHYSICAL_PAGE_COUNT_Y = 32      // 4096 / 128
MAX_PHYSICAL_PAGES = 2048       // 64 × 32
```

**代码实现位置:** `VSMCommon.hlsl:4-15`

---

## 9. 重要的坐标空间流程图

```
世界空间 (World Space)
    ↓ (lightMatrix)
光源空间 (Light Space, clip coords)
    ↓ (perspective divide + NDC→UV)
光源UV空间 (Light UV [0,1])
    ↓ (× PAGE_TABLE_RESOLUTION, floor)
虚拟页坐标 (Virtual Page Coords [0,31])
    ↓ (+ cascadeOffset, mod 32)
包裹页坐标 (Wrapped Page Coords [0,31])
    ↓ (VPT查找)
物理页坐标 (Physical Page Coords [0,63]×[0,31])
    ↓ (× PAGE_SIZE + pageUV × PAGE_SIZE)
物理纹素坐标 (Physical Texel Coords [0,8191]×[0,4095])
```

---

## 10. 论文关键引用

### 10.1 Cascade选择（Section 12.2.1）

> "Each screen pixel is assigned a shadow cascade index based on a heuristic... The first heuristic prioritizes achieving pixel-perfect shadows—a one-to-one mapping of screen pixels to shadow map texels."

### 10.2 Sliding Window（Section 12.2）

> "As the cascade frustum moves to follow the main camera, new pages, previously located on the edge just outside of the cascade frustum, might need to be drawn. In order to preserve previously cached pages, we utilize a sliding window, also called 2D wraparound addressing."

### 10.3 光源约束（Section 12.2）

> "Further, the light position is constrained so that, when modified, it slides along a plane parallel to the near-plane of the respective light matrix. This constraint is necessary for the depth stored in cached pages to remain valid even after translating the light matrix."

### 10.4 HPB剔除（Section 12.2.2）

> "Similarly to how a hierarchical Z-buffer (Hi-Z) is built from the depth buffer for the purpose of occlusion culling, we build the HPB from each cascade VPT for the purpose of culling geometry that does not overlap any dirty page."

### 10.5 滤波边界（Section 12.2.3）

> "When filtering, this assumption is broken. One or more samples from the filtering region may fall into parts of the world that are not visible from the main camera... Instead of marking only the page directly corresponding to the visible texel, we mark all pages lying in a region around it."

---

## 总结

Virtual Shadow Maps的核心思想是通过虚拟内存机制和空间映射公式，将巨大的虚拟阴影贴图（16个4096×4096级联）映射到有限的物理内存（8192×4096）。关键技术包括:

1. **两阶段cascade选择启发式** - 平衡质量与性能
2. **Sliding Window机制** - 保留缓存页提高效率
3. **HPB层次剔除** - 细粒度几何体剔除
4. **光源与frustum快照** - 保证缓存有效性
5. **页面分配与失效策略** - 动态管理物理内存

这些公式和转换构成了整个VSM系统的数学基础。
