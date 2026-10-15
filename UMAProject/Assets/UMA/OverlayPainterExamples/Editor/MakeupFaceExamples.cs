using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UMA.CharacterSystem;
using UMA.TexturePaint.Editor;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Examples.Editor
{
    /// <summary>Isolated captures on an actual built UMA mesh. The avatar and its materials are never modified.</summary>
    public static class MakeupFaceExamples
    {
        [MenuItem("UMA/Overlay Painter/Examples/Capture Makeup on Current UMA Face")]
        public static void CaptureMenu()
        {
            string folder=EditorUtility.SaveFolderPanel("Makeup examples","Build/Validation/MakeupCoatings","");if(string.IsNullOrEmpty(folder))return;
            try{Capture(folder,2048);}catch(Exception e){UnityEngine.Debug.LogException(e);}
        }
        public static string Capture(string folder,int resolution=2048)
        {
            var avatar=UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(a=>a.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh!=null));
            if(avatar==null)throw new InvalidOperationException("Open an UMA scene with a built character first.");
            var renderer=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.sharedMesh!=null);
            var owned=new List<UnityEngine.Object>();ProceduralMeshMaps maps=null;PreviewRenderUtility preview=null;
            T Own<T>(T o)where T:UnityEngine.Object{owned.Add(o);return o;}
            Directory.CreateDirectory(folder);var report=new List<string>();
            try
            {
                var mesh=Own(new Mesh{name="Makeup validation snapshot"});renderer.BakeMesh(mesh);
                var matrix=avatar.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                mesh.vertices=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();mesh.normals=mesh.normals.Select(v=>matrix.inverse.transpose.MultiplyVector(v).normalized).ToArray();mesh.RecalculateBounds();
                var rig=TexturePaintAnatomy.Capture(avatar.transform,renderer);var frame=MakeupFaceFrame.Resolve(rig);
                var skinIndices=renderer.sharedMaterials.Select((m,i)=>(m,i)).Where(x=>x.m!=null&&x.m.shader.name.Contains("Skin")).Select(x=>x.i).ToArray();
                if(skinIndices.Length==0)throw new InvalidOperationException("No UMA skin material found.");
                var surfaceObject=Own(new GameObject("Makeup validation collider"){hideFlags=HideFlags.HideAndDontSave});
                var collision=Own(UnityEngine.Object.Instantiate(mesh));collision.subMeshCount=1;collision.triangles=skinIndices.SelectMany(mesh.GetTriangles).ToArray();
                var collider=surfaceObject.AddComponent<MeshCollider>();collider.sharedMesh=collision;
                var ray=new Ray(frame.origin+frame.forward*.4f-frame.up*frame.scale*.45f,-frame.forward);
                if(!collider.Raycast(ray,out var hit,1))throw new InvalidOperationException("Unable to locate the skin surface in front of the facial landmarks.");
                int triangle=hit.triangleIndex,submesh=-1;foreach(int index in skinIndices){int count=mesh.GetTriangles(index).Length/3;if(triangle<count){submesh=index;break;}triangle-=count;}
                if(submesh<0)throw new InvalidOperationException("Unable to resolve the face material.");
                var faceMesh=Own(UnityEngine.Object.Instantiate(mesh));faceMesh.subMeshCount=1;faceMesh.triangles=mesh.GetTriangles(submesh);
                var surface=new ReconstructedSurface{index=0,mesh=faceMesh,gameObject=surfaceObject,anatomy=rig,triangleIslands=new int[faceMesh.triangles.Length/3]};
                var timer=Stopwatch.StartNew();maps=ProceduralMeshMapBuilder.Build(surface,resolution,resolution,requestedMaps:TexturePaintMeshMapMask.WorldPosition|TexturePaintMeshMapMask.WorldNormal|TexturePaintMeshMapMask.SurfaceId);report.Add("Mesh maps "+timer.Elapsed.TotalMilliseconds.ToString("F1")+" ms");
                var positions=maps.position.GetPixels();var normals=maps.worldNormal.GetPixels();var ids=maps.id.GetPixels();
                using var host=new PluginHost();host.Discover();
                preview=new PreviewRenderUtility();preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=10;preview.camera.fieldOfView=25;
                preview.camera.clearFlags=CameraClearFlags.Color;preview.camera.backgroundColor=new Color(.15f,.17f,.19f);preview.ambientColor=new Color(.3f,.3f,.3f);
                preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(25,205,0);preview.lights[1].intensity=.9f;preview.lights[1].transform.rotation=Quaternion.Euler(340,145,0);
                for(int look=-1;look<6;look++)
                {
                    var preset=look<0?null:Own(TexturePaintFaceCoatingPresets.Build(look,host));var materials=preset==null?Array.Empty<MakeupMaterial>():preset.layers.Select(l=>new MakeupMaterial(l.pluginParameters)).ToArray();
                    var albedo=new Color[resolution*resolution];var roughness=new Color[albedo.Length];timer.Restart();
                    Parallel.For(0,resolution,y=>
                    {
                        for(int x=0;x<resolution;x++)
                        {
                            int i=y*resolution+x;if(ids[i].a<.5f)continue;
                            var p=positions[i];var n=normals[i];Vector3 world=new(p.r,p.g,p.b),normal=new(n.r*2-1,n.g*2-1,n.b*2-1);
                            var dx=positions[y*resolution+Math.Min(resolution-1,x+1)];var dy=positions[Math.Min(resolution-1,y+1)*resolution+x];
                            float aa=Mathf.Clamp(Mathf.Max(Vector3.Distance(world,new(dx.r,dx.g,dx.b)),Vector3.Distance(world,new(dy.r,dy.g,dy.b)))/frame.scale,.0001f,.03f);
                            Color c=Color.clear;float alpha=0,r=0;
                            foreach(var m in materials)
                            {
                                var s=m.Sample(frame,world,normal,aa);float a=s.coverage,total=a+alpha*(1-a);
                                if(total>0){c=(s.color*a+c*(alpha*(1-a)))/total;r=(s.roughness*a+r*alpha*(1-a))/total;}
                                alpha=total; // Presets leave micro relief off; underlying skin normals are preserved.
                            }
                            albedo[i]=new(c.r,c.g,c.b,alpha);roughness[i]=new(r,r,r,alpha);
                        }
                    });
                    string name=look<0?"Original Skin":TexturePaintFaceCoatingPresets.Names[look];report.Add(name+" "+resolution+"px, "+materials.Length+" cosmetic layers: "+timer.Elapsed.TotalMilliseconds.ToString("F1")+" ms core sampling");
                    Texture2D Texture(Color[] pixels,string suffix)
                    {
                        var t=Own(new Texture2D(resolution,resolution,TextureFormat.RGBAHalf,false,true));t.SetPixels(pixels);t.Apply();
                        var png=Own(new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true));png.SetPixels(suffix=="Albedo"?pixels.Select(p=>{float a=p.a;p=p.gamma;p.a=a;return p;}).ToArray():pixels);png.Apply();File.WriteAllBytes(folder+"/"+name+"-"+suffix+".png",png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);owned.Remove(png);return t;
                    }
                    var colorTexture=Texture(albedo,"Albedo");var roughTexture=Texture(roughness,"Roughness");
                    var drawMaterials=renderer.sharedMaterials.Select(m=>Own(new Material(m))).ToArray();var skin=drawMaterials[submesh];
                    var blend=Own(new Material(Shader.Find("Hidden/UMA/MakeupExampleComposite")));blend.SetTexture("_Makeup",colorTexture);
                    var composite=Own(new RenderTexture(resolution,resolution,0,RenderTextureFormat.ARGBHalf));composite.Create();Graphics.Blit(skin.GetTexture("_BaseMap"),composite,blend);skin.SetTexture("_BaseMap",composite);
                    if(skin.HasProperty("_MaskMap")&&skin.GetTexture("_MaskMap")!=null)
                    {
                        var packed=Own(new RenderTexture(resolution,resolution,0,RenderTextureFormat.ARGBHalf));packed.Create();blend.SetTexture("_Makeup",roughTexture);blend.SetFloat("_Mask",1);Graphics.Blit(skin.GetTexture("_MaskMap"),packed,blend);skin.SetTexture("_MaskMap",packed);
                    }
                    Vector3 focus=frame.origin-frame.up*frame.scale*.4f;preview.camera.transform.position=focus+frame.forward*.62f;preview.camera.transform.LookAt(focus,frame.up);
                    preview.BeginStaticPreview(new Rect(0,0,1024,1024));for(int sm=0;sm<drawMaterials.Length;sm++)preview.DrawMesh(mesh,Matrix4x4.identity,drawMaterials[sm],sm);
                    preview.Render(true);var image=preview.EndStaticPreview();File.WriteAllBytes(folder+"/"+name+"-Face.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                    foreach(var m in drawMaterials){UnityEngine.Object.DestroyImmediate(m);owned.Remove(m);}
                    foreach(var o in new UnityEngine.Object[]{colorTexture,roughTexture,blend,composite,preset}){UnityEngine.Object.DestroyImmediate(o);owned.Remove(o);}
                }
                report.Insert(0,avatar.activeRace.name+"; "+renderer.sharedMesh.name+"; skin material "+submesh+"; eye distance "+frame.scale.ToString("F4")+" m; Unity "+Application.unityVersion);
                report.Add(SystemInfo.processorType+" / "+SystemInfo.graphicsDeviceName);File.WriteAllLines(folder+"/FaceResults.txt",report);return string.Join("\n",report);
            }
            finally{maps?.Dispose();preview?.Cleanup();for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);}
        }
    }
}
