//	============================================================
//	Name:		UmaTexturePrepareWindow
//	Author: 	Joen Joensen (@UnLogick)
//	============================================================


using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;
using UMA;

namespace UMAEditor
{
	public class UmaTexturePrepareWindow : EditorWindow {

	    public Texture2D diffuse;
	    public Texture2D normal;
	    public Texture2D specular;

	    public string materialName;
	    public UnityEngine.Object overlayFolder;
	    public UnityEngine.Object slotFolder;
	    public UnityEngine.Object relativeFolder;
	    public SkinnedMeshRenderer racePrefab;
	    public SkinnedMeshRenderer slotMesh;
	    public Material slotMaterial;
	    public bool processAutomatically;
	    public OverlayData textureOverride;

	    private string normalWarning;
	    private Texture2D privateNormal;
	    private Texture2D[] textures; // reset for each processing

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
	        
	        EditorGUILayout.Space();

	        racePrefab = EditorGUILayout.ObjectField("Race Prefab SkinnedMeshRenderer", racePrefab, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
	        slotMesh = EditorGUILayout.ObjectField("Slot Mesh SkinnedMeshRenderer", slotMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
	        slotMaterial = EditorGUILayout.ObjectField("MaterialSample", slotMaterial, typeof(Material), false) as Material;
	        slotFolder = EditorGUILayout.ObjectField("Slot Folder", slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
	        EnforceFolder(ref slotFolder);
	        
	        if (GUILayout.Button("Create Slot"))
	        {
	            Debug.Log("Processing...");
	            if (CreateSlot() != null)
	            {
	                Debug.Log("Success.");
	            }
	        }
	      
	        GUILayout.Label("", EditorStyles.boldLabel);
	        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
	        GUI.Box(dropArea, "Drag textures and meshes here");
	        GUILayout.Label("Automatic Drag and Drop processing", EditorStyles.boldLabel);
	        processAutomatically = EditorGUILayout.Toggle("Process Drops", processAutomatically);
	        relativeFolder = EditorGUILayout.ObjectField("Relative Folder", relativeFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
	        EnforceFolder(ref relativeFolder);

	        DropAreaGUI(dropArea);
	    }

	    private SlotData CreateSlot()
	    {
			if(materialName == null || materialName == ""){
				Debug.LogError("materialName must be specified.");
	            return null;
			}
			
	        return CreateSlot_Internal();
	    }

	    private SlotData CreateSlot_Internal()
	    {
//	        var material = slotMaterial ?? AssetDatabase.LoadAssetAtPath("Assets/UMA_Assets/MaterialSamples/UMABaseShaderSample.mat", typeof(Material)) as Material;
			var material = slotMaterial;
			if (material == null) material = AssetDatabase.LoadAssetAtPath("Assets/UMA_Assets/MaterialSamples/UMABaseShaderSample.mat", typeof(Material)) as Material;
			if(materialName == null || materialName == ""){
				Debug.LogError("materialName must be specified.");
	            return null;
			}
			
			if (material == null)
	        {
	            Debug.LogError("Couldn't locate default material at Assets/UMA_Assets/MaterialSamples/UMABaseShaderSample.mat and no material specified.");
	            return null;
	        }

	        if (slotFolder == null)
	        {
	            Debug.LogError("Slot folder not supplied");
	            return null;
	        }

	        if (slotMesh == null)
	        {
	            Debug.LogError("Slot Mesh not supplied.");
	            return null;
	        }
            Debug.Log("Slot Mesh: " + slotMesh.name, slotMesh.gameObject); 
	        SlotData slot = UMATextureImporterUtil.CreateSlotData(AssetDatabase.GetAssetPath(slotFolder), GetAssetFolder(), GetAssetName(), slotMesh, material, racePrefab);
	        return slot;
	    }

	    private OverlayData CreateOverlay()
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

	    private OverlayData CreateOverlay_Internal()
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
	            textures = textureOverride.textureList;
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
	            textures = new Texture2D[2] 
	            { 
	                AssetDatabase.LoadAssetAtPath(AssetDatabase.GetAssetPath(overlayFolder)+"/"+GetAssetFolder()+GetAssetName()+"_diffuse.png", typeof(Texture2D)) as Texture2D,
	                AssetDatabase.LoadAssetAtPath(AssetDatabase.GetAssetPath(overlayFolder)+"/"+GetAssetFolder()+GetAssetName()+"_normal.png", typeof(Texture2D)) as Texture2D
	            };
	        }
	        return res;
	    }

	    private void DropAreaGUI(Rect dropArea)
	    {
	        var evt = Event.current;

	        if (evt.type == EventType.DragUpdated)
	        {
	            if (dropArea.Contains(evt.mousePosition))
	            {
	                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
	            }
	        }

	        if (evt.type == EventType.DragPerform)
	        {
	            if (dropArea.Contains(evt.mousePosition))
	            {
	                DragAndDrop.AcceptDrag();
	                int receivedMask = 0;
	                //string name = "";
	                StringBuilder errors = new StringBuilder();

	                UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
	                for (int i = 0; i < draggedObjects.Length; i++)
	                {
	                    GameObject go = draggedObjects[i] as GameObject;
	                    Texture2D tex = draggedObjects[i] as Texture2D;
	                    SkinnedMeshRenderer mesh = draggedObjects[i] as SkinnedMeshRenderer;

	                    if (go != null)
	                    {
	                        mesh = go.GetComponent<SkinnedMeshRenderer>();
	                        if (mesh != null)
	                        {
	                            slotMesh = mesh;
	                            GetMaterialName(go.name, go);
	                            if (processAutomatically && textureOverride != null)
	                            {
	                                receivedMask = receivedMask & 0x000F;
	                                // we got texture override and we got mesh, go go go.
	                                if (CreateSlot() != null)
	                                {
	                                    mesh = null; // lets not process this one again
	                                    Debug.Log("Batch importer processed mesh: " + materialName);
	                                }
	                            }
	                            else
	                            {
	                                receivedMask = receivedMask | 0x0010;
	                            }
	                            continue;
	                        }
	                    }
	                    if (tex != null)
	                    {
	                        string textureType = ProcessTextureTypeAndName(tex);
	                        if (textureType == "_dif")
	                        {
	                            // looks like a diffuse texture
	                            receivedMask = receivedMask | 0x0001;
	                            diffuse = tex;
	                        }
	                        else if (textureType == "_spec")
	                        {
	                            // looks like a specular texture
	                            receivedMask = receivedMask | 0x0002;
	                            specular = tex;
	                        }
	                        else if (textureType == "_nor")
	                        {
	                            // looks like a normal map
	                            receivedMask = receivedMask | 0x0004;
	                            normal = tex;
	                        }
	                        else
	                        {
	                            errors.AppendFormat("unrecognizable texture {0}\n", tex.name);
	                        }
	                    }
	                    else if (mesh != null)
	                    {
	                        slotMesh = mesh;
	                        receivedMask = receivedMask | 0x0010;
	                    }
	                    else
	                    {
	                        errors.AppendFormat("unrecognizable drag and drop object {0}\n", draggedObjects[i]);
	                    }
	                }
	                if (receivedMask == 0x0017 && processAutomatically)
	                {
	                    Debug.Log("Drag and Drop initiated processing, Creating Slot...");
	                    if (CreateSlot() != null)
	                    {
	                        Debug.Log("Success.");
	                    }
	                }
	               
	                if (errors.Length > 0)
	                {
	                    Debug.LogError(errors.ToString());
	                }
	            }
	        }
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
	                GetMaterialName(name, tex);
	                return suffix;
	            }
	        }
	        return "";
	    }

	    private void GetMaterialName(string name, UnityEngine.Object obj)
	    {
	        if (relativeFolder != null)
	        {
	            var relativeLocation = AssetDatabase.GetAssetPath(relativeFolder);
	            var assetLocation = AssetDatabase.GetAssetPath(obj);
	            if (assetLocation.StartsWith(relativeLocation, System.StringComparison.InvariantCultureIgnoreCase))
	            {
	                string temp = assetLocation.Substring(relativeLocation.Length + 1); // remove the prefix
	                temp = temp.Substring(0, temp.LastIndexOf('/') + 1); // remove the asset name
	                materialName = temp + name; // add the cleaned name
	            }
	        }
	        else
	        {
	            materialName = name;
	        }
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
			File.WriteAllBytes(baseTextureName, bytes);
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

	    [MenuItem("UMA/Material Builder")]
	    public static void OpenUmaTexturePrepareWindow()
	    {
	        UmaTexturePrepareWindow window = (UmaTexturePrepareWindow)EditorWindow.GetWindow(typeof(UmaTexturePrepareWindow));
	        window.title = "MaterialBuilder";
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
			foreach (var obj in Selection.GetFiltered(typeof(OverlayLibrary), SelectionMode.Editable))
			{
				var overlays = (obj as OverlayLibrary).GetAllOverlays();
				foreach (var overlay in overlays)
				{
					foreach (var texture in overlay.textureList)
					{
						if (texture != null)
						{
							string file = AssetDatabase.GetAssetPath(texture);
							var importer = TextureImporter.GetAtPath(file) as TextureImporter;
							if (!importer.isReadable)
							{
								importer.isReadable = true;
								AssetDatabase.ImportAsset(file);
							}
						}
					}
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