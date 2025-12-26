using UnityEngine;

public class TerrainCaveModule : MonoBehaviour, ITerrainStep
{
    public int Order => 140;

    public GameObject cavePrefab;
    public int caveCount = 2;

    [Header("Hole size (meters)")]
    public float holeRadius = 10f;

    [Header("Spawn constraints")]
    [Range(0f, 1f)] public float minHeight01 = 0.05f;
    [Range(0f, 1f)] public float maxSlope01 = 0.35f;

    public float caveYOffset = -1.0f; // 입구를 살짝 박고 싶을 때

    public void Apply(Terrain terrain, int seed)
    {
        if (!cavePrefab) return;

        var td = terrain.terrainData;

        int hr = td.holesResolution;
        // 전체 홀 맵 가져오기
        bool[,] holes = td.GetHoles(0, 0, hr, hr);

        var prev = Random.state;
        Random.InitState(seed ^ 0xC0A7E); // 모듈 시드 분리

        float cellSizeX = td.size.x / (hr - 1f);
        float cellSizeZ = td.size.z / (hr - 1f);

        for (int i = 0; i < caveCount; i++)
        {
            float u = Random.value;
            float v = Random.value;

            float h01 = td.GetInterpolatedHeight(u, v) / td.size.y;
            float s01 = td.GetSteepness(u, v) / 90f;

            if (h01 < minHeight01) continue;
            if (s01 > maxSlope01) continue;

            int cx = Mathf.RoundToInt(u * (hr - 1));
            int cy = Mathf.RoundToInt(v * (hr - 1));

            int rx = Mathf.CeilToInt(holeRadius / cellSizeX);
            int ry = Mathf.CeilToInt(holeRadius / cellSizeZ);

            int xMin = Mathf.Clamp(cx - rx, 0, hr - 1);
            int xMax = Mathf.Clamp(cx + rx, 0, hr - 1);
            int yMin = Mathf.Clamp(cy - ry, 0, hr - 1);
            int yMax = Mathf.Clamp(cy + ry, 0, hr - 1);

            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                float dx = (x - cx) / Mathf.Max(1f, rx);
                float dy = (y - cy) / Mathf.Max(1f, ry);
                if (dx * dx + dy * dy <= 1f)
                    holes[y, x] = true; // true = hole
            }

            // 프리팹 배치(월드 좌표)
            Vector3 worldPos = terrain.transform.position +
                               new Vector3(u * td.size.x, 0f, v * td.size.z);
            worldPos.y = terrain.SampleHeight(worldPos) + terrain.transform.position.y + caveYOffset;

            Instantiate(cavePrefab, worldPos, Quaternion.identity, transform);
        }

        td.SetHoles(0, 0, holes);

        Random.state = prev;
    }
}
