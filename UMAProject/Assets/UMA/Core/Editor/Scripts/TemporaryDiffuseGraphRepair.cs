#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class TemporaryDiffuseGraphRepair
{
    private const string Folder = "tmp/DiffuseGraphRepair/";
    private const string Asset = "Assets/UMA/SRP/ShaderGraphs/Materials/UMA_SG_Diffuse.shadergraph";
    static TemporaryDiffuseGraphRepair() { EditorApplication.update += RunWhenReady; }

    private static void RunWhenReady()
    {
        if (!File.Exists(Folder + "pending"))
        {
            EditorApplication.update -= RunWhenReady;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= RunWhenReady;
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a =>
                a.GetType("UnityEditor.ShaderGraph.GraphData") != null);
            var graphType = assembly.GetType("UnityEditor.ShaderGraph.GraphData");
            var graph = Activator.CreateInstance(graphType, true);
            graphType.GetProperty("assetGuid").SetValue(graph, AssetDatabase.AssetPathToGUID(Asset));
            var multiJson = assembly.GetType("UnityEditor.ShaderGraph.Serialization.MultiJson");
            multiJson.GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(graphType).Invoke(null, new object[] {
                    graph, File.ReadAllText(Folder + "normalized.json"), null, false });
            var utilities = assembly.GetType("UnityEditor.ShaderGraph.FileUtilities");
            var saved = utilities.GetMethod("WriteShaderGraphToDisk",
                BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { (object)Asset, graph });
            if (saved == null) throw new IOException("Shader Graph serialization did not save the graph.");
            AssetDatabase.ImportAsset(Asset, ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Asset);
            if (shader == null) throw new InvalidOperationException("Reimport did not produce a Shader.");
            if (ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader imported but compilation reports errors.");
            File.WriteAllText(Folder + "result.txt", "SUCCESS: " + shader.name +
                "\nUnity: " + Application.unityVersion);
            Debug.Log("Diffuse Shader Graph repaired and reimported: " + shader.name);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "result.txt", error.ToString());
            Debug.LogException(error);
        }
        finally { File.Delete(Folder + "pending"); }
    }
}
#endif
