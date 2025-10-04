# VSM 实现与论文对比 - Bug 分析报告

## 已发现的关键 Bug

### 🔴 Bug #1: Cascade Offset 计算错误 (已修复)
**位置**: `VirtualShadowMapManager.cs:194`

**问题**:
```csharp
// 错误实现 - 使用世界空间坐标
Vector3 cascadeOrigin = lightPos - new Vector3(cascadeSize / 2, cascadeSize / 2, 0);
int offsetX = Mathf.FloorToInt(cascadeOrigin.x / pageWorldSize);
```

**症状**:
- 出现"圆圈+方形"的怪异图案
- Cascade 扩散不正确
- 页面坐标映射错误

**原因**:
论文中的 offset 应该在 **light space (2D)** 中计算，而不是 world space。光照矩阵的 X-Y 平面对应 light space 的 2D 投影平面。

**修复**:
```csharp
// 正确实现 - 在 light space 中计算
Vector3 frustumBottomLeft = lightPos - right * (cascadeSize / 2) - up * (cascadeSize / 2);
float originX = Vector3.Dot(frustumBottomLeft, right);  // 投影到 light space X 轴
float originY = Vector3.Dot(frustumBottomLeft, up);      // 投影到 light space Y 轴
int offsetX = Mathf.FloorToInt(originX / pageWorldSize);
int offsetY = Mathf.FloorToInt(originY / pageWorldSize);
```

---

## 论文关键概念对比

### 1. Sliding Window (滑动窗口)
**论文 Listing 12.1**:
> "we store a per-cascade offset of the respective light matrix position from the origin"

**实现要点**:
- Offset 必须在 light space 2D 坐标系中计算
- 当 camera 移动时，light matrix 平移，offset 更新
- Offset 用于将 virtual page coords 转换为 wrapped coords (模运算)

**当前实现**: ✅ 已修复

---

### 2. Page Coordinate Wrapping (页面坐标环绕)
**论文**:
> "Virtual page coordinates are converted to wrapped coordinates using modulo arithmetic"

**实现** (`VSMCommon.hlsl:VirtualPageCoordsToWrappedCoords`):
```hlsl
int2 offsetPageCoords = pageCoords.xy + cascadeOffset;
int2 wrappedPageCoords;
wrappedPageCoords.x = offsetPageCoords.x % VSM_PAGE_TABLE_RESOLUTION;
wrappedPageCoords.y = offsetPageCoords.y % VSM_PAGE_TABLE_RESOLUTION;
if (wrappedPageCoords.x < 0) wrappedPageCoords.x += VSM_PAGE_TABLE_RESOLUTION;
if (wrappedPageCoords.y < 0) wrappedPageCoords.y += VSM_PAGE_TABLE_RESOLUTION;
```

**当前实现**: ✅ 正确

---

### 3. Cascade Selection Heuristics (级联选择启发式)
**论文 Section 12.2.1** 提供两种方法:

#### 第一种: Pixel-Perfect (像素完美)
```
level = max(⌈log₂(T_w / T_c₀)⌉, 0)
```
- T_w: 屏幕空间纹素的世界空间尺寸
- T_c₀: Cascade 0 纹素的世界空间尺寸
- 目标: 1:1 映射 (screen pixel → shadow texel)

#### 第二种: Distance-Based (基于距离)
```
level = max(⌈log₂(d / s_c₀)⌉, 0)
```
- d: 到相机的世界空间距离
- s_c₀: Cascade 0 的 frustum 边长
- 优点: 旋转不变性

**当前实现**: ✅ 两种都实现了，可通过 `_UsePixelPerfectHeuristic` 切换

---

### 4. Filter Margin (过滤边距)
**论文 Section 12.2.3**:
> "we mark all pages lying in a region around it... to account for filtering and light/camera frustum mismatch"

**实现** (`VSMMarkVisiblePages.compute:88-92`):
```hlsl
for (int dy = -_FilterMargin; dy <= _FilterMargin; dy++)
{
    for (int dx = -_FilterMargin; dx <= _FilterMargin; dx++)
    {
        int3 pageCoords = int3(basePage + int2(dx, dy), cascadeLevel);
        // Mark page...
    }
}
```

**当前实现**: ✅ 正确
**推荐值**: 4 pages (覆盖 PCF + frustum mismatch)

---

### 5. HPB Culling (层次页面剔除)
**论文 Section 12.2.2**:
> "select the level in which the bounding-box bounds intersect exactly four texels"

**实现** (`VSMCommon.hlsl:CalculateHPBMipLevel`):
```hlsl
float maxDimension = max(texelSize.x, texelSize.y);
int level = max(0, (int)ceil(log2(maxDimension / 2.0)));
```

**当前实现**: ✅ 正确

---

### 6. Meshlet Rendering with Task Shader
**论文 Listing 12.3**:
> "The task shader performs frustum culling followed by culling against the HPB"

**实现流程**:
1. Task Shader:
   - Frustum culling per meshlet
   - HPB culling per meshlet
   - 只为通过的 meshlet dispatch mesh shader
2. Mesh Shader:
   - 生成 meshlet 几何
   - 输出到 fragment shader
3. Fragment Shader:
   - 计算深度
   - 使用 `InterlockedMin` 写入物理内存

**当前实现**: ✅ 基本正确 (使用 StructuredBuffer 代替 RWTexture2D)

---

## 可能的剩余问题

### 🟡 问题 A: Light Space UV 变换
**检查点**: `VSMCommon.hlsl:WorldToLightSpaceUV`

当前实现:
```hlsl
float4 lightSpacePos = mul(lightMatrix, float4(worldPos, 1.0));
float2 uv = lightSpacePos.xy * 0.5 + 0.5;
```

**疑问**: 是否需要透视除法？
```hlsl
// 可能需要:
float2 uv = (lightSpacePos.xy / lightSpacePos.w) * 0.5 + 0.5;
```

对于正交投影，`w = 1.0`，所以当前实现应该正确。但如果未来支持透视阴影，需要加上。

---

### 🟡 问题 B: Depth Comparison 方向
**检查点**: `VSMForwardLit.shader:165`

当前实现:
```hlsl
float shadowFactor = (currentDepth - _ShadowBias) > shadowDepth ? 0.0 : 1.0;
```

**Unity 平台差异**:
- DX12/Vulkan (UNITY_REVERSED_Z): depth = [1, 0] (1=near, 0=far)
- OpenGL: depth = [0, 1] (0=near, 1=far)

当前代码已处理平台差异 (line 153-156)，应该正确。

---

### 🟡 问题 C: Physical Memory 布局
**检查点**: 物理页分配顺序

当前布局:
- 64×32 = 2048 pages
- 每个 page 128×128 texels
- 总计 8192×4096 texels

**分配策略**:
- Free pages 从 (0,0) 开始
- Used pages 从末尾开始

**论文**: 没有特定要求，当前实现应该可行

---

## 调试步骤

### 使用 VSMDebugVisualizer 诊断:

1. **Mode 0: Physical Memory**
   - 应该看到: 分散的 128×128 页块
   - 白色 = 远平面 (1.0)
   - 黑色/灰色 = 有深度数据

2. **Mode 1: Page Table State**
   - 黑色 = 未分配
   - 绿色 = 已分配但不可见
   - 黄色 = 可见但不脏
   - 红色 = 脏页 (需要渲染)

3. **Mode 2: Physical Mapping**
   - 相同颜色 = 映射到相同物理页
   - 应该看到 32×32 的虚拟页网格

4. **Mode 3: Virtual Depth**
   - 通过虚拟页采样深度
   - 应该看到完整的阴影图

5. **Mode 4: Cascade Coverage**
   - 检查每个 cascade 的有效范围

---

## 与论文的差异总结

| 特性 | 论文要求 | 当前实现 | 状态 |
|-----|---------|---------|------|
| Sliding Window | Light space offset | ✅ 已修复 | ✅ |
| Wraparound | Modulo 32 | ✅ | ✅ |
| Cascade Heuristic | 两种方法 | ✅ | ✅ |
| Filter Margin | 周围页面标记 | ✅ | ✅ |
| HPB Culling | Mip level 选择 | ✅ | ✅ |
| Meshlet Rendering | Task+Mesh Shader | ✅ (模拟) | ✅ |
| InterlockedMin | Atomic depth | ✅ (Buffer) | ✅ |
| Depth Format | Float/Uint | ✅ 双系统 | ✅ |

---

## 建议的测试场景

1. **静态场景**:
   - 一个平面 + 一个立方体
   - 检查阴影是否正确

2. **相机移动**:
   - 观察 sliding window 是否正确工作
   - 检查页面是否正确失效和重新分配

3. **级联过渡**:
   - 在不同距离观察阴影质量
   - 检查 cascade 边界是否平滑

4. **物理内存可视化**:
   - Mode 0: 应该看到稀疏的页块
   - 不应该有"圆圈+方形"的异常图案

---

## 下一步行动

1. ✅ **已修复**: Cascade offset 计算
2. 🔄 **测试**: 使用 VSMDebugVisualizer 检查每个 mode
3. 📊 **分析**: 如果仍有问题，截图并分析图案
4. 🐛 **调试**: 根据可视化结果定位具体问题

---

## 使用方法

1. 将 `VSMDebugVisualizer.cs` 添加到 Main Camera
2. 将 `VSMDebugVisualize.shader` 分配到 Debug Shader 字段
3. 运行游戏，在 GUI 中切换不同的可视化模式
4. 观察图案，识别异常

Expected Results:
- **Physical Memory**: 稀疏分布的 128×128 方块
- **Page Table**: 红色脏页集中在可见区域
- **Virtual Depth**: 完整连续的阴影图
