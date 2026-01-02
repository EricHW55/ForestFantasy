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

    [Header("Goblin Group Prefab (optional, if set should have GoblinGroup component)")]
    public GameObject goblinGroupPrefab;

    // =========================
    // Root / Hierarchy
    // =========================
    [Header("Spawned Parent (Top Root = Mobs_Root)")]
    [Tooltip("비워두면 spawnedRootName으로 찾거나 생성합니다. 여기엔 보통 Mobs_Root를 넣으세요.")]
    [SerializeField] private Transform spawnedRoot;

    [Tooltip("spawnedRoot가 비어있을 때 찾을 Top Root 이름 (권장: Mobs_Root)")]
    public string spawnedRootName = "Mobs_Root";

    [Tooltip("Top Root가 새로 생성될 때, 이 오브젝트 밑으로 둘지 여부")]
    public bool parentRootToThisObject = true;

    [Tooltip("이제는 Mobs_Root 전체가 아니라, Mobs_Root/Goblin 아래만 지웁니다.")]
    public bool clearPrevious = true;

    [Header("Goblin SubRoot (Mobs_Root/Goblin)")]
    public string goblinRootName = "Goblin";

    private Transform _goblinRoot; // Mobs_Root/Goblin

    // =========================
    // Area
    // =========================
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

        // 1) Top Root(Mobs_Root) 확보
        EnsureSpawnedRoot();

        // 2) Sub Root(Mobs_Root/Goblin) 확보
        EnsureGoblinRoot();

        // 3) Clear는 Goblin 아래만!
        if (clearPrevious)
        {
            ClearChildren(_goblinRoot);
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

            int minG = Mathf.Max(0, rule.minGroups);
            int maxG = Mathf.Max(minG, rule.maxGroups);
            int numGroups = UnityEngine.Random.Range(minG, maxG + 1);

            int groupsSpawned = 0;

            for (int g = 0; g < numGroups; g++)
            {
                // 1) 그룹 중심점 찾기
                if (!TryFindGroupCenter(terrain, minXZ, maxXZ, rule, out Vector3 groupCenter))
                {
                    // NavMesh가 없거나(또는 반경 너무 작거나) 경사/거리 조건이 빡세면 여기서 계속 실패함
                    Debug.LogWarning($"[GoblinGroupSpawner] '{rule.groupName}' 그룹 {g + 1}: 중심점 찾기 실패 (NavMesh/경사/거리 조건 확인)");
                    continue;
                }

                _groupCenters.Add(groupCenter);

                // 2) 그룹 오브젝트 생성 (부모: Mobs_Root/Goblin)
                GameObject groupObj = CreateGroupObject(rule.groupName, groupCenter, g);

                // 3) GoblinGroup 컴포넌트 보장
                GoblinGroup groupComp = groupObj.GetComponent<GoblinGroup>();
                if (groupComp == null) groupComp = groupObj.AddComponent<GoblinGroup>();

                // 4) 해당 그룹에 고블린들 스폰
                int minC = Mathf.Max(0, rule.minGoblinsPerGroup);
                int maxC = Mathf.Max(minC, rule.maxGoblinsPerGroup);
                int goblinsInGroup = UnityEngine.Random.Range(minC, maxC + 1);

                int goblinsSpawned = SpawnGoblinsInGroup(terrain, groupCenter, groupComp, rule, goblinsInGroup);

                if (goblinsSpawned > 0)
                {
                    groupsSpawned++;
                    totalGoblins += goblinsSpawned;
                    Debug.Log($"[GoblinGroupSpawner] '{rule.groupName}' 그룹 #{g + 1}: {goblinsSpawned}/{goblinsInGroup} 고블린 스폰");
                }
                else
                {
                    // 고블린이 하나도 스폰 안됐으면 그룹 삭제 + 중심점도 제거
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
            groupObj = Instantiate(goblinGroupPrefab, position, Quaternion.identity, _goblinRoot);
            groupObj.name = $"{ruleName}_{index + 1}";
        }
        else
        {
            groupObj = new GameObject($"{ruleName}_{index + 1}");
            groupObj.transform.position = position;
            groupObj.transform.SetParent(_goblinRoot, worldPositionStays: true);
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
            if (!TryFindGoblinPosition(t, groupCenter, rule, goblinPositions, out Vector3 pos, out Quaternion rot))
            {
                continue;
            }

            GameObject prefab = PickWeightedPrefab(rule.colorVariants);
            if (prefab == null) continue;

            GameObject goblinObj = Instantiate(prefab, pos, rot, groupComp.transform);

            var agent = goblinObj.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(pos);

            var goblinAI = goblinObj.GetComponent<GoblinAI>();
            if (goblinAI != null) goblinAI.group = groupComp;

            goblinPositions.Add(pos);
            spawned++;
        }

        return spawned;
    }

    private bool TryFindGoblinPosition(
        Terrain t,
        Vector3 groupCenter,
        GoblinGroupRule rule,
        List<Vector3> existingPositions,
        out Vector3 pos,
        out Quaternion rot)
    {
        for (int attempt = 0; attempt < rule.maxTriesPerGoblin; attempt++)
        {
            Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * rule.groupRadius;
            float x = groupCenter.x + randomOffset.x;
            float z = groupCenter.z + randomOffset.y;

            if (!IsSlopeOk(t, x, z, rule.maxSlopeAngle))
                continue;

            float y = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y + rule.yOffset;
            Vector3 p = new Vector3(x, y, z);

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

        // 1) 내 자식에서 찾기
        var child = transform.Find(spawnedRootName);
        if (child != null)
        {
            spawnedRoot = child;
            return;
        }

        // 2) 씬에서 찾기
        var rootGO = GameObject.Find(spawnedRootName);
        if (rootGO == null)
        {
            rootGO = new GameObject(spawnedRootName);
            if (parentRootToThisObject)
                rootGO.transform.SetParent(transform, worldPositionStays: true);
        }

        spawnedRoot = rootGO.transform;
    }

    private void EnsureGoblinRoot()
    {
        if (spawnedRoot == null) EnsureSpawnedRoot();
        if (spawnedRoot == null) return;

        if (_goblinRoot != null && _goblinRoot.parent == spawnedRoot) return;

        var child = spawnedRoot.Find(goblinRootName);
        if (child != null)
        {
            _goblinRoot = child;
            return;
        }

        var go = new GameObject(goblinRootName);
        go.transform.SetParent(spawnedRoot, worldPositionStays: true);
        _goblinRoot = go.transform;
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
