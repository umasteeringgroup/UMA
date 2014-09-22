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
			List<UnityReference> references = new List<UnityReference>();

			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "1772484567", FindAssetGuid("OverlayData", "cs"), "11500000")); // OverlayData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1278852528", FindAssetGuid("SlotData", "cs"), "11500000")); // SlotData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-335686737", FindAssetGuid("RaceData", "cs"), "11500000")); // RaceData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1571472132", FindAssetGuid("UMADefaultMeshCombiner", "cs"), "11500000")); // UMADefaultMeshCombiner.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-946187639", FindAssetGuid("UMALegacyMeshCombiner", "cs"), "11500000")); // UMALegacyMeshCombiner.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1550055707", FindAssetGuid("UMAData", "cs"), "11500000")); // UMAData.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1708169498", FindAssetGuid("UmaTPose", "cs"), "11500000")); // UmaTPose.cs
			references.Add(new UnityReference("e20699a64490c4e4284b27a8aeb05666", "-1175167296", FindAssetGuid("TextureMerge", "cs"), "11500000")); // TextureMerge.cs

			ReplaceReferences(Application.dataPath, references);
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
			if (EditorSettings.serializationMode != SerializationMode.ForceText)
			{
				Debug.LogError("Failed to replace refrences, you must set serialzation mode to text. Edit -> Project Settings -> Editor -> Asset Serialziation = Force Text");
				return;
			}

			string[] files = Directory.GetFiles(assetFolder, "*", SearchOption.AllDirectories);
			for (int i = 0; i < files.Length; i++)
			{
				string file = files[i];

				if (EditorUtility.DisplayCancelableProgressBar("Replace UMA DLL", file, i / (float)files.Length))
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
		}

		static void ReplaceReferencesInFile(string filePath, List<UnityReference> references)
		{
			var fileContents = System.IO.File.ReadAllText(filePath);

			bool match = false;

			foreach (UnityReference r in references)
			{
				Regex regex = new Regex(@"fileID: " + r.srcFileId + ", guid: " + r.srcGuid);
				if (regex.IsMatch(fileContents))
				{
					fileContents = regex.Replace(fileContents, "fileID: " + r.dstFileId + ", guid: " + r.dstGuid);
					match = true;
					Debug.Log("Replaced: " + filePath);
				}
			}

			if (match)
			{
				System.IO.File.WriteAllText(filePath, fileContents);
			}
		}

		/// <summary>
		/// Just to make sure that all references are replaced.
		/// </summary>
		static void FindNotReplacedReferences(string filePath, string guid)
		{
			var fileContents = System.IO.File.ReadAllText(filePath);

			// -?        number can be negative
			// [0-9]+    1-n numbers
			Regex.Replace(fileContents, @"fileID: -?[0-9]+, guid: " + guid,
						  (match) =>
						  {
							  if (match.Value != "fileID: 11500000, guid: " + guid)
							  {
								  Debug.LogWarning("NotReplaced: " + match.Value + "  " + filePath);
							  }
							  return match.Value;
						  });
		}


		class UnityReference
		{
			public UnityReference(string srcGuid, string srcFileId, string dstGuid, string dstFileId)
			{
				this.srcGuid = srcGuid;
				this.srcFileId = srcFileId;
				this.dstGuid = dstGuid;
				this.dstFileId = dstFileId;
			}

			public string srcGuid;
			public string srcFileId;
			public string dstGuid;
			public string dstFileId;
		}
	}
}
