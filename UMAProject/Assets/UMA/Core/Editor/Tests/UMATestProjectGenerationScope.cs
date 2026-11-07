using System;
using System.IO;
using UnityEditor;

namespace UMA.Tests
{
    // Importing/deleting temporary test shaders asks IDE integrations to rewrite
    // the user's projects. Keep those files unchanged while these fixtures run,
    // including when an IDE holds a read lock. Do not suppress import errors.
    internal sealed class UMATestProjectGenerationScope : IDisposable
    {
        private static int activeScopes;
        private bool disposed;

        internal UMATestProjectGenerationScope() => activeScopes++;

        internal static string PreserveExistingFile(string path, string generatedContent)
        {
            if (activeScopes == 0 || !File.Exists(path)) return generatedContent;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, true);
            return reader.ReadToEnd();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            activeScopes--;
        }
    }

    internal sealed class UMATestProjectGenerationPostprocessor : AssetPostprocessor
    {
        private static string OnGeneratedCSProject(string path, string content) =>
            UMATestProjectGenerationScope.PreserveExistingFile(path, content);

        private static string OnGeneratedSlnSolution(string path, string content) =>
            UMATestProjectGenerationScope.PreserveExistingFile(path, content);
    }
}
