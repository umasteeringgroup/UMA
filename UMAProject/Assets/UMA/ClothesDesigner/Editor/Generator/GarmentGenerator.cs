using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Drafts a finished sewing pattern from a garment type and its style values (<see cref="GarmentStyle"/>),
    /// measured to a body (<see cref="GarmentBody"/>): pieces cut to the body's girths plus the ease asked for,
    /// laid round the body where they belong, sewn, with sizes that follow the body when it changes. The result is
    /// an ordinary pattern: drape it, edit it on the Pattern Canvas, bake it. Drafting takes a few milliseconds,
    /// so a style can be changed and re-drafted while the artist watches.
    /// </summary>
    public static partial class GarmentGenerator
    {
        /// <summary>Where a top, a dress or a skirt can end, from the crop top to the floor.</summary>
        public static readonly string[] LengthStops = { "Crop", "Waist", "High Hip", "Hip", "Mini", "Above Knee", "Knee", "Midi", "Ankle", "Floor" };

        /// <summary>Where a trouser leg can end.</summary>
        public static readonly string[] LegStops = { "Hot Pants", "Shorts", "Bermuda", "Capri", "Ankle", "Full Length" };

        /// <summary>Names for sleeve lengths (share of the arm), for the panel.</summary>
        public static string SleeveName(float sleeve)
        {
            if (sleeve < 0.04f) return "Sleeveless";
            if (sleeve < 0.2f) return "Cap";
            if (sleeve < 0.4f) return "Short";
            if (sleeve < 0.62f) return "Elbow";
            if (sleeve < 0.88f) return "Three-Quarter";
            return sleeve <= 1.03f ? "Long" : "Extra Long";
        }

        public static string StopName(string[] stops, float value) => stops[Mathf.Clamp(Mathf.RoundToInt(value), 0, stops.Length - 1)];

        /// <summary>The height (cm below the top of the shoulders) of a point on <see cref="LengthStops"/>.</summary>
        public static float LengthY(GarmentBody b, float stop)
        {
            float[] ys =
            {
                b.chestY + 7f, b.waistY, b.waistY + 0.5f * (b.hipY - b.waistY), b.hipY, b.crotchY + 12f,
                // Floor: a little above it, as long skirts hang a few centimetres longer than drafted (silk stretched 2.7% on a gown).
                b.kneeY - 9f, b.kneeY + 2f, 0.5f * (b.kneeY + b.ankleY), b.ankleY - 4f, b.floorY - 6f
            };
            return Stops(ys, stop);
        }

        /// <summary>The height (cm below the top of the shoulders) of a point on <see cref="LegStops"/>.</summary>
        public static float LegY(GarmentBody b, float stop)
        {
            float[] ys = { b.crotchY + 5f, b.crotchY + 17f, b.kneeY - 6f, 0.5f * (b.kneeY + b.ankleY) + 4f, b.ankleY, b.floorY - 3f };
            return Stops(ys, stop);
        }

        private static float Stops(float[] ys, float stop)
        {
            float f = Mathf.Clamp(stop, 0f, ys.Length - 1);
            int i = Mathf.Min(ys.Length - 2, (int)f);
            return Mathf.Lerp(ys[i], ys[i + 1], f - i);
        }

        /// <summary>Drafts the garment. The style is clamped to its ranges first (a copy; the given one is not changed).</summary>
        /// <param name="tightUvs">
        /// False lays the UVs out by bounding box, in a millisecond (<see cref="UvLayout.QuickPack"/>), for drafts redone
        /// while a slider moves; the bake packs them tightly. True packs them tightly now (a few hundred milliseconds).
        /// </param>
        public static ClothesPattern Generate(GarmentStyle style, GarmentBody body, bool tightUvs = true)
        {
            GarmentStyle s = style.Clone();
            s.Clamp();
            body ??= GarmentBody.Default;
            var d = new Draft { b = new PatternBuilder(TitleFor(s), Describe(s, body)), s = s, body = body };
            switch (s.type)
            {
                case GarmentType.Trousers:
                    Trousers(d, -1f);
                    break;
                case GarmentType.Skirt:
                    Skirt(d);
                    break;
                case GarmentType.Jumpsuit:
                    Jumpsuit(d);
                    break;
                default:
                    Top(d, -1f, null);
                    break;
            }
            ClothesPattern pattern = d.b.Finish(tightUvs);
            foreach (System.Action<ClothesPattern> step in d.after) step(pattern);
            pattern.generatorStyle = s.ToJson();
            return pattern;
        }

        /// <summary>One drafting: the builder, the style and the body, and work left for when the pieces are laid out.</summary>
        private sealed class Draft
        {
            public PatternBuilder b;
            public GarmentStyle s;
            public GarmentBody body;
            /// <summary>Run on the finished pattern (its pieces moved apart on the canvas): tacks, which are placed by piece position.</summary>
            public readonly List<System.Action<ClothesPattern>> after = new List<System.Action<ClothesPattern>>();
        }

        private static string TitleFor(GarmentStyle s)
        {
            if (!string.IsNullOrEmpty(s.preset) && s.preset != GarmentPresets.Custom) return s.preset;
            return !string.IsNullOrEmpty(s.basedOn) ? s.basedOn + " (Custom)" : "Custom " + s.type;
        }

        private static string Describe(GarmentStyle s, GarmentBody body)
        {
            string fit = s.easeCm < 0f ? "stretched " + (-s.easeCm).ToString("0") + " cm under the body" : s.easeCm.ToString("0") + " cm of ease";
            string where = body.measured ? "measured from the body it was drafted on" : "drafted on Human Female 3.0's measurements";
            switch (s.type)
            {
                case GarmentType.Trousers:
                    return "Trousers to " + StopName(LegStops, s.legLength).ToLowerInvariant() + " in " + s.fabric.ToLowerInvariant() + ", " + fit + " round the hips. Drafted by the garment generator, " + where + ".";
                case GarmentType.Skirt:
                    return "Skirt to " + StopName(LengthStops, s.length).ToLowerInvariant() + " in " + s.fabric.ToLowerInvariant() + (s.pleats > 0 ? ", " + s.pleats + " pleats a panel" : "") + ". Drafted by the garment generator, " + where + ".";
                default:
                    return s.type + " to " + (s.type == GarmentType.Jumpsuit ? StopName(LegStops, s.legLength) : StopName(LengthStops, s.length)).ToLowerInvariant() + " in " + s.fabric.ToLowerInvariant() + ", " + SleeveName(s.sleeve).ToLowerInvariant() + " sleeves, " + fit + " round the chest. Drafted by the garment generator, " + where + ".";
            }
        }

        // ---- Shared geometry -------------------------------------------------------------------

        /// <summary>A level down a piece's side: its height on the piece and its half width.</summary>
        private struct Level
        {
            public float y, half;
            public Level(float y, float half) { this.y = y; this.half = half; }
        }

        private static string SideName(string prefix, int i) => (prefix.Length > 0 ? prefix + " side" : "Side") + (i == 0 ? "" : " " + (i + 1));

        /// <summary>
        /// Smooths a run of edges through their points (Catmull-Rom tangents), so a side seam through the waist and
        /// the hips bends without corners. The run's ends keep the direction of their own edge.
        /// </summary>
        private static void Smooth(PieceBuilder p, IList<string> edges)
        {
            int n = edges.Count;
            if (n < 2) return;
            var pts = new Vector2[n + 1];
            for (int i = 0; i < n; i++) pts[i] = p.Piece.points[p.Index(edges[i])].position;
            int last = p.Index(edges[n - 1]);
            pts[n] = p.Piece.points[(last + 1) % p.Piece.points.Count].position;
            var tangent = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                if (i == 0) tangent[i] = pts[1] - pts[0];
                else if (i == n) tangent[i] = pts[n] - pts[n - 1];
                else tangent[i] = 0.5f * (pts[i + 1] - pts[i - 1]);
            }
            for (int i = 0; i < n; i++)
            {
                PatternEdge e = p.Edge(edges[i]);
                e.curved = true;
                e.handleA = tangent[i] / 3f;
                e.handleB = -tangent[i + 1] / 3f;
            }
        }

        private static float CubicLength(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            float length = 0f;
            Vector2 prev = p0;
            const int steps = 32;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1f - t;
                Vector2 q = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
                length += (q - prev).magnitude;
                prev = q;
            }
            return length;
        }

        /// <summary>A band: a rectangle with edges Bottom, Right end, Top, Left end; with a shorter top, a contour band that fits a cone.</summary>
        private static PieceBuilder Band(PatternBuilder b, string name, float length, float height, string fabric, float topLength = -1f)
        {
            float top = topLength > 0f ? topLength : length;
            return b.Piece(name)
                .Point(-length * 0.5f, 0f, "Bottom")
                .Point(length * 0.5f, 0f, "Right end")
                .Point(top * 0.5f, height, "Top")
                .Point(-top * 0.5f, height, "Left end")
                .Fabric(fabric);
        }

        private static string BandFabric(GarmentStyle s, BandStyle band) => band == BandStyle.Rib ? "Rib" : band == BandStyle.Self ? s.fabric : s.trimFabric;

        /// <summary>How much shorter than its edge a band is cut, so it gathers the edge in.</summary>
        private static float Gather(BandStyle band, bool onTop) => band == BandStyle.Rib ? (onTop ? 0.88f : 0.75f) : 1f;

        private static bool Stretchy(string fabric)
        {
            FabricPreset p = FabricPresets.Find(fabric);
            return p != null && p.Settings.stretchCompliance > 0.01f;
        }
    }
}
