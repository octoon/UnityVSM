# Virtual Shadow Maps - Unity设置说明

## 为什么需要自定义阴影采样？

Unity的内置阴影系统使用传统的Shadow Map。VSM（Virtual Shadow Maps）使用完全不同的虚拟纹理系统，因此需要自定义阴影采样函数来替换Unity内置的阴影系统。

## 设置步骤

### 方法1: 使用VSM专用Shader（推荐）

直接使用提供的VSM shader，它们已经内置了正确的阴影采样：

1. **VSMSimpleLit.shader** - 简单的光照+阴影shader
   - 适合大多数场景物体
   - 使用路径: `VSM/SimpleLit`

2. **VSMStandardSurface.shader** - PBR表面shader
   - 支持金属度和光滑度
   - 使用路径: `VSM/StandardSurface`

**使用方法：**
- 选择场景中的物体
- 在Inspector中，给Material分配上述shader
- 阴影将自动使用VSM系统

### 方法2: 修改现有Shader

如果要在现有shader中使用VSM阴影：

1. 在shader文件中包含VSM头文件：
```hlsl
#include "Assets/Shaders/Include/VSMCommon.hlsl"
```

2. 添加VSM全局变量：
```hlsl
Texture2DArray<uint> _VSM_VirtualPageTable;
Texture2D<uint> _VSM_PhysicalMemory;
StructuredBuffer<float4x4> _VSM_CascadeLightMatrices;
StructuredBuffer<int2> _VSM_CascadeOffsets;
float _VSM_FirstCascadeSize;
float3 _VSM_CameraPosition;
```

3. 添加采样函数（参考VSMSimpleLit.shader中的实现）

4. 在fragment shader中调用：
```hlsl
float shadow = VSM_SampleShadow(worldPos, 0.001);
```

### 方法3: 替换Unity内置AutoLight（高级）

**警告：此方法会影响整个项目的阴影系统！**

1. 打开 `Edit > Project Settings > Graphics`

2. 在 `Built-in Shader Settings` 部分：
   - 找到 `Always Included Shaders`
   - 添加自定义shader包含

3. 或者在每个使用阴影的shader开头添加：
```hlsl
#define AUTOLIGHT_INCLUDED
#include "Assets/Shaders/Include/VSMAutoLight.cginc"
```

## 配置VirtualShadowMapManager

确保场景中的 `VirtualShadowMapManager` 组件已正确配置：

### 必需的Compute Shader分配：
- Clear Visible Flags Shader: `VSMClearVisibleFlags.compute`
- Free Invalidated Pages Shader: `VSMFreeInvalidatedPages.compute`
- Mark Visible Pages Shader: `VSMMarkVisiblePages.compute`
- Allocate Pages Shader: `VSMAllocatePages.compute`
- Clear Pages Shader: `VSMClearPages.compute`
- **Clear Memory Shader: `VSMClearMemory.compute`** ← 新增！
- Build HPB Shader: `VSMBuildHPB.compute`

### 必需的Shader分配：
- VSM Depth Shader: `VSMDepthRender.shader`

### 光源设置：
- Directional Light: 拖拽场景中的方向光

## 测试阴影

1. 在场景中添加地面平面和一些物体
2. 给物体分配 `VSM/SimpleLit` shader材质
3. 运行场景
4. 打开预览窗口查看VSM工作状态：`Window > VSM Preview`

### 调试技巧：

**如果看不到阴影：**
- 检查VirtualShadowMapManager是否在运行
- 检查物体是否在正确的Layer（Shadow Casters）
- 使用预览窗口查看物理内存是否有深度数据
- 查看Console是否有错误信息

**如果阴影闪烁：**
- 调整 `First Cascade Size`
- 增加 `Filter Margin`

**性能问题：**
- 减少 `Page Size` 或 `Physical Memory` 分辨率
- 减少 `Cascade Count`

## 已创建的Shader列表

### VSM专用Shader：
- `VSM/SimpleLit` - 简单光照+VSM阴影
- `VSM/StandardSurface` - PBR表面+VSM阴影
- `VSM/SimpleShadowReceiver` - 基础阴影接收

### VSM系统Shader：
- `VSM/DepthRender` - 深度渲染（系统内部使用）
- `VSM/MeshletRender` - Meshlet渲染（高级功能）

### 工具Shader：
- `VSMAutoLight.cginc` - 自定义阴影采样函数库
- `VSMCommon.hlsl` - VSM通用函数
- `VSMSampling.hlsl` - VSM采样函数

## 常见问题

### Q: 为什么不能直接使用Unity的Standard Shader？
A: Unity的Standard Shader使用内置的阴影采样函数，它们是为传统Shadow Map设计的。VSM需要自定义的采样逻辑。

### Q: 可以混用VSM和传统阴影吗？
A: 不建议。VSM替换了整个阴影系统。如果需要同时使用，需要为每个shader明确指定使用哪种阴影系统。

### Q: VSM支持点光源和聚光灯吗？
A: 当前版本只支持方向光。点光源和聚光灯的VSM实现更复杂，未来版本可能会添加。

### Q: 如何调整阴影质量？
A: 在 `VSMConstants.cs` 中调整：
- `PAGE_SIZE` - 每页分辨率（128推荐）
- `PAGE_TABLE_RESOLUTION` - 页表分辨率（32推荐）
- `PHYSICAL_MEMORY_WIDTH/HEIGHT` - 物理内存大小

## 技术支持

遇到问题？检查：
1. Unity Console的错误信息
2. VSM Preview窗口显示的纹理
3. VirtualShadowMapManager的Inspector配置
4. Shader编译错误（Shader Inspector）
