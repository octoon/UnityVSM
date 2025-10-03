using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// 编辑器工具: 生成复杂的VSM测试场景
/// </summary>
public class VSMSceneGenerator
{
    [MenuItem("VSM/Generate Complex Scene")]
    public static void GenerateComplexScene()
    {
        // 清除现有物体(保留相机和光源)
        GameObject[] allObjects = Object.FindObjectsOfType<GameObject>();
        foreach (GameObject obj in allObjects)
        {
            if (obj.name != "Main Camera" && obj.name != "Directional Light")
            {
                Object.DestroyImmediate(obj);
            }
        }

        // 获取或创建材质
        Material shadowMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/VSMShadowMaterial.mat");
        if (shadowMat == null)
        {
            Debug.LogError("找不到 VSMShadowMaterial.mat!");
            return;
        }

        // 1. 创建大地面 (50x50)
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5, 1, 5); // Plane 默认是 10x10,所以这里变成 50x50
        ground.GetComponent<MeshRenderer>().material = shadowMat;

        // 2. 创建建筑群 (多个高楼)
        CreateBuilding("Building_Center", new Vector3(0, 0, 0), new Vector3(3, 5, 3), shadowMat);
        CreateBuilding("Building_NE", new Vector3(10, 0, 10), new Vector3(2, 8, 2), shadowMat);
        CreateBuilding("Building_NW", new Vector3(-10, 0, 10), new Vector3(2.5f, 6, 2.5f), shadowMat);
        CreateBuilding("Building_SE", new Vector3(10, 0, -10), new Vector3(3, 4, 3), shadowMat);
        CreateBuilding("Building_SW", new Vector3(-10, 0, -10), new Vector3(2, 7, 2), shadowMat);

        // 3. 创建立方体网格 (5x5 网格)
        for (int x = -2; x <= 2; x++)
        {
            for (int z = -2; z <= 2; z++)
            {
                if (x == 0 && z == 0) continue; // 中心留空

                Vector3 pos = new Vector3(x * 4f, 0.5f, z * 4f);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cube_{x}_{z}";
                cube.transform.position = pos;
                cube.transform.localScale = Vector3.one;
                cube.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                cube.GetComponent<MeshRenderer>().material = shadowMat;
            }
        }

        // 4. 创建随机球体
        for (int i = 0; i < 15; i++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-20f, 20f),
                Random.Range(0.5f, 3f),
                Random.Range(-20f, 20f)
            );
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"Sphere_{i}";
            sphere.transform.position = pos;
            sphere.transform.localScale = Vector3.one * Random.Range(0.5f, 1.5f);
            sphere.GetComponent<MeshRenderer>().material = shadowMat;
        }

        // 5. 创建圆柱体阵列 (围绕中心的柱子)
        int columnCount = 12;
        float radius = 15f;
        for (int i = 0; i < columnCount; i++)
        {
            float angle = (i / (float)columnCount) * Mathf.PI * 2f;
            Vector3 pos = new Vector3(
                Mathf.Cos(angle) * radius,
                2f,
                Mathf.Sin(angle) * radius
            );
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = $"Column_{i}";
            cylinder.transform.position = pos;
            cylinder.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
            cylinder.GetComponent<MeshRenderer>().material = shadowMat;
        }

        // 6. 创建墙壁
        CreateWall("Wall_North", new Vector3(0, 1.5f, 22), new Vector3(40, 3, 0.5f), shadowMat);
        CreateWall("Wall_South", new Vector3(0, 1.5f, -22), new Vector3(40, 3, 0.5f), shadowMat);
        CreateWall("Wall_East", new Vector3(22, 1.5f, 0), new Vector3(0.5f, 3, 40), shadowMat);
        CreateWall("Wall_West", new Vector3(-22, 1.5f, 0), new Vector3(0.5f, 3, 40), shadowMat);

        // 7. 创建台阶
        CreateStairs("Stairs_1", new Vector3(5, 0, 5), shadowMat);
        CreateStairs("Stairs_2", new Vector3(-5, 0, -5), shadowMat);

        Debug.Log("复杂场景生成完成! 包含建筑、立方体、球体、圆柱和墙壁。");
        EditorUtility.SetDirty(ground);
    }

    static void CreateBuilding(string name, Vector3 position, Vector3 size, Material mat)
    {
        GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
        building.name = name;
        building.transform.position = position + new Vector3(0, size.y / 2, 0);
        building.transform.localScale = size;
        building.GetComponent<MeshRenderer>().material = mat;
    }

    static void CreateWall(string name, Vector3 position, Vector3 size, Material mat)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = size;
        wall.GetComponent<MeshRenderer>().material = mat;
    }

    static void CreateStairs(string name, Vector3 position, Material mat)
    {
        GameObject stairsParent = new GameObject(name);
        stairsParent.transform.position = position;

        for (int i = 0; i < 5; i++)
        {
            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = $"{name}_Step_{i}";
            step.transform.parent = stairsParent.transform;
            step.transform.localPosition = new Vector3(0, i * 0.2f, i * 0.5f);
            step.transform.localScale = new Vector3(2, 0.2f, 0.5f);
            step.GetComponent<MeshRenderer>().material = mat;
        }
    }
}
#endif
