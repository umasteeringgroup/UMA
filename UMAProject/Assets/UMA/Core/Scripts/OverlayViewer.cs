using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UnityEngine.UI;

namespace UMA
{
    /// <summary>
    /// This class is used by the OverlayEditor to set parameters for viewing overlays in the scene view.
    /// See the OverlayAligner scene for an example of both.
    /// </summary>
    public class OverlayViewer : MonoBehaviour
    {
        public TextureMerge TextureMergePrefab;
        public SlotDataAsset SlotDataAsset;
        public OverlayDataAsset BaseOverlay;
        public List<OverlayDataAsset> Overlays = new List<OverlayDataAsset>();
        public RawImage ImageViewer;
        public GameObject AnnoyingPanel;

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}