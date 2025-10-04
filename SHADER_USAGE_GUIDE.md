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

<!-- 调试版 ForwardLitDebug 已移除，使用普通 ForwardLit 并借助日志/可视化面板进行排查 -->

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

<!-- 调试版 ForwardLitDebug 已移除 -->

## 常见问题

### Q: 为什么我的物体没有阴影？

**检查清单：**
1. ✅ 使用了 VSM/ForwardLit shader？
2. ✅ VirtualShadowMapManager在运行？（Play模式）
3. ✅ Directional Light已分配？
4. ✅ 物体在Shadow Casters Layer？
5. ✅ VSM Preview窗口显示有深度数据？

**调试步骤：**
```
1. 使用 VSM/ForwardLit 材质并检查阴影强度/偏移
2. 在场景窗口观察阴影是否随相机/物体移动更新
3. 若需要更底层排查，可启用日志或添加可视化面板（非默认提供）
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
// 写入路径（绘制阶段）：原子写入 StructuredBuffer<uint>
RWStructuredBuffer<uint> _PhysicalMemoryBuffer;
uint idx = y * PhysicalWidth + x;
InterlockedMin(_PhysicalMemoryBuffer[idx], asuint(depth));

// 复制至纹理（采样阶段使用）：compute shader 将 Buffer -> Texture2D<float>
RWTexture2D<float> _PhysicalMemoryTexture;

// 采样：
Texture2D<float> _VSM_PhysicalMemory;
float depth = _VSM_PhysicalMemory.Load(int3(pixel, 0)).r;
```

## 更新日志

- **v1.0** - 初始版本
  - VSM/ForwardLit - 基础前向渲染shader
  - VSM/ForwardLitDebug - 调试版本
  - VSM Setup Helper - 自动化工具
