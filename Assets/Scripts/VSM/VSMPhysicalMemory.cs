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
        private ComputeBuffer physicalMemoryBuffer;  // For atomic writes from fragment shader
        private ComputeShader clearShader;
        private ComputeShader copyBufferToTextureShader;  // Copy buffer back to texture for sampling

        public RenderTexture Texture => physicalMemoryTexture;
        public ComputeBuffer Buffer => physicalMemoryBuffer;

        public VSMPhysicalMemory(ComputeShader clearMemoryShader = null, ComputeShader copyShader = null)
        {
            clearShader = clearMemoryShader;
            copyBufferToTextureShader = copyShader;
            Debug.LogWarning($"[VSM PhysicalMemory] CONSTRUCTOR called! Stack trace:\n{System.Environment.StackTrace}");
            InitializeResources();
        }

        private void InitializeResources()
        {
            // Create RenderTexture for sampling (read-only in shaders that sample shadows)
            physicalMemoryTexture = new RenderTexture(
                VSMConstants.PHYSICAL_MEMORY_WIDTH,
                VSMConstants.PHYSICAL_MEMORY_HEIGHT,
                0,
                RenderTextureFormat.RFloat,  // Float for sampling
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,  // Need write access for copy operation
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "VSM_PhysicalMemory_Texture"
            };
            physicalMemoryTexture.Create();

            // Create ComputeBuffer for atomic writes (RWStructuredBuffer in shaders)
            int totalTexels = VSMConstants.PHYSICAL_MEMORY_WIDTH * VSMConstants.PHYSICAL_MEMORY_HEIGHT;
            physicalMemoryBuffer = new ComputeBuffer(totalTexels, sizeof(uint));

            // Clear to far plane (1.0 as uint bits: 0x3F800000)
            ClearMemory();
        }

        public void ClearMemory()
        {
            Debug.Log($"[VSM PhysicalMemory] ClearMemory() called - clearing {physicalMemoryBuffer.count} texels");

            // TEMP: Force CPU clear for debugging
            Debug.LogWarning("[VSM PhysicalMemory] FORCING CPU clear for debugging");
            int totalTexels = VSMConstants.PHYSICAL_MEMORY_WIDTH * VSMConstants.PHYSICAL_MEMORY_HEIGHT;
            uint[] clearData = new uint[totalTexels];
            uint farPlane = asuint(1.0f);
            Debug.Log($"[VSM PhysicalMemory] Filling array with value 0x{farPlane:X8} (should be 0x3F800000 for 1.0f)");

            for (int i = 0; i < totalTexels; i++)
            {
                clearData[i] = farPlane;
            }

            Debug.Log($"[VSM PhysicalMemory] Array filled, calling SetData...");
            physicalMemoryBuffer.SetData(clearData);
            Debug.Log($"[VSM PhysicalMemory] CPU clear completed successfully. Buffer InstanceID: {physicalMemoryBuffer.GetNativeBufferPtr().ToInt64():X}");
            return;

            // Old GPU clear code (disabled for debugging)
            /*
            // Clear compute buffer to 1.0 (far plane)
            if (clearShader != null)
            {
                int kernel = clearShader.FindKernel("ClearBufferToOne");
                clearShader.SetBuffer(kernel, "_PhysicalMemoryBuffer", physicalMemoryBuffer);
                clearShader.SetInt("_TotalTexels", physicalMemoryBuffer.count);

                // GPU limit: max 65535 thread groups per dimension
                // Each thread group has 256 threads, so max threads per dispatch = 65535 * 256 = 16,776,960
                int totalTexels = physicalMemoryBuffer.count;
                int threadsPerGroup = 256;
                int maxThreadsPerDispatch = 65535 * threadsPerGroup;

                // Dispatch in batches if needed
                int offset = 0;
                while (offset < totalTexels)
                {
                    int remainingTexels = totalTexels - offset;
                    int texelsThisDispatch = Mathf.Min(remainingTexels, maxThreadsPerDispatch);
                    int threadGroups = Mathf.CeilToInt(texelsThisDispatch / (float)threadsPerGroup);

                    clearShader.SetInt("_Offset", offset);
                    clearShader.Dispatch(kernel, threadGroups, 1, 1);

                    offset += texelsThisDispatch;
                }

                Debug.Log($"[VSM PhysicalMemory] ClearMemory() completed - dispatched {Mathf.CeilToInt(totalTexels / (float)maxThreadsPerDispatch)} batches");
            }
            else
            {
                // Fallback: clear on CPU
                Debug.LogWarning("[VSM PhysicalMemory] ClearShader is NULL - using CPU clear");
                int totalTexels = VSMConstants.PHYSICAL_MEMORY_WIDTH * VSMConstants.PHYSICAL_MEMORY_HEIGHT;
                uint[] clearData = new uint[totalTexels];
                uint farPlane = asuint(1.0f);
                for (int i = 0; i < totalTexels; i++)
                {
                    clearData[i] = farPlane;
                }
                physicalMemoryBuffer.SetData(clearData);
            }
            */
        }

        private float asfloat(uint value)
        {
            byte[] bytes = System.BitConverter.GetBytes(value);
            return System.BitConverter.ToSingle(bytes, 0);
        }

        private uint asuint(float value)
        {
            byte[] bytes = System.BitConverter.GetBytes(value);
            return System.BitConverter.ToUInt32(bytes, 0);
        }

        public void CopyBufferToTexture()
        {
            // Copy from buffer (written by fragment shaders) to texture (for sampling)
            if (copyBufferToTextureShader != null)
            {
                int kernel = copyBufferToTextureShader.FindKernel("CopyBufferToTexture");
                copyBufferToTextureShader.SetBuffer(kernel, "_PhysicalMemoryBuffer", physicalMemoryBuffer);
                copyBufferToTextureShader.SetTexture(kernel, "_PhysicalMemoryTexture", physicalMemoryTexture);
                copyBufferToTextureShader.SetInt("_Width", VSMConstants.PHYSICAL_MEMORY_WIDTH);
                copyBufferToTextureShader.SetInt("_Height", VSMConstants.PHYSICAL_MEMORY_HEIGHT);

                copyBufferToTextureShader.Dispatch(kernel,
                    Mathf.CeilToInt(VSMConstants.PHYSICAL_MEMORY_WIDTH / 8.0f),
                    Mathf.CeilToInt(VSMConstants.PHYSICAL_MEMORY_HEIGHT / 8.0f),
                    1);
            }
        }

        public void Release()
        {
            if (physicalMemoryTexture != null)
            {
                physicalMemoryTexture.Release();
                physicalMemoryTexture = null;
            }

            if (physicalMemoryBuffer != null)
            {
                physicalMemoryBuffer.Release();
                physicalMemoryBuffer = null;
            }
        }
    }
}
