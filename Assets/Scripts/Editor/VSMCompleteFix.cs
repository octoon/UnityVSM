using UnityEngine;
using UnityEditor;

public class VSMCompleteFix : EditorWindow
{
    [MenuItem("VSM/Complete Fix and Configuration")]
    public static void ShowWindow()
    {
        var window = GetWindow<VSMCompleteFix>("VSM Complete Fix");
        window.minSize = new Vector2(400, 300);
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("VSM完整修复工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "当前症状：\n" +
            "• 近处：白色方块 ✅\n" +
            "• 远处：红紫条纹 ❌\n\n" +
            "问题：Cascade覆盖范围不够",
            MessageType.Info
        );

        EditorGUILayout.Space();

        if (GUILayout.Button("修复1：增大所有Cascade覆盖范围", GUILayout.Height(40)))
        {
            FixCascadeCoverage();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("修复2：调整相机和光源", GUILayout.Height(40)))
        {
            FixCameraAndLight();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("修复3：应用带光照的VSM材质", GUILayout.Height(40)))
        {
            ApplyLitMaterial();
        }

        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "执行顺序：\n" +
            "1. 先点击修复1\n" +
            "2. 再点击修复3\n" +
            "3. Play运行查看效果",
            MessageType.Info
        );
    }

    void FixCascadeCoverage()
    {
        var manager = FindObjectOfType<VSM.VirtualShadowMapManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("错误", "找不到VirtualShadowMapManager", "确定");
            return;
        }

        Undo.RecordObject(manager, "Fix Cascade Coverage");

        var type = typeof(VSM.VirtualShadowMapManager);

        // 大幅增加First Cascade Size
        var firstCascadeField = type.GetField("firstCascadeSize",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (firstCascadeField != null)
        {
            // 设置为50，覆盖更大范围
            firstCascadeField.SetValue(manager, 50.0f);
            Debug.Log("First Cascade Size → 50");
        }

        // 增加Filter Margin
        var filterMarginField = type.GetField("filterMargin",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (filterMarginField != null)
        {
            filterMarginField.SetValue(manager, 3);
            Debug.Log("Filter Margin → 3");
        }

        EditorUtility.SetDirty(manager);

        EditorUtility.DisplayDialog(
            "完成",
            "Cascade配置已更新：\n\n" +
            "• First Cascade Size = 50\n" +
            "• Filter Margin = 3\n\n" +
            "这应该能覆盖整个场景。\n" +
            "现在停止Play，重新运行。",
            "确定"
        );
    }

    void FixCameraAndLight()
    {
        var manager = FindObjectOfType<VSM.VirtualShadowMapManager>();
        if (manager == null) return;

        // 确保相机视野合适
        Camera cam = manager.GetComponent<Camera>();
        if (cam != null)
        {
            Undo.RecordObject(cam, "Adjust Camera");

            // 建议的相机设置
            if (cam.farClipPlane < 100)
                cam.farClipPlane = 1000;

            Debug.Log("Camera Far Clip Plane → " + cam.farClipPlane);
        }

        // 检查光源
        var lightField = typeof(VSM.VirtualShadowMapManager).GetField("directionalLight",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (lightField != null)
        {
            Light light = lightField.GetValue(manager) as Light;
            if (light != null)
            {
                Debug.Log($"Directional Light: {light.name}");
                Debug.Log($"Light Intensity: {light.intensity}");
            }
        }

        EditorUtility.DisplayDialog("完成", "相机设置已检查", "确定");
    }

    void ApplyLitMaterial()
    {
        // 创建带光照的VSM材质
        Shader shader = Shader.Find("VSM/ForwardLit");
        if (shader == null)
        {
            EditorUtility.DisplayDialog("错误", "找不到VSM/ForwardLit shader", "确定");
            return;
        }

        Material mat = new Material(shader);
        mat.name = "VSM_ForwardLit_Auto";

        // 保存
        string folder = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/VSM_ForwardLit_Auto.mat");
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();

        // 应用到所有物体
        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
        int count = 0;

        foreach (var renderer in renderers)
        {
            Undo.RecordObject(renderer, "Apply VSM Lit Material");
            renderer.sharedMaterial = mat;
            count++;
        }

        EditorUtility.DisplayDialog(
            "完成",
            $"VSM/ForwardLit材质已应用到 {count} 个物体\n\n" +
            "这个材质包含光照计算和VSM阴影。\n" +
            "现在应该能看到正确的光照和阴影了。",
            "确定"
        );

        Debug.Log($"✅ VSM/ForwardLit材质已应用到 {count} 个物体");
    }
}
