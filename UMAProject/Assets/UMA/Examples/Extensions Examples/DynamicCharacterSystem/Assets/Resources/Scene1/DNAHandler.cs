using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UMA.CharacterSystem;

namespace UMA.CharacterSystem.Examples
{
    public class DNAHandler : MonoBehaviour
    {
        public GameObject SelectionPanel;
        public GameObject DnaPrefab;
        public GameObject LabelPrefab;

        DnaSetter DNA;
        DynamicCharacterAvatar Avatar;

        public void Setup(DynamicCharacterAvatar avatar, DnaSetter dna, GameObject panel)
        {
            Avatar = avatar;
            DNA = dna;
            SelectionPanel = panel;
        }

        /// <summary>
        /// 
        /// </summary>
        private void Cleanup()
        {
            if (SelectionPanel.transform.childCount > 0)
            {
                foreach (Transform t in SelectionPanel.transform)
                {
                    UMAUtils.DestroySceneObject(t.gameObject);
                }
            }
        }

        /// <summary>
        /// Button Handler
        /// </summary>
        public void OnClick()
        {
            Cleanup();
            // construct the label
            AddLabel(DNA.Name);

            // construct the slider
            GameObject go = GameObject.Instantiate(DnaPrefab);
            DNASliderHandler dsh = go.GetComponent<DNASliderHandler>();
            dsh.Setup(DNA, Avatar);
            go.transform.SetParent(SelectionPanel.transform);
        }

        private void AddLabel(string theText)
        {
            GameObject go = GameObject.Instantiate(LabelPrefab);
            go.transform.SetParent(SelectionPanel.transform);
            Text txt = go.GetComponentInChildren<Text>();
            txt.text = theText;
        }
    }
}
