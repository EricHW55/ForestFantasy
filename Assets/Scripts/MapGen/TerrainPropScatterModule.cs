using System;
using System.Collections.Generic;
using UnityEngine;

public class TerrainPropScatterModule : MonoBehaviour, ITerrainStep
{
    public int Order => 280;

    [Serializable]
    public class SpawnRule
    {
        public string name = "Rocks";
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

        [Header("Optional: prefer rocky area")]
        public TerrainFeatureHeightModule featureModule; // 있으면 rockMask 활용
        [Range(0f, 1f)] public float rockMaskMin01 = 0.0f;
        [Range(0f, 3f)] public float rockMaskChanceBoost = 1.0f;
    }

    // ✅ 이게 Inspector에 보여야 정상
    public List<SpawnRule> rules = new List<SpawnRule>();

    public void Apply(Terrain terrain, int seed)
    {
        if (rules == null || rules.Count == 0) return;

        var td = terrain.terrainData;
        float sizeX = td.size.x;
        float sizeZ = td.size.z;

        // 이전 랜덤 상태 보존(다른 모듈과 독립적으로)
        var prev = UnityEngine.Random.state;

        // 생성된 구조물 담을 부모(정리용)
        Transform root = transform.Find("PropsRoot");
        if (root == null)
        {
            var go = new GameObject("PropsRoot");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            root = go.transform;
        }

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

                made++;
            }
        }

        UnityEngine.Random.state = prev;
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
