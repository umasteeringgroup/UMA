using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UMA
{
	public static class FileUtils
	{
		public static string ReadAllText(string path)
		{
			using (var sr = new System.IO.StreamReader(path))
			{
				return sr.ReadToEnd();
			}
		}

		public static void WriteAllText(string path, string content)
		{
			using (var sw = new System.IO.StreamWriter(path, false))
			{
				sw.Write(content);
			}
		}

		internal static void WriteAllBytes(string path, byte[] content)
		{
			using (var sw = new System.IO.StreamWriter(path, false))
			{
				sw.Write(content);
			}
		}
	}
}
