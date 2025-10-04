# Virtual Shadow Maps 实现审查与修复报告

本文基于 GPU Zen 3《Virtual Shadow Maps》章节（Matej Sakmary 等）与仓库文档《gpu zen3 vsm.md》，对当前仓库的 VSM 实现进行审查，标出偏差与缺失，并给出已实施的修复。

## 概览

- 管线阶段（与论文一致）
  - 记账（Bookkeeping）：清可见位、释放失效页、标记可见页（含过滤边距）、分配物理页、清脏页、构建 HPB。
  - 绘制（Drawing）：渲染到虚拟分辨率，再拷贝至稀疏物理内存（结构化缓冲→RFloat 纹理）。
  - 采样（Sampling）：材质侧按世界坐标选择级联、查询 VPT、映射物理像素并比较深度（可选 PCF）。
- 兼容 Meshlet + Task/Mesh Shader 剔除方案（论文 Listing 12.3 路径），同时保留传统渲染路径。

## 发现的问题与影响

1) 采样阶段 UV/深度映射不一致（严重）
   - 症状：阴影错位/错误、跨平台深度比较反向。
   - 根因：
     - 多处未进行透视除法（ndc = clip.xyz / clip.w）就从 light space 映射至 UV。
     - 未按平台差异处理 UNITY_REVERSED_Z。
     - 若干示例/调试着色器各自为政，与公共位布局/坐标系不一致。

2) 物理内存纹理类型不一致（严重）
   - C# 侧绑定为 RFloat 纹理用于采样；部分着色器声明为 `Texture2D<uint>` 并以 bitcast 方式读取，导致类型/期望不匹配与潜在 UB。

3) HPB 投影边界未做透视除法（中等）
   - `ProjectBoundsToLightSpace` 直接使用 clip.xy 映射 UV，未除以 w，HPB 剔除与视锥判定可能偏差。

4) 调试分配器与物理页表结构不一致（次要）
   - `VSMAllocateAllPages.compute` 写入 `RWStructuredBuffer<uint>`，主实现为 `uint4`（xyz=虚拟页坐标，w=分配标记）。

5) 动态失效（Dynamic Invalidation）为占位实现（非阻断）
   - `ExtractPageBitFromMask` 仅示意实现，管理器未实际生成/写入动态物体位掩码。静态场景不受影响。

## 已实施修复

1) 统一采样 UV/深度映射与纹理类型
   - 文件：`Assets/Shaders/VSM/VSMSampling.hlsl`
     - 将 `_VSM_PhysicalMemory` 改为 `Texture2D<float>`；
     - 采样前统一使用 `ndc = lightSpacePos.xyz / lightSpacePos.w`；
     - `currentDepth` 在 `!UNITY_REVERSED_Z` 时从 [-1,1] 映射至 [0,1]；
     - 修正 PCF 分支同样流程。
   - 文件：`Assets/Shaders/Include/VSMAutoLight.cginc`
     - 统一为 float 纹理；加入透视除法与平台深度处理；修正物理像素坐标为 `pageBase + inPageOffset`。
   - 文件：`Assets/Shaders/VSMSimpleLit.shader`、`Assets/Shaders/SimpleShadowReceiver.shader`
     - 移除重复/错误实现，改为 `#include "Include/VSMCommon.hlsl"` 与 `#include "VSM/VSMSampling.hlsl"`，使用 `SampleVSM_PCF`。
   - 文件：`Assets/Shaders/VSMShadowOnly.shader`、`Assets/Shaders/VSMForwardLitDebug.shader`、`Assets/Shaders/VSMTest.shader`
     - 统一为 float 纹理；使用 ndc 与平台深度处理；修正物理像素坐标公式；修正 DEPTH/SHADOW 模式的取样逻辑。

2) 修正 HPB 投影边界计算
   - 文件：`Assets/Shaders/Include/VSMCommon.hlsl`
     - `ProjectBoundsToLightSpace` 对 8 个包围盒角点加入透视除法，再映射至 [0,1] UV。

3) 调试全量分配器对齐物理页表结构
   - 文件：`Assets/Shaders/VSM/VSMAllocateAllPages.compute`
     - `_PhysicalPageTable` 改为 `RWStructuredBuffer<uint4>`；写入 `uint4(virtualX, virtualY, cascade, 1)`。

## 与论文的一致性核对

- Sliding Window（级联包裹与偏移）：已在 `VirtualShadowMapManager.cs` 中于 light space 2D 网格对齐后计算 offset（见文件内部注释与修复记录）。
- Mark Visible Pages：实现过滤边距 `_FilterMargin`，支持 PCF/跨页采样所需的页邻域标记（12.2.3 节）。
- 两阶段页分配：先用空闲页，再回收“已分配但不可见”的页（12.2.1 节）。
- HPB 构建与剔除：按 2x2 texel 选择 mip，任一脏即保留（12.2.2 节）。
- Meshlet 路径：Task Shader + HPB 剔除，Mesh Shader 输出并以原子最小值写深度（采用 StructuredBuffer + 复制回纹理的等价实现）。

## 风险与兼容性

- 所有改动均保持 HLSL 5.0+ 与当前 C# 绑定方式一致；关键统一点为“采样使用 RFloat 纹理”。
- 若场景开启 `allocateAllPages` 调试选项，更新后 PPT 数据结构匹配，便于可视化与验证。

## 建议的后续改进

- 动态失效掩码：基于移动物体 AABB 写入 `_DynamicInvalidationMasks`，完善 `ExtractPageBitFromMask` 的索引与多掩码布局。
- 过滤边距：跨页 PCF/PCSS 时建议 `_FilterMargin >= 2`；根据场景调整以避免滤波越界采样黑边。
- 自测/可视化：保留 VSMDebug/Visualizer 通道，帧内统计已分配/可见/脏页数，验证 HPB 层级合理性与滑动窗口偏移。

## 变更清单（文件）

- 修改：Assets/Shaders/VSM/VSMSampling.hlsl
- 修改：Assets/Shaders/Include/VSMCommon.hlsl
- 修改：Assets/Shaders/Include/VSMAutoLight.cginc
- 修改：Assets/Shaders/SimpleShadowReceiver.shader
- 修改：Assets/Shaders/VSMSimpleLit.shader
- 修改：Assets/Shaders/VSMShadowOnly.shader
- 修改：Assets/Shaders/VSMForwardLitDebug.shader
- 修改：Assets/Shaders/VSMTest.shader
- 修改：Assets/Shaders/VSM/VSMAllocateAllPages.compute

以上改动已提交到仓库（工作区），如需我进一步帮助联调或添加更细粒度的调试可视化，请告知具体场景与预期。

