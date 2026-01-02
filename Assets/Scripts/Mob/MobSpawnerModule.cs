using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class MobSpawnerModule : MonoBehaviour, ITerrainStep
{
    public int Order => 900;

    [Serializable]
    public class WeightedPrefab
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f; // 0이면 사실상 안 나옴
    }

    [Serializable]
    public class SpawnRule
    {
        public string ruleName = "Imp";

        [Tooltip("이 룰에서 뽑을 프리팹 + 가중치(확률 비율)")]
        public List<WeightedPrefab> prefabs = new();

        [Header("Rule Chance (optional)")]
        public bool useRuleChance = false;
        [Range(0f, 1f)] public float ruleChance = 1f; // 예: OneEye는 0.3이면 30% 확률로만 룰 실행

        [Header("Count (random)")]
        public int minCount = 3;
        public int maxCount = 8;

        [Header("Placement overrides (optional)")]
        public bool overridePlacementRules = false;
        public float maxSlopeAngle = 35f;
        public float minDistanceBetweenMobs = 3f;
        public int maxTriesPerMob = 30;
        public float yOffset = 0.02f;

        [Header("NavMesh (optional)")]
        public bool requireNavMesh = false;
        public float navMeshSearchRadius = 2.0f;

        [Header("Hierarchy")]
        public bool createSubRoot = true;
    }

    [Header("Spawn Rules (Imp / OneEye / ...)")]
    public List<SpawnRule> spawnRules = new();

    // -------- Root / Clear --------
    [Header("Spawned parent (optional)")]
    [SerializeField] private Transform spawnedRoot;
    public string spawnedRootName = "Mobs_Root";
    public bool parentRootToThisObject = true;
    public bool clearPrevious = true;

    // -------- Area --------
    [Header("Terrain / Area")]
    public bool useWholeTerrain = true;
    public Vector2 areaMinXZ = new Vector2(0, 0);
    public Vector2 areaMaxXZ = new Vector2(100, 100);

    // -------- Default placement (Rule override 안 할 때) --------
    [Header("Default Placement rules")]
    public float maxSlopeAngle = 35f;
    public float minDistanceBetweenMobs = 3f;
    public int maxTriesPerMob = 30;
    public float yOffset = 0.02f;

    [Header("Default NavMesh (optional)")]
    public bool requireNavMesh = false;
    public float navMeshSearchRadius = 2.0f;

    [Header("Determinism")]
    public int seedOffset = 1337;

    private readonly List<Vector3> _spawnedPositions = new();
    private readonly Dictionary<string, Transform> _subRoots = new();

    public void Apply(Terrain terrain, int seed)
    {
        if (terrain == null) { Debug.LogError("[MobSpawnerModule] terrain null"); return; }
        if (spawnRules == null || spawnRules.Count == 0)
        {
            Debug.LogWarning("[MobSpawnerModule] spawnRules 비어있음");
            return;
        }

        terrain.Flush();
        EnsureSpawnedRoot();

        if (clearPrevious)
        {
            ClearChildren(spawnedRoot);
            _subRoots.Clear();
        }

        var prevState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(seed ^ seedOffset);

        _spawnedPositions.Clear();
        GetSpawnBoundsXZ(terrain, out Vector2 minXZ, out Vector2 maxXZ);

        int totalSpawned = 0;

        foreach (var rule in spawnRules)
        {
            if (rule == null) continue;
            if (rule.prefabs == null || rule.prefabs.Count == 0) continue;

            // 룰 자체 실행 확률
            if (rule.useRuleChance && UnityEngine.Random.value > rule.ruleChance)
                continue;

            int minC = Mathf.Max(0, rule.minCount);
            int maxC = Mathf.Max(minC, rule.maxCount);
            int targetCount = UnityEngine.Random.Range(minC, maxC + 1);

            float slope = rule.overridePlacementRules ? rule.maxSlopeAngle : maxSlopeAngle;
            float minDist = rule.overridePlacementRules ? rule.minDistanceBetweenMobs : minDistanceBetweenMobs;
            int triesPerMob = rule.overridePlacementRules ? rule.maxTriesPerMob : maxTriesPerMob;
            float yOff = rule.overridePlacementRules ? rule.yOffset : yOffset;

            bool needNav = rule.overridePlacementRules ? rule.requireNavMesh : requireNavMesh;
            float navRadius = rule.overridePlacementRules ? rule.navMeshSearchRadius : navMeshSearchRadius;

            Transform parent = spawnedRoot;
            if (rule.createSubRoot)
                parent = EnsureSubRoot(rule.ruleName);

            int spawned = 0;
            int safetyTries = targetCount * Mathf.Max(1, triesPerMob);

            for (int i = 0; i < safetyTries && spawned < targetCount; i++)
            {
                if (!TryFindSpawnPoint(terrain, minXZ, maxXZ, slope, minDist, triesPerMob, yOff, needNav, navRadius,
                        out Vector3 pos, out Quaternion rot))
                    continue;

                var prefab = PickWeightedPrefab(rule.prefabs);
                if (prefab == null) continue;

                var go = Instantiate(prefab, pos, rot, parent);

                var agent = go.GetComponent<NavMeshAgent>();
                if (agent != null) agent.Warp(pos);

                _spawnedPositions.Add(pos);
                spawned++;
            }

            totalSpawned += spawned;
            Debug.Log($"[MobSpawnerModule] Rule '{rule.ruleName}' spawned {spawned}/{targetCount}");
        }

        UnityEngine.Random.state = prevState;
        Debug.Log($"[MobSpawnerModule] Total spawned: {totalSpawned}");
    }

    private GameObject PickWeightedPrefab(List<WeightedPrefab> list)
    {
        float total = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            if (w == null || w.prefab == null) continue;
            if (w.weight <= 0f) continue;
            total += w.weight;
        }

        // 전부 weight 0이거나 이상하면 그냥 균등 fallback
        if (total <= 0f)
        {
            // null 아닌 prefab 하나라도 찾기
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].prefab != null)
                    return list[i].prefab;
            return null;
        }

        float r = UnityEngine.Random.value * total;
        GameObject last = null;

        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            if (w == null || w.prefab == null) continue;
            if (w.weight <= 0f) continue;

            last = w.prefab;
            r -= w.weight;
            if (r <= 0f) return w.prefab;
        }

        return last;
    }

    private void EnsureSpawnedRoot()
    {
        if (spawnedRoot != null) return;

        var child = transform.Find(spawnedRootName);
        if (child != null) { spawnedRoot = child; return; }

        var rootGO = GameObject.Find(spawnedRootName);
        if (rootGO == null)
        {
            rootGO = new GameObject(spawnedRootName);
            if (parentRootToThisObject)
                rootGO.transform.SetParent(transform, worldPositionStays: true);
        }

        spawnedRoot = rootGO.transform;
    }

    private Transform EnsureSubRoot(string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName)) ruleName = "Rule";

        if (_subRoots.TryGetValue(ruleName, out var t) && t != null)
            return t;

        var child = spawnedRoot.Find(ruleName);
        if (child != null)
        {
            _subRoots[ruleName] = child;
            return child;
        }

        var go = new GameObject(ruleName);
        go.transform.SetParent(spawnedRoot, worldPositionStays: true);
        _subRoots[ruleName] = go.transform;
        return go.transform;
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

    private bool TryFindSpawnPoint(
        Terrain t, Vector2 minXZ, Vector2 maxXZ,
        float slopeAngle, float minDist, int triesPerMob, float yOff,
        bool needNavMesh, float navRadius,
        out Vector3 pos, out Quaternion rot)
    {
        for (int attempt = 0; attempt < triesPerMob; attempt++)
        {
            float x = UnityEngine.Random.Range(minXZ.x, maxXZ.x);
            float z = UnityEngine.Random.Range(minXZ.y, maxXZ.y);

            if (!IsSlopeOk(t, x, z, slopeAngle))
                continue;

            float y = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y + yOff;
            Vector3 p = new Vector3(x, y, z);

            if (!IsFarEnough(p, minDist))
                continue;

            rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            if (needNavMesh)
            {
                if (NavMesh.SamplePosition(p, out var hit, navRadius, NavMesh.AllAreas))
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
