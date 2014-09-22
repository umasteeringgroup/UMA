//	============================================================
//	Name:		UMATextureImporterUtil
//	Author: 	Joen Joensen (@UnLogick)
//	============================================================


using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using UMA;


namespace UMAEditor
{
	public static class UMATextureImporterUtil 
	{
	    public static bool ConvertDefaultAssets_DiffuseAlpha_Normal_Specular(string diffuseAlpha, string normal, string specular, string assetFolder, string assetName, string textureFolder, Shader shader)
	    {
	        if (!System.IO.Directory.Exists(textureFolder + '/' + assetFolder))
	        {
	            System.IO.Directory.CreateDirectory(textureFolder + '/' + assetFolder);
	        }
	       
	        var diffuseName = textureFolder + "/" + assetFolder + assetName + "_diffuse.png";
	        if (!CopyTextureToPng(diffuseAlpha, diffuseName)) return false;

	        var normalName = textureFolder + "/" + assetFolder + assetName + "_normal.png";
	        if (!CopyTextureToPng(normal, normalName)) return false;

	        var specularName = textureFolder + "/" + assetFolder + assetName + "_specular.png";
	        if (!CopyTextureToPng(specular, specularName)) return false;

	        AssetDatabase.SaveAssets();
	        AssetDatabase.Refresh();
	        
	        EnforceReadAndARGB32(specularName);
	        EnforceReadAndARGB32(normalName);


	        AssetDatabase.SaveAssets();
	        AssetDatabase.Refresh();

	        Color32[] normals = LoadPixels(normalName);
	        Color32[] speculars = LoadPixels(specularName);

	        for (int i = 0; i < normals.Length; i++)
	        {
				normals[i].a = normals[i].r;
	            //normals[i].g = normals[i].g;
				normals[i].r = (byte)((speculars[i].r + speculars[i].g + speculars[i].b) / 3);
				normals[i].b = speculars[i].a;
	        }

	        RemoveNormalMapFlag(normalName);

	        var normalTexture = AssetDatabase.LoadAssetAtPath(normalName, typeof(Texture2D)) as Texture2D;
	        normalTexture.SetPixels32(normals);
	        normalTexture.Apply();
	        System.IO.File.WriteAllBytes(normalName, normalTexture.EncodeToPNG());

	        AssetDatabase.DeleteAsset(specularName);

	        bool readable = !UnityEditorInternal.InternalEditorUtility.HasPro();
	        SetReadable(normalName, readable);
	        SetReadable(diffuseName, readable);
	        
	        AssetDatabase.SaveAssets();
	        AssetDatabase.Refresh();
	        return true;
	    }

	    private static void SetReadable(string texture, bool readable)
	    {
	        var finalImporter = TextureImporter.GetAtPath(texture) as TextureImporter;
	        finalImporter.isReadable = readable;
	        AssetDatabase.ImportAsset(texture);
	    }

	    private static void RemoveNormalMapFlag(string texture)
	    {
	        var importer = TextureImporter.GetAtPath(texture) as TextureImporter;
	        if (importer.normalmap || importer.textureType != TextureImporterType.Advanced)
	        {
	            importer.textureType = TextureImporterType.Advanced;
	            importer.normalmap = false;
	            AssetDatabase.SaveAssets();
	            AssetDatabase.Refresh();
	        }
	    }

	    private static Color32[] LoadPixels(string texture)
	    {
	        var tex = AssetDatabase.LoadAssetAtPath(texture, typeof(Texture2D)) as Texture2D;
	        if (tex == null)
	        {
	            Debug.LogError(string.Format("LoadPixels Error: Failed to load texture {0}", texture));
	            return null;
	        }

	        return tex.GetPixels32();
	    }

	    private static void EnforceReadAndARGB32(string texture)
	    {
	        var importer = TextureImporter.GetAtPath(texture) as TextureImporter;
	        importer.isReadable = true;
	        importer.textureFormat = TextureImporterFormat.ARGB32;
	        AssetDatabase.ImportAsset(texture);
	    }

	    public static bool CopyTextureToPng(string source, string dest)
	    {
	        AssetDatabase.DeleteAsset(dest);
	        string sourceExtension = System.IO.Path.GetExtension(source);
	        if (string.Compare(sourceExtension, ".png", true) != 0)
	        {
	            var index = dest.LastIndexOf('/');
	            string temp = dest.Substring(0, index+1) + "TEMP_" + dest.Substring(index + 1)+sourceExtension;
	            AssetDatabase.DeleteAsset(temp);
	            if (!AssetDatabase.CopyAsset(source, temp))
	            {
	                Debug.LogError(string.Format("CopyTextureToPng error: Couldn't copy {0} to temp {1}", source, temp));
	                return false;
	            }
	            AssetDatabase.ImportAsset(temp); 
	            var importer = TextureImporter.GetAtPath(temp) as TextureImporter;
	            if (!importer.isReadable || importer.textureFormat != TextureImporterFormat.ARGB32)
	            {
	                importer.isReadable = true;
	                importer.textureFormat = TextureImporterFormat.ARGB32;
	                AssetDatabase.ImportAsset(temp);
	            }

	            var tex = AssetDatabase.LoadAssetAtPath(temp, typeof(Texture2D)) as Texture2D;
	            if (tex == null)
	            {
	                Debug.LogError("DAMNABBIT asset not readable: " + temp);
	                return false;
	            }

	            System.IO.File.WriteAllBytes(dest, tex.EncodeToPNG());
	            AssetDatabase.ImportAsset(dest); 

	            //var destTex = Texture2D.Instantiate(tex) as Texture2D;
	            //var destTex = new Texture2D(tex.width, tex.height, tex.format, tex.mipmapCount > 0);
	            //destTex.anisoLevel = tex.anisoLevel;
	            //destTex.filterMode = tex.filterMode;
	            //destTex.mipMapBias = tex.mipMapBias;
	            //destTex.wrapMode = tex.wrapMode;
	            //destTex.SetPixels32(tex.GetPixels32());
	            //AssetDatabase.CreateAsset(destTex, dest);

	            var tempimporter = TextureImporter.GetAtPath(temp) as TextureImporter;
	            var destimporter = TextureImporter.GetAtPath(dest) as TextureImporter;

	            var settings = new TextureImporterSettings();
	            tempimporter.ReadTextureSettings(settings);
	            destimporter.SetTextureSettings(settings);

	            destimporter.normalmap = false;
	            destimporter.isReadable = true;
	            AssetDatabase.ImportAsset(dest);
	            AssetDatabase.DeleteAsset(temp);
	        }
	        else
	        {
	            if (!AssetDatabase.CopyAsset(source, dest))
	            {
	                Debug.LogError(string.Format("CopyTextureToPng error: Couldn't copy {0} to {1}", source, dest));
	                return false;
	            }
	            AssetDatabase.ImportAsset(dest);
	        }
	        return true;
	    }

	    public static OverlayData CreateOverlayData(Texture2D[] textures, string assetFolder, string assetName, string overlayFolder)
	    {
	        if (!System.IO.Directory.Exists(overlayFolder + '/' + assetFolder))
	        {
	            System.IO.Directory.CreateDirectory(overlayFolder + '/' + assetFolder);
	        }


	        var overlay = ScriptableObject.CreateInstance<OverlayData>();
	        overlay.overlayName = assetName;
	        overlay.textureList = textures;
	        AssetDatabase.CreateAsset(overlay, overlayFolder + '/' + assetFolder + assetName + ".asset");
	        AssetDatabase.SaveAssets();
	        return overlay;

	    }

	    public static SlotData CreateSlotData(string slotFolder, string assetFolder, string assetName, SkinnedMeshRenderer mesh, Material material, SkinnedMeshRenderer prefabMesh)
	    {		
	        if (!System.IO.Directory.Exists(slotFolder + '/' + assetFolder))
	        {
	            System.IO.Directory.CreateDirectory(slotFolder + '/' + assetFolder);
	        }	
			
			if (!System.IO.Directory.Exists(slotFolder + '/' + assetName))
	        {
	            System.IO.Directory.CreateDirectory(slotFolder + '/' + assetName);
	        }
			
            GameObject tempGameObject = UnityEngine.Object.Instantiate(mesh.transform.parent.gameObject) as GameObject;
            PrefabUtility.DisconnectPrefabInstance(tempGameObject);
            var resultingSkinnedMeshes = tempGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer resultingSkinnedMesh = null;
            foreach (var skinnedMesh in resultingSkinnedMeshes)
            {
                if (skinnedMesh.name == mesh.name)
                {
                    resultingSkinnedMesh = skinnedMesh;
                }
            }

            Mesh resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, prefabMesh, 0.0001f);
            resultingSkinnedMesh.sharedMesh = resultingMesh;
            SkinnedMeshAligner.AlignBindPose(prefabMesh, resultingSkinnedMesh);
			
			AssetDatabase.CreateAsset(resultingMesh, slotFolder + '/' + assetName + '/' + mesh.name + ".asset");
					
			tempGameObject.name = mesh.transform.parent.gameObject.name;
			Transform[] transformList = tempGameObject.GetComponentsInChildren<Transform>();
				
			GameObject newObject = new GameObject();
			
			for(int i = 0; i < transformList.Length; i++){
				if(transformList[i].name == "Global"){
					transformList[i].parent = newObject.transform;
				}else if(transformList[i].name == mesh.name){
					transformList[i].parent = newObject.transform;
				}
			}
			
			GameObject.DestroyImmediate(tempGameObject);
			resultingSkinnedMesh = newObject.GetComponentInChildren<SkinnedMeshRenderer>();
	        if (resultingSkinnedMesh)
	        {
	            resultingSkinnedMesh.sharedMesh = resultingMesh;
	        }
			
			var skinnedResult = UnityEditor.PrefabUtility.CreatePrefab(slotFolder + '/' + assetName + '/' + assetName + "_Skinned.prefab", newObject);
	        GameObject.DestroyImmediate(newObject);

            var meshgo = skinnedResult.transform.Find(mesh.name);
            var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();
            var tempBoneData = ExtractNewBones(finalMeshRenderer, prefabMesh);
	        
	        SlotData slot = ScriptableObject.CreateInstance<SlotData>();
	        slot.slotName = assetName;
			slot.materialSample = material;
			slot.umaBoneData = tempBoneData;
            slot.meshRenderer = finalMeshRenderer;
	        AssetDatabase.CreateAsset(slot, slotFolder + '/' + assetName + '/' + assetName + "_Slot.asset");
			AssetDatabase.SaveAssets();
	        return slot;
	    }

	    private static Transform[] ExtractNewBones(SkinnedMeshRenderer newMesh, SkinnedMeshRenderer oldMesh)
	    {
	        List<Transform> newBones = new List<Transform>();
	        ExtractNewBonesRecursive(FindGlobalBone(newMesh.rootBone), FindGlobalBone(oldMesh.rootBone), newBones);
	        return newBones.ToArray();
	    }

	    private static Transform FindGlobalBone(Transform bone)
	    {
	        if (bone.name == "Global") return bone;
	        return FindGlobalBone(bone.parent);
	    }

	    private static void ExtractNewBonesRecursive(Transform newBone, Transform oldBone, List<Transform> newBones)
	    {
	        for (int i = 0; i < newBone.childCount; i++)
	        {
	            var newChildBone = newBone.GetChild(i);
	            var oldChildBone = oldBone != null ? oldBone.FindChild(newChildBone.name) : null;
	            if (oldChildBone == null)
	            {
	                Debug.LogWarning(newChildBone);
	                newBones.Add(newChildBone);
	            }
	            else
	            {
	                Debug.Log(newChildBone);
                    newBones.Add(newChildBone);
                }
                ExtractNewBonesRecursive(newChildBone, oldChildBone, newBones);
	        }
	    }
	}
}