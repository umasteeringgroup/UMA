using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SceneNote : MonoBehaviour
{
    private static readonly HashSet<SceneNote> activeNotes = new HashSet<SceneNote>();

    /// <summary>Active notes maintained without scanning every object in loaded scenes.</summary>
    public static IReadOnlyCollection<SceneNote> ActiveNotes => activeNotes;

    public string Title = "Note";
    [TextArea(3, 6)]
    public string Info = "Details...";
    public Color Color = Color.yellow;

    public Vector2 Size = new Vector2(200, 80);
    public Vector3 Offset = new Vector3(0, 2, 0);

    public bool Visible = true;

    private void OnEnable()
    {
        activeNotes.Add(this);
    }

    private void OnDisable()
    {
        activeNotes.Remove(this);
    }

    private void OnDestroy()
    {
        activeNotes.Remove(this);
    }
}
