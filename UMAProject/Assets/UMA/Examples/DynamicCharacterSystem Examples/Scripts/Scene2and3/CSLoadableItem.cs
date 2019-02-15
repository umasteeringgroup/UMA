using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace UMA.CharacterSystem.Examples
{
    public class CSLoadableItem : MonoBehaviour
    {
        public TestCustomizerDD customizerScript;
        public string filename = "";
        public string filepath = "";

        public void loadThisFile()
        {
            //make sure no others are clicked...
            foreach(Button but in this.gameObject.transform.parent.GetComponentsInChildren<Button>())
            {
                but.interactable = false;
            }
            customizerScript.LoadListedFile(filename, filepath);
        }
    }
}
