using UnityEngine;
using System.Collections;

// @cond doxygen ignore
namespace UMA.Examples
{
	public class CollisionMatrixFixer : MonoBehaviour 
	{
		const int _defaultRagdollLayer = 8;

		public static void FixLayers()
		{
			for (int i = 8; i < 32; i++)
			{
				if (i != _defaultRagdollLayer)
					Physics.IgnoreLayerCollision(_defaultRagdollLayer, i, true);
			}
			Physics.IgnoreLayerCollision(_defaultRagdollLayer, _defaultRagdollLayer, false);
		}

		// Use this for initialization
		void Start ()
		{ 
			FixLayers();
		}
	}
}
// @endcond
