using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns prefabs around a target (usually the Camera) in deterministic chunks and despawns them when out of range.
/// - Deterministic: same seed + same chunk coord => same placements.
/// - Efficient: object pooling + chunk streaming.
/// </summary>
[DisallowMultipleComponent]
public class ProximityPrefabSpawnModule : MonoBehaviour
{
    [Header("Target / Range")]
    public Transform target;
    [Min(1f)] public float spawnRadius = 70f;
    [Min(1f)] public float despawnRadius = 85f;

    [Header("Chunk")]
    [Min(4f)] public float chunkSize = 16f;
    [Tooltip("Limit how many NEW chunks are built per frame to avoid spikes.")]
    [Range(1, 32)] public int maxNewChunksPerFrame = 2;

    [Header("Determinism")]
    public bool useTerrainGenManagerSeed = true;
    public int seedOverride = 12345;

    [Header("Rules")]
    public List<SpawnRule> rules = new List<SpawnRule>();

    [Serializable]
    public class SpawnRule
    {
        public string name = "Rule";
        public List<GameObject> prefabs = new List<GameObject>();

        [Header("How many / chance")]
        [Min(0)] public int targetCountPerChunk = 80;
        [Range(0f, 1f)] public float spawnChance = 1f;
        [Min(1)] public int triesMultiplier = 6;

        [Header("Height / slope (0~1)")]
        [Range(0f, 1f)] public float minHeight01 = 0f;
        [Range(0f, 1f)] public float maxHeight01 = 1f;
        [Range(0f, 1f)] public float maxSlope01 = 0.85f; // 0=flat, 1=vertical(90deg)

        [Header("Spacing / placement")]
        [Min(0f)] public float minDistance = 0.35f;
        public bool alignToTerrainNormal = false;
        public bool randomYaw = true;
        public float yOffset = 0f;
        public Vector2 uniformScaleRange = new Vector2(0.8f, 1.4f);

        [Header("Natural clustering (noise)")]
        [Tooltip("Bigger -> larger patches. (Perlin uses worldPos * scale)")]
        [Min(0f)] public float macroNoiseScale = 0.02f;
        [Range(0f, 1f)] public float macroThreshold = 0.35f;
        [Min(0.01f)] public float macroSharpness = 2.0f;

        [Tooltip("Smaller patch variation inside macro areas.")]
        [Min(0f)] public float microNoiseScale = 0.12f;
        [Min(0.01f)] public float microSharpness = 1.0f;
    }

    private int _seed;
    private readonly Dictionary<Vector2Int, ChunkRecord> _activeChunks = new Dictionary<Vector2Int, ChunkRecord>();

    private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new Dictionary<GameObject, Stack<GameObject>>();
    private Transform _poolRoot;

    private Terrain[] _terrains;
    private Bounds[] _terrainBounds;
    private float _terrainRefreshT;

    private void Awake()
    {
        if (target == null && Camera.main != null) target = Camera.main.transform;

        _seed = seedOverride;
        if (useTerrainGenManagerSeed)
        {
            var gen = FindObjectOfType<TerrainGenManager>();
            if (gen != null) _seed = gen.seed;
        }

        _poolRoot = new GameObject("~PrefabPool").transform;
        _poolRoot.SetParent(transform, false);

        RefreshTerrains();
    }

    private void Update()
    {
        if (target == null) return;

        if (Time.unscaledTime - _terrainRefreshT > 1.0f)
        {
            RefreshTerrains();
            _terrainRefreshT = Time.unscaledTime;
        }

        Vector3 p = target.position;
        Vector2Int center = WorldToChunk(p);

        int rChunk = Mathf.CeilToInt(spawnRadius / chunkSize);
        int newBuilt = 0;

        // ensure chunks within radius exist
        for (int dz = -rChunk; dz <= rChunk; dz++)
        {
            for (int dx = -rChunk; dx <= rChunk; dx++)
            {
                Vector2Int c = new Vector2Int(center.x + dx, center.y + dz);

                Vector3 chunkCenter = ChunkToWorldCenter(c);
                float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(chunkCenter.x, chunkCenter.z));
                if (dist > spawnRadius) continue;

                if (_activeChunks.ContainsKey(c)) continue;

                if (newBuilt >= maxNewChunksPerFrame) continue;
                BuildChunk(c);
                newBuilt++;
            }
        }

        // despawn far chunks
        float despawnR = Mathf.Max(despawnRadius, spawnRadius + chunkSize);
        List<Vector2Int> toRemove = null;

        foreach (var kv in _activeChunks)
        {
            Vector2Int c = kv.Key;
            Vector3 chunkCenter = ChunkToWorldCenter(c);
            float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(chunkCenter.x, chunkCenter.z));
            if (dist <= despawnR) continue;

            toRemove ??= new List<Vector2Int>();
            toRemove.Add(c);
        }

        if (toRemove != null)
        {
            foreach (var c in toRemove) DestroyChunk(c);
        }
    }

    // ---------------- chunk build / destroy ----------------

    private void BuildChunk(Vector2Int chunk)
    {
        var rec = new ChunkRecord(chunk);
        _activeChunks.Add(chunk, rec);

        Vector3 origin = ChunkToWorldMin(chunk);

        for (int ri = 0; ri < rules.Count; ri++)
        {
            var r = rules[ri];
            if (r == null || r.prefabs == null || r.prefabs.Count == 0) continue;
            if (r.targetCountPerChunk <= 0) continue;

            SpawnRuleInChunk(rec, origin, chunk, ri, r);
        }
    }

    private void DestroyChunk(Vector2Int chunk)
    {
        if (!_activeChunks.TryGetValue(chunk, out var rec)) return;

        for (int i = 0; i < rec.instances.Count; i++)
        {
            var inst = rec.instances[i];
            Release(inst.prefab, inst.go);
        }
        rec.instances.Clear();

        _activeChunks.Remove(chunk);
    }

    private void SpawnRuleInChunk(ChunkRecord rec, Vector3 chunkOrigin, Vector2Int chunk, int ruleIndex, SpawnRule r)
    {
        float minDist = Mathf.Max(0.0001f, r.minDistance);
        float minDist2 = minDist * minDist;

        // Simple spatial hashing inside chunk to enforce minDistance cheaply
        float cellSize = minDist;
        int gridW = Mathf.CeilToInt(chunkSize / cellSize);
        int gridH = Mathf.CeilToInt(chunkSize / cellSize);

        var grid = DictionaryPool<int, List<Vector3>>.Get();

        int tries = r.targetCountPerChunk * Mathf.Max(1, r.triesMultiplier);
        int placed = 0;

        uint baseH = Hash3((uint)_seed, (uint)(chunk.x * 73856093), (uint)(chunk.y * 19349663));
        baseH = Hash3(baseH, (uint)ruleIndex, 0xA341316Cu);

        for (int t = 0; t < tries; t++)
        {
            // Deterministic random point in chunk
            float rx = Hash01(baseH, (uint)(t * 2 + 0));
            float rz = Hash01(baseH, (uint)(t * 2 + 1));

            float wx = chunkOrigin.x + rx * chunkSize;
            float wz = chunkOrigin.z + rz * chunkSize;

            // Noise-based clustering: macro(large patches) * micro(small variation)
            float macro = Mathf.PerlinNoise((wx + 1000f) * r.macroNoiseScale, (wz + 1000f) * r.macroNoiseScale);
            float macroGate = Mathf.InverseLerp(r.macroThreshold, 1f, macro);
            macroGate = Mathf.Clamp01(macroGate);
            macroGate = Mathf.Pow(macroGate, r.macroSharpness);

            float micro = Mathf.PerlinNoise((wx + 2000f) * r.microNoiseScale, (wz + 2000f) * r.microNoiseScale);
            micro = Mathf.Pow(Mathf.Clamp01(micro), r.microSharpness);

            float densityFactor = macroGate * micro;
            float accept = densityFactor * r.spawnChance;

            if (Hash01(baseH, (uint)(0xB5297A4Du + (uint)t)) > accept) continue;

            Vector3 worldPos = new Vector3(wx, 0f, wz);

            if (!TrySampleTerrain(worldPos, out float height, out Vector3 normal, out float height01, out float slope01))
                continue;

            if (height01 < r.minHeight01 || height01 > r.maxHeight01) continue;
            if (slope01 > r.maxSlope01) continue;

            worldPos.y = height + r.yOffset;

            // Min distance check via grid
            int cx = Mathf.Clamp(Mathf.FloorToInt((wx - chunkOrigin.x) / cellSize), 0, gridW - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((wz - chunkOrigin.z) / cellSize), 0, gridH - 1);
            int cellKey = cx + cz * 4096;

            bool tooClose = false;
            for (int dz = -1; dz <= 1 && !tooClose; dz++)
            {
                for (int dx = -1; dx <= 1 && !tooClose; dx++)
                {
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (nx < 0 || nx >= gridW || nz < 0 || nz >= gridH) continue;
                    int nk = nx + nz * 4096;

                    if (!grid.TryGetValue(nk, out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Vector3 q = list[i];
                        float d2 = (q.x - worldPos.x) * (q.x - worldPos.x) + (q.z - worldPos.z) * (q.z - worldPos.z);
                        if (d2 < minDist2) { tooClose = true; break; }
                    }
                }
            }
            if (tooClose) continue;

            if (!grid.TryGetValue(cellKey, out var cellList))
            {
                cellList = ListPool<Vector3>.Get();
                grid[cellKey] = cellList;
            }
            cellList.Add(worldPos);

            // Pick prefab deterministically
            int prefabIndex = Mathf.FloorToInt(Hash01(baseH, (uint)(0xC2B2AE35u + (uint)t)) * r.prefabs.Count);
            prefabIndex = Mathf.Clamp(prefabIndex, 0, r.prefabs.Count - 1);
            var prefab = r.prefabs[prefabIndex];
            if (prefab == null) continue;

            float scale01 = Hash01(baseH, (uint)(0x165667B1u + (uint)t));
            float scale = Mathf.Lerp(r.uniformScaleRange.x, r.uniformScaleRange.y, scale01);

            float yaw = r.randomYaw ? Hash01(baseH, (uint)(0x9E3779B9u + (uint)t)) * 360f : 0f;

            Quaternion rot;
            if (r.alignToTerrainNormal)
            {
                // Apply yaw around the normal-ish frame (good for rocks); for grass, usually keep alignToTerrainNormal=false
                rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                rot = Quaternion.Euler(0f, yaw, 0f);
            }

            var go = Acquire(prefab);
            go.transform.SetPositionAndRotation(worldPos, rot);
            go.transform.localScale = new Vector3(scale, scale, scale);

            rec.instances.Add(new SpawnedInstance(prefab, go));

            placed++;
            if (placed >= r.targetCountPerChunk) break;
        }

        // release pooled temp lists
        foreach (var kv in grid)
            ListPool<Vector3>.Release(kv.Value);
        DictionaryPool<int, List<Vector3>>.Release(grid);
    }

    // ---------------- terrain sampling ----------------

    private void RefreshTerrains()
    {
        _terrains = Terrain.activeTerrains;
        if (_terrains == null) _terrains = Array.Empty<Terrain>();

        _terrainBounds = new Bounds[_terrains.Length];
        for (int i = 0; i < _terrains.Length; i++)
        {
            Terrain t = _terrains[i];
            if (t == null || t.terrainData == null)
            {
                _terrainBounds[i] = new Bounds(Vector3.zero, Vector3.zero);
                continue;
            }

            Vector3 pos = t.transform.position;
            Vector3 size = t.terrainData.size;
            _terrainBounds[i] = new Bounds(pos + size * 0.5f, size);
        }
    }

    private bool TrySampleTerrain(Vector3 worldPos, out float height, out Vector3 normal, out float height01, out float slope01)
    {
        height = 0f;
        normal = Vector3.up;
        height01 = 0f;
        slope01 = 0f;

        if (_terrains == null || _terrains.Length == 0) return false;

        Terrain t = null;
        TerrainData td = null;

        for (int i = 0; i < _terrains.Length; i++)
        {
            if (_terrains[i] == null) continue;
            if (!_terrainBounds[i].Contains(new Vector3(worldPos.x, _terrainBounds[i].center.y, worldPos.z))) continue;
            t = _terrains[i];
            td = t.terrainData;
            break;
        }

        if (t == null || td == null) return false;

        Vector3 tp = t.transform.position;
        Vector3 size = td.size;

        float u = Mathf.InverseLerp(tp.x, tp.x + size.x, worldPos.x);
        float v = Mathf.InverseLerp(tp.z, tp.z + size.z, worldPos.z);

        float h = td.GetInterpolatedHeight(u, v);
        height = tp.y + h;

        normal = td.GetInterpolatedNormal(u, v).normalized;

        height01 = Mathf.Clamp01(h / Mathf.Max(0.0001f, size.y));
        slope01 = Mathf.Clamp01(Vector3.Angle(normal, Vector3.up) / 90f);

        return true;
    }

    // ---------------- pooling ----------------

    private GameObject Acquire(GameObject prefab)
    {
        if (prefab == null) return null;

        if (_pool.TryGetValue(prefab, out var stack) && stack.Count > 0)
        {
            var go = stack.Pop();
            go.SetActive(true);
            return go;
        }

        var inst = Instantiate(prefab);
        inst.name = prefab.name;
        inst.transform.SetParent(transform, true);
        return inst;
    }

    private void Release(GameObject prefab, GameObject go)
    {
        if (go == null) return;

        go.SetActive(false);
        go.transform.SetParent(_poolRoot, false);

        if (prefab == null) prefab = go; // fallback

        if (!_pool.TryGetValue(prefab, out var stack))
        {
            stack = new Stack<GameObject>();
            _pool[prefab] = stack;
        }
        stack.Push(go);
    }

    // ---------------- helpers / data ----------------

    private struct SpawnedInstance
    {
        public GameObject prefab;
        public GameObject go;
        public SpawnedInstance(GameObject prefab, GameObject go) { this.prefab = prefab; this.go = go; }
    }

    private class ChunkRecord
    {
        public Vector2Int chunk;
        public readonly List<SpawnedInstance> instances = new List<SpawnedInstance>(256);
        public ChunkRecord(Vector2Int c) { chunk = c; }
    }

    private Vector2Int WorldToChunk(Vector3 p)
    {
        int cx = Mathf.FloorToInt(p.x / chunkSize);
        int cz = Mathf.FloorToInt(p.z / chunkSize);
        return new Vector2Int(cx, cz);
    }

    private Vector3 ChunkToWorldMin(Vector2Int c)
    {
        return new Vector3(c.x * chunkSize, 0f, c.y * chunkSize);
    }

    private Vector3 ChunkToWorldCenter(Vector2Int c)
    {
        var min = ChunkToWorldMin(c);
        return new Vector3(min.x + chunkSize * 0.5f, 0f, min.z + chunkSize * 0.5f);
    }

    // -------- deterministic hash rng --------

    private static uint Hash2(uint a, uint b)
    {
        uint x = a * 0x9E3779B9u + b * 0x85EBCA6Bu;
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    private static uint Hash3(uint a, uint b, uint c)
    {
        uint x = Hash2(a, b);
        x = Hash2(x, c);
        return x;
    }

    private static float Hash01(uint h, uint salt)
    {
        uint x = Hash2(h, salt);
        return (x & 0x00FFFFFFu) / 16777215f;
    }

    // ---------------- tiny pools to avoid GC ----------------

    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> S = new Stack<List<T>>();
        public static List<T> Get() => (S.Count > 0) ? S.Pop() : new List<T>();
        public static void Release(List<T> list)
        {
            if (list == null) return;
            list.Clear();
            S.Push(list);
        }
    }

    private static class DictionaryPool<K, V>
    {
        private static readonly Stack<Dictionary<K, V>> S = new Stack<Dictionary<K, V>>();
        public static Dictionary<K, V> Get() => (S.Count > 0) ? S.Pop() : new Dictionary<K, V>();
        public static void Release(Dictionary<K, V> dict)
        {
            if (dict == null) return;
            dict.Clear();
            S.Push(dict);
        }
    }
}
