// 临时测试：禁用sliding window，看是否解决圆圈+方形问题

在 VirtualShadowMapManager.cs UpdateCascadeMatrices() 中，
临时将所有offset设为0：

```csharp
// 测试：禁用sliding window
cascadeOffsets[i] = Vector2Int.zero;  // 强制offset为0
```

这样可以验证问题是否出在offset计算上。
如果圆圈+方形消失，说明offset计算有问题。
如果还在，说明问题在UV映射本身。
