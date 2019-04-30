using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UMAPostProcess : MonoBehaviour
{
    public Shader shader;

    Material material;

    void Awake()
    {
        material = new Material(shader);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, destination, material);
    }
}
