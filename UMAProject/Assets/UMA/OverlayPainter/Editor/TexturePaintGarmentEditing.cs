using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private TexturePaintGarmentPresetAsset garmentPresetAsset;
        [SerializeField] private bool pathHemSeamSelected;
        private static readonly TexturePaintGarmentPreset[] garmentConstructions=(TexturePaintGarmentPreset[])Enum.GetValues(typeof(TexturePaintGarmentPreset));
        private static readonly TexturePaintSeamPreset[] hemConstructions=(TexturePaintSeamPreset[])Enum.GetValues(typeof(TexturePaintSeamPreset));
        private static string[] projectionConstructionNames => projectionNames ??= BuildGarmentConstructionNames(false);
        private static string[] projectionNames;
        private static string[] pathConstructionNames => pathNames ??= BuildGarmentConstructionNames(true);
        private static string[] pathNames;
        private static bool HasGarment(TexturePaintLayer layer)=>layer?.projectionSettings?.garment?.enabled==true ||
            layer?.splineSettings?.garment?.enabled==true || layer?.splineSettings?.hemSeam?.enabled==true;

        private static string[] BuildGarmentConstructionNames(bool path)
        {
            var names=new List<string>();
            foreach(var preset in garmentConstructions)
                names.Add(TexturePaintGarmentSettings.FamilyName(TexturePaintGarmentSettings.Kind(preset))+"/"+ObjectNames.NicifyVariableName(preset.ToString()));
            if(path)foreach(var preset in hemConstructions)names.Add("Hems & Seams/"+ObjectNames.NicifyVariableName(preset.ToString()));
            return names.ToArray();
        }

        private static int ResolvePathConstruction(TexturePaintGarmentSettings garment,TexturePaintHemSeamSettings hem,bool hemSelected)
        {
            // Match renderer precedence, while recognizing documents authored with only hems.
            if(garment?.enabled==true || garment!=null && hem?.enabled!=true && !hemSelected)
                return Mathf.Max(0,Array.IndexOf(garmentConstructions,garment.preset));
            if(hem!=null)return garmentConstructions.Length+Mathf.Max(0,Array.IndexOf(hemConstructions,hem.preset));
            return 0;
        }

        private int CurrentPathConstruction(TexturePaintLayer layer)
        {
            int index=ResolvePathConstruction(pathGarment,pathHemSeam,pathHemSeamSelected);
            return Mathf.Clamp(index,0,pathConstructionNames.Length-1);
        }

        private static TexturePaintGarmentSettings ReplaceGarmentConstruction(TexturePaintGarmentPreset preset,TexturePaintGarmentSettings previous)
        {
            var next=TexturePaintGarmentSettings.Create(preset);
            if(previous==null)return next;
            next.albedo=previous.albedo;next.ao=previous.ao;next.roughnessOutput=previous.roughnessOutput;
            next.openStart=previous.openStart;next.openEnd=previous.openEnd;next.masks=previous.masks;
            next.depthStrength=previous.depthStrength;next.shadingStrength=previous.shadingStrength;
            next.foldInput=previous.foldInput?.Clone();next.protectionInput=previous.protectionInput?.Clone();
            return next;
        }

        private static void SelectPathConstruction(int index,bool enabled,ref TexturePaintGarmentSettings garment,ref TexturePaintHemSeamSettings hem)
        {
            garment?.Normalize();hem?.Normalize();
            index=Mathf.Clamp(index,0,pathConstructionNames.Length-1);
            if(index<garmentConstructions.Length)
            {
                var preset=garmentConstructions[index];
                if(garment==null || garment.preset!=preset)
                {
                    var next=ReplaceGarmentConstruction(preset,garment);
                    if(garment==null && hem!=null)
                    {
                        next.albedo=hem.albedo;next.ao=hem.ao;next.roughnessOutput=hem.roughness;next.masks=hem.masks;
                        next.depthStrength=hem.depthStrength;next.shadingStrength=hem.shadingStrength;
                    }
                    garment=next;
                }
                garment.enabled=enabled;if(hem!=null)hem.enabled=false;
            }
            else
            {
                var preset=hemConstructions[index-garmentConstructions.Length];
                if(hem==null || hem.preset!=preset)
                {
                    var next=TexturePaintHemSeamSettings.Create(preset);
                    if(hem!=null)
                    {
                        next.albedo=hem.albedo;next.ao=hem.ao;next.roughness=hem.roughness;next.masks=hem.masks;
                        next.depthStrength=hem.depthStrength;next.shadingStrength=hem.shadingStrength;
                    }
                    else if(garment!=null)
                    {
                        next.albedo=garment.albedo;next.ao=garment.ao;next.roughness=garment.roughnessOutput;next.masks=garment.masks;
                        next.depthStrength=garment.depthStrength;next.shadingStrength=garment.shadingStrength;
                    }
                    hem=next;
                }
                hem.enabled=enabled;if(garment!=null)garment.enabled=false;
            }
        }
        private void AddGarmentMenu(GenericMenu menu,TextureSet set,bool path)
        {
            foreach(TexturePaintGarmentPreset preset in Enum.GetValues(typeof(TexturePaintGarmentPreset)))
            {
                if(path && TexturePaintPathGenerators.IsLinearGarment(preset))continue;
                var selected=preset;
                menu.AddItem(new GUIContent("Garments/"+TexturePaintGarmentSettings.FamilyName(TexturePaintGarmentSettings.Kind(preset))+"/"+ObjectNames.NicifyVariableName(preset.ToString())),false,
                    ()=> { if(path)CreateGarmentPath(set,selected);else AddProjectionLayerCore(set,TexturePaintGarmentSettings.Create(selected)); });
            }

        }
        private void ShowProjectionCreationMenu(TextureSet set)
        {
            var menu=new GenericMenu();menu.AddItem(new GUIContent("Image / Overlay Projection"),false,()=>AddProjectionLayer(set));
            AddGarmentMenu(menu,set,false);menu.ShowAsContext();
        }
        private void CreateGarmentPath(TextureSet set,TexturePaintGarmentPreset preset)
        {
            if(set==null)return;BeginLayerCreationUndo("Create Garment Path");
            var layer=CreateSplineLayer(set);pathGarment=TexturePaintGarmentSettings.Create(preset);
            layer.name=ObjectNames.NicifyVariableName(preset.ToString());
            ActiveBrush.size=pathGarment.kind==TexturePaintGarmentKind.Hardware ? .014f:.06f;
            ActiveBrush.hardness=1;ActiveBrush.flow=1;ActiveBrush.rotation=0;ActiveBrush.blendMode=TexturePaintBlendMode.Normal;
            pathStartFade=pathEndFade=pathSideFadeExtra=0;pathStartFadeCurve=pathEndFadeCurve=pathSideFadeCurve=null;
            strength=1;tool=TexturePaintTool.Paint;sourceMode=TexturePaintSourceMode.SourceOverlay;
            CaptureSplineSettings(layer);CompleteLayerCreationUndo(layer);MarkDocumentDirty();RepaintAll();
        }
        private void DrawPathGarmentProperties(TextureSet set)
        {
            if(!TryGetActivePathLayer(set,out var layer) || pathGenerator?.enabled==true)return;
            // A disabled construction still belongs to its path and must remain editable.
            // Ordinary image paths have no garment settings until explicitly converted.
            if(pathGarment==null && pathHemSeam==null)
            {
                using(new EditorGUI.DisabledScope(layer.links?.instance?.IsSet==true))
                    if(GUILayout.Button("Convert to Garment…"))
                    {
                        var menu=new GenericMenu();AddPathGarmentConversionMenu(menu,set);menu.ShowAsContext();
                    }
                return;
            }
            var settings=pathGarment?.Clone();var hem=pathHemSeam?.Clone();
            int selected=CurrentPathConstruction(layer);
            bool enabled=settings?.enabled==true || hem?.enabled==true;
            float width=ActiveBrush.size*2;
            EditorGUI.BeginChangeCheck();
            using(new EditorGUI.DisabledScope(layer.links?.instance?.IsSet==true))
            {
                EditorGUILayout.Space(4);EditorGUILayout.LabelField("Garment Generator",EditorStyles.boldLabel);
                GUI.SetNextControlName("GarmentGeneratorEnabled");
                enabled=EditorGUILayout.Toggle(new GUIContent("Generate Garment Detail",
                    "Generate the selected garment construction along this ribbon. Assigned images stay saved while generation is enabled."),enabled);
                selected=EditorGUILayout.Popup("Construction Preset",selected,pathConstructionNames);
                SelectPathConstruction(selected,enabled,ref settings,ref hem);
                if(enabled)
                {
                    if(selected<garmentConstructions.Length)DrawGarmentSettings(set,layer,settings,true,false);
                    else DrawHemSeamSettings(set,hem);
                }
            }
            if(EditorGUI.EndChangeCheck())ApplyPathConstructionEdit(set,settings,hem,width,selected);
        }

        internal void AddPathGarmentConversionActions(GenericMenu menu)
            => AddPathGarmentConversionMenu(menu,ActiveTextureSet);

        private void AddPathGarmentConversionMenu(GenericMenu menu,TextureSet set)
        {
            if(!TryGetActivePathLayer(set,out var layer))return;
            foreach(var preset in garmentConstructions)
            {
                if(TexturePaintPathGenerators.IsLinearGarment(preset))continue;
                var selected=preset;
                var label=new GUIContent("Convert to Garment/"+
                    TexturePaintGarmentSettings.FamilyName(TexturePaintGarmentSettings.Kind(preset))+"/"+
                    ObjectNames.NicifyVariableName(preset.ToString()));
                if(layer.links?.instance?.IsSet==true)menu.AddDisabledItem(label);
                else menu.AddItem(label,false,()=>
                {
                    if(TryGetActivePathLayer(set,out var current) && ReferenceEquals(current,layer))
                        ConvertPathToGarment(set,selected);
                });
            }
        }

        private bool ConvertPathToGarment(TextureSet set,TexturePaintGarmentPreset preset)
        {
            if(!Enum.IsDefined(typeof(TexturePaintGarmentPreset),preset) ||
                TexturePaintPathGenerators.IsLinearGarment(preset))return false;
            return ApplyPathConstructionEdit(set,TexturePaintGarmentSettings.Create(preset),null,
                ActiveBrush.size*2,Array.IndexOf(garmentConstructions,preset));
        }

        private bool ApplyPathConstructionEdit(TextureSet set,TexturePaintGarmentSettings garment,TexturePaintHemSeamSettings hem,float width,int selected)
        {
            if(!TryGetActivePathLayer(set,out var layer) || layer.links?.instance?.IsSet==true)return false;
            if(!float.IsFinite(width))width=ActiveBrush.size*2;
            width=Mathf.Max(.0002f,width);
            string Json(object value)=>value==null?string.Empty:JsonUtility.ToJson(value);
            if(Json(garment)==Json(pathGarment) && Json(hem)==Json(pathHemSeam) &&
                width==ActiveBrush.size*2 && CurrentPathConstruction(layer)==selected)
            {
                return false;
            }
            BeginLightweightPathUndo(set,"Edit Garment Generator");
            pathGarment=garment?.Clone();pathHemSeam=hem?.Clone();
            if(pathGarment?.enabled==true || pathHemSeam?.enabled==true)pathGenerator=null;
            pathGarment?.Normalize();pathHemSeam?.Normalize();
            if(pathGarment?.enabled==true && pathHemSeam!=null)pathHemSeam.enabled=false;
            if(pathGarment?.enabled==true || pathHemSeam?.enabled==true){pathMode=TexturePaintPathMode.Ribbon;tool=TexturePaintTool.Paint;}
            ActiveBrush.size=width*.5f;pathHemSeamSelected=selected>=garmentConstructions.Length;
            CompleteLightweightPathEdit(set,false);return true;
        }
        private static float DrawPathWidthField(float width,bool worldSpace)
        {
            var label=new GUIContent("Path Width",worldSpace
                ? "Full width of the entire path in world units. Drag the slider or enter an exact value. Point Width (%) multiplies this width locally; 100% uses the full path width."
                : "Full width of the entire path in normalized UV units. Drag the slider or enter an exact value. Point Width (%) multiplies this width locally; 100% uses the full path width.");
            Rect control=EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(),label);
            float numericWidth=Mathf.Min(72,control.width*.45f);
            Rect sliderRect=new Rect(control.x,control.y,Mathf.Max(0,control.width-numericWidth-5),control.height);
            Rect numberRect=new Rect(control.xMax-numericWidth,control.y,numericWidth,control.height);
            // Keep exact numeric entry available beyond the ordinary slider range.
            EditorGUI.BeginChangeCheck();
            float dragged=GUI.HorizontalSlider(sliderRect,width,.0002f,Mathf.Max(worldSpace?1f:2f,width));
            if(EditorGUI.EndChangeCheck())width=dragged;
            GUI.SetNextControlName("PathWidth");
            return EditorGUI.FloatField(numberRect,width);
        }
        private void DrawGarmentSettings(TextureSet set,TexturePaintLayer layer,TexturePaintGarmentSettings s,bool path,bool drawSelector=true)
        {
            s.Normalize();
            if(drawSelector)
            {
            EditorGUILayout.Space(4);EditorGUILayout.LabelField("Garment Generator",EditorStyles.boldLabel);
            s.enabled=EditorGUILayout.Toggle("Generate Garment Detail",s.enabled);if(!s.enabled)return;
            var preset=garmentConstructions[EditorGUILayout.Popup("Construction Preset",Mathf.Max(0,Array.IndexOf(garmentConstructions,s.preset)),projectionConstructionNames)];
            if(preset!=s.preset)
            {
                var next=ReplaceGarmentConstruction(preset,s);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(next),s);
            }
            }
            s.kind=TexturePaintGarmentSettings.Kind(s.preset);s.variant=TexturePaintGarmentSettings.Variant(s.preset);
            EditorGUILayout.LabelField(TexturePaintGarmentSettings.FamilyName(s.kind),EditorStyles.miniBoldLabel);
            if(!path)
            EditorGUILayout.HelpBox(path ? "Procedural detail follows this ribbon. Ribbon Width and path length define the working area. Size values use world units on 3D paths and UV units on 2D paths." :
                "Move, rotate, scale or wrap this generated detail with the projection handles. Size values below are world units. Outputs share the same placement and deformation.",MessageType.None);
            garmentPresetAsset=(TexturePaintGarmentPresetAsset)EditorGUILayout.ObjectField("Saved Preset",garmentPresetAsset,typeof(TexturePaintGarmentPresetAsset),false);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(garmentPresetAsset?.settings==null))
                    if(GUILayout.Button("Load Preset")){JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(garmentPresetAsset.settings),s);s.enabled=true;GUI.changed=true;}
                if(GUILayout.Button("Save Preset..."))
                {
                    string destination=EditorUtility.SaveFilePanelInProject("Save Garment Generator Preset",ObjectNames.NicifyVariableName(s.preset.ToString()),"asset","Choose a preset asset location.");
                    if(!string.IsNullOrEmpty(destination))
                    {
                        var asset=ScriptableObject.CreateInstance<TexturePaintGarmentPresetAsset>();asset.settings=s.Clone();
                        // Reusable assets keep construction parameters, not links into this document.
                        asset.settings.foldInput=null;asset.settings.protectionInput=null;
                        AssetDatabase.CreateAsset(asset,destination);AssetDatabase.SaveAssetIfDirty(asset);garmentPresetAsset=asset;
                    }
                }
            }
            s.amount=EditorGUILayout.Slider("Amount",s.amount,0,1);
            bool zipper=s.kind==TexturePaintGarmentKind.Hardware && s.variant<=1;
            if(s.kind!=TexturePaintGarmentKind.Pockets && (s.kind!=TexturePaintGarmentKind.Hardware || s.variant<=1))
            s.detailSize=EditorGUILayout.FloatField(new GUIContent("Feature Spacing","Fold spacing, zipper tooth pitch, exposed yarn spacing or embroidery thread spacing."),s.detailSize);
            if(s.kind!=TexturePaintGarmentKind.Wear && s.preset!=TexturePaintGarmentPreset.PrintedLogo)
            {s.relief=EditorGUILayout.FloatField("Relief Height",s.relief);if(s.normal)s.normalStrength=EditorGUILayout.Slider("Normal Strength",s.normalStrength,0,8);
                s.depthStrength=EditorGUILayout.Slider(new GUIContent("3D Depth",
                    zipper ? "Strength of zipper relief and its depth shading. Zero flattens the construction; 1 is normal depth. The footprint stays unchanged."
                        : "Strength of physical relief. Zero flattens the construction; 1 is normal depth. Relief Shading controls the material shading independently."),s.depthStrength,0,4);
                if(s.height)EditorGUILayout.LabelField("Height blends with the existing normal. Adjust Height Strength under Layer Channels & Settings > Normal Control.",EditorStyles.wordWrappedMiniLabel);}
            s.shadingStrength=EditorGUILayout.Slider(new GUIContent("Relief Shading",
                zipper ? "Additional strength of zipper bevel and contact shading. 3D Depth also controls the zipper's depth tone."
                    : "Strength of bevel and contact shading in the generated material. Controls shading separately from physical relief."),s.shadingStrength,0,3);
            s.angle=EditorGUILayout.Slider("Pattern Rotation",s.angle,-180,180);
            if(s.kind!=TexturePaintGarmentKind.Pockets && s.kind!=TexturePaintGarmentKind.Hardware)
            {s.irregularity=EditorGUILayout.Slider("Irregularity",s.irregularity,0,1);s.seed=EditorGUILayout.IntField("Seed",s.seed);}
            s.falloff=EditorGUILayout.Slider("Area Edge Fade",s.falloff,.001f,.49f);
            if(s.SupportsOpenEnds)
            {
                bool closed=path && spline?.closed==true;
                using(new EditorGUI.DisabledScope(closed))
                {
                    s.openStart=!EditorGUILayout.Toggle(new GUIContent("Finish at Start",
                        "Close the beginning with an edge and cross stitching (or a zipper stop). Turn off to continue the strip through the beginning without an end fade."),!s.openStart);
                    s.openEnd=!EditorGUILayout.Toggle(new GUIContent("Finish at End",
                        "Close the end with an edge and cross stitching (or a zipper stop). Turn off to continue the strip through the end without an end fade."),!s.openEnd);
                }
                if(closed)EditorGUILayout.LabelField("Closed paths continue around the join without end finishes.",EditorStyles.wordWrappedMiniLabel);
            }
            if(s.kind==TexturePaintGarmentKind.Wrinkles && s.variant<=2)
            {
                if(s.variant==0) s.focus=EditorGUILayout.Vector2Field(new GUIContent("Tension Origin (0-1)","Anchor within the projection rectangle: 0,0 is one corner and 1,1 is the opposite corner."),s.focus);
                s.spread=EditorGUILayout.Slider(s.variant==0 ? "Fan Spread":"Fold Curvature",s.spread,.05f,2);
                s.taper=EditorGUILayout.Slider("Fold Taper",s.taper,0,1);
            }
            if(DrawPropertySubsectionFoldout("garment.aging","Wear & Material Response"))
            {
                if(s.kind!=TexturePaintGarmentKind.Hardware)
                    s.wear=EditorGUILayout.Slider("Raised / Edge Wear",s.wear,0,1);
                s.recess=EditorGUILayout.Slider(new GUIContent(s.kind==TexturePaintGarmentKind.Hardware?"Contact Shading":"Recess Darkening",
                    "Darkens contact areas in albedo, including materials without an ambient-occlusion texture channel."),s.recess,0,1);
                if(s.kind==TexturePaintGarmentKind.Wear){s.dirt=EditorGUILayout.Slider("Dirt",s.dirt,0,1);s.protection=EditorGUILayout.Slider("Seam Protection",s.protection,0,1);}
                s.roughness=EditorGUILayout.Slider("Cloth Roughness",s.roughness,0,1);
                if(s.kind==TexturePaintGarmentKind.Hardware)s.metalRoughness=EditorGUILayout.Slider("Hardware Roughness",s.metalRoughness,0,1);
            }
            if(s.kind==TexturePaintGarmentKind.Pockets||s.kind==TexturePaintGarmentKind.Hardware||s.kind==TexturePaintGarmentKind.Distress||s.kind==TexturePaintGarmentKind.Labels)
            {
                if(s.kind!=TexturePaintGarmentKind.Labels || s.variant!=2)
                s.surfaceColor=EditorGUILayout.ColorField("Cloth / Backing Color",s.surfaceColor);
                if(s.kind==TexturePaintGarmentKind.Hardware&&s.variant==3 || s.kind==TexturePaintGarmentKind.Distress&&s.variant==3)
                    s.threadColor=EditorGUILayout.ColorField("Thread Color",s.threadColor);
                if(s.kind==TexturePaintGarmentKind.Hardware||s.kind==TexturePaintGarmentKind.Labels)s.accentColor=EditorGUILayout.ColorField(s.kind==TexturePaintGarmentKind.Hardware ? "Hardware Color":"Ink Color",s.accentColor);
                if(s.kind==TexturePaintGarmentKind.Pockets)s.colorAmount=EditorGUILayout.Slider(new GUIContent("Replace Cloth Color","Zero retains the fabric beneath the pocket/panel."),s.colorAmount,0,1);
                if((s.kind==TexturePaintGarmentKind.Pockets || s.kind==TexturePaintGarmentKind.Hardware&&s.variant<=2 || s.kind==TexturePaintGarmentKind.Distress&&s.variant==4 || s.kind==TexturePaintGarmentKind.Labels&&s.variant!=2) && DrawPropertySubsectionFoldout("garment.stitch","Stitching"))
                {
                    s.threadColor=EditorGUILayout.ColorField("Thread Color",s.threadColor);s.stitchRows=EditorGUILayout.IntSlider("Stitch Rows",s.stitchRows,0,3);
                    s.stitchSize=EditorGUILayout.FloatField("Thread Width",s.stitchSize);s.stitchSpacing=EditorGUILayout.FloatField("Stitch Spacing",s.stitchSpacing);
                    s.edgeWidth=EditorGUILayout.Slider("Stitch Inset / Width",s.edgeWidth,.005f,.2f);
                }
            }
            if(s.kind==TexturePaintGarmentKind.Hardware&&s.variant<=1 || s.kind==TexturePaintGarmentKind.Pockets&&s.variant<=1)s.opening=EditorGUILayout.Slider("Opening / Separation",s.opening,0,.7f);
            if(s.kind==TexturePaintGarmentKind.Hardware)
            {if(s.variant<=1)
                {
                    s.zipperWidth=EditorGUILayout.Slider(new GUIContent("Zipper Width (%)",
                        "Scales the teeth, slider, pull and stops independently of the backing ribbon. Keeps tooth spacing and thread width unchanged."),s.zipperWidth*100,10,200)/100;
                    s.slider=EditorGUILayout.Slider("Zipper Slider Position",s.slider,0,1);
                }
                else s.repeat=EditorGUILayout.IntSlider("Hardware Count",s.repeat,1,32);}
            if(s.kind==TexturePaintGarmentKind.Distress||s.kind==TexturePaintGarmentKind.Labels)
            {s.damage=EditorGUILayout.Slider("Damage / Cracking",s.damage,0,1);if(s.kind==TexturePaintGarmentKind.Distress)s.fray=EditorGUILayout.Slider("Fraying",s.fray,0,1);}
            if(s.kind==TexturePaintGarmentKind.Labels)
            {
                s.motif=(Texture2D)EditorGUILayout.ObjectField("Logo / Motif",s.motif,typeof(Texture2D),false);
                s.motifSprite=(Sprite)EditorGUILayout.ObjectField("Logo Sprite",s.motifSprite,typeof(Sprite),false);
                s.motifScale=EditorGUILayout.Slider("Logo Size",s.motifScale,.05f,1);s.motifColor=EditorGUILayout.Toggle("Use Image Colors",s.motifColor);
                EditorGUILayout.LabelField("Sprite takes precedence. Without an image, a diamond emblem previews the material. For lettering, use a prepared logo or the existing Text generator output.",EditorStyles.wordWrappedMiniLabel);
            }
            if(s.kind==TexturePaintGarmentKind.Wear && !path && DrawPropertySubsectionFoldout("garment.inputs","Live Fold & Seam Inputs"))
            {
                s.foldInput=DrawLayerReference("Fold Height",set,layer,s.foldInput,true,TexturePaintChannel.NormalControl,TexturePaintReferenceComponent.Red);
                s.protectionInput=DrawLayerReference("Seam Protection",set,layer,s.protectionInput,true,TexturePaintChannel.Custom,TexturePaintReferenceComponent.Green);
                EditorGUILayout.HelpBox("Fold Height: choose a wrinkle layer's Normal Control, Read Red (neutral 0.5). Protection: choose a seam's Custom channel, Read Green, or a painted mask. Sources regenerate this wear automatically; missing or circular references retain cached output and show an error.",MessageType.None);
            }
            if(path && s.kind==TexturePaintGarmentKind.Wear)
                EditorGUILayout.LabelField("For live fold/seam inputs, create Wear with + Projection. Path wear follows its own procedural pattern.",EditorStyles.wordWrappedMiniLabel);
            if(DrawPropertySubsectionFoldout("garment.outputs","Generated Channels"))
            {
                s.albedo=EditorGUILayout.Toggle("Albedo",s.albedo);s.normal=EditorGUILayout.Toggle(new GUIContent("Normal (Direct)","Optional normal-map output. Ordinary layer blending replaces underlying normal detail; Height (Normal Control) uses RNM to merge relief."),s.normal);s.ao=EditorGUILayout.Toggle("Ambient Occlusion",s.ao);
                s.roughnessOutput=EditorGUILayout.Toggle("Roughness",s.roughnessOutput);s.metallic=EditorGUILayout.Toggle("Metallic",s.metallic);
                s.height=EditorGUILayout.Toggle("Height (Normal Control)",s.height);s.masks=EditorGUILayout.Toggle("Masks (Custom RGB)",s.masks);
                if(s.height&&s.normal)EditorGUILayout.HelpBox("Normal and Normal Control both affect bump shading. Usually use one output to avoid double relief.",MessageType.Warning);
                if(s.masks)EditorGUILayout.LabelField("Custom R: wear; G: protection; B: recess/AO. Use these in layer masks or live wear inputs.",EditorStyles.wordWrappedMiniLabel);
                string missing="";foreach(var channel in s.OutputChannels())if(set.GetChannel(channel)==null)missing+=(missing.Length==0?"":", ")+TexturePaintChannelUtility.DisplayName(channel);
                if(missing.Length>0)EditorGUILayout.HelpBox("Outputs unavailable on this target: "+missing,MessageType.Info);
            }
            EditorGUILayout.LabelField("Relief, openings and loose threads are texture effects. Use Normal layer blending to preserve fabric beneath grayscale shading.",EditorStyles.wordWrappedMiniLabel);
        }
    }
}
