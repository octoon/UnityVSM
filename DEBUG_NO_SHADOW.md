# 没有阴影？调试步骤

## 快速诊断流程

### 步骤1: 使用测试shader

1. 创建材质，Shader选择 `VSM/Test`
2. 应用到场景中的一个物体
3. 运行场景

4. 在材质Inspector中切换Test Mode：

#### Test Mode = Color
- **看到你设置的颜色** ✅ Shader可以工作
- **看到黑色/错误颜色** ❌ Shader有问题

#### Test Mode = PageAlloc （最重要！）
- **绿色** ✅ VSM页面已分配，系统工作正常
- **红色** ❌ VSM页面未分配 → VSM系统没运行
- **紫色** ⚠️ 物体在光源范围外
- **青色** ⚠️ Wrapped坐标失败

#### Test Mode = Shadow
- **白色** = 光照区域
- **黑色** = 阴影区域
- **黄色** = 页面未分配
- **紫色** = 超出范围
- **青色** = Wrapped失败

#### Test Mode = Depth
- **白色** = 近处
- **灰色** = 中等深度
- **黑色** = 远处或未分配

---

## 如果Test Mode = PageAlloc显示红色

### 问题：VSM系统没有运行或页面没有分配

**检查清单：**

1. **VirtualShadowMapManager是否在场景中？**
   ```
   - 必须有GameObject附带VirtualShadowMapManager组件
   - 必须在Play模式下运行
   ```

2. **所有Compute Shader是否已分配？**
   ```
   在VirtualShadowMapManager Inspector中检查：
   ✅ Clear Visible Flags Shader
   ✅ Free Invalidated Pages Shader
   ✅ Mark Visible Pages Shader
   ✅ Allocate Pages Shader
   ✅ Clear Pages Shader
   ✅ Clear Memory Shader (新增！)
   ✅ Build HPB Shader
   ```

3. **Directional Light是否已分配？**
   ```
   - VirtualShadowMapManager需要引用场景中的方向光
   ```

4. **物体是否在Shadow Casters Layer？**
   ```
   - 检查物体的Layer
   - 检查VirtualShadowMapManager的shadowCasters设置
   ```

5. **打开VSM Preview窗口检查**
   ```
   Window > VSM Preview

   检查：
   - Physical Memory是否有深度数据（应该不是全白）
   - Page Table是否有分配（应该有颜色）
   ```

---

## 如果Test Mode = PageAlloc显示绿色，但还是没阴影

### 问题：VSM工作了，但shader采样有问题

1. **检查VSMForwardLit.shader是否正确**

   打开 `Assets/Shaders/VSMForwardLit.shader`

   确认fragment shader中有这段代码：
   ```hlsl
   // Sample VSM shadow
   float shadow = SampleVSMShadow(i.worldPos);

   // Apply lighting and shadows
   float3 lighting = _LightColor0.rgb * ndotl;
   lighting = lerp(lighting * (1.0 - _ShadowStrength), lighting, shadow);
   ```

2. **检查全局变量是否绑定**

   在Console中应该没有这些错误：
   - "Shader wants XXX but it wasn't set"
   - "Buffer not found"

3. **测试深度比较逻辑**

   在VSMForwardLit.shader中临时修改：
   ```hlsl
   // 原来：
   float shadow = SampleVSMShadow(i.worldPos);

   // 改为：
   float shadow = 0.5; // 强制50%阴影
   ```

   如果看到变化，说明shader工作了，问题在SampleVSMShadow函数

---

## 如果Test Mode = Shadow显示黄色

### 问题：到达shader了，但页面没分配

这和红色的问题一样，说明VSM系统没有为这些像素分配页面。

**可能原因：**
- 物体不在相机视锥内
- MarkVisiblePages shader没运行
- 物理内存满了（MAX_PHYSICAL_PAGES不够）

**解决：**
1. 确保物体在相机视野内
2. 增加MAX_PHYSICAL_PAGES (VSMConstants.cs)
3. 检查MarkVisiblePages compute shader是否运行

---

## 如果Test Mode = Shadow显示紫色/青色

### 问题：坐标系统有问题

**紫色 = 超出光源范围**
- 检查First Cascade Size是否太小
- 增加Cascade Count

**青色 = Wrapped坐标失败**
- VirtualPageCoordsToWrappedCoords返回了(-1, -1)
- 检查Cascade Offsets是否正确

---

## 最常见的问题

### ❌ 忘记分配Compute Shader
特别是新增的 `Clear Memory Shader`！

### ❌ 没有运行Play模式
VSM只在Play模式下工作

### ❌ 物体Layer不对
检查shadowCasters Layer Mask

### ❌ 光源太远或太近
调整First Cascade Size

---

## 使用VSM Preview窗口

```
Window > VSM Preview

上半部分 - Physical Memory Atlas:
- 应该看到深度图（不是全白）
- 如果全白 = 没有渲染任何深度

下半部分 - Page Table:
- 应该看到一些已分配的页面
- 如果全黑 = 没有分配任何页面
```

---

## 终极测试

创建一个最简单的场景：

```
1. 新建空场景
2. 添加Plane（地面）
3. 添加Cube（投影物体）
4. 添加Directional Light
5. 添加Main Camera
6. Main Camera添加VirtualShadowMapManager组件
   - 分配所有Compute Shader
   - 分配Directional Light
   - 分配VSM Depth Shader
7. 给Plane和Cube都用VSM/Test材质
   - Test Mode = PageAlloc
8. Play

预期结果：
- 应该看到绿色（页面已分配）
```

如果这个最简单的场景都不工作，说明VSM系统本身有问题。

---

## 联系信息

如果以上都检查了还是不行，请提供：
1. VSM/Test shader的截图（各个Test Mode）
2. VSM Preview窗口的截图
3. Unity Console的错误信息
4. VirtualShadowMapManager Inspector的截图
