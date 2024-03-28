using UnityEngine;
using UnityEditor;

namespace UMA.Examples
{
	[CustomEditor(typeof(BRDFLookupTexture))]
	internal class BRDFLookupTextureInspector : Editor
	{
		private bool changed = false;
		private bool previewRGB = true;
		
		private static string kDirectoryName = "Assets/UMA/Content/UMA/Textures/GeneratedTextures";
		private static string kExtensionName = "png";
		private static string kLookupTexturePropertyName = "_BRDFTex";
		
		private static int kTexturePreviewBorder = 8;
		private static string[] kTextureSizes = { "16", "32", "64", "128", "256" };
		private static int[] kTextureSizesValues = { 16, 32, 64, 128, 256 };
		

		private static Texture2D PersistLookupTexture (string assetName, Texture2D tex)
		{
			if (!System.IO.Directory.Exists (kDirectoryName))
            {
                System.IO.Directory.CreateDirectory (kDirectoryName);
            }

            string assetPath = System.IO.Path.Combine (kDirectoryName, assetName + "." + kExtensionName);
			bool newAsset = !System.IO.File.Exists (assetPath);
			
			System.IO.File.WriteAllBytes (assetPath, tex.EncodeToPNG());
			AssetDatabase.ImportAsset (assetPath, ImportAssetOptions.ForceUpdate);

			TextureImporter texSettings = AssetImporter.GetAtPath (assetPath) as TextureImporter;
			if (!texSettings)
			{
				// workaround for bug when importing first generated texture in the project
				AssetDatabase.Refresh ();
				AssetDatabase.ImportAsset (assetPath, ImportAssetOptions.ForceUpdate);
				texSettings = AssetImporter.GetAtPath (assetPath) as TextureImporter;
			}
			texSettings.textureCompression = TextureImporterCompression.Uncompressed;                             
			texSettings.wrapMode = TextureWrapMode.Clamp;
			if (newAsset)
            {
                AssetDatabase.ImportAsset (assetPath, ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.Refresh ();
			
			Texture2D newTex = AssetDatabase.LoadAssetAtPath (assetPath, typeof(Texture2D)) as Texture2D;		
			return newTex;
		}
		
		private void PersistLookupTexture ()
		{
			BRDFLookupTexture l = target as BRDFLookupTexture;
			if (!l)
            {
                return;
            }

            Material m = FindCompatibleMaterial (l);
			
			string assetName = (m ? m.name : l.gameObject.name) + kLookupTexturePropertyName;
			Texture2D persistentTexture = PersistLookupTexture (assetName, l.lookupTexture);
			
			if (m)
            {
                m.SetTexture (kLookupTexturePropertyName, persistentTexture);
            }
        }
		
		static Material FindCompatibleMaterial (BRDFLookupTexture l)
		{
			Renderer r = l.gameObject.GetComponent<Renderer>();
			if (!r)
            {
                return null;
            }

            Material m = r.sharedMaterial;
			if (m && m.HasProperty (kLookupTexturePropertyName))
            {
                return m;
            }

            return null;
		}

		public void OnEnable ()
		{
			BRDFLookupTexture l = target as BRDFLookupTexture;
			if (!l)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath (l.lookupTexture);
			if (path == "")
            {
                changed = true;
            }
        }
		
		public void OnDisable ()
		{
			// Access to AssetDatabase from OnDisable/OnDestroy results in a crash
			// otherwise would be nice to bake lookup texture when leaving asset
		}

		public override void OnInspectorGUI ()
		{
			BRDFLookupTexture l = target as BRDFLookupTexture;

			l.intensity = EditorGUILayout.Slider ("Intensity", l.intensity, 0f, 8f);

			EditorGUILayout.Space ();
			l.diffuseIntensity = EditorGUILayout.Slider ("Diffuse", l.diffuseIntensity, 0f, 2f);
			if (l.diffuseIntensity > 1e-6)
			{
				EditorGUI.indentLevel++;

				l.keyColor = EditorGUILayout.ColorField ("Key Color", l.keyColor);
				l.fillColor = EditorGUILayout.ColorField ("Fill Color", l.fillColor);
				l.backColor = EditorGUILayout.ColorField ("Back Color", l.backColor);
				l.wrapAround = EditorGUILayout.Slider ("Wrap Around", l.wrapAround, -1f, 1f);
				l.metalic = EditorGUILayout.Slider ("Metalic", l.metalic, 0f, 4f);

				EditorGUI.indentLevel--;
			}
			
			EditorGUILayout.Space ();
			l.specularIntensity = EditorGUILayout.Slider ("Specular", l.specularIntensity, 0f, 8f);
			if (l.specularIntensity > 1e-6)
			{
				EditorGUI.indentLevel++;
				l.specularShininess = 1f - EditorGUILayout.Slider ("Glossiness", 1f - l.specularShininess, 0f, 1f-0.03f);
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space ();
			l.translucency = EditorGUILayout.Slider ("Translucency", l.translucency, 0f, 1f);
			if (l.translucency > 1e-6)
			{
				EditorGUI.indentLevel++;
				l.translucentColor = EditorGUILayout.ColorField ("Color", l.translucentColor);
				EditorGUI.indentLevel--;
			}
			
			
			EditorGUILayout.Space ();
			GUILayout.BeginHorizontal ();
			EditorGUILayout.PrefixLabel ("Lookup Texture", "MiniPopup");
			l.lookupTextureWidth = EditorGUILayout.IntPopup (l.lookupTextureWidth, kTextureSizes, kTextureSizesValues, GUILayout.MinWidth(40));
			GUILayout.Label ("x");
			l.lookupTextureHeight = EditorGUILayout.IntPopup (l.lookupTextureHeight, kTextureSizes, kTextureSizesValues, GUILayout.MinWidth(40));
			GUILayout.FlexibleSpace ();
			GUILayout.EndHorizontal ();
			
			if (GUI.changed)
			{
				Undo.RecordObject (l, "BRDFTexture Params Change");
				changed = true;
			}
					
			// preview
			GUILayout.BeginHorizontal();
			l.fastPreview = EditorGUILayout.Toggle ("Fast Preview", l.fastPreview);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button (previewRGB? "RGB": "Alpha", "MiniButton", GUILayout.MinWidth(38)))
            {
                previewRGB = !previewRGB;
            }

            GUILayout.EndHorizontal();
			
			if (changed || !l.lookupTexture)
			{
				GUILayout.BeginHorizontal ();
				GUILayout.FlexibleSpace ();
				if (GUILayout.Button ("Bake", GUILayout.MinWidth (64)))
				{
					l.Bake ();
					PersistLookupTexture ();
					changed = false;
				}
				else
				{
					if (l.fastPreview)
                    {
                        l.Preview ();
                    }
                    else
                    {
                        l.Bake ();
                    }
                }
				GUILayout.EndHorizontal ();
			}
			
			Rect r = GUILayoutUtility.GetAspectRect (1.0f);
			r.x += kTexturePreviewBorder;
			r.y += kTexturePreviewBorder;
			r.width -= kTexturePreviewBorder * 2;
			r.height -= kTexturePreviewBorder * 2;
			if (previewRGB)
            {
                EditorGUI.DrawPreviewTexture (r, l.lookupTexture);
            }
            else
            {
                EditorGUI.DrawTextureAlpha (r, l.lookupTexture);
            }

            // save preview to disk
            if (GUI.changed && changed && l.lookupTexture && l.fastPreview)
            {
                PersistLookupTexture ();
            }
        }
	}
}
