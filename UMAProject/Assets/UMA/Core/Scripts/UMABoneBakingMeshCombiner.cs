namespace UMA
{
    /// <summary>
    /// Compatibility name for existing scenes and generator prefabs.
    /// The implementation now starts from the default mesh-combiner pipeline and
    /// adds bone baking in <see cref="UMADefaultBoneBakingMeshCombiner"/>.
    /// </summary>
    public class UMABoneBakingMeshCombiner : UMADefaultBoneBakingMeshCombiner
    {
    }
}
