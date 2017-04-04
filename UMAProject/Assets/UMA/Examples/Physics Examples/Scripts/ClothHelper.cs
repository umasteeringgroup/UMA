using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Dynamics.Examples
{
	public class ClothHelper : MonoBehaviour
	{
		public float distance = 0.0f;
		public float penetration = 10.0f;
		public float distanceMax = 0.0f;
		public float penetrationMax = 10.0f;
		public Texture2D clothWeightMap;

		[HideInInspector]
		public bool drawFlag = true;
		[HideInInspector]
		public Dictionary<Vector3, int> clothVerts = new Dictionary<Vector3, int>();

		private Cloth m_Cloth;
		private float m_CubeLen = 0.01f;

		// Use this for initialization
		void Start () 
		{
			m_Cloth = gameObject.GetComponent<Cloth> ();	
		}

		void OnDrawGizmos()
		{
			if (drawFlag) 
			{
				Vector3 size = new Vector3 (m_CubeLen, m_CubeLen, m_CubeLen);
				Gizmos.color = new Color (1, 0, 0, 1);
				if (m_Cloth == null) 
				{
					m_Cloth = gameObject.GetComponent<Cloth> ();	
				}

				if (m_Cloth != null) 
				{
					/*for (int i = 0; i < m_Cloth.vertices.Length; i++) 
					{
						Gizmos.DrawCube (m_Cloth.vertices [i], size);
					}*/
					foreach( KeyValuePair<Vector3, int> item in clothVerts )
					{
						Gizmos.DrawCube (item.Key, size);
					}
				}
			}
		}

		public void SetAllClothContraints()
		{
			Debug.Log ("Setting All Cloth Constraints");

			if (m_Cloth == null) 
			{
				Debug.LogError ("No Cloth component found!");
				return;
			}

			ClothSkinningCoefficient[] newConstraints = new ClothSkinningCoefficient[m_Cloth.coefficients.Length];
			for (int i = 0; i < m_Cloth.coefficients.Length; i++) 
			{
				newConstraints [i].maxDistance = distance;
				newConstraints [i].collisionSphereDistance = penetration;
			}
			m_Cloth.coefficients = newConstraints;
		}
	}
}
