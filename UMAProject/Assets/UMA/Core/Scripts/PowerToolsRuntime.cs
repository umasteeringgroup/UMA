using System;
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	public static class PowerToolsRuntime
	{
		public static Type GetPreferredRecipeFormat()
		{
			foreach (var format in UMARecipeBase.GetRecipeFormats())
			{
				if (format.FullName == "UMA.RecipeTools.BinaryRecipeFloat")
					return format;
			}
			return typeof(UMATextRecipe);
		}
		
		public static bool IsGeneratedTexture(UMAMaterial.ChannelType channelType)
		{
			return channelType == UMAMaterial.ChannelType.Texture || channelType == UMAMaterial.ChannelType.DiffuseTexture || channelType == UMAMaterial.ChannelType.NormalMap;
		}		

		public static GameObject SaveCharacterPrefab(string assetFolder, string name, UMAData originalUMAData, bool exportTPose = false)
		{
#if UNITY_EDITOR
			var clonedGO = UnityEngine.Object.Instantiate<GameObject>(originalUMAData.gameObject, null, false);
			var clonedTransform = clonedGO.transform;
			var umaData = clonedGO.GetComponent<UMAData>();

			EnsureProjectFolder(assetFolder);
			var prefabPath = assetFolder + "/" + name + ".prefab";

			var asset = ScriptableObject.CreateInstance(GetPreferredRecipeFormat()) as UMARecipeBase;
			asset.Save(umaData.umaRecipe);
			AssetDatabase.CreateAsset(asset, assetFolder+"/"+name+"_recipe.asset");
			AssetDatabase.SaveAssets();


			foreach(var generatedMaterial in originalUMAData.generatedMaterials.materials)
			{
				for(int i = 0; i < generatedMaterial.resultingAtlasList.Length; i++)
				{
					var materialChannel = generatedMaterial.umaMaterial.channels[i];
					if( !IsGeneratedTexture(materialChannel.channelType)) continue;
					var atlas = generatedMaterial.resultingAtlasList[i];
					Texture2D tex2D = atlas as Texture2D;
					if (tex2D == null)
					{
						tex2D = new Texture2D(atlas.width, atlas.height, TextureFormat.ARGB32, false, PlayerSettings.colorSpace == ColorSpace.Linear || materialChannel.channelType == UMAMaterial.ChannelType.NormalMap);
						RenderTexture.active = atlas as RenderTexture;
						tex2D.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0, false);
						RenderTexture.active = null;
#if !UNITY_ANDROID
						if (materialChannel.channelType == UMAMaterial.ChannelType.NormalMap)
						{
							TransformNormalMap(tex2D);
						}
#endif
					}
					tex2D.name = name + "_" + generatedMaterial.umaMaterial.name + materialChannel.materialPropertyName;
					WriteAllBytes(GetAssetPath(assetFolder + "/" + tex2D.name + ".png"), tex2D.EncodeToPNG());
				}
			}
			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

			if (exportTPose)
			{
				var WriteDefaultPoseMethod = typeof(Animator).GetMethod("WriteDefaultPose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				if (WriteDefaultPoseMethod != null)
				{
					clonedTransform.position = Vector3.zero;
					clonedTransform.rotation = Quaternion.identity;
					clonedTransform.localScale = Vector3.one;
					WriteDefaultPoseMethod.Invoke(umaData.animator, null);
				}
				else
				{
					Debug.LogError("Animator.WriteDefaultPose not found, cannot export prefab in tpose");
				}
			}

#if UNITY_2018_3_OR_NEWER
			var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(clonedGO, prefabPath, InteractionMode.AutomatedAction);
#else
			var prefab = PrefabUtility.CreatePrefab(assetFolder + "/" + name + ".prefab", clonedGO, ReplacePrefabOptions.ConnectToPrefab);
#endif
			Avatar avatar = Avatar.Instantiate(umaData.animator.avatar);
			avatar.name = name;
			AssetDatabase.AddObjectToAsset(avatar, prefab);

			prefab.GetComponent<Animator>().avatar = avatar;
			for (int i = 0; i < originalUMAData.RendererCount; i++)
				AssetDatabase.AddObjectToAsset(originalUMAData.GetRenderer(i).sharedMesh, prefab);

			var materialConversions = new Dictionary<Material, Material>();
			var materials = new Material[originalUMAData.generatedMaterials.materials.Count];
			for (int j = 0; j < originalUMAData.generatedMaterials.materials.Count; j++)
			{
				var generatedMaterial = originalUMAData.generatedMaterials.materials[j];
				var mat = new Material(generatedMaterial.material);
				mat.name = generatedMaterial.material.name;
				materialConversions.Add(generatedMaterial.material, mat);
				materials[j] = mat;
			
				for (int i = 0; i < generatedMaterial.resultingAtlasList.Length; i++)
				{
					var materialChannel = generatedMaterial.umaMaterial.channels[i];
					if( !IsGeneratedTexture(materialChannel.channelType)) continue;
					var texturePath =	assetFolder + "/" + name + "_" + generatedMaterial.umaMaterial.name + generatedMaterial.umaMaterial.channels[i].materialPropertyName + ".png";

					if ( materialChannel.channelType == UMA.UMAMaterial.ChannelType.NormalMap)
					{
						var ti = TextureImporter.GetAtPath(texturePath) as TextureImporter;
						ti.textureType = TextureImporterType.NormalMap;
						AssetDatabase.ImportAsset(texturePath);
					}
					else
					{
						if( PlayerSettings.colorSpace == ColorSpace.Linear )
						{
							var ti = TextureImporter.GetAtPath(texturePath) as TextureImporter;
							ti.sRGBTexture = false;
							AssetDatabase.ImportAsset(texturePath);
						}
					}
					mat.SetTexture(materialChannel.materialPropertyName, AssetDatabase.LoadMainAssetAtPath(texturePath) as Texture);
				}
				AssetDatabase.AddObjectToAsset(mat, prefab);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(prefab));
			prefab.GetComponentsInChildren<Animator>(true)[0].avatar = avatar;

			for (int i = 0; i < originalUMAData.RendererCount; i++)
			{
				var originalRenderer = originalUMAData.GetRenderer(i);
				var newRenderer = prefab.transform.Find(originalRenderer.name).GetComponent<SkinnedMeshRenderer>();
				var originalMaterials = originalRenderer.sharedMaterials;
				var newMaterials = new Material[originalMaterials.Length];
				for (int j = 0; j < originalMaterials.Length; j++)
				{
					newMaterials[j] = materials[Array.IndexOf(materials, materialConversions[originalMaterials[j]])];
				}
				newRenderer.sharedMaterials = newMaterials;
				newRenderer.sharedMesh = originalRenderer.sharedMesh;
				EditorUtility.SetDirty(newRenderer);
			}

			foreach (var component in prefab.GetComponents<MonoBehaviour>())
			{
				FilterUmaComponents(component);
			}

			foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
			{
				FilterUmaComponents(component);
			}

#if UNITY_2018_3_OR_NEWER
			PrefabUtility.SavePrefabAsset(prefab);
#endif
			AssetDatabase.ImportAsset(prefabPath);
			AssetDatabase.SaveAssets();

			GameObject.DestroyImmediate(clonedGO, false);
			return prefab;
#else
			throw new NotImplementedException("SaveCharacterPrefab Cannot save a prefab outside of the Unity environment. This method only works in the editor!");
#endif
		}

		private static void FilterUmaComponents(MonoBehaviour component)
		{
			if (IsUmaComponent(component))
			{
				UMAData umaDataComponent = component as UMAData;
				if (umaDataComponent != null)
				{
					umaDataComponent.umaRoot = null;
				}
				UnityEngine.Object.DestroyImmediate(component, true);
			}
		}

		private static bool IsUmaComponent(MonoBehaviour component)
		{
			var nameSpace = component.GetType().Namespace;
			return string.Compare(nameSpace, "UMA", true) == 0 || nameSpace.StartsWith("UMA.", StringComparison.InvariantCultureIgnoreCase);
		}

		private static void TransformNormalMap(Texture2D tex2D)
		{
			var pixels = tex2D.GetPixels32();
			for (int i = 0; i < pixels.Length; i ++)
			{
				TransformNormalMapPixel(ref pixels[i]);
			}
			tex2D.SetPixels32(pixels);
		}

		private static void TransformNormalMapPixel(ref Color32 color)
		{
			byte R = color.a;
			byte G = color.g;
			int iR = R;
			int iG = G;
			int B = Mathf.FloorToInt(Mathf.Sqrt(65535f - (iR * iR + iG * iG)));
			color.a = 255;
			color.r = R;
			color.g = G;
			color.b = (byte)B;			
		}

		public static void SaveCharacterPrefab(UMAData umaData, string prefabName)
		{
#if UNITY_EDITOR
			EnsureProjectFolder(UMAPathUtility.GeneratedCharactersRoot);
			var assetFolder = AssetDatabase.GenerateUniqueAssetPath(UMAPathUtility.GeneratedCharactersRoot + "/" + prefabName);
			SaveCharacterPrefab(assetFolder, prefabName, umaData);
#else
			throw new NotImplementedException("SaveCharacterPrefab Cannot save a prefab outside of the Unity environment. This method only works in the editor!");
#endif
		}

		#region helper methods

		public static void EnsureProjectFolder(string folder)
		{
#if UNITY_EDITOR
			if (!System.IO.Directory.Exists(System.IO.Directory.GetCurrentDirectory() + "/" + folder))
			{
				EnsureProjectFolder(System.IO.Path.GetDirectoryName(folder));
				AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder), System.IO.Path.GetFileName(folder));
			}
#else
			throw new NotImplementedException("EnsureProjectFolder: The concept of ensuring a project folder outside the Unity environment is flawed. This method only works in the editor!");
#endif
		}

		public static string GetAssetPath(string path)
		{
			return System.IO.Directory.GetCurrentDirectory() + "/" + path;
		}

		public static void WriteAllBytes(string path, byte[] data)
		{
			using (var file = System.IO.File.Open(path, System.IO.FileMode.OpenOrCreate))
			{
				file.Write(data, 0, data.Length);
				file.SetLength(data.Length);
				file.Flush();
			}
		}
		#endregion
	}
}
