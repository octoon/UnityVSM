# VSM阴影故障排除 - 详细步骤

## 当前状态
- ✅ 材质已应用到所有物体（使用VSM/ForwardLit）
- ❌ 但是看不到阴影

---

## 第一步：检查VSM系统是否在运行

### 1.1 打开诊断工具
```
Unity菜单 > Window > VSM Debug Checker
点击 "运行诊断" 按钮
```

### 1.2 查看Console输出

**应该看到：**
```
=== VSM系统诊断开始 ===
✅ 找到VirtualShadowMapManager
✅ clearVisibleFlagsShader: VSMClearVisibleFlags
✅ freeInvalidatedPagesShader: VSMFreeInvalidatedPages
✅ markVisiblePagesShader: VSMMarkVisiblePages
✅ allocatePagesShader: VSMAllocatePages
✅ clearPagesShader: VSMClearPages
✅ clearMemoryShader: VSMClearMemory  ← 这个很重要！
✅ buildHPBShader: VSMBuildHPB
=== VSM系统诊断完成 ===
```

**如果看到警告：**
```
⚠️ clearMemoryShader 未分配
```
→ **这是最常见的问题！**

---

## 第二步：使用测试Shader确认VSM状态

### 2.1 创建测试材质
```
1. Project窗口右键 > Create > Material
2. 命名为 "VSM_Test"
3. Shader选择 "VSM/Test"
```

### 2.2 应用到一个物体
```
拖拽 VSM_Test 材质到场景中的一个Cube或Plane
```

### 2.3 测试PageAlloc模式
```
1. 选中VSM_Test材质
2. Inspector中：Test Mode = PageAlloc
3. Play运行场景
4. 观察物体颜色
```

### 结果分析：

#### 🟢 绿色 = VSM工作正常
→ 跳到"第三步"

#### 🔴 红色 = VSM没有分配页面
→ 这是问题所在！继续下面的检查

#### 🟣 紫色 = 物体在光源范围外
→ 调整First Cascade Size

#### 🔵 青色 = 坐标系统问题
→ 检查Cascade设置

---

## 如果是红色（最常见）

### 问题：VSM系统没有运行或页面分配失败

### 检查清单A：VirtualShadowMapManager配置

```
1. Hierarchy中找到有VirtualShadowMapManager的GameObject
2. 选中它，查看Inspector
3. 检查以下字段是否都已分配：
```

**Compute Shaders部分：**
- [ ] Clear Visible Flags Shader → VSMClearVisibleFlags
- [ ] Free Invalidated Pages Shader → VSMFreeInvalidatedPages
- [ ] Mark Visible Pages Shader → VSMMarkVisiblePages
- [ ] Allocate Pages Shader → VSMAllocatePages
- [ ] Clear Pages Shader → VSMClearPages
- [ ] **Clear Memory Shader → VSMClearMemory** ← 经常漏掉！
- [ ] Build HPB Shader → VSMBuildHPB

**Rendering部分：**
- [ ] VSM Depth Shader → VSMDepthRender

**VSM Settings部分：**
- [ ] Directional Light → 场景中的Directional Light

### 检查清单B：场景设置

- [ ] 场景中有Directional Light？
- [ ] Main Camera有VirtualShadowMapManager组件？
- [ ] 场景在Play模式下运行？
- [ ] 物体在相机视野内？

---

## 如果是绿色但还是没阴影

### 问题：VSM工作了，但shader没有正确应用阴影

### 3.1 切换到Shadow测试模式
```
VSM_Test材质 > Test Mode = Shadow
```

**期望结果：**
- 应该看到黑白对比
- 黑色 = 阴影区域
- 白色 = 光照区域

**如果看到：**
- 全白色 → 没有采样到阴影数据
- 全黄色 → 页面未分配（回到红色的检查）
- 有黑白对比 → VSM采样工作了！

### 3.2 如果有黑白对比但ForwardLit没阴影

**问题出在VSM/ForwardLit shader**

打开 `Assets/Shaders/VSMForwardLit.shader`

找到fragment shader中的这段代码（约154-158行）：

```hlsl
// Sample VSM shadow
float shadow = SampleVSMShadow(i.worldPos);

// Combine lighting
float3 diffuse = _LightColor0.rgb * ndotl * shadow;
float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;
```

临时改为：
```hlsl
// 强制阴影测试
float shadow = 0.5; // 强制50%阴影

float3 diffuse = _LightColor0.rgb * ndotl * shadow;
float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb;
```

如果看到物体变暗了，说明shader能工作，问题在SampleVSMShadow函数。

---

## 第四步：检查VSM Preview窗口

```
Unity菜单 > Window > VSM Preview
```

### 上半部分：Physical Memory Atlas

**应该看到：**
- 不是全白的纹理
- 有一些深度数据（灰色/黑色区域）

**如果是全白：**
→ 没有渲染任何深度
→ 检查VSM Depth Shader是否分配
→ 检查物体是否在Shadow Casters layer

### 下半部分：Page Table

**应该看到：**
- 有一些已分配的页面（不是全黑）

**如果是全黑：**
→ 没有分配任何页面
→ Mark Visible Pages shader可能没运行

---

## 常见错误和解决方案

### ❌ 错误1：clearMemoryShader未分配

**症状：**
- Test Mode = PageAlloc 显示红色
- VSM Preview全白

**解决：**
```
1. 选中VirtualShadowMapManager
2. 找到 Clear Memory Shader 字段
3. 拖入 VSMClearMemory.compute
```

### ❌ 错误2：没在Play模式

**症状：**
- 什么都看不到

**解决：**
```
点击Unity的Play按钮！
VSM只在运行时工作
```

### ❌ 错误3：物体Layer不对

**症状：**
- Preview窗口没有深度数据

**解决：**
```
1. 选中VirtualShadowMapManager
2. Shadow Casters设置为 "Everything"
```

### ❌ 错误4：光源设置问题

**症状：**
- Test Mode = PageAlloc 显示紫色

**解决：**
```
增加 First Cascade Size
默认值2.0可能太小，试试10.0或更大
```

---

## 快速诊断流程图

```
开始
  ↓
运行VSM Debug Checker → 有错误？→ 修复错误
  ↓ 无错误
使用VSM/Test材质
  ↓
Test Mode = PageAlloc
  ↓
红色？→ 检查VirtualShadowMapManager配置
  ↓ 绿色
Test Mode = Shadow
  ↓
有黑白对比？→ 检查VSM/ForwardLit shader
  ↓ 没有对比
检查VSM Preview窗口
  ↓
Physical Memory有数据？→ 检查采样代码
  ↓ 没数据
检查Depth Shader和Shadow Casters
```

---

## 终极测试场景

创建一个最小测试场景：

```
1. 新建空场景
2. GameObject > 3D Object > Plane（地面）
3. GameObject > 3D Object > Cube（投影物体）
   - Position: (0, 1, 0)
4. GameObject > Light > Directional Light
5. GameObject > Camera（确保能看到Plane和Cube）
6. 给Camera添加VirtualShadowMapManager
   - 分配所有Compute Shader
   - 分配Directional Light
   - First Cascade Size = 10
7. 给Plane和Cube都用VSM/Test材质
8. Test Mode = PageAlloc
9. Play

预期：都应该是绿色
```

如果最小场景都不工作，说明VSM系统配置有问题。

---

## 提供诊断信息

如果以上都试了还不行，请提供：

1. VSM Debug Checker的Console输出（截图）
2. VSM/Test材质 PageAlloc模式的截图
3. VSM Preview窗口的截图
4. VirtualShadowMapManager Inspector的截图
5. Console中的任何错误信息
