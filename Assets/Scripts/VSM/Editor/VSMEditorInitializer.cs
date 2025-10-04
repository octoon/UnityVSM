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
        static VSMEditorInitializer()
        {
            Debug.Log("[VSM Editor] Ensuring VSM fallback buffers are bound in editor mode...");
            VSMStaticInitializer.EnsureInitialized();

            // Register cleanup on domain reload
            EditorApplication.quitting += Cleanup;
        }

        static void Cleanup()
        {
            VSMStaticInitializer.Cleanup();
            Debug.Log("[VSM Editor] Cleaned up VSM fallback buffers");
        }
    }
}
