using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint.Examples.Editor
{
    /// <summary>Reproducible, isolated captures of a real built UMA face. Never edits the avatar.</summary>
    public static class EyebrowFaceExamples
    {
        [MenuItem("UMA/Overlay Painter/Examples/Capture Eyebrows on Current UMA Face")]
        public static void CaptureCurrent()
        {
            string folder=EditorUtility.SaveFolderPanel("Eyebrow visual examples","Build/Validation/Eyebrows","");
            if(string.IsNullOrEmpty(folder))return;
            try {Capture(BrowPreset.Natural,2048,folder);EditorUtility.RevealInFinder(folder);}
            catch(Exception e){Debug.LogException(e);EditorUtility.DisplayDialog("Eyebrow examples",e.Message,"OK");}
        }

        public static string Capture(BrowPreset preset,int resolution,string folder)
        {
            var avatar=UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .FirstOrDefault(a=>a.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh!=null));
            if(avatar==null)throw new InvalidOperationException("Open an UMA scene with a built character first.");
            var renderer=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh!=null);
            Directory.CreateDirectory(folder);
            var owned=new List<UnityEngine.Object>();TextureSet set=null;PreviewRenderUtility preview=null;
            UnityEngine.Object Own(UnityEngine.Object o){owned.Add(o);return o;}
            try
            {
                var mesh=(Mesh)Own(new Mesh {name="UMA face validation snapshot"});renderer.BakeMesh(mesh);
                var surfaceObject=(GameObject)Own(new GameObject("Eyebrow raycast snapshot"){hideFlags=HideFlags.HideAndDontSave});
                // Eyelash cards can cover the entire brow region. Fit to skin only, and omit
                // existing hair/lash cards in the capture so they cannot masquerade as this groom.
                var skinSubmeshes=renderer.sharedMaterials.Select((m,i)=>(m,i)).Where(x=>x.m.shader.name.Contains("Skin")).Select(x=>x.i).ToArray();
                var collisionMesh=(Mesh)Own(UnityEngine.Object.Instantiate(mesh));collisionMesh.subMeshCount=1;
                collisionMesh.triangles=skinSubmeshes.SelectMany(mesh.GetTriangles).ToArray();
                var collider=surfaceObject.AddComponent<MeshCollider>();collider.sharedMesh=collisionMesh;
                var browBone=renderer.bones.FirstOrDefault(b=>b!=null&&b.name=="RightEyebrowMiddle");
                if(browBone==null)throw new InvalidOperationException("This example fitter needs UMA3 eyebrow bones. Place eyebrows manually on other rigs.");
                Vector3 brow=renderer.transform.InverseTransformPoint(browBone.position);
                float centerX=renderer.transform.InverseTransformPoint(renderer.bones.First(b=>b.name=="Head").position).x;
                float sign=Mathf.Sign(brow.x-centerX);float near=Mathf.Abs(brow.x-centerX)*.38f,far=Mathf.Abs(brow.x-centerX)*1.85f;
                var samples=new List<StrokeSample>();int submesh=-1;
                for(int i=0;i<=40;i++)
                {
                    float u=i/40f;float x=centerX+sign*Mathf.Lerp(near,far,u);
                    float y=brow.y+.008f+Mathf.Sin(u*Mathf.PI)*.004f-u*u*.004f;
                    var ray=new Ray(new Vector3(x,y,mesh.bounds.max.z+.1f),Vector3.back);
                    if(!collider.Raycast(ray,out var hit,2))throw new InvalidOperationException("Could not fit the example path to the face at "+u);
                    if(submesh<0){int triangle=hit.triangleIndex;foreach(int sm in skinSubmeshes){int n=mesh.GetTriangles(sm).Length/3;if(triangle<n){submesh=sm;break;}triangle-=n;}}
                    samples.Add(new StrokeSample(hit.point,hit.normal,hit.textureCoord,0,hit.triangleIndex){pressure=1,flowMultiplier=1,sizeMultiplier=1});
                }
                var faceMesh=(Mesh)Own(UnityEngine.Object.Instantiate(mesh));faceMesh.subMeshCount=1;faceMesh.triangles=mesh.GetTriangles(submesh);
                set=new TextureSet {persistentId="eyebrow-face-example",surface=new ReconstructedSurface {index=0,gameObject=surfaceObject,mesh=faceMesh,slotName="UMA Face",triangleIslands=new int[faceMesh.triangles.Length/3]}};
                foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                    set.channels.Add(channel,new TextureChannelTarget {channel=channel,format=RenderTextureFormat.ARGBHalf,
                        editable=new EditableTextureTarget(channel.ToString(),resolution,resolution,RenderTextureFormat.ARGBHalf,null,Color.clear)});
                var layer=set.AddSplineLayer("Eyebrows - "+preset);
                var s=EyebrowSettings.Preset(preset);if(sign<0)s.flipAcross=true;
                var settings=new TexturePaintPathGeneratorSettings {enabled=true,generatorId=EyebrowPathGenerator.GeneratorId,extensionJson=s.Save()};
                const float width=.009f;
                var brush=(BrushPreset)Own(ScriptableObject.CreateInstance<BrushPreset>());brush.size=width*.5f;brush.hardness=1;brush.flow=1;
                var context=new StrokeContext {textures=set,brush=brush,tool=TexturePaintTool.Paint,channel=TexturePaintChannel.Albedo,
                    color=Color.white,paintSource=TexturePaintBrushSource.Color,strength=1,projectionDepth=2,normalAngleLimit=100,
                    pathGenerator=settings,pathGeneratorContext=TexturePaintPathGeneratorContext.FromSamples(samples,width,resolution,false,false)};
                foreach(var channel in set.channels.Keys)context.channelSources[channel]=new TexturePaintChannelSourceSettings {source=TexturePaintBrushSource.Color,color=Color.white};
                var method=typeof(UMA.TexturePaint.Editor.TexturePaintStageWindow).GetMethod("BuildRibbonSegments",BindingFlags.Static|BindingFlags.NonPublic);
                var segments=(List<TexturePaintRibbonSegment>)method.Invoke(null,new object[]{samples,width*.5f,width,false,false});
                foreach(var original in segments.ToArray())
                {
                    var copy=original;copy.leftStartAlong.x=2*centerX-copy.leftStartAlong.x;copy.rightStartFlow.x=2*centerX-copy.rightStartFlow.x;
                    copy.leftEndAlong.x=2*centerX-copy.leftEndAlong.x;copy.rightEndFlow.x=2*centerX-copy.rightEndFlow.x;
                    copy.normalStartPressure.x=-copy.normalStartPressure.x;copy.normalEndPressure.x=-copy.normalEndPressure.x;segments.Add(copy);
                }
                var shader=Shader.Find("Hidden/UMA/TexturePaint/RibbonProjection");
                using(var engine=new PaintingEngine(null,null,null,shader))
                {
                    if(!engine.BeginStroke(context,TexturePaintSourceMode.SourceOverlay))throw new InvalidOperationException(engine.LastStrokeError);
                    bool applied=engine.ApplyRibbon(segments,samples,false,false);engine.EndStroke(applied);
                    if(!applied)throw new InvalidOperationException("Face ribbon raster failed.");
                }
                string stem=preset+"-"+resolution;
                foreach(var pair in layer.channels)Save(pair.Value.Front,Path.Combine(folder,stem+"-FaceUV-"+pair.Key+".png"));
                File.WriteAllText(Path.Combine(folder,stem+"-Settings.json"),s.Save());
                File.WriteAllText(Path.Combine(folder,stem+"-Path.json"),JsonUtility.ToJson(new SampleDocument {samples=samples.ToArray(),width=width,generator=settings},true));
                using(var prepared=(EyebrowPathGenerator.Prepared)new EyebrowPathGenerator().Prepare(settings,context.pathGeneratorContext))
                    foreach(var pair in prepared.raster.Channels)File.WriteAllBytes(Path.Combine(folder,stem+"-Transparent-"+pair.Key+".png"),pair.Value.EncodeToPNG());
                string statistics=EyebrowPathGenerator.LastStatistics;
                var materials=renderer.sharedMaterials.Select(m=>(Material)Own(new Material(m))).ToArray();
                var skin=materials[submesh];Texture source=skin.GetTexture("_BaseMap");
                var composite=(RenderTexture)Own(new RenderTexture(resolution,resolution,0,RenderTextureFormat.ARGBHalf));composite.Create();
                var blend=(Material)Own(new Material(Shader.Find("Hidden/UMA/EyebrowExampleComposite")));
                blend.SetTexture("_Brow",layer.channels[TexturePaintChannel.Albedo].Front);Graphics.Blit(source,composite,blend);skin.SetTexture("_BaseMap",composite);
                // Match roughness with the same authored coverage, without touching base skin elsewhere.
                var mask=skin.GetTexture("_MaskMap");
                if(mask!=null)
                {
                    var packed=(RenderTexture)Own(new RenderTexture(resolution,resolution,0,RenderTextureFormat.ARGBHalf));packed.Create();
                    blend.SetTexture("_Brow",layer.channels[TexturePaintChannel.Roughness].Front);blend.SetFloat("_Mask",1);Graphics.Blit(mask,packed,blend);skin.SetTexture("_MaskMap",packed);
                }
                var normal=skin.GetTexture("_BumpMap");
                if(normal!=null)
                {
                    var bumped=(RenderTexture)Own(new RenderTexture(resolution,resolution,0,RenderTextureFormat.ARGBHalf));bumped.Create();
                    blend.SetTexture("_Brow",layer.channels[TexturePaintChannel.NormalControl].Front);blend.SetFloat("_Normal",1);Graphics.Blit(normal,bumped,blend);skin.SetTexture("_BumpMap",bumped);
                }
                preview=new PreviewRenderUtility();preview.cameraFieldOfView=25;
                preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=10;preview.camera.clearFlags=CameraClearFlags.SolidColor;
                preview.camera.backgroundColor=new Color(.16f,.18f,.21f,1);
                preview.lights[0].intensity=1.7f;preview.lights[0].transform.rotation=Quaternion.Euler(25,205,0);
                preview.lights[1].intensity=1.1f;preview.lights[1].transform.rotation=Quaternion.Euler(340,145,0);
                preview.ambientColor=new Color(.3f,.3f,.3f,1);
                void Render(string suffix,Vector3 focus,float distance,int pixels)
                {
                    preview.camera.transform.position=focus+Vector3.forward*distance;preview.camera.transform.LookAt(focus);
                    preview.BeginStaticPreview(new Rect(0,0,pixels,pixels));
                    for(int i=0;i<materials.Length;i++)
                        if(!materials[i].name.Contains("Eyelashes")&&!materials[i].shader.name.Contains("Hair"))
                            preview.DrawMesh(mesh,Matrix4x4.identity,materials[i],i);
                    preview.Render(true);var image=preview.EndStaticPreview();
                    try{File.WriteAllBytes(Path.Combine(folder,stem+suffix+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(image);}
                }
                Render("-Face",new Vector3(centerX,brow.y-.035f,.045f),.55f,1024);
                Render("-Closeup",new Vector3(centerX,brow.y+.004f,.045f),.30f,1024);
                string report=$"{avatar.activeRace.name}; {renderer.sharedMesh.name}; material {submesh}; face texture {resolution}; eyebrow footprint {context.pathGeneratorContext.footprintPixels:F1} texels; {statistics}\nCPU {SystemInfo.processorType}; GPU {SystemInfo.graphicsDeviceName}; Unity {Application.unityVersion}";
                File.WriteAllText(Path.Combine(folder,stem+"-Report.txt"),report);return report;
            }
            finally
            {
                preview?.Cleanup();set?.Dispose();
                foreach(var o in owned.AsEnumerable().Reverse())if(o!=null)UnityEngine.Object.DestroyImmediate(o);
            }
        }
        [Serializable] private sealed class SampleDocument {public StrokeSample[] samples;public float width;public TexturePaintPathGeneratorSettings generator;}
        private static void Save(RenderTexture source,string path)
        {
            var previous=RenderTexture.active;var texture=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
            try{RenderTexture.active=source;texture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
