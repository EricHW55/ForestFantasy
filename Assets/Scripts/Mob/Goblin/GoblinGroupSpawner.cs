using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class GoblinGroupSpawner : MonoBehaviour, ITerrainStep
{
    public int Order => 905; // MobSpawner 다음에 실행

    [Serializable]
    public class GoblinColorVariant
    {
        public GameObject prefab; // 색깔별 고블린 프리팹
        [Min(0f)] public float weight = 1f; // 가중치
    }

    [Serializable]
    public class GoblinGroupRule
    {
        public string groupName = "GoblinGroup";
        
        [Header("Group Spawn Chance")]
        public bool useGroupChance = false;
        [Range(0f, 1f)] public float groupChance = 1f;
        
        [Header("Number of Groups")]
        public int minGroups = 2;
        public int maxGroups = 5;
        
        [Header("Goblins per Group")]
        public int minGoblinsPerGroup = 3;
        public int maxGoblinsPerGroup = 8;
        
        [Header("Group Composition")]
        [Tooltip("이 그룹의 고블린 색깔 구성 (가중치로 비율 조절)")]
        public List<GoblinColorVariant> colorVariants = new();
        
        [Header("Group Spread")]
        [Tooltip("그룹 내 고블린들이 퍼질 반경")]
        public float groupRadius = 8f;
        public float minDistanceBetweenGoblins = 2f;
        
        [Header("Group Spacing")]
        [Tooltip("그룹 간 최소 거리")]
        public float minDistanceBetweenGroups = 20f;
        
        [Header("Placement Rules")]
        public float maxSlopeAngle = 35f;
        public int maxTriesPerGroup = 30;
        public int maxTriesPerGoblin = 20;
        public float yOffset = 0.02f;
        
        [Header("NavMesh")]
        public bool requireNavMesh = true;
        public float navMeshSearchRadius = 2.0f;
    }

    [Header("Goblin Group Rules")]
    public List<GoblinGroupRule> groupRules = new();
    
    [Header("Goblin Group Prefab (must have GoblinGroup component)")]
    public GameObject goblinGroupPrefab;
    
    [Header("Spawned Parent")]
    [SerializeField] private Transform spawnedRoot;
    public string spawnedRootName = "GoblinGroups_Root";
    public bool parentRootToThisObject = true;
    public bool clearPrevious = true;
    
    [Header("Terrain / Area")]
    public bool useWholeTerrain = true;
    public Vector2 areaMinXZ = new Vector2(0, 0);
    public Vector2 areaMaxXZ = new Vector2(100, 100);
    
    [Header("Determinism")]
    public int seedOffset = 7777;

    private readonly List<Vector3> _groupCenters = new();

    public void Apply(Terrain terrain, int seed)
    {
        if (terrain == null)
        {
            Debug.LogError("[GoblinGroupSpawner] terrain null");
            return;
        }

        if (groupRules == null || groupRules.Count == 0)
        {
            Debug.LogWarning("[GoblinGroupSpawner] groupRules 비어있음");
            return;
        }

        terrain.Flush();
        EnsureSpawnedRoot();

        if (clearPrevious)
        {
            ClearChildren(spawnedRoot);
        }

        var prevState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(seed ^ seedOffset);

        _groupCenters.Clear();
        GetSpawnBoundsXZ(terrain, out Vector2 minXZ, out Vector2 maxXZ);

        int totalGroups = 0;
        int totalGoblins = 0;

        foreach (var rule in groupRules)
        {
            if (rule == null) continue;
            if (rule.colorVariants == null || rule.colorVariants.Count == 0) continue;

            // 그룹 스폰 확률 체크
            if (rule.useGroupChance && UnityEngine.Random.value > rule.groupChance)
                continue;

            int numGroups = UnityEngine.Random.Range(rule.minGroups, rule.maxGroups + 1);
            int groupsSpawned = 0;

            for (int g = 0; g < numGroups; g++)
            {
                // 1. 그룹 중심점 찾기
                if (!TryFindGroupCenter(terrain, minXZ, maxXZ, rule, out Vector3 groupCenter))
                {
                    Debug.LogWarning($"[GoblinGroupSpawner] 그룹 {g+1} 중심점을 찾을 수 없음");
                    continue;
                }

                _groupCenters.Add(groupCenter);

                // 2. GoblinGroup 오브젝트 생성
                GameObject groupObj = CreateGroupObject(rule.groupName, groupCenter, g);
                GoblinGroup groupComp = groupObj.GetComponent<GoblinGroup>();

                // 3. 해당 그룹에 고블린들 스폰
                int goblinsInGroup = UnityEngine.Random.Range(rule.minGoblinsPerGroup, rule.maxGoblinsPerGroup + 1);
                int goblinsSpawned = SpawnGoblinsInGroup(terrain, groupCenter, groupComp, rule, goblinsInGroup);

                if (goblinsSpawned > 0)
                {
                    groupsSpawned++;
                    totalGoblins += goblinsSpawned;
                    Debug.Log($"[GoblinGroupSpawner] '{rule.groupName}' 그룹 #{g+1}: {goblinsSpawned}/{goblinsInGroup} 고블린 스폰");
                }
                else
                {
                    // 고블린이 하나도 스폰 안됐으면 그룹 삭제
                    if (Application.isPlaying) Destroy(groupObj);
                    else DestroyImmediate(groupObj);
                    _groupCenters.RemoveAt(_groupCenters.Count - 1);
                }
            }

            totalGroups += groupsSpawned;
            Debug.Log($"[GoblinGroupSpawner] Rule '{rule.groupName}': {groupsSpawned}/{numGroups} 그룹 스폰 완료");
        }

        UnityEngine.Random.state = prevState;
        Debug.Log($"[GoblinGroupSpawner] 총 {totalGroups}개 그룹, {totalGoblins}마리 고블린 스폰 완료");
    }

    private bool TryFindGroupCenter(Terrain t, Vector2 minXZ, Vector2 maxXZ, GoblinGroupRule rule, out Vector3 center)
    {
        for (int attempt = 0; attempt < rule.maxTriesPerGroup; attempt++)
        {
            float x = UnityEngine.Random.Range(minXZ.x, maxXZ.x);
            float z = UnityEngine.Random.Range(minXZ.y, maxXZ.y);

            if (!IsSlopeOk(t, x, z, rule.maxSlopeAngle))
                continue;

            float y = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y + rule.yOffset;
            Vector3 p = new Vector3(x, y, z);

            // 다른 그룹들과의 거리 체크
            if (!IsGroupFarEnough(p, rule.minDistanceBetweenGroups))
                continue;

            // NavMesh 체크 (옵션)
            if (rule.requireNavMesh)
            {
                if (NavMesh.SamplePosition(p, out var hit, rule.navMeshSearchRadius, NavMesh.AllAreas))
                {
                    center = hit.position;
                    return true;
                }
                continue;
            }

            center = p;
            return true;
        }

        center = default;
        return false;
    }

    private GameObject CreateGroupObject(string ruleName, Vector3 position, int index)
    {
        GameObject groupObj;
        
        if (goblinGroupPrefab != null)
        {
            groupObj = Instantiate(goblinGroupPrefab, position, Quaternion.identity, spawnedRoot);
            groupObj.name = $"{ruleName}_{index + 1}";
        }
        else
        {
            groupObj = new GameObject($"{ruleName}_{index + 1}");
            groupObj.transform.position = position;
            groupObj.transform.SetParent(spawnedRoot);
            groupObj.AddComponent<GoblinGroup>();
        }

        return groupObj;
    }

    private int SpawnGoblinsInGroup(Terrain t, Vector3 groupCenter, GoblinGroup groupComp, GoblinGroupRule rule, int targetCount)
    {
        List<Vector3> goblinPositions = new List<Vector3>();
        int spawned = 0;

        for (int i = 0; i < targetCount; i++)
        {
            // 그룹 반경 내에서 위치 찾기
            if (!TryFindGoblinPosition(t, groupCenter, rule, goblinPositions, out Vector3 pos, out Quaternion rot))
            {
                Debug.LogWarning($"[GoblinGroupSpawner] 고블린 {i+1} 위치를 찾을 수 없음");
                continue;
            }

            // NavMesh 위치 검증 (매우 중요!)
            if (rule.requireNavMesh)
            {
                if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, rule.navMeshSearchRadius, NavMesh.AllAreas))
                {
                    Debug.LogWarning($"[GoblinGroupSpawner] 위치 {pos}가 NavMesh 위에 없음");
                    continue;
                }
                pos = hit.position;
            }

            // 가중치 기반으로 색깔 선택
            GameObject prefab = PickWeightedPrefab(rule.colorVariants);
            if (prefab == null)
            {
                Debug.LogWarning($"[GoblinGroupSpawner] 유효한 프리팹을 찾을 수 없음");
                continue;
            }

            // 고블린 생성
            GameObject goblinObj = Instantiate(prefab, pos, rot, groupComp.transform);
            
            // NavMeshAgent 설정 (안전하게)
            var agent = goblinObj.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                // Agent 비활성화 후 위치 설정
                agent.enabled = false;
                goblinObj.transform.position = pos;
                agent.enabled = true;
                
                // Warp로 NavMesh에 확실히 배치
                if (!agent.Warp(pos))
                {
                    Debug.LogWarning($"[GoblinGroupSpawner] NavMeshAgent Warp 실패: {pos}");
                    Destroy(goblinObj);
                    continue;
                }
            }

            // GoblinAI에 그룹 할당
            var goblinAI = goblinObj.GetComponent<GoblinAI>();
            if (goblinAI != null)
            {
                goblinAI.group = groupComp;
            }

            goblinPositions.Add(pos);
            spawned++;
        }

        return spawned;
    }

    private bool TryFindGoblinPosition(Terrain t, Vector3 groupCenter, GoblinGroupRule rule, List<Vector3> existingPositions, out Vector3 pos, out Quaternion rot)
    {
        for (int attempt = 0; attempt < rule.maxTriesPerGoblin; attempt++)
        {
            // 그룹 반경 내 랜덤 위치
            Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * rule.groupRadius;
            float x = groupCenter.x + randomOffset.x;
            float z = groupCenter.z + randomOffset.y;

            if (!IsSlopeOk(t, x, z, rule.maxSlopeAngle))
                continue;

            float y = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y + rule.yOffset;
            Vector3 p = new Vector3(x, y, z);

            // 다른 고블린들과의 거리 체크
            if (!IsGoblinFarEnough(p, existingPositions, rule.minDistanceBetweenGoblins))
                continue;

            rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            if (rule.requireNavMesh)
            {
                if (NavMesh.SamplePosition(p, out var hit, rule.navMeshSearchRadius, NavMesh.AllAreas))
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

    private GameObject PickWeightedPrefab(List<GoblinColorVariant> list)
    {
        float total = 0f;
        foreach (var variant in list)
        {
            if (variant == null || variant.prefab == null) continue;
            if (variant.weight <= 0f) continue;
            total += variant.weight;
        }

        if (total <= 0f)
        {
            // Fallback: 첫 번째 유효한 프리팹
            foreach (var variant in list)
            {
                if (variant != null && variant.prefab != null)
                    return variant.prefab;
            }
            return null;
        }

        float r = UnityEngine.Random.value * total;
        GameObject last = null;

        foreach (var variant in list)
        {
            if (variant == null || variant.prefab == null) continue;
            if (variant.weight <= 0f) continue;

            last = variant.prefab;
            r -= variant.weight;
            if (r <= 0f) return variant.prefab;
        }

        return last;
    }

    private bool IsGroupFarEnough(Vector3 p, float minDist)
    {
        float minDistSqr = minDist * minDist;
        foreach (var center in _groupCenters)
        {
            if ((p - center).sqrMagnitude < minDistSqr)
                return false;
        }
        return true;
    }

    private bool IsGoblinFarEnough(Vector3 p, List<Vector3> positions, float minDist)
    {
        float minDistSqr = minDist * minDist;
        foreach (var existingPos in positions)
        {
            if ((p - existingPos).sqrMagnitude < minDistSqr)
                return false;
        }
        return true;
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

    private void EnsureSpawnedRoot()
    {
        if (spawnedRoot != null) return;

        var child = transform.Find(spawnedRootName);
        if (child != null)
        {
            spawnedRoot = child;
            return;
        }

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
}