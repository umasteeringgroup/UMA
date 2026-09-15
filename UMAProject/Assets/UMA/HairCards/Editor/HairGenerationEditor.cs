using System;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static partial class HairGenerationEditor
    {
        internal static SerializedProperty GroupProperty(SerializedObject serialized, HairCardStage stage, HairGroup group)
            => serialized.FindProperty("groups").GetArrayElementAtIndex(stage.Groom.Groups.IndexOf(group));

        private static SerializedProperty PopulationProperty(SerializedProperty group, HairGroup owner, HairGenerationStage population)
        {
            var pipeline = group.FindPropertyRelative("generation");
            return population == owner.generation.cards ? pipeline.FindPropertyRelative("cards") :
                pipeline.FindPropertyRelative("clumps").GetArrayElementAtIndex(owner.generation.clumps.IndexOf(population));
        }
        private static void Field(SerializedProperty owner, string field, string label, string tip = null)
            => EditorGUILayout.PropertyField(owner.FindPropertyRelative(field), new GUIContent(label, tip), true);
        private static void Apply(SerializedObject serialized, HairCardStage stage)
        {
            if (serialized.ApplyModifiedProperties()) HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Evaluation);
        }

        internal static void DrawOverview(HairCardStage stage, HairGroup group)
        {
            using var serialized = new SerializedObject(stage.Groom);
            var generation = GroupProperty(serialized, stage, group).FindPropertyRelative("generation");
            Field(generation, "enabled", "Surface generation", "Generate independently anchored roots over Growth / Density. Disable to use the existing children-per-guide workflow; neither set of settings is lost.");
            Apply(serialized, stage);
            if (!group.generation.enabled)
            {
                EditorGUILayout.HelpBox("Using children-per-guide. For the swept-clump hairstyle, choose Swept Clumps Preset in the tree. It preserves your authored guides and sculpt passes.", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox("Shape a few guides, generate clump guides, then fill them with cards. Select a population in the tree to tune it. Use Inspect in the tree to see its output; Final Hair returns to the finished result.", MessageType.Info);
            EditorGUILayout.LabelField("Generation order", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{group.guides.Count:N0} authored guides", EditorStyles.wordWrappedLabel);
            foreach (var population in group.generation.clumps) if (population != null) DrawCount(stage, population);
            DrawCount(stage, group.generation.cards);
            EditorGUILayout.HelpBox("More guides add control, not necessarily more hair. Card density is independent of how many control guides you authored.", MessageType.None);
        }

        private static HairPopulationInfo Info(HairCardStage stage, HairGenerationStage population)
            => stage.Evaluation?.populations.Find(p => p.stageId == population.Id);
        private static void DrawCount(HairCardStage stage, HairGenerationStage population)
        {
            var info = Info(stage, population);
            string result = !population.enabled ? "bypassed" : info == null ? "not evaluated" : $"{info.outputCount:N0} curves";
            EditorGUILayout.LabelField("   ↓ " + population.name, result);
        }

        internal static void DrawPopulation(HairCardStage stage, HairGroomNode node)
        {
            var population = node.Population; var info = Info(stage, population);
            if (info != null)
            {
                EditorGUILayout.HelpBox($"{info.inputCount:N0} input guides → {info.outputCount:N0} {(population == node.Group.generation.cards ? "cards" : "clump guides")}\n" +
                    (info.rootsReused ? "Scalp roots cached" : "Scalp roots regenerated") + (info.neighborsReused ? " · neighbor weights cached" : "") + $" · {info.milliseconds:F1} ms for this stage", MessageType.None);
                if (info.rejectedInfluences > 0)
                    EditorGUILayout.HelpBox($"{info.rejectedInfluences:N0} {(info.rejectedInfluences == 1 ? "root cannot" : "roots cannot")} find a compatible guide. Add guides in those areas, increase Influence Radius, or review Part Regions under Root placement.", MessageType.Warning);
                if (info.surfaceRoots < population.count)
                    EditorGUILayout.LabelField("Painted density and minimum spacing can reduce the requested count.", EditorStyles.wordWrappedMiniLabel);
            }
            using var serialized = new SerializedObject(stage.Groom);
            var property = PopulationProperty(GroupProperty(serialized, stage, node.Group), node.Group, population);
            Field(property, "enabled", "Active"); Field(property, "name", "Name");
            var count = property.FindPropertyRelative("count");
            count.intValue = Mathf.Clamp(EditorGUILayout.DelayedIntField(new GUIContent("Count at full density",
                "Independent of authored-guide count. Press Enter to regenerate; a softer Growth / Density map reduces the population."), count.intValue), 1, 20000);
            Field(property, "clump", "Follow parent clump", "0 blends neighboring flow at the new scalp root. 1 pulls toward the closest compatible parent's curved centerline. Roots and segment lengths stay fixed.");
            Field(property, "clumpAlongStrand", "Root → tip clumping");
            Field(property, "clumpSpread", "Clump separation", "Retain a fraction of the roots' separation at the curved clump centerline. Avoids collapsing every strand onto one centerline.");
            Field(property, "splitTips", "Split tips", "Relax clumping through the last 30% of the strand, keeping the incoming blended flow and original segment lengths.");
            Field(property, "rootInfluence", "Root bending", "0 protects bending at the base; 1 allows full response. Root position is always anchored.");
            var rootFoldout = property.FindPropertyRelative("minimumSpacing");
            rootFoldout.isExpanded = EditorGUILayout.Foldout(rootFoldout.isExpanded, "Root placement & guide influence", true);
            if (rootFoldout.isExpanded)
            {
                Field(property, "minimumSpacing", "Minimum spacing (m)"); Field(property, "uniformity", "Uniformity");
                Field(property, "neighbors", "Blended neighbors"); Field(property, "influenceRadius", "Influence radius (m)");
                Field(property, "surfaceConform", "Follow scalp curvature", "Transport neighboring guide shapes into each new root's surface frame. Reduces strands sinking into curved areas between guides.");
                Field(property, "minimumNormalDot", "Surface compatibility", "Minimum dot product of root normals. Higher values prevent front/back blending, but may need more authored guides.");
                MapPopup(property.FindPropertyRelative("partMapId"), node.Group, "Part regions", "Optional map: paint different sides near 0 and 1 to prevent interpolation across the part.");
            }
            var variation = property.FindPropertyRelative("lengthVariation");
            variation.isExpanded = EditorGUILayout.Foldout(variation.isExpanded, "Natural variation & flyaways", true);
            if (variation.isExpanded)
            {
                Field(property, "lengthVariation", "Length variation"); Field(property, "widthVariation", "Width variation");
                Field(property, "tiltVariation", "Tilt variation (degrees)"); Field(property, "parentCoherence", "Keep clumps related");
                Field(property, "flyawayFraction", "Flyaway fraction"); Field(property, "flyawayAmplitude", "Flyaway amplitude (m)"); Field(property, "seed", "Seed");
            }
            var hairline = property.FindPropertyRelative("hairline");
            Field(hairline, "enabled", "Refine hairline");
            if (hairline.FindPropertyRelative("enabled").boolValue)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Field(hairline, "distance", "Transition distance (m)"); Field(hairline, "edgeLength", "Edge length multiplier");
                    Field(hairline, "edgeWidth", "Edge width multiplier"); Field(hairline, "edgeDensity", "Edge density multiplier");
                    Field(hairline, "alignFacing", "Outward facing"); Field(hairline, "inwardLean", "Lean into growth area");
                }
                EditorGUILayout.LabelField("Uses the painted growth boundary, including on a closed head mesh.", EditorStyles.wordWrappedMiniLabel);
            }
            var advanced = property.FindPropertyRelative("shapeSamples");
            advanced.isExpanded = EditorGUILayout.Foldout(advanced.isExpanded, "Advanced shape resolution", true);
            if (advanced.isExpanded)
            {
                Field(property, "shapeSamples", "Shape control points");
                EditorGUILayout.LabelField("Independent of card tessellation and preview quality. Usually leave at 16; changing this can alter fine deformation.", EditorStyles.wordWrappedMiniLabel);
            }
            Apply(serialized, stage);
        }

        private static void MapPopup(SerializedProperty property, HairGroup group, string label, string tooltip = null)
        {
            var labels = new System.Collections.Generic.List<string> { "None" };
            var ids = new System.Collections.Generic.List<string> { string.Empty };
            foreach (var map in group.maps) if (map != null) { labels.Add(map.DisplayName); ids.Add(map.Id); }
            string old = property.stringValue ?? string.Empty;
            if (!ids.Contains(old)) { labels.Add("Missing map (choose replacement)"); ids.Add(old); }
            int choice = EditorGUILayout.Popup(new GUIContent(label, tooltip), ids.IndexOf(old), labels.ToArray());
            property.stringValue = ids[choice];
        }

        internal static void DrawModifierFields(HairCardStage stage, HairModifierSettings modifier)
        {
            var node = stage.SelectedNode;
            using var serialized = new SerializedObject(stage.Groom);
            var group = GroupProperty(serialized, stage, node.Group);
            SerializedProperty list = node.Population != null ? PopulationProperty(group, node.Group, node.Population).FindPropertyRelative("modifiers") :
                group.FindPropertyRelative("sculptLayers").GetArrayElementAtIndex(node.Group.sculptLayers.IndexOf(node.Layer)).FindPropertyRelative("modifiers");
            int index = node.Population != null ? node.Population.modifiers.IndexOf(modifier) : node.Layer.modifiers.IndexOf(modifier);
            if (index < 0) return;
            var property = list.GetArrayElementAtIndex(index);
            if (modifier.type == HairModifierType.Clump)
                Field(property, "clumpRadius", "Clump size (m)", "Select local centerlines from the incoming population. Smaller values produce more, narrower clumps.");
            if (modifier.type == HairModifierType.Noise)
            {
                Field(property, "noiseFrequency", "Noise cycles / meter");
                Field(property, "noiseParentCoherence", "Shared clump noise");
                EditorGUILayout.LabelField("Continuous strand-space noise. Preview tessellation does not reseed the hairstyle.", EditorStyles.wordWrappedMiniLabel);
            }
            var mask = property.FindPropertyRelative("mask");
            mask.isExpanded = EditorGUILayout.Foldout(mask.isExpanded, "Where this modifier applies", true);
            if (mask.isExpanded)
            {
                MapPopup(property.FindPropertyRelative("maskMapId"), node.Group, "Painted mask");
                Field(mask, "useLength", "Filter by strand length");
                if (mask.FindPropertyRelative("useLength").boolValue) Field(mask, "lengthRange", "Length 0 → 1 (m)");
                Field(mask, "useHairline", "Filter by hairline distance");
                if (mask.FindPropertyRelative("useHairline").boolValue) Field(mask, "hairlineRange", "Distance 0 → 1 (m)");
                Field(mask, "randomVariation", "Random mask variation"); Field(mask, "parentCoherence", "Shared clump mask");
                Field(mask, "remap", "Remap mask"); Field(mask, "invert", "Invert result");
                EditorGUILayout.LabelField("Filters multiply together, then remap/invert. A missing assigned map is treated as zero. Use Show Mask in the tree: dark = protected, bright = affected.", EditorStyles.wordWrappedMiniLabel);
            }
            Apply(serialized, stage);
        }

        internal static void DrawRibbonProfile(HairCardStage stage, HairCardProfileAsset profile)
        {
            EditorGUI.BeginChangeCheck();
            int spans = EditorGUILayout.IntSlider("Cross-width spans", profile.RibbonSpans, 1, 4);
            float camber;
            using (new EditorGUI.DisabledScope(spans == 1)) camber = EditorGUILayout.Slider("Ribbon roundness", profile.RibbonCamber, 0f, 0.5f);
            bool adaptive = EditorGUILayout.Toggle("Adaptive card segments", profile.AdaptiveSampling);
            float length = profile.MaximumSegmentLength, angle = profile.MaximumSegmentAngle;
            if (adaptive)
            {
                length = EditorGUILayout.Slider("Maximum segment (mm)", length * 1000f, 1f, 50f) * 0.001f;
                angle = EditorGUILayout.Slider("Bend per segment (degrees)", angle, 2f, 60f);
                EditorGUILayout.LabelField("Length and curvature choose the segment budget, capped by the current LOD/profile samples. Two spans and roundness 0.2 make a gently arched ribbon.", EditorStyles.wordWrappedMiniLabel);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Edit Hair Ribbon Shape"); profile.ConfigureRibbon(spans, camber, adaptive, length, angle);
                EditorUtility.SetDirty(profile); stage.TrackResourceEdit(profile); stage.QueuePreviewChange(HairPreviewChange.Geometry);
            }
        }

        internal static void DrawScalp(HairCardStage stage, HairGroup group)
        {
            using var serialized = new SerializedObject(stage.Groom);
            var scalp = GroupProperty(serialized, stage, group).FindPropertyRelative("generation").FindPropertyRelative("scalp");
            Field(scalp, "enabled", "Enable scalp shading");
            using (new EditorGUI.DisabledScope(!scalp.FindPropertyRelative("enabled").boolValue))
            {
                Field(scalp, "color", "Scalp shadow color"); Field(scalp, "strength", "Strength"); Field(scalp, "edgeFade", "Hairline fade (m)");
                Field(scalp, "preserveAlpha", "Preserve existing alpha");
            }
            Apply(serialized, stage);
            EditorGUILayout.HelpBox("Colors the existing scalp/body mesh under painted growth. Preview uses an owned mesh copy; source assets are never recolored. Create / Update writes a slot-bound UMA Mesh Modifier. The skin shader must read vertex RGB to display it.", MessageType.Info);
            EditorGUILayout.ObjectField("Mesh Modifier", group.generation.scalp.meshModifier, typeof(UMA.MeshModifier), false);
            if (GUILayout.Button("Create / Update & Apply Scalp Modifier", GUILayout.Height(28f)))
                HairScalpShadingEditor.CreateAndApply(stage, group);
        }
    }
}
