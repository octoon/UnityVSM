# Virtual Shadow Maps 代码Bug分析报告

## 分析概览

通过对比GPU Zen 3论文和Unity实现代码，发现了以下几类问题：
1. **严重Bug** - 导致功能错误或崩溃
2. **性能问题** - 影响效率
3. **实现偏差** - 与论文不一致但可能有意为之
4. **潜在问题** - 边界情况未处理

---

## 🔴 严重Bug

### Bug #1: 物理页坐标计算错误 (VSMSampling.hlsl:82)

**位置:** `VSMSampling.hlsl:82`

**错误代码:**
```hlsl
int2 physicalPixel = int2((physicalPageCoords + pageUV) * VSM_PAGE_SIZE);
```

**问题分析:**
这是一个**严重的数学错误**！应该先将`pageUV`乘以`VSM_PAGE_SIZE`计算页内像素坐标，再加到物理页起始坐标上。

当前代码先将物理页坐标（例如`[5, 3]`）加上UV（`[0.5, 0.5]`）得到`[5.5, 3.5]`，然后乘以128得到`[704, 448]`。这是错误的！

**正确实现:**
```hlsl
// 先计算物理页起始像素坐标
int2 physicalPagePixelBase = physicalPageCoords * VSM_PAGE_SIZE;
// 再计算页内偏移
int2 pagePixelOffset = int2(pageUV * VSM_PAGE_SIZE);
// 最终坐标
int2 physicalPixel = physicalPagePixelBase + pagePixelOffset;
```

**正确的一行版本:**
```hlsl
int2 physicalPixel = physicalPageCoords * VSM_PAGE_SIZE + int2(pageUV * VSM_PAGE_SIZE);
```

**对比论文公式（Section 12.2.2, Listing 12.3）:**
```
in_memory_offset = physical_page_coords * VSM_PAGE_SIZE
memory_texel_coords = in_memory_offset + in_page_texel_coords
```

**影响:**
- 采样位置完全错误
- 阴影会出现随机错误或完全失效
- 可能访问越界内存

**修复优先级:** 🔥 **最高优先级**

---

### Bug #2: PCF滤波中的相同错误 (VSMSampling.hlsl:141)

**位置:** `VSMSampling.hlsl:141`

**错误代码:**
```hlsl
int2 physicalPixel = int2((physicalPageCoords + pageUV) * VSM_PAGE_SIZE);
```

**问题:** 与Bug #1完全相同的错误

**修复:** 同Bug #1

**修复优先级:** 🔥 **最高优先级**

---

### Bug #3: MarkVisiblePages深度重建可能有平台兼容性问题 (VSMMarkVisiblePages.compute:35-51)

**位置:** `VSMMarkVisiblePages.compute:35-51`

**潜在问题代码:**
```hlsl
float3 ReconstructWorldPosition(float2 uv, float depth)
{
    #if UNITY_REVERSED_Z
        // DX12/Vulkan: depth is [1 (near), 0 (far)]
        // 已经正确用于clip space
    #else
        // OpenGL: depth is [0 (near), 1 (far)]
        // 需要重映射到 [-1, 1] for clip space
        depth = depth * 2.0 - 1.0;
    #endif

    float4 clipPos = float4(uv * 2.0 - 1.0, depth, 1.0);
    clipPos.y = -clipPos.y;  // Unity的Y翻转
    float4 worldPos = mul(_CameraInverseViewProjection, clipPos);
    return worldPos.xyz / worldPos.w;
}
```

**问题分析:**
1. **平台判断可能不完整** - Unity在不同平台有不同的深度范围
2. **Y翻转可能不正确** - `clipPos.y = -clipPos.y` 这行可能在某些平台导致错误
3. **未使用`LinearEyeDepth`** - Unity通常推荐使用`LinearEyeDepth`辅助函数

**推荐修复:**
```hlsl
float3 ReconstructWorldPosition(float2 uv, float depth)
{
    // 使用Unity的深度处理宏
    #if defined(UNITY_REVERSED_Z)
        depth = 1.0 - depth;  // 反转到[0,1]
    #endif

    // 构建clip空间坐标
    float4 clipPos = float4(
        uv.x * 2.0 - 1.0,
        (1.0 - uv.y) * 2.0 - 1.0,  // Unity UV y轴是反的
        depth * 2.0 - 1.0,
        1.0
    );

    float4 worldPos = mul(_CameraInverseViewProjection, clipPos);
    return worldPos.xyz / worldPos.w;
}
```

**修复优先级:** 🟠 **高优先级** （跨平台兼容性）

---

### Bug #4: CopyDepth中的offset处理不一致 (VSMCopyDepth.compute:43-48)

**位置:** `VSMCopyDepth.compute:43-48`

**问题代码:**
```hlsl
// DEBUG: If offset is (0,0), use direct mapping without wrapping
// This matches AllocateAllPages debug mode behavior
if (cascadeOffset.x == 0 && cascadeOffset.y == 0)
{
    wrappedCoords = pageCoords;  // Direct mapping, no wrap
}
```

**问题分析:**
这是一个**调试代码残留**，在生产代码中会导致逻辑不一致：
- `MarkVisiblePages`使用正常的wrapping
- `CopyDepth`在offset为(0,0)时跳过wrapping
- 这导致两个阶段使用不同的坐标映射！

**影响:**
- 当cascadeOffset恰好为(0,0)时，写入和读取使用不同的坐标
- 导致阴影错误或丢失

**修复:**
```hlsl
// 删除这段DEBUG代码，总是使用wrapping
int3 wrappedCoords = VirtualPageCoordsToWrappedCoords(pageCoords, cascadeOffset);
```

**修复优先级:** 🔥 **最高优先级**

---

## 🟡 性能问题

### 性能问题 #1: Cascade选择重复计算 (VSMSampling.hlsl)

**位置:** `VSMSampling.hlsl:45, 96`

**问题:**
在`SampleVSM`和`SampleVSM_PCF`中，cascade选择逻辑完全重复。如果采样多次，会重复计算。

**建议优化:**
```hlsl
// 新增辅助函数
int SelectCascadeLevel(float3 worldPos)
{
    return CalculateCascadeLevel(worldPos, _VSM_CameraPosition, _VSM_FirstCascadeSize);
}

// 在fragment shader中预先计算
int cascadeLevel = SelectCascadeLevel(worldPos);
float shadow = SampleVSMWithCascade(worldPos, cascadeLevel);
```

---

### 性能问题 #2: PCF循环中的VPT重复查找

**位置:** `VSMSampling.hlsl:113-147`

**问题:**
9次PCF采样，每次都要：
1. 计算pageCoords
2. 计算wrappedCoords
3. 查找VPT
4. 解包物理坐标

对于同一个页面内的采样，这些步骤是重复的。

**建议优化:**
```hlsl
// 预先检查是否所有采样点都在同一页面
int2 basePage = pageCoords;
bool allInSamePage = true;  // 检查filter kernel是否跨页

if (allInSamePage)
{
    // 只查找一次VPT
    // 所有采样直接计算物理像素坐标
}
else
{
    // 使用当前的逐采样查找逻辑
}
```

---

### 性能问题 #3: 不必要的atomic操作 (VSMCopyDepth.compute:77)

**位置:** `VSMCopyDepth.compute:77`

**代码:**
```hlsl
InterlockedMin(_PhysicalMemoryBuffer[bufferIndex], asuint(depth));
```

**问题:**
`InterlockedMin`是原子操作，开销较大。但在传统渲染路径中：
1. 每个页面只渲染一次
2. 深度已经通过Z-buffer处理了重叠
3. 不需要atomic操作

**建议:**
只在Meshlet渲染路径中使用atomic，传统路径直接写入：
```hlsl
#ifdef MESHLET_RENDERING
    InterlockedMin(_PhysicalMemoryBuffer[bufferIndex], asuint(depth));
#else
    _PhysicalMemoryBuffer[bufferIndex] = asuint(depth);
#endif
```

---

## 🔵 实现偏差（与论文不一致）

### 偏差 #1: 未实现Meshlet的per-meshlet剔除

**论文描述 (Section 12.2.2):**
> "One meshlet is mapped to a single task shader invocation. The task shader performs frustum culling followed by culling against the HPB."

**当前实现:**
- `VSMMeshletRenderer.cs`存在但实现不完整
- 没有真正的Task Shader（Unity不支持）
- Frustum culling在CPU端完成（`VirtualShadowMapManager.cs:688-689`）

**影响:**
- 无法达到论文描述的细粒度剔除性能
- 这是Unity引擎限制，不算bug，但应该注记

---

### 偏差 #2: HPB未用于实际剔除

**论文描述:**
> "Similarly to how a hierarchical Z-buffer (Hi-Z) is built from the depth buffer... we build the HPB from each cascade VPT for the purpose of culling geometry"

**当前实现:**
- `VSMBuildHPB.compute`确实构建了HPB
- 但在渲染循环中**没有使用HPB做剔除**
- `VSMMeshletRenderer`未完成HPB culling逻辑

**影响:**
- 性能不如论文描述
- 所有allocated页面都会被绘制，即使不是dirty

**建议:**
在`DrawingPhase_Traditional`中添加HPB culling:
```csharp
// 在渲染前检查物体是否与任何dirty页面重叠
if (!HPBCullTest(renderer.bounds, cascadeIndex))
    continue;  // 跳过此物体
```

---

### 偏差 #3: Filter margin默认为0

**位置:** `VirtualShadowMapManager.cs:17`

**代码:**
```csharp
[SerializeField] [Range(0, 8)] private int filterMargin = 0;
```

**论文描述 (Section 12.2.3):**
> "Instead of marking only the page directly corresponding to the visible texel, we mark all pages lying in a region around it. This region must be greater than or equal to the size of the filtering region."

**问题:**
- 默认值为0意味着禁用了filter margin
- 但代码中有PCF滤波（3×3 kernel）
- **这会导致PCF采样时访问未分配的页面！**

**修复:**
```csharp
[SerializeField] [Range(0, 8)] private int filterMargin = 1;  // 对于3×3 PCF，至少需要1
```

**修复优先级:** 🔥 **高优先级** （影响PCF正确性）

---

## 🟣 潜在问题

### 潜在问题 #1: 物理内存不足时的处理

**位置:** `VSMAllocatePages.compute`

**问题:**
当物理页面用完（2048页全部分配）时：
- `_FreePhysicalPages`为空
- `_UsedPhysicalPages`也可能不够
- 代码没有明确的fallback策略

**建议:**
添加警告和降级策略：
```csharp
if (pageCounts[0] == 0 && pageCounts[1] == 0)
{
    Debug.LogWarning("VSM: Out of physical memory! Consider reducing cascade count or page resolution.");
    // 可选: 强制释放最老的缓存页
}
```

---

### 潜在问题 #2: Cascade快照可能导致抖动

**位置:** `VirtualShadowMapManager.cs:165-194`

**问题:**
虽然实现了页面网格快照，但光源方向变化时：
1. 所有cascade的`rightOffset`和`upOffset`会重新计算
2. 可能导致小的数值误差累积
3. 在边界情况下可能导致页面抖动

**建议:**
添加hysteresis（迟滞）机制：
```csharp
float epsilon = pageWorldSize * 0.01f;  // 1%的容差
if (abs(rightOffset - previousRightOffset) < epsilon)
    rightOffset = previousRightOffset;
```

---

### 潜在问题 #3: 深度精度问题

**位置:** 物理内存使用`uint`存储`float`深度

**代码:** `VSMCopyDepth.compute:77`, `VSMSampling.hlsl:14`

```hlsl
// 写入
InterlockedMin(_PhysicalMemoryBuffer[bufferIndex], asuint(depth));

// 读取
float depthFloat = asfloat(depthUint);
```

**问题:**
- `asuint/asfloat`位转换保留了浮点精度
- 但`InterlockedMin`对uint进行比较，**不等于浮点数比较！**
- 例如：负数的uint表示会比正数大

**分析:**
由于深度值在[0,1]范围（非负），这个问题**暂时不会发生**。但代码不够健壮。

**建议:**
添加注释说明这个限制：
```hlsl
// IMPORTANT: 只对非负深度值有效！
// asuint()的位模式在[0,1]范围内保持单调性
InterlockedMin(_PhysicalMemoryBuffer[bufferIndex], asuint(depth));
```

---

## 📊 Bug优先级总结

| Bug ID | 描述 | 优先级 | 影响 |
|--------|------|--------|------|
| Bug #1 | 物理页坐标计算错误 | 🔥 P0 | 阴影完全错误 |
| Bug #2 | PCF相同错误 | 🔥 P0 | PCF滤波失效 |
| Bug #4 | CopyDepth offset不一致 | 🔥 P0 | 坐标映射错误 |
| 偏差 #3 | Filter margin默认为0 | 🔥 P0 | PCF越界访问 |
| Bug #3 | 深度重建兼容性 | 🟠 P1 | 跨平台问题 |
| 性能 #3 | 不必要的atomic | 🟡 P2 | 性能损失 |
| 性能 #1 | Cascade重复计算 | 🟡 P2 | 性能损失 |
| 潜在 #1 | 内存不足处理 | 🟢 P3 | 边界情况 |

---

## 🔧 推荐修复顺序

1. **立即修复 Bug #1 和 #2** - 这是最严重的数学错误
2. **立即修复 Bug #4** - 逻辑不一致导致坐标错误
3. **修改默认filterMargin为1** - 保证PCF正确性
4. **修复Bug #3** - 提高跨平台兼容性
5. **性能优化** - 在功能正确后进行
6. **完善HPB剔除** - 长期改进

---

## 📝 测试建议

修复后应该测试：
1. **基础阴影测试** - 简单场景，单一物体，验证阴影正确
2. **PCF滤波测试** - 验证软阴影边缘平滑
3. **Cascade过渡测试** - 移动相机，观察级联切换
4. **Sliding window测试** - 平移相机，验证缓存页正确
5. **压力测试** - 大场景，测试物理内存不足情况
6. **跨平台测试** - Windows (DX12), macOS (Metal), Linux (Vulkan)

---

## 🎯 代码审查建议

为防止未来bug：
1. **删除所有DEBUG代码路径** - 如`allocateAllPages`分支
2. **统一坐标计算** - 所有地方使用相同的公式
3. **添加断言** - 检查坐标范围有效性
4. **添加单元测试** - 特别是数学转换函数
5. **文档化假设** - 如"深度值必须非负"

---

## 结论

这个VSM实现有很好的架构设计，但存在几个**关键的数学错误**（Bug #1, #2）导致功能失效。修复这些bug后，实现应该能正常工作，尽管某些论文描述的优化（如细粒度HPB剔除）未完全实现。

**最关键的修复:** 物理像素坐标的计算公式必须从 `(pageCoords + pageUV) * pageSize` 改为 `pageCoords * pageSize + pageUV * pageSize`。
