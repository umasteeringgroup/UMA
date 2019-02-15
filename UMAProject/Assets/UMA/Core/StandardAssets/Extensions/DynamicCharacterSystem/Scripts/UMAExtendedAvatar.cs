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
				if(!Application.isPlaying && previewMesh != null)
				{
					
					mat.SetPass(0);
					Graphics.DrawMeshNow(previewMesh, Matrix4x4.TRS(transform.position, transform.rotation * Quaternion.Euler(-90,180,0), new Vector3(0.88f,0.88f,0.88f)));	
				}
				lastPreviewModel = previewModel;
			}
		}
		
		void LoadMesh()
		{
			//search string finds both male and female!
			string[] assets = UnityEditor.AssetDatabase.FindAssets("t:Model Male_Unified");
			string male = "";
			string female = "";
			GameObject model = null;

			foreach(string guid in assets)
			{
				string thePath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
				if (thePath.ToLower().Contains("female"))
					female = thePath;
				else
					male = thePath;
			}

			if (previewModel == PreviewModel.Male)
			{
				if(!string.IsNullOrEmpty(male))
				{
					model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(male);
				}
				else
				{
					if(Debug.isDebugBuild)
						Debug.LogWarning("Could not load Male_Unified model for preview!");
				}
			}

            if (previewModel == PreviewModel.Female)
            {
                if (!string.IsNullOrEmpty(female))
                {
                    model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(female);
                }
                else
                {
                    if (Debug.isDebugBuild)
                        Debug.LogWarning("Could not load Female_Unified model for preview!");
                }
            }

			if (model != null)
				previewMesh = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
			else
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("Preview Model not found on object " + gameObject.name);
			}
		}
	#endif
	}
}
