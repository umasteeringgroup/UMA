using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Examples.Editor
{
    /// <summary>Editable UV paths for the shell used by HDEyeBrows_Thin_Wardrobe.</summary>
    public static class EyebrowShellExamples
    {
        public const string Folder = "Assets/UMA/OverlayPainterExamples/Eyebrows/Projects";
        const string SlotPath = "Assets/UMA/UMA3/Races/Slots/UMA30_Eyebrows/UMA30_Eyebrows_slot.asset";
        const string OverlayPath = "Assets/UMA/UMA3/Wearables/eyebrows/HDEyeBrows_Thin_Overlay.asset";
        static Type Storage => typeof(UMA.TexturePaint.Editor.TexturePaintStageWindow).Assembly.GetType("UMA.TexturePaint.Editor.TexturePaintDocumentStorage", true);

        [MenuItem("UMA/Overlay Painter/Examples/Create Eyebrow Shell Projects")]
        public static void CreateProjects()
        {
            foreach (var preset in new[] { BrowPreset.Groomed, BrowPreset.Bushy }) Create(preset, 2048);
            AssetDatabase.Refresh();
        }

        public static string Create(BrowPreset preset, int resolution)
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(SlotPath);
            var overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(OverlayPath);
            if (slot == null || overlay == null) throw new InvalidOperationException("Install the UMA3 HDEyeBrows_Thin_Wardrobe content first.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            string assetPath = Folder + "/HDEyebrows " + preset + ".asset";
            var launch = new TexturePaintLaunchContext { kind = TexturePaintLaunchKind.StandaloneSlot,
                sourceMode = TexturePaintStandaloneSourceMode.OverlayDataAsset, selectedSlot = slot,
                umaMaterial = overlay.material, resolution = resolution, selectedSlotGuid = AssetDatabase.AssetPathToGUID(SlotPath),
                umaMaterialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(overlay.material)) };
            launch.members.Add(new TexturePaintStandaloneMemberContext { slot = slot, overlay = overlay,
                slotGuid = launch.selectedSlotGuid, overlayGuid = AssetDatabase.AssetPathToGUID(OverlayPath) });
            using var controller = new TexturePaintStageController();
            controller.InitializeStandalone(launch, null, null, null, resolution: resolution,
                ribbonShader: Shader.Find("Hidden/UMA/TexturePaint/RibbonProjection"));
            var set = controller.Textures.Sets.Single();
            foreach (var channel in set.channels.Values) channel.editable.Reset(null, Color.clear);
            while (set.layers.Count > 0) set.RemoveLayerAt(0);
            for (int side = 0; side < 2; side++) AddBrow(controller, set, preset, side, resolution);
            AssertVisible(set,"before save");
            var document = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(assetPath);
            if (document == null)
            {
                document = (TexturePaintDocument)Storage.GetMethod("CreateTransient", BindingFlags.Static | BindingFlags.Public)
                    .Invoke(null, new object[] { null, launch });
                document.hideFlags = HideFlags.None; AssetDatabase.CreateAsset(document, assetPath);
            }
            document.launchContext = launch;
            controller.AttachDocument(document);
            Storage.GetMethod("Save", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { document, controller.Textures, false });
            var report = Storage.GetMethod("Restore", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { document, controller.Textures });
            if (set.layers.Count != 2 || set.layers.Any(l => l.spline?.PointCount != 4 || l.splineSettings?.pathGenerator?.generatorId != EyebrowPathGenerator.GeneratorId))
                throw new InvalidOperationException("Eyebrow project did not survive save/restore.");
            AssertVisible(set,"after restore");
            SaveCombined(set, Folder + "/HDEyebrows " + preset + " Albedo.png");
            CaptureShell(document, preset, Folder);
            return assetPath + " — two editable paths; " + report;
        }

        static void AddBrow(TexturePaintStageController controller, TextureSet set, BrowPreset preset, int side, int resolution)
        {
            const float width = .105f;
            var layer = set.AddSplineLayer((side == 0 ? "Right" : "Left") + " eyebrow — " + preset);
            layer.spline.worldSpace = false;
            // Original shell UVs: head near the centre of the texture; tail towards its sides.
            var points = new[] { new Vector2(.455f,.395f), new Vector2(.36f,.455f), new Vector2(.25f,.445f), new Vector2(.145f,.35f) };
            foreach (var original in points)
            {
                var uv = side == 0 ? original : new Vector2(1.01f-original.x,original.y);
                layer.spline.AddPoint(new Vector3(uv.x,uv.y,0), uv, set.surface.index, -1, Vector3.forward);
            }
            var settings = EyebrowSettings.Preset(preset);
            settings.flipAcross = side == 0; settings.seed += side * 113; settings.archHeight.value = 0;
            settings.layerDepthContrast.value = .24f;
            layer.splineSettings = new TexturePaintSplineSettings { brushSize = width*.5f, brushHardness = 1,
                projectionDepth = 2, color = Color.white, pathGenerator = new TexturePaintPathGeneratorSettings {
                    enabled = true, generatorId = EyebrowPathGenerator.GeneratorId, extensionJson = settings.Save() } };
            var samples = layer.spline.Sample(width*.05f, set.surface.index);
            for (int i=0;i<samples.Count;i++) { var sample=samples[i]; sample.worldPosition=new Vector3(sample.uv.x,sample.uv.y,0); sample.worldNormal=Vector3.forward; samples[i]=sample; }
            var brush = ScriptableObject.CreateInstance<BrushPreset>(); brush.size=width*.5f; brush.hardness=1; brush.flow=1;
            try
            {
                var context = new StrokeContext { textures=set, brush=brush, tool=TexturePaintTool.Paint,
                    channel=TexturePaintChannel.Albedo, color=Color.white, paintSource=TexturePaintBrushSource.Color,
                    strength=1, directUV=true, projectionDepth=2, pathGenerator=layer.splineSettings.pathGenerator,
                    pathGeneratorContext=TexturePaintPathGeneratorContext.FromSamples(samples,width,resolution,false,true) };
                foreach(var channel in new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl})
                    if(set.GetChannel(channel)!=null)context.channelSources[channel]=new TexturePaintChannelSourceSettings { source=TexturePaintBrushSource.Color,color=Color.white };
                var segments=(List<TexturePaintRibbonSegment>)typeof(UMA.TexturePaint.Editor.TexturePaintStageWindow)
                    .GetMethod("BuildRibbonSegments",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{samples,width*.5f,width,false,false});
                if(!controller.Painting.BeginStroke(context,TexturePaintSourceMode.SourceOverlay))throw new InvalidOperationException(controller.Painting.LastStrokeError);
                bool applied=controller.Painting.ApplyRibbon(segments,samples,false,false,false,true);
                controller.Painting.EndStroke(applied);
                if(!applied)throw new InvalidOperationException("Shell eyebrow rendering failed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(brush); }
        }

        static Texture2D Combined(TextureSet set)
        {
            var target=set.layers[0].channels[TexturePaintChannel.Albedo];
            int width=target.Width,height=target.Height;
            var result=new Color[width*height]; var old=RenderTexture.active;
            var read=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            try
            {
                foreach(var layer in set.layers)
                {
                    RenderTexture.active=layer.channels[TexturePaintChannel.Albedo].Front;
                    read.ReadPixels(new Rect(0,0,width,height),0,0);read.Apply();var pixels=read.GetPixels();
                    for(int i=0;i<result.Length;i++)
                    {
                        float a=pixels[i].a+result[i].a*(1-pixels[i].a);
                        var color=a>0?(pixels[i]*pixels[i].a+result[i]*(result[i].a*(1-pixels[i].a)))/a:Color.clear;
                        color.a=a;result[i]=color;
                    }
                }
            }
            finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(read);}
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,false);
            for(int i=0;i<result.Length;i++){float a=result[i].a;result[i]=result[i].gamma;result[i].a=a;}
            texture.SetPixels(result);texture.Apply();return texture;
        }
        static void SaveCombined(TextureSet set,string path)
        {
            var texture=Combined(set);try{File.WriteAllBytes(path,texture.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        static void AssertVisible(TextureSet set,string stage)
        {
            var texture=Combined(set);
            try {if(texture.GetPixels32().All(p=>p.a==0))throw new InvalidOperationException("Eyebrow alpha is empty "+stage);}
            finally{UnityEngine.Object.DestroyImmediate(texture);}
        }
        static void CaptureShell(TexturePaintDocument document,BrowPreset preset,string folder)
        {
            var avatar=UnityEngine.Object.FindObjectsByType<DynamicCharacterAvatar>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .FirstOrDefault(a=>a.umaData?.umaRecipe?.slotDataList?.Any(s=>s?.asset?.slotName=="UMA30_Eyebrows_slot")==true);
            if(avatar==null)return; // Projects remain useful without a scene avatar; no scene is changed.
            using var reconstruction=MeshReconstructor.Reconstruct(avatar);
            var shell=reconstruction.surfaces.Single(s=>s.slotNames.Contains("UMA30_Eyebrows_slot"));
            // Exercise document rebinding against the equipped, DNA-deformed shell, not just
            // the standalone mesh that authored it. Reconstruction owns the borrowed surface.
            var shellOnly=new MeshReconstructionResult();shellOnly.surfaces.Add(shell);
            using var store=new TextureStore();store.Initialize(shellOnly,2048);
            Storage.GetMethod("Restore",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{document,store});
            var set=store.Sets.Single();
            if(set.layers.Count!=2)throw new InvalidOperationException("Shell project did not rebind both paths to the equipped character.");
            AssertVisible(set,"after rebinding to equipped shell");
            var texture=Combined(set);var material=new Material(shell.sourceMaterial);var preview=new PreviewRenderUtility();
            try
            {
                foreach(string property in new[]{"_BaseMap","_MainTex"})if(material.HasProperty(property))material.SetTexture(property,texture);
                foreach(string property in new[]{"_BaseColor","_Color"})if(material.HasProperty(property))material.SetColor(property,Color.white);
                if(material.HasProperty("_Cutoff"))material.SetFloat("_Cutoff",.02f);
                preview.cameraFieldOfView=25;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=10;
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.16f,.18f,.21f,1);
                preview.lights[0].intensity=1.7f;preview.lights[0].transform.rotation=Quaternion.Euler(25,205,0);
                preview.lights[1].intensity=1.1f;preview.lights[1].transform.rotation=Quaternion.Euler(340,145,0);
                preview.ambientColor=new Color(.3f,.3f,.3f,1);
                var focus=shell.mesh.bounds.center;
                preview.camera.transform.position=focus+Vector3.forward*.30f;preview.camera.transform.LookAt(focus);
                preview.BeginStaticPreview(new Rect(0,0,1024,1024));
                foreach(var surface in reconstruction.surfaces)
                    if(surface==shell)preview.DrawMesh(surface.mesh,Matrix4x4.identity,material,0);
                    else if(surface.sourceMaterial!=null&&!surface.sourceMaterial.shader.name.Contains("Hair"))
                        preview.DrawMesh(surface.mesh,Matrix4x4.identity,surface.sourceMaterial,0);
                preview.Render(true);var image=preview.EndStaticPreview();
                try{File.WriteAllBytes(Path.Combine(folder,"HDEyebrows "+preset+" on UMA.png"),image.EncodeToPNG());}
                finally{UnityEngine.Object.DestroyImmediate(image);}
            }
            finally{preview.Cleanup();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
