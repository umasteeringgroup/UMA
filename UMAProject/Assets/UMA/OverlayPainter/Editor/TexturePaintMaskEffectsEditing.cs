using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private static readonly (TexturePaintMaskEffectKind kind, bool white, string label)[] maskEffectChoices = BuildMaskEffectChoices();
        private static readonly string[] maskEffectChoiceLabels = Array.ConvertAll(maskEffectChoices, item => item.label);
        private int newMaskEffectIndex = Array.FindIndex(maskEffectChoices, item => item.kind == TexturePaintMaskEffectKind.Noise);
        private TexturePaintSmartMask smartMaskExample;
        private TexturePaintMaskPreset smartMaskPreset;
        private bool maskStackExpanded = true;
        private readonly HashSet<string> collapsedMaskEffects = new HashSet<string>();

        private void DrawMaskStackProperties(TextureSet set, TexturePaintLayer layer)
        {
            if (layer.layerMask == null) return;
            maskStackExpanded = EditorGUILayout.Foldout(maskStackExpanded, "Mask Effects & Smart Masks", true);
            if (!maskStackExpanded) return;
            var effects = layer.layerMask.effects.Clone();
            EditorGUI.BeginChangeCheck();
            DrawComposableMaskEffects(set, layer, effects, out bool? initializeWhite);
            bool changed = EditorGUI.EndChangeCheck();
            if (initializeWhite.HasValue)
                AddPaintedMaskEffectWithHistory(set, layer, effects, initializeWhite.Value);
            else if (changed && JsonUtility.ToJson(effects) != JsonUtility.ToJson(layer.layerMask.effects))
                ChangeLayerMaskEffects(set, layer, effects);
        }
        private static (TexturePaintMaskEffectKind kind, bool white, string label)[] BuildMaskEffectChoices()
        {
            var choices = new List<(TexturePaintMaskEffectKind, bool, string)>();
            foreach (TexturePaintMaskEffectKind kind in Enum.GetValues(typeof(TexturePaintMaskEffectKind)))
                if (kind == TexturePaintMaskEffectKind.PaintedMask)
                {
                    choices.Add((kind, true, "White Painted Mask"));
                    choices.Add((kind, false, "Black Painted Mask"));
                }
                else choices.Add((kind, false, ObjectNames.NicifyVariableName(kind.ToString())));
            return choices.ToArray();
        }

        private void DrawComposableMaskEffects(TextureSet set, TexturePaintLayer layer, TexturePaintLayerMaskEffects effects, out bool? initializeWhite)
        {
            initializeWhite = null;
            effects.Normalize();
            EditorGUILayout.LabelField("Effects run from top to bottom. White reveals; black hides. Add White Painted Mask to paint exclusions, or Black Painted Mask to paint reveals.", EditorStyles.wordWrappedMiniLabel);
            effects.startFromPaint = EditorGUILayout.Toggle("Start From Painted Mask", effects.startFromPaint);
            if (!effects.startFromPaint) effects.initialValue = EditorGUILayout.Slider("Starting Value", effects.initialValue, 0, 1);
            if (effects.noise.enabled || effects.textureOverlay.enabled)
            {
                EditorGUILayout.HelpBox("Legacy noise/texture effects run first. Convert them into editable stack entries to reorder them.", MessageType.None);
                if (GUILayout.Button("Convert Legacy Effects to Stack"))
                {
                    int insert = 0;
                    if (effects.noise.enabled)
                    {
                        var n = effects.noise; var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Noise);
                        effect.seed=n.seed; effect.tiling=n.tiling; effect.offset=n.offset; effect.octaves=n.octaves;
                        effect.opacity=n.opacity; effect.blend=(TexturePaintMaskBlend)n.combine; effect.invert=n.invert;
                        // Preserve legacy balance/contrast in the noise-specific controls.
                        effect.inputMin=n.balance-.5f/n.contrast; effect.inputMax=n.balance+.5f/n.contrast;
                        effects.stack.Insert(insert++,effect); n.enabled=false;
                    }
                    if (effects.textureOverlay.enabled)
                    {
                        var o=effects.textureOverlay; var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Texture);
                        effect.texture=o.texture; effect.channel=o.sourceChannel; effect.tiling=o.tiling; effect.offset=o.offset;
                        effect.rotation=o.rotation; effect.invert=o.invert; effect.opacity=o.opacity; effect.blend=(TexturePaintMaskBlend)o.combine;
                        effects.stack.Insert(insert,effect); o.enabled=false;
                    }
                    GUI.changed=true;
                }
            }
            int remove=-1, move=-1, to=-1, duplicate=-1;
            for(int i=0;i<effects.stack.Count;i++)
            {
                var effect=effects.stack[i];
                bool expanded = !collapsedMaskEffects.Contains(effect.id);
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        effect.enabled=EditorGUILayout.Toggle(effect.enabled,GUILayout.Width(18));
                        string label = string.IsNullOrWhiteSpace(effect.name) ? ObjectNames.NicifyVariableName(effect.kind.ToString()) : effect.name;
                        if (effect.opacity <= 0f) label += " (Opacity 0 — no effect)";
                        expanded=EditorGUILayout.Foldout(expanded, label, true);
                        using(new EditorGUI.DisabledScope(i==0)) if(GUILayout.Button(new GUIContent("Up","Evaluate this effect earlier"),GUILayout.Width(30))) {move=i;to=i-1;}
                        using(new EditorGUI.DisabledScope(i==effects.stack.Count-1)) if(GUILayout.Button(new GUIContent("Down","Evaluate this effect later"),GUILayout.Width(45))) {move=i;to=i+1;}
                        if(GUILayout.Button(new GUIContent("+","Duplicate effect"),GUILayout.Width(24))) duplicate=i;
                        if(GUILayout.Button(new GUIContent("X","Remove effect"),GUILayout.Width(24))) remove=i;
                    }
                    if (expanded) collapsedMaskEffects.Remove(effect.id); else collapsedMaskEffects.Add(effect.id);
                    if(!expanded) continue;
                    effect.name=EditorGUILayout.TextField("Name",effect.name);
                    effect.blend=(TexturePaintMaskBlend)EditorGUILayout.EnumPopup("Blend",effect.blend);
                    effect.opacity=EditorGUILayout.Slider("Opacity",effect.opacity,0,1);
                    using(new EditorGUI.DisabledScope(!effect.enabled)) DrawMaskEffectParameters(set,layer,effect);
                }
            }
            if(remove>=0) {effects.stack.RemoveAt(remove);GUI.changed=true;}
            else if(move>=0) {var effect=effects.stack[move];effects.stack.RemoveAt(move);effects.stack.Insert(to,effect);GUI.changed=true;}
            else if(duplicate>=0) {var copy=effects.stack[duplicate].Clone();copy.id=Guid.NewGuid().ToString("N");effects.stack.Insert(duplicate+1,copy);GUI.changed=true;}
            using(new EditorGUILayout.HorizontalScope())
            {
                newMaskEffectIndex=EditorGUILayout.Popup("New Effect",newMaskEffectIndex,maskEffectChoiceLabels);
                if(GUILayout.Button("Add",GUILayout.Width(55)))
                {
                    var choice=maskEffectChoices[newMaskEffectIndex];
                    if(choice.kind==TexturePaintMaskEffectKind.PaintedMask) initializeWhite=choice.white;
                    else effects.stack.Add(TexturePaintMaskEffect.Create(choice.kind));
                    GUI.changed=true;
                }
            }
            if(maskEffectChoices[newMaskEffectIndex].kind==TexturePaintMaskEffectKind.PaintedMask)
                EditorGUILayout.HelpBox("Initializes the layer's painted mask to the selected color and replaces existing Painted Mask entries with one enabled Multiply effect at opacity 1, at the end of the stack. Other effects are kept. Undo restores the previous paint and settings.", MessageType.Info);
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Smart Masks",EditorStyles.boldLabel);
            smartMaskExample=(TexturePaintSmartMask)EditorGUILayout.EnumPopup("Example",smartMaskExample);
            if(GUILayout.Button("Use Example Recipe")) {CopyMaskRecipe(TexturePaintMaskPreset.CreateRecipe(smartMaskExample),effects,false);GUI.changed=true;}
            smartMaskPreset=(TexturePaintMaskPreset)EditorGUILayout.ObjectField("Recipe Asset",smartMaskPreset,typeof(TexturePaintMaskPreset),false);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(smartMaskPreset==null))
                {
                    if(GUILayout.Button("Replace Effects")) {CopyMaskRecipe(smartMaskPreset.effects,effects,false);GUI.changed=true;}
                    if(GUILayout.Button("Append Effects")) {CopyMaskRecipe(smartMaskPreset.effects,effects,true);GUI.changed=true;}
                }
                if(GUILayout.Button("Save Recipe...")) SaveMaskRecipe(effects);
            }
            EditorGUILayout.LabelField("Recipes preserve this target's painted corrections. Layer references need their source layers. Mesh dirt/AO and thickness use quick estimates unless you assign an imported map.",EditorStyles.wordWrappedMiniLabel);
        }
        private static void CopyMaskRecipe(TexturePaintLayerMaskEffects source,TexturePaintLayerMaskEffects destination,bool append)
        {
            var copy=source.Clone();copy.Normalize();
            if (append)
            {
                int index=0;
                if(copy.noise.enabled)
                {
                    var n=copy.noise;var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Noise);
                    effect.seed=n.seed;effect.tiling=n.tiling;effect.offset=n.offset;effect.octaves=n.octaves;
                    effect.opacity=n.opacity;effect.blend=(TexturePaintMaskBlend)n.combine;effect.invert=n.invert;
                    effect.inputMin=n.balance-.5f/n.contrast;effect.inputMax=n.balance+.5f/n.contrast;
                    copy.stack.Insert(index++,effect);
                }
                if(copy.textureOverlay.enabled)
                {
                    var o=copy.textureOverlay;var effect=TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Texture);
                    effect.texture=o.texture;effect.channel=o.sourceChannel;effect.tiling=o.tiling;effect.offset=o.offset;
                    effect.rotation=o.rotation;effect.invert=o.invert;effect.opacity=o.opacity;effect.blend=(TexturePaintMaskBlend)o.combine;
                    copy.stack.Insert(index,effect);
                }
            }
            if(!append)
            {destination.stack.Clear();destination.noise=copy.noise;destination.textureOverlay=copy.textureOverlay;destination.startFromPaint=copy.startFromPaint;destination.initialValue=copy.initialValue;}
            foreach(var effect in copy.stack) {effect.id=Guid.NewGuid().ToString("N");destination.stack.Add(effect);}
        }
        private void SaveMaskRecipe(TexturePaintLayerMaskEffects effects)
        {
            string path=EditorUtility.SaveFilePanelInProject("Save Smart Mask","Smart Mask","asset","Save the mask's non-destructive effect recipe.");
            if(string.IsNullOrEmpty(path)) return;
            var preset=CreateInstance<TexturePaintMaskPreset>();preset.effects=effects.Clone();
            AssetDatabase.CreateAsset(preset,AssetDatabase.GenerateUniqueAssetPath(path));
            // Embed texture inputs so moving the recipe does not lose its image dependencies.
            var textures=new System.Collections.Generic.Dictionary<Texture2D,Texture2D>();
            Texture2D Embed(Texture2D texture)
            {
                if(texture==null)return null;if(textures.TryGetValue(texture,out var existing))return existing;
                var copy=Instantiate(texture);copy.name=texture.name;copy.hideFlags=HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(copy,preset);textures.Add(texture,copy);return copy;
            }
            preset.effects.textureOverlay.texture=Embed(preset.effects.textureOverlay.texture);
            foreach(var effect in preset.effects.stack) effect.texture=Embed(effect.texture);
            EditorUtility.SetDirty(preset);AssetDatabase.SaveAssetIfDirty(preset);smartMaskPreset=preset;
            ShowWorkspaceStatus("Saved smart mask recipe with embedded texture inputs.");
        }
        private void DrawMaskEffectParameters(TextureSet set,TexturePaintLayer layer,TexturePaintMaskEffect e)
        {
            var k=e.kind;
            bool pattern=k>=TexturePaintMaskEffectKind.Noise && k<=TexturePaintMaskEffectKind.Dots;
            bool mesh=k>=TexturePaintMaskEffectKind.Curvature && k<=TexturePaintMaskEffectKind.Dust;
            bool grunge=k>=TexturePaintMaskEffectKind.EdgeWear && k<=TexturePaintMaskEffectKind.Dust;
            if(k==TexturePaintMaskEffectKind.AnatomicalRegion) DrawAnatomicalParameters(set,e);
            if(k==TexturePaintMaskEffectKind.Texture || mesh)
                e.texture=(Texture2D)EditorGUILayout.ObjectField(mesh?"Map Override (optional)":"Texture",e.texture,typeof(Texture2D),false);
            if(k==TexturePaintMaskEffectKind.LayerReference) e.reference=DrawLayerReference("Source",set,layer,e.reference,true);
            if(k==TexturePaintMaskEffectKind.Texture || k==TexturePaintMaskEffectKind.MeshID)
                e.channel=(TexturePaintLayerMaskTextureChannel)EditorGUILayout.EnumPopup("Read Channel",e.channel);
            if(k==TexturePaintMaskEffectKind.PaintedMask) EditorGUILayout.LabelField("Uses the original editable mask, independent of generators. Paint on the layer's mask thumbnail to change it.",EditorStyles.wordWrappedMiniLabel);
            if(k==TexturePaintMaskEffectKind.Voronoi)
            {
                e.inputMin=EditorGUILayout.Slider(new GUIContent("Black Expansion", "Expand the black centers within existing cells without moving or resizing the cell layout. Zero preserves the original gradient; one makes the result black."),e.inputMin,0,1);
                e.amount=EditorGUILayout.Slider(new GUIContent("Contrast", "Sharpen the transition between black cell centers and white regions. One preserves the original softness."),e.amount,1,8);
            }
            if(k==TexturePaintMaskEffectKind.Fill) e.value=EditorGUILayout.Slider("Value",e.value,0,1);
            if(pattern || k==TexturePaintMaskEffectKind.Warp || grunge)
            {e.seed=EditorGUILayout.IntField("Seed",e.seed);e.octaves=EditorGUILayout.IntSlider("Noise Detail",e.octaves,1,8);}
            if(pattern || k==TexturePaintMaskEffectKind.Texture || k==TexturePaintMaskEffectKind.Transform || k==TexturePaintMaskEffectKind.Warp || k==TexturePaintMaskEffectKind.LayerReference || grunge)
            {
                e.tiling=EditorGUILayout.Vector2Field("UV Scale",e.tiling);e.offset=EditorGUILayout.Vector2Field("UV Offset",e.offset);
                e.rotation=EditorGUILayout.FloatField("Rotation",e.rotation);e.repeat=EditorGUILayout.Toggle("Repeat at Borders",e.repeat);
            }
            if(k==TexturePaintMaskEffectKind.Noise || k==TexturePaintMaskEffectKind.Levels || k==TexturePaintMaskEffectKind.Remap || k==TexturePaintMaskEffectKind.Clamp || k==TexturePaintMaskEffectKind.Smoothstep || k==TexturePaintMaskEffectKind.WorldPosition)
            {e.inputMin=EditorGUILayout.FloatField("Input Minimum",e.inputMin);e.inputMax=EditorGUILayout.FloatField("Input Maximum",e.inputMax);}
            if(k==TexturePaintMaskEffectKind.Levels || k==TexturePaintMaskEffectKind.Remap)
            {e.outputMin=EditorGUILayout.Slider("Output Minimum",e.outputMin,0,1);e.outputMax=EditorGUILayout.Slider("Output Maximum",e.outputMax,0,1);}
            if(k==TexturePaintMaskEffectKind.Levels || k==TexturePaintMaskEffectKind.Gamma || k==TexturePaintMaskEffectKind.Remap)
                e.gamma=EditorGUILayout.Slider("Gamma",e.gamma,.01f,10);
            if(k==TexturePaintMaskEffectKind.Curves) e.curve=EditorGUILayout.CurveField("Curve",e.curve,Color.white,new Rect(0,0,1,1));
            if(k==TexturePaintMaskEffectKind.BrightnessContrast)
            {e.value=EditorGUILayout.Slider("Brightness",e.value,-1,1);e.amount=EditorGUILayout.Slider("Contrast",e.amount,0,4);}
            if(k==TexturePaintMaskEffectKind.Threshold || k==TexturePaintMaskEffectKind.Stripes || k==TexturePaintMaskEffectKind.Dots)
            {e.threshold=EditorGUILayout.Slider("Threshold",e.threshold,0,1);e.softness=EditorGUILayout.Slider("Softness",e.softness,0,1);}
            if(k==TexturePaintMaskEffectKind.Posterize) e.steps=EditorGUILayout.IntSlider("Steps",e.steps,2,256);
            if(k>=TexturePaintMaskEffectKind.Blur && k<=TexturePaintMaskEffectKind.Feather || k==TexturePaintMaskEffectKind.Warp)
                e.radius=EditorGUILayout.Slider("Radius (pixels)",e.radius,0,128);
            if(k==TexturePaintMaskEffectKind.DirectionalBlur) e.rotation=EditorGUILayout.Slider("Direction",e.rotation,-180,180);
            if(k==TexturePaintMaskEffectKind.Distance || k==TexturePaintMaskEffectKind.Feather) e.threshold=EditorGUILayout.Slider("Boundary Threshold",e.threshold,0,1);
            if(k==TexturePaintMaskEffectKind.Sharpen || k==TexturePaintMaskEffectKind.HighPass || k==TexturePaintMaskEffectKind.EdgeDetect || k==TexturePaintMaskEffectKind.Thickness || grunge)
                e.amount=EditorGUILayout.Slider("Strength",e.amount,0,10);
            if(k==TexturePaintMaskEffectKind.WorldNormal || k==TexturePaintMaskEffectKind.WorldPosition || k==TexturePaintMaskEffectKind.Dust)
                e.direction=EditorGUILayout.Vector3Field("World Direction",e.direction);
            if(grunge) e.threshold=EditorGUILayout.Slider("Grunge Amount",e.threshold,0,1);
            if(k==TexturePaintMaskEffectKind.MeshID) e.value=EditorGUILayout.IntField("ID Value",Mathf.RoundToInt(e.value));
            e.invert=EditorGUILayout.Toggle("Invert Result",e.invert);
        }
    }
}
