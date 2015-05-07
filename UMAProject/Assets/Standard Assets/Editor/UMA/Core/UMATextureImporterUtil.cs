#if UNITY_EDITOR

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

	    public static OverlayDataAsset CreateOverlayData(Texture[] textures, string assetFolder, string assetName, string overlayFolder)
	    {
	        if (!System.IO.Directory.Exists(overlayFolder + '/' + assetFolder))
	        {
	            System.IO.Directory.CreateDirectory(overlayFolder + '/' + assetFolder);
	        }


	        var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
	        overlay.overlayName = assetName;
	        overlay.textureList = textures;
	        AssetDatabase.CreateAsset(overlay, overlayFolder + '/' + assetFolder + assetName + ".asset");
	        AssetDatabase.SaveAssets();
	        return overlay;

	    }


		[System.Obsolete("UMATextureImporterUtil.CreateSlotData is obsolete use UMASlotProcessingUtil.CreateSlotData instead.", false)]
		public static SlotDataAsset CreateSlotData(string slotFolder, string assetFolder, string assetName, SkinnedMeshRenderer mesh, Material material, SkinnedMeshRenderer prefabMesh)
		{
			throw new NotImplementedException();
		}
	}
}
#endif
