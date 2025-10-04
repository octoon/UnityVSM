using UnityEngine;
using UnityEngine.Rendering;

namespace VSM
{
    /// <summary>
    /// Static initializer to create VSM buffers early to prevent shader errors
    /// </summary>
    public static class VSMStaticInitializer
    {
        internal static readonly int kVsmCascadeLightMatricesId = Shader.PropertyToID("_VSM_CascadeLightMatrices");
        internal static readonly int kVsmCascadeOffsetsId = Shader.PropertyToID("_VSM_CascadeOffsets");
        internal static readonly int kCascadeLightMatricesId = Shader.PropertyToID("_CascadeLightMatrices");
        internal static readonly int kCascadeOffsetsId = Shader.PropertyToID("_CascadeOffsets");

        private static ComputeBuffer s_cascadeLightMatricesBuffer;
        private static ComputeBuffer s_cascadeOffsetsBuffer;
        private static bool s_initialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cleanup();
        }

        // Extra early hooks to guarantee global buffers are bound before any draw on D3D12
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void InitAfterAssembliesLoaded()
        {
            EnsureInitialized();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void InitBeforeSplash()
        {
            EnsureInitialized();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeBeforeScene()
        {
            EnsureInitialized();
        }

        public static void EnsureInitialized()
        {
            if (s_initialized && IsBufferValid(s_cascadeLightMatricesBuffer) && IsBufferValid(s_cascadeOffsetsBuffer))
            {
                BindGlobals();
                return;
            }

            Debug.Log("[VSM Static] Initializing fallback buffers for VSM shaders");

            Cleanup();

            s_cascadeLightMatricesBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(float) * 16, ComputeBufferType.Structured);
            s_cascadeLightMatricesBuffer.SetData(new Matrix4x4[VSMConstants.CASCADE_COUNT]);

            s_cascadeOffsetsBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(int) * 2, ComputeBufferType.Structured);
            var zeroOffsets = new Vector2Int[VSMConstants.CASCADE_COUNT];
            s_cascadeOffsetsBuffer.SetData(zeroOffsets);

            BindGlobals();
            s_initialized = true;
        }

        private static void BindGlobals()
        {
            if (!IsBufferValid(s_cascadeLightMatricesBuffer) || !IsBufferValid(s_cascadeOffsetsBuffer))
                return;

            Shader.SetGlobalBuffer(kVsmCascadeLightMatricesId, s_cascadeLightMatricesBuffer);
            Shader.SetGlobalBuffer(kCascadeLightMatricesId, s_cascadeLightMatricesBuffer);
            Shader.SetGlobalBuffer(kVsmCascadeOffsetsId, s_cascadeOffsetsBuffer);
            Shader.SetGlobalBuffer(kCascadeOffsetsId, s_cascadeOffsetsBuffer);
        }

        private static bool IsBufferValid(ComputeBuffer buffer)
        {
            if (buffer == null)
                return false;

            try
            {
                buffer.GetNativeBufferPtr();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Cleanup()
        {
            if (s_cascadeLightMatricesBuffer != null)
            {
                s_cascadeLightMatricesBuffer.Release();
                s_cascadeLightMatricesBuffer = null;
            }

            if (s_cascadeOffsetsBuffer != null)
            {
                s_cascadeOffsetsBuffer.Release();
                s_cascadeOffsetsBuffer = null;
            }

            s_initialized = false;
        }
    }

    /// <summary>
    /// Main manager for Virtual Shadow Maps system
    /// Orchestrates all VSM passes: bookkeeping, drawing, and sampling
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VirtualShadowMapManager : MonoBehaviour
    {
        [Header("VSM Settings")]
        [SerializeField] private Light directionalLight;
        [SerializeField] private float firstCascadeSize = 10.0f;  // FIXED: Increased from 2m to 10m for better coverage
        [SerializeField] private LayerMask shadowCasters = -1;
        [SerializeField] [Range(0, 8)] private int filterMargin = 1;  // FIXED: Default to 1 for 3x3 PCF filtering support

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
        [SerializeField] private ComputeShader copyDepthShader;  // NEW: Copy depth from RT to physical memory
        [SerializeField] private ComputeShader copyBufferToTextureShader;  // NEW: Copy buffer to texture for sampling

        [Header("Rendering")]
        [SerializeField] private Shader vsmDepthShader;
        [SerializeField] private Material vsmDepthMaterial;

        [Header("Meshlet Rendering (Advanced)")]
        [SerializeField] private bool useMeshletRendering = true;  // Enable meshlet rendering by default
        [SerializeField] private ComputeShader meshletTaskShader;
        [SerializeField] private Shader meshletRenderShader;

        // Debug options removed for production build

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
        private const int DynamicMaskStride = VSMConstants.PAGE_TABLE_RESOLUTION * VSMConstants.PAGE_TABLE_RESOLUTION / 32; // 32
        private uint[] dynamicInvalidationMaskCPU; // CPU-side bitmask (stride * cascades)
        private System.Collections.Generic.Dictionary<Renderer, Bounds> prevRendererBounds = new System.Collections.Generic.Dictionary<Renderer, Bounds>();

        // Previous frame cascade origins for tracking movement (sliding window)
        private Vector3[] previousCascadeOrigins;

        // Allocation counter buffer for two-stage allocation
        private ComputeBuffer allocationCounterBuffer;

        // Command buffer for rendering VSM
        private CommandBuffer vsmCommandBuffer;

        // Temporary depth texture
        private RenderTexture tempDepthTexture;

        // Meshlet renderer
        private VSMMeshletRenderer meshletRenderer;

        private bool isInitialized = false;

        void Awake()
        {
            VSMStaticInitializer.EnsureInitialized();
            mainCamera = GetComponent<Camera>();
        }

        void Start()
        {
            mainCamera = GetComponent<Camera>();
            InitializeVSM();
        }

        void InitializeVSM()
        {
            VSMStaticInitializer.EnsureInitialized();

            // Create core components
            pageTable = new VSMPageTable();
            physicalPageTable = new VSMPhysicalPageTable();
            physicalMemory = new VSMPhysicalMemory(clearMemoryShader, copyBufferToTextureShader);
            hpb = new VSMHierarchicalPageBuffer(buildHPBShader);

            // Initialize cascade data
            // Note: Static initializer created dummy buffers, we now create real ones
            cascadeLightMatrices = new Matrix4x4[VSMConstants.CASCADE_COUNT];
            cascadeLightMatricesBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(float) * 16);
            cascadeOffsetsBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(int) * 2);
            cascadeShiftsBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(int) * 2);
            previousCascadeOrigins = new Vector3[VSMConstants.CASCADE_COUNT];

            // Initialize dynamic invalidation masks (32 uint per cascade)
            dynamicInvalidationMasksBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT * DynamicMaskStride, sizeof(uint));
            dynamicInvalidationMaskCPU = new uint[VSMConstants.CASCADE_COUNT * DynamicMaskStride];
            System.Array.Clear(dynamicInvalidationMaskCPU, 0, dynamicInvalidationMaskCPU.Length);
            dynamicInvalidationMasksBuffer.SetData(dynamicInvalidationMaskCPU);

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
            if (useMeshletRendering)
            {
                GameObject meshletObj = new GameObject("VSM_MeshletRenderer");
                meshletObj.transform.SetParent(transform);
                meshletRenderer = meshletObj.AddComponent<VSMMeshletRenderer>();
                meshletRenderer.Initialize(hpb);

                Debug.Log("Meshlet rendering enabled (论文实现：Task+Mesh Shader剔除)");
            }

            UpdateCascadeMatrices();

            // Bind VSM data to shaders immediately after initialization
            BindVSMDataToShaders();

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

                // FIXED: Cascades should be CONCENTRIC around the camera position
                // Paper: "16 overlapping cascades arranged in a concentric manner"
                // The light position should be behind the camera to see the cascade area

                // Calculate light position: behind the cascade center (camera) looking forward
                // We need to be far enough back to see the entire cascade area
                Vector3 lightPos = snappedCameraPos - lightDir * (cascadeSize * 1.5f);

                // Snap light position to page grid to maintain cache validity
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

                // CRITICAL FIX: The cascade origin is the bottom-left corner of the orthographic frustum
                // in light space (X,Y plane). We project lightPos onto the X-Y plane perpendicular to lightDir.
                // The offset calculation should be in light-space coordinates, not world-space!

                // FIXED: Calculate cascade bottom-left corner (cascade is centered at camera)
                Vector3 frustumBottomLeft = snappedCameraPos - right * (cascadeSize / 2) - up * (cascadeSize / 2);

                // Project onto right/up plane to get 2D cascade origin
                float originX = Vector3.Dot(frustumBottomLeft, right);
                float originY = Vector3.Dot(frustumBottomLeft, up);

                // Convert to page coordinates (these are the offsets for sliding window)
                int offsetX = Mathf.FloorToInt(originX / pageWorldSize);
                int offsetY = Mathf.FloorToInt(originY / pageWorldSize);

                // Always enable sliding window in production
                cascadeOffsets[i] = new Vector2Int(offsetX, offsetY);

                // Store frustum origin for next frame comparison (in 2D light space)
                Vector3 cascadeOrigin = new Vector3(originX, originY, 0);

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

                // Store for next frame (in 2D light space)
                previousCascadeOrigins[i] = cascadeOrigin;

                // Create orthographic projection for cascade
                // FIXED: View matrix should look at the CASCADE CENTER (camera), not from lightPos
                Matrix4x4 viewMatrix = Matrix4x4.LookAt(
                    lightPos,                    // Eye position (behind cascade)
                    snappedCameraPos,           // Look at cascade center (camera)
                    Vector3.up                  // Up vector
                );

                // FIXED: Ortho projection should cover the cascade area
                // The cascade is cascadeSize x cascadeSize, centered at the origin in view space
                Matrix4x4 projMatrix = Matrix4x4.Ortho(
                    -cascadeSize / 2, cascadeSize / 2,
                    -cascadeSize / 2, cascadeSize / 2,
                    0.1f, cascadeSize * 3  // Extend far plane to cover entire cascade depth
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

            // Phase 3: Copy buffer to texture for sampling
            physicalMemory.CopyBufferToTexture();

            // Debug buffer inspection removed

            // Bind VSM data for sampling in shaders
            BindVSMDataToShaders();

            // Finalize: clear DIRTY flags for processed pages (after drawing finishes)
            if (clearPagesShader != null)
            {
                ComputeBuffer.CopyCount(pageTable.AllocationRequests, allocationCounterBuffer, 0);
                uint[] counts2 = new uint[2];
                allocationCounterBuffer.GetData(counts2);
                uint finalizeRequestCount = counts2[0];
                if (finalizeRequestCount > 0)
                {
                    int finalizeKernel = clearPagesShader.FindKernel("ClearDirtyFlags");
                    clearPagesShader.SetTexture(finalizeKernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                    clearPagesShader.SetBuffer(finalizeKernel, "_AllocationRequests", pageTable.AllocationRequests);
                    clearPagesShader.SetBuffer(finalizeKernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                    clearPagesShader.SetInt("_AllocationRequestCount", (int)finalizeRequestCount);

                    int groups = Mathf.Min(65535, Mathf.CeilToInt(finalizeRequestCount / 64.0f));
                    clearPagesShader.Dispatch(finalizeKernel, groups, 1, 1);
                }
            }
        }

        void BookkeepingPhase()
        {
            // Production: no allocate-all debug path

            // Normal bookkeeping follows...
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

            // Step 1a: Build dynamic invalidation masks from moved renderers
            BuildDynamicInvalidationMasks();

            // Step 1b: Free invalidated pages
            if (freeInvalidatedPagesShader != null)
            {
                int kernel = freeInvalidatedPagesShader.FindKernel("FreeInvalidatedPages");
                freeInvalidatedPagesShader.SetTexture(kernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                freeInvalidatedPagesShader.SetBuffer(kernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                freeInvalidatedPagesShader.SetBuffer(kernel, "_CascadeShifts", cascadeShiftsBuffer);
                freeInvalidatedPagesShader.SetBuffer(kernel, "_DynamicInvalidationMasks", dynamicInvalidationMasksBuffer);
                freeInvalidatedPagesShader.SetInt("_DynamicMaskStride", DynamicMaskStride);

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

                // CRITICAL: Don't use GL.GetGPUProjectionMatrix for camera
                // The depth texture already handles platform differences
                Matrix4x4 viewProj = mainCamera.projectionMatrix * mainCamera.worldToCameraMatrix;
                markVisiblePagesShader.SetMatrix("_CameraInverseViewProjection", viewProj.inverse);
                markVisiblePagesShader.SetMatrix("_CameraViewProjection", viewProj);
                markVisiblePagesShader.SetVector("_CameraPosition", mainCamera.transform.position);
                markVisiblePagesShader.SetFloat("_FirstCascadeSize", firstCascadeSize);
                markVisiblePagesShader.SetInt("_ScreenWidth", mainCamera.pixelWidth);
                markVisiblePagesShader.SetInt("_ScreenHeight", mainCamera.pixelHeight);
                markVisiblePagesShader.SetInt("_FilterMargin", filterMargin);

                // DEBUG: Log depth texture info
                RenderTexture depthTex = GetDepthTexture();
                if (depthTex != null)
                {
                    Debug.Log($"[VSM MarkVisible] Depth texture: {depthTex.width}×{depthTex.height}, format: {depthTex.format}");
                }
                else
                {
                    Debug.LogError("[VSM MarkVisible] Depth texture is null!");
                }

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

                // DEBUG: Only log if there are requests
                if (allocationRequestCount > 0)
                {
                    Debug.Log($"[VSM AllocationPhase] Allocation requests: {allocationRequestCount}");
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

                    // Debug counts/logs removed
                }
            }

            // Step 5: Clear dirty pages
            if (clearPagesShader != null)
            {
                // Get allocation request count again for clearing
                ComputeBuffer.CopyCount(pageTable.AllocationRequests, allocationCounterBuffer, 0);
                uint[] counts = new uint[2];
                allocationCounterBuffer.GetData(counts);
                uint allocationRequestCount = counts[0];

                if (allocationRequestCount > 0)
                {
                    int clearKernel = clearPagesShader.FindKernel("ClearDirtyPages");
                    clearPagesShader.SetBuffer(clearKernel, "_PhysicalMemoryBuffer", physicalMemory.Buffer);
                    clearPagesShader.SetInt("_PhysicalMemoryWidth", VSMConstants.PHYSICAL_MEMORY_WIDTH);
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

        // Build dynamic invalidation bit masks for objects that moved this frame
        void BuildDynamicInvalidationMasks()
        {
            if (dynamicInvalidationMaskCPU == null)
                return;

            System.Array.Clear(dynamicInvalidationMaskCPU, 0, dynamicInvalidationMaskCPU.Length);

            // Iterate all renderers and mark pages their bounds cover if moved since last frame
            var renderers = FindObjectsOfType<Renderer>();

            for (int ri = 0; ri < renderers.Length; ri++)
            {
                var r = renderers[ri];
                if (((1 << r.gameObject.layer) & shadowCasters) == 0)
                    continue;

                Bounds current = r.bounds;
                bool moved = true;
                if (prevRendererBounds.TryGetValue(r, out var prev))
                {
                    // Consider moved if center or extents changed beyond small epsilon
                    moved = (prev.center - current.center).sqrMagnitude > 1e-6f || (prev.extents - current.extents).sqrMagnitude > 1e-6f;
                }
                prevRendererBounds[r] = current;

                if (!moved) continue;

                // Project to each cascade and mark intersecting pages
                for (int c = 0; c < VSMConstants.CASCADE_COUNT; c++)
                {
                    Matrix4x4 lightMat = cascadeLightMatrices[c];
                    // Transform 8 corners into light space
                    Vector3 min = current.min; Vector3 max = current.max;
                    Vector3[] corners = new Vector3[8]
                    {
                        new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
                        new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z),
                        new Vector3(min.x,min.y,max.z), new Vector3(max.x,min.y,max.z),
                        new Vector3(min.x,max.y,max.z), new Vector3(max.x,max.y,max.z)
                    };

                    Vector2 uvMin = new Vector2(1e10f, 1e10f);
                    Vector2 uvMax = new Vector2(-1e10f, -1e10f);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector4 lp = lightMat * new Vector4(corners[i].x, corners[i].y, corners[i].z, 1.0f);
                        Vector3 ndc = new Vector3(lp.x / lp.w, lp.y / lp.w, lp.z / lp.w);
                        Vector2 uv = new Vector2(ndc.x * 0.5f + 0.5f, ndc.y * 0.5f + 0.5f);
                        uvMin = Vector2.Min(uvMin, uv);
                        uvMax = Vector2.Max(uvMax, uv);
                    }

                    // Clamp intersection with [0,1]
                    uvMin = Vector2.Max(uvMin, Vector2.zero);
                    uvMax = Vector2.Min(uvMax, Vector2.one);
                    if (uvMin.x >= uvMax.x || uvMin.y >= uvMax.y)
                        continue;

                    // Convert to page coordinates and clamp
                    Vector2Int pageMin = new Vector2Int(
                        Mathf.Clamp((int)Mathf.Floor(uvMin.x * VSMConstants.PAGE_TABLE_RESOLUTION), 0, VSMConstants.PAGE_TABLE_RESOLUTION - 1),
                        Mathf.Clamp((int)Mathf.Floor(uvMin.y * VSMConstants.PAGE_TABLE_RESOLUTION), 0, VSMConstants.PAGE_TABLE_RESOLUTION - 1)
                    );
                    Vector2Int pageMax = new Vector2Int(
                        Mathf.Clamp((int)Mathf.Floor(uvMax.x * VSMConstants.PAGE_TABLE_RESOLUTION), 0, VSMConstants.PAGE_TABLE_RESOLUTION - 1),
                        Mathf.Clamp((int)Mathf.Floor(uvMax.y * VSMConstants.PAGE_TABLE_RESOLUTION), 0, VSMConstants.PAGE_TABLE_RESOLUTION - 1)
                    );

                    for (int py = pageMin.y; py <= pageMax.y; py++)
                    {
                        for (int px = pageMin.x; px <= pageMax.x; px++)
                        {
                            int linear = py * VSMConstants.PAGE_TABLE_RESOLUTION + px;
                            int wordIndex = linear >> 5;
                            int bitIndex = linear & 31;
                            int globalIndex = c * DynamicMaskStride + wordIndex;
                            dynamicInvalidationMaskCPU[globalIndex] |= (uint)(1u << bitIndex);
                        }
                    }
                }
            }

            // Upload to GPU
            dynamicInvalidationMasksBuffer.SetData(dynamicInvalidationMaskCPU);
        }

        void DrawingPhase()
        {
            // Paper section 12.2.2: "Drawing Phase"
            // Render scene geometry to each cascade's dirty pages using HPB culling

            if (vsmCommandBuffer == null)
                return;

            Debug.Log($"[VSM DrawingPhase] Using {(useMeshletRendering && meshletRenderer != null ? "MESHLET" : "TRADITIONAL")} rendering");

            // 选择渲染路径：Meshlet（论文方法）或传统方法
            if (useMeshletRendering && meshletRenderer != null)
            {
                DrawingPhase_Meshlet();
            }
            else
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

            // Create temporary render texture for each cascade (virtual resolution)
            // We render to virtual resolution, then copy to sparse physical memory
            int virtualRes = VSMConstants.VIRTUAL_TEXTURE_RESOLUTION;
            int tempDepthID = Shader.PropertyToID("_VSMTempDepth");

            vsmCommandBuffer.GetTemporaryRT(tempDepthID,
                virtualRes, virtualRes,
                24, FilterMode.Point, RenderTextureFormat.RFloat);

            // Set global shader properties for VSM rendering
            vsmCommandBuffer.SetGlobalTexture("_VirtualPageTable", pageTable.VirtualPageTableTexture);
            vsmCommandBuffer.SetGlobalBuffer("_CascadeLightMatrices", cascadeLightMatricesBuffer);
            vsmCommandBuffer.SetGlobalBuffer("_CascadeOffsets", cascadeOffsetsBuffer);

            // Get all shadow casters in the scene
            Renderer[] renderers = FindObjectsOfType<Renderer>();

            // Debug renderer count log removed

            // Render each cascade
            for (int cascadeIndex = 0; cascadeIndex < VSMConstants.CASCADE_COUNT; cascadeIndex++)
            {
                int drawnThisCascade = 0; // DEBUG counter

                // Set render target and clear
                vsmCommandBuffer.SetRenderTarget(tempDepthID);
                vsmCommandBuffer.ClearRenderTarget(true, true, Color.clear);

                // Set current cascade index
                vsmCommandBuffer.SetGlobalInt("_CurrentCascade", cascadeIndex);

                foreach (Renderer renderer in renderers)
                {
                    // Check if renderer is in shadow caster layer
                    if (((1 << renderer.gameObject.layer) & shadowCasters) == 0)
                    {
                        continue;
                    }

                    // Get mesh
                    MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    // Draw mesh to temp RT using command buffer
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

                // Debug draw count log removed

                // Copy from temp RT to physical memory using compute shader
                if (copyDepthShader != null)
                {
                    int kernel = copyDepthShader.FindKernel("CopyDepthToPhysicalMemory");
                    vsmCommandBuffer.SetComputeTextureParam(copyDepthShader, kernel, "_SourceDepth", tempDepthID);
                    vsmCommandBuffer.SetComputeBufferParam(copyDepthShader, kernel, "_PhysicalMemoryBuffer", physicalMemory.Buffer);
                    vsmCommandBuffer.SetComputeIntParam(copyDepthShader, "_PhysicalMemoryWidth", VSMConstants.PHYSICAL_MEMORY_WIDTH);
                    vsmCommandBuffer.SetComputeTextureParam(copyDepthShader, kernel, "_VirtualPageTable", pageTable.VirtualPageTableTexture);
                    vsmCommandBuffer.SetComputeBufferParam(copyDepthShader, kernel, "_CascadeOffsets", cascadeOffsetsBuffer);
                    vsmCommandBuffer.SetComputeIntParam(copyDepthShader, "_CurrentCascade", cascadeIndex);
                    vsmCommandBuffer.SetComputeIntParam(copyDepthShader, "_VirtualResolution", virtualRes);

                    int threadGroups = Mathf.CeilToInt(virtualRes / 8.0f);
                    vsmCommandBuffer.DispatchCompute(copyDepthShader, kernel, threadGroups, threadGroups, 1);

                    // Debug copy log removed
                }
                else
                {
                    // No copy shader set
                }
            }

            // Release temporary RT
            vsmCommandBuffer.ReleaseTemporaryRT(tempDepthID);

            // Execute the command buffer
            Graphics.ExecuteCommandBuffer(vsmCommandBuffer);

            // Debug physical memory inspection removed
        }

        void DrawingPhase_Meshlet()
        {
            // 论文实现：Meshlet + Task/Mesh Shader
            // "To achieve granular culling, our drawing was implemented with meshlets combined with mesh shaders"

            vsmCommandBuffer.Clear();

            // Set global buffer for physical memory (RWStructuredBuffer)
            vsmCommandBuffer.SetGlobalBuffer("_PhysicalMemory", physicalMemory.Buffer);
            vsmCommandBuffer.SetGlobalInt("_PhysicalMemoryWidth", VSMConstants.PHYSICAL_MEMORY_WIDTH);

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
                        physicalMemory.Buffer  // Pass buffer instead of texture
                    );
                }
            }

            // Execute command buffer
            Graphics.ExecuteCommandBuffer(vsmCommandBuffer);
        }

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
            // Ensure buffers are created before binding
            if (cascadeLightMatricesBuffer == null || cascadeOffsetsBuffer == null)
            {
                Debug.LogError("[VSM] Cannot bind shaders - buffers not initialized!");
                return;
            }

            // Bind VSM textures and buffers globally for sampling in materials
            Shader.SetGlobalTexture("_VSM_VirtualPageTable", pageTable.VirtualPageTableTexture);
            Shader.SetGlobalTexture("_VSM_PhysicalMemory", physicalMemory.Texture);
            Shader.SetGlobalBuffer(VSMStaticInitializer.kVsmCascadeLightMatricesId, cascadeLightMatricesBuffer);
            Shader.SetGlobalBuffer(VSMStaticInitializer.kCascadeLightMatricesId, cascadeLightMatricesBuffer);
            Shader.SetGlobalBuffer(VSMStaticInitializer.kVsmCascadeOffsetsId, cascadeOffsetsBuffer);
            Shader.SetGlobalBuffer(VSMStaticInitializer.kCascadeOffsetsId, cascadeOffsetsBuffer);
            Shader.SetGlobalFloat("_VSM_FirstCascadeSize", firstCascadeSize);
            Shader.SetGlobalVector("_VSM_CameraPosition", mainCamera.transform.position);

            Debug.Log($"[VSM] Buffers bound: CascadeLightMatrices={cascadeLightMatricesBuffer.count}, CascadeOffsets={cascadeOffsetsBuffer.count}");

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
            if (meshletRenderer != null)
            {
                meshletRenderer.Release();
                Destroy(meshletRenderer.gameObject);
            }
        }

        // OnGUI debug overlay removed
    }
}
