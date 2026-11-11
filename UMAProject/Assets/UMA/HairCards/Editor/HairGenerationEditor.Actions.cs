using System;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static partial class HairGenerationEditor
    {
        internal enum Preset { ShortHairPart, CurlyVolume, SweptClumps }

        /// <summary>Confirm, then replace this group's generation settings with a preset.</summary>
        internal static bool ConfirmPreset(HairCardStage stage, HairGroomNode node, Preset preset)
        {
            if (stage?.Groom == null || node?.Group == null || node.Locked) return false;
            switch (preset)
            {
                case Preset.ShortHairPart:
                    if (!EditorUtility.DisplayDialog("Apply Short Hair Part Preset?", "Replace this group's generation settings and create a dedicated short-ribbon profile? Authored guides, maps, sculpt passes, textures and assigned materials are preserved.\n\nThe default ceiling is 20,000 triangles for this group, not the whole groom. Divide the card count between groups. Existing LOD density and sample overrides are preserved; review them after applying. Undo restores settings.", "Apply Preset", "Cancel")) return false;
                    ApplyRegularPreset(stage.Groom, node.Group); break;
                case Preset.CurlyVolume:
                    if (!EditorUtility.DisplayDialog("Apply Curly Volume Preset?", "Replace generation settings with editable ringlets and create a dedicated curl ribbon profile? Guides, sculpt passes and assigned textures/materials are preserved. Existing material shine is not changed; choose Matte in Materials & UVs if desired. Undo restores settings.", "Apply Preset", "Cancel")) return false;
                    ApplyCurlyPreset(stage.Groom, node.Group); break;
                default:
                    if (!EditorUtility.DisplayDialog("Apply Swept Clumps Preset?", "Replace generation settings with the swept-clump preset and create a dedicated ribbon profile? Your guides, sculpt passes, textures and assigned materials are preserved. Undo restores the settings.", "Apply Preset", "Cancel")) return false;
                    ApplySweptPreset(stage.Groom, node.Group); break;
            }
            stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Children, node.Group.Id));
            GUIUtility.ExitGUI();
            return true;
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
                domain = HairModifierDomain.Children, amount = HairGroomCommands.DefaultModifierAmount(type), rootInfluence = type == HairModifierType.Gather ? 1f : 0.3f };
            modifier.EnsureIntegrity(); population.modifiers.Add(modifier); HairGroomCommands.Commit(groom); return modifier;
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
