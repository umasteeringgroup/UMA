using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;
using UMA;

namespace UMAEditor
{
	public class UmaLegacyMaterialBuilderWindow : EditorWindow {

	    public Texture2D diffuse;
	    public Texture2D normal;
	    public Texture2D specular;

	    public string materialName;
	    public UnityEngine.Object overlayFolder;
	    public UMAMaterial slotMaterial;
	    public OverlayData textureOverride;

	    private string normalWarning;
	    private Texture2D privateNormal;
	    private Texture[] textures; // reset for each processing

	    string GetAssetFolder()
	    {
	        int index = materialName.LastIndexOf('/');
	        if( index > 0 )
	        {
	            return materialName.Substring(0, index+1);
	        }
	        return "";
	    }

	    string GetAssetName()
	    {
	        int index = materialName.LastIndexOf('/');
	        if (index > 0)
	        {
	            return materialName.Substring(index + 1);
	        }
	        return materialName;
	    }

	    public void EnforceFolder(ref UnityEngine.Object folderObject)
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
	                folderObject = AssetDatabase.LoadMainAssetAtPath(destpath);
	            }
	        }
	    }


	    void OnGUI()
	    {
			GUILayout.Label("UMA Legacy Material Builder");
			GUILayout.Space(20);
			diffuse = EditorGUILayout.ObjectField("Diffuse Texture", diffuse, typeof(Texture2D), false) as Texture2D;
	        normal = EditorGUILayout.ObjectField("Normal Map (optional)", normal, typeof(Texture2D), false) as Texture2D;
	        if( Event.current.type == EventType.layout && privateNormal != normal )
	        {
	            normalWarning = null;
	            privateNormal = normal;
	            if (normal != null)
	            {
	                var importer = TextureImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
	                if (importer.normalmap)
	                {
	                    normalWarning = "Normal Map texture should not be set as normal map type";
	                }
	            }
	        }
	        if (!string.IsNullOrEmpty(normalWarning))
	        {
	            Color storedColor = GUI.contentColor;
	            GUI.contentColor = Color.yellow;
	            GUILayout.Label(normalWarning);
	            GUI.contentColor = storedColor;
	        }
	        specular = EditorGUILayout.ObjectField("Specular Texture (optional)", specular, typeof(Texture2D), false) as Texture2D;

	        materialName = EditorGUILayout.TextField("Element Name", materialName);
	        overlayFolder = EditorGUILayout.ObjectField("Texture Folder", overlayFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
	        EnforceFolder(ref overlayFolder); 

	        EditorGUILayout.Space();

	        if (GUILayout.Button("Create Overlay"))
	        {
				Debug.Log("Processing...");
				if (CreateTexture())
				{
					Debug.Log("Success");
				}
				else
				{
					Debug.LogError("Failed to create texture");
				}

				if (CreateOverlay() != null)
				{
					Debug.Log("Success.");
				}
	        }
	    }

		private OverlayDataAsset CreateOverlay()
	    {
	        
			var overlay = CreateOverlay_Internal();
	        
			if(materialName == null || materialName == ""){
				Debug.LogError("materialName must be specified.");
	            return null;
			}
			
			if (overlay == null)
	        {
	            Debug.LogError("Failed to create overlay");
	            return null;
	        }
	        return overlay;
	    }

		private OverlayDataAsset CreateOverlay_Internal()
	    {
			if(materialName == null || materialName == ""){
				Debug.LogError("materialName must be specified.");
	            return null;
			}
			
			if (overlayFolder == null)
	        {
	            Debug.LogError("Overlay folder not supplied");
	            return null;
	        }
			var overlay = UMATextureImporterUtil.CreateOverlayData(textures, GetAssetFolder(), GetAssetName(), AssetDatabase.GetAssetPath(overlayFolder));
	        return overlay;
	    }

	    private bool CreateTexture()
	    {
	        textures = null;
	       
			if(materialName == null || materialName == ""){
				Debug.LogError("materialName must be specified.");
	            return false;
			}
			
			if (textureOverride != null )
	        {
				textures = textureOverride.asset.textureList;
	            return true;
	        }

	        if (overlayFolder == null)
	        {
	            Debug.LogError("Texture folder not supplied");
	            return false;
	        }
	        if (diffuse == null)
	        {
	            Debug.LogError("Not all textures present");
	            return false;
	        }
			if(normal == null){
				SaveTexture(ref normal, new Color32(128,128,255,255),AssetDatabase.GetAssetPath(overlayFolder) +"/"+ "Base_Normal.png",diffuse.width,diffuse.height);
				Debug.LogWarning("No NormalMap was provided, creating a neutral one");
			}
			
			if(specular == null){
				SaveTexture(ref specular, new Color32(64,64,64,32),AssetDatabase.GetAssetPath(overlayFolder) +"/"+ "Base_specular.png",diffuse.width,diffuse.height);
				Debug.LogWarning("No Specular was provided, creating a neutral one");
			}
			
	        bool res = UMATextureImporterUtil.ConvertDefaultAssets_DiffuseAlpha_Normal_Specular(AssetDatabase.GetAssetPath(diffuse), AssetDatabase.GetAssetPath(normal), AssetDatabase.GetAssetPath(specular), GetAssetFolder(), GetAssetName(), AssetDatabase.GetAssetPath(overlayFolder), Shader.Find("UMA/Regular"));
	        if (res)
	        {
	            textures = new Texture[2] 
	            { 
	                AssetDatabase.LoadAssetAtPath(AssetDatabase.GetAssetPath(overlayFolder)+"/"+GetAssetFolder()+GetAssetName()+"_diffuse.png", typeof(Texture)) as Texture,
	                AssetDatabase.LoadAssetAtPath(AssetDatabase.GetAssetPath(overlayFolder)+"/"+GetAssetFolder()+GetAssetName()+"_normal.png", typeof(Texture)) as Texture
	            };
	        }
	        return res;
	    }
		
	    private string ProcessTextureTypeAndName(Texture2D tex)
	    {
	        var suffixes = new string[] { "_dif", "_spec", "_nor" };
	       
	        int index = 0;
	        foreach( var suffix in suffixes )
	        {
	            index = tex.name.IndexOf(suffix, System.StringComparison.InvariantCultureIgnoreCase);
	            if( index > 0 )
	            {
	                string name = tex.name.Substring(0,index);
					materialName = name;
	                return suffix;
	            }
	        }
	        return "";
	    }

		void SaveTexture(ref Texture2D dest, Color32 color,string baseTextureName,int width,int height){			
			Color32[] textureColor = new Color32[width*height]; 
			for(int i = 0; i < textureColor.Length; i++){
				textureColor[i] = color;
			}
			Texture2D texture = new Texture2D(diffuse.width,diffuse.height,TextureFormat.ARGB32,true);
			texture.SetPixels32(textureColor);
			texture.Apply();
			
			byte[] bytes = texture.EncodeToPNG();
			System.IO.File.WriteAllBytes(baseTextureName, bytes);
			AssetDatabase.ImportAsset(baseTextureName); 
			
			TextureImporter textureImporter = AssetImporter.GetAtPath(baseTextureName) as TextureImporter;
			var tempimporter = TextureImporter.GetAtPath(AssetDatabase.GetAssetPath(diffuse)) as TextureImporter;
			
			var settings = new TextureImporterSettings();
	        tempimporter.ReadTextureSettings(settings);
	        textureImporter.SetTextureSettings(settings);
			
			AssetDatabase.WriteImportSettingsIfDirty (baseTextureName);
			AssetDatabase.ImportAsset(baseTextureName, ImportAssetOptions.ForceUpdate);
			
			dest = AssetDatabase.LoadAssetAtPath(baseTextureName, typeof(Texture2D)) as Texture2D;
			Debug.Log(dest.width);
		}

	    [MenuItem("UMA/Legacy Texture Builder")]
	    public static void OpenUmaTexturePrepareWindow()
	    {
	        UmaLegacyMaterialBuilderWindow window = (UmaLegacyMaterialBuilderWindow)EditorWindow.GetWindow(typeof(UmaLegacyMaterialBuilderWindow));
#if !UNITY_4_6 && !UNITY_5_0
			window.titleContent.text = "UMA Legacy Material Builder";
#else
			window.title = "UMA Legacy Material Builder";
#endif
        }

		[MenuItem("UMA/Optimize Overlay Textures")]
		public static void OptimizeOverlayTextures()
		{
			foreach (var obj in Selection.objects)
			{
				var overlayData = obj as OverlayDataAsset;
				if (overlayData != null)
				{
					foreach (var textureObj in overlayData.textureList)
					{
						if (textureObj == null) continue;
						string file = AssetDatabase.GetAssetPath(textureObj);
						var importer = TextureImporter.GetAtPath(file) as TextureImporter;
						bool changed = false;
						if (importer.isReadable)
						{
							importer.isReadable = false;
							changed = true;
						}
						if (importer.filterMode != FilterMode.Point)
						{
							importer.filterMode = FilterMode.Point;
							changed = true;
						}
						if( changed )AssetDatabase.ImportAsset(file);
					}
				}
			}
			AssetDatabase.SaveAssets();
		}

		[MenuItem("UMA/Half Selected AtlasScale")]
		public static void HalfSelectedAtlasScale()
		{
			foreach (var obj in Selection.objects)
			{
				var go = (obj as GameObject);
				if (go != null)
				{
					var umaData = go.GetComponent<UMAData>();
					if (umaData != null)
					{
						umaData.atlasResolutionScale = umaData.atlasResolutionScale * 0.5f;
						umaData.Dirty(false, true, false);
					}
				}
			}
		}

		[MenuItem("UMA/Double Selected AtlasScale")]
		public static void DoubleSelectedAtlasScale()
		{
			foreach (var obj in Selection.objects)
			{
				var go = (obj as GameObject);
				if (go != null)
				{
					var umaData = go.GetComponent<UMAData>();
					if (umaData != null)
					{
						if (umaData.atlasResolutionScale < 1f)
						{
							umaData.atlasResolutionScale = umaData.atlasResolutionScale * 2f;
							umaData.Dirty(false, true, false);
						}
					}
				}
			}
		}

		[MenuItem("UMA/Tools/PNG/Set Alpha Opaque")]
	    public static void SetAlphaOpaqueMenuItem()
	    {
	        foreach (var obj in Selection.GetFiltered(typeof(Texture2D), SelectionMode.Assets))
	        {
	            string file = AssetDatabase.GetAssetPath(obj);
	            if (file.EndsWith(".png", System.StringComparison.InvariantCultureIgnoreCase))
	            {
	                var importer = TextureImporter.GetAtPath(file) as TextureImporter;
	                bool wasReadable = importer.isReadable;
	                if (!wasReadable)
	                {
	                    importer.isReadable = true;
	                    AssetDatabase.ImportAsset(file);
	                }

	                var texture = AssetDatabase.LoadAssetAtPath(file, typeof(Texture2D)) as Texture2D;
	                if (texture.format == TextureFormat.ARGB32)
	                {
	                    var pix = texture.GetPixels32();
	                    for (int i = 0; i < pix.Length; i++)
	                    {
	                        pix[i].a = 255;
	                    }
	                    texture.SetPixels32(pix);
	                }
	                else
	                {
	                    Color[] pix = texture.GetPixels();
	                    for (int i = 0; i < pix.Length; i++)
	                    {
	                        pix[i].a = 1;
	                    }
	                    texture.SetPixels(pix);
	                }
	                texture.Apply();
					System.IO.File.WriteAllBytes(file, texture.EncodeToPNG());
	                if (!wasReadable)
	                {
	                    importer = TextureImporter.GetAtPath(file) as TextureImporter;
	                    importer.isReadable = false;
	                }
	                AssetDatabase.ImportAsset(file);
	            }
	        }
	    }
        [MenuItem("UMA/Tools/Texture/Set Readable")]
	    public static void SetTextureReadableMenuItem()
	    {
	        foreach (var obj in Selection.GetFiltered(typeof(Texture2D), SelectionMode.Assets))
	        {
	            string file = AssetDatabase.GetAssetPath(obj);
	            var importer = TextureImporter.GetAtPath(file) as TextureImporter;
	            if (!importer.isReadable)
	            {
	                importer.isReadable = true;
	                AssetDatabase.ImportAsset(file);
	            }
	        }
		}
        [MenuItem("UMA/Tools/Texture/Clear Readable")]
	    public static void ClearTextureReadableMenuItem()
	    {
	        foreach (var obj in Selection.GetFiltered(typeof(Texture2D), SelectionMode.Assets))
	        {
	            string file = AssetDatabase.GetAssetPath(obj);
	            var importer = TextureImporter.GetAtPath(file) as TextureImporter;
	            if (importer.isReadable)
	            {
	                importer.isReadable = false;
	                AssetDatabase.ImportAsset(file);
	            }
	        }
	    }

	}
}
