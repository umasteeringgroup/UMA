namespace UMA
{
    /// <summary>
    /// Marks a Scene view stage as a self-contained UMA editing tool, such as the Vertex
    /// Editor, the face (MeshHideAsset) editor, the Overlay Painter or the Hair Card editor.
    /// The UMA Toolbar hides itself while one of these stages is open: its commands act on
    /// the UMA characters in the main scene, not on the stage's preview model.
    /// </summary>
    public interface IUMAEditingStage
    {
    }
}
