using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

[DisallowMultipleComponent]
public class NavMeshBakeModule : MonoBehaviour, ITerrainStep
{
    // Tree/Detail 이후, MobSpawner(900) 이전에 실행
    public int Order => 850;

    [Header("NavMesh Surface")]
    [SerializeField] private NavMeshSurface surface;

    [Header("Optional overrides")]
    public bool overrideSettings = true;
    public CollectObjects collectObjects = CollectObjects.All; // 맵이 전부 MapGenerator 아래면 Children도 OK
    public NavMeshCollectGeometry useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    public LayerMask layerMask = ~0;
    public int defaultArea = 0;

    [Header("Build")]
    public bool clearBeforeBuild = true;
    public bool logBuildInfo = true;

    public void Apply(Terrain terrain, int seed)
    {
        if (!EnsureSurface())
        {
            Debug.LogError("[NavMeshBakeModule] NavMeshSurface를 찾거나 생성하지 못했습니다.");
            return;
        }

        if (overrideSettings)
        {
            surface.collectObjects = collectObjects;
            surface.useGeometry = useGeometry;
            surface.layerMask = layerMask;
            surface.defaultArea = defaultArea;
        }

        if (clearBeforeBuild)
            surface.RemoveData();

        surface.BuildNavMesh();

        if (logBuildInfo)
        {
            var tri = NavMesh.CalculateTriangulation();
            Debug.Log($"[NavMeshBakeModule] Build done. verts={tri.vertices.Length}, tris={tri.indices.Length / 3}");
        }
    }

    private bool EnsureSurface()
    {
        if (surface != null) return true;

        // 1) 같은 오브젝트에서 찾기
        surface = GetComponent<NavMeshSurface>();
        if (surface != null) return true;

        // 2) 자식에서 찾기 (MapGenerator 밑에 Navigation 오브젝트 두는 방식 지원)
        surface = GetComponentInChildren<NavMeshSurface>();
        if (surface != null) return true;

        // 3) 없으면 자동 생성
        var navGO = new GameObject("Navigation");
        navGO.transform.SetParent(transform, false);
        surface = navGO.AddComponent<NavMeshSurface>();
        return true;
    }
}
