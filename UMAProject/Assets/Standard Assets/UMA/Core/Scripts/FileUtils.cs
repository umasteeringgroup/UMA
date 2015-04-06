using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
			using (var sw = new System.IO.StreamWriter(path, false))
			{
				sw.Write(content);
			}
		}

		/// <summary>
		/// Writes byte data to a file.
		/// </summary>
		/// <param name="path">File path.</param>
		/// <param name="content">Data.</param>
		public static void WriteAllBytes(string path, byte[] content)
		{
			using (var sw = new System.IO.StreamWriter(path, false))
			{
				sw.Write(content);
			}
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
	}
}
