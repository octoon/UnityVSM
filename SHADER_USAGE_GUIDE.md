# VSM Shader 使用指南

## 推荐使用的Shader（已测试）

### ✅ VSM/ForwardLit
**最推荐使用！**
- 路径：`VSM/ForwardLit`
- 类型：前向渲染
- 功能：完整的VSM阴影采样 + Lambert光照
- 属性：
  - Color: 物体颜色
  - Main Tex: 主纹理
  - Shadow Bias: 阴影偏移（防止阴影痤疮）

**使用方法：**
```
1. 创建材质
2. Shader选择 VSM/ForwardLit
3. 应用到物体
```

### ✅ VSM/ForwardLitDebug
**用于调试！**
- 路径：`VSM/ForwardLitDebug`
- 类型：前向渲染 + 调试可视化
- 调试模式：
  - **Off**: 正常渲染
  - **Shadow**: 阴影可视化（黑=阴影，白=光照）
  - **Cascade**: Cascade层级可视化（颜色编码）
  - **PageAllocation**: 页面分配可视化（绿=已分配，红=未分配）

**调试技巧：**
```
如果阴影不显示：
1. 切换Debug Mode到Shadow，查看是否有阴影数据
2. 切换到PageAllocation，检查页面是否分配（应该是绿色）
3. 切换到Cascade，查看cascade分布是否正确
```

### ✅ VSM/SimpleShadowReceiver
- 路径：`VSM/SimpleShadowReceiver`
- 类型：简单的阴影接收shader
- 功能：基础VSM阴影采样
- 属性：
  - Color: 物体颜色
  - Shadow Strength: 阴影强度

### ⚠️ VSM/StandardSurface
**暂不支持VSM！**
- 这是标准的Surface Shader
- 目前不支持VSM阴影
- 仅作为普通材质使用

## 不推荐直接使用的Shader（系统内部）

### VSM/DepthRender
- 系统内部使用
- VirtualShadowMapManager用于渲染深度
- 不要手动使用

### VSM/MeshletRender
- 高级功能（Meshlet渲染）
- 需要特殊设置
- 一般用户不需要

## 快速开始

### 方法1：使用设置助手（推荐）

```
1. 打开 Window > VSM Setup Helper
2. 点击"创建新的VSM ForwardLit材质"
3. 点击"应用到所有场景物体"
4. 完成！
```

### 方法2：手动设置

```
1. 创建材质
2. Shader选择 VSM/ForwardLit
3. 拖拽到场景物体上
4. 运行场景查看阴影
```

## Shader对比

| Shader | VSM阴影 | 光照模型 | 调试功能 | 推荐度 |
|--------|---------|----------|----------|--------|
| VSM/ForwardLit | ✅ | Lambert | ❌ | ⭐⭐⭐⭐⭐ |
| VSM/ForwardLitDebug | ✅ | Lambert | ✅ | ⭐⭐⭐⭐ (调试) |
| VSM/SimpleShadowReceiver | ✅ | 简单 | ❌ | ⭐⭐⭐ |
| VSM/StandardSurface | ❌ | PBR | ❌ | ⭐ (不支持VSM) |

## 材质属性说明

### VSM/ForwardLit

```hlsl
_Color          // 基础颜色 (默认: 白色)
_MainTex        // 主纹理 (默认: 白色)
_ShadowBias     // 阴影偏移 (默认: 0.001)
                // 如果出现阴影痤疮(shadow acne)，增加此值
                // 如果阴影悬浮(peter panning)，减小此值
```

### VSM/ForwardLitDebug

```hlsl
_Color          // 基础颜色
_MainTex        // 主纹理
_ShadowBias     // 阴影偏移
_DebugMode      // 调试模式
                // 0 = Off (正常)
                // 1 = Shadow (阴影)
                // 2 = Cascade (层级)
                // 3 = PageAllocation (页面分配)
```

## 常见问题

### Q: 为什么我的物体没有阴影？

**检查清单：**
1. ✅ 使用了 VSM/ForwardLit 或 VSM/ForwardLitDebug shader？
2. ✅ VirtualShadowMapManager在运行？（Play模式）
3. ✅ Directional Light已分配？
4. ✅ 物体在Shadow Casters Layer？
5. ✅ VSM Preview窗口显示有深度数据？

**调试步骤：**
```
1. 使用VSM/ForwardLitDebug材质
2. Debug Mode设为Shadow
   - 如果是白色：没有采样到阴影
   - 如果是黑色：在阴影中
3. Debug Mode设为PageAllocation
   - 绿色：页面已分配（正常）
   - 红色：页面未分配（VSM系统问题）
```

### Q: 阴影有锯齿或闪烁？

**解决方法：**
- 增加 `Page Size` (VSMConstants.cs)
- 调整 `First Cascade Size`
- 增加 `Filter Margin`

### Q: 性能不好？

**优化建议：**
- 减少 `Cascade Count`
- 减小 `Physical Memory` 分辨率
- 减小 `Page Size`

## 技术细节

### VSM/ForwardLit 工作流程

```hlsl
Fragment Shader:
1. 计算世界坐标
2. 调用 SampleVSMShadow(worldPos)
   a. 计算Cascade层级
   b. 转换到光空间
   c. 查找虚拟页表
   d. 获取物理页坐标
   e. 采样深度
   f. 深度比较
3. 应用阴影到光照计算
4. 输出最终颜色
```

### 深度存储格式

```hlsl
// 物理内存使用uint存储
RWTexture2D<uint> _VSM_PhysicalMemory

// 写入：
InterlockedMin(_VSM_PhysicalMemory[coords], asuint(depth));

// 读取：
uint depthUint = _VSM_PhysicalMemory.Load(int3(coords, 0)).r;
float depth = asfloat(depthUint);
```

## 更新日志

- **v1.0** - 初始版本
  - VSM/ForwardLit - 基础前向渲染shader
  - VSM/ForwardLitDebug - 调试版本
  - VSM Setup Helper - 自动化工具
