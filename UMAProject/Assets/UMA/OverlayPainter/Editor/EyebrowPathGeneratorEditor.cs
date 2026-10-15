using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    [InitializeOnLoad]
    public static class EyebrowPathGeneratorEditor
    {
        private sealed class EditingState : ScriptableObject { public EyebrowSettings settings=new(); }
        private static EditingState state;
        private static SerializedObject serialized;
        private static string lastJson;
        private static readonly HashSet<string> expanded=new();
        private static bool applyPresetShape;
        static EyebrowPathGeneratorEditor()
        {
            EyebrowPathGenerator.Register();
            TexturePaintPathGeneratorEditors.Inspectors[EyebrowPathGenerator.GeneratorId]=Draw;
            AssemblyReloadEvents.beforeAssemblyReload+=Dispose;
            EditorApplication.quitting+=Dispose;
        }
        private static void Dispose()
        {if(state!=null)UnityEngine.Object.DestroyImmediate(state);state=null;serialized=null;EyebrowPathGenerator.ClearCaches();}
        private static void Draw(TexturePaintPathGeneratorSettings settings)
        {
            try
            {
                if(state==null)
                {
                    state=ScriptableObject.CreateInstance<EditingState>();
                    // Keep temporary inspector data hidden and unsaved, but editable.
                    // HideAndDontSave also includes NotEditable, disabling PropertyField controls.
                    state.hideFlags=HideFlags.HideAndDontSave & ~HideFlags.NotEditable;
                    serialized=new SerializedObject(state);lastJson=null;
                }
                if(lastJson!=settings.extensionJson){state.settings=EyebrowSettings.Load(settings.extensionJson);lastJson=settings.extensionJson;}
                serialized.Update();
                EditorGUILayout.LabelField(state.settings.reverse?"TAIL  ←  HEAD (nose)":"HEAD (nose)  →  TAIL",EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Place the first point near the nose and draw toward the temple. Path Width defines the base ribbon; point widths multiply it. u follows arc length, v runs lower → upper, s runs root → tip.",MessageType.None);
                EditorGUI.BeginChangeCheck();
                var chosen=(BrowPreset)EditorGUILayout.EnumPopup("Preset",state.settings.preset);
                applyPresetShape=EditorGUILayout.Toggle(new GUIContent("Apply preset shape too","Normally preserves all shape profiles and your editable path."),applyPresetShape);
                if(chosen!=state.settings.preset)
                {
                    serialized.ApplyModifiedProperties();var previous=state.settings;var next=EyebrowSettings.Preset(chosen);
                    if(!applyPresetShape)
                    {
                        next.fullness=previous.fullness;next.upper=previous.upper;next.lower=previous.lower;
                        next.centerOffset=previous.centerOffset;next.lengthScale=previous.lengthScale;next.rotation=previous.rotation;next.slant=previous.slant;
                        next.archHeight=previous.archHeight;next.archPosition=previous.archPosition;next.tailDrop=previous.tailDrop;
                        next.headTaper=previous.headTaper;next.tailTaper=previous.tailTaper;next.slits=previous.slits;
                    }
                    next.reverse=previous.reverse;next.flipAcross=previous.flipAcross;next.seed=previous.seed;next.advanced=previous.advanced;
                    state.settings=next;serialized.Update();GUI.changed=true;
                }
                Field("advanced","Advanced controls");
                Parameter("fullness","Brow width / fullness","u; factor of Path Width",0,2);
                Field("count","Main hair count (before secondary / baby)");
                Parameter("length","Hair length","u; base path widths",.01f,2);
                Parameter("thickness","Strand thickness","u; base path widths",.0001f,.05f);
                Parameter("hairSlant","Hair slant","u; additive degrees from local path tangent",-90,90);
                Parameter("directionVariation","Groom disorder","u; independent angular variation ± degrees",0,60);
                Field("color","Main color");
                Field("seed","Master seed","Changes the deterministic hair variation. The same seed and settings reproduce the same eyebrow; preview quality does not reshuffle hairs.");
                Field("reverse","Reverse head / tail (keep path)");Field("flipAcross","Swap lower / upper sides");
                if(state.settings.advanced)
                {
                    Section("Shape","lengthScale rotation slant upper lower centerOffset archPosition archHeight tailDrop headTaper tailTaper edgeSoftness containment strayAllowance slits");
                    Section("Distribution","distribution areaDensity density densityAcross distributionStart distributionEnd thinning secondary secondaryLength secondaryThickness baby babyLength babyThickness");
                    Section("Flow","headAngle bodyAngle archAngle tailAngle headEnd tailStart upperAngle lowerAngle convergence convergenceStrength");
                    Section("Strands","lengthAcross thicknessAcross opacity bend strandWidth strandOpacity rootFade tipFade rootTaper tipTaper sharpness trimmedEnds rootFadeCurve tipFadeCurve rootTaperCurve tipTaperCurve");
                    Section("Variation","rootJitter densityVariation lengthVariation thicknessVariation bendVariation waviness waveVariation clumpVariation colorVariation opacityVariation flyaways broadVariation");
                    Section("Clumping","clumpStrength clumpSpacing clumpSize strandClump tipSpread guideVariation preserveLength");
                    Section("Appearance","tipColor colorAlongStrand layerDepthContrast grayFraction grayColor roughness roughnessVariation relief ao");
                    Section("Symmetry","symmetry sideSeed asymmetry");
                    if(expanded.Contains("Symmetry"))EditorGUILayout.HelpBox("Enable the path's Mirror X for paired brows. Exact Mirror shares hairs; Linked Shape varies the mirrored hairs by the Asymmetry curve; Independent uses a fully independent hair seed. For independently editable shapes use two path layers and disable path symmetry. Shared/overlapping UV texels cannot hold independent hairs.",MessageType.Info);
                    Section("Quality","finalQuality maximumRaster diagnostic");
                    if(expanded.Contains("Quality"))EditorGUILayout.HelpBox("Raster size is estimated from the brow's UV footprint. Preview and final use identical strands. Growth diagnostic is local (R = tangent X, G = across Y, signed −1..1 encoded 0..1), not a material-ready world/tangent flow map. Export in Material mode for production.",MessageType.Info);
                }
                if(EditorGUI.EndChangeCheck())
                {
                    serialized.ApplyModifiedProperties();settings.extensionJson=lastJson=state.settings.Save();GUI.changed=true;
                }
                else serialized.ApplyModifiedProperties();
                EditorGUILayout.HelpBox("Use the path's Auto toggle for live updates, or turn it off and press Update. Eyebrows require open paths. Height is written to Normal Control only; add AO if desired.",MessageType.None);
                if(!string.IsNullOrEmpty(EyebrowPathGenerator.LastStatistics))EditorGUILayout.LabelField(EyebrowPathGenerator.LastStatistics,EditorStyles.wordWrappedMiniLabel);
                if(!string.IsNullOrEmpty(EyebrowPathGenerator.LastWarning))EditorGUILayout.HelpBox(EyebrowPathGenerator.LastWarning,MessageType.Warning);
            }
            catch(ExitGUIException){throw;}
            catch(Exception e){EditorGUILayout.HelpBox(e.Message,MessageType.Error);}
        }
        private static SerializedProperty Property(string name)=>serialized.FindProperty("settings."+name);
        private static void Field(string name,string label=null,string tooltip=null)=>EditorGUILayout.PropertyField(Property(name),new GUIContent(label??ObjectNames.NicifyVariableName(name),tooltip),true);
        private static void Section(string name,string fields)
        {
            bool was=expanded.Contains(name),now=EditorGUILayout.Foldout(was,name,true);
            if(now)expanded.Add(name);else expanded.Remove(name);
            if(!now)return;
            using(new EditorGUI.IndentLevelScope())foreach(string field in fields.Split(' '))
            {
                if(typeof(EyebrowSettings).GetField(field)?.FieldType==typeof(BrowParameter))
                {
                    bool strand=field.StartsWith("strand",StringComparison.Ordinal)||field=="colorAlongStrand";
                    bool across=field.EndsWith("Across",StringComparison.Ordinal);
                    string domain=strand?"s: root → tip":across?"v: lower → upper":"u: head → tail";
                    Parameter(field,ObjectNames.NicifyVariableName(field),domain,float.NaN,float.NaN);
                }
                else Field(field);
            }
        }
        private static void Parameter(string name,string label,string units,float min,float max)
        {
            var p=Property(name);var value=p.FindPropertyRelative("value");
            using(new EditorGUILayout.HorizontalScope())
            {
                p.isExpanded=EditorGUILayout.Foldout(p.isExpanded,new GUIContent(label,units),true);
                if(float.IsNaN(min))value.floatValue=EditorGUILayout.FloatField(value.floatValue,GUILayout.Width(75));
                else value.floatValue=EditorGUILayout.Slider(value.floatValue,min,max);
                if(GUILayout.Button(new GUIContent("↺","Reset this value and its curve"),GUILayout.Width(25)))
                {
                    serialized.ApplyModifiedProperties();typeof(EyebrowSettings).GetField(name).SetValue(state.settings,
                        typeof(EyebrowSettings).GetField(name).GetValue(new EyebrowSettings()));serialized.Update();GUI.changed=true;
                }
            }
            if(!p.isExpanded)return;
            using(new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField(units,EditorStyles.miniLabel);
                var mode=p.FindPropertyRelative("mode");EditorGUILayout.PropertyField(mode,new GUIContent("Modulation"));
                var selected=(BrowCurveMode)mode.enumValueIndex;
                if(selected==BrowCurveMode.Constant)return;
                bool angle=p.FindPropertyRelative("additive").boolValue;
                EditorGUILayout.LabelField(angle?"Value + curve offset (degrees)":"Value × curve factor",EditorStyles.miniLabel);
                if(selected==BrowCurveMode.Curve)EditorGUILayout.PropertyField(p.FindPropertyRelative("curve"),new GUIContent("Value curve"));
                else
                {
                    EditorGUILayout.PropertyField(p.FindPropertyRelative("start"),new GUIContent("Start / outside"));
                    EditorGUILayout.PropertyField(p.FindPropertyRelative("end"),new GUIContent("End"));
                    if(selected==BrowCurveMode.StartMiddleEnd||selected==BrowCurveMode.Region)
                        EditorGUILayout.PropertyField(p.FindPropertyRelative("middle"),new GUIContent("Middle / inside"));
                    if(selected==BrowCurveMode.StartMiddleEnd)EditorGUILayout.Slider(p.FindPropertyRelative("midpoint"),.001f,.999f,new GUIContent("Midpoint"));
                    if(selected==BrowCurveMode.Region||selected==BrowCurveMode.FromEnd)EditorGUILayout.Slider(p.FindPropertyRelative("regionStart"),0,1,new GUIContent("Influence start"));
                    if(selected==BrowCurveMode.Region||selected==BrowCurveMode.FromStart)EditorGUILayout.Slider(p.FindPropertyRelative("regionEnd"),0,1,new GUIContent("Influence end"));
                    if(selected==BrowCurveMode.Region)foreach(var field in new[]{"entrance","exit","entranceCurve","exitCurve"})EditorGUILayout.PropertyField(p.FindPropertyRelative(field));
                }
            }
        }
    }
}
