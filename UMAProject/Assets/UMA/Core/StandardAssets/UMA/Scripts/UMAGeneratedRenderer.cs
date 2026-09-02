using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Marks a renderer GameObject as generated and owned by UMA. The marker
    /// lets UMA distinguish its output from user-authored renderers when play
    /// mode is entered without reloading the domain or scene.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class UMAGeneratedRenderer : MonoBehaviour
    {
    }
}
