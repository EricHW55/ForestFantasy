using UnityEngine;

public class TerrainTextureModule : MonoBehaviour, ITerrainStep
{
    public int Order => 200;

    [Header("Terrain Layers")]
    public TerrainLayer grassLayer;
    public TerrainLayer dirtLayer;
    public TerrainLayer rockLayer;
    public TerrainLayer snowLayer; // 필요 없으면 null

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
    public float noiseScale = 5f;
    [Range(0f, 0.6f)] public float noiseStrength = 0.25f;

    [Header("Dirt patches (sparse blotches)")]
    public float dirtMacroScale = 0.6f;
    [Range(0f, 1f)] public float dirtMacroMix = 0.75f;
    public float dirtPatchScale = 2.0f;

    [Tooltip("전체 평균 흙 비중(지역 변조가 있더라도 이 값을 중심으로 움직임). 듬성듬성=0.06~0.12, 조금 더=0.12~0.20")]
    [Range(0f, 0.50f)] public float dirtPatchCoverage = 0.12f;

    [Range(0f, 1f)] public float dirtPatchStrength = 0.85f;
    [Range(0.25f, 6f)] public float dirtPatchSharpness = 1.2f;
    [Range(0.5f, 3f)] public float dirtMaskBoost = 1.6f;
    [Range(0f, 1f)] public float dirtSlopeBlend = 0.05f;
    [Range(0f, 1f)] public float dirtPatchSlopePenalty = 0.0f;
    [Range(0f, 1f)] public float dirtPatchEdgeWarp = 0.35f;

    [Header("Regional variation (make some areas dirtier / cleaner)")]
    [Tooltip("지역 덩어리 크기(작을수록 더 큰 덩어리). 0.12~0.25 추천")]
    [Range(0.01f, 2.0f)] public float dirtRegionScale = 0.18f;

    [Tooltip("지역이 '깨끗한 구역'일 때 coverage 배수")]
    [Range(0.1f, 3.0f)] public float dirtCoverageMinMultiplier = 0.55f;

    [Tooltip("지역이 '흙 많은 구역'일 때 coverage 배수")]
    [Range(0.1f, 3.0f)] public float dirtCoverageMaxMultiplier = 1.75f;

    [Tooltip("지역 대비(클수록 지역 차이가 뚜렷해짐). 1.2~2.0 추천")]
    [Range(0.2f, 4.0f)] public float dirtRegionContrast = 1.4f;

    [Header("Dirt intensity random (opacity-ish)")]
    [Tooltip("패치마다 흙 진하기를 랜덤으로 흔듦(최소)")]
    [Range(0.2f, 2.0f)] public float dirtStrengthRandomMin = 0.75f;

    [Tooltip("패치마다 흙 진하기를 랜덤으로 흔듦(최대)")]
    [Range(0.2f, 2.0f)] public float dirtStrengthRandomMax = 1.35f;

    [Tooltip("랜덤 진하기 노이즈 변화 빈도(클수록 더 자주 바뀜)")]
    [Range(0.5f, 20f)] public float dirtStrengthNoiseScale = 6f;

    [Header("Safety (if dirt becomes invisible)")]
    [Tooltip("흙 패치가 '있어야 하는 곳'에서 최소로 보장할 가중치(흐릿 방지). 0.05~0.10 추천")]
    [Range(0f, 0.2f)] public float dirtMinVisibleWeight = 0.07f;

    // u,v(0~1)에서도 Perlin이 충분히 변하도록 주파수 보정
    const float FREQ_MULT = 12f;

    public void Apply(Terrain terrain, int seed)
    {
        var td = terrain.terrainData;

        if (snowLayer != null)
            td.terrainLayers = new[] { grassLayer, dirtLayer, rockLayer, snowLayer };
        else
            td.terrainLayers = new[] { grassLayer, dirtLayer, rockLayer };

        int lc = td.terrainLayers.Length;

        int aw = td.alphamapWidth;
        int ah = td.alphamapHeight;
        var a = new float[ah, aw, lc];

        var prev = Random.state;
        Random.InitState(seed ^ 0x5EED123);

        float o1 = Random.Range(-10000f, 10000f);
        float o2 = Random.Range(-10000f, 10000f);
        float o3 = Random.Range(-10000f, 10000f);
        float o4 = Random.Range(-10000f, 10000f);
        float o5 = Random.Range(-10000f, 10000f);
        float o6 = Random.Range(-10000f, 10000f);
        float o7 = Random.Range(-10000f, 10000f);
        float o8 = Random.Range(-10000f, 10000f);
        float o9 = Random.Range(-10000f, 10000f);
        float o10 = Random.Range(-10000f, 10000f);

        float macroFreq = Mathf.Max(0.01f, dirtMacroScale) * FREQ_MULT;
        float patchFreq = Mathf.Max(0.01f, dirtPatchScale) * FREQ_MULT;
        float warpFreq = patchFreq * 3f;
        float strengthFreq = patchFreq * dirtStrengthNoiseScale;

        // 지역 덩어리(바이옴) 주파수
        float regionFreq = Mathf.Max(0.01f, dirtRegionScale) * FREQ_MULT;

        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
        {
            float u = x / (float)(aw - 1);
            float v = y / (float)(ah - 1);

            float h01 = td.GetInterpolatedHeight(u, v) / td.size.y;
            float s01 = td.GetSteepness(u, v) / 90f;

            // slope 경계 흔들기
            float n = Mathf.PerlinNoise(u * noiseScale + o1, v * noiseScale + o2);
            float slopeN = Mathf.Clamp01(s01 + (n - 0.5f) * noiseStrength);

            // Rock
            float rockW = Smooth01(rockSlopeStart01, rockSlopeEnd01, slopeN);

            if (featureModule != null)
            {
                float fm = featureModule.SampleRockMask01(u, v);
                if (fm >= featureRockMin01)
                    rockW = Mathf.Clamp01(rockW + fm * featureRockBoost);
            }

            // Snow
            float snowW = 0f;
            if (lc == 4)
            {
                snowW = Smooth01(snowStart01, snowEnd01, h01);
                snowW *= Mathf.Clamp01(1f - s01 * 0.9f);
            }

            // 경사 흙 약간(옵션)
            float dirtSlopeW = Smooth01(dirtSlopeStart01, dirtSlopeEnd01, slopeN) * dirtSlopeBlend;

            // =========================
            // Dirt patch mask (맵 전체 듬성듬성 + 지역 편차)
            // =========================

            // (A) 지역 마스크: 어떤 구역은 흙이 더 많고, 어떤 구역은 더 적게
            float region = Mathf.PerlinNoise(u * regionFreq + o9, v * regionFreq + o10);
            region = Mathf.Pow(region, dirtRegionContrast); // 대비 강화 (0~1)

            float localCoverage =
                dirtPatchCoverage * Mathf.Lerp(dirtCoverageMinMultiplier, dirtCoverageMaxMultiplier, region);

            localCoverage = Mathf.Clamp(localCoverage, 0f, 0.50f);

            float macro = Mathf.PerlinNoise(u * macroFreq + o5, v * macroFreq + o6);
            float baseP = Mathf.PerlinNoise(u * patchFreq + o3, v * patchFreq + o4);
            float warpP = Mathf.PerlinNoise(u * warpFreq + o4, v * warpFreq + o3);

            float p = Mathf.Lerp(baseP, macro, dirtMacroMix);
            p = Mathf.Clamp01(p + (warpP - 0.5f) * dirtPatchEdgeWarp);

            // coverage: 상위 영역을 흙 후보로 (지역별 localCoverage를 사용!)
            float thr = 1f - localCoverage;

            // thr~(thr+band) 구간으로 마스크 만들기
            float band = Mathf.Lerp(0.22f, 0.10f, Mathf.Clamp01(dirtPatchSharpness / 3f));
            float hi = Mathf.Clamp01(thr + band);

            float mask = Smooth01(thr, hi, p);
            mask = Mathf.Pow(mask, dirtPatchSharpness);
            mask = Mathf.Clamp01(mask * dirtMaskBoost);

            float dirtPatchW = mask * dirtPatchStrength;

            // ---- 진하기(가중치) 랜덤 ----
            float strengthN = Mathf.PerlinNoise(u * strengthFreq + o7, v * strengthFreq + o8);
            float strengthMul = Mathf.Lerp(dirtStrengthRandomMin, dirtStrengthRandomMax, strengthN);
            dirtPatchW *= strengthMul;

            // 패치가 잡힌 곳은 최소 가시량 보장(흐릿 방지)
            if (mask > 0.001f)
                dirtPatchW = Mathf.Max(dirtPatchW, dirtMinVisibleWeight);

            if (dirtPatchSlopePenalty > 0f)
                dirtPatchW *= Mathf.Clamp01(1f - s01 * dirtPatchSlopePenalty);

            float dirtW = Mathf.Clamp01(dirtPatchW + dirtSlopeW);

            // rock/snow 우선권
            float priorityMul = (1f - rockW) * (1f - snowW);
            dirtW *= priorityMul;

            // 우선권 곱한 뒤에도 “최소 가시량” 다시 보장
            if (mask > 0.001f)
            {
                float minAfter = dirtMinVisibleWeight * priorityMul;
                dirtW = Mathf.Max(dirtW, minAfter);
            }

            float grassW = Mathf.Clamp01(1f - (rockW + dirtW + snowW));

            float sum = grassW + dirtW + rockW + snowW;
            if (sum < 1e-6f)
            {
                grassW = 1f; sum = 1f;
                dirtW = rockW = snowW = 0f;
            }

            a[y, x, 0] = grassW / sum; // 0번 = grassLayer
            a[y, x, 1] = dirtW / sum;  // 1번 = dirtLayer
            a[y, x, 2] = rockW / sum;  // 2번 = rockLayer
            if (lc == 4) a[y, x, 3] = snowW / sum;
        }

        td.SetAlphamaps(0, 0, a);
        Random.state = prev;
    }

    static float Smooth01(float a, float b, float t)
        => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));
}
