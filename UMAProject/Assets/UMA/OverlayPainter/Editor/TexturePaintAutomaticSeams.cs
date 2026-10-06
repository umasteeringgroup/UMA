using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    public sealed class TexturePaintAutomaticSeams : EditorWindow
    {
        private Mesh mesh;
        private Action<List<Vector2>,bool,string,float> create;
        private List<TexturePaintBoundaryPaths.Contour> contours=new();
        private readonly List<int> selected=new();
        private DropdownField source,generator;
        private ObjectField mask;
        private FloatField threshold,inset,tolerance,width;
        private ScrollView choices;
        private Label status;
        private readonly List<string> generatorIds=new();

        internal static void Open(Mesh mesh,Action<List<Vector2>,bool,string,float> create)
        {
            var window=CreateInstance<TexturePaintAutomaticSeams>();window.mesh=mesh;window.create=create;
            window.titleContent=new GUIContent("Automatic Seam Paths");window.minSize=new Vector2(450,490);window.ShowUtility();
        }
        public void CreateGUI()
        {
            var root=rootVisualElement;root.style.paddingLeft=10;root.style.paddingRight=10;root.style.paddingTop=8;
            root.Add(new HelpBox("Choose the boundaries that represent real seams. UV borders can also be texture cuts. Each selected contour becomes an editable path layer.",HelpBoxMessageType.Info));
            source=new DropdownField("Boundary Source",new List<string>{"UV island borders","Mask contours"},0);root.Add(source);
            mask=new ObjectField("Coverage Mask"){objectType=typeof(Texture2D),allowSceneObjects=false};root.Add(mask);
            threshold=new FloatField("Mask Threshold"){value=.5f};root.Add(threshold);
            inset=new FloatField("Inset (UV units)"){value=.008f};root.Add(inset);
            tolerance=new FloatField("Simplify (UV units)"){value=.001f};root.Add(tolerance);
            width=new FloatField("Path Width (UV units)"){value=.008f};root.Add(width);
            var names=new List<string>();
            foreach(var item in TexturePaintPathGenerators.All)
                if(item.Id.StartsWith("com.uma.path.stitch.",StringComparison.Ordinal)||item.Id.StartsWith("com.uma.path.seam.",StringComparison.Ordinal))
                {generatorIds.Add(item.Id);names.Add(item.MenuPath);}
            generator=new DropdownField("Construction",names,0);root.Add(generator);
            root.Add(new Button(Scan){text="Find Boundaries"});
            choices=new ScrollView();choices.style.flexGrow=1;root.Add(choices);
            status=new Label();status.style.whiteSpace=WhiteSpace.Normal;root.Add(status);
            root.Add(new Button(CreateSelected){text="Create Selected Seam Paths"});
            source.RegisterValueChangedCallback(_=>Scan());mask.RegisterValueChangedCallback(_=>Scan());threshold.RegisterValueChangedCallback(_=>Scan());
            Scan();
        }
        private void Scan()
        {
            choices.Clear();selected.Clear();contours.Clear();
            try
            {
                if(source.index==0)
                {if(mesh==null)throw new InvalidOperationException("The source mesh is no longer available.");contours=TexturePaintBoundaryPaths.FromMesh(mesh.uv,mesh.triangles);}
                else
                {
                    if(mask.value is not Texture2D texture)throw new InvalidOperationException("Choose a mask texture.");
                    int w=Mathf.Min(512,texture.width),h=Mathf.Min(512,texture.height);
                    var rt=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                    var old=RenderTexture.active;var readable=new Texture2D(w,h,TextureFormat.RGBA32,false,true);
                    try {Graphics.Blit(texture,rt);RenderTexture.active=rt;readable.ReadPixels(new Rect(0,0,w,h),0,0);readable.Apply();contours=TexturePaintBoundaryPaths.FromMask(readable.GetPixels(),w,h,Mathf.Clamp(threshold.value,.001f,1));}
                    finally {RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);DestroyImmediate(readable);}
                }
                if(contours.Count>512)throw new InvalidOperationException("More than 512 contours were found. Use a cleaner panel mask or adjust its threshold before creating seams.");
                for(int i=0;i<contours.Count;i++)
                {
                    int index=i;var path=contours[i];var bounds=new Bounds(path.points[0],Vector3.zero);foreach(var point in path.points)bounds.Encapsulate(point);
                    var toggle=new Toggle($"{i+1}: {(path.closed?"Closed":"Open")} — UV center {bounds.center.x:F3}, {bounds.center.y:F3} ({path.points.Count} points)");
                    toggle.RegisterValueChangedCallback(evt=>{if(evt.newValue)selected.Add(index);else selected.Remove(index);});choices.Add(toggle);
                }
                status.text=$"Found {contours.Count} boundaries. Select only the garment boundaries you want to sew.";
            }
            catch(Exception exception){status.text=exception.Message;}
        }
        private void CreateSelected()
        {
            if(create==null){status.text="Reopen this dialog from the active painter after a script reload.";return;}
            if(selected.Count==0||generator.index<0){status.text="Select at least one boundary and a construction.";return;}
            try
            {
                foreach(int index in selected)
                {
                    var points=TexturePaintBoundaryPaths.Prepare(contours[index],Mathf.Clamp(inset.value,-.1f,.1f),Mathf.Clamp(tolerance.value,0,.02f));
                    if(points.Count>2048)throw new InvalidOperationException("This contour has too many points. Increase Simplify or use a cleaner mask.");
                    create(points,contours[index].closed,generatorIds[generator.index],Mathf.Clamp(width.value,.0001f,.2f));
                }
                Close();
            }
            catch(Exception exception){status.text=exception.Message;}
        }
    }

    public sealed partial class TexturePaintStageWindow
    {
        private void OpenAutomaticSeams(TextureSet set)
        {
            if(set?.surface?.mesh==null)return;
            TexturePaintAutomaticSeams.Open(set.surface.mesh,(points,closed,id,width)=>
            {
                if(controller?.Textures?.Sets.Contains(set)!=true)throw new InvalidOperationException("The target is no longer open.");
                selectedSurface=controller.Textures.Sets.ToList().IndexOf(set);
                BeginLayerCreationUndo("Create Automatic Seam Path");
                var layer=CreateSplineLayer(set);
                var definition=TexturePaintPathGenerators.Find(id);pathGenerator=definition.CreateSettings();
                layer.name="Auto Seam — "+definition.MenuPath.Substring(definition.MenuPath.LastIndexOf('/')+1);
                spline.worldSpace=false;spline.closed=closed;spline.useBezier=false;
                foreach(var uv in points)spline.AddPoint(new Vector3(uv.x,uv.y,0),uv,selectedSurface,-1,Vector3.forward);
                ActiveBrush.size=width*.5f;ActiveBrush.hardness=1;ActiveBrush.flow=1;ActiveBrush.rotation=0;
                pathStartFade=pathEndFade=pathSideFadeExtra=0;strength=1;
                EnsurePathGeneratorChannels(set,layer,pathGenerator);CaptureSplineSettings(layer);
                CompleteLayerCreationUndo(layer);ApplySpline();MarkDocumentDirty();RepaintAll();
            });
        }
    }
}
