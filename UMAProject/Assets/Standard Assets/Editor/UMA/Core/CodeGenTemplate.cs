using System;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace UMA
{
	public class CodeGenTemplate
	{
		public String Format;
		public StringBuilder sb;
		public String Name;
		public static IFormatProvider formatter;
		public string Tag;
		public void Append(IDictionary<string, object> data)
		{
			try
			{
				sb.AppendFormat(formatter, Format, data);
			}
			catch (FormatException fe)
			{
				Debug.LogError(fe);
				Debug.Log(Format);
			}
		}

		public static CodeGenTemplate[] ParseTemplates(string sourceDir, string pageTemplate)
		{
			List<CodeGenTemplate> res = new List<CodeGenTemplate>();
			foreach (var line in pageTemplate.Split('\r', '\n'))
			{
				if (line.StartsWith("//#TEMPLATE "))
				{
					var parsedLine = line.Split(' ');
					if (parsedLine.Length >= 3)
					{
						var filename = Path.Combine(sourceDir, parsedLine[2]);
						var tag = parsedLine.Length > 3 ? parsedLine[3] : "";
						if (!File.Exists(filename))
						{
							Debug.LogError("File not found: " + filename);
						}
						res.Add(new CodeGenTemplate() { Format = FileUtils.ReadAllText(filename), sb = new StringBuilder(), Name = parsedLine[1], Tag = tag });
					}
				}
			}
			return res.ToArray();
		}
	}
}
