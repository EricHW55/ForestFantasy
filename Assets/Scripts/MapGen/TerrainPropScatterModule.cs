using System;
using System.Collections.Generic;
using UnityEngine;

public class TerrainPropScatterModule : MonoBehaviour, ITerrainStep
{
    public int Order => 280;

    [Serializable]
    public class SpawnRule
    {
        public string name = "Props";
        public GameObject[] prefabs;

        [Header("How many / chance")]
        public int targetCount = 100;
        [Range(0f, 1f)] public float spawnChance = 1f;
        public int triesMultiplier = 6;

        [Header("Height / slope limits")]
        [Range(0f, 1f)] public float minHeight01 = 0.05f;
        [Range(0f, 1f)] public float maxHeight01 = 0.80f;
        [Range(0f, 1f)] public float maxSlope01  = 0.55f;

        [Header("Spacing (meters)")]
        public float minDistanceMeters = 3f;

        [Header("Placement")]
        public bool alignToTerrainNormal = true;
        public bool randomYaw = true;
        public float yOffset = 0f;

        [Header("Scale")]
        public Vector2 uniformScaleRange = new Vector2(0.9f, 1.4f);
        
        [Header("Collider & Layer")]
        [Tooltip("프리팹에 Collider가 없으면 자동 추가")]
        public bool autoAddCollider = true;
        public ColliderType colliderType = ColliderType.Capsule;
        
        [Tooltip("레이어 설정 (비워두면 Default)")]
        public string layerName = "Default";
        
        [Tooltip("크기별 레이어 분리 (예: 큰 돌만 Obstacle)")]
        public bool useSizeBasedLayer = false;
        public float minSizeForObstacle = 1.2f;
        public string obstacleLayerName = "Obstacle";

        [Header("Optional: prefer rocky area")]
        public TerrainFeatureHeightModule featureModule;
        [Range(0f, 1f)] public float rockMaskMin01 = 0.0f;
        [Range(0f, 3f)] public float rockMaskChanceBoost = 1.0f;
    }
    
    public enum ColliderType
    {
        None,
        Capsule,
        Box,
        Sphere
    }

    public List<SpawnRule> rules = new List<SpawnRule>();
    
    [Header("Root")]
    public string propsRootName = "PropsRoot";

    public void Apply(Terrain terrain, int seed)
    {
        if (rules == null || rules.Count == 0)
        {
            Debug.LogWarning("[TerrainPropScatterModule] Rules가 비어있습니다");
            return;
        }

        var td = terrain.terrainData;
        float sizeX = td.size.x;
        float sizeZ = td.size.z;

        var prev = UnityEngine.Random.state;

        Transform root = transform.Find(propsRootName);
        if (root == null)
        {
            var go = new GameObject(propsRootName);
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            root = go.transform;
        }
        else
        {
            // 기존 오브젝트 정리
            ClearChildren(root);
        }

        int totalPlaced = 0;

        for (int ri = 0; ri < rules.Count; ri++)
        {
            var r = rules[ri];
            if (r == null || r.prefabs == null || r.prefabs.Length == 0) continue;
            if (r.targetCount <= 0) continue;

            // 규칙별 시드 분리
            UnityEngine.Random.InitState(seed ^ 0x71A9C3D ^ (ri * 9973));

            var placed = new List<Vector2>();
            int tries = Mathf.Max(1, r.targetCount) * Mathf.Max(1, r.triesMultiplier);
            int made = 0;

            for (int t = 0; t < tries && made < r.targetCount; t++)
            {
                float u = UnityEngine.Random.value;
                float v = UnityEngine.Random.value;

                float h01 = td.GetInterpolatedHeight(u, v) / td.size.y;
                float s01 = td.GetSteepness(u, v) / 90f;

                if (h01 < r.minHeight01 || h01 > r.maxHeight01) continue;
                if (s01 > r.maxSlope01) continue;

                float chance = r.spawnChance;

                if (r.featureModule != null)
                {
                    float rock = r.featureModule.SampleRockMask01(u, v);
                    if (rock < r.rockMaskMin01) continue;
                    chance *= Mathf.Clamp01(1f + rock * r.rockMaskChanceBoost);
                }

                if (UnityEngine.Random.value > Mathf.Clamp01(chance)) continue;

                float wx = u * sizeX;
                float wz = v * sizeZ;

                if (r.minDistanceMeters > 0.01f)
                {
                    if (!FarEnough(placed, wx, wz, r.minDistanceMeters)) continue;
                    placed.Add(new Vector2(wx, wz));
                }

                Vector3 worldPos = terrain.transform.position + new Vector3(wx, 0f, wz);
                worldPos.y = terrain.SampleHeight(worldPos) + terrain.transform.position.y + r.yOffset;

                float yaw = r.randomYaw ? UnityEngine.Random.Range(0f, 360f) : 0f;
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

                if (r.alignToTerrainNormal)
                {
                    Vector3 n = td.GetInterpolatedNormal(u, v);
                    rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.AngleAxis(yaw, Vector3.up);
                }

                float sc = Mathf.Max(0.01f, UnityEngine.Random.Range(r.uniformScaleRange.x, r.uniformScaleRange.y));

                var prefab = r.prefabs[UnityEngine.Random.Range(0, r.prefabs.Length)];
                var go = Instantiate(prefab, worldPos, rot, root);
                go.transform.localScale *= sc;
                
                // Collider 자동 추가
                if (r.autoAddCollider)
                {
                    EnsureCollider(go, r.colliderType, sc);
                }
                
                // 레이어 설정
                AssignLayer(go, r, sc);

                made++;
            }
            
            totalPlaced += made;
            Debug.Log($"[TerrainPropScatterModule] '{r.name}': {made}/{r.targetCount} 배치 완료");
        }

        UnityEngine.Random.state = prev;
        Debug.Log($"[TerrainPropScatterModule] 총 {totalPlaced}개 오브젝트 배치 완료");
    }
    
    private void EnsureCollider(GameObject obj, ColliderType type, float scale)
    {
        if (type == ColliderType.None) return;
        
        // 이미 Collider가 있으면 패스
        var existingColliders = obj.GetComponentsInChildren<Collider>();
        if (existingColliders.Length > 0) return;
        
        switch (type)
        {
            case ColliderType.Capsule:
                var capsule = obj.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 2f * scale, 0f);
                capsule.radius = 0.3f * scale;
                capsule.height = 4f * scale;
                capsule.direction = 1; // Y축
                break;
                
            case ColliderType.Box:
                var box = obj.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 1f * scale, 0f);
                box.size = new Vector3(1f * scale, 2f * scale, 1f * scale);
                break;
                
            case ColliderType.Sphere:
                var sphere = obj.AddComponent<SphereCollider>();
                sphere.center = new Vector3(0f, 1f * scale, 0f);
                sphere.radius = 1f * scale;
                break;
        }
    }
    
    private void AssignLayer(GameObject obj, SpawnRule rule, float scale)
    {
        int layer = 0; // Default
        
        if (rule.useSizeBasedLayer)
        {
            // 크기별 레이어 분리
            if (scale >= rule.minSizeForObstacle)
            {
                layer = LayerMask.NameToLayer(rule.obstacleLayerName);
                if (layer < 0)
                {
                    Debug.LogWarning($"[TerrainPropScatterModule] 레이어 '{rule.obstacleLayerName}'를 찾을 수 없습니다");
                    layer = 0;
                }
            }
            else
            {
                layer = LayerMask.NameToLayer(rule.layerName);
                if (layer < 0) layer = 0;
            }
        }
        else
        {
            // 고정 레이어
            layer = LayerMask.NameToLayer(rule.layerName);
            if (layer < 0)
            {
                if (!string.IsNullOrEmpty(rule.layerName) && rule.layerName != "Default")
                {
                    Debug.LogWarning($"[TerrainPropScatterModule] 레이어 '{rule.layerName}'를 찾을 수 없습니다");
                }
                layer = 0;
            }
        }
        
        SetLayerRecursively(obj.transform, layer);
    }
    
    private void SetLayerRecursively(Transform tr, int layer)
    {
        tr.gameObject.layer = layer;
        for (int i = 0; i < tr.childCount; i++)
        {
            SetLayerRecursively(tr.GetChild(i), layer);
        }
    }
    
    private void ClearChildren(Transform root)
    {
        if (root == null) return;
        
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var c = root.GetChild(i).gameObject;
            if (Application.isPlaying)
                Destroy(c);
            else
                DestroyImmediate(c);
        }
    }

    private static bool FarEnough(List<Vector2> placed, float x, float z, float minDist)
    {
        float min2 = minDist * minDist;
        for (int i = 0; i < placed.Count; i++)
        {
            float dx = placed[i].x - x;
            float dz = placed[i].y - z;
            if (dx * dx + dz * dz < min2) return false;
        }
        return true;
    }
}