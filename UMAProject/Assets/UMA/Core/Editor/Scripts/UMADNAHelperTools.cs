#if UNITY_EDITOR

using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;

namespace UMA.Editors
{
	public static class UMADNAHelperTools
	{
		static DictionaryCustomFormatter customFormatter;

		[MenuItem("UMA/Create DNA Helper Code")]
		static void CreateDNAHelperCode()
		{
			var destDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets/UMA/Generated/DNAHelpers");
			var sourceDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets/UMA/Core/Scripts/Editor/Templates");
			var baseTemplate = FileUtils.ReadAllText(Path.Combine(sourceDir, "UmaDna_Template.cs.txt"));
			var pageTemplate = FileUtils.ReadAllText(Path.Combine(sourceDir, "UmaDnaChild_Template.cs.txt"));

			var templates = CodeGenTemplate.ParseTemplates(sourceDir, baseTemplate);
			var pageTemplates = CodeGenTemplate.ParseTemplates(sourceDir, pageTemplate);

			customFormatter = new DictionaryCustomFormatter();
			CodeGenTemplate.formatter = customFormatter;

			var baseDnaType = typeof(UMADnaBase);
			var customData = new Dictionary<string, object>();
			customData.Add("ClassName", "");

			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (var dnaType in assembly.GetTypes())
				{
					if (DerivesFrom(dnaType, baseDnaType))
					{
						if (dnaType.Name == "UMADna" || dnaType.Name == "DynamicUMADnaBase")
                        {
                            continue;
                        }

                        customData["ClassName"] = dnaType.Name;
						foreach (var template in templates)
						{
							template.Append(customData);
						}
						if (dnaType.Name == "DynamicUMADna")
                        {
                            continue;
                        }

                        foreach (var template in pageTemplates)
						{
							template.sb.Length = 0;
						}
						CreateDNAHelperCode(dnaType, destDir, pageTemplate, pageTemplates);
					}
				}
			}

			foreach (var template in templates)
			{
				customData.Add(template.Name, template.sb);
			}

			CreateBaseDNAExtension(destDir, baseTemplate, customData);
			AssetDatabase.Refresh();
		}

		private static bool DerivesFrom(Type type, Type baseType)
		{
			Type parent = type.BaseType;
			while (parent != null)
			{
				if (parent == baseType)
                {
                    return true;
                }

                parent = parent.BaseType;
			}
			return false;
		}

		private static void CreateBaseDNAExtension(string destination, string formatString, Dictionary<string, object> customData)
		{
			FileUtils.WriteAllText(FindPathFor("UMADna", destination), String.Format(customFormatter, formatString, customData));
		}

		private static void CreateDNAHelperCode(Type dnaType, string destination, string formatString, CodeGenTemplate[] templates)
		{
			var scriptPath = FindPathFor(dnaType, destination);
			var customData = new Dictionary<string, object>();
			customData.Add("ClassName", dnaType.Name);
			customData.Add("FieldName", "");
			customData.Add("Index", 0);
			int index = 0;
			var fields = dnaType.GetFields();
			customData.Add("DnaEntries", fields.Length);
			foreach (var field in fields)
			{
				customData["FieldName"] = field.Name;
				customData["Index"] = index;
				foreach (var template in templates)
				{
					template.Append(customData);
				}
				index++;
			}
			foreach (var template in templates)
			{
				customData.Add(template.Name, template.sb);
			}
			FileUtils.WriteAllText(scriptPath, String.Format(customFormatter, formatString, customData));
		}

		private static string FindPathFor(Type dnaType, string destination)
		{
			return FindPathFor(dnaType.Name, destination);
		}

		private static string FindPathFor(string dnaTypeName, string destination)
		{
			var scriptPath = Path.Combine(destination, dnaTypeName + "_Generated.cs");

			var matchingScripts = AssetDatabase.FindAssets("t:MonoScript " + dnaTypeName);
			var desiredSuffix = "/" + dnaTypeName + ".cs";
			foreach (var guid in matchingScripts)
			{
				var scripts = AssetDatabase.GUIDToAssetPath(guid);
				if (scripts.EndsWith(desiredSuffix, StringComparison.InvariantCultureIgnoreCase))
				{
					scriptPath = scripts.Insert(scripts.Length - 3, "_Generated");
					break;
				}
			}
			FileUtils.EnsurePath(Path.GetDirectoryName(scriptPath));
			return scriptPath;
		}
	}
}
#endif
