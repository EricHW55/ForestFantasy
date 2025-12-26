using UnityEngine;

public class TerrainFeatureHeightModule : MonoBehaviour, ITerrainStep
{
    public int Order => 120;

    public enum StampType { RockHill, MesaCliff }
    public StampType stampType = StampType.MesaCliff;

    [Header("How many features")]
    public int featureCount = 6;
    [Range(0f, 1f)] public float spawnChance = 0.35f; // 낮은 확률 이벤트

    [Header("Stamp shape (meters)")]
    public float radiusMin = 60f;
    public float radiusMax = 180f;

    [Header("Height impact (normalized 0~1)")]
    [Range(0f, 0.5f)] public float addHeight01 = 0.12f;      // RockHill용
    [Range(0f, 0.5f)] public float cliffStep01 = 0.18f;      // MesaCliff용
    [Range(0.1f, 8f)] public float edgeSharpness = 3.5f;     // 가장자리 급함 정도

    // 텍스처/디테일 모듈에서 읽을 수 있게 공개
    [HideInInspector] public float[,] rockMask01; // heightmapResolution과 동일 크기

    public float SampleRockMask01(float u, float v)
    {
        if (rockMask01 == null) return 0f;
        int res = rockMask01.GetLength(0);
        int x = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
        return rockMask01[y, x];
    }

    public void Apply(Terrain terrain, int seed)
    {
        var td = terrain.terrainData;
        int res = td.heightmapResolution;

        var h = td.GetHeights(0, 0, res, res);
        rockMask01 = new float[res, res];

        // 모듈끼리 랜덤 소비 순서가 꼬여도 결과가 흔들리지 않게: 로컬 시드 사용
        var prev = Random.state;
        Random.InitState(seed ^ 0x51F3A1B); // 상수 XOR로 모듈별 시드 분리

        float sizeX = td.size.x;
        float sizeZ = td.size.z;

        for (int k = 0; k < featureCount; k++)
        {
            if (Random.value > spawnChance) continue;

            float u0 = Random.value;
            float v0 = Random.value;

            float rMeters = Random.Range(radiusMin, radiusMax);
            float rU = rMeters / sizeX;
            float rV = rMeters / sizeZ;

            int xMin = Mathf.Clamp(Mathf.FloorToInt((u0 - rU) * (res - 1)), 0, res - 1);
            int xMax = Mathf.Clamp(Mathf.CeilToInt((u0 + rU) * (res - 1)), 0, res - 1);
            int yMin = Mathf.Clamp(Mathf.FloorToInt((v0 - rV) * (res - 1)), 0, res - 1);
            int yMax = Mathf.Clamp(Mathf.CeilToInt((v0 + rV) * (res - 1)), 0, res - 1);

            // MesaCliff는 "한쪽이 확 올라가는 절벽 띠" 느낌을 위해 방향 벡터 하나 뽑음
            Vector2 dir = Vector2.right;
            if (stampType == StampType.MesaCliff)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)).normalized;
            }

            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                float u = x / (res - 1f);
                float v = y / (res - 1f);

                float du = (u - u0) / Mathf.Max(1e-6f, rU);
                float dv = (v - v0) / Mathf.Max(1e-6f, rV);
                float d = Mathf.Sqrt(du * du + dv * dv); // 0(중심) ~ 1(반경)

                if (d > 1f) continue;

                // 0~1 중심 강도. edgeSharpness가 클수록 가장자리가 급해짐(절벽 느낌)
                float w = Mathf.Pow(1f - d, edgeSharpness);

                if (stampType == StampType.RockHill)
                {
                    // 부드러운 돌산
                    h[y, x] = Mathf.Clamp01(h[y, x] + w * addHeight01);
                    rockMask01[y, x] = Mathf.Max(rockMask01[y, x], w);
                }
                else // MesaCliff
                {
                    // "절벽" = 같은 원 안에서도 방향에 따라 한쪽은 올리고 한쪽은 덜 올림(단차 느낌)
                    Vector2 p = new Vector2(u - u0, v - v0);
                    float side = Vector2.Dot(p.normalized, dir); // -1~1
                    float step = Mathf.SmoothStep(-0.15f, 0.15f, side); // 0~1
                    float cliffW = w * step;

                    h[y, x] = Mathf.Clamp01(h[y, x] + cliffW * cliffStep01);
                    rockMask01[y, x] = Mathf.Max(rockMask01[y, x], cliffW);
                }
            }
        }

        td.SetHeights(0, 0, h);

        Random.state = prev;
    }
}
