## 问题诊断

### 圆圈+方形 Artifact 的原因分析

**观察**:
- Mode 2 (shadow depth): 紫红色圆圈 + 灰色方形
- Mode 5 (page allocation): 紫红色圆圈 + 黑色方形
- 方形恰好在圆圈内部

**假设 1**: Y轴翻转问题
- MarkVisiblePages 在 line 38: `clipPos.y = -clipPos.y`
- 这可能导致标记的页面和渲染的页面在Y轴上镜像

**测试方法**:
1. 临时注释掉 `clipPos.y = -clipPos.y`
2. 看圆圈+方形是否变化

**假设 2**: 坐标精度问题
- 相机深度重建的世界坐标 vs 直接的世界坐标
- 微小差异导致不同的page coords

**假设 3**: UV映射范围问题
- MarkVisiblePages 标记的 UV 范围
- 实际渲染的 UV 范围
- 两者可能有偏移

### 下一步测试

尝试注释掉 Y 翻转，看看是否解决问题。
