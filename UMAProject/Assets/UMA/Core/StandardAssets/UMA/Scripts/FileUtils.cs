using UnityEngine;
using System.IO;

namespace UMA
{
	/// <summary>
	/// File utilities.
	/// </summary>
	public static class FileUtils
	{
		/// <summary>
		/// Reads all text from a file.
		/// </summary>
		/// <returns>The text.</returns>
		/// <param name="path">File path.</param>
		public static string ReadAllText(string path)
		{
			using (var sr = new System.IO.StreamReader(path))
			{
				return sr.ReadToEnd();
			}
		}

		/// <summary>
		/// Writes text to a file.
		/// </summary>
		/// <param name="path">File path.</param>
		/// <param name="content">Text.</param>
		public static void WriteAllText(string path, string content)
		{
			System.IO.File.WriteAllText(path, content);
		}

		/// <summary>
		/// Writes byte data to a file.
		/// </summary>
		/// <param name="path">File path.</param>
		/// <param name="content">Data.</param>
		public static void WriteAllBytes(string path, byte[] content)
		{
			System.IO.File.WriteAllBytes(path, content);
		}

		/// <summary>
		/// Creates a directory if it is missing.
		/// </summary>
		/// <param name="path">File path.</param>
		public static void EnsurePath(string path)
		{
			if (System.IO.Directory.Exists(path)) return;
			System.IO.Directory.CreateDirectory(path);
		}

		/// <summary>
		/// Returns the UMAInternalDataStore folder path. Use this to store generated data files that UMA needs, to make it less likely they will be deleted or moved by users.
		/// </summary>
		/// <param name="fullPath">if true returns the full system path, otherwise returns path starting with "Assets/"</param>
		/// <param name="editorOnly">if false the path will be the Resources folder inside "UMAInternalDataStore" and will be included in the game.</param>
		public static string GetInternalDataStoreFolder(bool fullPath = false, bool editorOnly = true)
		{
			var settingsFolderPath = "";
			if (fullPath)
				settingsFolderPath = Path.Combine(Application.dataPath, Path.Combine("UMA", "InternalDataStore"));
			else
				settingsFolderPath = Path.Combine("Assets", Path.Combine("UMA", "InternalDataStore"));
			if (editorOnly)
			{
				settingsFolderPath = Path.Combine(settingsFolderPath, "InEditor");
			}
			else
			{
				settingsFolderPath = Path.Combine(settingsFolderPath, Path.Combine("InGame", "Resources"));
			}
			if (!Directory.Exists(settingsFolderPath))
				Directory.CreateDirectory(settingsFolderPath);
			if (fullPath)
				settingsFolderPath = Path.GetFullPath(settingsFolderPath);
			return settingsFolderPath;
		}
	}
}
