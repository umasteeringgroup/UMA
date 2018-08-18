using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class DynamicColorList : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;

        public GameObject colorPrefab;
        public SharedColorTable colorList;

        // Use this for initialization
        public void Initialize(string colorType)
        {
            if (colorList == null)
                return;

            foreach (OverlayColorData colorData in colorList.colors)
            {
                GameObject prefab = Instantiate(colorPrefab);
                prefab.transform.SetParent(gameObject.transform);
                prefab.GetComponent<Button>().image.color = colorData.color;

                SetNetworkColor nc = prefab.GetComponent<SetNetworkColor>();
                nc.avatar = avatar;
                nc.ColorType = colorType;
            }
        }
    }
}
