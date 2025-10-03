using UnityEngine;
using UnityEditor;

public class QuickFixVSM
{
    [MenuItem("VSM/Quick Fix - Auto Configure")]
    public static void QuickFix()
    {
        var manager = Object.FindObjectOfType<VSM.VirtualShadowMapManager>();

        if (manager == null)
        {
            EditorUtility.DisplayDialog("错误", "找不到VirtualShadowMapManager", "确定");
            return;
        }

        Undo.RecordObject(manager, "Quick Fix VSM");

        // 通过反射设置私有字段
        var type = typeof(VSM.VirtualShadowMapManager);

        // 增大First Cascade Size
        var firstCascadeField = type.GetField("firstCascadeSize",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (firstCascadeField != null)
        {
            float currentValue = (float)firstCascadeField.GetValue(manager);
            float newValue = Mathf.Max(currentValue, 20.0f); // 设置为至少20
            firstCascadeField.SetValue(manager, newValue);
            Debug.Log($"First Cascade Size: {currentValue} → {newValue}");
        }

        // 设置Filter Margin
        var filterMarginField = type.GetField("filterMargin",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (filterMarginField != null)
        {
            filterMarginField.SetValue(manager, 2);
            Debug.Log("Filter Margin: → 2");
        }

        EditorUtility.SetDirty(manager);

        EditorUtility.DisplayDialog(
            "配置已更新",
            "已自动调整VSM设置：\n\n" +
            "• First Cascade Size → 20 (增大光源覆盖范围)\n" +
            "• Filter Margin → 2\n\n" +
            "请重新运行Play模式测试",
            "确定"
        );

        Debug.Log("✅ VSM Quick Fix 完成");
    }
}
