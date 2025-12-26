using System.Linq;
using UnityEngine;

public class TerrainGenManager : MonoBehaviour
{
    public Terrain terrain;
    public bool randomSeedOnPlay = true;
    public int seed = 12345;
    public bool cloneTerrainDataOnPlay = true;

    void Start()
    {
        if (!terrain) terrain = FindObjectOfType<Terrain>();
        if (!terrain) { Debug.LogError("Terrain이 없음"); return; }

        if (cloneTerrainDataOnPlay)
            terrain.terrainData = Instantiate(terrain.terrainData);

        // ✅ 중요: 콜라이더가 같은 TerrainData를 보게 강제 연결
        var col = terrain.GetComponent<TerrainCollider>();
        if (!col) col = terrain.gameObject.AddComponent<TerrainCollider>();
        col.terrainData = terrain.terrainData;

        if (randomSeedOnPlay) seed = System.Environment.TickCount;
        Random.InitState(seed);

        var steps = GetComponents<MonoBehaviour>().OfType<ITerrainStep>().OrderBy(s => s.Order);
        foreach (var step in steps) step.Apply(terrain, seed);

        // 콜라이더 갱신 한 번 더 (안전빵)
        col.terrainData = terrain.terrainData;

        terrain.Flush();
        Physics.SyncTransforms();
    }
}
