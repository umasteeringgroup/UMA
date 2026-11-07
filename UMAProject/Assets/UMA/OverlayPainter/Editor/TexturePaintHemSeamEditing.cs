using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private void CreateHemSeamLayer(TextureSet set, TexturePaintSeamPreset preset)
        {
            if (set == null) return;
            BeginLayerCreationUndo("Create Garment Path");
            TexturePaintLayer layer = CreateSplineLayer(set);
            pathHemSeam = TexturePaintHemSeamSettings.Create(preset);
            // A new construction starts at garment scale with full coverage, independent of
            // the previously edited image ribbon's size, fade curves and brush opacity.
            ActiveBrush.size = .0125f;
            ActiveBrush.hardness = 1f;
            ActiveBrush.flow = 1f;
            ActiveBrush.rotation = 0f;
            ActiveBrush.blendMode = TexturePaintBlendMode.Normal;
            strength = 1f;
            pathSideFadeExtra = pathStartFade = pathEndFade = 0f;
            pathSideFadeCurve = pathStartFadeCurve = pathEndFadeCurve = null;
            layer.name = ObjectNames.NicifyVariableName(preset.ToString());
            tool = TexturePaintTool.Paint;
            sourceMode = TexturePaintSourceMode.SourceOverlay;
            CaptureSplineSettings(layer);
            CompleteLayerCreationUndo(layer);
            MarkDocumentDirty();
            ShowWorkspaceStatus("Garment path: place path points, then adjust Ribbon Width and Construction Preset in Path properties.");
        }

        private void DrawHemSeamSettings(TextureSet set,TexturePaintHemSeamSettings next)
        {
                next.Normalize();
                EditorGUILayout.LabelField("Hems & Seams",EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Procedural ribbon: width includes the cloth shading around the seam. Cloth shading preserves the fabric below; each stitch row has its own color. Uses the layer channels and standard path fades.",MessageType.None);
                next.profile=(TexturePaintSeamProfile)EditorGUILayout.EnumPopup("Cloth Profile",next.profile);
                next.bandWidth=Percent("Fold / Band Width (%)",next.bandWidth,5,95);
                next.offset=Percent("Construction Offset (%)",next.offset,-40,40);
                next.mirror=EditorGUILayout.Toggle(new GUIContent("Mirror Across Path","Mirror folds and all stitch rows together across the path."),next.mirror);
                next.relief=Percent("Relief / Width (%)",next.relief,0,15);
                next.depthStrength=EditorGUILayout.Slider(new GUIContent("3D Depth",
                    "Strength of cloth and thread relief. Zero flattens the construction; 1 is normal depth. Ribbon width stays unchanged."),next.depthStrength,0,4);
                next.shadingStrength=EditorGUILayout.Slider(new GUIContent("Relief Shading",
                    "Strength of fold, thread and contact shading, separate from physical relief."),next.shadingStrength,0,3);
                if (DrawPropertySubsectionFoldout("properties.path.hem.roping","Roping & Cloth Bunching"))
                {
                    next.roping=EditorGUILayout.Slider("Roping Amount",next.roping,0,1);
                    next.ropingFrequency=EditorGUILayout.Slider(new GUIContent("Ridges per Width","Number of raised/recessed cycles along a distance equal to the full path width. Closed paths fit whole cycles."),next.ropingFrequency,.25f,24);
                    next.ropingSlant=EditorGUILayout.Slider("Roping Slant",next.ropingSlant,-6,6);
                    next.irregularity=EditorGUILayout.Slider("Irregularity",next.irregularity,0,1);
                    next.seed=EditorGUILayout.IntField("Cloth Seed",next.seed);
                    next.puckering=EditorGUILayout.Slider("Stitch Puckering",next.puckering,0,1);
                    next.fraying=EditorGUILayout.Slider("Edge Fraying",next.fraying,0,1);
                }
                if (DrawPropertySubsectionFoldout("properties.path.hem.wear","Wear, Recesses & Finish"))
                {
                    next.recessDarkening=EditorGUILayout.Slider("Recess Darkening",next.recessDarkening,0,1);
                    next.wear=EditorGUILayout.Slider("Raised Cloth Wear",next.wear,0,1);
                    next.seamProtection=EditorGUILayout.Slider(new GUIContent("Protected Seam / Newness","Reduce wear at stitch lines and recesses, retaining a darker, less worn appearance. The optional Custom mask exposes this protection to other effects."),next.seamProtection,0,1);
                    next.ambientOcclusion=EditorGUILayout.Slider("Ambient Occlusion",next.ambientOcclusion,0,1);
                    next.fabricRoughness=EditorGUILayout.Slider("Cloth Roughness",next.fabricRoughness,0,1);
                    next.threadRoughness=EditorGUILayout.Slider("Thread Roughness",next.threadRoughness,0,1);
                    if(next.normal)next.normalStrength=EditorGUILayout.Slider("Normal Strength",next.normalStrength,0,4);
                    if(next.height)EditorGUILayout.LabelField("Height blends with the existing normal. Adjust Height Strength under Layer Channels & Settings > Normal Control.",EditorStyles.wordWrappedMiniLabel);
                }
                if (DrawPropertySubsectionFoldout("properties.path.hem.rows","Stitch Rows ("+next.rows.Count+")"))
                {
                    EditorGUILayout.LabelField("Offsets are measured from the construction center. Negative/positive positions put rows on opposite sides. Rows later in this list cross over earlier rows.",EditorStyles.wordWrappedMiniLabel);
                    if(next.preset==TexturePaintSeamPreset.DenimChainstitchHem)
                        EditorGUILayout.HelpBox("The outside of a chainstitched jean hem shows a straight needle line. Choose Chainstitch below to show the looped underside.",MessageType.None);
                    int remove=-1;
                    for(int i=0;i<next.rows.Count;i++)
                    {
                        var row=next.rows[i];
                        using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            using(new EditorGUILayout.HorizontalScope())
                            {
                                row.enabled=EditorGUILayout.ToggleLeft("Row "+(i+1),row.enabled);
                                if(GUILayout.Button("Remove",GUILayout.Width(65))) remove=i;
                            }
                            row.pattern=(TexturePaintStitchPattern)EditorGUILayout.EnumPopup("Stitch Pattern",row.pattern);
                            row.color=EditorGUILayout.ColorField("Thread Color",row.color);
                            row.offset=Percent("Position (%)",row.offset,-48,48);
                            row.thickness=Percent("Thread Width (%)",row.thickness,.2f,10);
                            row.spacing=Percent("Stitch Spacing (%)",row.spacing,2.5f,100);
                            row.length=Percent("Stitch Length / Spacing (%)",row.length,5,100);
                            if(row.pattern!=TexturePaintStitchPattern.Lockstitch)
                                row.span=Percent("Loop / Crosswise Span (%)",row.span,1,80);
                            row.phase=EditorGUILayout.Slider("Phase",row.phase,0,1);
                            row.relief=EditorGUILayout.Slider("Thread Relief",row.relief,0,2);
                        }
                    }
                    if(remove>=0) { next.rows.RemoveAt(remove);GUI.changed=true; }
                    using(new EditorGUI.DisabledScope(next.rows.Count>=TexturePaintHemSeamSettings.MaximumRows))
                        if(GUILayout.Button("Add Stitch Row")) { next.rows.Add(new TexturePaintSeamStitchRow {offset=0});GUI.changed=true; }
                    EditorGUILayout.LabelField("Up to eight rows. Coverstitch underside: add Cover Looper below the needle rows. A short path with Bar Tack produces pocket/corner reinforcement.",EditorStyles.wordWrappedMiniLabel);
                }
                if(DrawPropertySubsectionFoldout("properties.path.hem.outputs","Generated Channels"))
                {
                    next.albedo=EditorGUILayout.Toggle("Albedo (cloth + thread)",next.albedo);
                    next.normal=EditorGUILayout.Toggle(new GUIContent("Normal (Direct)","Optional normal-map output. Use Height (Normal Control) for RNM merging with the underlying cloth normal."),next.normal);
                    next.ao=EditorGUILayout.Toggle("Ambient Occlusion",next.ao);
                    next.roughness=EditorGUILayout.Toggle("Roughness",next.roughness);
                    next.height=EditorGUILayout.Toggle("Height (Normal Control)",next.height);
                    next.masks=EditorGUILayout.Toggle("Masks (Custom RGB)",next.masks);
                    if(next.normal && next.height) EditorGUILayout.HelpBox("Normal and Normal Control both affect bump shading. Usually enable one of them to avoid doubling relief.",MessageType.Warning);
                    if(next.masks) EditorGUILayout.LabelField("Custom: R = wear, G = protected seam/newness, B = recess/AO mask. Reference a component in a mask stack to control later effects.",EditorStyles.wordWrappedMiniLabel);
                    string missing="";
                    foreach(var channel in next.OutputChannels()) if(set.GetChannel(channel)==null)
                        missing+=(missing.Length==0 ? "" : ", ")+TexturePaintChannelUtility.DisplayName(channel);
                    if(missing.Length>0)EditorGUILayout.HelpBox("The current target has no matching channel for: "+missing+". These outputs are skipped on this target.",MessageType.Info);
                    if(!next.HasOutputs)EditorGUILayout.HelpBox("No channels selected; the generated path is cleared.",MessageType.Info);
                }
                EditorGUILayout.LabelField("Use Normal layer/channel blending for grayscale cloth shading. Relief and fraying are texture effects; the garment mesh is unchanged.",EditorStyles.wordWrappedMiniLabel);
        }
        private static float Percent(string label,float value,float minimum,float maximum)
            => EditorGUILayout.Slider(label,value*100f,minimum,maximum)*.01f;
    }
}
