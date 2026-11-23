using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// What Unity's Undo records for the cloth itself: the particle positions and the simulation time.
    /// One grab is one Undo step; undoing it puts the cloth back where it was before the drag.
    /// </summary>
    public sealed class ClothesDesignerUndoState : ScriptableObject
    {
        public Vector3[] positions = new Vector3[0];
        public float simTime;
        /// <summary>Changes with every recorded step, to notice that Undo or Redo touched this state.</summary>
        public int version;
        /// <summary>
        /// The assets the designer has open. Kept here, on an object Unity never unloads, so that UMA
        /// unloading unused assets while it builds a character cannot drop them from under the designer.
        /// </summary>
        public ClothesPattern patternAsset;
        public ClothesFit fitAsset;
    }
}
