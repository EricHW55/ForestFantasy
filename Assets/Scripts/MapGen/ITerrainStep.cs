public interface ITerrainStep
{
    int Order { get; }
    void Apply(UnityEngine.Terrain terrain, int seed);
}
