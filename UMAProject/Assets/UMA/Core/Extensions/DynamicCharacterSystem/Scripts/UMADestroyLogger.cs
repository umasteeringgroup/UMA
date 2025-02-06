using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UMADestroyLogger : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnDestroy()
    {
        Debug.Log($"This game object {gameObject.name} was destroyed!");
    }
}
