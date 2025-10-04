using UnityEngine;

namespace VSM
{
    /// <summary>
    /// Debug visualization for VSM physical memory and page table
    /// 调试可视化工具：显示物理内存、页表状态、级联覆盖等
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VSMDebugVisualizer : MonoBehaviour
    {
        public enum VisualizationMode
        {
            PhysicalMemory = 0,      // 物理内存深度图
            PageTableState = 1,      // 页表状态（分配/可见/脏页）
            PhysicalMapping = 2,     // 虚拟页->物理页映射
            VirtualDepth = 3,        // 通过虚拟页采样的深度
            CascadeCoverage = 4      // 级联覆盖范围
        }

        [Header("Visualization Settings")]
        [SerializeField] private VisualizationMode visMode = VisualizationMode.PhysicalMemory;
        [SerializeField] [Range(0, 15)] private int cascadeLevel = 0;
        [SerializeField] private bool showDebugWindow = true;

        [Header("Shader")]
        [SerializeField] private Shader debugShader;

        private Material debugMaterial;

        void OnEnable()
        {
            if (debugShader != null)
            {
                debugMaterial = new Material(debugShader);
            }
        }

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (debugMaterial != null)
            {
                debugMaterial.SetFloat("_VisMode", (float)visMode);
                debugMaterial.SetFloat("_CascadeLevel", cascadeLevel);

                Graphics.Blit(source, destination, debugMaterial);
            }
            else
            {
                Graphics.Blit(source, destination);
            }
        }

        void OnGUI()
        {
            if (!showDebugWindow)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 400, 300));
            GUILayout.BeginVertical("box");

            GUILayout.Label("VSM Debug Visualization", GUI.skin.box);

            GUILayout.Space(10);

            GUILayout.Label("Visualization Mode:");
            visMode = (VisualizationMode)GUILayout.SelectionGrid(
                (int)visMode,
                System.Enum.GetNames(typeof(VisualizationMode)),
                1
            );

            GUILayout.Space(10);

            GUILayout.Label($"Cascade Level: {cascadeLevel}");
            cascadeLevel = (int)GUILayout.HorizontalSlider(cascadeLevel, 0, VSMConstants.CASCADE_COUNT - 1);

            GUILayout.Space(10);

            // 显示当前模式的说明
            string description = GetModeDescription(visMode);
            GUILayout.Label(description, GUI.skin.box);

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        string GetModeDescription(VisualizationMode mode)
        {
            switch (mode)
            {
                case VisualizationMode.PhysicalMemory:
                    return "物理内存深度图\n白色=远平面(1.0), 黑色=近平面(0.0)\n" +
                           "应该看到分散的128×128页块";

                case VisualizationMode.PageTableState:
                    return "页表状态\n黑色=未分配\n绿色=已分配但不可见\n" +
                           "黄色=可见但不脏\n红色=脏页(需要渲染)";

                case VisualizationMode.PhysicalMapping:
                    return "虚拟页->物理页映射\n颜色表示物理页坐标\n" +
                           "相同颜色=相同物理页";

                case VisualizationMode.VirtualDepth:
                    return "虚拟纹理深度采样\n通过虚拟页表采样深度\n" +
                           "应该看到完整的阴影图";

                case VisualizationMode.CascadeCoverage:
                    return "级联覆盖范围\n绿色=有效范围\n红色=超出范围";

                default:
                    return "";
            }
        }

        void OnDisable()
        {
            if (debugMaterial != null)
            {
                Destroy(debugMaterial);
            }
        }
    }
}
