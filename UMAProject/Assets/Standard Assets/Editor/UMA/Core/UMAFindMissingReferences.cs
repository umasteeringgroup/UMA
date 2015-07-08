#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UMAEditor
{
	/// <summary>
	/// After removing the UMA.dll from the UMA open source project. Unity3d lost all references to essential scripts such as SlotData.cs, OverlayData.cs ...
	/// </summary>
	public class FindMissingReferences
	{
		[MenuItem("UMA/Find Missing References")]
		static void Replace()
		{
#if UNITY_WEBPLAYER 
			Debug.LogError("MenuItem - UMA/Find Missing References does not work when the build target is set to Webplayer, we need the full mono framework available.");
 #if UMA2_LEAN_AND_CLEAN 
			Debug.LogError("MenuItem - UMA/Find Missing References does not work with the define UMA2_LEAN_AND_CLEAN, we need all legacy fields available.");
 #endif
#else
 #if UMA2_LEAN_AND_CLEAN 
			Debug.LogError("MenuItem - UMA/Find Missing References does not work with the define UMA2_LEAN_AND_CLEAN, we need all legacy fields available.");
 #else

			List<UnityReference> references = new List<UnityReference>();
			var slotFilePaths = new List<string>();
			var overlayFilePaths = new List<string>();

			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "1772484567", FindAssetGuid("OverlayDataAsset", "cs"), "11500000") { updatedFiles = overlayFilePaths }); // OverlayData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1278852528", FindAssetGuid("SlotDataAsset", "cs"), "11500000") { updatedFiles = slotFilePaths }); // SlotData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-335686737", FindAssetGuid("RaceData", "cs"), "11500000")); // RaceData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1571472132", FindAssetGuid("UMADefaultMeshCombiner", "cs"), "11500000")); // UMADefaultMeshCombiner.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1550055707", FindAssetGuid("UMAData", "cs"), "11500000")); // UMAData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1708169498", FindAssetGuid("UmaTPose", "cs"), "11500000")); // UmaTPose.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1175167296", FindAssetGuid("TextureMerge", "cs"), "11500000")); // TextureMerge.cs
			references.Add(new UnityReference("7e407fe772026ae4cb2f52b8b5567db5", "11500000", FindAssetGuid("OverlayDataAsset", "cs"), "11500000") { updatedFiles = overlayFilePaths }); // OverlayData.cs
			references.Add(new UnityReference("a248d59ac2f3fa14b9c2894f47000560", "11500000", FindAssetGuid("SlotDataAsset", "cs"), "11500000") { updatedFiles = slotFilePaths }); // SlotData.cs

			ReplaceReferences(Application.dataPath, references);

			if (slotFilePaths.Count > 0 || overlayFilePaths.Count > 0)
			{
				UMA.UMAMaterial material = AssetDatabase.LoadAssetAtPath("Assets/UMA_Assets/MaterialSamples/DefaultUMAMaterial.asset", typeof(UMA.UMAMaterial)) as UMA.UMAMaterial;
				if (material == null) material = AssetDatabase.LoadAssetAtPath("Assets/UMA_Assets/MaterialSamples/UMALegacy.asset", typeof(UMA.UMAMaterial)) as UMA.UMAMaterial;

				foreach (var slotFilePath in slotFilePaths)
				{
					var correctedAssetDatabasePath = "Assets" + slotFilePath.Substring(Application.dataPath.Length);
					AssetDatabase.ImportAsset(correctedAssetDatabasePath);
					var slotData = AssetDatabase.LoadAssetAtPath(correctedAssetDatabasePath, typeof(UMA.SlotDataAsset)) as UMA.SlotDataAsset;
#pragma warning disable 618
					if (slotData.meshRenderer != null)
					{
						UMASlotProcessingUtil.OptimizeSlotDataMesh(slotData.meshRenderer);
						slotData.UpdateMeshData(slotData.meshRenderer);
						slotData.material = material;
						EditorUtility.SetDirty(slotData);
					}
#pragma warning restore 618
				}
				foreach (var overlayFilePath in overlayFilePaths)
				{
					var correctedAssetDatabasePath = "Assets" + overlayFilePath.Substring(Application.dataPath.Length);
					AssetDatabase.ImportAsset(correctedAssetDatabasePath);
					var overlayData = AssetDatabase.LoadAssetAtPath(correctedAssetDatabasePath, typeof(UMA.OverlayDataAsset)) as UMA.OverlayDataAsset;
					overlayData.material = material;
					EditorUtility.SetDirty(overlayData);
				}
			}
 #endif
#endif
		}

		static string FindAssetGuid(string assetName, string assetExtension)
		{
			string fullAssetName = assetName + "." + assetExtension;
			string[] guids = AssetDatabase.FindAssets(assetName);

			foreach (string guid in guids)
			{
				string assetPath = AssetDatabase.GUIDToAssetPath(guid);
				if (assetPath.EndsWith(fullAssetName))
				{
					return guid;
				}
			}

			// make sure that we don't continue and break anything!
			throw new System.Exception("Unable to find guid for " + fullAssetName);
		}

		static void ReplaceReferences(string assetFolder, List<UnityReference> r)
		{
#if !UNITY_WEBPLAYER 
			if (EditorSettings.serializationMode != SerializationMode.ForceText)
			{
				Debug.LogError("Failed to replace refrences, you must set serialzation mode to text. Edit -> Project Settings -> Editor -> Asset Serialziation = Force Text");
				return;
			}

			string[] files = Directory.GetFiles(assetFolder, "*", SearchOption.AllDirectories);
			for (int i = 0; i < files.Length; i++)
			{
				string file = files[i];

				if (EditorUtility.DisplayCancelableProgressBar("Update to UMA2", file, i / (float)files.Length))
				{
					EditorUtility.ClearProgressBar();
					return;
				}

				if (file.EndsWith(".asset") || file.EndsWith(".prefab") || file.EndsWith(".unity"))
				{
					ReplaceReferencesInFile(file, r);
					FindNotReplacedReferences(file, "e20699a64490c4e4284b27a8aeb05666");
				}
			}

			EditorUtility.ClearProgressBar();
#endif
		}

		static bool IsValidHexString(IEnumerable<char> chars)
		{
			bool isHex; 
			foreach(var c in chars)
			{
				isHex = ((c >= '0' && c <= '9') || 
				         (c >= 'a' && c <= 'f') || 
				         (c >= 'A' && c <= 'F'));
				
				if(!isHex)
					return false;
			}
			return true;
		}

		static bool IsHexJunk(string junk)
		{
			if ((junk.Length % 2) == 1)
			{
				// can't be an odd number of characters...
				return false; 
			}
			if (IsValidHexString(junk))
			{
				return true;
			}
			return false;
		}

		static string HexToString(string hexstr)
		{ 
			var sb = new System.Text.StringBuilder(hexstr.Length/2);
			for (int i=0;i<hexstr.Length;i+=2)
			{
				string hex = hexstr.Substring(i,2);
				int value = System.Convert.ToInt32(hex, 16);
				sb.Append(System.Char.ConvertFromUtf32(value));
			}
			return sb.ToString();
		}

		static void ReplaceReferencesInFile(string filePath, List<UnityReference> references)
		{
			var fileContents = UMA.FileUtils.ReadAllText(filePath);

			bool match = false;

			foreach (UnityReference r in references)
			{
				Regex regex = new Regex(@"fileID: " + r.srcFileId + ", guid: " + r.srcGuid);
				if (regex.IsMatch(fileContents))
				{
					if (r.updatedFiles != null)
					{
						r.updatedFiles.Add(filePath);
					}
					fileContents = regex.Replace(fileContents, "fileID: " + r.dstFileId + ", guid: " + r.dstGuid);
					match = true;
					Debug.Log("Replaced: " + filePath);
				}
			}

			// fix name if it's slotdata or overlaydata
			if (match) {
				string[] lines = fileContents.Split ('\n');
				foreach (string line in lines) {
					string[] l = line.Trim ('\r', '\n', ' ').Split (':');

					if (l.Length > 1) {
						if (l [0].StartsWith ("overlayName") || l [0].StartsWith ("slotName")) {
							// is it hex junk?
							string HexJunk = l [1].Trim ();
							if (IsHexJunk (HexJunk)) {
								string NewJunk = HexToString(HexJunk);
								fileContents = fileContents.Replace(HexJunk,NewJunk);
							}
						}
					}
				}
			}

			if (match)
			{
				UMA.FileUtils.WriteAllText(filePath, fileContents);
			}
		}

		/// <summary>
		/// Just to make sure that all references are replaced.
		/// </summary>
		static void FindNotReplacedReferences(string filePath, string guid)
		{
			var fileContents = UMA.FileUtils.ReadAllText(filePath);

			// -?        number can be negative
			// [0-9]+    1-n numbers
			Regex.Replace(fileContents, @"fileID: -?[0-9]+, guid: " + guid,
						  (match) =>
						  {
							  //if (match.Value != "fileID: 11500000, guid: " + guid)
							  //{
							  //	Debug.LogWarning("NotReplaced: " + match.Value + "  " + filePath);
							  //}
							  Debug.LogWarning("NotReplaced: " + match.Value + "  " + filePath);
							  return match.Value;
						  });
		}


		public class UnityReference
		{
			public UnityReference(string srcGuid, string srcFileId, string dstGuid, string dstFileId)
			{
				this.srcGuid = srcGuid;
				this.srcFileId = srcFileId;
				this.dstGuid = dstGuid;
				this.dstFileId = dstFileId;
			}
			public List<string> updatedFiles;
			public string srcGuid;
			public string srcFileId;
			public string dstGuid;
			public string dstFileId;
		}
	}
}
#endif
