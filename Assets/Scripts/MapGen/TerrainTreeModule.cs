using System.Collections.Generic;
using UnityEngine;

public class TerrainTreeModule : MonoBehaviour, ITerrainStep
{
    public int Order => 300;

    public GameObject[] treePrefabs;
    public int treeCount = 4000;

    [Range(0f, 1f)] public float minHeight01 = 0.05f;
    [Range(0f, 1f)] public float maxHeight01 = 0.75f;
    [Range(0f, 1f)] public float maxSlope01  = 0.40f;

    public void Apply(Terrain terrain, int seed)
    {
        var td = terrain.terrainData;
        if (treePrefabs == null || treePrefabs.Length == 0) return;

        var protos = new TreePrototype[treePrefabs.Length];
        for (int i = 0; i < treePrefabs.Length; i++)
            protos[i] = new TreePrototype { prefab = treePrefabs[i] };
        td.treePrototypes = protos;

        var trees = new List<TreeInstance>(treeCount);
        int tries = treeCount * 3;

        for (int i = 0; i < tries && trees.Count < treeCount; i++)
        {
            float u = Random.value;
            float v = Random.value;

            float h01 = td.GetInterpolatedHeight(u, v) / td.size.y;
            float s01 = td.GetSteepness(u, v) / 90f;

            if (h01 < minHeight01 || h01 > maxHeight01) continue;
            if (s01 > maxSlope01) continue;

            int protoIndex = Random.Range(0, protos.Length);

            trees.Add(new TreeInstance
            {
                prototypeIndex = protoIndex,
                position = new Vector3(u, h01, v),
                widthScale = Random.Range(0.9f, 1.2f),
                heightScale = Random.Range(0.9f, 1.3f),
                color = Color.white,
                lightmapColor = Color.white
            });
        }

        td.treeInstances = trees.ToArray();
    }
}
