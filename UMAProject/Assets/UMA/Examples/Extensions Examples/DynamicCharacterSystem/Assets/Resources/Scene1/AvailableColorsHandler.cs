using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA;
using UnityEngine.UI;

namespace UMA.CharacterSystem.Examples
{
    public class AvailableColorsHandler : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;

        // List<OverlayColorData> Colors = new List<OverlayColorData>();
        public SharedColorTable Colors;
        public GameObject ColorPanel;
        public GameObject ColorButtonPrefab;
        public string ColorName;
        public GameObject LabelPrefab;

        public void Setup(DynamicCharacterAvatar avatar, string colorName, GameObject colorPanel, SharedColorTable colorTable)
        {
            ColorName = colorName;
            Avatar = avatar;
            ColorPanel = colorPanel;
            Colors = colorTable;
        }

    /*  public OverlayColorData GetColor(Color c, Color additive)
        {
            OverlayColorData ocd = new OverlayColorData(3);
            ocd.channelMask[0] = c;
            ocd.channelAdditiveMask[0] = additive;
            return ocd;
        }*/

        public void OnClick()
        {
            Cleanup();

            AddLabel(ColorName);
            foreach(OverlayColorData ocd in Colors.colors)
            {
                AddButton(ocd);
            }
        }

        private void AddLabel(string theText)
        {
            GameObject go = GameObject.Instantiate(LabelPrefab);
            go.transform.SetParent(ColorPanel.transform);
            Text txt = go.GetComponentInChildren<Text>();
            txt.text = theText;
        }

        private void AddButton(OverlayColorData ocd)
        {
            GameObject go = GameObject.Instantiate(ColorButtonPrefab);
            ColorHandler ch = go.GetComponent<ColorHandler>();
            ch.Setup(Avatar,ColorName, ocd );
            Image i = go.GetComponent<Image>();
            i.color = ocd.color;
            go.transform.SetParent(ColorPanel.transform);
        }

        private void Cleanup()
        {
            foreach (Transform t in ColorPanel.transform)
            {
                UMAUtils.DestroySceneObject(t.gameObject);
            }
        }
    }
}
