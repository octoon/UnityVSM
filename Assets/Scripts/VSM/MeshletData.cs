using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VSM
{
    /// <summary>
    /// Meshlet: 小型几何簇，用于细粒度剔除
    /// 论文: "To achieve granular culling, our drawing was implemented with meshlets combined with mesh shaders.
    ///        One meshlet is mapped to a single task shader invocation."
    /// </summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Meshlet
    {
        // 包围盒（用于剔除）
        public Vector3 boundsMin;
        public Vector3 boundsMax;

        // 顶点数据范围
        public uint vertexOffset;    // 在全局顶点buffer中的起始位置
        public uint vertexCount;     // 顶点数量（通常≤64）

        // 索引数据范围
        public uint indexOffset;     // 在全局索引buffer中的起始位置
        public uint triangleCount;   // 三角形数量（通常≤126）

        // Meshlet索引
        public uint meshletIndex;

        // 填充到64字节对齐
        public uint padding1;
        public uint padding2;
        public uint padding3;
    }

    /// <summary>
    /// Meshlet顶点数据（压缩格式）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshletVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;

        public MeshletVertex(Vector3 pos, Vector3 norm, Vector2 texCoord)
        {
            position = pos;
            normal = norm;
            uv = texCoord;
        }
    }

    /// <summary>
    /// Meshlet索引（3个顶点组成1个三角形）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshletTriangle
    {
        public uint v0, v1, v2;

        public MeshletTriangle(uint vertex0, uint vertex1, uint vertex2)
        {
            v0 = vertex0;
            v1 = vertex1;
            v2 = vertex2;
        }
    }

    /// <summary>
    /// Mesh的Meshlet表示
    /// </summary>
    [System.Serializable]
    public class MeshletMesh
    {
        public Mesh sourceMesh;
        public Meshlet[] meshlets;
        public MeshletVertex[] vertices;
        public MeshletTriangle[] triangles;

        // GPU Buffers
        public ComputeBuffer meshletBuffer;
        public ComputeBuffer vertexBuffer;
        public ComputeBuffer triangleBuffer;

        public void CreateGPUBuffers()
        {
            if (meshlets == null || meshlets.Length == 0)
            {
                Debug.LogError("No meshlets to upload!");
                return;
            }

            // 创建Meshlet描述buffer
            meshletBuffer = new ComputeBuffer(
                meshlets.Length,
                Marshal.SizeOf<Meshlet>(),
                ComputeBufferType.Structured
            );
            meshletBuffer.SetData(meshlets);

            // 创建顶点buffer
            vertexBuffer = new ComputeBuffer(
                vertices.Length,
                Marshal.SizeOf<MeshletVertex>(),
                ComputeBufferType.Structured
            );
            vertexBuffer.SetData(vertices);

            // 创建索引buffer
            triangleBuffer = new ComputeBuffer(
                triangles.Length,
                Marshal.SizeOf<MeshletTriangle>(),
                ComputeBufferType.Structured
            );
            triangleBuffer.SetData(triangles);
        }

        public void Release()
        {
            meshletBuffer?.Release();
            vertexBuffer?.Release();
            triangleBuffer?.Release();
        }
    }
}
