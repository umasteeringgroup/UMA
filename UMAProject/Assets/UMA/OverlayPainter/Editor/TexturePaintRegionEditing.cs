using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private enum RegionTool { Paint, Rectangle, Lasso, Material, UVIsland }
        private RegionTool regionTool;
        private TexturePaintRegionCombine regionCombine;
        private bool regionThrough,regionDragging,regionScene;
        private int regionControl;
        private TextureSet regionGestureSet;
        private Rect regionTextureRect;
        private EditorWindow regionGestureView;
        private int regionGrow,regionFeather;
        private string regionName="Region";
        private readonly List<Vector2> regionPoints=new List<Vector2>();
        private Material regionPreviewMaterial;
        private Texture2D regionPreviewSource, regionProtectedPreview;
        [SerializeField] private bool showSelection = true;
        internal static bool SupportsRegionSelection(TexturePaintLayer layer, bool maskMode)
            => ShowsPaintingProperties(layer,maskMode) ||
                layer != null && layer.links?.instance?.IsSet!=true &&
                (layer.kind==TexturePaintLayerKind.Spline || layer.kind==TexturePaintLayerKind.Fill ||
                 layer.kind==TexturePaintLayerKind.Projection || layer.kind==TexturePaintLayerKind.Plugin);
        private bool RegionRestrictsPainting(TextureSet set)
            => set!=null && ShowsPaintingProperties(
                (uint)set.activeLayerIndex<(uint)set.layers.Count ? set.layers[set.activeLayerIndex] : null,
                IsLayerMaskMode(set));
        private bool CanEditRegion(TextureSet set)
            => set!=null && SupportsRegionSelection(
                (uint)set.activeLayerIndex<(uint)set.layers.Count ? set.layers[set.activeLayerIndex] : null,
                IsLayerMaskMode(set));
        internal bool SelectionToolbarVisible => SceneToolbarReady &&
            (CanEditRegion(ActiveTextureSet) || CountSelectedRegions()>0);
        private bool SuspendRegionToolForNonPaintLayer(TextureSet set)
        {
            if(CanEditRegion(set))return false;
            // A hidden selection tool must not steal clicks from layer editing. Keep the
            // selection itself: it can still be converted to a mask or cleared.
            if(regionTool!=RegionTool.Paint || regionDragging)ExitRegionTool();
            return true;
        }
        private void DrawRegionProperties(TextureSet set)
        {
            if(set==null)return;
            var nextTool=(RegionTool)EditorGUILayout.EnumPopup("Selection Tool",regionTool);
            if(nextTool!=regionTool)SetRegionTool(nextTool);
            if(regionTool!=RegionTool.Paint && GUILayout.Button("Finish Selection (Esc)")) ExitRegionTool();
            _=set.RegionTexture;
            if(!string.IsNullOrEmpty(set.RegionError))EditorGUILayout.HelpBox(set.RegionError,MessageType.Error);
            int selectedSets=CountSelectedRegions();
            EditorGUILayout.HelpBox(!RegionRestrictsPainting(set)
                ? "Selections define regions for mask creation. Convert Selection to Mask to restrict this layer's output. Selecting or clearing a region does not change generated content."
                : set.activeRegion?.IsValid==true
                    ? "Painting is limited by this selection. Cyan marks protected areas; selected areas show their normal appearance."
                    : selectedSets>0 ? "This tile is unrestricted. Other tiles still have selections." : "No selection: painting is unrestricted.",MessageType.None);
            if(selectedSets>0)
            {
                EditorGUI.BeginChangeCheck();
                showSelection=EditorGUILayout.Toggle("Show Selection",showSelection);
                if(EditorGUI.EndChangeCheck())RepaintAll();
                if(GUILayout.Button("Clear All Selections")) ClearAllRegions();
            }
            if(regionTool!=RegionTool.Paint)
            {
                regionCombine=(TexturePaintRegionCombine)EditorGUILayout.EnumPopup("Combine",regionCombine);
                if(regionTool==RegionTool.Rectangle || regionTool==RegionTool.Lasso)
                    regionThrough=EditorGUILayout.Toggle("Select Through (3D)",regionThrough);
                EditorGUILayout.HelpBox("Rectangle and lasso work in UV and Scene views. Material and UV Island select geometry under the cursor. Selection limits painting; it can also become a layer mask.",MessageType.None);
            }
            if(set.activeRegion?.IsValid==true && string.IsNullOrEmpty(set.RegionError))
            {
                regionGrow=EditorGUILayout.IntSlider("Grow / Shrink (px)",regionGrow,-64,64);
                regionFeather=EditorGUILayout.IntSlider("Feather (px)",regionFeather,0,64);
                if(GUILayout.Button("Apply Grow / Feather"))SetRegion(set,set.activeRegion.Adjust(regionGrow,regionFeather,false));
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Invert"))SetRegion(set,set.activeRegion.Adjust(0,0,true));
                if(GUILayout.Button("Clear"))SetRegion(set,null);
                
                GUILayout.EndHorizontal();
                regionName=EditorGUILayout.TextField("Region Name",regionName);
                if(GUILayout.Button("Save Region"))
                {
                    var before=CopyRegions(set.savedRegions);var region=set.activeRegion.Clone();region.id=Guid.NewGuid().ToString("N");region.name=string.IsNullOrWhiteSpace(regionName)?"Region":regionName.Trim();
                    set.savedRegions.Add(region);var after=CopyRegions(set.savedRegions);
                    PushLightweightCommand("Save Region",()=>set.savedRegions=CopyRegions(before),()=>set.savedRegions=CopyRegions(after));SetDocumentDirtyFlags();
                }
                using(new EditorGUI.DisabledScope(!CanConvertRegionToMask(set)))
                    if(GUILayout.Button("Convert Selection to Mask"))RegionToMask(set);
            }
            foreach(var saved in new List<TexturePaintRegion>(set.savedRegions))
            {
                if(saved?.IsValid!=true)continue;
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(saved.name))SetRegion(set,saved.Clone());
                if(GUILayout.Button("Delete",GUILayout.Width(52)))
                {var before=CopyRegions(set.savedRegions);set.savedRegions.Remove(saved);var after=CopyRegions(set.savedRegions);PushLightweightCommand("Delete Region",()=>set.savedRegions=CopyRegions(before),()=>set.savedRegions=CopyRegions(after));SetDocumentDirtyFlags();}
                GUILayout.EndHorizontal();
            }
        }
        private static List<TexturePaintRegion> CopyRegions(List<TexturePaintRegion> regions)
            => regions?.FindAll(item=>item?.IsValid==true).ConvertAll(item=>item.Clone()) ?? new List<TexturePaintRegion>();
        private int CountSelectedRegions()
        {
            int count=0;
            if(controller?.Textures!=null)foreach(var set in controller.Textures.Sets)if(set.activeRegion?.IsValid==true)count++;
            return count;
        }
        private void ClearAllRegions()
        {
            var clear=new Dictionary<TextureSet,TexturePaintRegion>();
            if(controller?.Textures!=null)foreach(var set in controller.Textures.Sets)if(set.activeRegion!=null)clear[set]=null;
            if(clear.Count>0)SetRegions(clear,false);
        }
        private void SetRegion(TextureSet set,TexturePaintRegion next)
        {
            var before=set.activeRegion?.Clone();var after=next?.Clone();set.activeRegion=after?.Clone();
            PushLightweightCommand("Change Selection",()=>{set.activeRegion=before?.Clone();RepaintAll();},()=>{set.activeRegion=after?.Clone();RepaintAll();});SetDocumentDirtyFlags();RepaintAll();
        }
        private void CombineRegion(TextureSet set,TexturePaintRegion next)=>SetRegions(new Dictionary<TextureSet,TexturePaintRegion>{{set,next}},true);
        private void SetRegions(Dictionary<TextureSet,TexturePaintRegion> regions,bool combine)
        {
            var before=new Dictionary<TextureSet,TexturePaintRegion>();var after=new Dictionary<TextureSet,TexturePaintRegion>();
            foreach(var pair in regions)
            {
                var set=pair.Key;var next=pair.Value?.Clone();before[set]=set.activeRegion?.Clone();
                if(combine && next!=null && regionCombine!=TexturePaintRegionCombine.Replace)
                    next=TexturePaintRegion.CombineRegions(set.activeRegion,next,regionCombine);
                after[set]=next;
            }
            void Apply(Dictionary<TextureSet,TexturePaintRegion> state){foreach(var pair in state)pair.Key.activeRegion=pair.Value?.Clone();RepaintAll();}
            Apply(after);
            PushLightweightCommand("Change Selection",()=>Apply(before),()=>Apply(after));SetDocumentDirtyFlags();RepaintAll();
        }
        private void RegionToMask(TextureSet set)
        {
            if(!CanConvertRegionToMask(set))return;
            FinishProjectionEdit();
            if(!TryResolveLogicalPeers(set,set.layers[set.activeLayerIndex],out var peers,out string error))
            {ShowWorkspaceStatus(error);return;}
            var before=new List<LayerLocation>();var after=new List<LayerLocation>();
            try
            {
                foreach(var peer in peers)
                {
                    var tile=peer.textureSet;
                    if(tile.activeRegion==null)continue;
                    var copy=tile.CloneLayer(peer.layer,peer.layer.name,true);
                    int index=tile.layers.IndexOf(peer.layer);
                    before.Add(new LayerLocation{set=tile,layer=peer.layer,index=index});
                    after.Add(new LayerLocation{set=tile,layer=copy,index=index});
                    var mask=tile.InitializeLayerMask(copy,1);
                    mask.target.Reset(tile.RegionTexture,Color.white);
                    mask.effects=new TexturePaintLayerMaskEffects();
                }
            }
            catch
            {
                foreach(var item in after)item.layer.Dispose();
                throw;
            }
            void Apply(bool redo)
            {
                for(int i=0;i<before.Count;i++)
                    SwapLayerSnapshot(before[i].set,redo?before[i].layer:after[i].layer,
                        redo?after[i].layer:before[i].layer,before[i].index);
                RepaintAll();
            }
            Apply(true);
            PushLightweightCommand("Convert Selection to Mask",()=>Apply(false),()=>Apply(true),()=>
            {
                foreach(var item in before)DisposeLayerIfDetached(item.set,item.layer);
                foreach(var item in after)DisposeLayerIfDetached(item.set,item.layer);
            });
            SetDocumentDirtyFlags();RepaintAll();
        }
        private void CancelRegionGesture()
        {
            if(regionControl!=0 && GUIUtility.hotControl==regionControl)GUIUtility.hotControl=0;
            regionControl=0;regionDragging=false;regionGestureSet=null;regionGestureView=null;regionPoints.Clear();
        }
        private void SetRegionTool(RegionTool value)
        {
            if(strokeActive)EndPaint();
            if(uvStrokeActive)EndUVStroke(true);
            paintGestureActive=false;ReleasePaintControl();
            ReleaseSplineHandleCapture(true,false);
            CancelRegionGesture();FinishProjectionEdit();regionTool=value;
            if(value!=RegionTool.Paint){geometryFillMode=0;ReleaseProjectionHandle();}
            RepaintAll();
        }
        private void ExitRegionTool()
        {
            CancelRegionGesture();regionTool=RegionTool.Paint;RepaintAll();
        }
        private bool CancelRegionInput(Event e)
        {
            if(e.type==EventType.KeyDown && e.keyCode==KeyCode.Escape && !EditorGUIUtility.editingTextField)
            {ExitRegionTool();e.Use();return true;}
            if(regionDragging && (e.alt || e.type==EventType.Ignore || e.type==EventType.MouseLeaveWindow ||
                GUIUtility.hotControl!=regionControl))CancelRegionGesture();
            return false;
        }
        private void BeginRegionGesture(int control,bool scene,TextureSet set,Rect textureRect,EditorWindow view,Vector2 point)
        {
            regionDragging=true;regionScene=scene;regionControl=control;regionGestureSet=set;
            regionTextureRect=textureRect;regionGestureView=view;GUIUtility.hotControl=control;
            regionPoints.Clear();regionPoints.Add(point);regionPoints.Add(point);
        }
        private bool HasRegionArea()
        {
            var polygon=RegionPolygon();float area=0;
            for(int i=0;i<polygon.Count;i++)
            {Vector2 a=polygon[i],b=polygon[(i+1)%polygon.Count];area+=a.x*b.y-b.x*a.y;}
            return Mathf.Abs(area)>=8f;
        }
        private bool HandleRegionUV(Rect canvas,Rect textureRect,TextureSet set)
        {
            if(SuspendRegionToolForNonPaintLayer(set))return false;
            Event e=Event.current;
            if(regionTool==RegionTool.Paint)return false;
            int control=GUIUtility.GetControlID("OverlayPainterUVRegion".GetHashCode(),FocusType.Passive);
            if(regionDragging && regionScene)return false;
            if(CancelRegionInput(e))return true;
            if(regionDragging && (!ReferenceEquals(set,regionGestureSet) || textureRect!=regionTextureRect))CancelRegionGesture();
            if(e.alt || e.button!=0)return false;
            if(e.type==EventType.MouseDown && canvas.Contains(e.mousePosition) && GUIUtility.hotControl==0)
            {
                if(regionTool==RegionTool.Material || regionTool==RegionTool.UVIsland)
                {
                    Vector2 uv=CanvasPointToUV(e.mousePosition,textureRect);int triangle=FindRegionTriangle(set,uv);
                    if(triangle>=0)SelectRegionGeometry(set,triangle);e.Use();return true;
                }
                BeginRegionGesture(control,false,set,textureRect,null,e.mousePosition);e.Use();
            }
            if(regionDragging)
            {
                if(e.type==EventType.MouseDrag){UpdateRegionGesture(e.mousePosition);e.Use();RepaintAll();}
                if(e.type==EventType.MouseUp)
                {
                    try
                    {
                        UpdateRegionGesture(e.mousePosition);
                        if(HasRegionArea())
                        {
                            var polygon=RegionPolygon();for(int i=0;i<polygon.Count;i++)polygon[i]=CanvasPointToUV(polygon[i],regionTextureRect);
                            RegionDimensions(set,out int width,out int height);
                            CombineRegion(set,TexturePaintRegionRenderer.Polygon(width,height,polygon));
                        }
                    }
                    catch(Exception error){ShowWorkspaceStatus("Selection failed: "+error.Message);}
                    finally{CancelRegionGesture();e.Use();RepaintAll();}
                }
                if(regionDragging)DrawRegionGesture();return true;
            }
            return canvas.Contains(e.mousePosition) && (e.type==EventType.MouseDown || e.type==EventType.MouseDrag || e.type==EventType.MouseUp);
        }
        private bool HandleRegionScene(SceneView view,Event e)
        {
            if(SuspendRegionToolForNonPaintLayer(ActiveTextureSet))return false;
            if(regionTool==RegionTool.Paint)return false;
            int control=GUIUtility.GetControlID("OverlayPainterRegion".GetHashCode(),FocusType.Passive);
            if(regionDragging && (!regionScene || regionGestureView!=view))return true;
            if(CancelRegionInput(e))return true;
            if(e.alt || e.button!=0)return false;
            if(e.type==EventType.Layout)HandleUtility.AddDefaultControl(control);
            if(e.type==EventType.MouseDown && GUIUtility.hotControl==0 && HandleUtility.nearestControl==control)
            {
                if(regionTool==RegionTool.Material || regionTool==RegionTool.UVIsland)
                {if(hasHover)SelectRegionGeometry(controller.Textures.FindSet(hoverSurface.index),hoverHit.triangleIndex);e.Use();return true;}
                BeginRegionGesture(control,true,null,default,view,e.mousePosition);e.Use();
            }
            if(regionDragging)
            {
                if(e.type==EventType.MouseDrag){UpdateRegionGesture(e.mousePosition);e.Use();view.Repaint();}
                if(e.type==EventType.MouseUp)
                {
                    try
                    {
                        UpdateRegionGesture(e.mousePosition);
                        if(HasRegionArea())
                        {
                            var polygon=RegionPolygon();
                            for(int i=0;i<polygon.Count;i++)
                            {Ray ray=HandleUtility.GUIPointToWorldRay(polygon[i]);Vector3 p=view.camera.WorldToViewportPoint(ray.GetPoint(Mathf.Max(view.camera.nearClipPlane*2,1)));polygon[i]=new Vector2(p.x,p.y);}
                            SetRegions(TexturePaintRegionRenderer.Project(controller.Textures.Sets,view.camera,polygon,regionThrough),true);
                        }
                    }
                    catch(Exception error){ShowWorkspaceStatus("Selection failed: "+error.Message);}
                    finally{CancelRegionGesture();e.Use();view.Repaint();}
                }
                if(regionDragging){Handles.BeginGUI();DrawRegionGesture();Handles.EndGUI();}
            }
            return true;
        }
        private void UpdateRegionGesture(Vector2 point)
        {if(regionTool==RegionTool.Rectangle)regionPoints[1]=point;else if(Vector2.Distance(regionPoints[regionPoints.Count-1],point)>3)regionPoints.Add(point);}
        private List<Vector2> RegionPolygon()
        {
            if(regionTool!=RegionTool.Rectangle)return new List<Vector2>(regionPoints);
            Vector2 a=regionPoints[0],b=regionPoints[1];return new List<Vector2>{a,new Vector2(b.x,a.y),b,new Vector2(a.x,b.y)};
        }
        private void DrawRegionGesture()
        {
            if(Event.current.type!=EventType.Repaint || regionPoints.Count<2)return;
            var polygon=RegionPolygon();var points=new Vector3[polygon.Count+1];for(int i=0;i<polygon.Count;i++)points[i]=polygon[i];points[polygon.Count]=points[0];
            Color previous=Handles.color;Handles.color=Color.cyan;Handles.DrawAAPolyLine(2,points);Handles.color=previous;
        }
        private static void RegionDimensions(TextureSet set,out int width,out int height)
        {width=height=1024;foreach(var channel in set.channels.Values)if(channel.Texture!=null){width=channel.Texture.width;height=channel.Texture.height;break;}}
        private static int FindRegionTriangle(TextureSet set,Vector2 uv)
        {
            var mesh=set.surface?.mesh;if(mesh==null)return -1;var triangles=mesh.triangles;var uvs=mesh.uv;if(uvs.Length!=mesh.vertexCount)return -1;
            for(int i=0;i<triangles.Length;i+=3)if(TexturePaintRegion.Contains(new[]{uvs[triangles[i]],uvs[triangles[i+1]],uvs[triangles[i+2]]},uv))return i/3;return -1;
        }
        private void SelectRegionGeometry(TextureSet set,int triangle)
        {
            if(set==null)return;RegionDimensions(set,out int width,out int height);
            int island=regionTool==RegionTool.UVIsland&&set.surface.triangleIslands!=null && (uint)triangle<(uint)set.surface.triangleIslands.Length?set.surface.triangleIslands[triangle]:-1;
            // A reconstructed surface is a material slot / UDIM member.
            Texture2D mask=TexturePaintGeometryMask.Build(set.surface,width,height,null,island,null);
            try{var colors=mask.GetPixels32();var values=new byte[colors.Length];for(int i=0;i<values.Length;i++)values[i]=colors[i].r;CombineRegion(set,TexturePaintRegion.Encode(width,height,values));}
            finally{UnityEngine.Object.DestroyImmediate(mask);}
        }
        private void DrawRegionPreview(Rect rect,TextureSet set)
        {
            if(Event.current.type!=EventType.Repaint || !showSelection || set?.activeRegion==null)return;
            var preview=GetProtectedRegionPreview(set);
            if(preview==null)return;
            Color old=GUI.color;
            try{GUI.color=new Color(0,.9f,1,.24f);GUI.DrawTexture(rect,preview,ScaleMode.StretchToFill,true);}
            finally{GUI.color=old;}
        }

        private Texture2D GetProtectedRegionPreview(TextureSet set)
        {
            var source=set?.RegionTexture;
            if(source!=null && source==regionPreviewSource && regionProtectedPreview!=null)return regionProtectedPreview;
            if(regionProtectedPreview!=null)DestroyImmediate(regionProtectedPreview);
            regionProtectedPreview=null;regionPreviewSource=source;
            if(source==null)return null;
            // Cache the inverse only for display. Selection data and paint coverage remain unchanged.
            var pixels=source.GetPixels32();
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,(byte)(255-pixels[i].r));
            regionProtectedPreview=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true)
                {name="Protected selection preview",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};
            regionProtectedPreview.SetPixels32(pixels);regionProtectedPreview.Apply(false,false);
            return regionProtectedPreview;
        }

        private void DisposeRegionPreviews()
        {
            if(regionPreviewMaterial!=null)DestroyImmediate(regionPreviewMaterial);
            if(regionProtectedPreview!=null)DestroyImmediate(regionProtectedPreview);
            regionPreviewMaterial=null;regionProtectedPreview=null;regionPreviewSource=null;
        }

        private void DrawRegionScenePreview()
        {
            if(Event.current.type!=EventType.Repaint || !showSelection || controller?.Textures==null)return;
            foreach(var set in controller.Textures.Sets)
            {
                if(set.activeRegion==null || set.surface?.gameObject==null)continue;
                var owner=set.surface.gameObject;
                var renderer=owner.GetComponent<MeshRenderer>();
                if(!owner.activeInHierarchy || renderer==null || !renderer.enabled)continue;
                // Use the displayed mesh so hidden reference slots stay hidden.
                var mesh=owner.GetComponent<MeshFilter>()?.sharedMesh;
                var mask=set.RegionTexture;
                if(mesh==null || mask==null)continue;
                if(regionPreviewMaterial==null)
                {
                    var shader=Shader.Find("Hidden/UMA/TexturePaint/RegionSelection");
                    if(shader==null || !shader.isSupported)return;
                    regionPreviewMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                }
                regionPreviewMaterial.SetTexture("_SelectionShape",mask);
                if(regionPreviewMaterial.SetPass(2))
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                        Graphics.DrawMeshNow(mesh,owner.transform.localToWorldMatrix,sub);
            }
        }
    }
}
