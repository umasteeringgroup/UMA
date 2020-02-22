using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UnityEngine.UI;

namespace UMA
{
    public class OverlayViewer : MonoBehaviour
    {
        public TextureMerge TextureMergePrefab;
        public SlotDataAsset SlotDataAsset;
        public OverlayDataAsset BaseOverlay;
        public List<OverlayDataAsset> Overlays = new List<OverlayDataAsset>();
        public RawImage ImageViewer;

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