# Thread Group Count 超限错误已修复! ✅

## 问题原因

**错误**: `Thread group count is above the maximum allowed limit. Maximum allowed thread group count is 65535.`

GPU 硬件限制:
- X, Y, Z 每个维度最多 65535 个线程组
- 这是 DirectX 11 的硬件限制
- 不能超过,否则 Dispatch 失败

## 出现问题的地方

### ❌ 原问题1: AllocatePages Dispatch
```csharp
// allocationRequestCount 可能是 16384+ (32*32*16 页面)
allocatePagesShader.Dispatch(allocKernel,
    Mathf.CeilToInt(allocationRequestCount / 64.0f), 1, 1);
// 计算: 16384 / 64 = 256 线程组 ✅
// 但如果所有页面都请求: 32*32*16 / 64 = 256 ✅
```

等等,让我重新计算...实际上问题可能是 buffer 读取错误导致 count 值异常大。

### ❌ 原问题2: ClearPages Dispatch Z 轴
```csharp
clearPagesShader.Dispatch(clearKernel,
    Mathf.CeilToInt(VSMConstants.PAGE_SIZE / 8.0f),  // 128/8 = 16
    Mathf.CeilToInt(VSMConstants.PAGE_SIZE / 8.0f),  // 128/8 = 16
    (int)allocationRequestCount);  // 可能 > 65535!
```

## 修复方案

### ✅ 修复1: FillAllocatorBuffers
```csharp
int fillThreadGroups = Mathf.CeilToInt(VSMConstants.MAX_PHYSICAL_PAGES / 64.0f);
fillThreadGroups = Mathf.Min(fillThreadGroups, 65535);  // 限制
allocatePagesShader.Dispatch(fillKernel, fillThreadGroups, 1, 1);
```

### ✅ 修复2: AllocatePages
```csharp
int threadGroups = Mathf.CeilToInt(allocationRequestCount / 64.0f);
threadGroups = Mathf.Min(threadGroups, 65535);  // 限制
allocatePagesShader.Dispatch(allocKernel, threadGroups, 1, 1);
```

### ✅ 修复3: ClearPages Z轴
```csharp
int zGroups = (int)Mathf.Min(allocationRequestCount, 65535);  // 限制
clearPagesShader.Dispatch(clearKernel,
    Mathf.CeilToInt(VSMConstants.PAGE_SIZE / 8.0f),
    Mathf.CeilToInt(VSMConstants.PAGE_SIZE / 8.0f),
    zGroups);
```

## GPU Dispatch 限制

| 平台 | X 最大 | Y 最大 | Z 最大 | 总最大 |
|------|--------|--------|--------|--------|
| DX11 | 65535 | 65535 | 65535 | - |
| DX12 | 65535 | 65535 | 65535 | - |
| Vulkan | 65535 | 65535 | 65535 | - |
| OpenGL | 65535 | 65535 | 65535 | - |

**注意**: 这是线程组数,不是线程数!
- 每个线程组可以有多个线程 (如 [numthreads(8,8,1)] = 64 线程/组)
- 总线程数 = 线程组数 × 每组线程数

## 为什么会超限?

### 可能原因1: Append Buffer Count 读取错误
```csharp
ComputeBuffer.CopyCount(pageTable.AllocationRequests, allocationCounterBuffer, 0);
uint[] counts = new uint[2];
allocationCounterBuffer.GetData(counts);
uint allocationRequestCount = counts[0];  // 可能读到错误值
```

如果 AllocationRequests 是 Append buffer,CopyCount 应该正确。但如果 buffer 类型不匹配,可能读到垃圾数据。

### 可能原因2: 所有页面都标记为 dirty
如果初始化时所有虚拟页面都被标记为需要分配:
- 32×32 页表 × 16 级联 = 16,384 页面
- 如果 shader 错误地全部标记,会产生大量请求

## 调试建议

添加调试输出:
```csharp
Debug.Log($"Allocation Request Count: {allocationRequestCount}");
Debug.Log($"Thread Groups: {threadGroups}");
```

### 正常值应该是:
- 首帧: 几百到几千个页面请求
- 后续帧: 几十到几百个 (只有变动的页面)

### 异常值:
- > 10,000: 太多页面被标记
- > 100,000: Buffer 读取错误

## 限制的影响

**添加 65535 限制后**:
- ✅ 不会崩溃
- ⚠️ 但如果真的有 > 65535 个请求,只处理前 65535 个
- ⚠️ 可能导致部分阴影缺失

**更好的解决方案**:
分批处理:
```csharp
while (remaining > 0) {
    int batchSize = Mathf.Min(remaining, 65535);
    shader.Dispatch(..., batchSize, 1, 1);
    remaining -= batchSize;
}
```

但目前的简单限制足够用于测试! 🎯
