using UnityEngine;
using UnityEditor;

/// <summary>
/// VSM调试检查器 - 检查VSM系统配置是否正确
/// </summary>
public class VSMDebugChecker : EditorWindow
{
    [MenuItem("Window/VSM Debug Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<VSMDebugChecker>("VSM Debug");
        window.minSize = new Vector2(400, 500);
        window.Show();
    }

    private Vector2 scrollPos;

    void OnGUI()
    {
        GUILayout.Label("VSM系统诊断", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("此工具检查VSM系统配置是否正确", MessageType.Info);

        if (GUILayout.Button("运行诊断", GUILayout.Height(30)))
        {
            RunDiagnostics();
        }

        EditorGUILayout.Space();
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        CheckVSMManager();
        EditorGUILayout.Space();

        CheckShaders();
        EditorGUILayout.Space();

        CheckGlobalVariables();
        EditorGUILayout.Space();

        CheckMaterials();

        EditorGUILayout.EndScrollView();
    }

    void RunDiagnostics()
    {
        Debug.Log("=== VSM系统诊断开始 ===");

        var manager = FindObjectOfType<VSM.VirtualShadowMapManager>();
        if (manager == null)
        {
            Debug.LogError("❌ 未找到VirtualShadowMapManager！");
            return;
        }

        Debug.Log("✅ 找到VirtualShadowMapManager");

        // 检查Compute Shaders
        var fields = typeof(VSM.VirtualShadowMapManager).GetFields(
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance
        );

        foreach (var field in fields)
        {
            if (field.FieldType == typeof(ComputeShader))
            {
                var shader = field.GetValue(manager) as ComputeShader;
                if (shader == null)
                    Debug.LogWarning($"⚠️ {field.Name} 未分配");
                else
                    Debug.Log($"✅ {field.Name}: {shader.name}");
            }
        }

        // 检查全局变量
        CheckGlobalVariable("_VSM_VirtualPageTable");
        CheckGlobalVariable("_VSM_PhysicalMemory");
        CheckGlobalVariable("_VSM_FirstCascadeSize");

        Debug.Log("=== VSM系统诊断完成 ===");
        Repaint();
    }

    void CheckGlobalVariable(string name)
    {
        // 尝试获取全局变量（运行时才有效）
        if (Application.isPlaying)
        {
            if (Shader.GetGlobalTexture(name) != null)
                Debug.Log($"✅ 全局变量 {name} 已设置");
            else if (Shader.GetGlobalFloat(name) != 0)
                Debug.Log($"✅ 全局变量 {name} 已设置");
            else
                Debug.LogWarning($"⚠️ 全局变量 {name} 可能未设置");
        }
    }

    void CheckVSMManager()
    {
        GUILayout.Label("1. VirtualShadowMapManager检查", EditorStyles.boldLabel);

        var manager = FindObjectOfType<VSM.VirtualShadowMapManager>();

        if (manager == null)
        {
            EditorGUILayout.HelpBox("❌ 未找到VirtualShadowMapManager组件！", MessageType.Error);
            if (GUILayout.Button("创建VSM Manager"))
            {
                var go = new GameObject("VSM Manager");
                go.AddComponent<Camera>();
                go.AddComponent<VSM.VirtualShadowMapManager>();
                Selection.activeGameObject = go;
            }
            return;
        }

        EditorGUILayout.HelpBox("✅ 找到VirtualShadowMapManager", MessageType.Info);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("状态:");

        if (Application.isPlaying)
            EditorGUILayout.LabelField("运行中 ✅", EditorStyles.boldLabel);
        else
            EditorGUILayout.LabelField("未运行（需要Play模式）", EditorStyles.miniLabel);

        if (GUILayout.Button("选中VSM Manager"))
        {
            Selection.activeGameObject = manager.gameObject;
        }

        EditorGUILayout.EndVertical();
    }

    void CheckShaders()
    {
        GUILayout.Label("2. Shader检查", EditorStyles.boldLabel);

        string[] shaderNames = new string[]
        {
            "VSM/ForwardLit",
            "VSM/ForwardLitDebug",
            "VSM/Test",
            "VSM/DepthRender"
        };

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        foreach (var name in shaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader != null)
                EditorGUILayout.LabelField($"✅ {name}");
            else
                EditorGUILayout.LabelField($"❌ {name} - 未找到", EditorStyles.boldLabel);
        }

        EditorGUILayout.EndVertical();
    }

    void CheckGlobalVariables()
    {
        GUILayout.Label("3. 全局变量检查", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("需要在Play模式下检查全局变量", MessageType.Warning);
        }
        else
        {
            string[] globalVars = new string[]
            {
                "_VSM_VirtualPageTable",
                "_VSM_PhysicalMemory",
                "_VSM_FirstCascadeSize",
                "_VSM_CameraPosition"
            };

            foreach (var varName in globalVars)
            {
                var tex = Shader.GetGlobalTexture(varName);
                var val = Shader.GetGlobalFloat(varName);

                if (tex != null)
                    EditorGUILayout.LabelField($"✅ {varName} (Texture)");
                else if (val != 0)
                    EditorGUILayout.LabelField($"✅ {varName} = {val}");
                else
                    EditorGUILayout.LabelField($"⚠️ {varName} - 未设置?");
            }
        }

        EditorGUILayout.EndVertical();
    }

    void CheckMaterials()
    {
        GUILayout.Label("4. 场景材质检查", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        var renderers = FindObjectsOfType<MeshRenderer>();
        int vsmCount = 0;
        int otherCount = 0;

        foreach (var renderer in renderers)
        {
            if (renderer.sharedMaterial != null &&
                renderer.sharedMaterial.shader != null)
            {
                string shaderName = renderer.sharedMaterial.shader.name;
                if (shaderName.StartsWith("VSM/"))
                    vsmCount++;
                else
                    otherCount++;
            }
        }

        EditorGUILayout.LabelField($"使用VSM材质的物体: {vsmCount}");
        EditorGUILayout.LabelField($"使用其他材质的物体: {otherCount}");

        if (vsmCount == 0)
        {
            EditorGUILayout.HelpBox("⚠️ 没有物体使用VSM材质！", MessageType.Warning);
            if (GUILayout.Button("打开VSM Setup Helper"))
            {
                EditorWindow.GetWindow<VSMSetupHelper>("VSM Setup");
            }
        }

        EditorGUILayout.EndVertical();
    }
}
