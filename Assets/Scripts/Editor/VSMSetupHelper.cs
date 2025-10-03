using UnityEngine;
using UnityEditor;

/// <summary>
/// VSM设置助手 - 自动配置场景使用VSM材质
/// </summary>
public class VSMSetupHelper : EditorWindow
{
    private Material vsmMaterial;
    private bool includeChildren = true;
    private bool onlyIfNoMaterial = false;

    [MenuItem("Window/VSM Setup Helper")]
    public static void ShowWindow()
    {
        var window = GetWindow<VSMSetupHelper>("VSM Setup");
        window.minSize = new Vector2(300, 250);
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("VSM材质设置助手", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox("此工具会将VSM材质应用到场景中的所有MeshRenderer", MessageType.Info);

        EditorGUILayout.Space();

        // 材质选择
        vsmMaterial = (Material)EditorGUILayout.ObjectField(
            "VSM材质",
            vsmMaterial,
            typeof(Material),
            false
        );

        if (vsmMaterial == null)
        {
            EditorGUILayout.HelpBox("请先创建或选择一个VSM材质", MessageType.Warning);

            if (GUILayout.Button("创建新的VSM ForwardLit材质"))
            {
                CreateVSMMaterial();
            }
        }

        EditorGUILayout.Space();

        // 选项
        includeChildren = EditorGUILayout.Toggle("包含子物体", includeChildren);
        onlyIfNoMaterial = EditorGUILayout.Toggle("仅设置无材质物体", onlyIfNoMaterial);

        EditorGUILayout.Space();

        // 应用按钮
        GUI.enabled = vsmMaterial != null;

        if (GUILayout.Button("应用到所有场景物体", GUILayout.Height(30)))
        {
            ApplyMaterialToScene();
        }

        if (GUILayout.Button("应用到选中物体", GUILayout.Height(30)))
        {
            ApplyMaterialToSelection();
        }

        GUI.enabled = true;

        EditorGUILayout.Space();

        // 信息
        EditorGUILayout.HelpBox(
            "建议使用 VSM/ForwardLit shader\n" +
            "确保VirtualShadowMapManager已正确配置",
            MessageType.Info);
    }

    void CreateVSMMaterial()
    {
        // 查找VSM/ForwardLit shader
        Shader vsmShader = Shader.Find("VSM/ForwardLit");

        if (vsmShader == null)
        {
            EditorUtility.DisplayDialog("错误", "找不到 VSM/ForwardLit shader\n请确保shader已编译", "确定");
            return;
        }

        // 创建材质
        Material mat = new Material(vsmShader);
        mat.name = "VSM_ForwardLit_Material";

        // 保存到Assets
        string path = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(path + "/VSM_ForwardLit.mat");
        AssetDatabase.CreateAsset(mat, assetPath);
        AssetDatabase.SaveAssets();

        vsmMaterial = mat;

        EditorUtility.DisplayDialog("成功", "材质已创建: " + assetPath, "确定");
    }

    void ApplyMaterialToScene()
    {
        if (vsmMaterial == null) return;

        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
        int count = 0;

        foreach (var renderer in renderers)
        {
            if (ApplyMaterialToRenderer(renderer))
                count++;
        }

        EditorUtility.DisplayDialog(
            "完成",
            string.Format("已将VSM材质应用到 {0} 个物体", count),
            "确定"
        );

        Debug.Log($"VSM材质已应用到 {count} 个MeshRenderer");
    }

    void ApplyMaterialToSelection()
    {
        if (vsmMaterial == null) return;

        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选择要应用材质的物体", "确定");
            return;
        }

        int count = 0;

        foreach (var go in selected)
        {
            if (includeChildren)
            {
                MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>();
                foreach (var renderer in renderers)
                {
                    if (ApplyMaterialToRenderer(renderer))
                        count++;
                }
            }
            else
            {
                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null && ApplyMaterialToRenderer(renderer))
                    count++;
            }
        }

        EditorUtility.DisplayDialog(
            "完成",
            string.Format("已将VSM材质应用到 {0} 个物体", count),
            "确定"
        );

        Debug.Log($"VSM材质已应用到选中的 {count} 个MeshRenderer");
    }

    bool ApplyMaterialToRenderer(MeshRenderer renderer)
    {
        if (renderer == null) return false;

        // 如果设置了"仅无材质"，检查是否已有材质
        if (onlyIfNoMaterial && renderer.sharedMaterial != null)
            return false;

        Undo.RecordObject(renderer, "Apply VSM Material");
        renderer.sharedMaterial = vsmMaterial;
        EditorUtility.SetDirty(renderer);

        return true;
    }
}
