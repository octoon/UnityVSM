using UnityEngine;
using UnityEditor;
using System.Reflection;

/// <summary>
/// VSM实时预览窗口
/// 上方：Physical Memory Atlas（物理内存图集）
/// 下方：Page Table Lookup Texture（页表查找纹理）
/// </summary>
public class VSMPreviewWindow : EditorWindow
{
    private VSM.VirtualShadowMapManager vsmManager;
    private Vector2 scrollPosition;
    private bool autoRefresh = true;
    private float refreshRate = 0.1f;
    private double lastRefreshTime;

    private bool showPhysicalMemory = true;
    private bool showPageTable = true;
    private int selectedCascade = 0;
    private float textureScale = 0.5f;

    private enum VisMode { Raw, Depth, Binary }
    private VisMode physicalMemoryMode = VisMode.Depth;
    private VisMode pageTableMode = VisMode.Binary;

    [MenuItem("Window/VSM Preview")]
    public static void ShowWindow()
    {
        VSMPreviewWindow window = GetWindow<VSMPreviewWindow>("VSM Preview");
        window.minSize = new Vector2(500, 700);
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
            vsmManager = FindObjectOfType<VSM.VirtualShadowMapManager>();
            if (vsmManager == null)
            {
                EditorGUILayout.HelpBox("未找到 VirtualShadowMapManager\n请确保场景中有VSM Manager组件", MessageType.Warning);
                if (GUILayout.Button("刷新"))
                {
                    Repaint();
                }
                return;
            }
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // === 控制面板 ===
        GUILayout.Label("控制面板", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        autoRefresh = EditorGUILayout.Toggle("自动刷新", autoRefresh);
        if (!autoRefresh && GUILayout.Button("手动刷新", GUILayout.Width(100)))
        {
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        if (autoRefresh)
        {
            refreshRate = EditorGUILayout.Slider("刷新率(秒)", refreshRate, 0.05f, 2f);
        }

        EditorGUILayout.Space();
        showPhysicalMemory = EditorGUILayout.Toggle("显示物理内存图集", showPhysicalMemory);
        showPageTable = EditorGUILayout.Toggle("显示页表纹理", showPageTable);

        EditorGUILayout.Space();
        textureScale = EditorGUILayout.Slider("预览缩放", textureScale, 0.1f, 2f);
        selectedCascade = EditorGUILayout.IntSlider("Cascade层级", selectedCascade, 0, VSM.VSMConstants.CASCADE_COUNT - 1);

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // === 物理内存图集 ===
        if (showPhysicalMemory)
        {
            DrawPhysicalMemorySection();
        }

        EditorGUILayout.Space(10);

        // === 页表纹理 ===
        if (showPageTable)
        {
            DrawPageTableSection();
        }

        EditorGUILayout.EndScrollView();

        // 自动刷新
        if (autoRefresh && EditorApplication.timeSinceStartup - lastRefreshTime > refreshRate)
        {
            lastRefreshTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    void DrawPhysicalMemorySection()
    {
        GUILayout.Label("物理内存图集 (Physical Memory Atlas)", EditorStyles.boldLabel);

        string info = string.Format("分辨率: {0}x{1} | 页大小: {2}x{2} | 总页数: {3}",
            VSM.VSMConstants.PHYSICAL_MEMORY_WIDTH,
            VSM.VSMConstants.PHYSICAL_MEMORY_HEIGHT,
            VSM.VSMConstants.PAGE_SIZE,
            VSM.VSMConstants.MAX_PHYSICAL_PAGES);

        EditorGUILayout.HelpBox(info, MessageType.Info);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        physicalMemoryMode = (VisMode)EditorGUILayout.EnumPopup("可视化模式", physicalMemoryMode);

        RenderTexture physicalMemory = GetPhysicalMemoryTexture();
        if (physicalMemory != null)
        {
            float aspect = (float)physicalMemory.width / physicalMemory.height;
            float width = Mathf.Min(position.width - 40, 800) * textureScale;
            float height = width / aspect;

            Rect rect = GUILayoutUtility.GetRect(width, height);

            switch (physicalMemoryMode)
            {
                case VisMode.Raw:
                    EditorGUI.DrawPreviewTexture(rect, physicalMemory);
                    break;
                case VisMode.Depth:
                    DrawInverted(rect, physicalMemory);
                    break;
                case VisMode.Binary:
                    DrawBinary(rect, physicalMemory);
                    break;
            }

            GUILayout.Label(string.Format("纹理: {0}x{1}", physicalMemory.width, physicalMemory.height), EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.HelpBox("物理内存纹理未初始化或不可访问", MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    void DrawPageTableSection()
    {
        GUILayout.Label("页表查找纹理 (Page Table)", EditorStyles.boldLabel);

        string info = string.Format("分辨率: {0}x{0} | Cascades: {1} | 当前: Cascade {2}",
            VSM.VSMConstants.PAGE_TABLE_RESOLUTION,
            VSM.VSMConstants.CASCADE_COUNT,
            selectedCascade);

        EditorGUILayout.HelpBox(info, MessageType.Info);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        pageTableMode = (VisMode)EditorGUILayout.EnumPopup("可视化模式", pageTableMode);

        RenderTexture pageTable = GetPageTableTexture();
        if (pageTable != null)
        {
            float size = Mathf.Min(position.width - 40, 600) * textureScale;
            Rect rect = GUILayoutUtility.GetRect(size, size);

            // 2D Array纹理需要特殊处理，这里简化显示
            EditorGUI.DrawPreviewTexture(rect, pageTable);

            GUILayout.Label(string.Format("纹理: {0}x{1} Array[{2}]",
                pageTable.width, pageTable.height, pageTable.volumeDepth),
                EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.HelpBox("页表纹理未初始化或不可访问", MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    RenderTexture GetPhysicalMemoryTexture()
    {
        if (vsmManager == null) return null;

        var field = typeof(VSM.VirtualShadowMapManager).GetField("physicalMemory",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
        {
            var physicalMemory = field.GetValue(vsmManager);
            if (physicalMemory != null)
            {
                var texProp = physicalMemory.GetType().GetProperty("Texture");
                return texProp?.GetValue(physicalMemory) as RenderTexture;
            }
        }
        return null;
    }

    RenderTexture GetPageTableTexture()
    {
        if (vsmManager == null) return null;

        var field = typeof(VSM.VirtualShadowMapManager).GetField("pageTable",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
        {
            var pageTable = field.GetValue(vsmManager);
            if (pageTable != null)
            {
                var texProp = pageTable.GetType().GetProperty("VirtualPageTableTexture");
                return texProp?.GetValue(pageTable) as RenderTexture;
            }
        }
        return null;
    }

    void DrawInverted(Rect rect, RenderTexture tex)
    {
        // 简单反转深度显示
        Material mat = new Material(Shader.Find("Hidden/Internal-Colored"));
        EditorGUI.DrawPreviewTexture(rect, tex);
        Object.DestroyImmediate(mat);
    }

    void DrawBinary(Rect rect, RenderTexture tex)
    {
        EditorGUI.DrawPreviewTexture(rect, tex);
    }

    void OnInspectorUpdate()
    {
        if (autoRefresh)
        {
            Repaint();
        }
    }
}
