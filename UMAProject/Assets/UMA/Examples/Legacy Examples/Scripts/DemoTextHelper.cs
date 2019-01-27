using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Examples
{
    public class DemoTextHelper : MonoBehaviour
    {
        public GameObject Panel;

        public void Activate(bool active)
        {
            if(Panel != null)
            {
                Panel.SetActive(active);
            }
        }
    }
}
