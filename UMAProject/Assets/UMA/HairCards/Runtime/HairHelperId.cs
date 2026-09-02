using UnityEngine;

namespace UMA.HairCards.Runtime
{
    /// <summary>Stable serialized identity for scene objects used by a HairGroomAsset helper binding.</summary>
    [DisallowMultipleComponent]
    public sealed class HairHelperId : MonoBehaviour
    {
        [SerializeField] private string helperId;
        public string Id => helperId;

        private void Awake()
        {
            HairStableId.Ensure(ref helperId);
        }

        private void OnValidate()
        {
            HairStableId.Ensure(ref helperId);
        }

        [ContextMenu("Create New Hair Helper ID")]
        public void CreateNewId()
        {
            helperId = HairStableId.Create();
        }
    }
}
