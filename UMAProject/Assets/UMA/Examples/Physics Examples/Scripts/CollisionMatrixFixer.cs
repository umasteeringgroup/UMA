using UnityEngine;
using System.Collections;

// @cond doxygen ignore
namespace UMA.Examples
{
	public class CollisionMatrixFixer : MonoBehaviour 
	{
		static int _defaultRagdollLayer = 8;
		static int _noCollisionLayer = 10;

		public static void FixLayers()
		{
			for (int i = 8; i < 32; i++)
			{
				if (i != _defaultRagdollLayer)
					Physics.IgnoreLayerCollision(_defaultRagdollLayer, i, true);
				Physics.IgnoreLayerCollision(_noCollisionLayer, i, true);
			}
			Physics.IgnoreLayerCollision(_defaultRagdollLayer, _defaultRagdollLayer, false);
		}

		// Use this for initialization
		void Start ()
		{
			_defaultRagdollLayer = LayerMask.NameToLayer("Ragdoll");
			_noCollisionLayer = LayerMask.NameToLayer("NoCollisions");
			// if not found, use the defaults.
			if (_defaultRagdollLayer == -1) _defaultRagdollLayer = 8;
			if (_noCollisionLayer == -1) _noCollisionLayer = 10;
			FixLayers();
		}
	}
}
// @endcond
