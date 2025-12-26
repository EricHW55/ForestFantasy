using UnityEngine;

public class TerrainHeightModule : MonoBehaviour, ITerrainStep
{
    public int Order => 100;

    [Range(0.01f, 0.6f)] public float heightScale01 = 0.25f;
    public float noiseScale = 6f;
    public int octaves = 5;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;

    public bool useIslandFalloff = true;
    public float falloffPower = 2.2f;

    public void Apply(Terrain terrain, int seed)
    {
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        float[,] h = new float[res, res];

        float offX = Random.Range(-10000f, 10000f);
        float offY = Random.Range(-10000f, 10000f);

        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float u = x / (res - 1f);
            float v = y / (res - 1f);

            float e = FractalNoise(u, v, offX, offY);

            if (useIslandFalloff)
            {
                float dx = u - 0.5f;
                float dv = v - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dv * dv) / 0.5f;
                float falloff = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Clamp01(d), falloffPower));
                e *= falloff;
            }

            h[y, x] = Mathf.Clamp01(e * heightScale01);
        }

        td.SetHeights(0, 0, h);
    }

    float FractalNoise(float u, float v, float offX, float offY)
    {
        float amp = 1f, freq = 1f, sum = 0f, norm = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float nx = (u * noiseScale * freq) + offX;
            float ny = (v * noiseScale * freq) + offY;

            sum += Mathf.PerlinNoise(nx, ny) * amp;
            norm += amp;

            amp *= persistence;
            freq *= lacunarity;
        }

        return norm > 0f ? sum / norm : 0f;
    }
}
