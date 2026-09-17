using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static partial class HairGenerationEditor
    {
        // An additive preset: no schema, evaluator or existing preset changes. Separate
        // groups (or Part Regions) preserve an authored part through both populations.
        internal static void ApplyRegularPreset(HairGroomAsset groom, HairGroup group, string resourceFolder = null)
        {
            if (groom == null || group == null || group.locked || !groom.Groups.Contains(group)) return;
            Undo.RecordObject(groom, "Apply Short Hair Part Preset");
            group.generation = new HairGenerationPipeline { enabled = true };
            var clumps = new HairGenerationStage
            {
                name = "Soft direction clumps", count = 380, minimumSpacing = .002f,
                uniformity = .65f, neighbors = 4, clump = .18f, clumpSpread = .5f,
                lengthVariation = .04f, widthVariation = .08f, tiltVariation = 4f,
                shapeSamples = 12, flyawayFraction = 0f, seed = 61
            };
            clumps.modifiers.Add(new HairModifierSettings
            {
                name = "Subtle clump variation", type = HairModifierType.Noise,
                domain = HairModifierDomain.Children, amount = .001f,
                noiseFrequency = 16f, rootInfluence = .2f, seed = 61
            });
            group.generation.clumps.Add(clumps);
            var cards = group.generation.cards;
            cards.name = "Short hair part cards"; cards.count = 2500;
            cards.minimumSpacing = .0008f; cards.uniformity = .7f;
            cards.neighbors = 3; cards.clump = .55f; cards.clumpSpread = .4f;
            cards.splitTips = .18f; cards.shapeSamples = 12;
            cards.lengthVariation = .08f; cards.widthVariation = .16f;
            cards.tiltVariation = 6f; cards.parentCoherence = .6f;
            cards.flyawayFraction = .012f; cards.flyawayAmplitude = .0015f; cards.seed = 73;
            cards.hairline.enabled = true; cards.hairline.distance = .009f;
            cards.hairline.edgeLength = .8f; cards.hairline.edgeWidth = .65f;
            cards.hairline.edgeDensity = .85f; cards.hairline.inwardLean = .05f;
            cards.modifiers.Add(new HairModifierSettings
            {
                name = "Fine texture variation", type = HairModifierType.Noise,
                domain = HairModifierDomain.Children, amount = .0005f,
                noiseFrequency = 30f, noiseParentCoherence = .3f, rootInfluence = .15f, seed = 73
            });
            group.generation.scalp.enabled = true;
            group.generation.EnsureIntegrity();
            var profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.name = groom.name + " Short Ribbon";
            // 2,500 cards * four length segments * two triangles = 20,000 maximum
            // for this group with the default profile. A shader draws both sides.
            profile.Configure(HairCardShape.Ribbon, .008f, .003f, 5, generateBackfaces: false);
            profile.ConfigureRibbon(1, 0f, false);
            SaveResourceNear(groom, profile, resourceFolder); group.profile = profile;
            if (group.atlas == null)
            {
                group.atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
                group.atlas.name = groom.name + " Short Atlas"; group.atlas.EnsureIntegrity();
                HairSweptAtlasSetup.ConfigureNew(group.atlas);
                group.atlas.regions.Clear(); HairSweptAtlasSetup.AddRegularStrips(group.atlas);
                SaveResourceNear(groom, group.atlas, resourceFolder);
            }
            if (group.atlas.material == null)
            {
                var material = HairSweptShaderGUI.CreateMaterial(groom, group.atlas, resourceFolder);
                if (material != null) HairSweptShaderGUI.ApplyFinish(material, HairSweptShaderGUI.Finish.Matte);
            }
            group.rootEmbedDepth = 0f;
            group.preventCardPenetration = true; group.cardSurfaceClearance = .0005f;
            // LOD sampling is groom-wide. Do not silently alter other groups when
            // applying a preset to this one; the panel reports the active LOD ceiling.
            HairGroomCommands.Commit(groom);
        }
    }
}
