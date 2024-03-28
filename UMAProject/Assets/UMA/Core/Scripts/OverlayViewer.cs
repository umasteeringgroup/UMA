using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
#endif

namespace UMA
{
    /// <summary>
    /// This class is used by the OverlayEditor to set parameters for viewing overlays in the scene view.
    /// See the OverlayAligner scene for an example of both.
    /// </summary>
    [ExecuteInEditMode]
    public class OverlayViewer : MonoBehaviour
    {
        public TextureMerge TextureMergePrefab;
        public SlotDataAsset SlotDataAsset;
        public OverlayDataAsset BaseOverlay;
        public List<OverlayDataAsset> Overlays = new List<OverlayDataAsset>();
        public RawImage ImageViewer;
        public GameObject AnnoyingPanel;
#if UNITY_EDITOR
        private PopUpAssetInspector inspector;

        // Start is called before the first frame update
        void Start()
        {
            CheckInspector();
        }

        private void OnDestroy()
        {
            if (inspector != null)
            {
                inspector.Close();
                inspector = null;
            }
        }

        // Update is called once per frame
        void Update()
        {
            CheckInspector();
        }

        void CheckInspector()
        {
            if (inspector == null)
            {
                inspector = PopUpAssetInspector.Create(this);
            }
        }
#endif
    }
}
