using UnityEngine;
using UnityEditor;

namespace VSM.Editor
{
    /// <summary>
    /// 实时预览窗口：显示Virtual Shadow Map的物理内存图集和页表查找纹理
    /// 上方显示Physical Memory Atlas（合并的阴影图集）
    /// 下方显示Page Table Lookup Texture（虚拟页表纹理）
    /// </summary>
    public class VSMPreviewWindow : EditorWindow
    {
        private VirtualShadowMapManager vsmManager;
        private Vector2 scrollPosition;
        private bool autoRefresh = true;
        private float refreshRate = 0.1f;
        private double lastRefreshTime;

        // 显示选项
        private bool showPhysicalMemory = true;
        private bool showPageTable = true;
        private int selectedCascade = 0;
        private float textureScale = 1.0f;

        // 可视化模式
        private enum VisualizationMode
        {
            Depth,          // 深度可视化
            Binary,         // 二值化（分配/未分配）
            Heatmap         // 热力图
        }
        private VisualizationMode physicalMemoryMode = VisualizationMode.Depth;
        private VisualizationMode pageTableMode = VisualizationMode.Binary;

        [MenuItem("Window/VSM/Preview Window")]
        public static void ShowWindow()
        {
            VSMPreviewWindow window = GetWindow<VSMPreviewWindow>("VSM Preview");
            window.minSize = new Vector2(400, 600);
            window.Show();
        }

        void OnEnable()
        {
            lastRefreshTime = EditorApplication.timeSinceStartup;
        }

        void OnGUI()
        {
            // 查找VSM管理器
            if (vsmManager == null)
            {
                vsmManager = FindObjectOfType<VirtualShadowMapManager>();
                if (vsmManager == null)
                {
                    EditorGUILayout.HelpBox("未找到VirtualShadowMapManager组件。请确保场景中存在VSM Manager。", MessageType.Warning);
                    return;
                }
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // 控制面板
            DrawControlPanel();

            EditorGUILayout.Space(10);

            // 物理内存图集预览
            if (showPhysicalMemory)
            {
                DrawPhysicalMemoryPreview();
            }

            EditorGUILayout.Space(10);

            // 页表查找纹理预览
            if (showPageTable)
            {
                DrawPageTablePreview();
            }

            EditorGUILayout.EndScrollView();

            // 自动刷新
            if (autoRefresh && EditorApplication.timeSinceStartup - lastRefreshTime > refreshRate)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        void DrawControlPanel()
        {
            EditorGUILayout.LabelField("控制面板", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 自动刷新控制
            EditorGUILayout.BeginHorizontal();
            autoRefresh = EditorGUILayout.Toggle("自动刷新", autoRefresh);
            if (!autoRefresh)
            {
                if (GUILayout.Button("手动刷新", GUILayout.Width(80)))
                {
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (autoRefresh)
            {
                refreshRate = EditorGUILayout.Slider("刷新率 (秒)", refreshRate, 0.01f, 1.0f);
            }

            EditorGUILayout.Space(5);

            // 显示选项
            showPhysicalMemory = EditorGUILayout.Toggle("显示物理内存图集", showPhysicalMemory);
            showPageTable = EditorGUILayout.Toggle("显示页表纹理", showPageTable);

            EditorGUILayout.Space(5);

            // 缩放控制
            textureScale = EditorGUILayout.Slider("预览缩放", textureScale, 0.1f, 2.0f);

            EditorGUILayout.Space(5);

            // Cascade选择
            selectedCascade = EditorGUILayout.IntSlider("选择Cascade层级", selectedCascade, 0, VSMConstants.CASCADE_COUNT - 1);

            EditorGUILayout.EndVertical();
        }

        void DrawPhysicalMemoryPreview()
        {
            EditorGUILayout.LabelField("物理内存图集 (Physical Memory Atlas)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"分辨率: {VSMConstants.PHYSICAL_MEMORY_WIDTH}x{VSMConstants.PHYSICAL_MEMORY_HEIGHT} | 页大小: {VSMConstants.PAGE_SIZE}x{VSMConstants.PAGE_SIZE}", MessageType.Info);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 可视化模式选择
            physicalMemoryMode = (VisualizationMode)EditorGUILayout.EnumPopup("可视化模式", physicalMemoryMode);

            EditorGUILayout.Space(5);

            // 获取物理内存纹理
            RenderTexture physicalMemoryTexture = GetPhysicalMemoryTexture();
            if (physicalMemoryTexture != null)
            {
                // 计算显示尺寸
                float aspectRatio = (float)physicalMemoryTexture.width / physicalMemoryTexture.height;
                float displayWidth = position.width - 40;
                float displayHeight = displayWidth / aspectRatio;
                displayWidth *= textureScale;
                displayHeight *= textureScale;

                Rect textureRect = GUILayoutUtility.GetRect(displayWidth, displayHeight);

                // 根据可视化模式绘制纹理
                switch (physicalMemoryMode)
                {
                    case VisualizationMode.Depth:
                        // 深度可视化（反转：黑色=远，白色=近）
                        DrawTextureWithDepthVisualization(textureRect, physicalMemoryTexture);
                        break;
                    case VisualizationMode.Binary:
                        // 二值化显示
                        DrawTextureWithBinaryVisualization(textureRect, physicalMemoryTexture);
                        break;
                    case VisualizationMode.Heatmap:
                        // 热力图
                        DrawTextureWithHeatmapVisualization(textureRect, physicalMemoryTexture);
                        break;
                }

                // 显示统计信息
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"总页数: {VSMConstants.MAX_PHYSICAL_PAGES}");
                EditorGUILayout.LabelField($"页布局: {VSMConstants.PHYSICAL_MEMORY_WIDTH / VSMConstants.PAGE_SIZE} x {VSMConstants.PHYSICAL_MEMORY_HEIGHT / VSMConstants.PAGE_SIZE}");
            }
            else
            {
                EditorGUILayout.HelpBox("物理内存纹理未初始化", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        void DrawPageTablePreview()
        {
            EditorGUILayout.LabelField("页表查找纹理 (Page Table Lookup)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"分辨率: {VSMConstants.PAGE_TABLE_RESOLUTION}x{VSMConstants.PAGE_TABLE_RESOLUTION} | Cascades: {VSMConstants.CASCADE_COUNT}", MessageType.Info);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 可视化模式选择
            pageTableMode = (VisualizationMode)EditorGUILayout.EnumPopup("可视化模式", pageTableMode);

            EditorGUILayout.Space(5);

            // 获取页表纹理（2D数组纹理，选择当前cascade）
            RenderTexture pageTableTexture = GetPageTableTexture();
            if (pageTableTexture != null)
            {
                // 计算显示尺寸
                float displaySize = Mathf.Min(position.width - 40, 512) * textureScale;
                Rect textureRect = GUILayoutUtility.GetRect(displaySize, displaySize);

                // 根据可视化模式绘制纹理
                switch (pageTableMode)
                {
                    case VisualizationMode.Depth:
                        // 显示物理页坐标（X, Y编码为颜色）
                        DrawPageTableCoordinates(textureRect, pageTableTexture);
                        break;
                    case VisualizationMode.Binary:
                        // 二值化：已分配/未分配
                        DrawPageTableAllocation(textureRect, pageTableTexture);
                        break;
                    case VisualizationMode.Heatmap:
                        // 热力图：显示页面使用密度
                        DrawPageTableHeatmap(textureRect, pageTableTexture);
                        break;
                }

                // 显示统计信息
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"当前Cascade: {selectedCascade}");
                EditorGUILayout.LabelField($"虚拟页分辨率: {VSMConstants.PAGE_TABLE_RESOLUTION}x{VSMConstants.PAGE_TABLE_RESOLUTION}");
            }
            else
            {
                EditorGUILayout.HelpBox("页表纹理未初始化", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        // 获取物理内存纹理
        RenderTexture GetPhysicalMemoryTexture()
        {
            if (vsmManager == null) return null;

            // 通过反射获取私有字段
            var physicalMemoryField = typeof(VirtualShadowMapManager).GetField("physicalMemory",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (physicalMemoryField != null)
            {
                var physicalMemory = physicalMemoryField.GetValue(vsmManager);
                if (physicalMemory != null)
                {
                    var textureProperty = physicalMemory.GetType().GetProperty("Texture");
                    return textureProperty?.GetValue(physicalMemory) as RenderTexture;
                }
            }

            return null;
        }

        // 获取页表纹理
        RenderTexture GetPageTableTexture()
        {
            if (vsmManager == null) return null;

            // 通过反射获取私有字段
            var pageTableField = typeof(VirtualShadowMapManager).GetField("pageTable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (pageTableField != null)
            {
                var pageTable = pageTableField.GetValue(vsmManager);
                if (pageTable != null)
                {
                    var textureProperty = pageTable.GetType().GetProperty("VirtualPageTableTexture");
                    return textureProperty?.GetValue(pageTable) as RenderTexture;
                }
            }

            return null;
        }

        // 深度可视化（反转深度）
        void DrawTextureWithDepthVisualization(Rect rect, RenderTexture texture)
        {
            Material visualizeMat = new Material(Shader.Find("Hidden/VSMDepthVisualize"));
            if (visualizeMat != null)
            {
                EditorGUI.DrawPreviewTexture(rect, texture, visualizeMat);
                DestroyImmediate(visualizeMat);
            }
            else
            {
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }

        // 二值化可视化
        void DrawTextureWithBinaryVisualization(Rect rect, RenderTexture texture)
        {
            Material visualizeMat = new Material(Shader.Find("Hidden/VSMBinaryVisualize"));
            if (visualizeMat != null)
            {
                EditorGUI.DrawPreviewTexture(rect, texture, visualizeMat);
                DestroyImmediate(visualizeMat);
            }
            else
            {
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }

        // 热力图可视化
        void DrawTextureWithHeatmapVisualization(Rect rect, RenderTexture texture)
        {
            Material visualizeMat = new Material(Shader.Find("Hidden/VSMHeatmapVisualize"));
            if (visualizeMat != null)
            {
                EditorGUI.DrawPreviewTexture(rect, texture, visualizeMat);
                DestroyImmediate(visualizeMat);
            }
            else
            {
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }

        // 页表坐标可视化
        void DrawPageTableCoordinates(Rect rect, RenderTexture texture)
        {
            Material visualizeMat = new Material(Shader.Find("Hidden/VSMPageTableCoords"));
            if (visualizeMat != null)
            {
                visualizeMat.SetInt("_CascadeIndex", selectedCascade);
                EditorGUI.DrawPreviewTexture(rect, texture, visualizeMat);
                DestroyImmediate(visualizeMat);
            }
            else
            {
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }

        // 页表分配状态可视化
        void DrawPageTableAllocation(Rect rect, RenderTexture texture)
        {
            Material visualizeMat = new Material(Shader.Find("Hidden/VSMPageTableAllocation"));
            if (visualizeMat != null)
            {
                visualizeMat.SetInt("_CascadeIndex", selectedCascade);
                EditorGUI.DrawPreviewTexture(rect, texture, visualizeMat);
                DestroyImmediate(visualizeMat);
            }
            else
            {
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }

        // 页表热力图可视化
        void DrawPageTableHeatmap(Rect rect, RenderTexture texture)
        {
            Material visualizeMat = new Material(Shader.Find("Hidden/VSMPageTableHeatmap"));
            if (visualizeMat != null)
            {
                visualizeMat.SetInt("_CascadeIndex", selectedCascade);
                EditorGUI.DrawPreviewTexture(rect, texture, visualizeMat);
                DestroyImmediate(visualizeMat);
            }
            else
            {
                EditorGUI.DrawPreviewTexture(rect, texture);
            }
        }

        void OnInspectorUpdate()
        {
            if (autoRefresh)
            {
                Repaint();
            }
        }
    }
}
