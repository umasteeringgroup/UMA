using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class TextureMerge : MonoBehaviour {

		public Camera myCamera;
		public Material material;
		public Transform textureModule;
		public Transform myTransform;
		public List<Transform> textureModuleList;
		
		void Awake () {
			textureModuleList = new List<Transform>();
		}
	}
}