using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class MobSpawnerModule : MonoBehaviour, ITerrainStep
{
    // TerrainHeightModule(Order=100) 이후 + 디테일/프랍 이후에 스폰하고 싶으면 충분히 크게
    public int Order => 900;

    [Header("Prefabs (Imp Blue/Brown/Red 등)")]
    public List<GameObject> mobPrefabs = new();

    [Header("Spawned parent (optional)")]
    [SerializeField] private Transform spawnedRoot;
    public string spawnedRootName = "Mobs_Root";
    public bool parentRootToThisObject = true;
    public bool clearPrevious = true;

    [Header("Terrain / Area")]
    public bool useWholeTerrain = true;
    public Vector2 areaMinXZ = new Vector2(0, 0);
    public Vector2 areaMaxXZ = new Vector2(100, 100);

    [Header("Count (random)")]
    public int minCount = 5;
    public int maxCount = 15;

    [Header("Placement rules")]
    public float maxSlopeAngle = 35f;
    public float minDistanceBetweenMobs = 3f;
    public int maxTriesPerMob = 30;
    public float yOffset = 0.02f;

    [Header("NavMesh (optional)")]
    public bool requireNavMesh = false;
    public float navMeshSearchRadius = 2.0f;

    [Header("Determinism")]
    public int seedOffset = 1337;

    private readonly List<Vector3> _spawnedPositions = new();

    // ✅ ITerrainStep 인터페이스 시그니처 그대로!
    public void Apply(Terrain terrain, int seed)
    {
        if (terrain == null) { Debug.LogError("[MobSpawnerModule] terrain null"); return; }
        if (mobPrefabs == null || mobPrefabs.Count == 0) { Debug.LogWarning("[MobSpawnerModule] mobPrefabs 비어있음"); return; }

        terrain.Flush(); // 높이/콜라이더 갱신 안전빵

        EnsureSpawnedRoot();

        if (clearPrevious)
            ClearChildren(spawnedRoot);

        // 다른 모듈 랜덤에 영향 덜 주려고 state 보관/복구
        var prevState = Random.state;
        Random.InitState(seed ^ seedOffset);

        _spawnedPositions.Clear();

        GetSpawnBoundsXZ(terrain, out Vector2 minXZ, out Vector2 maxXZ);
        int targetCount = Random.Range(minCount, maxCount + 1);

        int spawned = 0;
        int safetyTries = targetCount * Mathf.Max(1, maxTriesPerMob);

        for (int i = 0; i < safetyTries && spawned < targetCount; i++)
        {
            if (!TryFindSpawnPoint(terrain, minXZ, maxXZ, out Vector3 pos, out Quaternion rot))
                continue;

            var prefab = mobPrefabs[Random.Range(0, mobPrefabs.Count)];
            var go = Instantiate(prefab, pos, rot, spawnedRoot);

            // NavMeshAgent 있으면 초기 위치 확정(있어도/없어도 문제 없음)
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(pos);

            _spawnedPositions.Add(pos);
            spawned++;
        }

        Random.state = prevState;

        Debug.Log($"[MobSpawnerModule] spawned {spawned}/{targetCount}");
    }

    private void EnsureSpawnedRoot()
    {
        if (spawnedRoot != null) return;

        // 1) 같은 오브젝트 밑에서 먼저 찾기(관리 편함)
        var child = transform.Find(spawnedRootName);
        if (child != null)
        {
            spawnedRoot = child;
            return;
        }

        // 2) 씬 전체에서 찾기(이미 만들어둔 경우)
        var rootGO = GameObject.Find(spawnedRootName);
        if (rootGO == null)
        {
            rootGO = new GameObject(spawnedRootName);
            if (parentRootToThisObject)
                rootGO.transform.SetParent(transform, worldPositionStays: true);
        }

        spawnedRoot = rootGO.transform;
    }

    private void ClearChildren(Transform root)
    {
        if (root == null) return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
    }

    private void GetSpawnBoundsXZ(Terrain t, out Vector2 minXZ, out Vector2 maxXZ)
    {
        if (!useWholeTerrain)
        {
            minXZ = areaMinXZ;
            maxXZ = areaMaxXZ;
            return;
        }

        Vector3 tp = t.transform.position;
        Vector3 size = t.terrainData.size;
        minXZ = new Vector2(tp.x, tp.z);
        maxXZ = new Vector2(tp.x + size.x, tp.z + size.z);
    }

    private bool TryFindSpawnPoint(Terrain t, Vector2 minXZ, Vector2 maxXZ, out Vector3 pos, out Quaternion rot)
    {
        for (int attempt = 0; attempt < maxTriesPerMob; attempt++)
        {
            float x = Random.Range(minXZ.x, maxXZ.x);
            float z = Random.Range(minXZ.y, maxXZ.y);

            // 경사 체크는 TerrainData 노멀로
            if (!IsSlopeOk(t, x, z, maxSlopeAngle))
                continue;

            // Terrain 표면 높이로 y 세팅
            float y = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y + yOffset;
            Vector3 p = new Vector3(x, y, z);

            if (!IsFarEnough(p, minDistanceBetweenMobs))
                continue;

            rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // NavMesh 강제 옵션이면 NavMesh 위로 스냅
            if (requireNavMesh)
            {
                if (NavMesh.SamplePosition(p, out var hit, navMeshSearchRadius, NavMesh.AllAreas))
                {
                    pos = hit.position;
                    return true;
                }
                continue;
            }

            pos = p;
            return true;
        }

        pos = default;
        rot = Quaternion.identity;
        return false;
    }

    private bool IsSlopeOk(Terrain t, float worldX, float worldZ, float maxAngle)
    {
        Vector3 tp = t.transform.position;
        Vector3 size = t.terrainData.size;

        float u = Mathf.InverseLerp(tp.x, tp.x + size.x, worldX);
        float v = Mathf.InverseLerp(tp.z, tp.z + size.z, worldZ);

        Vector3 n = t.terrainData.GetInterpolatedNormal(u, v);
        float angle = Vector3.Angle(n, Vector3.up);
        return angle <= maxAngle;
    }

    private bool IsFarEnough(Vector3 p, float minDist)
    {
        float minDistSqr = minDist * minDist;
        for (int i = 0; i < _spawnedPositions.Count; i++)
        {
            if ((p - _spawnedPositions[i]).sqrMagnitude < minDistSqr)
                return false;
        }
        return true;
    }
}
