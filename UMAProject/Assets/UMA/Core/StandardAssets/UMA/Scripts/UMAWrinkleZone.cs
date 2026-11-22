namespace UMA
{
    /// <summary>
    /// Expression wrinkle zones of the "UMA/URP/Realistic Skin SSS Wrinkles" shader. The value is the zone id stored in
    /// the wrinkle control map (0 = no zone) and the number in the zone's weight property, <c>_WrinkleZone01</c> to
    /// <c>_WrinkleZone15</c>. Left and Right are the character's own sides.
    /// </summary>
    public enum UMAWrinkleZone
    {
        None = 0,
        ForeheadLeft = 1,
        ForeheadRight = 2,
        /// <summary>Vertical frown lines between the brows.</summary>
        Glabella = 3,
        /// <summary>Horizontal lines across the top of the nose when the brows come down.</summary>
        NoseRoot = 4,
        /// <summary>Lines on the sides of the nose in a sneer.</summary>
        BunnyLines = 5,
        /// <summary>Crow's feet at the outer corner of the eye and the folds under the lower lid.</summary>
        CrowsFeetLeft = 6,
        CrowsFeetRight = 7,
        NasolabialLeft = 8,
        NasolabialRight = 9,
        /// <summary>Lines from the corner of the mouth toward the chin.</summary>
        MouthCornerLeft = 10,
        MouthCornerRight = 11,
        /// <summary>Vertical lines above and below the lips in a pucker.</summary>
        LipLines = 12,
        /// <summary>Dimpling of the chin and the crease under the lower lip.</summary>
        Chin = 13,
        NeckLeft = 14,
        NeckRight = 15,
    }

    /// <summary>Constants shared by the wrinkle shader, its baker and its driver.</summary>
    public static class UMAWrinkleZones
    {
        /// <summary>Number of usable zone ids, 1 to 15.</summary>
        public const int ZoneCount = 15;
        /// <summary>Distinct ids a control map can store, including 0.</summary>
        public const int IdLevels = 16;
        public const string WrinkleMapProperty = "_WrinkleMap";
        public const string ControlMapProperty = "_WrinkleControlMap";
        public const string ShaderName = "UMA/URP/Realistic Skin SSS Wrinkles";
        /// <summary>Overlay that carries the UMA 3 head's wrinkle maps (and, as its UMA Material, the wrinkle skin).</summary>
        public const string UMA3OverlayName = "UMA30_ExpressionWrinkles_UDIM1001";
        /// <summary>The UMA 3 head slot, UDIM tile 1001.</summary>
        public const string UMA3HeadSlotName = "UMA30_Body_UDIM1001";
        /// <summary>The expression-to-zone profile for the UMA 3 expression set.</summary>
        public const string UMA3ProfilePath = "Assets/UMA/UMA3/Races/Expressions/UMA 30 Wrinkle Profile.asset";

        /// <summary>Name of the material float that weights <paramref name="zone"/>, for example "_WrinkleZone03".</summary>
        public static string WeightProperty(UMAWrinkleZone zone)
        {
            int id = (int)zone;
            if (id < 1 || id > ZoneCount) throw new System.ArgumentOutOfRangeException(nameof(zone));
            return "_WrinkleZone" + id.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Byte stored in a control map id channel for <paramref name="id"/> (0..15): id * 17.</summary>
        public static byte EncodeId(int id)
        {
            if (id < 0 || id >= IdLevels) throw new System.ArgumentOutOfRangeException(nameof(id));
            return (byte)(id * 17);
        }

        /// <summary>Zone id read back from a filtered id channel value in 0..1, as the shader does: round(value * 15).</summary>
        public static int DecodeId(float value)
        {
            int id = (int)System.Math.Round(value * (IdLevels - 1), System.MidpointRounding.AwayFromZero);
            return id < 0 ? 0 : id >= IdLevels ? IdLevels - 1 : id;
        }
    }
}
