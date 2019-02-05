using UnityEngine;

public class TextureBliter
{
    public static void BlitRect(Rect rect, RenderTexture target, Texture texture, Material material = null)
    {
        RenderTexture backup = RenderTexture.active;
        RenderTexture.active = target;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, target.width, target.height, 0);
        Graphics.DrawTexture(rect, texture, material);
        GL.PopMatrix();
        RenderTexture.active = backup;
    }
}
