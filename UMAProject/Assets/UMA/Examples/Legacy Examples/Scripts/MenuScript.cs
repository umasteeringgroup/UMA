using UnityEngine;
using System.Collections;
using UnityEngine.Networking;

namespace UMA.Examples
{
    public class MenuScript : NetworkBehaviour
    {
        public static string umaString;

        private NetworkManager manager;

        //Setup reference to network manager
        private void Start()
        {
            manager = GameObject.Find("NetworkManager").GetComponent<NetworkManager>();
        }

        //Check if we klick on a generated UMA GameObject
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 100.0f))
                {
                    if (hit.collider.name == "Generated Character")
                    {
                        SaveAndStart(hit.collider.gameObject);
                    }
                }
            }
        }

        private void SaveAndStart(GameObject selectedChar)
        {
            //Save the selected UMA into a static string so we can access it from the Game Scene
            UMAAvatarBase avatar = selectedChar.GetComponent<UMAAvatarBase>();
            UMATextRecipe asset = ScriptableObject.CreateInstance<UMATextRecipe>();
            asset.Save(avatar.umaData.umaRecipe, avatar.context);
            umaString = asset.recipeString;

            //Start network host
            manager.StartHost();
        }
    }
}

