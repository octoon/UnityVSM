using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace VSM
{
    /// <summary>
    /// VSM Meshlet渲染器
    /// 论文实现：Task Shader剔除 + Mesh Shader渲染
    /// Unity实现：Compute Shader剔除 + Instanced渲染
    /// </summary>
    public class VSMMeshletRenderer : MonoBehaviour
    {
        [Header("Meshlet Settings")]
        [SerializeField] private bool enableMeshletRendering = true;
        [SerializeField] private bool autoGenerateMeshlets = true;

        [Header("Shaders")]
        [SerializeField] private ComputeShader taskShader; // VSMTaskShader
        [SerializeField] private Shader meshletRenderShader; // VSMMeshletRender

        // Meshlet数据
        private Dictionary<Mesh, MeshletMesh> meshletCache = new Dictionary<Mesh, MeshletMesh>();
        private Material meshletRenderMaterial;

        // 可见性剔除buffers
        private ComputeBuffer visibleMeshletsBuffer;
        private ComputeBuffer argsBuffer; // 间接渲染参数

        // HPB引用
        private VSMHierarchicalPageBuffer hpb;

        public void Initialize(VSMHierarchicalPageBuffer hierarchicalPageBuffer)
        {
            hpb = hierarchicalPageBuffer;

            if (meshletRenderShader != null)
            {
                meshletRenderMaterial = new Material(meshletRenderShader);
            }

            // 创建可见meshlet buffer（估算最大数量）
            visibleMeshletsBuffer = new ComputeBuffer(
                10000, // 最大可见meshlet数
                sizeof(uint),
                ComputeBufferType.Append
            );

            // 间接渲染参数：indexCount, instanceCount, startIndex, baseVertex, startInstance
            argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        /// <summary>
        /// 获取或创建Mesh的Meshlet表示
        /// </summary>
        public MeshletMesh GetOrCreateMeshlets(Mesh mesh)
        {
            if (mesh == null) return null;

            if (meshletCache.TryGetValue(mesh, out MeshletMesh meshletMesh))
            {
                return meshletMesh;
            }

            // 构建meshlet
            Debug.Log($"Building meshlets for mesh: {mesh.name}");
            meshletMesh = MeshletBuilder.BuildMeshlets(mesh);

            if (meshletMesh != null)
            {
                MeshletBuilder.OptimizeMeshletOrder(meshletMesh);
                meshletMesh.CreateGPUBuffers();
                meshletCache[mesh] = meshletMesh;
            }

            return meshletMesh;
        }

        /// <summary>
        /// 使用Meshlet渲染单个物体到VSM
        /// 论文流程：Task Shader剔除 → Mesh Shader渲染
        /// </summary>
        public void RenderMeshletToVSM(
            MeshletMesh meshletMesh,
            Matrix4x4 modelMatrix,
            int cascadeIndex,
            CommandBuffer cmd,
            ComputeBuffer cascadeLightMatricesBuffer,
            ComputeBuffer cascadeOffsetsBuffer,
            RenderTexture virtualPageTable,
            ComputeBuffer physicalMemoryBuffer)  // Changed from RenderTexture to ComputeBuffer
        {
            if (meshletMesh == null || taskShader == null || meshletRenderMaterial == null)
                return;

            // 步骤1: Task Shader剔除
            // 论文: "The task (or amplification) shader performs frustum culling followed by culling against the HPB"
            visibleMeshletsBuffer.SetCounterValue(0);

            int taskKernel = taskShader.FindKernel("TaskShaderCulling");

            cmd.SetComputeBufferParam(taskShader, taskKernel, "_Meshlets", meshletMesh.meshletBuffer);
            cmd.SetComputeBufferParam(taskShader, taskKernel, "_VisibleMeshlets", visibleMeshletsBuffer);
            cmd.SetComputeTextureParam(taskShader, taskKernel, "_HPB", hpb.GetMipLevel(0));
            cmd.SetComputeBufferParam(taskShader, taskKernel, "_CascadeLightMatrices", cascadeLightMatricesBuffer);
            cmd.SetComputeBufferParam(taskShader, taskKernel, "_CascadeOffsets", cascadeOffsetsBuffer);
            cmd.SetComputeIntParam(taskShader, "_TotalMeshletCount", meshletMesh.meshlets.Length);
            cmd.SetComputeIntParam(taskShader, "_CurrentCascade", cascadeIndex);
            cmd.SetComputeIntParam(taskShader, "_HPBMaxLevel", VSMConstants.HPB_MIP_LEVELS - 1);
            cmd.SetComputeMatrixParam(taskShader, "_ModelMatrix", modelMatrix);

            int threadGroups = Mathf.CeilToInt(meshletMesh.meshlets.Length / 64.0f);
            cmd.DispatchCompute(taskShader, taskKernel, threadGroups, 1, 1);

            // 步骤2: 准备间接渲染参数
            // 论文: "After culling, a group of 32 mesh shader threads is dispatched for each of the surviving meshlets"
            ComputeBuffer.CopyCount(visibleMeshletsBuffer, argsBuffer, 0);

            // 获取可见meshlet数量（仅用于调试）
            uint[] args = new uint[5];
            argsBuffer.GetData(args);
            uint visibleMeshletCount = args[0];

            if (visibleMeshletCount == 0)
                return; // 全部被剔除

            // 设置间接参数
            // indexCount = 每meshlet最大三角形数 * 3
            // instanceCount = 可见meshlet数量
            args[0] = 126 * 3; // MAX_TRIANGLES_PER_MESHLET * 3
            args[1] = visibleMeshletCount;
            args[2] = 0;
            args[3] = 0;
            args[4] = 0;
            argsBuffer.SetData(args);

            // 步骤3: Mesh Shader渲染（间接绘制）
            meshletRenderMaterial.SetBuffer("_Meshlets", meshletMesh.meshletBuffer);
            meshletRenderMaterial.SetBuffer("_MeshletVertices", meshletMesh.vertexBuffer);
            meshletRenderMaterial.SetBuffer("_MeshletTriangles", meshletMesh.triangleBuffer);
            meshletRenderMaterial.SetBuffer("_VisibleMeshletIndices", visibleMeshletsBuffer);
            meshletRenderMaterial.SetTexture("_VirtualPageTable", virtualPageTable);
            meshletRenderMaterial.SetBuffer("_PhysicalMemory", physicalMemoryBuffer);  // Use buffer instead of texture
            meshletRenderMaterial.SetInt("_PhysicalMemoryWidth", VSMConstants.PHYSICAL_MEMORY_WIDTH);  // For 2D to 1D conversion
            meshletRenderMaterial.SetBuffer("_CascadeLightMatrices", cascadeLightMatricesBuffer);
            meshletRenderMaterial.SetBuffer("_CascadeOffsets", cascadeOffsetsBuffer);
            meshletRenderMaterial.SetInt("_CurrentCascade", cascadeIndex);
            meshletRenderMaterial.SetMatrix("_ModelMatrix", modelMatrix);

            // 间接绘制：每个instance代表一个可见的meshlet
            // 使用程序化mesh（三角形列表）
            Mesh proceduralMesh = GetProceduralMesh(126); // 最大126个三角形
            cmd.DrawMeshInstancedIndirect(
                proceduralMesh,
                0,
                meshletRenderMaterial,
                0,
                argsBuffer
            );
        }

        // 创建程序化mesh（简单的三角形索引缓冲）
        private Mesh proceduralMesh = null;
        private Mesh GetProceduralMesh(int maxTriangles)
        {
            if (proceduralMesh != null)
                return proceduralMesh;

            proceduralMesh = new Mesh();
            proceduralMesh.name = "MeshletProceduralMesh";

            // 创建足够的顶点索引
            int[] indices = new int[maxTriangles * 3];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            // 虚拟顶点（实际顶点在shader中生成）
            Vector3[] vertices = new Vector3[maxTriangles * 3];

            proceduralMesh.vertices = vertices;
            proceduralMesh.SetIndices(indices, MeshTopology.Triangles, 0);
            proceduralMesh.UploadMeshData(true); // 标记为只读，优化性能

            return proceduralMesh;
        }

        public void Release()
        {
            visibleMeshletsBuffer?.Release();
            argsBuffer?.Release();

            foreach (var meshletMesh in meshletCache.Values)
            {
                meshletMesh.Release();
            }
            meshletCache.Clear();

            if (meshletRenderMaterial != null)
            {
                Destroy(meshletRenderMaterial);
            }

            if (proceduralMesh != null)
            {
                Destroy(proceduralMesh);
            }
        }

        void OnDestroy()
        {
            Release();
        }
    }
}
