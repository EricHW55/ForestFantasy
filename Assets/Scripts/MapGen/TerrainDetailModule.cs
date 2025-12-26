using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class TerrainDetailModule : MonoBehaviour, ITerrainStep
{
    public int Order => 390;

    [Header("Optional: force terrain detail resolution (recommended)")]
    public bool forceDetailResolution = true;
    public int detailResolution = 1024;
    public int detailResolutionPerPatch = 16;

    [Header("Optional: avoid cliffs via feature rock mask")]
    public TerrainFeatureHeightModule featureModule;

    [Header("Prefab helpers (prevents white details)")]
    [Tooltip("If a rule uses Prefab, try to extract an albedo-like texture and feed it to DetailPrototype.prototypeTexture.")]
    public bool usePrefabAlbedoAsPrototypeTexture = true;

    [Tooltip("If no albedo texture exists, try to tint (healthy/dry) using prefab material color (_BaseColor/_Color/etc).")]
    public bool autoTintFromPrefabMaterialColor = true;

    [Tooltip("For Prefab rules, force render mode to VertexLit (most stable).")]
    public bool forcePrefabRenderModeVertexLit = true;

    [Tooltip("If both Texture and Prefab are set, prefer Texture mode (Grass Billboard).")]
    public bool preferTextureWhenProvided = true;

    [Tooltip("Log what texture/color was picked from prefab.")]
    public bool logPrefabExtraction = true;

    [Serializable]
    public class DetailRule
    {
        public string name = "Base Grass";

        [Header("Prototype (Texture OR Prefab)")]
        public Texture2D texture;
        public GameObject prefab;

        [Header("Render")]
        public DetailRenderMode renderMode = DetailRenderMode.VertexLit;
        public Color healthyColor = Color.white;
        public Color dryColor = Color.white;

        [Header("Size (meters-ish)")]
        public float minWidth = 0.7f;
        public float maxWidth = 1.2f;
        public float minHeight = 0.7f;
        public float maxHeight = 1.6f;

        [Header("Where it can spawn")]
        [Range(0f, 1f)] public float minHeight01 = 0f;
        [Range(0f, 1f)] public float maxHeight01 = 1f;
        [Range(0f, 1f)] public float maxSlope01 = 0.85f;

        [Header("Density / coverage")]
        public int maxDensity = 40;
        [Range(0f, 1f)] public float coverage = 0.95f;
        [Range(0f, 1f)] public float spawnChance = 1f;

        [Header("Clumping (make patches)")]
        public float noiseScale = 6f;
        public float clumpSharpness = 0.8f;
        [Range(0f, 1f)] public float edgeWarp = 0.25f;

        [Header("Optional: avoid rocky mask")]
        public bool avoidRockMask = false;
        [Range(0f, 1f)] public float rockMaskThreshold = 0.35f;
        [Range(0f, 3f)] public float rockMaskBlock = 1f;
    }

    public List<DetailRule> rules = new();

    public void Apply(Terrain terrain, int seed)
    {
        if (!terrain) return;
        var td = terrain.terrainData;
        if (!td) return;

        // 1) Ensure resolution
        if (forceDetailResolution)
        {
            detailResolution = Mathf.Clamp(detailResolution, 32, 4096);
            detailResolutionPerPatch = Mathf.Clamp(detailResolutionPerPatch, 8, 128);
            td.SetDetailResolution(detailResolution, detailResolutionPerPatch);
        }

        int res = td.detailResolution;
        if (res <= 0) return;

        // 2) Build prototypes
        var prototypes = new List<DetailPrototype>();
        var activeRules = new List<DetailRule>();

        foreach (var r in rules)
        {
            if (r == null) continue;

            bool hasPrefab = r.prefab != null;
            bool hasTex = r.texture != null;

            if (!hasPrefab && !hasTex) continue;

            // ✅ 둘 다 있으면(사용자가 실수로 둘 다 넣는 경우 많음) Texture 우선 가능
            bool useTextureMode = hasTex && (!hasPrefab || preferTextureWhenProvided);

            var p = new DetailPrototype
            {
                healthyColor = r.healthyColor,
                dryColor = r.dryColor,
                minWidth = Mathf.Max(0.01f, r.minWidth),
                maxWidth = Mathf.Max(0.01f, r.maxWidth),
                minHeight = Mathf.Max(0.01f, r.minHeight),
                maxHeight = Mathf.Max(0.01f, r.maxHeight),
            };

            if (useTextureMode)
            {
                // Texture (billboard) detail
                p.usePrototypeMesh = false;
                p.prototype = null;
                p.prototypeTexture = r.texture;
                p.renderMode = DetailRenderMode.GrassBillboard;
            }
            else
            {
                // Prefab (mesh) detail
                p.usePrototypeMesh = true;
                p.prototype = r.prefab;

                if (usePrefabAlbedoAsPrototypeTexture)
                {
                    var texPick = TryGetAlbedoTextureSmart(r.prefab);
                    p.prototypeTexture = texPick.texture;

                    if (logPrefabExtraction)
                    {
                        if (p.prototypeTexture != null)
                            Debug.Log($"[TerrainDetailModule] Prefab '{r.prefab.name}' albedo picked: '{p.prototypeTexture.name}' (via {texPick.source}/{texPick.property})", r.prefab);
                        else
                            Debug.LogWarning($"[TerrainDetailModule] Prefab '{r.prefab.name}' has no albedo texture. It may render white in Terrain Detail.", r.prefab);
                    }
                }
                else
                {
                    p.prototypeTexture = null;
                }

                // 텍스처가 없으면 색으로라도 틴트(흰 덩어리 방지용)
                if (p.prototypeTexture == null && autoTintFromPrefabMaterialColor)
                {
                    var cPick = TryGetMaterialBaseColor(r.prefab);
                    if (cPick.hasColor)
                    {
                        // 룰 색이 기본(흰색)일 때만 자동 적용
                        if (r.healthyColor == Color.white && r.dryColor == Color.white)
                        {
                            p.healthyColor = cPick.color;
                            p.dryColor = cPick.color;

                            if (logPrefabExtraction)
                                Debug.Log($"[TerrainDetailModule] Prefab '{r.prefab.name}' tint picked: {cPick.color} (prop: {cPick.property})", r.prefab);
                        }
                    }
                }

                // Prefab은 VertexLit이 제일 덜 깨짐
                if (forcePrefabRenderModeVertexLit)
                    p.renderMode = DetailRenderMode.VertexLit;
                else
                    p.renderMode = (r.renderMode == DetailRenderMode.GrassBillboard) ? DetailRenderMode.VertexLit : r.renderMode;
            }

            prototypes.Add(p);
            activeRules.Add(r);
        }

        td.detailPrototypes = prototypes.ToArray();
        if (td.detailPrototypes == null || td.detailPrototypes.Length == 0) return;

        // 3) Clear layers
        for (int i = 0; i < td.detailPrototypes.Length; i++)
            td.SetDetailLayer(0, 0, i, new int[res, res]);

        // 4) Fill layers
        uint baseSeed = (uint)seed;

        for (int layer = 0; layer < activeRules.Count; layer++)
        {
            var r = activeRules[layer];
            var map = new int[res, res];

            float ox = Hash01(baseSeed, (uint)(layer * 92821 + 11)) * 1000f;
            float oy = Hash01(baseSeed, (uint)(layer * 92821 + 97)) * 1000f;
            float wx = Hash01(baseSeed, (uint)(layer * 92821 + 201)) * 1000f;
            float wy = Hash01(baseSeed, (uint)(layer * 92821 + 301)) * 1000f;

            float ns = Mathf.Max(0.01f, r.noiseScale);
            float sharp = Mathf.Max(0.01f, r.clumpSharpness);
            float covThr = 1f - Mathf.Clamp01(r.coverage);

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float u = (res == 1) ? 0f : x / (float)(res - 1);
                float v = (res == 1) ? 0f : y / (float)(res - 1);

                // edge warp
                if (r.edgeWarp > 0f)
                {
                    float w1 = Mathf.PerlinNoise(u * ns * 0.35f + wx, v * ns * 0.35f + wy) - 0.5f;
                    float w2 = Mathf.PerlinNoise(u * ns * 0.35f + wy, v * ns * 0.35f + wx) - 0.5f;
                    float amp = r.edgeWarp * 0.02f;
                    u = Mathf.Clamp01(u + w1 * amp);
                    v = Mathf.Clamp01(v + w2 * amp);
                }

                // height gating
                float worldH = td.GetInterpolatedHeight(u, v);
                float h01 = worldH / Mathf.Max(0.0001f, td.size.y);
                if (h01 < r.minHeight01 || h01 > r.maxHeight01) continue;

                // slope gating
                Vector3 nrm = td.GetInterpolatedNormal(u, v);
                float slope01 = Mathf.Clamp01(Vector3.Angle(nrm, Vector3.up) / 90f);
                if (slope01 > r.maxSlope01) continue;

                // avoid rock mask
                if (r.avoidRockMask && featureModule != null)
                {
                    float rock = featureModule.SampleRockMask01(u, v) * Mathf.Max(0f, r.rockMaskBlock);
                    if (rock > r.rockMaskThreshold) continue;
                }

                // clump mask
                float n = Mathf.PerlinNoise(u * ns + ox, v * ns + oy);
                float m = Mathf.InverseLerp(covThr, 1f, n);
                m = Mathf.Clamp01(m);
                m = Mathf.Pow(m, sharp);

                // spawn chance
                if (r.spawnChance < 1f)
                {
                    float gate = Hash01((uint)(x + y * 1315423911), (uint)(seed ^ (layer * 2654435761)));
                    if (gate > r.spawnChance) m = 0f;
                }

                int val = Mathf.RoundToInt(m * Mathf.Max(0, r.maxDensity));
                if (val <= 0) continue;

                map[y, x] = val;
            }

            td.SetDetailLayer(0, 0, layer, map);
        }

        terrain.Flush();
    }

    // ---------------- Prefab extraction ----------------

    private struct TexPick
    {
        public Texture2D texture;
        public string property;
        public string source; // "MaterialProp" or "Dependencies"
    }

    private static TexPick TryGetAlbedoTextureSmart(GameObject prefab)
    {
        var fromMat = TryGetAlbedoTextureFromMaterials(prefab);
        if (fromMat.texture != null) return fromMat;

#if UNITY_EDITOR
        var fromDep = TryGetAlbedoTextureFromDependencies(prefab);
        if (fromDep.texture != null) return fromDep;
#endif

        return new TexPick { texture = null, property = "not found", source = "none" };
    }

    private static TexPick TryGetAlbedoTextureFromMaterials(GameObject prefab)
    {
        if (!prefab) return new TexPick { texture = null, property = "none", source = "MaterialProp" };

        var rend = prefab.GetComponentInChildren<Renderer>(true);
        if (!rend) return new TexPick { texture = null, property = "no renderer", source = "MaterialProp" };

        var mats = rend.sharedMaterials;
        if (mats == null || mats.Length == 0) return new TexPick { texture = null, property = "no material", source = "MaterialProp" };

        // 우선순위
        string[] priority =
        {
            "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo",
            "_BaseTexture", "_Diffuse", "_ColorMap"
        };

        foreach (var mat in mats)
        {
            if (!mat) continue;

            foreach (var p in priority)
            {
                if (!mat.HasProperty(p)) continue;
                var t = mat.GetTexture(p) as Texture2D;
                if (t != null) return new TexPick { texture = t, property = p, source = "MaterialProp" };
            }

            // 모든 텍스처 프로퍼티 스캔(노출된 것만 나옴)
            var props = mat.GetTexturePropertyNames();
            if (props == null) continue;

            foreach (var p in props)
            {
                var low = p.ToLowerInvariant();
                if (!(low.Contains("base") || low.Contains("albedo") || low.Contains("diff") || low.Contains("main") || low.Contains("color")))
                    continue;

                var t = mat.GetTexture(p) as Texture2D;
                if (t != null) return new TexPick { texture = t, property = p, source = "MaterialProp" };
            }
        }

        return new TexPick { texture = null, property = "not found", source = "MaterialProp" };
    }

#if UNITY_EDITOR
    private static TexPick TryGetAlbedoTextureFromDependencies(GameObject prefab)
    {
        if (!prefab) return new TexPick { texture = null, property = "none", source = "Dependencies" };

        var path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path)) return new TexPick { texture = null, property = "no asset path", source = "Dependencies" };

        var deps = AssetDatabase.GetDependencies(path, true);
        if (deps == null || deps.Length == 0) return new TexPick { texture = null, property = "no deps", source = "Dependencies" };

        Texture2D best = null;
        int bestScore = int.MinValue;
        string bestWhy = "";

        foreach (var d in deps)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(d);
            if (!tex) continue;

            string n = tex.name.ToLowerInvariant();
            int score = 0;

            // 강한 제외(노말/마스크류)
            if (n.Contains("normal") || n.Contains("_n") || n.Contains("nrm")) score -= 1000;
            if (n.Contains("mask") || n.Contains("metal") || n.Contains("rough") || n.Contains("ao") || n.Contains("occl") || n.Contains("height") || n.Contains("spec")) score -= 800;

            // 알베도/컬러 힌트
            if (n.Contains("albedo") || n.Contains("basecolor") || n.Contains("diffuse") || n.Contains("color")) score += 200;
            if (n.Contains("grass") || n.Contains("leaf") || n.Contains("foliage")) score += 60;

            // 임포터 힌트
            var imp = AssetImporter.GetAtPath(d) as TextureImporter;
            if (imp != null)
            {
                if (imp.textureType == TextureImporterType.NormalMap) score -= 1000;
                if (imp.DoesSourceTextureHaveAlpha()) score += 80;
                if (imp.alphaIsTransparency) score += 40;
            }

            // 해상도 약간 가산
            score += Mathf.Clamp(Mathf.Max(tex.width, tex.height) / 256, 0, 10);

            if (score > bestScore)
            {
                bestScore = score;
                best = tex;
                bestWhy = d;
            }
        }

        if (best != null)
            return new TexPick { texture = best, property = bestWhy, source = "Dependencies" };

        return new TexPick { texture = null, property = "not found", source = "Dependencies" };
    }
#endif

    private struct ColorPick
    {
        public bool hasColor;
        public Color color;
        public string property;
    }

    private static ColorPick TryGetMaterialBaseColor(GameObject prefab)
    {
        if (!prefab) return new ColorPick { hasColor = false, color = Color.white, property = "none" };

        var rend = prefab.GetComponentInChildren<Renderer>(true);
        if (!rend) return new ColorPick { hasColor = false, color = Color.white, property = "no renderer" };

        var mat = rend.sharedMaterial;
        if (!mat) return new ColorPick { hasColor = false, color = Color.white, property = "no material" };

        string[] props = { "_BaseColor", "_Color", "_TintColor", "_AlbedoColor" };

        foreach (var p in props)
        {
            if (!mat.HasProperty(p)) continue;
            return new ColorPick { hasColor = true, color = mat.GetColor(p), property = p };
        }

        return new ColorPick { hasColor = false, color = Color.white, property = "not found" };
    }

    private static float Hash01(uint a, uint b)
    {
        uint x = a * 374761393u + b * 668265263u;
        x = (x ^ (x >> 13)) * 1274126177u;
        x ^= (x >> 16);
        return (x & 0x00FFFFFFu) / 16777215f;
    }
}
