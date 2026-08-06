namespace UMA
{
    /// <summary>
    /// Receives distance-zone changes from <see cref="Examples.UMASimpleLOD"/>.
    /// </summary>
    /// <remarks>
    /// Implement this interface on a MonoBehaviour and add that component to
    /// UMASimpleLOD.lodTunings. The callback is made whenever the LOD zone
    /// changes, even when the avatar has no slot or internal mesh for that LOD.
    /// </remarks>
    public interface ILODTuning
    {
        /// <summary>
        /// Called after the avatar enters a different LOD distance zone.
        /// LOD zero is the highest-detail zone.
        /// </summary>
        /// <param name="lodLevel">The new zero-based LOD zone.</param>
        void OnLODChanged(int lodLevel);
    }
}
