using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[DisallowMultipleComponent]
public class NavMeshBakeModule : MonoBehaviour, ITerrainStep
{
    public int Order => 850;

    [SerializeField] private NavMeshSurface surface;

    [Header("Layer Settings")]
    public string groundLayerName = "Ground";
    public string obstacleLayerName = "Obstacle";
    public bool includeObstacleInBake = true;
    
    [Header("Obstacle Optimization")]
    [Tooltip("큰 장애물만 베이크 (예: 바위, 큰 나무)")]
    public bool useLargeObstaclesOnly = true;
    public float minObstacleSize = 1.5f; // 이것보다 작은 콜라이더는 무시

    [Header("NavMesh Settings")]
    public bool overrideSettings = true;
    public CollectObjects collectObjects = CollectObjects.All;
    public NavMeshCollectGeometry useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    public int defaultArea = 0;

    [Header("Build Options")]
    public bool clearBeforeBuild = true;
    public bool logBuildInfo = true;

    [Header("Layer Assignment")]
    public bool autoAssignLayers = true;
    public string terrainObjectName = "Terrain";
    public string mapRootName = "Map";

    public void Apply(Terrain terrain, int seed)
    {
        if (!EnsureSurface())
        {
            Debug.LogError("[NavMeshBakeModule] NavMeshSurface를 생성할 수 없습니다");
            return;
        }

        // 1. 레이어 ID 확인
        int groundLayer = LayerMask.NameToLayer(groundLayerName);
        int obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);

        if (groundLayer < 0)
        {
            Debug.LogError($"[NavMeshBakeModule] '{groundLayerName}' 레이어가 존재하지 않습니다. Project Settings > Tags and Layers에서 생성해주세요.");
            return;
        }

        if (includeObstacleInBake && obstacleLayer < 0)
        {
            Debug.LogWarning($"[NavMeshBakeModule] '{obstacleLayerName}' 레이어가 존재하지 않습니다.");
        }

        // 2. 자동으로 오브젝트에 레이어 할당
        if (autoAssignLayers)
        {
            AssignLayersToObjects(terrain, groundLayer, obstacleLayer);
        }

        // 3. NavMeshSurface 설정
        if (overrideSettings)
        {
            surface.collectObjects = collectObjects;
            surface.useGeometry = useGeometry;
            surface.defaultArea = defaultArea;
            
            // Ground + Obstacle 레이어만 베이크
            surface.layerMask = BuildLayerMask(groundLayer, obstacleLayer);
            
            Debug.Log($"[NavMeshBakeModule] LayerMask 설정: {LayerMaskToString(surface.layerMask)}");
        }

        // 4. NavMesh 빌드
        if (clearBeforeBuild)
        {
            surface.RemoveData();
        }

        Debug.Log("[NavMeshBakeModule] NavMesh 빌드 시작...");
        surface.BuildNavMesh();

        // 5. 결과 확인
        var tri = NavMesh.CalculateTriangulation();
        
        if (logBuildInfo)
        {
            Debug.Log($"[NavMeshBakeModule] 빌드 완료! " +
                     $"정점: {tri.vertices.Length}, " +
                     $"삼각형: {tri.indices.Length / 3}, " +
                     $"레이어 마스크: {surface.layerMask.value}");
        }

        // 6. 빌드 실패 체크
        if (tri.vertices.Length == 0)
        {
            Debug.LogError("[NavMeshBakeModule] NavMesh가 생성되지 않았습니다! 다음을 확인해주세요:\n" +
                          $"1. '{groundLayerName}' 레이어가 올바른 오브젝트에 할당되었는지\n" +
                          "2. 해당 오브젝트에 Collider가 있는지\n" +
                          "3. NavMeshSurface의 Collect Objects 설정");
            
            LogSceneLayerInfo(groundLayer, obstacleLayer);
        }
    }

    private void AssignLayersToObjects(Terrain terrain, int groundLayer, int obstacleLayer)
    {
        int assigned = 0;

        // Terrain 오브젝트에 Ground 레이어 할당
        if (terrain != null)
        {
            terrain.gameObject.layer = groundLayer; // 재귀 제거 - Terrain만
            assigned++;
            Debug.Log($"[NavMeshBakeModule] Terrain에 '{groundLayerName}' 레이어 할당");
        }

        // 씬의 모든 Terrain 찾아서 할당
        foreach (var t in Object.FindObjectsOfType<Terrain>())
        {
            if (t != terrain)
            {
                t.gameObject.layer = groundLayer; // 재귀 제거
                assigned++;
                Debug.Log($"[NavMeshBakeModule] {t.name}에 '{groundLayerName}' 레이어 할당");
            }
        }

        // Map 루트 오브젝트 - 자식은 건드리지 않음
        var mapGo = GameObject.Find(mapRootName);
        if (mapGo != null)
        {
            mapGo.layer = groundLayer; // 재귀 제거 - Map 자체만
            assigned++;
            Debug.Log($"[NavMeshBakeModule] {mapRootName}에 '{groundLayerName}' 레이어 할당");
        }
        
        // 큰 장애물만 Obstacle 레이어에 할당
        if (includeObstacleInBake && useLargeObstaclesOnly && obstacleLayer >= 0)
        {
            AssignLargeObstacles(obstacleLayer);
        }

        if (assigned == 0)
        {
            Debug.LogWarning("[NavMeshBakeModule] Ground 레이어를 할당할 오브젝트를 찾지 못했습니다!");
        }
    }
    
    private void AssignLargeObstacles(int obstacleLayer)
    {
        int count = 0;
        var allColliders = Object.FindObjectsOfType<Collider>();
        
        foreach (var col in allColliders)
        {
            if (col.isTrigger) continue; // 트리거는 제외
            
            // 크기 체크
            Bounds bounds = col.bounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            
            if (size >= minObstacleSize)
            {
                col.gameObject.layer = obstacleLayer;
                count++;
            }
        }
        
        Debug.Log($"[NavMeshBakeModule] {count}개의 큰 장애물을 '{obstacleLayerName}' 레이어에 할당");
    }

    private LayerMask BuildLayerMask(int groundLayer, int obstacleLayer)
    {
        int mask = 1 << groundLayer;

        if (includeObstacleInBake && obstacleLayer >= 0)
        {
            mask |= 1 << obstacleLayer;
        }

        return mask;
    }

    private static void SetLayerRecursively(Transform tr, int layer)
    {
        tr.gameObject.layer = layer;
        
        for (int i = 0; i < tr.childCount; i++)
        {
            SetLayerRecursively(tr.GetChild(i), layer);
        }
    }

    private bool EnsureSurface()
    {
        if (surface != null) return true;
        
        surface = GetComponent<NavMeshSurface>();
        if (surface != null) return true;
        
        surface = GetComponentInChildren<NavMeshSurface>();
        if (surface != null) return true;

        // NavMeshSurface가 없으면 자동 생성
        Debug.Log("[NavMeshBakeModule] NavMeshSurface 컴포넌트를 자동 생성합니다.");
        
        var navGO = new GameObject("Navigation");
        navGO.transform.SetParent(transform, false);
        surface = navGO.AddComponent<NavMeshSurface>();
        
        return true;
    }

    private string LayerMaskToString(LayerMask mask)
    {
        string result = "";
        int count = 0;

        for (int i = 0; i < 32; i++)
        {
            if ((mask.value & (1 << i)) != 0)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    if (count > 0) result += ", ";
                    result += $"{layerName}({i})";
                    count++;
                }
            }
        }

        return string.IsNullOrEmpty(result) ? "없음" : result;
    }

    private void LogSceneLayerInfo(int groundLayer, int obstacleLayer)
    {
        Debug.Log("=== 씬의 레이어 정보 ===");

        int groundCount = 0;
        int obstacleCount = 0;
        int colliderCount = 0;

        var allObjects = Object.FindObjectsOfType<GameObject>();
        
        foreach (var obj in allObjects)
        {
            if (obj.layer == groundLayer)
            {
                groundCount++;
                if (obj.GetComponent<Collider>() != null)
                {
                    colliderCount++;
                }
            }
            else if (obj.layer == obstacleLayer)
            {
                obstacleCount++;
            }
        }

        Debug.Log($"'{groundLayerName}' 레이어 오브젝트: {groundCount}개");
        Debug.Log($"'{groundLayerName}' 레이어 + Collider 오브젝트: {colliderCount}개");
        Debug.Log($"'{obstacleLayerName}' 레이어 오브젝트: {obstacleCount}개");

        if (groundCount == 0)
        {
            Debug.LogError($"씬에 '{groundLayerName}' 레이어를 가진 오브젝트가 없습니다!");
        }

        if (colliderCount == 0)
        {
            Debug.LogError($"'{groundLayerName}' 레이어를 가진 오브젝트 중 Collider가 있는 것이 없습니다!");
        }
    }

    // 디버그용: 인스펙터에서 수동으로 레이어 정보 확인
    [ContextMenu("씬 레이어 정보 출력")]
    private void DebugLogSceneLayers()
    {
        int groundLayer = LayerMask.NameToLayer(groundLayerName);
        int obstacleLayer = LayerMask.NameToLayer(obstacleLayerName);
        
        LogSceneLayerInfo(groundLayer, obstacleLayer);
    }

    // 디버그용: 수동으로 NavMesh 빌드
    [ContextMenu("NavMesh 수동 빌드")]
    private void ManualBuild()
    {
        Apply(null, 0);
    }
}