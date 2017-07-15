using UnityEngine;
using System.Collections;

public class CollisionMatrixFixer : MonoBehaviour 
{
    private int _defaultRagdollLayer = 8;

	// Use this for initialization
	void Start () 
    {
        for (int i = 8; i < 32; i++)
        {
            if( i != _defaultRagdollLayer )
                Physics.IgnoreLayerCollision(_defaultRagdollLayer, i, true);
        }	
        Physics.IgnoreLayerCollision(_defaultRagdollLayer, _defaultRagdollLayer, false);
	}
}
