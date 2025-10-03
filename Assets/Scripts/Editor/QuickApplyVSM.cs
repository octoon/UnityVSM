using UnityEngine;
using UnityEditor;

/// <summary>
/// 快速应用VSM材质到场景
/// </summary>
public class QuickApplyVSM
{
    [MenuItem("VSM/Quick Apply VSM Material to All Objects")]
    public static void ApplyVSMToAll()
    {
        // 1. 查找或创建VSM材质
        Material vsmMat = FindVSMMaterial();

        if (vsmMat == null)
        {
            vsmMat = CreateVSMMaterial();
        }

        if (vsmMat == null)
        {
            EditorUtility.DisplayDialog("错误", "无法创建VSM材质", "确定");
            return;
        }

        // 2. 应用到所有MeshRenderer
        MeshRenderer[] renderers = Object.FindObjectsOfType<MeshRenderer>();
        int count = 0;

        foreach (var renderer in renderers)
        {
            Undo.RecordObject(renderer, "Apply VSM Material");
            renderer.sharedMaterial = vsmMat;
            count++;
        }

        EditorUtility.DisplayDialog(
            "完成",
            $"VSM材质已应用到 {count} 个物体\n材质: {vsmMat.name}",
            "确定"
        );

        Debug.Log($"✅ VSM材质已应用到 {count} 个物体");
    }

    static Material FindVSMMaterial()
    {
        // 查找现有的VSM材质
        string[] guids = AssetDatabase.FindAssets("t:Material");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null && mat.shader != null && mat.shader.name == "VSM/ForwardLit")
            {
                Debug.Log($"找到现有VSM材质: {path}");
                return mat;
            }
        }

        return null;
    }

    static Material CreateVSMMaterial()
    {
        Shader vsmShader = Shader.Find("VSM/ForwardLit");

        if (vsmShader == null)
        {
            Debug.LogError("找不到 VSM/ForwardLit shader！");
            return null;
        }

        // 创建材质
        Material mat = new Material(vsmShader);
        mat.name = "VSM_ForwardLit_Material";

        // 保存
        string folder = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/VSM_ForwardLit.mat");
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();

        Debug.Log($"✅ 创建VSM材质: {path}");
        return mat;
    }
}
