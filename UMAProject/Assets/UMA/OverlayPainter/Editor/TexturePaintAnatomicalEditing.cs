using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private bool anatomicalPlacementExpanded;
        private TexturePaintAnatomicalRegion newAnatomicalRegion;
        private TexturePaintRegionProfile anatomicalProfile;
        private bool separateAnatomicalGroup;
        private readonly HashSet<string> expandedAnatomicalBones = new HashSet<string>();
        private bool showAnatomicalEnvelopes = true;
        private double nextAnatomicalProfileCheck;
        private readonly Dictionary<TexturePaintRegionProfile, string> observedAnatomicalProfiles = new Dictionary<TexturePaintRegionProfile, string>();

        private void DrawAnatomicalPlacement(TextureSet set, TexturePaintLayer layer)
        {
            anatomicalPlacementExpanded = EditorGUILayout.Foldout(anatomicalPlacementExpanded, "Region Placement", true);
            if (!anatomicalPlacementExpanded) return;
            showAnatomicalEnvelopes = EditorGUILayout.Toggle("Show Envelopes in Scene", showAnatomicalEnvelopes);
            EditorGUILayout.LabelField("Bone envelopes restrict every channel. Regions in a group are added together, preserving the existing painted mask. Use knees for wear, armpits for sweat, or cuffs for dirt.", EditorStyles.wordWrappedMiniLabel);
            newAnatomicalRegion = (TexturePaintAnatomicalRegion)EditorGUILayout.EnumPopup("Add Region", newAnatomicalRegion);
            var existingGroup = layer.layerMask?.effects?.stack?.FindLast(entry => entry.enabled && entry.kind == TexturePaintMaskEffectKind.AnatomicalRegion);
            if (existingGroup != null)
                separateAnatomicalGroup = EditorGUILayout.Toggle(new GUIContent("Separate Mask Group", "Starts another restriction: its coverage must overlap the previous mask. Leave off to include another region in the last enabled group."), separateAnatomicalGroup);
            if (existingGroup == null || separateAnatomicalGroup)
                anatomicalProfile = (TexturePaintRegionProfile)EditorGUILayout.ObjectField("Shared Profile", anatomicalProfile, typeof(TexturePaintRegionProfile), false);
            else EditorGUILayout.LabelField("Adds to the last enabled region group, using that group's shared profile.", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Add Region Mask"))
            {
                if (layer.layerMask == null && !AddLayerMaskWithHistory(set, layer, 1)) return;
                var effects = layer.layerMask.effects.Clone();
                var group = effects.stack.FindLast(entry => entry.enabled && entry.kind == TexturePaintMaskEffectKind.AnatomicalRegion);
                if (group != null && !separateAnatomicalGroup)
                {
                    group.regionUnion ??= new List<TexturePaintAnatomicalSettings>();
                    group.regionUnion.Add(new TexturePaintAnatomicalSettings { region = newAnatomicalRegion });
                }
                else
                {
                    var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.AnatomicalRegion);
                    effect.name = "Region Group";
                    effect.regionSettings.region = newAnatomicalRegion; effect.regionProfile = anatomicalProfile;
                    effects.stack.Add(effect);
                }
                ChangeLayerMaskEffects(set, layer, effects);
            }
            if (layer.layerMask?.effects?.stack == null) return;
            var copy = layer.layerMask.effects.Clone();
            int remove = -1;
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < copy.stack.Count; i++)
            {
                var effect = copy.stack[i];
                if (effect.kind != TexturePaintMaskEffectKind.AnatomicalRegion) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        effect.enabled = EditorGUILayout.Toggle(effect.enabled, GUILayout.Width(18));
                        bool expanded = !collapsedMaskEffects.Contains(effect.id);
                        expanded = EditorGUILayout.Foldout(expanded, ObjectNames.NicifyVariableName(effect.regionSettings.region.ToString()) +
                            (effect.regionUnion?.Count > 0 ? " + " + effect.regionUnion.Count + " regions" : ""), true);
                        if (expanded) collapsedMaskEffects.Remove(effect.id); else collapsedMaskEffects.Add(effect.id);
                        if (GUILayout.Button("Remove", GUILayout.Width(64))) remove = i;
                    }
                    if (collapsedMaskEffects.Contains(effect.id)) continue;
                    effect.blend = (TexturePaintMaskBlend)EditorGUILayout.EnumPopup("Mask Blend", effect.blend);
                    effect.opacity = EditorGUILayout.Slider("Opacity", effect.opacity, 0, 1);
                    DrawAnatomicalParameters(set, effect);
                }
            }
            bool changed = EditorGUI.EndChangeCheck();
            if (remove >= 0) { copy.stack.RemoveAt(remove); changed = true; }
            if (changed && JsonUtility.ToJson(copy) != JsonUtility.ToJson(layer.layerMask.effects)) ChangeLayerMaskEffects(set, layer, copy);
        }

        private void DrawAnatomicalParameters(TextureSet set, TexturePaintMaskEffect effect)
        {
            effect.regionSettings ??= new TexturePaintAnatomicalSettings();
            effect.regionUnion ??= new List<TexturePaintAnatomicalSettings>();
            var profile = (TexturePaintRegionProfile)EditorGUILayout.ObjectField("Shared Profile", effect.regionProfile, typeof(TexturePaintRegionProfile), false);
            if (effect.regionProfile != null && profile == null) MakeAnatomicalRegionsLocal(effect);
            effect.regionProfile = profile;
            if (effect.regionProfile != null)
            {
                if (GUILayout.Button("Edit Shared Profile")) AssetDatabase.OpenAsset(effect.regionProfile);
                if (GUILayout.Button("Make Local Copy"))
                {
                    MakeAnatomicalRegionsLocal(effect);
                }
            }
            DrawAnatomicalRegionEntry(set, effect, effect.regionSettings, effect.id + ":0");
            if (effect.regionUnion.Count > 0 && GUILayout.Button("Remove First Region"))
            { effect.regionSettings = effect.regionUnion[0]; effect.regionUnion.RemoveAt(0); GUI.changed = true; }
            int remove = -1;
            for (int i = 0; i < effect.regionUnion.Count; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button("Remove Additional Region")) remove = i;
                    DrawAnatomicalRegionEntry(set, effect, effect.regionUnion[i], effect.id + ":" + (i + 1));
                }
            }
            if (remove >= 0) { effect.regionUnion.RemoveAt(remove); GUI.changed = true; }
            if (GUILayout.Button("Add Region to This Group"))
            { effect.regionUnion.Add(new TexturePaintAnatomicalSettings { region = newAnatomicalRegion }); GUI.changed = true; }
            if (effect.regionProfile == null)
            {
                var kinds = new HashSet<TexturePaintAnatomicalRegion> { effect.regionSettings.region };
                bool duplicate = false;
                foreach (var entry in effect.regionUnion) if (!kinds.Add(entry.region)) duplicate = true;
                if (duplicate) EditorGUILayout.HelpBox("A shared profile stores one envelope per named region. Keep this group local when using multiple versions of the same region, or save it as a mask recipe.", MessageType.Info);
                using (new EditorGUI.DisabledScope(duplicate))
                if (GUILayout.Button("Save Shared Region Profile..."))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save Region Profile", "Region Profile", "asset", "Reuse envelopes and bone names across layers and characters.");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var saved = CreateInstance<TexturePaintRegionProfile>();
                        foreach (var entry in effect.ResolveRegions())
                        {
                            saved.regions.RemoveAll(existing => existing.region == entry.region);
                            saved.regions.Add(entry.Clone());
                        }
                        AssetDatabase.CreateAsset(saved, AssetDatabase.GenerateUniqueAssetPath(path));
                        effect.regionProfile = saved;
                        GUI.changed = true;
                    }
                }
            }
            EditorGUILayout.LabelField("Regions within this group use a union. Mask Blend combines the whole group with earlier mask effects. Size X/Z controls width/depth; Y runs along the bone. Mirrored UVs cannot separate physical sides.", EditorStyles.wordWrappedMiniLabel);
        }

        private static void MakeAnatomicalRegionsLocal(TexturePaintMaskEffect effect)
        {
            var resolved = effect.ResolveRegions();
            effect.regionSettings = resolved[0]; resolved.RemoveAt(0); effect.regionUnion = resolved;
            effect.regionProfile = null; GUI.changed = true;
        }

        private void DrawAnatomicalRegionEntry(TextureSet set, TexturePaintMaskEffect effect, TexturePaintAnatomicalSettings s, string key)
        {
            s.region = (TexturePaintAnatomicalRegion)EditorGUILayout.EnumPopup("Region", s.region);
            bool paired = TexturePaintAnatomicalSettings.IsPaired(s.region);
            if (paired) s.side = (TexturePaintRegionSide)EditorGUILayout.EnumPopup("Side", s.side);
            var profileEntry = effect.regionProfile?.Get(s.region);
            if (profileEntry != null)
                EditorGUILayout.LabelField("Envelope dimensions and bone names come from the shared profile.", EditorStyles.wordWrappedMiniLabel);
            else
            {
                if (effect.regionProfile != null) EditorGUILayout.HelpBox("This region is missing from the shared profile. Local settings below are used instead.", MessageType.Warning);
                s.size = EditorGUILayout.Vector3Field("Envelope Size", s.size);
                s.offset = EditorGUILayout.Vector3Field("Envelope Offset", s.offset);
                s.rotation = EditorGUILayout.Slider("Around Bone", s.rotation, -180, 180);
                s.feather = EditorGUILayout.Slider("Edge Feather", s.feather, .001f, 1);
                if (s.region == TexturePaintAnatomicalRegion.Knees || s.region == TexturePaintAnatomicalRegion.Elbows)
                    s.jointArc = EditorGUILayout.Slider("Joint Coverage (degrees)", s.jointArc, 1, 360);
                bool bones = EditorGUILayout.Foldout(expandedAnatomicalBones.Contains(key), "Advanced: Bone Overrides", true);
                if (bones) expandedAnatomicalBones.Add(key); else expandedAnatomicalBones.Remove(key);
                if (bones)
                {
                    s.leftAnchor = EditorGUILayout.TextField("Left / Center Anchor", s.leftAnchor);
                    s.leftParent = EditorGUILayout.TextField("Left / Center Parent", s.leftParent);
                    s.leftChild = EditorGUILayout.TextField("Left / Center Child", s.leftChild);
                    if (paired)
                    {
                        s.rightAnchor = EditorGUILayout.TextField("Right Anchor", s.rightAnchor);
                        s.rightParent = EditorGUILayout.TextField("Right Parent", s.rightParent);
                        s.rightChild = EditorGUILayout.TextField("Right Child", s.rightChild);
                    }
                }
            }
            var resolved = effect.regionProfile?.Get(s.region)?.Clone() ?? s.Clone();
            resolved.side = s.side;
            TexturePaintAnatomicalMask.Resolve(set.surface?.anatomy, resolved, out string diagnostic);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Warning);
        }

        private void UpdateAnatomicalProfiles()
        {
            if (controller?.Textures == null || IsPersistenceActive || pluginLayerCancellation != null ||
                EditorApplication.timeSinceStartup < nextAnatomicalProfileCheck) return;
            nextAnatomicalProfileCheck = EditorApplication.timeSinceStartup + .5;
            var signatures = new Dictionary<TexturePaintRegionProfile, string>();
            var changed = new HashSet<TexturePaintRegionProfile>();
            foreach (var set in controller.Textures.Sets)
                foreach (var layer in set.layers)
                    if (layer.layerMask?.effects?.stack != null)
                        foreach (var effect in layer.layerMask.effects.stack)
                        {
                            var profile = effect.regionProfile;
                            if (profile == null || signatures.ContainsKey(profile)) continue;
                            string signature = JsonUtility.ToJson(profile); signatures[profile] = signature;
                            if (observedAnatomicalProfiles.TryGetValue(profile, out string previous) && previous != signature) changed.Add(profile);
                        }
            observedAnatomicalProfiles.Clear();
            foreach (var entry in signatures) observedAnatomicalProfiles[entry.Key] = entry.Value;
            if (changed.Count == 0) return;
            foreach (var set in controller.Textures.Sets)
            {
                bool refresh = false;
                foreach (var layer in set.layers)
                    if (layer.layerMask?.effects?.stack != null)
                        foreach (var effect in layer.layerMask.effects.stack)
                            if (effect.regionProfile != null && changed.Contains(effect.regionProfile)) refresh = true;
                if (refresh) set.BindPreviewTextures();
            }
            RepaintAll(); SceneView.RepaintAll();
        }

        private void DrawAnatomicalEnvelopes()
        {
            if (!showAnatomicalEnvelopes || !anatomicalPlacementExpanded || Event.current.type != EventType.Repaint) return;
            TextureSet set = ActiveTextureSet;
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return;
            var effects = set.layers[set.activeLayerIndex].layerMask?.effects?.stack;
            if (effects == null) return;
            using (new Handles.DrawingScope(new Color(1, .75f, .15f, .85f), TexturePaintProjectionGeometry.LocalToWorld(set)))
                foreach (var effect in effects)
                {
                    if (!effect.enabled || effect.kind != TexturePaintMaskEffectKind.AnatomicalRegion) continue;
                    foreach (var settings in effect.ResolveRegions())
                    foreach (var envelope in TexturePaintAnatomicalMask.Resolve(set.surface.anatomy, settings, out _))
                    {
                        Vector3 side = Vector3.Cross(envelope.axis, envelope.outward).normalized;
                        var points = new Vector3[33];
                        for (int end = -1; end <= 1; end++)
                        {
                            for (int i = 0; i < points.Length; i++)
                            {
                                float angle = Mathf.Lerp(-envelope.arc * .5f, envelope.arc * .5f, i / 32f) * Mathf.Deg2Rad;
                                // Cross-sections for an ellipsoid, or the ends of a cylinder.
                                float y = end * (envelope.cylindrical ? 1 : .7f);
                                float radial = envelope.cylindrical ? 1 : Mathf.Sqrt(1 - y * y);
                                points[i] = envelope.center + envelope.axis * envelope.radii.y * y +
                                    radial * (side * Mathf.Sin(angle) * envelope.radii.x + envelope.outward * Mathf.Cos(angle) * envelope.radii.z);
                            }
                            Handles.DrawAAPolyLine(points);
                        }
                        Handles.DrawLine(envelope.center, envelope.center + envelope.outward * envelope.radii.z);
                        Handles.Label(envelope.center, settings.region.ToString());
                    }
                }
        }
    }
}
