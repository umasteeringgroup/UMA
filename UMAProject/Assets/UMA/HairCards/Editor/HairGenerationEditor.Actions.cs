using System;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static partial class HairGenerationEditor
    {
        internal static void DrawTreeActions(HairCardStage stage, HairGroomNode node)
        {
            if (node.Kind == HairGroomNodeKind.Source && GUILayout.Button("Import Authored Guide File…")) HairGuideImport.ImportDialog();
            HairSweptAtlasSetup.DrawTreeAction(stage, node);
            if (node.Kind == HairGroomNodeKind.Children || node.Kind == HairGroomNodeKind.Group)
                using (new EditorGUI.DisabledScope(node.Locked))
                {
                    if (GUILayout.Button(new GUIContent("Curly Volume Preset…", "Set up surface-rooted ringlets with automatic curl resolution. Preserves guides, sculpt passes, assigned textures and materials.")))
                        if (EditorUtility.DisplayDialog("Apply Curly Volume Preset?", "Replace generation settings with editable ringlets and create a dedicated curl ribbon profile? Guides, sculpt passes and assigned textures/materials are preserved. Existing material shine is not changed; choose Matte in Materials & UVs if desired. Undo restores settings.", "Apply Preset", "Cancel"))
                        { ApplyCurlyPreset(stage.Groom, node.Group); stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Children, node.Group.Id)); }
                    if (GUILayout.Button(new GUIContent("Swept Clumps Preset…", "Configure generated clumps, hairline, curved ribbons and LOD sampling. Existing guides/sculpting and assigned textures/materials are preserved.")))
                        if (EditorUtility.DisplayDialog("Apply Swept Clumps Preset?", "Replace generation settings with the swept-clump preset and create a dedicated ribbon profile? Your guides, sculpt passes, textures and assigned materials are preserved. Undo restores the settings.", "Apply Preset", "Cancel"))
                        { ApplySweptPreset(stage.Groom, node.Group); stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Children, node.Group.Id)); }
                    if (node.Group.generation.enabled && GUILayout.Button("+ Clump Guide Stage"))
                    {
                        var population = AddPopulation(stage.Groom, node.Group);
                        stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Population, population.Id));
                    }
                }
            if (node.Population != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(stage.InspectedPopulationId == node.Population.Id ? "Inspecting population" : "Inspect Population"))
                        stage.InspectPopulation(node.Population.Id);
                    if (GUILayout.Button("Final Hair")) stage.InspectPopulation(null);
                }
                EditorGUILayout.LabelField(stage.InspectedPopulationId == node.Population.Id ? "ISOLATED · colored by parent clump" : "FINAL HAIR · all downstream stages included", EditorStyles.wordWrappedMiniLabel);
                if (node.Modifier != null && GUILayout.Button(stage.InspectedMaskId == node.Modifier.Id ? "Hide Mask" : "Show Mask"))
                    stage.InspectPopulation(node.Population.Id, stage.InspectedMaskId == node.Modifier.Id ? null : node.Modifier.Id);
                if (node.CanReorder)
                    using (new EditorGUI.DisabledScope(node.Locked))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int i = Index(node), count = Count(node);
                        using (new EditorGUI.DisabledScope(i <= 0)) if (GUILayout.Button("Earlier")) Move(stage, node, -1);
                        using (new EditorGUI.DisabledScope(i >= count - 1)) if (GUILayout.Button("Later")) Move(stage, node, 1);
                        if (GUILayout.Button("Duplicate")) Duplicate(stage, node);
                        if (GUILayout.Button("Remove…")) Remove(stage, node);
                    }
            }
            else if (node.Modifier != null)
            {
                if (GUILayout.Button(stage.InspectedMaskId == node.Modifier.Id ? "Hide Mask" : "Show Mask"))
                    stage.InspectPopulation(null, stage.InspectedMaskId == node.Modifier.Id ? null : node.Modifier.Id);
            }
            if (node.Population == null && (!string.IsNullOrEmpty(stage.InspectedPopulationId) || !string.IsNullOrEmpty(stage.InspectedMaskId)))
            {
                EditorGUILayout.HelpBox("An inspection preview is active. It is not the baked output.", MessageType.Info);
                if (GUILayout.Button("Return to Final Hair")) stage.InspectPopulation(null);
            }
        }

        private static int Index(HairGroomNode node) => node.Modifier != null ? node.Population.modifiers.IndexOf(node.Modifier) : node.Group.generation.clumps.IndexOf(node.Population);
        private static int Count(HairGroomNode node) => node.Modifier != null ? node.Population.modifiers.Count : node.Group.generation.clumps.Count;
        private static bool Valid(HairCardStage stage, HairGroomNode node) => stage?.Groom != null && node?.Population != null &&
            !node.Locked && stage.Groom.Groups.Contains(node.Group) && (node.Group.generation.clumps.Contains(node.Population) || node.Group.generation.cards == node.Population) &&
            (node.Modifier == null || node.Population.modifiers.Contains(node.Modifier));
        internal static bool Move(HairCardStage stage, HairGroomNode node, int offset)
        {
            if (!Valid(stage, node) || !node.CanReorder) return false;
            int from = Index(node), to = Mathf.Clamp(from + offset, 0, Count(node) - 1);
            if (from < 0 || from == to) return false;
            Undo.RecordObject(stage.Groom, "Reorder Hair Generation");
            if (node.Modifier != null)
            { node.Population.modifiers.RemoveAt(from); node.Population.modifiers.Insert(to, node.Modifier); }
            else { node.Group.generation.clumps.RemoveAt(from); node.Group.generation.clumps.Insert(to, node.Population); }
            HairGroomCommands.Commit(stage.Groom); stage.SelectNode(node.Key); return true;
        }
        internal static bool MoveRelative(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after)
        {
            if (!Valid(stage, source) || !Valid(stage, target) || source.ParentKey != target.ParentKey || source.Kind != target.Kind || !target.CanReorder) return false;
            int from = Index(source), to = Index(target) + (after ? 1 : 0);
            if (from < to) to--;
            return Move(stage, source, to - from);
        }
        internal static bool Duplicate(HairCardStage stage, HairGroomNode node)
        {
            if (!Valid(stage, node) || !node.CanReorder) return false;
            Undo.RecordObject(stage.Groom, "Duplicate Hair Generation"); string id;
            if (node.Modifier != null)
            { var copy = node.Modifier.Duplicate(); copy.name += " Copy"; node.Population.modifiers.Insert(Index(node) + 1, copy); id = copy.Id; }
            else { var copy = node.Population.Duplicate(); copy.name += " Copy"; node.Group.generation.clumps.Insert(Index(node) + 1, copy); id = copy.Id; }
            HairGroomCommands.Commit(stage.Groom); stage.SelectNode(HairGroomNodes.Key(node.Kind, id)); return true;
        }
        internal static bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm = null)
        {
            if (!Valid(stage, node) || !node.CanReorder) return false;
            confirm ??= EditorUtility.DisplayDialog;
            if (!confirm("Remove Hair Generation Item", "Remove '" + node.Label + "'? Later stages reconnect to the previous enabled population. Undo restores it.", "Remove", "Cancel")) return false;
            if (!Valid(stage, node)) return false;
            Undo.RecordObject(stage.Groom, "Remove Hair Generation Item");
            if (node.Modifier != null) node.Population.modifiers.Remove(node.Modifier); else node.Group.generation.clumps.Remove(node.Population);
            stage.InspectPopulation(null); HairGroomCommands.Commit(stage.Groom); stage.SelectNode(node.ParentKey); return true;
        }
        internal static HairGenerationStage AddPopulation(HairGroomAsset groom, HairGroup group)
        {
            if (groom == null || group == null || group.locked || !groom.Groups.Contains(group)) return null;
            Undo.RecordObject(groom, "Add Clump Guide Stage");
            var population = new HairGenerationStage { name = "Clump Guides " + (group.generation.clumps.Count + 1) };
            population.EnsureIntegrity(); group.generation.clumps.Add(population); group.generation.enabled = true;
            HairGroomCommands.Commit(groom); return population;
        }
        internal static HairModifierSettings AddModifier(HairGroomAsset groom, HairGroup group, HairGenerationStage population, HairModifierType type)
        {
            if (groom == null || group == null || group.locked || !groom.Groups.Contains(group) ||
                (population != group.generation.cards && !group.generation.clumps.Contains(population))) return null;
            Undo.RecordObject(groom, "Add Population Modifier");
            var modifier = new HairModifierSettings { name = ObjectNames.NicifyVariableName(type.ToString()), type = type,
                domain = HairModifierDomain.Children, amount = HairGroomCommands.DefaultModifierAmount(type), rootInfluence = 0.3f };
            modifier.EnsureIntegrity(); population.modifiers.Add(modifier); HairGroomCommands.Commit(groom); return modifier;
        }
        internal static void ShowModifierMenu(HairCardStage stage)
        {
            var node = stage.SelectedNode; if (!Valid(stage, node)) return;
            var menu = new GenericMenu();
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                if (type == HairModifierType.LodReduction) continue;
                var captured = type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.ToString())), false, () =>
                {
                    var modifier = AddModifier(stage.Groom, node.Group, node.Population, captured);
                    if (modifier != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id));
                });
            }
            menu.ShowAsContext();
        }

        internal static void ApplySweptPreset(HairGroomAsset groom, HairGroup group, string resourceFolder = null)
        {
            if (groom == null || group == null || group.locked || !groom.Groups.Contains(group)) return;
            Undo.RecordObject(groom, "Apply Swept Clumps Preset");
            group.generation = new HairGenerationPipeline { enabled = true };
            var clumps = new HairGenerationStage { name = "Primary Clumps", count = 430, minimumSpacing = 0.003f, clump = 0.3f, neighbors = 6, seed = 31, shapeSamples = 9 };
            clumps.modifiers.Add(new HairModifierSettings { name = "Soft clump bend", type = HairModifierType.Noise,
                domain = HairModifierDomain.Children, amount = 0.0015f, noiseFrequency = 20f, rootInfluence = 0.2f, seed = 31 });
            group.generation.clumps.Add(clumps);
            var cards = group.generation.cards;
            cards.name = "Fine Hair Cards"; cards.count = 4500; cards.minimumSpacing = 0.001f; cards.clump = 0.8f;
            cards.widthVariation = 0.2f; cards.tiltVariation = 8f; cards.parentCoherence = 0.7f;
            cards.hairline.enabled = true; cards.seed = 3;
            cards.shapeSamples = 9; cards.clumpSpread = .12f; cards.splitTips = .15f;
            cards.modifiers.Add(new HairModifierSettings { name = "Fine strand variation", type = HairModifierType.Noise,
                domain = HairModifierDomain.Children, amount = 0.0008f, noiseFrequency = 45f, noiseParentCoherence = 0.25f, rootInfluence = 0.2f, seed = 3 });
            group.generation.scalp.enabled = true;
            group.generation.EnsureIntegrity();
            var profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.name = groom.name + " Swept Ribbon";
            profile.Configure(HairCardShape.Ribbon, 0.0101f, 0.0005f, 13, generateBackfaces: false);
            profile.ConfigureRibbon(2, 0.2f, true, 0.025f, 25f);
            SaveResourceNear(groom, profile, resourceFolder); group.profile = profile;
            if (group.atlas == null)
            {
                group.atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
                group.atlas.name = groom.name + " Swept Atlas"; group.atlas.EnsureIntegrity();
                HairSweptAtlasSetup.ConfigureNew(group.atlas);
                SaveResourceNear(groom, group.atlas, resourceFolder);
            }
            if (group.atlas.material == null) HairSweptShaderGUI.CreateMaterial(groom, group.atlas, resourceFolder);
            group.rootEmbedDepth = 0.001f;
            if (groom.Lods.Count > 0) groom.Lods[0].useProfileSamples = true;
            HairGroomCommands.Commit(groom);
        }
        internal static void ApplyCurlyPreset(HairGroomAsset groom, HairGroup group, string resourceFolder = null)
        {
            if (groom == null || group == null || group.locked || !groom.Groups.Contains(group)) return;
            Undo.RecordObject(groom, "Apply Curly Volume Preset");
            group.generation = new HairGenerationPipeline { enabled = true };
            var cards = group.generation.cards;
            cards.name = "Curly Hair Cards"; cards.count = 1300; cards.minimumSpacing = .0045f;
            cards.neighbors = 3; cards.clump = .15f; cards.clumpSpread = .65f;
            cards.shapeSamples = 16; cards.lengthVariation = .16f; cards.widthVariation = .2f;
            cards.tiltVariation = 12f; cards.parentCoherence = .35f; cards.flyawayFraction = .025f;
            cards.hairline.enabled = true; cards.hairline.distance = .016f;
            cards.hairline.edgeLength = .65f; cards.hairline.edgeWidth = .5f;
            cards.seed = 41;
            cards.minimumNormalDot = -.1f; cards.influenceRadius = .25f;
            cards.modifiers.Add(new HairModifierSettings { name = "Volume envelope", type = HairModifierType.Length,
                domain = HairModifierDomain.Children, amount = 1.2f });
            cards.modifiers.Add(new HairModifierSettings { name = "Loose centerline variation", type = HairModifierType.Noise,
                domain = HairModifierDomain.Children, amount = .012f, noiseFrequency = 18f, noiseParentCoherence = .3f, rootInfluence = .2f, seed = 14 });
            cards.modifiers.Add(new HairModifierSettings { name = "Ringlets — shape & variation", type = HairModifierType.Ringlets,
                domain = HairModifierDomain.Children, amount = .0112f, rootInfluence = .6f, seed = 4,
                ringlets = new HairRingletSettings { spacingMode = HairRingletSpacingMode.DistanceBetweenTurns, tipRadius = 1f, turnVariation = .3f, radiusVariation = .25f, clumpCoherence = .15f } });
            group.generation.scalp.enabled = true;
            group.generation.EnsureIntegrity();
            var profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.name = groom.name + " Curl Ribbon";
            profile.Configure(HairCardShape.Ribbon, .0064f, .0035f, 96, generateBackfaces: false);
            profile.ConfigureRibbon(1, 0f, true, .009f, 18f);
            SaveResourceNear(groom, profile, resourceFolder); group.profile = profile;
            if (group.atlas == null)
            {
                group.atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
                group.atlas.name = groom.name + " Curl Atlas"; group.atlas.EnsureIntegrity();
                HairSweptAtlasSetup.ConfigureNew(group.atlas, true); SaveResourceNear(groom, group.atlas, resourceFolder);
            }
            if (group.atlas.material == null)
            {
                var material = HairSweptShaderGUI.CreateMaterial(groom, group.atlas, resourceFolder);
                if (material != null) HairSweptShaderGUI.ApplyFinish(material, HairSweptShaderGUI.Finish.Matte);
            }
            group.rootEmbedDepth = .001f;
            if (groom.Lods.Count > 0) groom.Lods[0].useProfileSamples = true;
            HairGroomCommands.Commit(groom);
        }

        internal static void SaveResourceNear(HairGroomAsset groom, UnityEngine.Object resource, string resourceFolder = null)
        {
            string path = AssetDatabase.GetAssetPath(groom);
            if (string.IsNullOrEmpty(path)) return;
            string folder = resourceFolder ?? System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = resource.name;
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            AssetDatabase.CreateAsset(resource, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + (resource is Material ? ".mat" : ".asset")));
            Undo.RegisterCreatedObjectUndo(resource, "Create Hair Resource");
        }
    }
}
