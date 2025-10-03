using UnityEngine;
using UnityEngine.Rendering;

namespace VSM
{
    /// <summary>
    /// Main manager for Virtual Shadow Maps system
    /// Orchestrates all VSM passes: bookkeeping, drawing, and sampling
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VirtualShadowMapManager : MonoBehaviour
    {
        [Header("VSM Settings")]
        [SerializeField] private Light directionalLight;
        [SerializeField] private float firstCascadeSize = 2.0f;  // Side length of first cascade frustum (not radius)
        [SerializeField] private LayerMask shadowCasters = -1;
        [SerializeField] [Range(0, 3)] private int filterMargin = 0;  // Pages margin for PCF filtering (TEMP: set to 0 to reduce allocation requests)

        [Header("Cascade Selection Heuristic")]
        [SerializeField] private bool usePixelPerfectHeuristic = false;  // Use first (pixel-perfect) or second (distance) heuristic
        [SerializeField] [Range(-2f, 2f)] private float cascadeBias = 0f;  // Bias to offset cascade selection

        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader clearVisibleFlagsShader;  // NEW: Clear visible bits every frame
        [SerializeField] private ComputeShader freeInvalidatedPagesShader;
        [SerializeField] private ComputeShader markVisiblePagesShader;
        [SerializeField] private ComputeShader allocatePagesShader;
        [SerializeField] private ComputeShader clearPagesShader;
        [SerializeField] private ComputeShader clearMemoryShader;  // Clear physical memory to 1.0
        [SerializeField] private ComputeShader buildHPBShader;

        [Header("Rendering")]
        [SerializeField] private Shader vsmDepthShader;
        [SerializeField] private Material vsmDepthMaterial;

        [Header("Meshlet Rendering (Advanced)")]
        [SerializeField] private bool useMeshletRendering = false;
        [SerializeField] private ComputeShader meshletTaskShader;
        [SerializeField] private Shader meshletRenderShader;

        [Header("Debug")]
        [SerializeField] private bool debugVisualization = false;

        // Core components
        private Camera mainCamera;
        private VSMPageTable pageTable;
        private VSMPhysicalPageTable physicalPageTable;
        private VSMPhysicalMemory physicalMemory;
        private VSMHierarchicalPageBuffer hpb;

        // Cascade data
        private Matrix4x4[] cascadeLightMatrices;
        private ComputeBuffer cascadeLightMatricesBuffer;
        private ComputeBuffer cascadeOffsetsBuffer;
        private ComputeBuffer cascadeShiftsBuffer;

        // Dynamic invalidation masks (for moving objects)
        private ComputeBuffer dynamicInvalidationMasksBuffer;

        // Previous frame cascade origins for tracking movement (sliding window)
        private Vector3[] previousCascadeOrigins;

        // Allocation counter buffer for two-stage allocation
        private ComputeBuffer allocationCounterBuffer;

        // Command buffer for rendering VSM
        private CommandBuffer vsmCommandBuffer;

        // Temporary depth texture
        private RenderTexture tempDepthTexture;

        // Meshlet renderer
        // private VSMMeshletRenderer meshletRenderer;  // Disabled - optional feature

        private bool isInitialized = false;

        void Start()
        {
            mainCamera = GetComponent<Camera>();
            InitializeVSM();
        }

        void InitializeVSM()
        {
            // Create core components
            pageTable = new VSMPageTable();
            physicalPageTable = new VSMPhysicalPageTable();
            physicalMemory = new VSMPhysicalMemory(clearMemoryShader);
            hpb = new VSMHierarchicalPageBuffer(buildHPBShader);

            // Initialize cascade data
            cascadeLightMatrices = new Matrix4x4[VSMConstants.CASCADE_COUNT];
            cascadeLightMatricesBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(float) * 16);
            cascadeOffsetsBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(int) * 2);
            cascadeShiftsBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(int) * 2);
            previousCascadeOrigins = new Vector3[VSMConstants.CASCADE_COUNT];

            // Initialize dynamic invalidation masks (empty for now - for static scenes)
            dynamicInvalidationMasksBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(uint));
            uint[] emptyMasks = new uint[VSMConstants.CASCADE_COUNT];
            dynamicInvalidationMasksBuffer.SetData(emptyMasks);

            // Allocation counter buffer: [0] = free page counter, [1] = used page counter
            // Use Raw type for CopyComputeBufferCount compatibility
            allocationCounterBuffer = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Raw);
            allocationCounterBuffer.SetData(new uint[] { 0, 0 });

            // Create depth material if not assigned
            if (vsmDepthMaterial == null && vsmDepthShader != null)
            {
                vsmDepthMaterial = new Material(vsmDepthShader);
            }

            // Create command buffer for VSM rendering
            vsmCommandBuffer = new CommandBuffer();
            vsmCommandBuffer.name = "Virtual Shadow Maps";

            // Initialize meshlet renderer if enabled
            /*if (useMeshletRendering)
            {
                GameObject meshletObj = new GameObject("VSM_MeshletRenderer");
                meshletObj.transform.SetParent(transform);
                meshletRenderer = meshletObj.AddComponent<VSMMeshletRenderer>();
                meshletRenderer.Initialize(hpb);

                Debug.Log("Meshlet rendering enabled (论文实现：Task+Mesh Shader剔除)");
            }*/

            UpdateCascadeMatrices();

            isInitialized = true;
            Debug.Log("Virtual Shadow Maps initialized successfully");
        }

        void UpdateCascadeMatrices()
        {
            if (directionalLight == null)
                return;

            Vector3 lightDir = directionalLight.transform.forward;
            Vector3 cameraPos = mainCamera.transform.position;

            // Arrays to track cascade offset changes (for sliding window)
            Vector2Int[] cascadeOffsets = new Vector2Int[VSMConstants.CASCADE_COUNT];
            Vector2Int[] cascadeShifts = new Vector2Int[VSMConstants.CASCADE_COUNT];

            for (int i = 0; i < VSMConstants.CASCADE_COUNT; i++)
            {
                // Calculate cascade size (each cascade is 2x the previous)
                float cascadeSize = firstCascadeSize * Mathf.Pow(2, i);

                // Paper section 12.2: "cascade frustums are snapped to the page grid"
                float pageWorldSize = cascadeSize / VSMConstants.PAGE_TABLE_RESOLUTION;
                Vector3 snappedCameraPos = new Vector3(
                    Mathf.Floor(cameraPos.x / pageWorldSize) * pageWorldSize,
                    Mathf.Floor(cameraPos.y / pageWorldSize) * pageWorldSize,
                    Mathf.Floor(cameraPos.z / pageWorldSize) * pageWorldSize
                );

                // Paper section 12.2: "the light position is constrained so that, when modified,
                // it slides along a plane parallel to the near-plane of the respective light matrix"
                // This ensures cached page depths remain valid after light matrix translation

                // Calculate light position: project along light direction
                Vector3 lightPos = snappedCameraPos - lightDir * cascadeSize;

                // Constrain to plane parallel to near plane (perpendicular to lightDir)
                // The near plane is defined by its normal (lightDir) and a point on it
                // We snap the light position to a grid on this plane
                Vector3 right = Vector3.Cross(lightDir, Vector3.up).normalized;
                if (right.magnitude < 0.1f)
                    right = Vector3.Cross(lightDir, Vector3.right).normalized;
                Vector3 up = Vector3.Cross(right, lightDir).normalized;

                // Project light position onto the perpendicular plane and snap
                float rightOffset = Vector3.Dot(lightPos, right);
                float upOffset = Vector3.Dot(lightPos, up);
                rightOffset = Mathf.Floor(rightOffset / pageWorldSize) * pageWorldSize;
                upOffset = Mathf.Floor(upOffset / pageWorldSize) * pageWorldSize;

                // Reconstruct snapped light position on the plane
                float depthOffset = Vector3.Dot(lightPos, lightDir);
                lightPos = right * rightOffset + up * upOffset + lightDir * depthOffset;

                // Paper Listing 12.1: Calculate cascade offset for sliding window (wraparound addressing)
                // "we store a per-cascade offset of the respective light matrix position from the origin"
                // This offset is used to translate virtual page coordinates into wrapped coordinates
                Vector3 cascadeOrigin = lightPos - new Vector3(cascadeSize / 2, cascadeSize / 2, 0);

                // Convert world-space origin to page coordinates
                int offsetX = Mathf.FloorToInt(cascadeOrigin.x / pageWorldSize);
                int offsetY = Mathf.FloorToInt(cascadeOrigin.y / pageWorldSize);
                cascadeOffsets[i] = new Vector2Int(offsetX, offsetY);

                // Calculate shift from previous frame (for invalidating sliding window pages)
                Vector3 previousOrigin = previousCascadeOrigins[i];
                if (previousOrigin != Vector3.zero)
                {
                    int prevOffsetX = Mathf.FloorToInt(previousOrigin.x / pageWorldSize);
                    int prevOffsetY = Mathf.FloorToInt(previousOrigin.y / pageWorldSize);
                    cascadeShifts[i] = new Vector2Int(
                        offsetX - prevOffsetX,
                        offsetY - prevOffsetY
                    );
                }
                else
                {
                    cascadeShifts[i] = Vector2Int.zero;
                }

                // Store for next frame
                previousCascadeOrigins[i] = cascadeOrigin;

                // Create orthographic projection for cascade
                Matrix4x4 viewMatrix = Matrix4x4.TRS(
                    lightPos,
                    Quaternion.LookRotation(lightDir),
                    Vector3.one
                ).inverse;

                Matrix4x4 projMatrix = Matrix4x4.Ortho(
                    -cascadeSize / 2, cascadeSize / 2,
                    -cascadeSize / 2, cascadeSize / 2,
                    0.1f, cascadeSize * 2
                );

                cascadeLightMatrices[i] = projMatrix * viewMatrix;
            }

            cascadeLightMatricesBuffer.SetData(cascadeLightMatrices);
            cascadeOffsetsBuffer.SetData(cascadeOffsets);
            cascadeShiftsBuffer.SetData(cascadeShifts);
        }

        void OnPreRender()
        {
            if (!isInitialized || directionalLight == null)
                return;

            UpdateCascadeMatrices();
            ExecuteVSMPipeline();
        }

        void ExecuteVSMPipeline()
        {
            // Phase 1: Bookkeeping
            BookkeepingPhase();

            // Phase 2: Drawing
            DrawingPhase();

            // Bind VSM data for sampling in shaders
            BindVSMDataToShaders();
        }

        void BookkeepingPhase()
        {
            // Step 0: Clear visible flags (CRITICAL - must be done first every frame)
            // Paper: "At the start of each frame, we must clear the visible flag from all pages"
            if (clearVisibleFlagsShader != null)
            {
                int kernel = clearVisibleFlagsShader.FindKernel("ClearVisibleFlags");
                clearVisibleFlagsShader.SetTexture(kernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                clearVisibleFlagsShader.Dispatch(kernel,
                    Mathf.CeilToInt(VSMConstants.PAGE_TABLE_RESOLUTION / 8.0f),
                    Mathf.CeilToInt(VSMConstants.PAGE_TABLE_RESOLUTION / 8.0f),
                    VSMConstants.CASCADE_COUNT);
            }

            // Step 1: Free invalidated pages
            if (freeInvalidatedPagesShader != null)
            {
                int kernel = freeInvalidatedPagesShader.FindKernel("FreeInvalidatedPages");
                freeInvalidatedPagesShader.SetTexture(kernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                freeInvalidatedPagesShader.SetBuffer(kernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                freeInvalidatedPagesShader.SetBuffer(kernel, "_CascadeShifts", cascadeShiftsBuffer);
                freeInvalidatedPagesShader.SetBuffer(kernel, "_DynamicInvalidationMasks", dynamicInvalidationMasksBuffer);

                for (int i = 0; i < VSMConstants.CASCADE_COUNT; i++)
                {
                    freeInvalidatedPagesShader.SetInt("_CurrentCascade", i);
                    freeInvalidatedPagesShader.Dispatch(kernel,
                        Mathf.CeilToInt(VSMConstants.PAGE_TABLE_RESOLUTION / 8.0f),
                        Mathf.CeilToInt(VSMConstants.PAGE_TABLE_RESOLUTION / 8.0f),
                        1);
                }
            }

            // Step 2: Mark visible pages
            if (markVisiblePagesShader != null)
            {
                pageTable.ResetAllocationRequests();

                int kernel = markVisiblePagesShader.FindKernel("MarkVisiblePages");
                markVisiblePagesShader.SetTexture(kernel, "_CameraDepthTexture", GetDepthTexture());
                markVisiblePagesShader.SetTexture(kernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                markVisiblePagesShader.SetBuffer(kernel, "_AllocationRequests", pageTable.AllocationRequests);
                markVisiblePagesShader.SetBuffer(kernel, "_CascadeLightMatrices", cascadeLightMatricesBuffer);
                markVisiblePagesShader.SetBuffer(kernel, "_CascadeOffsets", cascadeOffsetsBuffer);

                Matrix4x4 viewProj = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
                markVisiblePagesShader.SetMatrix("_CameraInverseViewProjection", viewProj.inverse);
                markVisiblePagesShader.SetMatrix("_CameraViewProjection", viewProj);
                markVisiblePagesShader.SetVector("_CameraPosition", mainCamera.transform.position);
                markVisiblePagesShader.SetFloat("_FirstCascadeSize", firstCascadeSize);
                markVisiblePagesShader.SetInt("_ScreenWidth", mainCamera.pixelWidth);
                markVisiblePagesShader.SetInt("_ScreenHeight", mainCamera.pixelHeight);
                markVisiblePagesShader.SetInt("_FilterMargin", filterMargin);

                // Cascade selection heuristic parameters
                markVisiblePagesShader.SetInt("_UsePixelPerfectHeuristic", usePixelPerfectHeuristic ? 1 : 0);
                markVisiblePagesShader.SetFloat("_CascadeBias", cascadeBias);

                markVisiblePagesShader.Dispatch(kernel,
                    Mathf.CeilToInt(mainCamera.pixelWidth / 8.0f),
                    Mathf.CeilToInt(mainCamera.pixelHeight / 8.0f),
                    1);
            }

            // Step 3: Fill allocator buffers
            if (allocatePagesShader != null)
            {
                pageTable.ResetPhysicalPageBuffers();

                int fillKernel = allocatePagesShader.FindKernel("FillAllocatorBuffers");
                allocatePagesShader.SetTexture(fillKernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                allocatePagesShader.SetBuffer(fillKernel, "_PhysicalPageTable", physicalPageTable.Buffer);
                allocatePagesShader.SetBuffer(fillKernel, "_FreePhysicalPages", pageTable.FreePhysicalPages);
                allocatePagesShader.SetBuffer(fillKernel, "_UsedPhysicalPages", pageTable.UsedPhysicalPages);
                allocatePagesShader.SetBuffer(fillKernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                allocatePagesShader.SetInt("_MaxPhysicalPages", VSMConstants.MAX_PHYSICAL_PAGES);

                int fillThreadGroups = Mathf.CeilToInt(VSMConstants.MAX_PHYSICAL_PAGES / 64.0f);
                fillThreadGroups = Mathf.Min(fillThreadGroups, 65535);

                allocatePagesShader.Dispatch(fillKernel, fillThreadGroups, 1, 1);
            }

            // Step 4: Allocate pages
            if (allocatePagesShader != null)
            {
                // Get allocation request count
                ComputeBuffer.CopyCount(pageTable.AllocationRequests, allocationCounterBuffer, 0);
                uint[] counts = new uint[2];
                allocationCounterBuffer.GetData(counts);
                uint allocationRequestCount = counts[0];

                // DEBUG: Log allocation request count
                Debug.Log($"[VSM AllocationPhase] Allocation requests: {allocationRequestCount}");

                if (allocationRequestCount > 0)
                {
                    // Reset allocation counters
                    allocationCounterBuffer.SetData(new uint[] { 0, 0 });

                    // Get free and used page counts
                    ComputeBuffer.CopyCount(pageTable.FreePhysicalPages, allocationCounterBuffer, 0);
                    ComputeBuffer.CopyCount(pageTable.UsedPhysicalPages, allocationCounterBuffer, sizeof(uint));
                    uint[] pageCounts = new uint[2];
                    allocationCounterBuffer.GetData(pageCounts);

                    // Reset counters for allocation
                    allocationCounterBuffer.SetData(new uint[] { 0, 0 });

                    int allocKernel = allocatePagesShader.FindKernel("AllocatePages");
                    allocatePagesShader.SetTexture(allocKernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                    allocatePagesShader.SetBuffer(allocKernel, "_PhysicalPageTable", physicalPageTable.Buffer);
                    allocatePagesShader.SetBuffer(allocKernel, "_AllocationRequests", pageTable.AllocationRequests);
                    allocatePagesShader.SetBuffer(allocKernel, "_FreePhysicalPagesConsume", pageTable.FreePhysicalPages);
                    allocatePagesShader.SetBuffer(allocKernel, "_UsedPhysicalPagesConsume", pageTable.UsedPhysicalPages);
                    allocatePagesShader.SetBuffer(allocKernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                    allocatePagesShader.SetBuffer(allocKernel, "_AllocationCounter", allocationCounterBuffer);
                    allocatePagesShader.SetInt("_AllocationRequestCount", (int)allocationRequestCount);
                    allocatePagesShader.SetInt("_FreePageCount", (int)pageCounts[0]);
                    allocatePagesShader.SetInt("_UsedPageCount", (int)pageCounts[1]);

                    // Clamp thread groups to GPU limit (65535)
                    int threadGroups = Mathf.CeilToInt(allocationRequestCount / 64.0f);
                    threadGroups = Mathf.Min(threadGroups, 65535);

                    allocatePagesShader.Dispatch(allocKernel, threadGroups, 1, 1);

                    // DEBUG: Log allocated pages
                    Debug.Log($"[VSM AllocationPhase] Allocated {allocationRequestCount} pages (free: {pageCounts[0]}, used: {pageCounts[1]})");
                }
                else
                {
                    Debug.LogWarning("[VSM AllocationPhase] No allocation requests! Pages not being marked as visible.");
                }
            }

            // Step 5: Clear dirty pages
            if (clearPagesShader != null)
            {
                // Get allocation request count again for clearing
                uint[] counts = new uint[2];
                allocationCounterBuffer.GetData(counts);
                uint allocationRequestCount = counts[0];

                if (allocationRequestCount > 0)
                {
                    int clearKernel = clearPagesShader.FindKernel("ClearDirtyPages");
                    clearPagesShader.SetTexture(clearKernel, "_PhysicalMemory", physicalMemory.Texture);
                    clearPagesShader.SetTexture(clearKernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                    clearPagesShader.SetBuffer(clearKernel, "_AllocationRequests", pageTable.AllocationRequests);
                    clearPagesShader.SetBuffer(clearKernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                    clearPagesShader.SetInt("_AllocationRequestCount", (int)allocationRequestCount);

                    // Clamp Z thread groups to GPU limit
                    int zGroups = (int)Mathf.Min(allocationRequestCount, 65535);

                    clearPagesShader.Dispatch(clearKernel,
                        Mathf.CeilToInt(VSMConstants.PAGE_SIZE / 8.0f),
                        Mathf.CeilToInt(VSMConstants.PAGE_SIZE / 8.0f),
                        zGroups);
                }
            }

            // Build HPB for culling
            hpb.BuildHPB(pageTable.VirtualPageTableTexture);
        }

        void DrawingPhase()
        {
            // Paper section 12.2.2: "Drawing Phase"
            // Render scene geometry to each cascade's dirty pages using HPB culling

            if (vsmCommandBuffer == null)
                return;

            // 选择渲染路径：Meshlet（论文方法）或传统方法
            /*if (useMeshletRendering && meshletRenderer != null)
            {
                DrawingPhase_Meshlet();
            }
            else*/
            {
                DrawingPhase_Traditional();
            }
        }

        void DrawingPhase_Traditional()
        {
            // 传统渲染路径（向后兼容）
            if (vsmDepthMaterial == null)
                return;

            vsmCommandBuffer.Clear();

            // IMPORTANT: Fragment shader需要一个渲染目标才会执行
            // 虽然我们在shader中使用InterlockedMin直接写入_PhysicalMemory，
            // 但仍需要设置一个渲染目标来触发fragment shader执行

            // 创建临时深度纹理作为渲染目标
            int tempDepthID = Shader.PropertyToID("_VSMTempDepth");
            vsmCommandBuffer.GetTemporaryRT(tempDepthID,
                VSMConstants.PAGE_SIZE, VSMConstants.PAGE_SIZE,
                24, FilterMode.Point, RenderTextureFormat.Depth);
            vsmCommandBuffer.SetRenderTarget(tempDepthID);

            // Set global shader properties for VSM rendering
            vsmCommandBuffer.SetGlobalTexture("_VirtualPageTable", pageTable.VirtualPageTableTexture);
            vsmCommandBuffer.SetGlobalTexture("_PhysicalMemory", physicalMemory.Texture);
            vsmCommandBuffer.SetGlobalBuffer("_CascadeLightMatrices", cascadeLightMatricesBuffer);
            vsmCommandBuffer.SetGlobalBuffer("_CascadeOffsets", cascadeOffsetsBuffer);

            // Get all shadow casters in the scene
            Renderer[] renderers = FindObjectsOfType<Renderer>();

            // DEBUG: Log total renderer count
            Debug.Log($"[VSM DrawingPhase] Total renderers found: {renderers.Length}");

            // Render each cascade
            for (int cascadeIndex = 0; cascadeIndex < VSMConstants.CASCADE_COUNT; cascadeIndex++)
            {
                int drawnThisCascade = 0; // DEBUG counter

                // Clear depth buffer for this cascade
                vsmCommandBuffer.ClearRenderTarget(true, false, Color.clear);

                // Set current cascade index
                vsmCommandBuffer.SetGlobalInt("_CurrentCascade", cascadeIndex);

                // Paper: "Due to the VSM having many cascades, it is critical to have effective and granular scene culling"
                // In a full implementation, you would:
                // 1. Use HPB for per-meshlet culling (via compute shader or task shader)
                // 2. Only render geometry that intersects dirty pages (HPB culling - see VSMCullAndDraw.compute)
                // 3. Use indirect drawing for maximum efficiency

                foreach (Renderer renderer in renderers)
                {
                    // Check if renderer is in shadow caster layer
                    if (((1 << renderer.gameObject.layer) & shadowCasters) == 0)
                    {
                        // DEBUG: Log filtered objects
                        Debug.Log($"[VSM] Skipping {renderer.name} - not in shadowCasters layer");
                        continue;
                    }

                    // Frustum culling against cascade
                    // TEMPORARILY DISABLED for debugging
                    /*Bounds bounds = renderer.bounds;
                    if (!IsInCascadeFrustum(bounds, cascadeIndex))
                    {
                        // DEBUG: Log culled objects
                        //Debug.Log($"[VSM] Culled {renderer.name} - outside cascade {cascadeIndex} frustum");
                        continue;
                    }*/

                    // HPB culling would go here - for now we render all objects in frustum
                    // See VSMCullAndDraw.compute for HPB culling implementation

                    // Get mesh
                    MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                    {
                        Debug.Log($"[VSM] Skipping {renderer.name} - no MeshFilter or shared mesh");
                        continue;
                    }

                    // Draw mesh to VSM using command buffer
                    Matrix4x4 worldMatrix = renderer.transform.localToWorldMatrix;
                    vsmCommandBuffer.DrawMesh(
                        meshFilter.sharedMesh,
                        worldMatrix,
                        vsmDepthMaterial,
                        0, // submesh index
                        0  // shader pass
                    );

                    drawnThisCascade++; // DEBUG counter
                }

                // DEBUG: Log draw count per cascade
                Debug.Log($"[VSM DrawingPhase] Cascade {cascadeIndex}: Drew {drawnThisCascade} objects");
            }

            // Release temporary RT
            vsmCommandBuffer.ReleaseTemporaryRT(tempDepthID);

            // Execute the command buffer
            Graphics.ExecuteCommandBuffer(vsmCommandBuffer);
        }

        /*void DrawingPhase_Meshlet()
        {
            // 论文实现：Meshlet + Task/Mesh Shader
            // "To achieve granular culling, our drawing was implemented with meshlets combined with mesh shaders"

            vsmCommandBuffer.Clear();

            // Get all shadow casters
            Renderer[] renderers = FindObjectsOfType<Renderer>();

            // Render each cascade
            for (int cascadeIndex = 0; cascadeIndex < VSMConstants.CASCADE_COUNT; cascadeIndex++)
            {
                foreach (Renderer renderer in renderers)
                {
                    // Check shadow caster layer
                    if (((1 << renderer.gameObject.layer) & shadowCasters) == 0)
                        continue;

                    // Frustum culling
                    if (!IsInCascadeFrustum(renderer.bounds, cascadeIndex))
                        continue;

                    // Get or create meshlet representation
                    MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                        continue;

                    MeshletMesh meshletMesh = meshletRenderer.GetOrCreateMeshlets(meshFilter.sharedMesh);
                    if (meshletMesh == null)
                        continue;

                    // 论文流程：Task Shader剔除 + Mesh Shader渲染
                    // "The task shader performs frustum culling followed by culling against the HPB"
                    // "After culling, a group of 32 mesh shader threads is dispatched for each of the surviving meshlets"
                    meshletRenderer.RenderMeshletToVSM(
                        meshletMesh,
                        renderer.transform.localToWorldMatrix,
                        cascadeIndex,
                        vsmCommandBuffer,
                        cascadeLightMatricesBuffer,
                        cascadeOffsetsBuffer,
                        pageTable.VirtualPageTableTexture,
                        physicalMemory.Texture
                    );
                }
            }

            // Execute command buffer
            Graphics.ExecuteCommandBuffer(vsmCommandBuffer);
        }*/

        bool IsInCascadeFrustum(Bounds bounds, int cascadeIndex)
        {
            // Simple AABB frustum test against cascade
            // Transform bounds to light space and check if inside [-1,1] range

            Matrix4x4 lightMatrix = cascadeLightMatrices[cascadeIndex];
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            // Check all 8 corners of the bounding box
            bool anyInside = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3(
                    (i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y,
                    (i & 4) == 0 ? -extents.z : extents.z
                );

                Vector4 lightSpacePos = lightMatrix * new Vector4(corner.x, corner.y, corner.z, 1.0f);
                Vector3 ndc = new Vector3(
                    lightSpacePos.x / lightSpacePos.w,
                    lightSpacePos.y / lightSpacePos.w,
                    lightSpacePos.z / lightSpacePos.w
                );

                // Check if inside NDC cube [-1, 1]
                if (ndc.x >= -1 && ndc.x <= 1 &&
                    ndc.y >= -1 && ndc.y <= 1 &&
                    ndc.z >= -1 && ndc.z <= 1)
                {
                    anyInside = true;
                    break;
                }
            }

            return anyInside;
        }

        void BindVSMDataToShaders()
        {
            // Bind VSM textures and buffers globally for sampling in materials
            Shader.SetGlobalTexture("_VSM_VirtualPageTable", pageTable.VirtualPageTableTexture);
            Shader.SetGlobalTexture("_VSM_PhysicalMemory", physicalMemory.Texture);
            Shader.SetGlobalBuffer("_VSM_CascadeLightMatrices", cascadeLightMatricesBuffer);
            Shader.SetGlobalBuffer("_VSM_CascadeOffsets", cascadeOffsetsBuffer);
            Shader.SetGlobalFloat("_VSM_FirstCascadeSize", firstCascadeSize);
            Shader.SetGlobalVector("_VSM_CameraPosition", mainCamera.transform.position);

            if (directionalLight != null)
            {
                Shader.SetGlobalVector("_DirectionalLightDir", directionalLight.transform.forward);
                Shader.SetGlobalColor("_DirectionalLightColor", directionalLight.color * directionalLight.intensity);
            }
        }

        RenderTexture GetDepthTexture()
        {
            // Enable depth texture mode
            mainCamera.depthTextureMode = DepthTextureMode.Depth;

            // Try to get the built-in depth texture
            RenderTexture depthTex = Shader.GetGlobalTexture("_CameraDepthTexture") as RenderTexture;

            // If not available, create a temporary one
            if (depthTex == null)
            {
                if (tempDepthTexture == null ||
                    tempDepthTexture.width != mainCamera.pixelWidth ||
                    tempDepthTexture.height != mainCamera.pixelHeight)
                {
                    if (tempDepthTexture != null)
                        RenderTexture.ReleaseTemporary(tempDepthTexture);

                    tempDepthTexture = RenderTexture.GetTemporary(
                        mainCamera.pixelWidth,
                        mainCamera.pixelHeight,
                        24,
                        RenderTextureFormat.Depth
                    );
                    tempDepthTexture.filterMode = FilterMode.Point;
                }
                depthTex = tempDepthTexture;
            }

            return depthTex;
        }

        void OnDestroy()
        {
            Cleanup();
        }

        void Cleanup()
        {
            pageTable?.Release();
            physicalPageTable?.Release();
            physicalMemory?.Release();
            hpb?.Release();

            cascadeLightMatricesBuffer?.Release();
            cascadeOffsetsBuffer?.Release();
            cascadeShiftsBuffer?.Release();
            dynamicInvalidationMasksBuffer?.Release();
            allocationCounterBuffer?.Release();

            if (vsmCommandBuffer != null)
            {
                vsmCommandBuffer.Release();
                vsmCommandBuffer = null;
            }

            if (vsmDepthMaterial != null)
            {
                Destroy(vsmDepthMaterial);
            }

            // Release temporary depth texture
            if (tempDepthTexture != null)
            {
                RenderTexture.ReleaseTemporary(tempDepthTexture);
                tempDepthTexture = null;
            }

            // Cleanup meshlet renderer
            /*if (meshletRenderer != null)
            {
                meshletRenderer.Release();
                Destroy(meshletRenderer.gameObject);
            }*/
        }

        void OnGUI()
        {
            if (debugVisualization)
            {
                GUI.Label(new Rect(10, 10, 300, 20), "Virtual Shadow Maps Active");
                GUI.Label(new Rect(10, 30, 300, 20), $"Physical Memory: {VSMConstants.PHYSICAL_MEMORY_WIDTH}x{VSMConstants.PHYSICAL_MEMORY_HEIGHT}");
                GUI.Label(new Rect(10, 50, 300, 20), $"Cascades: {VSMConstants.CASCADE_COUNT}");
            }
        }
    }
}
