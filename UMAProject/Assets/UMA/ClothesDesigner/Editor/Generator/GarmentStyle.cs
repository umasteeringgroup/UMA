using System;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>The kinds of garment the generator drafts.</summary>
    public enum GarmentType
    {
        /// <summary>Tees, shirts, tanks, hoodies, cardigans: a front, a back, sleeves, collars and bands.</summary>
        Top = 0,
        /// <summary>A top carried on down to a skirt in one piece, from mini to gown.</summary>
        Dress = 1,
        /// <summary>A loose robe with wide sleeves and a wrapped front.</summary>
        Kimono = 2,
        /// <summary>Trousers, from hot pants to full length: four pieces, each laid round its leg.</summary>
        Trousers = 3,
        /// <summary>A skirt from the waist: two panels, pencil to full, with darts or pleats.</summary>
        Skirt = 4,
        /// <summary>A top sewn to trousers at the waist (a romper with short legs).</summary>
        Jumpsuit = 5
    }

    public enum NecklineShape
    {
        /// <summary>Round and close to the neck.</summary>
        Crew = 0,
        /// <summary>Round and lower.</summary>
        Scoop = 1,
        /// <summary>Straight down to a point.</summary>
        V = 2,
        /// <summary>Straight across.</summary>
        Square = 3,
        /// <summary>Wide and shallow, from shoulder to shoulder.</summary>
        Boat = 4
    }

    public enum CollarStyle
    {
        None = 0,
        /// <summary>A straight stand round the neckline (a polo's or a mandarin collar).</summary>
        Band = 1,
        /// <summary>A shirt collar: a firm stand with a leaf turned down over it.</summary>
        Shirt = 2,
        /// <summary>A two-panel hood.</summary>
        Hood = 3,
        /// <summary>A tall tube up the neck, folded or not.</summary>
        Turtleneck = 4
    }

    public enum FrontStyle
    {
        /// <summary>One front piece.</summary>
        Closed = 0,
        /// <summary>Two front pieces meeting at the centre, not sewn (a cardigan or a jacket worn open).</summary>
        Open = 1,
        /// <summary>Two front pieces crossing over each other (a kimono or a robe).</summary>
        Wrap = 2
    }

    public enum BandStyle
    {
        None = 0,
        /// <summary>A band in the garment's own fabric, the length of what it is sewn to.</summary>
        Self = 1,
        /// <summary>A rib band cut shorter than what it is sewn to, so it gathers it in and grips.</summary>
        Rib = 2
    }

    /// <summary>
    /// What the garment generator drafts: a garment type and its style values, the way Divine Cut and Tailornova
    /// describe a garment (length, neck, sleeves, fit, collars, bands), in centimetres and fractions. The
    /// generator measures the body for the rest, so the same style fits any body. Every value has a sensible
    /// range; <see cref="Clamp"/> keeps it there.
    /// </summary>
    [Serializable]
    public sealed class GarmentStyle
    {
        public GarmentType type = GarmentType.Top;
        /// <summary>The preset these values started from (for the panel); "Custom" after a change.</summary>
        public string preset = "T-Shirt";
        /// <summary>The preset a custom style was changed from, for naming the pattern ("Hoodie (Custom)").</summary>
        public string basedOn;

        // ---- Body of a top, a dress or a kimono; a skirt's length ----

        /// <summary>Where the hem falls, on <see cref="GarmentGenerator.LengthStops"/>: 0 crop top to 9 floor.</summary>
        public float length = 3f;
        /// <summary>Room round the body (cm): round the chest for tops, round the hips for trousers and skirts. Negative for stretch fabrics worn tight.</summary>
        public float easeCm = 10f;
        /// <summary>How far the sides follow the waist in: 0 straight, 1 fitted.</summary>
        public float waistShaping;
        /// <summary>How far the hem flares out below the hips: 0 straight, 1 full.</summary>
        public float flare;
        /// <summary>How much the middle of the hem drops below its sides (cm): a shirt tail.</summary>
        public float hemDropCm;
        /// <summary>Wider (positive) or narrower shoulders than the body's (cm each side). Dropped shoulders for oversized tops.</summary>
        public float shoulderCm;

        public NecklineShape neckline = NecklineShape.Crew;
        /// <summary>How wide the neckline is: 0 close to the neck, 1 out towards the shoulders.</summary>
        public float neckWidth = 0.25f;
        /// <summary>How deep the front neckline is below the neck points (cm).</summary>
        public float neckDepthCm = 9f;
        /// <summary>How deep the back neckline is (cm).</summary>
        public float backNeckDepthCm = 2.5f;

        public CollarStyle collar = CollarStyle.None;
        /// <summary>The collar's height (cm): a band's or a turtleneck's, a shirt collar's stand.</summary>
        public float collarHeightCm = 5f;

        public FrontStyle front = FrontStyle.Closed;
        /// <summary>How far each front crosses past the centre on a wrapped front (cm).</summary>
        public float overlapCm = 10f;

        /// <summary>Sleeve length as a share of the arm, shoulder to wrist: 0 sleeveless, 0.3 short, 0.5 elbow, 1 wrist.</summary>
        public float sleeve = 0.3f;
        /// <summary>Room round the upper arm (cm).</summary>
        public float sleeveEaseCm = 9f;
        /// <summary>How the sleeve opens at its hem: 0 close round the arm, 1 a wide bell.</summary>
        public float sleeveFlare;
        /// <summary>Sleeveless: the width of the shoulder strap (cm).</summary>
        public float strapCm = 6f;
        public BandStyle cuffs = BandStyle.None;
        public BandStyle hemBand = BandStyle.None;
        /// <summary>The height of cuffs and hem bands (cm).</summary>
        public float bandHeightCm = 6f;

        // ---- Trousers, skirts and the legs of a jumpsuit ----

        /// <summary>Where the top sits: 0 low on the hips, 0.5 on the hips, 1 at the waist.</summary>
        public float rise = 0.5f;
        /// <summary>Trouser length on <see cref="GarmentGenerator.LegStops"/>: 0 hot pants to 5 floor.</summary>
        public float legLength = 4f;
        /// <summary>Room round each thigh (cm).</summary>
        public float thighEaseCm = 8f;
        /// <summary>The leg's width at the hem: 0 close round the ankle, 1 as wide as the thigh, up to 1.6 flared.</summary>
        public float hemWidth = 0.35f;
        public BandStyle waistband = BandStyle.Self;
        /// <summary>The waistband's height (cm).</summary>
        public float waistbandCm = 4f;
        public BandStyle legCuffs = BandStyle.None;
        /// <summary>Skirts: pressed knife pleats on each panel.</summary>
        public int pleats;

        /// <summary>The fabric preset for the garment.</summary>
        public string fabric = "Jersey";
        /// <summary>The fabric preset for its bands, cuffs and turtleneck (rib bands use Rib, whatever this says).</summary>
        public string trimFabric = "Elastic";

        public GarmentStyle Clone() => (GarmentStyle)MemberwiseClone();

        public string ToJson() => JsonUtility.ToJson(this);

        public static GarmentStyle FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var style = JsonUtility.FromJson<GarmentStyle>(json);
                style?.Clamp();
                return style;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Keeps every value in the range the generator drafts well.</summary>
        public void Clamp()
        {
            length = Mathf.Clamp(length, 0f, GarmentGenerator.LengthStops.Length - 1);
            easeCm = Mathf.Clamp(easeCm, -20f, 60f);
            waistShaping = Mathf.Clamp01(waistShaping);
            flare = Mathf.Clamp01(flare);
            hemDropCm = Mathf.Clamp(hemDropCm, 0f, 15f);
            shoulderCm = Mathf.Clamp(shoulderCm, -4f, 10f);
            neckWidth = Mathf.Clamp01(neckWidth);
            neckDepthCm = Mathf.Clamp(neckDepthCm, 1f, 35f);
            backNeckDepthCm = Mathf.Clamp(backNeckDepthCm, 1f, 30f);
            collarHeightCm = Mathf.Clamp(collarHeightCm, 2f, 20f);
            overlapCm = Mathf.Clamp(overlapCm, 0f, 25f);
            sleeve = Mathf.Clamp(sleeve, 0f, 1.1f);
            sleeveEaseCm = Mathf.Clamp(sleeveEaseCm, 0f, 40f);
            sleeveFlare = Mathf.Clamp01(sleeveFlare);
            strapCm = Mathf.Clamp(strapCm, 2f, 14f);
            bandHeightCm = Mathf.Clamp(bandHeightCm, 2f, 15f);
            rise = Mathf.Clamp01(rise);
            legLength = Mathf.Clamp(legLength, 0f, GarmentGenerator.LegStops.Length - 1);
            thighEaseCm = Mathf.Clamp(thighEaseCm, -15f, 50f);
            hemWidth = Mathf.Clamp(hemWidth, 0f, 1.6f);
            waistbandCm = Mathf.Clamp(waistbandCm, 2f, 12f);
            pleats = Mathf.Clamp(pleats, 0, 12);
            if (FabricPresets.Find(fabric) == null) fabric = FabricPresets.DefaultName;
            if (FabricPresets.Find(trimFabric) == null) trimFabric = "Elastic";
        }

        /// <summary>True for the types with a top part (a bodice).</summary>
        public bool HasTop => type == GarmentType.Top || type == GarmentType.Dress || type == GarmentType.Kimono || type == GarmentType.Jumpsuit;

        /// <summary>True for the types with legs.</summary>
        public bool HasLegs => type == GarmentType.Trousers || type == GarmentType.Jumpsuit;
    }
}
