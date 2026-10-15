using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using UMA.TexturePaint;
using UMA.TexturePaint.Examples;

// Companion example builder. Snapshots a selected UMA 3 face; never rebuilds the avatar.
// The display mask is intentionally illustrative; author production beard boundaries in the painter.
public static class StubbleExamples
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Folder = "Assets/UMA/OverlayPainterExamples/Stubble";
    static T Construct<T>(params object[] args) => (T)Activator.CreateInstance(typeof(T), Hidden, null, args, null);
    [MenuItem("UMA/Overlay Painter/Examples/Render Stubble On Selected UMA Face")]
    public static void CreateMenu()
    {
        try { Run(); EditorUtility.DisplayDialog("Stubble Examples", "Created four 2K presets, transparent textures and a 4K comparison in " + Folder, "OK"); }
        catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("Stubble Examples", e.Message, "OK"); }
        finally { EditorUtility.ClearProgressBar(); }
    }
    public static string Run()
    {
        try
        {
            UMA.TexturePaint.Editor.TexturePaintStubblePresets.CreateMissing();
            var reports = new List<string>();
            for (int i = 0; i < 4; i++) { EditorUtility.DisplayProgressBar("Stubble Examples", StubbleMakerPresets.Names[i], i / 5f); reports.Add(RunCase(i, 2048)); }
            EditorUtility.DisplayProgressBar("Stubble Examples", "4K comparison", .8f);
            reports.Add(RunCase(1, 4096));
            ConfigureImports();
            return string.Join("\n", reports);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    public static void ConfigureImports()
    {
        AssetDatabase.Refresh();
        foreach (string file in Directory.GetFiles(Folder))
        {
            string path = file.Replace('\\', '/');
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
            bool data = path.EndsWith("-Roughness.png", StringComparison.Ordinal) || path.EndsWith("-NormalControl.exr", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !data;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = path.EndsWith("-Albedo.png", StringComparison.Ordinal);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
    }
    static string RunCase(int preset, int size)
    {
        var version = AssetDatabase.LoadAssetAtPath<UMASettings>("Assets/UMA/InternalDataStore/InGame/Resources/UMASettings.asset");
        if (version == null) throw new Exception("Installed UMASettings missing");
        var avatar = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<DynamicCharacterAvatar>() : null;
        if (avatar == null || !avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.sharedMesh != null))
            throw new InvalidOperationException("Select a built UMA 3 character. This example snapshots its face without rebuilding or editing it.");
        var renderer = avatar.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.sharedMesh != null);
        Directory.CreateDirectory(Folder);
        var owned = new List<UnityEngine.Object>();
        T Own<T>(T obj) where T : UnityEngine.Object { owned.Add(obj); return obj; }
        PreviewRenderUtility preview = null;
        ProceduralMeshMaps maps = null;
        try
        {
            var mesh = Own(new Mesh()); renderer.BakeMesh(mesh);
            var go = Own(new GameObject("Stubble review snapshot") { hideFlags = HideFlags.HideAndDontSave });
            var skinIndices = renderer.sharedMaterials.Select((m,i) => (m,i)).Where(p => p.m != null && p.m.shader.name.Contains("Skin")).Select(p => p.i).ToArray();
            var collision = Own(UnityEngine.Object.Instantiate(mesh)); collision.subMeshCount = 1;
            collision.triangles = skinIndices.SelectMany(mesh.GetTriangles).ToArray();
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = collision;
            Vector3 brow = renderer.transform.InverseTransformPoint(renderer.bones.First(b => b != null && b.name == "RightEyebrowMiddle").position);
            float center = renderer.transform.InverseTransformPoint(renderer.bones.First(b => b != null && b.name == "Head").position).x;
            if (!collider.Raycast(new Ray(new Vector3(center + .035f, brow.y - .055f, mesh.bounds.max.z + .1f), Vector3.back), out var hit, 2))
                throw new Exception("Could not find cheek surface");
            int tri = hit.triangleIndex, submesh = -1;
            foreach (int sm in skinIndices) { int n = mesh.GetTriangles(sm).Length/3; if (tri < n) { submesh = sm; break; } tri -= n; }
            var face = Own(UnityEngine.Object.Instantiate(mesh)); face.subMeshCount = 1; face.triangles = mesh.GetTriangles(submesh);
            var surface = new ReconstructedSurface { index = 0, mesh = face, gameObject = go, triangleIslands = new int[face.triangles.Length/3] };
            string label = StubbleMakerPresets.Names[preset].Replace(" ", "-") + "-" + size;
            maps = ProceduralMeshMapBuilder.Build(surface, size, size, requestedMaps: TexturePaintMeshMapMask.WorldPosition | TexturePaintMeshMapMask.WorldNormal | TexturePaintMeshMapMask.SurfaceId);
            var mapDict = new Dictionary<string, TexturePaintReadOnlyMeshMap>();
            foreach (var p in new[] { (TexturePaintMeshMap.WorldPosition,maps.position), (TexturePaintMeshMap.WorldNormal,maps.worldNormal), (TexturePaintMeshMap.SurfaceId,maps.id) })
                mapDict.Add("review|mesh|" + (int)p.Item1, Construct<TexturePaintReadOnlyMeshMap>("review",p.Item1,size,size,p.Item2.GetPixels()));
            var infos = new Dictionary<string,TexturePaintReadOnlyChannelInfo>();
            var channels = new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl };
            foreach (var channel in channels) infos.Add("review|"+(int)channel,Construct<TexturePaintReadOnlyChannelInfo>("review",channel,size,size,false));
            var source = Construct<TexturePaintReadContextV2>(null,infos,mapDict,null,new List<string>{"review"},null,null,new Dictionary<string,TexturePaintAnatomy>{{"review",TexturePaintAnatomy.Capture(avatar.transform,renderer).Copy(renderer.transform.worldToLocalMatrix * avatar.transform.localToWorldMatrix)}});
            var plugin = Own(ScriptableObject.CreateInstance<StubbleMakerGeneratorPlugin>());
            using var host = new PluginHost();
            var parameters = host.CreateParameters(plugin);
            StubbleMakerPresets.Apply(parameters, preset);
            var context = Construct<TexturePaintCommandContextV2>(plugin.Descriptor,source,parameters,CancellationToken.None,null,512L*1024*1024,TexturePaintPluginTarget.LayerContent);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // Plugin worker code uses immutable snapshots and does not dispatch back to Unity.
            plugin.ExecuteAsync(context).GetAwaiter().GetResult();
            long generationMs = timer.ElapsedMilliseconds;
            var queued = (IEnumerable)typeof(TexturePaintCommandContextV2).GetMethod("SealAndSnapshot",Hidden).Invoke(context,null);
            var pixels = channels.ToDictionary(c=>c,c=>new Color[size*size]);
            foreach (var command in queued)
            {
                var type = command.GetType(); type.GetMethod("MaterializeCompactPixels").Invoke(command,null);
                var channel = (TexturePaintChannel)type.GetField("channel").GetValue(command);
                bool srgb = (TexturePaintPluginColorSpace)type.GetField("colorSpace").GetValue(command) == TexturePaintPluginColorSpace.SRGB;
                var rect = (RectInt)type.GetField("rect").GetValue(command);
                var compact = (Color32[])type.GetField("compactPixels").GetValue(command);
                var floating = (Color[])type.GetField("pixels").GetValue(command);
                for (int y=0;y<rect.height;y++) for(int x=0;x<rect.width;x++)
                    { Color value = compact != null ? (Color)compact[y*rect.width+x] : floating[y*rect.width+x]; pixels[channel][(y+rect.y)*size+x+rect.x] = srgb ? value.linear : value; }
            }
            // A simple display mask isolates lower-face skin; it is not an anatomical generator feature.
            var positions = maps.position.GetPixels(); var normals = maps.worldNormal.GetPixels();
            foreach(var channel in channels)
            {
                var data=pixels[channel];
                for(int i=0;i<data.Length;i++)
                {
                    var p=positions[i];float x=Mathf.Abs(p.r-center),y=brow.y-p.g;
                    float top=Mathf.Lerp(.067f,.048f,Mathf.Clamp01(x/.05f));
                    float alpha=Mathf.SmoothStep(0,1,(y-top)/.012f)*Mathf.SmoothStep(0,1,(.145f-y)/.014f);
                    alpha*=Mathf.SmoothStep(0,1,(normals[i].b-.5f)*3)*p.a;
                    float mouth=Mathf.Pow(x/.026f,2)+Mathf.Pow((y-.084f)/.014f,2);
                    alpha*=Mathf.SmoothStep(0,1,(mouth-.8f)/.5f);
                    data[i].a*=alpha;
                }
            }
            // Export the exact transparent generator channels used by the render.
            if (size == 2048)
            foreach (var channel in channels)
            {
                bool albedo = channel == TexturePaintChannel.Albedo;
                var export = Own(new Texture2D(size, size, TextureFormat.RGBAFloat, false, true));
                var colors = (Color[])pixels[channel].Clone();
                if (albedo) for (int i=0;i<colors.Length;i++) { float alpha=colors[i].a; colors[i]=colors[i].gamma; colors[i].a=alpha; }
                export.SetPixels(colors); export.Apply();
                File.WriteAllBytes(Folder+"/"+label+"-"+channel+(channel == TexturePaintChannel.NormalControl ? ".exr" : ".png"),
                    channel == TexturePaintChannel.NormalControl ? export.EncodeToEXR() : export.EncodeToPNG());
            }
            var draw=renderer.sharedMaterials.Select(m=>Own(new Material(m))).ToArray();
            var compositeShader = Shader.Find("Hidden/UMA/EyebrowExampleComposite");
            if (compositeShader == null) throw new InvalidOperationException("The OverlayPainter Examples composite shader is missing. Install the complete Examples companion package.");
            var skin=draw[submesh]; var blend=Own(new Material(compositeShader));
            foreach(var channel in channels)
            {
                var tex=Own(new Texture2D(size,size,TextureFormat.RGBAHalf,false,true)); tex.SetPixels(pixels[channel]);tex.Apply();
                string property=channel==TexturePaintChannel.Albedo?"_BaseMap":channel==TexturePaintChannel.Roughness?"_MaskMap":"_BumpMap";
                if(!skin.HasProperty(property)||skin.GetTexture(property)==null)continue;
                var rt=Own(new RenderTexture(size,size,0,RenderTextureFormat.ARGBHalf));rt.Create();
                blend.SetTexture("_Brow",tex);blend.SetFloat("_Mask",channel==TexturePaintChannel.Roughness?1:0);blend.SetFloat("_Normal",channel==TexturePaintChannel.NormalControl?1:0);
                Graphics.Blit(skin.GetTexture(property),rt,blend);skin.SetTexture(property,rt);
            }
            preview=new PreviewRenderUtility();preview.cameraFieldOfView=25;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=10;
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.16f,.18f,.21f,1);preview.ambientColor=new Color(.3f,.3f,.3f,1);
            preview.lights[0].intensity=1.7f;preview.lights[0].transform.rotation=Quaternion.Euler(25,205,0);
            preview.lights[1].intensity=1.1f;preview.lights[1].transform.rotation=Quaternion.Euler(340,145,0);
            void Render(string name,Vector3 focus,float distance,bool stubble)
            {
                preview.camera.transform.position=focus+Vector3.forward*distance;preview.camera.transform.LookAt(focus);
                preview.BeginStaticPreview(new Rect(0,0,1024,1024));
                for(int sm=0;sm<draw.Length;sm++) if(!draw[sm].shader.name.Contains("Hair")&&!draw[sm].name.Contains("Eyelashes"))
                    preview.DrawMesh(mesh,Matrix4x4.identity,stubble?draw[sm]:renderer.sharedMaterials[sm],sm);
                preview.Render(true);var png=preview.EndStaticPreview();
                try{File.WriteAllBytes(Folder+"/"+name+".png",png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
            }
            var focus=new Vector3(center,brow.y-.071f,.06f);
            Render(label+"-Face",focus,.6f,true);Render(label+"-Closeup",focus,.32f,true);
            var report=$"Project {Application.dataPath}; UMA {version.UMAVersion}; Unity {Application.unityVersion}\nStubble version {plugin.Descriptor.pluginVersion}; {label}; anatomical flow; {size}px face texture; {generationMs} ms generation\n{SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}\nSnapshot of {avatar.activeRace.name}, {renderer.name}, material {submesh}. Existing EyebrowExampleComposite shader uses unit relief. Display-only lower-face mask; no avatar/document changes.\n";
            File.WriteAllText(Folder+"/"+label+".txt",report);File.WriteAllText(Folder+"/"+label+"-Parameters.json",JsonUtility.ToJson(parameters,true));return report;
        }
        finally { preview?.Cleanup();maps?.Dispose();foreach(var obj in owned.AsEnumerable().Reverse())if(obj!=null)UnityEngine.Object.DestroyImmediate(obj); }
    }
}
