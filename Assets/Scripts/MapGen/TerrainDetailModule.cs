using UnityEngine;

public class TerrainDetailModule : MonoBehaviour, ITerrainStep
{
    public int Order => 400;

    [Header("Detail textures (optional)")]
    public Texture2D grassDetailTexture;
    public Texture2D flowerDetailTexture;

    [Header("Density")]
    public int grassMaxDensity = 16;
    public int flowerMaxDensity = 6;

    [Range(0f, 1f)] public float maxSlope01 = 0.35f;
    [Range(0f, 1f)] public float maxHeight01 = 0.75f;
    public float noiseScale = 8f;

    [Header("Optional: link to feature module to avoid cliffs")]
    public TerrainFeatureHeightModule featureModule;
    [Range(0f, 2f)] public float rockMaskBlock = 1f;

    public void Apply(Terrain terrain, int seed)
    {
        var td = terrain.terrainData;

        // 디테일 프로토타입 설정
        var prototypes = new System.Collections.Generic.List<DetailPrototype>();
        int grassLayerIndex = -1, flowerLayerIndex = -1;

        if (grassDetailTexture)
        {
            prototypes.Add(new DetailPrototype
            {
                prototypeTexture = grassDetailTexture,
                renderMode = DetailRenderMode.GrassBillboard,
                healthyColor = Color.white,
                dryColor = Color.white,
                minWidth = 0.8f,
                maxWidth = 1.2f,
                minHeight = 0.8f,
                maxHeight = 1.6f
            });
            grassLayerIndex = prototypes.Count - 1;
        }

        if (flowerDetailTexture)
        {
            prototypes.Add(new DetailPrototype
            {
                prototypeTexture = flowerDetailTexture,
                renderMode = DetailRenderMode.GrassBillboard,
                healthyColor = Color.white,
                dryColor = Color.white,
                minWidth = 0.5f,
                maxWidth = 1.0f,
                minHeight = 0.5f,
                maxHeight = 1.2f
            });
            flowerLayerIndex = prototypes.Count - 1;
        }

        if (prototypes.Count == 0) return;

        td.detailPrototypes = prototypes.ToArray();

        int dr = td.detailResolution;
        int[,] grass = (grassLayerIndex >= 0) ? new int[dr, dr] : null;
        int[,] flower = (flowerLayerIndex >= 0) ? new int[dr, dr] : null;

        var prev = Random.state;
        Random.InitState(seed ^ 0xD371A); // 모듈 시드 분리

        for (int y = 0; y < dr; y++)
        for (int x = 0; x < dr; x++)
        {
            float u = x / (dr - 1f);
            float v = y / (dr - 1f);

            float h01 = td.GetInterpolatedHeight(u, v) / td.size.y;
            float s01 = td.GetSteepness(u, v) / 90f;

            if (h01 > maxHeight01 || s01 > maxSlope01) continue;

            float rock = 0f;
            if (featureModule) rock = featureModule.SampleRockMask01(u, v) * rockMaskBlock;
            if (rock > 0.25f) continue; // 절벽/돌산 영역엔 디테일 차단

            float n = Mathf.PerlinNoise(u * noiseScale + 13.3f, v * noiseScale + 9.7f);
            n = Mathf.SmoothStep(0.2f, 1f, n); // 낮은 값 밀어올리기
            float ng = Mathf.Pow(n, 0.8f);              // 0.8: 적당히 촘촘(0.6은 더 과감)

            if (grass != null)
                grass[y, x] = Mathf.RoundToInt(ng * grassMaxDensity);
            if (flower != null)
            {
                // 꽃은 더 희귀하게
                float f = Mathf.SmoothStep(0.75f, 1f, n);
                flower[y, x] = Mathf.RoundToInt(f * flowerMaxDensity);
            }
        }

        if (grass != null) td.SetDetailLayer(0, 0, grassLayerIndex, grass);
        if (flower != null) td.SetDetailLayer(0, 0, flowerLayerIndex, flower);

        Random.state = prev;
    }
}
