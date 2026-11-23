using UnityEngine;

namespace UMA
{

    public class DestroyDebugger : MonoBehaviour
    {
        private void OnDestroy()
        {
            Debug.Log("Destroying " + gameObject.name);
        }
        private void OnDisable()
        {
            Debug.Log("Disabling " + gameObject.name);
        }
    }
}
