using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Simple wrapper ScriptableObject with Binary serialization set that other objects (like Mesh) can be added to to force binary serialization.
    /// See AssetDatabase.AddObjectToAsset()
    /// </summary>
    [System.Serializable]
    [PreferBinarySerialization]
    public class BinaryAssetWrapper : ScriptableObject
    {
    }
}
