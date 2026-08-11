#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace UMA.Editors
{
	public static class TPoseExtracter
	{
	    [MenuItem("CONTEXT/Animator/Extract UMA T-Pose", priority = 30)]
	    static void ExtractTPoseFromAnimatorContext(MenuCommand command)
	    {
			var animator = command.context as Animator;
			if (animator == null)
			{
				return;
			}
			string assetPath = AssetDatabase.GetAssetPath(animator.gameObject);
			ExtractTPoseFromAnimator(animator, assetPath);
	    }

	    [MenuItem("CONTEXT/Animator/Extract UMA T-Pose", true)]
	    static bool ExtractTPoseFromAnimatorContext_Validate(MenuCommand command)
	    {
			return command.context is Animator;
	    }

	    [MenuItem("CONTEXT/UMAAvatarBase/Extract UMA T-Pose", priority = 30)]
	    static void ExtractTPoseFromAvatarContext(MenuCommand command)
	    {
			var avatar = command.context as UMAAvatarBase;
			if (avatar == null)
			{
				return;
			}
			var animator = avatar.GetComponentInChildren<Animator>();
			if (animator == null)
			{
				EditorUtility.DisplayDialog("Extract T-Pose", "No Animator found under the Avatar.", "OK");
				return;
			}
			string assetPath = AssetDatabase.GetAssetPath(avatar.gameObject);
			ExtractTPoseFromAnimator(animator, assetPath);
	    }

	    [MenuItem("CONTEXT/UMAAvatarBase/Extract UMA T-Pose", true)]
	    static bool ExtractTPoseFromAvatarContext_Validate(MenuCommand command)
	    {
			return command.context is UMAAvatarBase;
	    }

	    [MenuItem("UMA/Tools/Pose Tools/Extract T-Pose", priority = 130)]
	    static void ExtractTPose()
	    {
			TryExtractSelectedTPose();
	    }

		public static bool TryExtractSelectedTPose()
		{
			var selectedObjects = Selection.objects;
			if (selectedObjects.Length > 0)
			{
				bool extracted = false;
				foreach (var selectedObject in selectedObjects)
				{
					var assetPath = AssetDatabase.GetAssetPath(selectedObject);

					if (!string.IsNullOrEmpty(assetPath))
					{
						// Get asset path directory
						var assetDirectory = new FileInfo(assetPath).Directory.FullName + Path.DirectorySeparatorChar + "TPoses";

						// Trim off the path at "Assets" to get the relative path to the assets directory
						assetDirectory = assetDirectory.Substring(assetDirectory.IndexOf("Assets"));

						var modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
						if( modelImporter != null )
						{
							var asset = UmaTPose.CreateInstance<UMA.UmaTPose>();
							asset.ReadFromHumanDescription(modelImporter.humanDescription);
							var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
							if (prefab != null)
							{
								var animator = prefab.GetComponentInChildren<Animator>();
								if (animator != null)
								{
									asset.ExtractHumanPoseFromAnimator(animator);
								}
							}
							var name = selectedObject.name;
							if (name.EndsWith("(Clone)"))
							{
								name = name.Substring(0, name.Length - 7);
								asset.boneInfo[0].name = name;
								asset.Serialize();
							}
							if (!Directory.Exists(assetDirectory))
                            {
                                Directory.CreateDirectory(assetDirectory);
                            }

                            try
                            {
                                AssetDatabase.CreateAsset(asset, assetDirectory + Path.DirectorySeparatorChar + name + "_TPose.asset");
                            }
                            catch (UnityException e)
                            {
                                Debug.Log(e.ToString());
                            }
							extracted = true;
						}
					}
				}
				if (extracted)
				{
					AssetDatabase.SaveAssets();
					return true;
				}
			}

			/*
	        foreach (var animator in Transform.FindObjectsOfType(typeof(Animator)) as Animator[])
	        {
	            var asset = UmaTPose.CreateInstance<UmaTPose>();
	            asset.ReadFromTransform(animator);
	            var name = animator.name;
	            if (name.EndsWith("(Clone)"))
	            {
	                name = name.Substring(0, name.Length - 7);
	                asset.boneInfo[0].name = name;
	                asset.Serialize();
	            }

			// Default path
			string path = UMAPathUtility.GeneratedTPosesRoot;

			string[] inds = AssetDatabase.FindAssets("AssetIndexer t:umaassetindexer");
			if (inds.Length > 0)
			{
				// If UMA has moved, then move the pose path also.
				string tpath = AssetDatabase.GUIDToAssetPath(inds[0]);
				int pos = tpath.IndexOf("UMA/InternalDataStore", System.StringComparison.OrdinalIgnoreCase);
				string UMABase = tpath.Substring(0, pos) + "/UMA";
				path = UMABase + "Content/Generated/TPoses";
			}


	            if (!Directory.Exists(path))
	            {
	                Directory.CreateDirectory(path);
	            }

	            AssetDatabase.CreateAsset(asset, path+"/" + name + "_TPose.asset");
	            EditorUtility.SetDirty(asset);
	            AssetDatabase.SaveAssets();
	        }*/
			return false;
	    }

		private static void ExtractTPoseFromAnimator(Animator animator, string assetPath)
		{
			if (animator == null)
			{
				return;
			}

			var asset = UmaTPose.CreateInstance<UMA.UmaTPose>();
			asset.ReadFromTransform(animator);
			string name = animator.gameObject.name;
			if (name.EndsWith("(Clone)"))
			{
				name = name.Substring(0, name.Length - 7);
				asset.boneInfo[0].name = name;
				asset.Serialize();
			}

			string path = UMAPathUtility.GeneratedTPosesRoot;
			if (!string.IsNullOrEmpty(assetPath))
			{
				var assetDirectory = new FileInfo(assetPath).Directory.FullName + Path.DirectorySeparatorChar + "TPoses";
				assetDirectory = assetDirectory.Substring(assetDirectory.IndexOf("Assets"));
				path = assetDirectory;
			}

			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
			}

			string outputPath = path + Path.DirectorySeparatorChar + name + "_TPose.asset";
			try
			{
				AssetDatabase.CreateAsset(asset, outputPath);
				AssetDatabase.SaveAssets();
				EditorUtility.SetDirty(asset);
			}
			catch (UnityException e)
			{
				Debug.Log(e.ToString());
			}
		}
	}
}
#endif
