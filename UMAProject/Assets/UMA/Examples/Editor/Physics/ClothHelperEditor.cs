using UnityEngine;
using UnityEditor;

namespace UMA.Dynamics.Examples
{
    [CustomEditor(typeof(ClothHelper))]
	public class ClothHelperEditor : Editor
	{

		ClothHelper m_ClothHelper;
		Cloth m_Cloth;

		void OnEnable()
		{
			m_ClothHelper = (ClothHelper)target;
			m_Cloth = m_ClothHelper.gameObject.GetComponent<Cloth> ();
		}

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector ();

			if( m_Cloth != null )
            {
                EditorGUILayout.LabelField( "Number of Constraints: ", m_Cloth.coefficients.Length.ToString() );
            }

            m_ClothHelper.drawFlag = EditorGUILayout.Toggle ( "Display Constraints", m_ClothHelper.drawFlag);

			if (GUILayout.Button ("Set Default Constraints")) 
			{
				m_ClothHelper.SetAllClothContraints ();
			}
			if (GUILayout.Button ("Set Constraints From Weight Map")) 
			{
				SetClothWeightMap ();
				EditorUtility.ClearProgressBar ();
			}
		}

		private void SetClothWeightMap()
		{
			if (m_ClothHelper.clothWeightMap == null) 
			{
				Debug.Log ("No clothWeightMap set!");
				return;
			}

			SkinnedMeshRenderer renderer = m_ClothHelper.gameObject.GetComponent<SkinnedMeshRenderer> ();
			Cloth cloth = m_ClothHelper.gameObject.GetComponent<Cloth> ();

			if (renderer == null) 
			{
				Debug.Log ("No skinnedMeshRenderer found!");
				return;
			}

			if (cloth == null) 
			{
				Debug.LogError ("No Cloth component found!");
				return;
			}

			//m_ClothHelper.SetAllClothContraints ();
			Debug.Log("Number of Contraints: " + cloth.coefficients.Length.ToString() );
		

			m_ClothHelper.clothVerts.Clear ();
			for(int i = 0; i < cloth.vertices.Length; i++)
			{
				Vector3 key = cloth.vertices [i];
				if (renderer.rootBone != null) 
				{
					//
					//key = key + renderer.rootBone.localPosition;
					//key = RotatePointAroundPivot (key, new Vector3 (), renderer.rootBone.eulerAngles);
					key = renderer.rootBone.InverseTransformPoint (key);

					//
				}

				if (!m_ClothHelper.clothVerts.ContainsKey ( key ))
                {
                    m_ClothHelper.clothVerts.Add ( key, i);
                }
            }

			int matchCount = 0;
			ClothSkinningCoefficient[] newConstraints = new ClothSkinningCoefficient[cloth.coefficients.Length];

			for (int i = 0; i < renderer.sharedMesh.vertexCount; i++) 
			{
				EditorUtility.DisplayProgressBar ("Painting Cloth Weights", "", (float)(i / renderer.sharedMesh.vertexCount) );

				Vector3 rVertex = renderer.sharedMesh.vertices [i];

				if (m_ClothHelper.clothVerts.ContainsKey (rVertex)) 
				{
					matchCount++;
					int clothIndex = m_ClothHelper.clothVerts[rVertex];

					int x = (int)(renderer.sharedMesh.uv [i].x * m_ClothHelper.clothWeightMap.width);
					int y = (int)(renderer.sharedMesh.uv [i].y * m_ClothHelper.clothWeightMap.height);
					Color c = m_ClothHelper.clothWeightMap.GetPixel (x, y);

					float distWeight = c.r * m_ClothHelper.distanceMax;
					float penetrationWeight = c.g * m_ClothHelper.penetrationMax;

					newConstraints [clothIndex].maxDistance = distWeight;
					newConstraints [clothIndex].collisionSphereDistance = penetrationWeight;
				}
			}
			Debug.Log (matchCount + " Vertices matched!");
			cloth.coefficients = newConstraints;

			EditorUtility.ClearProgressBar ();
		}


		public Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Vector3 angles) {
			Vector3 dir = point - pivot; // get point direction relative to pivot
			dir = Quaternion.Euler(angles) * dir; // rotate it
			point = dir + pivot; // calculate rotated point
			return point; // return it
		}
	}
}
