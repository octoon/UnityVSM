using UnityEngine;
using UnityEngine.Rendering;

namespace VSM
{
    /// <summary>
    /// Physical memory texture that stores actual shadow depth data
    /// Organized as a grid of pages
    /// </summary>
    public class VSMPhysicalMemory
    {
        private RenderTexture physicalMemoryTexture;
        private ComputeShader clearShader;

        public RenderTexture Texture => physicalMemoryTexture;

        public VSMPhysicalMemory(ComputeShader clearMemoryShader = null)
        {
            clearShader = clearMemoryShader;
            InitializeResources();
        }

        private void InitializeResources()
        {
            // Create large physical memory texture to store all pages
            // Using R32_UInt for depth storage (required for InterlockedMin atomic operations)
            // Depth values are stored as asuint(depth) for atomic min operations
            physicalMemoryTexture = new RenderTexture(
                VSMConstants.PHYSICAL_MEMORY_WIDTH,
                VSMConstants.PHYSICAL_MEMORY_HEIGHT,
                0,
                RenderTextureFormat.RInt,
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "VSM_PhysicalMemory"
            };
            physicalMemoryTexture.Create();

            // Clear to far plane (1.0 stored as uint)
            ClearMemory();
        }

        public void ClearMemory()
        {
            // Clear uint texture to asuint(1.0) = 0x3F800000
            if (clearShader != null)
            {
                int kernel = clearShader.FindKernel("ClearToOne");
                clearShader.SetTexture(kernel, "_Target", physicalMemoryTexture);
                clearShader.Dispatch(kernel,
                    Mathf.CeilToInt(VSMConstants.PHYSICAL_MEMORY_WIDTH / 8.0f),
                    Mathf.CeilToInt(VSMConstants.PHYSICAL_MEMORY_HEIGHT / 8.0f),
                    1);
            }
            // If no clear shader, texture will be cleared by ClearPages during allocation
        }

        public void Release()
        {
            if (physicalMemoryTexture != null)
            {
                physicalMemoryTexture.Release();
                physicalMemoryTexture = null;
            }
        }
    }
}
