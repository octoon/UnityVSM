using UnityEngine;
using UnityEngine.Rendering;

namespace VSM
{
    /// <summary>
    /// Hierarchical Page Buffer (HPB) for efficient culling
    /// Similar to Hi-Z but stores dirty page flags instead of depth
    /// </summary>
    public class VSMHierarchicalPageBuffer
    {
        private RenderTexture[] hpbMipChain;
        private ComputeShader buildHPBShader;

        public RenderTexture GetMipLevel(int level) => hpbMipChain[level];

        public VSMHierarchicalPageBuffer(ComputeShader buildShader)
        {
            buildHPBShader = buildShader;
            InitializeResources();
        }

        private void InitializeResources()
        {
            // Create mip chain for HPB
            // Each level is a 2x2 reduction of dirty flags
            hpbMipChain = new RenderTexture[VSMConstants.HPB_MIP_LEVELS];

            int resolution = VSMConstants.PAGE_TABLE_RESOLUTION;
            for (int i = 0; i < VSMConstants.HPB_MIP_LEVELS; i++)
            {
                hpbMipChain[i] = new RenderTexture(
                    resolution,
                    resolution,
                    0,
                    RenderTextureFormat.R8,
                    RenderTextureReadWrite.Linear)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = VSMConstants.CASCADE_COUNT,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = $"VSM_HPB_Mip{i}"
                };
                hpbMipChain[i].Create();

                resolution = Mathf.Max(1, resolution / 2);
            }
        }

        public void BuildHPB(RenderTexture virtualPageTable)
        {
            if (buildHPBShader == null)
                return;

            int buildKernel = buildHPBShader.FindKernel("BuildHPB");

            // Build each mip level
            for (int i = 0; i < VSMConstants.HPB_MIP_LEVELS; i++)
            {
                if (i == 0)
                {
                    // Base level - extract dirty flags from VPT
                    buildHPBShader.SetTexture(buildKernel, "_VirtualPageTable", virtualPageTable);
                    buildHPBShader.SetTexture(buildKernel, "_SourceHPB", virtualPageTable); // Set as dummy to avoid error
                }
                else
                {
                    // Subsequent levels - reduce previous level
                    buildHPBShader.SetTexture(buildKernel, "_VirtualPageTable", virtualPageTable);
                    buildHPBShader.SetTexture(buildKernel, "_SourceHPB", hpbMipChain[i - 1]);
                }

                buildHPBShader.SetTexture(buildKernel, "_OutputHPB", hpbMipChain[i]);
                buildHPBShader.SetInt("_MipLevel", i);
                buildHPBShader.SetInt("_Resolution", hpbMipChain[i].width);

                int threadGroups = Mathf.CeilToInt(hpbMipChain[i].width / 8.0f);
                buildHPBShader.Dispatch(buildKernel, threadGroups, threadGroups, VSMConstants.CASCADE_COUNT);
            }
        }

        public void Release()
        {
            if (hpbMipChain != null)
            {
                foreach (var rt in hpbMipChain)
                {
                    if (rt != null)
                    {
                        rt.Release();
                    }
                }
                hpbMipChain = null;
            }
        }
    }
}
