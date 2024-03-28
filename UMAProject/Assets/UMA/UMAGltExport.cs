using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UMA_GLTF

using GLTFast;

public class UMAGltExport : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        GLTFast.Exporter.Exporter.Export(gameObject, "Assets/UMA/UMA_Gltf_Export");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
#endif