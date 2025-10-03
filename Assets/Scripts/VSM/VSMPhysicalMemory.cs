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

        public RenderTexture Texture => physicalMemoryTexture;

        public VSMPhysicalMemory()
        {
            InitializeResources();
        }

        private void InitializeResources()
        {
            // Create large physical memory texture to store all pages
            // Using R32_Float for depth storage
            physicalMemoryTexture = new RenderTexture(
                VSMConstants.PHYSICAL_MEMORY_WIDTH,
                VSMConstants.PHYSICAL_MEMORY_HEIGHT,
                0,
                RenderTextureFormat.RFloat,
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "VSM_PhysicalMemory"
            };
            physicalMemoryTexture.Create();

            // Clear to far plane (1.0)
            ClearMemory();
        }

        public void ClearMemory()
        {
            RenderTexture.active = physicalMemoryTexture;
            GL.Clear(false, true, new Color(1, 1, 1, 1));  // Clear to 1.0 (far plane)
            RenderTexture.active = null;
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
