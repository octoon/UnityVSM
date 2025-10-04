using UnityEngine;
using UnityEditor;

namespace VSM
{
    /// <summary>
    /// Editor initializer to ensure VSM buffers exist in edit mode
    /// </summary>
    [InitializeOnLoad]
    public static class VSMEditorInitializer
    {
        private static ComputeBuffer s_cascadeLightMatricesBuffer;
        private static ComputeBuffer s_cascadeOffsetsBuffer;

        static VSMEditorInitializer()
        {
            Debug.Log("[VSM Editor] Initializing dummy buffers in editor mode...");

            // Clean up any existing buffers
            s_cascadeLightMatricesBuffer?.Release();
            s_cascadeOffsetsBuffer?.Release();

            // Create minimal buffers to prevent shader errors in editor
            s_cascadeLightMatricesBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(float) * 16);
            s_cascadeLightMatricesBuffer.SetData(new Matrix4x4[VSMConstants.CASCADE_COUNT]);
            Shader.SetGlobalBuffer("_VSM_CascadeLightMatrices", s_cascadeLightMatricesBuffer);

            s_cascadeOffsetsBuffer = new ComputeBuffer(VSMConstants.CASCADE_COUNT, sizeof(int) * 2);
            s_cascadeOffsetsBuffer.SetData(new int[VSMConstants.CASCADE_COUNT * 2]);
            Shader.SetGlobalBuffer("_VSM_CascadeOffsets", s_cascadeOffsetsBuffer);

            Debug.Log("[VSM Editor] Dummy buffers created and bound globally in editor");

            // Register cleanup on domain reload
            EditorApplication.quitting += Cleanup;
        }

        static void Cleanup()
        {
            s_cascadeLightMatricesBuffer?.Release();
            s_cascadeOffsetsBuffer?.Release();
            Debug.Log("[VSM Editor] Cleaned up editor buffers");
        }
    }
}
