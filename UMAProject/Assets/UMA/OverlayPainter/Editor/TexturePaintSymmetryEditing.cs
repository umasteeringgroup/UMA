using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        // Kept only to read older saved editor sessions; each layer owns the effective frame.
        [SerializeField] private TexturePaintSymmetry authoringSymmetry=new TexturePaintSymmetry();
        [SerializeField] private TexturePaintSymmetry pathLocalSymmetry;
        private bool symmetryHandles;
        private int symmetryHandleControl;

        private bool TryGetSymmetryLayer(TextureSet set,out TexturePaintLayer layer)
        {
            layer=null;
            if(set==null || (uint)set.activeLayerIndex>=(uint)set.layers.Count)return false;
            layer=set.layers[set.activeLayerIndex];return layer!=null;
        }

        private bool CanEditLayerSymmetry(TextureSet set)
            =>TryGetSymmetryLayer(set,out var layer) && layer.links?.instance?.IsSet!=true &&
                (IsLayerMaskMode(set) || layer.IsSplineLayer || layer.kind==TexturePaintLayerKind.Paint ||
                 layer.kind==TexturePaintLayerKind.Projection);

        private bool TryGetSymmetryPath(TextureSet set,out TexturePaintLayer layer)
        {
            if(IsLayerMaskMode(set)){layer=null;return false;}
            return TryGetActivePathLayer(set,out layer);
        }

        private TexturePaintSymmetry ResolveLayerSymmetry(TexturePaintLayer layer)
        {
            if(layer==null)return new TexturePaintSymmetry();
            if(layer.layerSymmetryVersion<1)
            {
                TexturePaintSymmetry previous=layer.IsSplineLayer ? layer.splineSettings?.symmetry
                    : layer.kind==TexturePaintLayerKind.Paint ? layer.paintSettings?.symmetry : null;
                if(previous?.enabled!=true && layer.IsSplineLayer && layer.splineSettings?.localSymmetry?.enabled==true)
                    previous=layer.splineSettings.localSymmetry;
                if(previous?.enabled==true)layer.layerSymmetry=previous.Clone();
                else
                {
                    bool legacyX=layer.IsSplineLayer
                        ? layer.splineSettings?.mirrorX==true || layer.splineSettings?.brushMirrorStroke==true
                        : layer.kind==TexturePaintLayerKind.Paint && (layer.paintSettings?.mirrorX==true || layer.paintSettings?.brushMirrorStroke==true);
                    int copies=layer.IsSplineLayer ? Mathf.Clamp(layer.splineSettings?.radialSymmetry ?? 1,1,16) : 1;
                    Vector3 axis=layer.IsSplineLayer ? layer.splineSettings?.symmetryAxis ?? Vector3.up : Vector3.up;
                    Vector3 pivot=layer.spline?.worldSpace==true && copies>1 && controller?.Reconstruction?.root!=null
                        ? controller.Reconstruction.root.transform.position : Vector3.zero;
                    layer.layerSymmetry=new TexturePaintSymmetry
                    {
                        enabled=legacyX || copies>1,mirrorX=legacyX,mirrorY=false,mirrorZ=false,
                        radialCopies=copies,radialAxis=axis,origin=pivot,legacyWorldMirrorOrder=copies>1
                    };
                }
                layer.layerSymmetryVersion=1;
            }
            return layer.layerSymmetry ??= new TexturePaintSymmetry();
        }

        private TexturePaintSymmetry CurrentLayerSymmetry
            =>TryGetSymmetryLayer(ActiveTextureSet,out var layer) ? ResolveLayerSymmetry(layer) : new TexturePaintSymmetry();

        private void DrawSymmetryProperties()=>DrawSymmetryPropertiesForSet(ActiveTextureSet);

        private void DrawSymmetryPropertiesForSet(TextureSet set)
        {
            if(!TryGetSymmetryLayer(set,out var layer))return;
            if(!CanEditLayerSymmetry(set))
            {
                EditorGUILayout.HelpBox("Symmetry is available for Paint, Path, and Projection layers, and while painting a layer mask.",MessageType.None);
                return;
            }
            var frame=ResolveLayerSymmetry(layer).Clone();
            bool path=layer.IsSplineLayer && !IsLayerMaskMode(set),uv=path && !layer.spline.worldSpace;
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("LayerSymmetryEnabled");
            frame.enabled=EditorGUILayout.Toggle("Symmetry Enabled",frame.enabled);
            using(new EditorGUI.DisabledScope(!frame.enabled))
            {
                GUI.SetNextControlName("SymmetryMirrorX");
                frame.mirrorX=EditorGUILayout.Toggle("Mirror X",frame.mirrorX);
                frame.mirrorY=EditorGUILayout.Toggle("Mirror Y",frame.mirrorY);
                using(new EditorGUI.DisabledScope(uv))
                    frame.mirrorZ=EditorGUILayout.Toggle("Mirror Z (3D)",frame.mirrorZ);
                if(uv)
                {
                    Vector2 offset=EditorGUILayout.Vector2Field("UV Center Offset",new Vector2(frame.origin.x,frame.origin.y));
                    frame.origin=new Vector3(offset.x,offset.y,frame.origin.z);
                    frame.euler=new Vector3(frame.euler.x,frame.euler.y,EditorGUILayout.FloatField("UV Rotation",frame.euler.z));
                }
                else
                {
                    frame.origin=EditorGUILayout.Vector3Field("Frame Origin",frame.origin);
                    frame.euler=EditorGUILayout.Vector3Field("Plane Rotation",frame.euler);
                }
                frame.radialCopies=EditorGUILayout.IntSlider("Radial Copies",frame.radialCopies,1,16);
                using(new EditorGUI.DisabledScope(uv))
                {
                    frame.radialAxis=EditorGUILayout.Vector3Field("Radial Axis (3D)",frame.radialAxis);
                    symmetryHandles=EditorGUILayout.Toggle("Edit Origin in Scene",symmetryHandles);
                }
            }
            EditorGUILayout.HelpBox(layer.kind==TexturePaintLayerKind.Projection && !IsLayerMaskMode(set)
                ? "These settings belong to this projection. Use Create Symmetry Instances to add linked copies."
                : "Symmetry belongs to this layer. The scene X button toggles its X mirror and enables symmetry when turned on. UV symmetry uses the texture center plus the XY offset and Z rotation.",MessageType.None);
            if(EditorGUI.EndChangeCheck())ApplyLayerSymmetryEdit(set,frame);
        }

        private void ApplyLayerSymmetryEdit(TextureSet set,TexturePaintSymmetry next)
        {
            if(!CanEditLayerSymmetry(set) || !TryGetSymmetryLayer(set,out var layer))return;
            var before=ResolveLayerSymmetry(layer).Clone();var after=next?.Clone() ?? new TexturePaintSymmetry();
            // Keep old radial/world-X placement through enable/X toggles. Editing the frame
            // itself adopts the single displayed origin for every axis.
            if(before.origin!=after.origin || before.euler!=after.euler || before.radialAxis!=after.radialAxis ||
                before.mirrorY!=after.mirrorY || before.mirrorZ!=after.mirrorZ)
                after.legacyWorldMirrorOrder=false;
            if(JsonUtility.ToJson(before)==JsonUtility.ToJson(after))return;
            if(layer.IsSplineLayer && !IsLayerMaskMode(set))
            {
                BeginLightweightPathUndo(set,"Edit Layer Symmetry");
                StoreLayerSymmetry(set,layer,after);
                CompleteLightweightPathEdit(set,false);
            }
            else
            {
                StoreLayerSymmetry(set,layer,after);
                PushLightweightCommand("Edit Layer Symmetry",
                    ()=>StoreLayerSymmetry(set,layer,before),()=>StoreLayerSymmetry(set,layer,after));
            }
            MarkDocumentDirty();RepaintAll();
        }

        private void StoreLayerSymmetry(TextureSet set,TexturePaintLayer layer,TexturePaintSymmetry frame)
        {
            if(!TryResolveLogicalPeers(set,layer,out List<TexturePaintLogicalLayerMember> peers,out _))
                peers=new List<TexturePaintLogicalLayerMember>{new TexturePaintLogicalLayerMember{textureSet=set,layer=layer}};
            foreach(var peer in peers)
            {
                peer.layer.layerSymmetry=frame.Clone();peer.layer.layerSymmetryVersion=1;
            }
            if(TryGetSymmetryLayer(ActiveTextureSet,out var active) && ReferenceEquals(active,layer))
                authoringSymmetry=frame.Clone();
            MarkDocumentDirty();RepaintAll();
        }

        private bool GetSymmetryMirrorX(TextureSet set)
            =>TryGetSymmetryLayer(set,out var layer) && ResolveLayerSymmetry(layer).enabled && ResolveLayerSymmetry(layer).mirrorX;

        private void SetSymmetryMirrorX(TextureSet set,bool value)
        {
            if(!CanEditLayerSymmetry(set) || !TryGetSymmetryLayer(set,out var layer) || value==GetSymmetryMirrorX(set))return;
            var frame=ResolveLayerSymmetry(layer).Clone();frame.mirrorX=value;
            if(value)frame.enabled=true;
            ApplyLayerSymmetryEdit(set,frame);
        }

        private int PathRadialCopies
        {
            get=>CurrentLayerSymmetry.radialCopies;
            set
            {
                if(!TryGetSymmetryLayer(ActiveTextureSet,out var layer))return;
                var frame=ResolveLayerSymmetry(layer).Clone();frame.radialCopies=value;
                if(value>1)frame.enabled=true;
                StoreLayerSymmetry(ActiveTextureSet,layer,frame);
            }
        }

        private List<Matrix4x4> GetPathSymmetryTransforms(bool uvSpace,BrushPreset pathBrush,Vector3 legacyPivot)
            =>CurrentLayerSymmetry.Transforms(uvSpace);

        private void DrawSymmetryHandles()
        {
            if(!symmetryHandles || !CanEditLayerSymmetry(ActiveTextureSet) ||
                !TryGetSymmetryLayer(ActiveTextureSet,out var layer) || layer.IsSplineLayer && !layer.spline.worldSpace)
            { ReleaseSymmetryHandleCapture(); return; }
            var frame=ResolveLayerSymmetry(layer).Clone();if(!frame.enabled){ReleaseSymmetryHandleCapture();return;}
            int previousControl = GUIUtility.hotControl;
            EditorGUI.BeginChangeCheck();
            var rotation=Quaternion.Euler(frame.euler);
            frame.origin=Handles.PositionHandle(frame.origin,rotation);
            frame.euler=Handles.RotationHandle(rotation,frame.origin).eulerAngles;
            if (previousControl == 0 && GUIUtility.hotControl != 0 && Event.current.rawType == EventType.MouseDown)
                symmetryHandleControl = GUIUtility.hotControl;
            if (symmetryHandleControl != 0 && GUIUtility.hotControl != symmetryHandleControl)
                symmetryHandleControl = 0;
            if(EditorGUI.EndChangeCheck())ApplyLayerSymmetryEdit(ActiveTextureSet,frame);
        }

        private void ReleaseSymmetryHandleCapture()
        {
            if (symmetryHandleControl != 0 && GUIUtility.hotControl == symmetryHandleControl)
                GUIUtility.hotControl = 0;
            symmetryHandleControl = 0;
        }
        private static StrokeSample SymmetricUV(StrokeSample source,Matrix4x4 matrix,float brushRotation = 0)
        {
            var sample=source;Vector3 uv=matrix.MultiplyPoint3x4(new Vector3(source.uv.x,source.uv.y,0));
            Vector3 previous=matrix.MultiplyPoint3x4(new Vector3(source.previousUV.x,source.previousUV.y,0));
            sample.uv=new Vector2(uv.x,uv.y);sample.previousUV=new Vector2(previous.x,previous.y);
            sample.worldPosition=uv;sample.previousWorldPosition=previous;sample.direction=matrix.MultiplyVector(source.direction);
            Vector3 axis=matrix.MultiplyVector(Quaternion.Euler(0,0,source.rotation+brushRotation)*Vector3.right);
            sample.rotation=Mathf.Atan2(axis.y,axis.x)*Mathf.Rad2Deg-brushRotation;
            if(matrix.determinant<0)sample.footprintScale.y=-(Mathf.Abs(sample.footprintScale.y)<.00001f?1:sample.footprintScale.y);
            return sample;
        }
        private List<TexturePaintRibbonSegment> ExpandPathRibbon(List<TexturePaintRibbonSegment> source,bool uvSpace,BrushPreset pathBrush,Vector3 legacyPivot)
        {
            var result=new List<TexturePaintRibbonSegment>();
            foreach(var matrix in GetPathSymmetryTransforms(uvSpace,pathBrush,legacyPivot))foreach(var original in source)
            {
                var segment=original;
                Vector4 Point(Vector4 p){Vector3 v=matrix.MultiplyPoint3x4(p);return new Vector4(v.x,v.y,v.z,p.w);}
                Vector4 Direction(Vector4 p){Vector3 v=matrix.MultiplyVector(p);return new Vector4(v.x,v.y,v.z,p.w);}
                segment.leftStartAlong=Point(segment.leftStartAlong);segment.rightStartFlow=Point(segment.rightStartFlow);
                segment.leftEndAlong=Point(segment.leftEndAlong);segment.rightEndFlow=Point(segment.rightEndFlow);
                segment.normalStartPressure=Direction(segment.normalStartPressure);segment.normalEndPressure=Direction(segment.normalEndPressure);result.Add(segment);
            }
            return result;
        }
        private void CreateProjectionSymmetry(TextureSet set,TexturePaintLayer source)
        {
            var symmetry=ResolveLayerSymmetry(source);
            if(!symmetry.enabled || source?.projectionSettings?.placed!=true)return;
            FinishProjectionEdit();
            if(!TryResolveLogicalPeers(set,source,out List<TexturePaintLogicalLayerMember> peers,out string error))
            {ShowWorkspaceStatus(error);return;}
            var created=new List<LayerLocation>();
            try
            {
                var transforms=symmetry.Transforms();
                using var renderer=new TexturePaintProjectionRenderer();
                for(int i=1;i<transforms.Count;i++)
                {
                    var definition=TexturePaintSymmetry.TransformProjection(source.projectionSettings,transforms[i]);
                    Vector3 forward=definition.rotation*Vector3.forward;
                    if(controller.Reconstruction!=null && controller.Reconstruction.Raycast(
                        new Ray(definition.position+forward*definition.FrontDepth,-forward),selectedSlots,true,out var hitSurface,out var hit)
                        && hit.distance<=definition.FrontDepth-definition.BackDepth)
                    {definition.regionSurfaceId=controller.Textures.FindSet(hitSurface.index)?.persistentId;definition.regionTriangle=hit.triangleIndex;}
                    var targets=new List<TextureSet>();var layers=new List<TexturePaintLayer>();
                    string logicalId=System.Guid.NewGuid().ToString("N");
                    foreach(var peer in peers)
                    {
                        var copy=peer.textureSet.CloneLayer(peer.layer,source.name+" Instance "+i,false,false);
                        created.Add(new LayerLocation{set=peer.textureSet,layer=copy,index=peer.textureSet.layers.IndexOf(peer.layer)+i});
                        copy.logicalLayerId=peers.Count>1 || !string.IsNullOrEmpty(source.paintTargetId)?logicalId:null;
                        copy.paintTargetId=source.paintTargetId;
                        copy.links=new TexturePaintLayerLinks{instance=TexturePaintLayerReference.To(peer.textureSet,peer.layer)};
                        targets.Add(peer.textureSet);layers.Add(copy);
                    }
                    if(!renderer.Generate(controller.Textures.Sets,targets,layers,definition,out error))
                        throw new System.InvalidOperationException(error);
                }
            }
            catch(System.Exception exception)
            {
                foreach(var item in created)item.layer.Dispose();
                ShowWorkspaceStatus("Symmetry instances could not be created: "+exception.Message);return;
            }
            if(created.Count==0){ShowWorkspaceStatus("Enable a mirror axis or increase Radial Copies to create instances.");return;}
            AttachLayerLocations(created);
            RegisterCreatedLayers(created,"Create Projection Symmetry");
            RefreshLinkedContent();MarkDocumentDirtyAfterStructuralChange();RepaintAll();
        }
    }
}
