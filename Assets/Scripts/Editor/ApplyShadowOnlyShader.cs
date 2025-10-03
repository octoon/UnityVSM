using UnityEngine;
using UnityEditor;

public class ApplyShadowOnlyShader
{
    [MenuItem("VSM/Apply Shadow Only Material (Direct Output)")]
    public static void ApplyShadowOnly()
    {
        // 查找或创建材质
        Material mat = FindOrCreateMaterial("VSM/ShadowOnly", "VSM_ShadowOnly");

        if (mat == null)
        {
            EditorUtility.DisplayDialog("错误", "无法创建VSM/ShadowOnly材质\n请确保shader已编译", "确定");
            return;
        }

        // 应用到所有物体
        MeshRenderer[] renderers = Object.FindObjectsOfType<MeshRenderer>();
        int count = 0;

        foreach (var renderer in renderers)
        {
            Undo.RecordObject(renderer, "Apply Shadow Only Material");
            renderer.sharedMaterial = mat;
            count++;
        }

        EditorUtility.DisplayDialog(
            "完成",
            $"VSM Shadow Only材质已应用到 {count} 个物体\n\n" +
            "显示说明：\n" +
            "⚫ 黑色 = 阴影区域\n" +
            "⚪ 白色 = 光照区域\n" +
            "🔴 红色 = VSM页面未分配\n" +
            "🟣 紫色 = 超出光源范围\n" +
            "🔵 青色 = 坐标错误",
            "确定"
        );

        Debug.Log($"✅ VSM Shadow Only材质已应用到 {count} 个物体");
        Debug.Log("黑色 = 阴影，白色 = 光照，红色 = 未分配");
    }

    static Material FindOrCreateMaterial(string shaderName, string matName)
    {
        // 先查找现有材质
        string[] guids = AssetDatabase.FindAssets("t:Material " + matName);
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.shader != null && mat.shader.name == shaderName)
            {
                Debug.Log($"使用现有材质: {path}");
                return mat;
            }
        }

        // 创建新材质
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"找不到Shader: {shaderName}");
            return null;
        }

        Material newMat = new Material(shader);
        newMat.name = matName;

        // 保存
        string folder = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{matName}.mat");
        AssetDatabase.CreateAsset(newMat, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"✅ 创建新材质: {assetPath}");
        return newMat;
    }
}
