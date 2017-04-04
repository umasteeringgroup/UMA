using UnityEngine;

namespace UMA.CharacterSystem
{
	public class UMAExtendedAvatar : UMADynamicAvatar 
	{
	#if UNITY_EDITOR	
		
		public bool showPlaceholder = true;
		private Mesh previewMesh;
		public enum PreviewModel {Male, Female}
		public PreviewModel previewModel;
		public Color previewColor = Color.grey;
		private PreviewModel lastPreviewModel;
		private Material mat;
	#endif
		
	// Draws Placeholder Model
	#if UNITY_EDITOR
		
		void OnDrawGizmos()
		{
			// Build Shader
			if (!mat)
			{
				Shader shader = Shader.Find ("Hidden/Internal-Colored");
				mat = new Material (shader);
				mat.hideFlags = HideFlags.HideAndDontSave;
			}
			
			if(showPlaceholder){
				// Check for mesh Change
				if(!previewMesh || lastPreviewModel != previewModel) LoadMesh();
				
				mat.color = previewColor;
				if(!Application.isPlaying)
				{
					
					mat.SetPass(0);
					Graphics.DrawMeshNow(previewMesh, Matrix4x4.TRS(transform.position, transform.rotation * Quaternion.Euler(-90,180,0), new Vector3(0.88f,0.88f,0.88f)));	
				}
				lastPreviewModel = previewModel;
			}
		}
		
		void LoadMesh()
		{
			string modelPath = "HumanMale/FBX/Male_Unified.fbx";
			if(previewModel == PreviewModel.Female) modelPath = "HumanFemale/FBX/Female_Unified.fbx";
			GameObject model = UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/UMA/Content/UMA/" + modelPath, typeof(GameObject)) as GameObject;
			previewMesh = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
		}
	#endif
	}
}
