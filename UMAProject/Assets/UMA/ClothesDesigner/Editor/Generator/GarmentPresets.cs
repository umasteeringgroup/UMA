using System;
using System.Collections.Generic;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>A named starting style for the garment generator.</summary>
    public sealed class GarmentPreset
    {
        public string Name;
        public GarmentType Type;
        public string Description;
        public Func<GarmentStyle> Make;
    }

    /// <summary>
    /// The garment generator's presets, by type: each a full style to start from (the T-shirt, jeans, a sundress...).
    /// The tops, trousers and skirts follow the matching sample patterns' fabrics, ease and bands, which were measured
    /// to drape and stay up on Human Female 3.0.
    /// </summary>
    public static class GarmentPresets
    {
        public const string Custom = "Custom";

        private static GarmentStyle Base(GarmentType type, string name, string fabric)
        {
            var s = new GarmentStyle { type = type, preset = name, fabric = fabric };
            switch (type)
            {
                case GarmentType.Trousers:
                case GarmentType.Skirt:
                    s.easeCm = 6f;
                    break;
                case GarmentType.Jumpsuit:
                    s.easeCm = 14f;
                    s.waistband = BandStyle.None;
                    break;
            }
            return s;
        }

        private static GarmentPreset P(string name, GarmentType type, string description, Func<GarmentStyle> make) =>
            new GarmentPreset { Name = name, Type = type, Description = description, Make = make };

        public static readonly IReadOnlyList<GarmentPreset> All = new List<GarmentPreset>
        {
            // ---- Tops ----
            P("T-Shirt", GarmentType.Top, "Crew-neck jersey tee with short sleeves, to the hips.", () =>
            {
                var s = Base(GarmentType.Top, "T-Shirt", "Jersey");
                s.length = 3f; s.easeCm = 10f; s.sleeve = 0.3f; s.sleeveEaseCm = 9f; s.neckWidth = 0.1f; s.neckDepthCm = 9f; s.backNeckDepthCm = 2.5f;
                return s;
            }),
            P("Long-Sleeve Tee", GarmentType.Top, "The tee with sleeves to the wrist.", () =>
            {
                var s = Base(GarmentType.Top, "Long-Sleeve Tee", "Jersey");
                s.length = 3f; s.easeCm = 10f; s.sleeve = 1f; s.sleeveEaseCm = 9f; s.neckWidth = 0.1f; s.neckDepthCm = 9f;
                return s;
            }),
            P("Tank Top", GarmentType.Top, "Sleeveless jersey tank with a scoop neck and narrow straps.", () =>
            {
                var s = Base(GarmentType.Top, "Tank Top", "Jersey");
                s.length = 3f; s.easeCm = 6f; s.sleeve = 0f; s.strapCm = 5f; s.neckline = NecklineShape.Scoop; s.neckWidth = 0.05f; s.neckDepthCm = 16f; s.backNeckDepthCm = 9f;
                return s;
            }),
            P("Crop Top", GarmentType.Top, "Fitted sleeveless spandex top ending above the waist.", () =>
            {
                var s = Base(GarmentType.Top, "Crop Top", "Spandex");
                s.length = 0f; s.easeCm = -4f; s.sleeve = 0f; s.strapCm = 6f; s.neckline = NecklineShape.Scoop; s.neckWidth = 0.1f; s.neckDepthCm = 11f; s.backNeckDepthCm = 5f;
                return s;
            }),
            P("Polo Shirt", GarmentType.Top, "Short-sleeved cotton top with a polo collar turned down over its stand.", () =>
            {
                var s = Base(GarmentType.Top, "Polo Shirt", "Cotton");
                s.length = 3.2f; s.easeCm = 12f; s.sleeve = 0.3f; s.sleeveEaseCm = 9f; s.neckWidth = 0.1f; s.neckDepthCm = 7f; s.collar = CollarStyle.Shirt; s.collarHeightCm = 5f;
                return s;
            }),
            P("Shirt", GarmentType.Top, "Long-sleeved cotton shirt with a shirt collar, cuffs and a shirt-tail hem.", () =>
            {
                var s = Base(GarmentType.Top, "Shirt", "Cotton");
                s.length = 3.8f; s.easeCm = 12f; s.sleeve = 1f; s.sleeveEaseCm = 10f; s.cuffs = BandStyle.Self; s.bandHeightCm = 6f; s.trimFabric = "Cotton";
                s.neckWidth = 0.1f; s.neckDepthCm = 8f; s.collar = CollarStyle.Shirt; s.collarHeightCm = 6f; s.hemDropCm = 5f;
                return s;
            }),
            P("Blouse", GarmentType.Top, "Light silk blouse with a V neck and elbow sleeves, shaped at the waist.", () =>
            {
                var s = Base(GarmentType.Top, "Blouse", "Silk");
                s.length = 2.6f; s.easeCm = 8f; s.waistShaping = 0.4f; s.sleeve = 0.5f; s.sleeveFlare = 0.3f; s.neckline = NecklineShape.V; s.neckWidth = 0.15f; s.neckDepthCm = 14f;
                return s;
            }),
            P("Hoodie", GarmentType.Top, "Pullover jersey hoodie: hood, long sleeves, rib cuffs and a rib hem band.", () =>
            {
                var s = Base(GarmentType.Top, "Hoodie", "Jersey");
                s.length = 3f; s.easeCm = 16f; s.sleeve = 1f; s.sleeveEaseCm = 14f; s.cuffs = BandStyle.Rib; s.hemBand = BandStyle.Rib; s.bandHeightCm = 7f;
                s.neckWidth = 0.2f; s.neckDepthCm = 10f; s.backNeckDepthCm = 3f; s.collar = CollarStyle.Hood;
                return s;
            }),
            P("Turtleneck", GarmentType.Top, "Close-fitting long-sleeved jersey top with a tall collar.", () =>
            {
                var s = Base(GarmentType.Top, "Turtleneck", "Jersey");
                s.length = 3f; s.easeCm = 6f; s.sleeve = 1f; s.sleeveEaseCm = 6f; s.neckWidth = 0.1f; s.neckDepthCm = 7f; s.backNeckDepthCm = 2.5f;
                s.collar = CollarStyle.Turtleneck; s.collarHeightCm = 9f; s.trimFabric = "Elastic";
                return s;
            }),
            P("Cardigan", GarmentType.Top, "Open-front wool cardigan with a V neck, rib cuffs and a rib hem band.", () =>
            {
                var s = Base(GarmentType.Top, "Cardigan", "Wool");
                s.length = 3f; s.easeCm = 14f; s.front = FrontStyle.Open; s.sleeve = 1f; s.sleeveEaseCm = 12f; s.cuffs = BandStyle.Rib; s.hemBand = BandStyle.Rib; s.bandHeightCm = 5f;
                s.neckline = NecklineShape.V; s.neckWidth = 0.2f; s.neckDepthCm = 24f;
                return s;
            }),
            P("Jacket", GarmentType.Top, "Open-front denim jacket with a band collar, cuffs and a hem band.", () =>
            {
                var s = Base(GarmentType.Top, "Jacket", "Denim");
                s.length = 2.8f; s.easeCm = 18f; s.front = FrontStyle.Open; s.sleeve = 1f; s.sleeveEaseCm = 14f; s.cuffs = BandStyle.Self; s.hemBand = BandStyle.Self; s.bandHeightCm = 5f;
                s.neckWidth = 0.15f; s.neckDepthCm = 7f; s.collar = CollarStyle.Band; s.collarHeightCm = 5f;
                return s;
            }),

            // ---- Dresses ----
            P("T-Shirt Dress", GarmentType.Dress, "Loose jersey dress with short sleeves, above the knee.", () =>
            {
                var s = Base(GarmentType.Dress, "T-Shirt Dress", "Jersey");
                s.length = 5f; s.easeCm = 12f; s.flare = 0.15f; s.sleeve = 0.3f; s.neckWidth = 0.1f; s.neckDepthCm = 9f;
                return s;
            }),
            P("Sheath Dress", GarmentType.Dress, "Sleeveless knee-length cotton dress fitted at the waist with darts.", () =>
            {
                var s = Base(GarmentType.Dress, "Sheath Dress", "Cotton");
                s.length = 6f; s.easeCm = 6f; s.waistShaping = 1f; s.sleeve = 0f; s.strapCm = 7f; s.neckline = NecklineShape.Scoop; s.neckWidth = 0.1f; s.neckDepthCm = 11f; s.backNeckDepthCm = 3f;
                return s;
            }),
            P("Sundress", GarmentType.Dress, "Flowing silk dress with narrow straps and a square neck, flaring to the knee.", () =>
            {
                var s = Base(GarmentType.Dress, "Sundress", "Silk");
                s.length = 6f; s.easeCm = 6f; s.waistShaping = 0.6f; s.flare = 0.35f; s.sleeve = 0f; s.strapCm = 4f;
                s.neckline = NecklineShape.Square; s.neckWidth = 0.2f; s.neckDepthCm = 14f; s.backNeckDepthCm = 10f;
                return s;
            }),
            P("Maxi Dress", GarmentType.Dress, "Sleeveless silk dress to the floor with a V neck.", () =>
            {
                var s = Base(GarmentType.Dress, "Maxi Dress", "Silk");
                s.length = 9f; s.easeCm = 6f; s.waistShaping = 0.7f; s.flare = 0.3f; s.sleeve = 0f; s.strapCm = 5f;
                s.neckline = NecklineShape.V; s.neckWidth = 0.15f; s.neckDepthCm = 18f; s.backNeckDepthCm = 8f;
                return s;
            }),
            P("Gown", GarmentType.Dress, "Floor-length silk gown, fitted to the waist and flaring wide.", () =>
            {
                var s = Base(GarmentType.Dress, "Gown", "Silk");
                s.length = 9f; s.easeCm = 4f; s.waistShaping = 1f; s.flare = 0.6f; s.sleeve = 0f; s.strapCm = 3f;
                s.neckline = NecklineShape.Scoop; s.neckWidth = 0.25f; s.neckDepthCm = 18f; s.backNeckDepthCm = 14f;
                return s;
            }),
            P("Shirt Dress", GarmentType.Dress, "Cotton shirt dress to the knee with a shirt collar and short sleeves.", () =>
            {
                var s = Base(GarmentType.Dress, "Shirt Dress", "Cotton");
                s.length = 6f; s.easeCm = 10f; s.waistShaping = 0.5f; s.flare = 0.2f; s.sleeve = 0.3f; s.neckWidth = 0.1f; s.neckDepthCm = 8f;
                s.collar = CollarStyle.Shirt; s.collarHeightCm = 6f;
                return s;
            }),

            // ---- Kimonos and robes ----
            // Cotton, as a yukata is: in silk the two limp fronts crossed through each other where they overlap
            // (Human Female 3.0); cotton and wool wrapped cleanly.
            P("Kimono", GarmentType.Kimono, "Cotton kimono wrapped left over right, with wide sleeves and a band collar, to mid-calf.", () =>
            {
                var s = Base(GarmentType.Kimono, "Kimono", "Cotton");
                s.length = 7f; s.easeCm = 24f; s.front = FrontStyle.Wrap; s.overlapCm = 12f; s.neckline = NecklineShape.V; s.neckWidth = 0.15f; s.neckDepthCm = 30f;
                s.collar = CollarStyle.Band; s.collarHeightCm = 6f; s.sleeve = 0.75f; s.sleeveEaseCm = 24f; s.sleeveFlare = 0.8f;
                return s;
            }),
            P("Robe", GarmentType.Kimono, "Soft fleece robe wrapped closed, with long wide sleeves, to the knee.", () =>
            {
                var s = Base(GarmentType.Kimono, "Robe", "Fleece");
                s.length = 6f; s.easeCm = 24f; s.flare = 0.3f; s.front = FrontStyle.Wrap; s.overlapCm = 14f; s.neckline = NecklineShape.V; s.neckWidth = 0.15f; s.neckDepthCm = 30f;
                s.collar = CollarStyle.Band; s.collarHeightCm = 8f; s.sleeve = 1f; s.sleeveEaseCm = 20f; s.sleeveFlare = 0.4f;
                return s;
            }),
            P("Haori", GarmentType.Kimono, "Short open kimono jacket with wide sleeves.", () =>
            {
                var s = Base(GarmentType.Kimono, "Haori", "Cotton");
                s.length = 4f; s.easeCm = 22f; s.front = FrontStyle.Open; s.neckline = NecklineShape.V; s.neckWidth = 0.15f; s.neckDepthCm = 25f;
                s.collar = CollarStyle.Band; s.collarHeightCm = 5f; s.sleeve = 0.6f; s.sleeveEaseCm = 22f; s.sleeveFlare = 0.7f;
                return s;
            }),

            // ---- Trousers ----
            P("Trousers", GarmentType.Trousers, "Straight wool trousers to the ankle with a waistband.", () =>
            {
                var s = Base(GarmentType.Trousers, "Trousers", "Wool");
                s.rise = 0.4f; s.legLength = 4f; s.easeCm = 8f; s.thighEaseCm = 12f; s.hemWidth = 0.7f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("Jeans", GarmentType.Trousers, "Slim denim jeans to the ankle with a waistband, sitting on the hips.", () =>
            {
                var s = Base(GarmentType.Trousers, "Jeans", "Denim");
                s.rise = 0.35f; s.legLength = 4f; s.easeCm = 6f; s.thighEaseCm = 8f; s.hemWidth = 0.35f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("Wide-Leg Trousers", GarmentType.Trousers, "High-waisted wool trousers flaring wide to the floor.", () =>
            {
                var s = Base(GarmentType.Trousers, "Wide-Leg Trousers", "Wool");
                s.rise = 0.8f; s.legLength = 5f; s.easeCm = 10f; s.thighEaseCm = 20f; s.hemWidth = 1.25f; s.waistband = BandStyle.Self; s.waistbandCm = 5f;
                return s;
            }),
            P("Joggers", GarmentType.Trousers, "Loose fleece joggers with a rib waistband and rib ankle cuffs.", () =>
            {
                var s = Base(GarmentType.Trousers, "Joggers", "Fleece");
                s.rise = 0.35f; s.legLength = 4f; s.easeCm = 26f; s.thighEaseCm = 18f; s.hemWidth = 0.4f; s.waistband = BandStyle.Rib; s.waistbandCm = 5f;
                s.legCuffs = BandStyle.Rib; s.bandHeightCm = 7f;
                return s;
            }),
            P("Leggings", GarmentType.Trousers, "Spandex leggings cut smaller than the body, so they stretch on and grip.", () =>
            {
                var s = Base(GarmentType.Trousers, "Leggings", "Spandex");
                s.rise = 0.4f; s.legLength = 4f; s.easeCm = -14f; s.thighEaseCm = -10f; s.hemWidth = 0f; s.waistband = BandStyle.None;
                return s;
            }),
            P("Shorts", GarmentType.Trousers, "Cotton shorts to mid-thigh with a waistband.", () =>
            {
                var s = Base(GarmentType.Trousers, "Shorts", "Cotton");
                s.rise = 0.35f; s.legLength = 1f; s.easeCm = 6f; s.thighEaseCm = 10f; s.hemWidth = 0.6f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("Hot Pants", GarmentType.Trousers, "Very short denim shorts.", () =>
            {
                var s = Base(GarmentType.Trousers, "Hot Pants", "Denim");
                s.rise = 0.4f; s.legLength = 0f; s.easeCm = 4f; s.thighEaseCm = 4f; s.hemWidth = 0.5f; s.waistband = BandStyle.Self; s.waistbandCm = 3f;
                return s;
            }),
            P("Bermuda Shorts", GarmentType.Trousers, "Loose cotton shorts to just above the knee.", () =>
            {
                var s = Base(GarmentType.Trousers, "Bermuda Shorts", "Cotton");
                s.rise = 0.4f; s.legLength = 2f; s.easeCm = 10f; s.thighEaseCm = 14f; s.hemWidth = 0.8f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("Boxer Shorts", GarmentType.Trousers, "Loose cotton boxers with a rib waistband.", () =>
            {
                var s = Base(GarmentType.Trousers, "Boxer Shorts", "Cotton");
                s.rise = 0.35f; s.legLength = 0.6f; s.easeCm = 20f; s.thighEaseCm = 18f; s.hemWidth = 0.9f; s.waistband = BandStyle.Rib; s.waistbandCm = 4f;
                return s;
            }),

            // ---- Skirts ----
            P("Mini Skirt", GarmentType.Skirt, "Short denim skirt with a waistband.", () =>
            {
                var s = Base(GarmentType.Skirt, "Mini Skirt", "Denim");
                s.length = 4f; s.flare = 0.15f; s.rise = 0.7f; s.easeCm = 5f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("A-Line Skirt", GarmentType.Skirt, "Cotton skirt flaring from a waistband to the knee.", () =>
            {
                var s = Base(GarmentType.Skirt, "A-Line Skirt", "Cotton");
                s.length = 6f; s.flare = 0.35f; s.rise = 1f; s.easeCm = 6f; s.waistband = BandStyle.Self; s.waistbandCm = 3f;
                return s;
            }),
            P("Pencil Skirt", GarmentType.Skirt, "Fitted wool skirt to the knee, darted under a waistband.", () =>
            {
                var s = Base(GarmentType.Skirt, "Pencil Skirt", "Wool");
                s.length = 6f; s.flare = 0f; s.rise = 1f; s.easeCm = 5f; s.waistband = BandStyle.Self; s.waistbandCm = 3f;
                return s;
            }),
            P("Pleated Skirt", GarmentType.Skirt, "Wool skirt with eight pressed knife pleats on each panel, under a waistband.", () =>
            {
                var s = Base(GarmentType.Skirt, "Pleated Skirt", "Wool");
                s.length = 5f; s.pleats = 8; s.rise = 1f; s.easeCm = 4f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("Full Skirt", GarmentType.Skirt, "Soft wool skirt flaring full from the waist.", () =>
            {
                var s = Base(GarmentType.Skirt, "Full Skirt", "Wool");
                s.length = 6.5f; s.flare = 1f; s.rise = 1f; s.easeCm = 6f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),
            P("Maxi Skirt", GarmentType.Skirt, "Cotton skirt to the floor, gently flared.", () =>
            {
                var s = Base(GarmentType.Skirt, "Maxi Skirt", "Cotton");
                s.length = 9f; s.flare = 0.4f; s.rise = 1f; s.easeCm = 6f; s.waistband = BandStyle.Self; s.waistbandCm = 4f;
                return s;
            }),

            // ---- Jumpsuits ----
            P("Jumpsuit", GarmentType.Jumpsuit, "Cotton jumpsuit to the ankle with short sleeves and a V neck.", () =>
            {
                var s = Base(GarmentType.Jumpsuit, "Jumpsuit", "Cotton");
                s.legLength = 4f; s.sleeve = 0.3f; s.easeCm = 14f; s.thighEaseCm = 14f; s.hemWidth = 0.6f; s.neckline = NecklineShape.V; s.neckWidth = 0.15f; s.neckDepthCm = 14f;
                return s;
            }),
            P("Romper", GarmentType.Jumpsuit, "Short cotton jumpsuit with short sleeves.", () =>
            {
                var s = Base(GarmentType.Jumpsuit, "Romper", "Cotton");
                s.legLength = 1f; s.sleeve = 0.25f; s.easeCm = 12f; s.thighEaseCm = 12f; s.hemWidth = 0.7f; s.neckline = NecklineShape.Scoop; s.neckWidth = 0.15f; s.neckDepthCm = 10f;
                return s;
            }),
            P("Boiler Suit", GarmentType.Jumpsuit, "Roomy long-sleeved work suit with a shirt collar and cuffs.", () =>
            {
                var s = Base(GarmentType.Jumpsuit, "Boiler Suit", "Cotton");
                s.legLength = 4f; s.sleeve = 1f; s.sleeveEaseCm = 12f; s.cuffs = BandStyle.Self; s.bandHeightCm = 5f; s.trimFabric = "Cotton"; s.easeCm = 20f; s.thighEaseCm = 18f; s.hemWidth = 0.8f;
                s.neckWidth = 0.1f; s.neckDepthCm = 8f; s.collar = CollarStyle.Shirt; s.collarHeightCm = 6f;
                return s;
            }),
            P("Catsuit", GarmentType.Jumpsuit, "Skin-tight spandex suit with long sleeves.", () =>
            {
                var s = Base(GarmentType.Jumpsuit, "Catsuit", "Spandex");
                s.legLength = 4f; s.sleeve = 1f; s.sleeveEaseCm = 0f; s.easeCm = -8f; s.thighEaseCm = -8f; s.hemWidth = 0f; s.neckWidth = 0.05f; s.neckDepthCm = 7f;
                return s;
            }),
        };

        public static GarmentPreset Find(string name)
        {
            foreach (GarmentPreset p in All) if (p.Name == name) return p;
            return null;
        }

        public static List<GarmentPreset> ForType(GarmentType type)
        {
            var list = new List<GarmentPreset>();
            foreach (GarmentPreset p in All) if (p.Type == type) list.Add(p);
            return list;
        }

        /// <summary>The first preset of a type: where choosing a type starts.</summary>
        public static GarmentStyle Default(GarmentType type) => ForType(type)[0].Make();
    }
}
