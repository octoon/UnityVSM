using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace VSM
{
    /// <summary>
    /// Meshlet构建器 - 将标准Mesh转换为Meshlet表示
    ///
    /// 算法: 贪心聚类
    /// - 每个meshlet最多64个顶点
    /// - 每个meshlet最多126个三角形（32个mesh shader线程，每线程4个三角形）
    /// - 使用空间局部性优化缓存
    /// </summary>
    public static class MeshletBuilder
    {
        // Meshlet大小限制（论文: "a group of 32 mesh shader threads is dispatched for each of the surviving meshlets"）
        private const int MAX_VERTICES_PER_MESHLET = 64;
        private const int MAX_TRIANGLES_PER_MESHLET = 126; // 32 threads × ~4 triangles
        private const int MESH_SHADER_THREADS = 32;

        /// <summary>
        /// 从Unity Mesh构建Meshlet表示
        /// </summary>
        public static MeshletMesh BuildMeshlets(Mesh mesh)
        {
            if (mesh == null)
            {
                Debug.LogError("Mesh is null!");
                return null;
            }

            // 获取Mesh数据
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] indices = mesh.triangles;

            if (normals == null || normals.Length == 0)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            if (uvs == null || uvs.Length == 0)
            {
                uvs = new Vector2[positions.Length];
            }

            // 构建Meshlet
            List<Meshlet> meshlets = new List<Meshlet>();
            List<MeshletVertex> allVertices = new List<MeshletVertex>();
            List<MeshletTriangle> allTriangles = new List<MeshletTriangle>();

            int triangleCount = indices.Length / 3;
            bool[] processedTriangles = new bool[triangleCount];
            int processedCount = 0;

            int meshletIndex = 0;

            while (processedCount < triangleCount)
            {
                // 创建新的meshlet
                Meshlet meshlet = new Meshlet
                {
                    meshletIndex = (uint)meshletIndex,
                    vertexOffset = (uint)allVertices.Count,
                    indexOffset = (uint)allTriangles.Count,
                    boundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                    boundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue)
                };

                List<MeshletVertex> meshletVertices = new List<MeshletVertex>();
                List<MeshletTriangle> meshletTriangles = new List<MeshletTriangle>();
                Dictionary<int, int> vertexRemap = new Dictionary<int, int>(); // 全局索引 → meshlet局部索引

                // 找到第一个未处理的三角形作为种子
                int seedTriangle = -1;
                for (int i = 0; i < triangleCount; i++)
                {
                    if (!processedTriangles[i])
                    {
                        seedTriangle = i;
                        break;
                    }
                }

                if (seedTriangle == -1) break;

                // 贪心添加三角形到当前meshlet
                Queue<int> triangleQueue = new Queue<int>();
                triangleQueue.Enqueue(seedTriangle);

                while (triangleQueue.Count > 0)
                {
                    int triIndex = triangleQueue.Dequeue();

                    if (processedTriangles[triIndex]) continue;

                    // 检查这个三角形是否能加入当前meshlet
                    int i0 = indices[triIndex * 3 + 0];
                    int i1 = indices[triIndex * 3 + 1];
                    int i2 = indices[triIndex * 3 + 2];

                    int newVertexCount = 0;
                    if (!vertexRemap.ContainsKey(i0)) newVertexCount++;
                    if (!vertexRemap.ContainsKey(i1)) newVertexCount++;
                    if (!vertexRemap.ContainsKey(i2)) newVertexCount++;

                    // 检查是否超出限制
                    if (meshletVertices.Count + newVertexCount > MAX_VERTICES_PER_MESHLET ||
                        meshletTriangles.Count >= MAX_TRIANGLES_PER_MESHLET)
                    {
                        continue; // 跳过这个三角形，留给下一个meshlet
                    }

                    // 添加顶点（如果还不在meshlet中）
                    int AddVertex(int globalIndex)
                    {
                        if (vertexRemap.ContainsKey(globalIndex))
                            return vertexRemap[globalIndex];

                        int localIndex = meshletVertices.Count;
                        MeshletVertex vertex = new MeshletVertex(
                            positions[globalIndex],
                            normals[globalIndex],
                            uvs[globalIndex]
                        );
                        meshletVertices.Add(vertex);
                        vertexRemap[globalIndex] = localIndex;

                        // 更新包围盒
                        meshlet.boundsMin = Vector3.Min(meshlet.boundsMin, vertex.position);
                        meshlet.boundsMax = Vector3.Max(meshlet.boundsMax, vertex.position);

                        return localIndex;
                    }

                    int local0 = AddVertex(i0);
                    int local1 = AddVertex(i1);
                    int local2 = AddVertex(i2);

                    // 添加三角形
                    meshletTriangles.Add(new MeshletTriangle((uint)local0, (uint)local1, (uint)local2));

                    // 标记为已处理
                    processedTriangles[triIndex] = true;
                    processedCount++;

                    // 将相邻三角形加入队列（空间局部性优化）
                    for (int adjacentTri = 0; adjacentTri < triangleCount; adjacentTri++)
                    {
                        if (processedTriangles[adjacentTri]) continue;

                        // 检查是否共享顶点
                        int ai0 = indices[adjacentTri * 3 + 0];
                        int ai1 = indices[adjacentTri * 3 + 1];
                        int ai2 = indices[adjacentTri * 3 + 2];

                        if (ai0 == i0 || ai0 == i1 || ai0 == i2 ||
                            ai1 == i0 || ai1 == i1 || ai1 == i2 ||
                            ai2 == i0 || ai2 == i1 || ai2 == i2)
                        {
                            if (!triangleQueue.Contains(adjacentTri))
                            {
                                triangleQueue.Enqueue(adjacentTri);
                            }
                        }
                    }
                }

                // 完成当前meshlet
                meshlet.vertexCount = (uint)meshletVertices.Count;
                meshlet.triangleCount = (uint)meshletTriangles.Count;

                allVertices.AddRange(meshletVertices);
                allTriangles.AddRange(meshletTriangles);
                meshlets.Add(meshlet);

                meshletIndex++;
            }

            // 创建MeshletMesh对象
            MeshletMesh meshletMesh = new MeshletMesh
            {
                sourceMesh = mesh,
                meshlets = meshlets.ToArray(),
                vertices = allVertices.ToArray(),
                triangles = allTriangles.ToArray()
            };

            Debug.Log($"Built {meshlets.Count} meshlets from mesh with {triangleCount} triangles");
            Debug.Log($"Total vertices: {allVertices.Count}, Total triangles: {allTriangles.Count}");

            return meshletMesh;
        }

        /// <summary>
        /// 优化Meshlet顺序以提高缓存命中率（可选）
        /// </summary>
        public static void OptimizeMeshletOrder(MeshletMesh meshletMesh)
        {
            // 使用深度优先遍历或Hilbert曲线排序
            // 这里简化实现：按包围盒中心的Morton码排序

            System.Array.Sort(meshletMesh.meshlets, (a, b) =>
            {
                Vector3 centerA = (a.boundsMin + a.boundsMax) * 0.5f;
                Vector3 centerB = (b.boundsMin + b.boundsMax) * 0.5f;

                uint mortonA = MortonCode3D(centerA);
                uint mortonB = MortonCode3D(centerB);

                return mortonA.CompareTo(mortonB);
            });

            // 更新meshlet索引
            for (int i = 0; i < meshletMesh.meshlets.Length; i++)
            {
                meshletMesh.meshlets[i].meshletIndex = (uint)i;
            }
        }

        // Morton码计算（Z-order curve）
        private static uint MortonCode3D(Vector3 pos)
        {
            // 归一化到[0,1023]
            uint x = (uint)Mathf.Clamp(pos.x * 100 + 512, 0, 1023);
            uint y = (uint)Mathf.Clamp(pos.y * 100 + 512, 0, 1023);
            uint z = (uint)Mathf.Clamp(pos.z * 100 + 512, 0, 1023);

            return Part1By2(x) | (Part1By2(y) << 1) | (Part1By2(z) << 2);
        }

        private static uint Part1By2(uint n)
        {
            n = (n ^ (n << 16)) & 0xff0000ff;
            n = (n ^ (n << 8)) & 0x0300f00f;
            n = (n ^ (n << 4)) & 0x030c30c3;
            n = (n ^ (n << 2)) & 0x09249249;
            return n;
        }
    }
}
