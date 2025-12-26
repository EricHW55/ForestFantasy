using UnityEngine;

public class TerrainTextureModule : MonoBehaviour, ITerrainStep
{
    public int Order => 200;

    [Header("Terrain Layers")]
    public TerrainLayer grassLayer;
    public TerrainLayer dirtLayer;
    public TerrainLayer rockLayer;
    public TerrainLayer snowLayer; // 필요 없으면 null로 두면 됨

    [Header("Feature (optional)")]
    public TerrainFeatureHeightModule featureModule;
    [Range(0f, 2f)] public float featureRockBoost = 1f;
    [Range(0f, 1f)] public float featureRockMin01 = 0.15f;

    [Header("Height (0~1)")]
    [Range(0f, 1f)] public float snowStart01 = 0.72f;
    [Range(0f, 1f)] public float snowEnd01 = 0.82f;

    [Header("Slope (0~1)")]
    [Range(0f, 1f)] public float rockSlopeStart01 = 0.45f;
    [Range(0f, 1f)] public float rockSlopeEnd01 = 0.70f;

    [Range(0f, 1f)] public float dirtSlopeStart01 = 0.20f;
    [Range(0f, 1f)] public float dirtSlopeEnd01 = 0.45f;

    [Header("Noise (break repetition)")]
    public float noiseScale = 5f;          // 클수록 큰 무늬(권장 4~8)
    [Range(0f, 0.6f)] public float noiseStrength = 0.25f;

    public void Apply(Terrain terrain, int seed)
    {
        var td = terrain.terrainData;

        // 레이어 세팅 (snowLayer가 없으면 3개만)
        if (snowLayer != null)
            td.terrainLayers = new[] { grassLayer, dirtLayer, rockLayer, snowLayer };
        else
            td.terrainLayers = new[] { grassLayer, dirtLayer, rockLayer };

        int lc = td.terrainLayers.Length;

        int aw = td.alphamapWidth;
        int ah = td.alphamapHeight;
        float[,,] a = new float[ah, aw, lc];

        var prev = Random.state;
        Random.InitState(seed ^ 0x5EED123);

        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
        {
            float u = x / (float)(aw - 1);
            float v = y / (float)(ah - 1);

            float h01 = td.GetInterpolatedHeight(u, v) / td.size.y;
            float s01 = td.GetSteepness(u, v) / 90f;

            // 저주파 노이즈로 경계 자연스럽게
            float n = Mathf.PerlinNoise(u * noiseScale, v * noiseScale);
            float slopeN = Mathf.Clamp01(s01 + (n - 0.5f) * noiseStrength);

            // Rock: 경사 기반
            float rockW = Smooth01(rockSlopeStart01, rockSlopeEnd01, slopeN);

            // Feature(절벽 스탬프 등) 있으면 rock 가중치 추가
            if (featureModule != null)
            {
                float fm = featureModule.SampleRockMask01(u, v);
                if (fm >= featureRockMin01)
                    rockW = Mathf.Clamp01(rockW + fm * featureRockBoost);
            }

            // Snow: 높이 기반 + 경사 큰 곳은 눈 덜 쌓이게
            float snowW = 0f;
            if (lc == 4) // snowLayer 존재
            {
                snowW = Smooth01(snowStart01, snowEnd01, h01);
                snowW *= Mathf.Clamp01(1f - s01 * 0.9f);
            }

            // Dirt: 완만~중간 경사 구간에 주로
            float dirtW = Smooth01(dirtSlopeStart01, dirtSlopeEnd01, slopeN);
            // rock/snow에 잠식되지 않게
            dirtW *= (1f - rockW) * (1f - snowW);

            // Grass: 나머지
            float grassW = Mathf.Clamp01(1f - (rockW + dirtW + snowW));

            // 정규화
            float sum = grassW + dirtW + rockW + snowW;
            if (sum < 1e-6f) { grassW = 1f; sum = 1f; }

            a[y, x, 0] = grassW / sum;
            a[y, x, 1] = dirtW  / sum;
            a[y, x, 2] = rockW  / sum;
            if (lc == 4) a[y, x, 3] = snowW / sum;
        }

        td.SetAlphamaps(0, 0, a);
        Random.state = prev;
    }

    static float Smooth01(float a, float b, float t)
        => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));
}
