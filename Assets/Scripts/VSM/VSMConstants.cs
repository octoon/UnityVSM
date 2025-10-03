using UnityEngine;

namespace VSM
{
    /// <summary>
    /// Virtual Shadow Map constants based on GPU Zen3 VSM paper
    /// </summary>
    public static class VSMConstants
    {
        // Virtual page table resolution per cascade
        public const int PAGE_TABLE_RESOLUTION = 32;  // 32x32 pages per cascade

        // Physical page size in texels
        public const int PAGE_SIZE = 128;  // 128x128 texels per page

        // Virtual resolution per cascade
        public const int VIRTUAL_TEXTURE_RESOLUTION = PAGE_TABLE_RESOLUTION * PAGE_SIZE;  // 4096x4096

        // Number of cascades
        public const int CASCADE_COUNT = 16;

        // Maximum physical pages available
        public const int MAX_PHYSICAL_PAGES = 2048;

        // Physical page memory dimensions (square root of max pages)
        public const int PHYSICAL_PAGE_COLS = 64;  // 64x32 = 2048 pages
        public const int PHYSICAL_PAGE_ROWS = 32;

        // Physical memory texture resolution
        public const int PHYSICAL_MEMORY_WIDTH = PHYSICAL_PAGE_COLS * PAGE_SIZE;  // 8192
        public const int PHYSICAL_MEMORY_HEIGHT = PHYSICAL_PAGE_ROWS * PAGE_SIZE;  // 4096

        // First cascade size in world units (meters)
        public const float FIRST_CASCADE_SIZE = 2.0f;

        // Page entry bit packing
        public const uint ALLOCATED_BIT = 1u << 0;
        public const uint VISIBLE_BIT = 1u << 1;
        public const uint DIRTY_BIT = 1u << 2;
        public const uint PHYSICAL_X_SHIFT = 3;
        public const uint PHYSICAL_Y_SHIFT = 14;
        public const uint PHYSICAL_COORD_MASK = 0x7FF;  // 11 bits for coordinates

        // HPB (Hierarchical Page Buffer) mip levels
        public const int HPB_MIP_LEVELS = 6;  // log2(32) + 1
    }

    /// <summary>
    /// Page entry structure for Virtual Page Table
    /// Bit layout:
    /// - Bit 0: Allocated flag
    /// - Bit 1: Visible flag
    /// - Bit 2: Dirty flag
    /// - Bits 3-13: Physical page X coordinate (11 bits)
    /// - Bits 14-24: Physical page Y coordinate (11 bits)
    /// </summary>
    public struct PageEntry
    {
        public uint data;

        public bool IsAllocated => (data & VSMConstants.ALLOCATED_BIT) != 0;
        public bool IsVisible => (data & VSMConstants.VISIBLE_BIT) != 0;
        public bool IsDirty => (data & VSMConstants.DIRTY_BIT) != 0;

        public Vector2Int PhysicalCoords
        {
            get
            {
                int x = (int)((data >> (int)VSMConstants.PHYSICAL_X_SHIFT) & VSMConstants.PHYSICAL_COORD_MASK);
                int y = (int)((data >> (int)VSMConstants.PHYSICAL_Y_SHIFT) & VSMConstants.PHYSICAL_COORD_MASK);
                return new Vector2Int(x, y);
            }
        }

        public static uint Pack(bool allocated, bool visible, bool dirty, int physicalX, int physicalY)
        {
            uint result = 0;
            if (allocated) result |= VSMConstants.ALLOCATED_BIT;
            if (visible) result |= VSMConstants.VISIBLE_BIT;
            if (dirty) result |= VSMConstants.DIRTY_BIT;
            result |= ((uint)physicalX & VSMConstants.PHYSICAL_COORD_MASK) << (int)VSMConstants.PHYSICAL_X_SHIFT;
            result |= ((uint)physicalY & VSMConstants.PHYSICAL_COORD_MASK) << (int)VSMConstants.PHYSICAL_Y_SHIFT;
            return result;
        }
    }
}
