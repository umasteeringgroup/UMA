using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public class UMAAssemblyDefinitionDependencyTests
    {
        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            public string name;
            public string[] references;
        }

        [Test]
        public void UmaCoreDoesNotReferenceEditorOnlyAssemblies()
        {
            string[] guids = AssetDatabase.FindAssets("UMA_Core t:AssemblyDefinitionAsset");
            Assert.That(guids, Is.Not.Empty, "Could not locate UMA_Core.asmdef.");

            string path = null;
            for (int i = 0; i < guids.Length; i++)
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.Equals(Path.GetFileName(candidate), "UMA_Core.asmdef", StringComparison.Ordinal))
                {
                    path = candidate;
                    break;
                }
            }

            Assert.That(path, Is.Not.Null, "Could not uniquely locate UMA_Core.asmdef.");
            AssemblyDefinitionData definition = JsonUtility.FromJson<AssemblyDefinitionData>(
                File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(path)));
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.name, Is.EqualTo("UMA_Core"));

            string[] references = definition.references ?? Array.Empty<string>();
            for (int i = 0; i < references.Length; i++)
            {
                string reference = references[i] ?? string.Empty;
                Assert.That(reference.IndexOf("Editor", StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                    $"UMA_Core must not reference editor-only assembly '{reference}'. " +
                    "Put editor integration behind a bridge owned by an editor assembly instead.");
            }
        }
    }
}
