using UnityEngine;
using UnityEngine.Rendering;

namespace VSM
{
    /// <summary>
    /// Virtual Page Table - maps virtual pages to physical pages
    /// Stores allocation, visibility, and dirty flags plus physical page coordinates
    /// </summary>
    public class VSMPageTable
    {
        private RenderTexture virtualPageTable;  // R32_UInt texture
        private ComputeBuffer allocationRequestsBuffer;
        private ComputeBuffer freePhysicalPagesBuffer;
        private ComputeBuffer usedPhysicalPagesBuffer;

        // Per-cascade data
        private Vector2Int[] cascadeOffsets;

        public RenderTexture VirtualPageTableTexture => virtualPageTable;
        public ComputeBuffer AllocationRequests => allocationRequestsBuffer;
        public ComputeBuffer FreePhysicalPages => freePhysicalPagesBuffer;
        public ComputeBuffer UsedPhysicalPages => usedPhysicalPagesBuffer;

        public VSMPageTable()
        {
            InitializeResources();
        }

        private void InitializeResources()
        {
            // Create 3D texture: resolution x resolution x cascade_count
            virtualPageTable = new RenderTexture(
                VSMConstants.PAGE_TABLE_RESOLUTION,
                VSMConstants.PAGE_TABLE_RESOLUTION,
                0,
                RenderTextureFormat.RInt,
                RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = VSMConstants.CASCADE_COUNT,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "VSM_VirtualPageTable"
            };
            virtualPageTable.Create();

            // Clear to zero (unallocated)
            ClearPageTable();

            // Allocation requests buffer (max one request per page per cascade)
            int maxRequests = VSMConstants.PAGE_TABLE_RESOLUTION * VSMConstants.PAGE_TABLE_RESOLUTION * VSMConstants.CASCADE_COUNT;
            allocationRequestsBuffer = new ComputeBuffer(maxRequests, sizeof(uint) * 4, ComputeBufferType.Append);

            // Free and used physical page buffers
            freePhysicalPagesBuffer = new ComputeBuffer(VSMConstants.MAX_PHYSICAL_PAGES, sizeof(uint) * 2, ComputeBufferType.Append);
            usedPhysicalPagesBuffer = new ComputeBuffer(VSMConstants.MAX_PHYSICAL_PAGES, sizeof(uint) * 2, ComputeBufferType.Append);

            // Initialize cascade offsets for sliding window
            cascadeOffsets = new Vector2Int[VSMConstants.CASCADE_COUNT];
            for (int i = 0; i < VSMConstants.CASCADE_COUNT; i++)
            {
                cascadeOffsets[i] = Vector2Int.zero;
            }
        }

        public void ClearPageTable()
        {
            // Clear all pages to unallocated state
            RenderTexture.active = virtualPageTable;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = null;
        }

        public Vector2Int GetCascadeOffset(int cascadeIndex)
        {
            return cascadeOffsets[cascadeIndex];
        }

        public void SetCascadeOffset(int cascadeIndex, Vector2Int offset)
        {
            cascadeOffsets[cascadeIndex] = offset;
        }

        public void ResetAllocationRequests()
        {
            allocationRequestsBuffer.SetCounterValue(0);
        }

        public void ResetPhysicalPageBuffers()
        {
            freePhysicalPagesBuffer.SetCounterValue(0);
            usedPhysicalPagesBuffer.SetCounterValue(0);
        }

        public void Release()
        {
            if (virtualPageTable != null)
            {
                virtualPageTable.Release();
                virtualPageTable = null;
            }

            if (allocationRequestsBuffer != null)
            {
                allocationRequestsBuffer.Release();
                allocationRequestsBuffer = null;
            }

            if (freePhysicalPagesBuffer != null)
            {
                freePhysicalPagesBuffer.Release();
                freePhysicalPagesBuffer = null;
            }

            if (usedPhysicalPagesBuffer != null)
            {
                usedPhysicalPagesBuffer.Release();
                usedPhysicalPagesBuffer = null;
            }
        }
    }

    /// <summary>
    /// Physical Page Table - inverse mapping from physical pages to virtual pages
    /// </summary>
    public class VSMPhysicalPageTable
    {
        private ComputeBuffer physicalPageTable;

        public ComputeBuffer Buffer => physicalPageTable;

        public VSMPhysicalPageTable()
        {
            InitializeResources();
        }

        private void InitializeResources()
        {
            // Each entry stores: virtual page X, Y, cascade index, and flags
            physicalPageTable = new ComputeBuffer(VSMConstants.MAX_PHYSICAL_PAGES, sizeof(uint) * 4);

            // Initialize to empty
            uint[] emptyData = new uint[VSMConstants.MAX_PHYSICAL_PAGES * 4];
            physicalPageTable.SetData(emptyData);
        }

        public void Release()
        {
            if (physicalPageTable != null)
            {
                physicalPageTable.Release();
                physicalPageTable = null;
            }
        }
    }
}
