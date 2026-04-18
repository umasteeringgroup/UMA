using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class UMADestroyLogger : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnDestroy()
        {
            Debug.Log($"This game object {gameObject.name} was destroyed!");
        }
#endif
    }
}