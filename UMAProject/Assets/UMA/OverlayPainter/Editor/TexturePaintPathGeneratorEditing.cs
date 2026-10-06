using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    /// <summary>Optional editor UI for registered path generators. Edit the supplied working copy.</summary>
    public static class TexturePaintPathGeneratorEditors
    {
        public static readonly Dictionary<string, Action<TexturePaintPathGeneratorSettings>> Inspectors = new();
    }

    public sealed partial class TexturePaintStageWindow
    {
        private void AddPathGeneratorMenu(GenericMenu menu, TextureSet set)
        {
            menu.AddItem(new GUIContent("Generators/Automatic Seams from Boundaries..."),false,()=>OpenAutomaticSeams(set));
            menu.AddSeparator("Generators/");
            foreach(var generator in TexturePaintPathGenerators.All)
            {
                var id=generator.Id;
                menu.AddItem(new GUIContent("Generators/"+generator.MenuPath),false,()=>CreatePathGeneratorLayer(set,id));
            }
        }

        private void CreatePathGeneratorLayer(TextureSet set,string id,TexturePaintPathGeneratorSettings initialSettings=null)
        {
            var generator=TexturePaintPathGenerators.Find(id);
            if(set==null||generator==null)return;
            BeginLayerCreationUndo("Create Path Generator");
            var layer=CreateSplineLayer(set);
            pathGenerator=initialSettings?.Clone() ?? generator.CreateSettings();
            layer.name=generator.MenuPath.Substring(generator.MenuPath.LastIndexOf('/')+1);
            ActiveBrush.size=id==TexturePaintPathGenerators.TextId || TexturePaintPathGenerators.IsTattoo(id) ? .035f : .0125f;
            ActiveBrush.hardness=1;ActiveBrush.flow=1;ActiveBrush.rotation=0;
            ActiveBrush.blendMode=TexturePaintBlendMode.Normal;
            pathStartFade=pathEndFade=pathSideFadeExtra=0;
            pathStartFadeCurve=pathEndFadeCurve=pathSideFadeCurve=null;
            strength=1;tool=TexturePaintTool.Paint;paintSource=TexturePaintBrushSource.Color;
            sourceMode=TexturePaintSourceMode.SourceOverlay;
            EnsurePathGeneratorChannels(set,layer,pathGenerator);
            CaptureSplineSettings(layer);CompleteLayerCreationUndo(layer);
            MarkDocumentDirty();RepaintAll();
            ShowWorkspaceStatus("Path generator ready. Shift-click to place points; edit its settings in Properties.");
        }

        private static IEnumerable<TexturePaintChannel> PathGeneratorDefaultChannels(TexturePaintPathGeneratorSettings settings)
        {
            if(settings.seam!=null)return settings.seam.OutputChannels();
            if(settings.garment!=null)return settings.garment.OutputChannels();
            return new[]{TexturePaintChannel.Albedo,TexturePaintChannel.Roughness,TexturePaintChannel.NormalControl};
        }

        private static void EnsurePathGeneratorChannels(TextureSet set,TexturePaintLayer layer,TexturePaintPathGeneratorSettings settings)
        {
            foreach(var channel in PathGeneratorDefaultChannels(settings))
            {
                if(layer.channels.ContainsKey(channel))continue;
                TryAddPathSourceChannel(set,layer,channel,new TexturePaintChannelSourceSettings
                { source=TexturePaintBrushSource.Color,color=channel==TexturePaintChannel.Roughness ? new Color(.65f,.65f,.65f,1) : DefaultChannelSourceColor(channel) });
            }
            layer.GetChannelSettings(TexturePaintChannel.NormalControl).blendMode=TexturePaintBlendMode.Overlay;
        }

        internal static void PopulatePathGeneratorSources(StrokeContext context,TexturePaintLayer layer)
        {
            context.channelSources.Clear();
            var settings=context.pathGenerator;
            if(settings.seam!=null)settings.seam.PopulateSources(context);
            if(settings.garment!=null)settings.garment.PopulateSources(context);
            foreach(var channel in layer.channels.Keys)
            {
                // The legacy seam/garment output switches remain authoritative for their channels.
                bool managed=settings.seam!=null && (channel==TexturePaintChannel.Albedo||channel==TexturePaintChannel.Normal||
                    channel==TexturePaintChannel.Roughness||channel==TexturePaintChannel.AmbientOcclusion||channel==TexturePaintChannel.NormalControl||channel==TexturePaintChannel.Custom);
                managed|=settings.garment!=null && (channel==TexturePaintChannel.Albedo||channel==TexturePaintChannel.Normal||channel==TexturePaintChannel.Metallic||
                    channel==TexturePaintChannel.Roughness||channel==TexturePaintChannel.AmbientOcclusion||channel==TexturePaintChannel.NormalControl||channel==TexturePaintChannel.Custom);
                if(managed&&!context.channelSources.ContainsKey(channel))continue;
                var source=layer.GetChannelSettings(channel).sourceSettings;
                context.channelSources[channel]=new TexturePaintChannelSourceSettings
                { source=TexturePaintBrushSource.Color,color=source?.source==TexturePaintBrushSource.Color ? source.color : DefaultChannelSourceColor(channel) };
            }
        }

        private void DrawPathGeneratorProperties(TextureSet set)
        {
            if(!TryGetActivePathLayer(set,out var layer))return;
            var choices=TexturePaintPathGenerators.All;
            var labels=new List<string>{"None (image / garment path)"};int selected=0;
            for(int i=0;i<choices.Count;i++)
            { labels.Add(choices[i].MenuPath);if(choices[i].Id==pathGenerator?.generatorId)selected=i+1; }
            bool missing=pathGenerator!=null&&TexturePaintPathGenerators.Find(pathGenerator.generatorId)==null;
            if(missing){labels.Add("Missing: "+pathGenerator.generatorId);selected=labels.Count-1;}
            var next=pathGenerator?.Clone();float width=ActiveBrush.size*2;
            using(new EditorGUI.DisabledScope(layer.links?.instance?.IsSet==true))
            {
                EditorGUILayout.Space(4);EditorGUILayout.LabelField("Path Generator",EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                int selection=EditorGUILayout.Popup("Generator",selected,labels.ToArray());
                bool changedGenerator=selection!=selected;
                if(changedGenerator)next=selection==0?null:choices[selection-1].CreateSettings();
                if(next!=null)
                {
                    next.enabled=EditorGUILayout.Toggle("Enabled",next.enabled);
                    width=DrawGarmentRibbonWidth(width,spline.worldSpace);
                    if(missing&&!changedGenerator)EditorGUILayout.HelpBox("Install this generator to edit it. The saved settings and output are preserved.",MessageType.Warning);
                    else if(next.seam!=null)DrawHemSeamSettings(set,next.seam);
                    else if(next.garment!=null)DrawGarmentSettings(set,layer,next.garment,true,false);
                    else if(next.generatorId==TexturePaintPathGenerators.TextId)DrawTextGenerator(next);
                    else if(TexturePaintPathGenerators.IsTattoo(next.generatorId))DrawTattooShapeGenerator(next);
                    else if(next.generatorId==TexturePaintPathGenerators.ScarId)DrawScarPathGenerator(next);
                    else if(TexturePaintPathGeneratorEditors.Inspectors.TryGetValue(next.generatorId,out var inspector))inspector(next);
                    else next.extensionJson=EditorGUILayout.TextArea(next.extensionJson,GUILayout.MinHeight(60));
                    EditorGUILayout.HelpBox("Add material channels in Layer Channels & Settings. All outputs follow the path; Tattoo and Lettering channels share the visible ink silhouette.",MessageType.None);
                }
                if(EditorGUI.EndChangeCheck())
                {
                    BeginLightweightPathUndo(set,"Edit Path Generator");
                    pathGenerator=next;pathGenerator?.Normalize();
                    if(pathGenerator?.enabled==true)
                    {
                        pathHemSeam=null;pathGarment=null;pathMode=TexturePaintPathMode.Ribbon;tool=TexturePaintTool.Paint;
                    }
                    ActiveBrush.size=Mathf.Max(.0001f,width*.5f);
                    CompleteLightweightPathEdit(set,false);
                }
            }
        }

        private static void DrawTextGenerator(TexturePaintPathGeneratorSettings s)
        {
            EditorGUILayout.LabelField("Text");s.text=EditorGUILayout.TextArea(s.text,GUILayout.MinHeight(48));
            s.font=(Font)EditorGUILayout.ObjectField("Font",s.font,typeof(Font),false);
            s.fontStyle=(FontStyle)EditorGUILayout.EnumPopup("Font Style",s.fontStyle);
            s.textFlipX=EditorGUILayout.Toggle(new GUIContent("Flip X","Mirror the text left/right along the path to correct UV orientation."),s.textFlipX);
            s.textFlipY=EditorGUILayout.Toggle(new GUIContent("Flip Y","Mirror the text top/bottom across the path. Enable both flips to rotate the lettering 180 degrees."),s.textFlipY);
            s.color=EditorGUILayout.ColorField("Ink Color",s.color);
            s.preserveTextAspect=EditorGUILayout.Toggle("Preserve Letter Proportions",s.preserveTextAspect);
            s.textRepeats=EditorGUILayout.IntSlider("Repeats Along Path",s.textRepeats,1,64);
            s.textInset=EditorGUILayout.Slider("Inset",s.textInset,0,.45f);
            s.relief=EditorGUILayout.Slider("Raised / Engraved",s.relief,-.4f,.4f);
            s.normalStrength=EditorGUILayout.Slider("Normal Strength",s.normalStrength,0,4);
            if(string.IsNullOrWhiteSpace(s.text))EditorGUILayout.HelpBox("Enter text to generate lettering. Empty text clears its output.",MessageType.Info);
        }

        private static void DrawTattooShapeGenerator(TexturePaintPathGeneratorSettings s)
        {
            var design=(TexturePaintTattooDesign)EditorGUILayout.EnumPopup("Design",s.tattooDesign);
            if(design!=s.tattooDesign)
            {
                s.tattooDesign=design;
                s.curlStart=s.curlEnd=design==TexturePaintTattooDesign.HookedLine;
                if(design==TexturePaintTattooDesign.HookedLine){s.lineWidth=.12f;s.lineBend=.4f;s.curlRadius=.19f;}
                if(design==TexturePaintTattooDesign.Spiral)s.tattooAspect=1;
                if(design==TexturePaintTattooDesign.TribalFlame||design==TexturePaintTattooDesign.TribalScroll)s.proceduralTribal=true;
            }
            s.color=EditorGUILayout.ColorField("Ink Color",s.color);
            s.lineWidth=EditorGUILayout.Slider("Line Thickness",s.lineWidth,.01f,.65f);
            s.tattooAspect=EditorGUILayout.Slider("Design Aspect",s.tattooAspect,1,8);
            s.tattooRepeats=EditorGUILayout.IntSlider("Repeats Along Path",s.tattooRepeats,1,32);
            s.tattooMirror=EditorGUILayout.Toggle("Flip Across Path",s.tattooMirror);
            if(s.tattooDesign==TexturePaintTattooDesign.TaperedLine||s.tattooDesign==TexturePaintTattooDesign.HookedLine)
            {
                s.lineBend=EditorGUILayout.Slider("Line Bend",s.lineBend,-1,1);
                s.taperStart=EditorGUILayout.Toggle("Taper Start to Center",s.taperStart);
                if(s.taperStart)s.taperStartLength=EditorGUILayout.Slider("Start Taper Length",s.taperStartLength,.01f,1);
                s.taperEnd=EditorGUILayout.Toggle("Taper End to Center",s.taperEnd);
                if(s.taperEnd)s.taperEndLength=EditorGUILayout.Slider("End Taper Length",s.taperEndLength,.01f,1);
            }
            s.taperPower=EditorGUILayout.Slider("Taper Curvature",s.taperPower,.25f,4);
            if(s.tattooDesign!=TexturePaintTattooDesign.Spiral)
            {
                s.curlStart=EditorGUILayout.Toggle("Curl Start Inward",s.curlStart);
                if(s.curlStart)s.curlStartSide=EditorGUILayout.Popup("Start Curl Direction",s.curlStartSide<0?1:0,new[]{"Left","Right"})==0?1:-1;
                s.curlEnd=EditorGUILayout.Toggle("Curl End Inward",s.curlEnd);
                if(s.curlEnd)s.curlEndSide=EditorGUILayout.Popup("End Curl Direction",s.curlEndSide<0?1:0,new[]{"Left","Right"})==0?1:-1;
                if(s.curlStart||s.curlEnd)s.curlRadius=EditorGUILayout.Slider("Curl Radius",s.curlRadius,.03f,.22f);
            }
            if(s.curlStart||s.curlEnd||s.tattooDesign>=TexturePaintTattooDesign.Spiral)
                s.curlTurns=EditorGUILayout.Slider("Curl Turns",s.curlTurns,.15f,1.8f);
            if(s.tattooDesign==TexturePaintTattooDesign.TribalFlame||s.tattooDesign==TexturePaintTattooDesign.TribalScroll)
            {
                DrawTribalArmControls(s);
                s.negativeSpace=EditorGUILayout.Slider("Negative-space Cuts",s.negativeSpace,0,.2f);
            }
            s.relief=EditorGUILayout.Slider("Raised / Engraved",s.relief,-.4f,.4f);
            s.normalStrength=EditorGUILayout.Slider("Normal Strength",s.normalStrength,0,4);
            EditorGUILayout.HelpBox("Tapers curve the outline toward the centerline, keeping solid ink. Curl either end independently. Combine paths to build larger tribal designs.",MessageType.None);
        }

        private static void DrawTribalArmControls(TexturePaintPathGeneratorSettings s)
        {
            s.proceduralTribal=EditorGUILayout.Toggle(new GUIContent("Procedural Arms",
                "Build branches from the controls below. Turn off to retain the original fixed tribal motif."),s.proceduralTribal);
            if(!s.proceduralTribal)return;
            s.tribalSymmetry=(TexturePaintTattooSymmetry)EditorGUILayout.EnumPopup("Arm Symmetry",s.tribalSymmetry);
            if(s.tribalSymmetry==TexturePaintTattooSymmetry.None)
                s.tribalArmCount=EditorGUILayout.IntSlider("Arm Count",s.tribalArmCount,0,16);
            else
            {
                s.tribalArmCount=2*EditorGUILayout.IntSlider(new GUIContent("Arm Pairs",
                    "Each pair has two reflected or rotated arms. Variation is shared within each pair."),(s.tribalArmCount+1)/2,0,8);
                EditorGUILayout.LabelField("Total Arms",s.tribalArmCount.ToString());
            }
            using(new EditorGUI.DisabledScope(s.tribalArmCount==0))
            {
                s.tribalBranchStart=EditorGUILayout.Slider(new GUIContent("Branch Start",
                    "Start of the attachment region along the design, from 0 to 1."),s.tribalBranchStart,.05f,.95f);
                s.tribalBranchEnd=EditorGUILayout.Slider(new GUIContent("Branch End",
                    "End of the attachment region. In Half Turn symmetry each arm also has a partner at the opposite end."),s.tribalBranchEnd,s.tribalBranchStart,.95f);
                s.tribalArmLength=EditorGUILayout.Slider("Arm Length",s.tribalArmLength,.1f,1.5f);
                s.tribalArmAngle=EditorGUILayout.Slider("Arm Angle",s.tribalArmAngle,10,160);
                s.tribalArmSweep=EditorGUILayout.Slider("Arm Sweep",s.tribalArmSweep,-1,1);
                s.tribalCurlDirection=(TexturePaintTattooCurlDirection)EditorGUILayout.EnumPopup("Arm Curl Direction",s.tribalCurlDirection);
                if(s.tattooDesign==TexturePaintTattooDesign.TribalScroll)
                    s.curlRadius=EditorGUILayout.Slider("Arm Curl Radius",s.curlRadius,.03f,.22f);
                s.tribalVariation=EditorGUILayout.Slider(new GUIContent("Variation",
                    "Seeded variation in branch placement, length, angle, thickness and curls. Zero ignores the seed."),s.tribalVariation,0,1);
                using(new EditorGUI.DisabledScope(s.tribalVariation==0))
                using(new EditorGUILayout.HorizontalScope())
                {
                    s.seed=EditorGUILayout.IntField("Seed",s.seed);
                    if(GUILayout.Button(new GUIContent("Next","Choose the next reproducible variation."),GUILayout.Width(45)))
                    { s.seed=unchecked(s.seed+1);GUI.changed=true; }
                }
            }
            s.lineBend=EditorGUILayout.Slider("Spine Bend",s.lineBend,-1,1);
            EditorGUILayout.HelpBox("The complete design fits within the path width. Longer arms make the spine relatively smaller. Mirror Across Path reflects branches; Half Turn rotates partners by 180°. Flip Across Path reverses the entire result.",MessageType.None);
        }

        private void DrawPathGeneratorChannelSource(TextureSet set,TexturePaintLayer layer,TexturePaintChannel channel)
        {
            var generator=layer.splineSettings.pathGenerator;
            bool generated=channel==TexturePaintChannel.Albedo||channel==TexturePaintChannel.Normal||channel==TexturePaintChannel.NormalControl||
                generator.generatorId!=TexturePaintPathGenerators.TextId && !TexturePaintPathGenerators.IsTattoo(generator.generatorId) &&
                (channel==TexturePaintChannel.Roughness||channel==TexturePaintChannel.AmbientOcclusion);
            if(generated)
            {
                EditorGUILayout.LabelField("Appearance is controlled by Path Generator settings.",EditorStyles.wordWrappedMiniLabel);
                return;
            }
            var source=layer.GetChannelSettings(channel).sourceSettings?.Clone() ?? new TexturePaintChannelSourceSettings
            { color=DefaultChannelSourceColor(channel) };
            source.source=TexturePaintBrushSource.Color;
            EditorGUI.BeginChangeCheck();
            if(TexturePaintChannelUtility.IsGrayscale(channel))
            {
                float value=EditorGUILayout.Slider("Value",TexturePaintChannelUtility.ScalarValue(source.color),0,1);
                source.color=new Color(value,value,value,1);
            }
            else source.color=EditorGUILayout.ColorField("Color",source.color);
            if(EditorGUI.EndChangeCheck()&&ChangeLayerChannelSources(set,layer,new Dictionary<TexturePaintChannel,TexturePaintChannelSourceSettings>{{channel,source}}))
                RequestSplineReapply(set,false);
        }

        internal static TexturePaintPathGeneratorSettings MigrateLegacyScarSettings(TexturePaintPluginParameterSet parameters)
        {
            var s=TexturePaintPathGenerators.Find(TexturePaintPathGenerators.ScarId).CreateSettings();
            if(parameters==null)return s;
            s.healing=parameters.Float("scarAge",s.healing);
            s.woundType=(TexturePaintPathWoundType)parameters.Integer("woundType",(int)s.woundType);
            s.color=parameters.Color("freshColor",s.color);s.healedColor=parameters.Color("insideColor",s.healedColor);
            s.rimColor=parameters.Color("sideColor",s.rimColor);
            s.inflammation=parameters.Float("inflammation",s.inflammation);
            s.depth=parameters.Float("height",s.depth);s.edgeLift=parameters.Float("rimHeight",s.edgeLift);
            s.roughness=parameters.Float("insideRoughness",s.roughness);s.seed=parameters.Integer("seed",s.seed);
            s.irregularity=parameters.Float("randomness",s.irregularity);s.Normalize();return s;
        }

        private void DrawLegacyScarMigration(TextureSet set,TexturePaintLayer layer)
        {
            if(layer.pluginId!="com.uma.texturepaint.scar-wound")return;
            EditorGUILayout.HelpBox("Scar and Wound is now a path generator: +Path > Generators > Skin. Existing procedural and guide-texture scars remain editable here. Copy these material settings to a new path, then place its points.",MessageType.Info);
            if(GUILayout.Button("Copy Settings to New Scar Path"))
            {
                var settings=MigrateLegacyScarSettings(layer.pluginParameters);
                CreatePathGeneratorLayer(set,TexturePaintPathGenerators.ScarId,settings);
            }
        }

        private static void DrawScarPathGenerator(TexturePaintPathGeneratorSettings s)
        {
            var type=(TexturePaintPathWoundType)EditorGUILayout.EnumPopup("Damage Type",s.woundType);
            if(type!=s.woundType)
            {
                s.woundType=type;
                s.healing=type==TexturePaintPathWoundType.FreshCut ? .15f : .8f;
                s.opening=type==TexturePaintPathWoundType.Burn ? .65f : type==TexturePaintPathWoundType.HealedScar ? .06f : .3f;
            }
            s.healing=EditorGUILayout.Slider("Healing",s.healing,0,1);
            s.opening=EditorGUILayout.Slider("Opening / Width",s.opening,.005f,.8f);
            s.depth=EditorGUILayout.Slider("Wound Depth",s.depth,0,.4f);
            s.edgeLift=EditorGUILayout.Slider("Edge Lift",s.edgeLift,0,.3f);
            s.taper=EditorGUILayout.Slider("Endpoint Taper",s.taper,0,1);
            s.irregularity=EditorGUILayout.Slider("Edge Irregularity",s.irregularity,0,1);
            s.inflammation=EditorGUILayout.Slider("Inflammation",s.inflammation,0,1);
            s.color=EditorGUILayout.ColorField("Wound Color",s.color);
            s.rimColor=EditorGUILayout.ColorField("Rim Color",s.rimColor);
            s.healedColor=EditorGUILayout.ColorField("Healed Color",s.healedColor);
            s.roughness=EditorGUILayout.Slider("Fresh Roughness",s.roughness,0,1);
            s.normalStrength=EditorGUILayout.Slider("Normal Strength",s.normalStrength,0,4);
            s.seed=EditorGUILayout.IntField("Seed",s.seed);
            s.stitches=EditorGUILayout.Toggle("Sutures",s.stitches);
            if(!s.stitches)return;
            s.removedStitches=EditorGUILayout.Toggle("Removed (Punctures Only)",s.removedStitches);
            s.stitchCount=EditorGUILayout.IntSlider("Stitch Count",s.stitchCount,1,256);
            s.stitchSpan=EditorGUILayout.Slider("Stitch Span / Width",s.stitchSpan,.05f,.9f);
            s.stitchThickness=EditorGUILayout.Slider("Thread Thickness / Width",s.stitchThickness,.002f,.08f);
            s.stitchAngle=EditorGUILayout.Slider("Stitch Angle",s.stitchAngle,-75,75);
            s.threadColor=EditorGUILayout.ColorField("Thread Color",s.threadColor);
            s.puckering=EditorGUILayout.Slider("Skin Puckering",s.puckering,0,1);
        }
    }
}
