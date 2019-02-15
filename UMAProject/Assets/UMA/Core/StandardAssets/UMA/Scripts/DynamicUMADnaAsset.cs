using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    [System.Serializable]
    public class DynamicUMADnaAsset : ScriptableObject, INameProvider
    {
		public int dnaTypeHash;

		//because this asset is now responsible for setting the dnaTypeHash (which UMA uses to define the DNA Type) 
		//we need to ensure this typeHash is unique if we can and that it gets changed to a new one if the user duplicates
		//a dna asset in the editor
		[SerializeField]
		protected string lastKnownAssetPath = "";
		[SerializeField]
		protected string lastKnownDuplicateAssetPath = "";
		[SerializeField]
		protected int lastKnownInstanceID = 0;

        #region INameProvider
        public string GetAssetName()
        {
            return this.name;
        }
        public int GetNameHash()
        {
            return 0;
        }
        #endregion
        //falls back to UMADnaHumanoid
        public string[] Names = {};

        public DynamicUMADnaAsset() {
			dnaTypeHash = GenerateUniqueDnaTypeHash();
        }
		//do we need to use something more robust here?
		public static int GenerateUniqueDnaTypeHash()
		{
			System.Random random = new System.Random();
			int i = random.Next();
			return i;
		}
		#region FILE HANDLING
#if UNITY_EDITOR
		/// <summary>
		/// Method to update the dnaTypeHash of a DynamicUMADnaAsset when the user duplicates an asset. 
		/// Not a full solution because assets outside of Unity will still have the same typeHash
		/// </summary>
		public bool SetCurrentAssetPath()
		{
			bool DoUpdate = false;
			//if the asset never had a typeHash genrated make one
			if (dnaTypeHash == 0)
			{
				dnaTypeHash = GenerateUniqueDnaTypeHash();
				DoUpdate = true;
			}
			var currentAssetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
			//if this asset has never known is last location its a new one so set these
			if (lastKnownAssetPath == "" || lastKnownDuplicateAssetPath == "")
			{
				if (lastKnownAssetPath == "")
				{
					lastKnownAssetPath = currentAssetPath;
					DoUpdate = true;
				}
				if (this.lastKnownDuplicateAssetPath == "")
				{
					lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
					DoUpdate = true;
				}
			}
			else
			{
				//the user has changed the file name. Dont update the ID but DO update the lastKnown paths
				if (currentAssetPath != lastKnownAssetPath && currentAssetPath != lastKnownDuplicateAssetPath)
				{
					lastKnownAssetPath = currentAssetPath;
					lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
					lastKnownInstanceID = GetInstanceID();
					DoUpdate = true;
				}
				//The user has duplicated the file so update the paths AND the ID
				else if (currentAssetPath != lastKnownAssetPath && currentAssetPath == lastKnownDuplicateAssetPath)
				{
					if (GetInstanceID() == lastKnownInstanceID)
					{
						lastKnownAssetPath = currentAssetPath;
						lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
					}
					else
					{
						dnaTypeHash = GenerateUniqueDnaTypeHash();
						lastKnownAssetPath = currentAssetPath;
						lastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
						lastKnownInstanceID = GetInstanceID();
					}
					DoUpdate = true;
				}
				//Otherwise we dont need to change the ID but may need to update the last knows paths and save the asset (handled by the custom Editor)     
				else if (currentAssetPath == lastKnownAssetPath)
				{
					var updatedlastKnownDuplicateAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(currentAssetPath);
					if (updatedlastKnownDuplicateAssetPath != lastKnownDuplicateAssetPath)
					{
						lastKnownAssetPath = updatedlastKnownDuplicateAssetPath;
						DoUpdate = true;
					}
					var updatedlastKnownInstanceID = GetInstanceID();
					if (updatedlastKnownInstanceID != lastKnownInstanceID)
					{
						lastKnownInstanceID = updatedlastKnownInstanceID;
						DoUpdate = true;
					}
				}
			}
			if (DoUpdate)
			{
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
			}
			return DoUpdate;
		}


		[UnityEditor.MenuItem("Assets/Create/UMA/DNA/Dynamic DNA Asset")]
		public static void CreateDynamicUMADnaAsset()
		{
			CustomAssetUtility.CreateAsset<DynamicUMADnaAsset>();
		}
#endif
#endregion
	}
}
