using UnityEngine;

[ExecuteAlways]
public class SceneNote : MonoBehaviour
{
    public string Title = "Note";
    [TextArea(3, 6)]
    public string Info = "Details...";
    public Color Color = Color.yellow;

    public Vector2 Size = new Vector2(200, 80);
    public Vector3 Offset = new Vector3(0, 2, 0);

    public bool Visible = true;
}
