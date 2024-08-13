using UnityEngine;
using UnityEngine.UI;

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
            Button[] array = this.gameObject.transform.parent.GetComponentsInChildren<Button>();
            for (int i = 0; i < array.Length; i++)
            {
                Button but = array[i];
                but.interactable = false;
            }
            customizerScript.LoadListedFile(filename, filepath);
        }
    }
}
