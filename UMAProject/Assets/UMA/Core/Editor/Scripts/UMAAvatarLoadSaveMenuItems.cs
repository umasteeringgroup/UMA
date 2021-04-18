using UnityEngine;
using UnityEditor;
using UMA;
using UMA.CharacterSystem;
using System.Collections.Generic;
using System.IO;
using UMA.Examples;
using UMA.PoseTools;
using static UMA.UMAPackedRecipeBase;

namespace UMA.Editors
{
	public class UMAAvatarLoadSaveMenuItems : Editor
	{
		[UnityEditor.MenuItem("GameObject/UMA/Save Mecanim Avatar to Asset (runtime only)")]
		[MenuItem("UMA/Runtime/Save Selected Avatars Mecanim Avatar to Asset", priority = 1)]
		public static void SaveMecanimAvatar()
		{
			if (!Application.isPlaying)
			{
				EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
				return;
			}
			if (Selection.gameObjects.Length != 1)
			{
				EditorUtility.DisplayDialog("Notice", "Only one Avatar can be selected.", "OK");
				return;
			}

			var selectedTransform = Selection.gameObjects[0].transform;
			var avatar = selectedTransform.GetComponent<UMAAvatarBase>();

			if (avatar == null)
			{
				EditorUtility.DisplayDialog("Notice", "An Avatar must be selected to use this function", "OK");
				return;
			}

			if (avatar.umaData == null)
			{
				EditorUtility.DisplayDialog("Notice", "The Avatar must be constructed before using this function", "OK");
				return;
			}

			if (avatar.umaData.animator == null)
			{
				EditorUtility.DisplayDialog("Notice", "Animator has not been assigned!", "OK");
				return;
			}
			if (avatar.umaData.animator.avatar == null)
			{
				EditorUtility.DisplayDialog("Notice", "Mecanim avatar is null!", "OK");
				return;
			}

			string path = EditorUtility.SaveFilePanelInProject("Save avatar", "CreatedAvatar.asset", "asset", "Save the avatar");

			AssetDatabase.CreateAsset(avatar.umaData.animator.avatar, path);
			AssetDatabase.SaveAssets();

			EditorUtility.DisplayDialog("Saved", "Avatar save to assets as CreatedAvatar", "OK");
		}

		public static void ConvertToNonUMA(GameObject baseObject, UMAAvatarBase avatar, string Folder, bool ConvertNormalMaps, string CharName, bool AddStandaloneDNA)
		{
			Folder = Folder + "/" + CharName;

			if (!System.IO.Directory.Exists(Folder))
			{
				System.IO.Directory.CreateDirectory(Folder);
			}

			SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
			int meshno = 0;
			foreach (SkinnedMeshRenderer smr in renderers)
			{
				Material[] mats = smr.sharedMaterials;

				int Material = 0;
				foreach (Material m in mats)
				{
					// get each texture.
					// if the texture has been generated (has no path) then we need to convert to Texture2D (if needed) save that asset.
					// update the material with that material.
					List<Texture> allTexture = new List<Texture>();
					Shader shader = m.shader;
					for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
					{
						if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
						{
							string propertyName = ShaderUtil.GetPropertyName(shader, i);
							Texture texture = m.GetTexture(propertyName);
							if (texture is Texture2D || texture is RenderTexture)
							{
								bool isNormal = false;
								string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
								if (string.IsNullOrEmpty(path))
								{
									if (ConvertNormalMaps)
									{
										if (propertyName.ToLower().Contains("bumpmap") || propertyName.ToLower().Contains("normal"))
										{
											// texture = ConvertNormalMap(texture);
											texture = sconvertNormalMap(texture);
											isNormal = true;
										}
									}
									string texName = Path.Combine(Folder, CharName + "_Mat_" + Material + propertyName + ".png");
									SaveTexture(texture, texName);
									AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
									if (isNormal)
                                    {
										TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(texName);
										importer.isReadable = true;
										importer.textureType = TextureImporterType.NormalMap;
										importer.maxTextureSize = 1024; // or whatever
										importer.textureCompression = TextureImporterCompression.CompressedHQ;
										EditorUtility.SetDirty(importer);
										importer.SaveAndReimport();
									}

									Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CustomAssetUtility.UnityFriendlyPath(texName));
									m.SetTexture(propertyName, tex);
								}
							}
						}
					}
					string matname = Folder + "/"+CharName+"_Mat_" + Material + ".mat";
					CustomAssetUtility.SaveAsset<Material>(m, matname);
					Material++;
					// Save the material to disk?
					// update the SMR
				}

				string meshName = Folder + "/"+CharName+"_Mesh_" + meshno + ".asset";
				meshno++;
				// Save Mesh to disk.
				CustomAssetUtility.SaveAsset<Mesh>(smr.sharedMesh, meshName);
				smr.sharedMaterials = mats;
				smr.materials = mats;
			}

			// save Animator Avatar.
			var animator = baseObject.GetComponent<Animator>();

			string avatarName = Folder + "/"+CharName+"_Avatar.asset";
			CustomAssetUtility.SaveAsset<Avatar>(animator.avatar, avatarName);

			DestroyImmediate(avatar);
			var lod = baseObject.GetComponent<UMASimpleLOD>();
			if (lod != null) DestroyImmediate(lod);

			if (AddStandaloneDNA)
			{
				UMAData uda = baseObject.GetComponent<UMAData>();
				StandAloneDNA sda = baseObject.AddComponent<UMA.StandAloneDNA>();
				sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(uda._umaRecipe);
				if (avatar is DynamicCharacterAvatar)
				{
					DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
					sda.avatarDefinition = avt.GetAvatarDefinition(true);
				}
				sda.umaData = uda;
			}
			else
			{
				var ud = baseObject.GetComponent<UMAData>();
				if (ud != null) DestroyImmediate(ud);
			}
			var ue = baseObject.GetComponent<UMAExpressionPlayer>();
			if (ue != null) DestroyImmediate(ue);

			baseObject.name = CharName;
			string prefabName = Folder + "/"+CharName+".prefab";
			prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
			PrefabUtility.SaveAsPrefabAssetAndConnect(baseObject, prefabName, InteractionMode.AutomatedAction);
		}


		[UnityEditor.MenuItem("GameObject/UMA/Save Atlas Textures (runtime only)")]
		[MenuItem("CONTEXT/DynamicCharacterAvatar/Save Selected Avatars generated textures to PNG", false, 10)]
		[MenuItem("UMA/Runtime/Save Selected Avatar Atlas Textures")]
		public static void SaveSelectedAvatarsPNG()
		{
			if (Selection.gameObjects.Length != 1)
			{
				EditorUtility.DisplayDialog("Notice", "Only one Avatar can be selected.", "OK");
				return;
			}

			var selectedTransform = Selection.gameObjects[0].transform;
			var avatar = selectedTransform.GetComponent<UMAAvatarBase>();

			if (avatar == null)
			{
				EditorUtility.DisplayDialog("Notice", "An Avatar must be selected to use this function", "OK");
				return;
			}

			SkinnedMeshRenderer smr = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
			if (smr == null)
			{
				EditorUtility.DisplayDialog("Warning", "Could not find SkinnedMeshRenderer in Avatar hierarchy", "OK");
				return;
			}

			string path = EditorUtility.SaveFilePanelInProject("Save Texture(s)", "Texture.png", "png", "Base Filename to save PNG files to.");
			if (!string.IsNullOrEmpty(path))
			{
				string basename = System.IO.Path.GetFileNameWithoutExtension(path);
				string pathname = System.IO.Path.GetDirectoryName(path);
				// save the diffuse texture
				for (int i = 0; i < smr.materials.Length; i++)
				{
					Material mat = smr.materials[i];
					string PathBase = System.IO.Path.Combine(pathname, basename + "_material_" + i.ToString());

					string[] texNames = mat.GetTexturePropertyNames();

					foreach (string tex in texNames)
					{
						string texname = PathBase + tex + ".PNG";
						Texture texture = mat.GetTexture(tex);
						if (texture != null) SaveTexture(texture, texname);
					}
				}
			}
		}

		private static void SaveTexture(Texture texture, string diffuseName, bool isNormal = false)
		{
			if (texture is RenderTexture)
			{
				SaveRenderTexture(texture as RenderTexture, diffuseName, isNormal);
				return;
			}
			else if (texture is Texture2D)
			{
				SaveTexture2D(texture as Texture2D, diffuseName);
				return;
			}
			EditorUtility.DisplayDialog("Error", "Texture is not RenderTexture or Texture2D", "OK");
		}

		/// <param name="normalMap"></param>
		/// <returns></returns>
		private static Texture2D sconvertNormalMap(Texture2D normalMap)
		{
			ComputeShader normalMapConverter = Resources.Load<ComputeShader>("Shader/NormalShader");
			int kernel = normalMapConverter.FindKernel("NormalConverter");
			RenderTexture normalMapRenderTex = new RenderTexture(normalMap.width, normalMap.height, 24);
			normalMapRenderTex.enableRandomWrite = true;
			normalMapRenderTex.Create();
			normalMapConverter.SetTexture(kernel, "Input", normalMap);
			normalMapConverter.SetTexture(kernel, "Result", normalMapRenderTex);
			normalMapConverter.Dispatch(kernel, normalMap.width, normalMap.height, 1);
			RenderTexture.active = normalMapRenderTex;

			Texture2D convertedNormalMap = new Texture2D(normalMap.width, normalMap.height, TextureFormat.RGBA32, false, true);
			convertedNormalMap.ReadPixels(new Rect(0, 0, normalMap.width, normalMap.height), 0, 0);
			convertedNormalMap.Apply();

			DestroyImmediate(normalMapRenderTex);
			return convertedNormalMap;
		}

		private static Texture2D sconvertNormalMap(RenderTexture normalMap)
		{
			ComputeShader normalMapConverter = Resources.Load<ComputeShader>("Shader/NormalShader");
			int kernel = normalMapConverter.FindKernel("NormalConverter");
			RenderTexture normalMapRenderTex = new RenderTexture(normalMap.width, normalMap.height, 24);
			normalMapRenderTex.enableRandomWrite = true;
			normalMapRenderTex.Create();
			normalMapConverter.SetTexture(kernel, "Input", normalMap);
			normalMapConverter.SetTexture(kernel, "Result", normalMapRenderTex);
			normalMapConverter.Dispatch(kernel, normalMap.width, normalMap.height, 1);
			RenderTexture.active = normalMapRenderTex;

			Texture2D convertedNormalMap = new Texture2D(normalMap.width, normalMap.height, TextureFormat.RGBA32, false, true);
			convertedNormalMap.ReadPixels(new Rect(0, 0, normalMap.width, normalMap.height), 0, 0);
			convertedNormalMap.Apply();

			DestroyImmediate(normalMapRenderTex);
			return convertedNormalMap;
		}

		private static Texture2D sconvertNormalMap2(RenderTexture rt)
		{
			Texture2D tex = GetRTPixels(rt);
			Texture2D result = sconvertNormalMap(tex);
			DestroyImmediate(tex);
			return result;
		}

		private static Texture2D sconvertNormalMap(Texture tex)
		{
			if (tex is RenderTexture)
				return sconvertNormalMap(tex as RenderTexture);

			return sconvertNormalMap(tex as Texture2D);
		}

		static public Texture2D GetRTPixels(RenderTexture rt)
		{
			/// Some goofiness ends up with the texture being too dark unless
			/// I send it to a new render texture.
			RenderTexture outputMap = new RenderTexture(rt.width, rt.height, 32);
			outputMap.enableRandomWrite = true;
			outputMap.Create();
			RenderTexture.active = outputMap;
			GL.Clear(true, true, Color.black);
			Graphics.Blit(rt, outputMap);


			// Remember currently active render texture
			RenderTexture currentActiveRT = RenderTexture.active;

			// Set the supplied RenderTexture as the active one
			RenderTexture.active = outputMap;

			// Create a new Texture2D and read the RenderTexture image into it
			Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
			tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

			// Restore previously active render texture
			RenderTexture.active = currentActiveRT;
			DestroyImmediate(outputMap);
			return tex;
		}

		private static void SaveRenderTexture(RenderTexture texture, string textureName, bool isNormal = false)
		{
			Texture2D tex;

			if (isNormal)
			{
				tex = sconvertNormalMap(texture);
			}
			else
			{
				tex = GetRTPixels(texture);
			}
			SaveTexture2D(tex, textureName);
		}

		private static void SaveTexture2D(Texture2D texture, string textureName)
		{
			if (texture.isReadable)
			{
				byte[] data = texture.EncodeToPNG();
				System.IO.File.WriteAllBytes(textureName, data);
			}
			else
			{
				Debug.LogError("Texture: " + texture.name + " is not readable. Skipping.");
			}
		}

		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as UMA Preset")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as UMA Preset")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar as UMA Preset", priority = 1)]
		public static void SaveSelectedAvatarsPreset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save avatar preset", "Assets", avatar.name + ".umapreset", "umapreset");
					if (path.Length != 0)
					{

						UMAPreset prs = new UMAPreset();
						prs.DefaultColors = avatar.characterColors;
						var DNA = avatar.GetDNA();
						prs.PredefinedDNA = new UMAPredefinedDNA();
						foreach (DnaSetter d in DNA.Values)
						{
							prs.PredefinedDNA.AddDNA(d.Name, d.Value);
						}
						prs.DefaultWardrobe = new DynamicCharacterAvatar.WardrobeRecipeList();
						foreach (UMATextRecipe utr in avatar.WardrobeRecipes.Values)
						{
							prs.DefaultWardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(utr));
						}
						string presetstring = JsonUtility.ToJson(prs);
						System.IO.File.WriteAllText(path, presetstring);
					}
				}
			}
		}


		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Character text file (runtime only)")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Text file (runtime only)")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar(s) Txt", priority = 1)]
		public static void SaveSelectedAvatarsTxt()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save serialized Avatar", "Assets", avatar.name + ".txt", "txt");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						//check if Avatar is DCS
						if (avatar is UMA.CharacterSystem.DynamicCharacterAvatar)
						{
							asset.Save(avatar.umaData.umaRecipe, avatar.context, (avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
						}
						else
						{
							asset.Save(avatar.umaData.umaRecipe, avatar.context);
						}
						System.IO.File.WriteAllText(path, asset.recipeString);
						UMAUtils.DestroySceneObject(asset);
					}
				}
			}
		}


		[UnityEditor.MenuItem("GameObject/UMA/Show Mesh Info (runtime only)")]
		public static void ShowSelectedAvatarStats()
		{
			if (Selection.gameObjects.Length == 1)
			{
				var selectedTransform = Selection.gameObjects[0].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					SkinnedMeshRenderer sk = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
					if (sk != null)
					{
						List<string> info = new List<string>
					{
						sk.gameObject.name,
						"Mesh index type: " + sk.sharedMesh.indexFormat.ToString(),
						"VertexLength: " + sk.sharedMesh.vertices.Length,
						"Submesh Count: " + sk.sharedMesh.subMeshCount
					};
						for (int i = 0; i < sk.sharedMesh.subMeshCount; i++)
						{
							int[] tris = sk.sharedMesh.GetTriangles(i);
							info.Add("Submesh " + i + " Tri count: " + tris.Length);
						}
						Rect R = new Rect(200.0f, 200.0f, 300.0f, 600.0f);
						DisplayListWindow.ShowDialog("Mesh Info", R, info);
					}
				}
			}

		}

		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Asset (runtime only)")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar(s) asset", priority = 1)]
		public static void SaveSelectedAvatarsAsset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanelInProject("Save serialized Avatar", avatar.name + ".asset", "asset", "Message 2");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						//check if Avatar is DCS
						if (avatar is DynamicCharacterAvatar)
						{
							asset.Save(avatar.umaData.umaRecipe, avatar.context, (avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
						}
						else
						{
							asset.Save(avatar.umaData.umaRecipe, avatar.context);
						}
						AssetDatabase.CreateAsset(asset, path);
						AssetDatabase.SaveAssets();
						Debug.Log("Recipe size: " + asset.recipeString.Length + " chars");

					}
				}
			}
		}

		[UnityEditor.MenuItem("GameObject/UMA/Load from Character Text file (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Load Avatar from text file (runtime only)")]
		[MenuItem("UMA/Load and Save/Load Selected Avatar(s) txt", priority = 1)]
		public static void LoadSelectedAvatarsTxt()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "txt");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						asset.recipeString = FileUtils.ReadAllText(path);
						//check if Avatar is DCS
						if (avatar is DynamicCharacterAvatar)
						{
							(avatar as DynamicCharacterAvatar).LoadFromRecipeString(asset.recipeString);
						}
						else
						{
							avatar.Load(asset);
						}

						UMAUtils.DestroySceneObject(asset);
					}
				}
			}
		}

		[UnityEditor.MenuItem("GameObject/UMA/Load from Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Load Avatar from Asset (runtime only)")]
		[MenuItem("UMA/Load and Save/Load Selected Avatar(s) assets", priority = 1)]
		public static void LoadSelectedAvatarsAsset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "asset");
					if (path.Length != 0)
					{
						var index = path.IndexOf("/Assets/");
						if (index > 0)
						{
							path = path.Substring(index + 1);
						}
						var asset = AssetDatabase.LoadMainAssetAtPath(path) as UMARecipeBase;
						if (asset != null)
						{
							//check if Avatar is DCS
							if (avatar is DynamicCharacterAvatar)
							{
								(avatar as DynamicCharacterAvatar).LoadFromRecipe(asset);
							}
							else
							{
								avatar.Load(asset);
							}
						}
						else
						{
							Debug.LogError("Failed To Load Asset \"" + path + "\"\nAssets must be inside the project and descend from the UMARecipeBase type");
						}
					}
				}
			}
		}

		//@jaimi this is the equivalent of your previous JSON save but the resulting file does not need a special load method
		[UnityEditor.MenuItem("GameObject/UMA/Save as Optimized Character Text File (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Optimized Character Text File")]
		[MenuItem("UMA/Load and Save/Save DynamicCharacterAvatar(s) txt (optimized)", priority = 1)]
		public static void SaveSelectedAvatarsDCSTxt()
		{
			if (!Application.isPlaying)
			{
				EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
				return;
			}
			else
			{
				EditorUtility.DisplayDialog("Notice", "The optimized save type is only compatible with DynamicCharacterAvatar avatars (or child classes of)", "Continue");
			}

			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save DynamicCharacterAvatar Optimized Text", "Assets", avatar.name + ".txt", "txt");
					if (path.Length != 0)
					{
						avatar.DoSave(false, path);
					}
				}
			}
		}
		//@jaimi this is the equivalent of your previous JSON save but the resulting file does not need a special load method and the resulting asset can also be inspected and edited
		[UnityEditor.MenuItem("GameObject/UMA/Save as Optimized Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Optimized Character Asset File")]
		[MenuItem("UMA/Load and Save/Save DynamicCharacterAvatar(s) asset (optimized)", priority = 1)]
		public static void SaveSelectedAvatarsDCSAsset()
		{
			if (!Application.isPlaying)
			{
				EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
				return;
			}
			else
			{
				EditorUtility.DisplayDialog("Notice", "The optimized save type is only compatible with DynamicCharacterAvatar avatars (or child classes of)", "Continue");
			}

			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanelInProject("Save DynamicCharacterAvatar Optimized Asset", avatar.name + ".asset", "asset", "Message 2");
					if (path.Length != 0)
					{
						avatar.DoSave(true, path);
					}
				}
			}
		}

		[UnityEditor.MenuItem("Assets/Add Selected Assets to UMA Global Library")]
		public static void AddSelectedToGlobalLibrary()
		{
			int added = 0;
			UMAAssetIndexer UAI = UMAAssetIndexer.Instance;

			foreach (Object o in Selection.objects)
			{
				System.Type type = o.GetType();
				if (UAI.IsIndexedType(type))
				{
					if (UAI.EvilAddAsset(type, o))
						added++;
				}
			}
			UAI.ForceSave();
			EditorUtility.DisplayDialog("Success", added + " item(s) added to Global Library", "OK");
		}
	}

	public class UmaPrefabSaverWindow : EditorWindow
	{
		[Tooltip("The character that you want to convert")]
		public UMAAvatarBase baseObject;
		[Tooltip("Convert Swizzled normal maps back to standard normal maps")]
		public bool UnswizzleNormalMaps = true;
		[Tooltip("If True, will keep the umaData, and add a Standalone DNA component allowing you to load/save/Deform skeletal DNA")]
		public bool AddStandaloneDNA = true;
		[Tooltip("The prefab will be named this, and it will be added to all assets saved")]
		public string CharacterName;
		[Tooltip("The folder where the prefab folder will be created")]
		public UnityEngine.Object prefabFolder;
		public string CheckFolder(ref UnityEngine.Object folderObject)
		{
			if (folderObject != null)
			{
				string destpath = AssetDatabase.GetAssetPath(folderObject);
				if (string.IsNullOrEmpty(destpath))
				{
					folderObject = null;
				}
				else if (!System.IO.Directory.Exists(destpath))
				{
					destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
				}
				return destpath;
			}
			return null;
		}

		void OnGUI()
		{
			EditorGUILayout.LabelField("UMA Prefab Saver", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("This will convert an UMA avatar into a non-UMA prefab. Once converted, it can be reused with little overhead, but all UMA functionality will be lost.", MessageType.None, false);
			baseObject = (UMAAvatarBase)EditorGUILayout.ObjectField("UMA Avatar",baseObject, typeof(UMAAvatarBase),true);
			EditorGUILayout.HelpBox("If you unswizzle normals (recommended) then they can be used in other applications, and UMA will automatically mark them as normal maps in the import settings.", MessageType.None);
			UnswizzleNormalMaps = EditorGUILayout.Toggle("Unswizzle Normals", UnswizzleNormalMaps);
			EditorGUILayout.HelpBox("Adding Standalone DNA will allow you to adjust most DNA of the character, without it being an UMA. However, it will require that you have the UMA system in the project.",MessageType.None);
			AddStandaloneDNA = EditorGUILayout.Toggle("Add Standalone DNA", AddStandaloneDNA);
			CharacterName = EditorGUILayout.TextField("Prefab Name", CharacterName);
			prefabFolder = EditorGUILayout.ObjectField("Prefab Base Folder", prefabFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

			string folder = CheckFolder(ref prefabFolder);

			if (prefabFolder != null && baseObject != null && !string.IsNullOrEmpty(CharacterName))
			{
				if (GUILayout.Button("Make Prefab") && prefabFolder != null)
				{
					UMAAvatarLoadSaveMenuItems.ConvertToNonUMA(baseObject.gameObject, baseObject, folder, UnswizzleNormalMaps, CharacterName,AddStandaloneDNA);
					EditorUtility.DisplayDialog("UMA Prefab Saver", "Conversion complete", "OK");
				}
			}
			else
            {
				if (baseObject == null)
				{
					EditorGUILayout.HelpBox("A valid character with DynamicCharacterAvatar or DynamicAvatar must be supplied",MessageType.Error);
				}
				if (string.IsNullOrEmpty(CharacterName))
                {
					EditorGUILayout.HelpBox("Prefab Name cannot be empty", MessageType.Error);
                }
				if (prefabFolder == null)
                {
					EditorGUILayout.HelpBox("A valid base folder must be supplied", MessageType.Error);
                }
            }
		}

		[MenuItem("UMA/Prefab Maker", priority = 20)]
		public static void OpenUmaPrefabWindow()
		{
			UmaPrefabSaverWindow window = (UmaPrefabSaverWindow)EditorWindow.GetWindow(typeof(UmaPrefabSaverWindow));
			window.titleContent.text = "UMA Prefab Maker";
		}
	}
}
