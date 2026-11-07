using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintSeamProfile { None, FoldedBand, Lapped, PressedOpen, EnclosedRidge, Piping, RawEdge }
    public enum TexturePaintStitchPattern { Lockstitch, Chainstitch, Zigzag, Overlock, CoverLooper, Blind, BarTack }
    public enum TexturePaintSeamPreset
    {
        DenimChainstitchHem, DoubleTurnHem, FlatFelled, MockFelled, Lapped,
        PlainPressedOpen, FrenchSeam, BoundEdge, Piping, OverlockedEdge,
        CoverstitchHem, RolledHem, BlindHem, RawFrayedHem, Topstitch, BarTack
    }

    [Serializable]
    public sealed class TexturePaintSeamStitchRow
    {
        public bool enabled = true;
        public TexturePaintStitchPattern pattern;
        public Color color = new Color(.58f, .48f, .34f, 1f);
        public float offset = .2f;
        public float thickness = .012f;
        public float spacing = .18f;
        public float length = .78f;
        public float span = .13f;
        public float phase;
        public float relief = .4f;
        public TexturePaintSeamStitchRow Clone() => (TexturePaintSeamStitchRow)MemberwiseClone();
        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(TexturePaintStitchPattern), pattern)) pattern = TexturePaintStitchPattern.Lockstitch;
            offset = TexturePaintHemSeamSettings.Finite(offset, -.48f, .48f, .2f);
            thickness = TexturePaintHemSeamSettings.Finite(thickness, .002f, .1f, .012f);
            spacing = TexturePaintHemSeamSettings.Finite(spacing, .025f, 1f, .18f);
            length = TexturePaintHemSeamSettings.Finite(length, .05f, 1f, .78f);
            span = TexturePaintHemSeamSettings.Finite(span, .01f, .8f, .13f);
            phase = TexturePaintHemSeamSettings.Finite(phase, 0f, 1f, 0f);
            relief = TexturePaintHemSeamSettings.Finite(relief, 0f, 2f, .4f);
            color = new Color(TexturePaintHemSeamSettings.Finite(color.r,0,1,.58f),
                TexturePaintHemSeamSettings.Finite(color.g,0,1,.48f),
                TexturePaintHemSeamSettings.Finite(color.b,0,1,.34f),
                TexturePaintHemSeamSettings.Finite(color.a,0,1,1));
        }
    }

    /// <summary>Native ribbon generator. Distances are fractions of the full ribbon width.
    /// Settings contain no transient textures, so document, history and UDIM peers share the
    /// same deterministic construction without baking the fabric underneath into the layer.</summary>
    [Serializable]
    public sealed class TexturePaintHemSeamSettings
    {
        public const int MaximumRows = 8;
        public bool enabled;
        public TexturePaintSeamPreset preset;
        public TexturePaintSeamProfile profile = TexturePaintSeamProfile.FoldedBand;
        public float bandWidth = .62f;
        public float offset;
        public float relief = .025f;
        public float depthStrength = 1f;
        public float shadingStrength = 1f;
        [SerializeField] private int materialResponseVersion = 1;
        public bool mirror;
        public float roping = .55f;
        public float ropingFrequency = 4.5f;
        public float ropingSlant = 1.8f;
        public float irregularity = .35f;
        public int seed = 1;
        public float puckering = .25f;
        public float fraying;
        public float recessDarkening = .35f;
        public float wear = .3f;
        public float seamProtection = .7f;
        public float ambientOcclusion = .55f;
        public float fabricRoughness = .82f;
        public float threadRoughness = .6f;
        public float normalStrength = 1f;
        public bool albedo = true;
        // Prefer height so the final RNM pass retains the underlying cloth normal.
        public bool normal;
        public bool ao = true;
        public bool roughness = true;
        public bool height = true;
        public bool masks;
        public List<TexturePaintSeamStitchRow> rows = new List<TexturePaintSeamStitchRow> { new TexturePaintSeamStitchRow() };

        public TexturePaintHemSeamSettings Clone()
        {
            var copy = (TexturePaintHemSeamSettings)MemberwiseClone();
            copy.rows = new List<TexturePaintSeamStitchRow>();
            if (rows != null) foreach (var row in rows) if (row != null) copy.rows.Add(row.Clone());
            return copy;
        }
        internal static float Finite(float x, float min, float max, float fallback)
            => float.IsFinite(x) ? Mathf.Clamp(x, min, max) : fallback;
        public void Normalize()
        {
            if(materialResponseVersion<1){depthStrength=shadingStrength=1;materialResponseVersion=1;}
            depthStrength=Finite(depthStrength,0,4,1);shadingStrength=Finite(shadingStrength,0,3,1);
            if (!Enum.IsDefined(typeof(TexturePaintSeamProfile),profile)) profile=TexturePaintSeamProfile.FoldedBand;
            if (!Enum.IsDefined(typeof(TexturePaintSeamPreset),preset)) preset=TexturePaintSeamPreset.DenimChainstitchHem;
            bandWidth=Finite(bandWidth,.05f,.95f,.62f); offset=Finite(offset,-.4f,.4f,0);
            relief=Finite(relief,0,.15f,.025f); roping=Finite(roping,0,1,.55f);
            ropingFrequency=Finite(ropingFrequency,.25f,24,4.5f); ropingSlant=Finite(ropingSlant,-6,6,1.8f);
            irregularity=Finite(irregularity,0,1,.35f); puckering=Finite(puckering,0,1,.25f);
            fraying=Finite(fraying,0,1,0); recessDarkening=Finite(recessDarkening,0,1,.35f);
            wear=Finite(wear,0,1,.3f); seamProtection=Finite(seamProtection,0,1,.7f);
            ambientOcclusion=Finite(ambientOcclusion,0,1,.55f); fabricRoughness=Finite(fabricRoughness,0,1,.82f);
            threadRoughness=Finite(threadRoughness,0,1,.6f); normalStrength=Finite(normalStrength,0,4,1);
            rows ??= new List<TexturePaintSeamStitchRow>(); rows.RemoveAll(x=>x==null);
            if (rows.Count>MaximumRows) rows.RemoveRange(MaximumRows,rows.Count-MaximumRows);
            foreach(var row in rows) row.Normalize();
        }
        public IEnumerable<TexturePaintChannel> OutputChannels()
        {
            if (albedo) yield return TexturePaintChannel.Albedo;
            if (normal) yield return TexturePaintChannel.Normal;
            if (ao) yield return TexturePaintChannel.AmbientOcclusion;
            if (roughness) yield return TexturePaintChannel.Roughness;
            if (height) yield return TexturePaintChannel.NormalControl;
            if (masks) yield return TexturePaintChannel.Custom;
        }
        public bool HasOutputs => albedo || normal || ao || roughness || height || masks;
        public bool SupportsAnyOutput(TextureSet textures)
        {
            foreach (var channel in OutputChannels()) if (textures?.GetChannel(channel) != null) return true;
            return false;
        }
        public void PopulateSources(StrokeContext context)
        {
            context.channelSources.Clear();
            foreach (var channel in OutputChannels()) context.channelSources[channel] = new TexturePaintChannelSourceSettings
                { source = TexturePaintBrushSource.Color, color = Color.white };
        }
        public void Bind(MaterialPropertyBlock properties, TexturePaintChannel channel)
        {
            // Work on a sanitized snapshot; rendering never mutates a saved authoring model.
            var value=Clone(); value.Normalize();
            properties.SetInt("_HemEnabled",enabled ? 1 : 0);
            properties.SetInt("_HemChannel",(int)channel);
            properties.SetVector("_HemShape",new Vector4((int)value.profile,value.bandWidth,value.offset,value.relief));
            properties.SetVector("_HemResponse",new Vector4(value.depthStrength,value.shadingStrength,0,0));
            properties.SetVector("_HemRope",new Vector4(value.roping,value.ropingFrequency,value.ropingSlant,value.irregularity));
            properties.SetVector("_HemCloth",new Vector4(value.recessDarkening,value.wear,value.seamProtection,value.ambientOcclusion));
            properties.SetVector("_HemFinish",new Vector4(value.fabricRoughness,value.threadRoughness,value.normalStrength,(uint)value.seed % 65521));
            properties.SetVector("_HemDetails",new Vector4(value.puckering,value.fraying,value.mirror ? -1 : 1,0));
            var a=new Vector4[MaximumRows]; var b=new Vector4[MaximumRows]; var colors=new Vector4[MaximumRows];
            int count=0;
            foreach(var row in value.rows)
            {
                if(!row.enabled) continue;
                a[count]=new Vector4(row.offset,row.thickness,row.spacing,row.length);
                b[count]=new Vector4((int)row.pattern,row.span,row.phase,row.relief);
                colors[count]=row.color.linear; count++;
            }
            properties.SetInt("_HemRowCount",count);
            properties.SetVectorArray("_HemRowsA",a); properties.SetVectorArray("_HemRowsB",b);
            properties.SetVectorArray("_HemRowColors",colors);
        }
        public static TexturePaintHemSeamSettings Create(TexturePaintSeamPreset preset)
        {
            var s = new TexturePaintHemSeamSettings { enabled=true, preset=preset };
            TexturePaintSeamStitchRow Row(float offset, TexturePaintStitchPattern pattern=TexturePaintStitchPattern.Lockstitch)
                => new TexturePaintSeamStitchRow { offset=offset,pattern=pattern };
            s.rows.Clear();
            switch(preset)
            {
                // Exterior needle line; switch the row to Chainstitch for the underside loops.
                case TexturePaintSeamPreset.DenimChainstitchHem:
                    s.rows.Add(Row(.23f));s.rows[0].color=new Color(.68f,.45f,.21f,1);s.rows[0].thickness=.014f;break;
                case TexturePaintSeamPreset.DoubleTurnHem:
                    s.roping=.12f;s.wear=.08f;s.relief=.018f;s.puckering=.12f;s.rows.Add(Row(.23f));s.rows[0].spacing=.15f;break;
                case TexturePaintSeamPreset.FlatFelled:
                    s.rows.Add(Row(-.23f));s.rows.Add(Row(.23f));s.roping=.18f;s.wear=.14f;s.relief=.02f;s.puckering=.18f;break;
                case TexturePaintSeamPreset.MockFelled:
                    s.profile=TexturePaintSeamProfile.Lapped;s.rows.Add(Row(-.1f));s.rows.Add(Row(.2f));s.relief=.015f;s.roping=.15f;s.wear=.1f;break;
                case TexturePaintSeamPreset.Lapped:
                    s.profile=TexturePaintSeamProfile.Lapped;s.rows.Add(Row(.065f));s.roping=.1f;s.relief=.018f;s.wear=.08f;s.puckering=.15f;break;
                case TexturePaintSeamPreset.PlainPressedOpen:
                    s.profile=TexturePaintSeamProfile.PressedOpen;s.roping=.08f;s.wear=.06f;s.relief=.018f;break;
                case TexturePaintSeamPreset.FrenchSeam:
                    s.profile=TexturePaintSeamProfile.EnclosedRidge;s.bandWidth=.25f;s.roping=.1f;s.wear=.04f;s.relief=.025f;break;
                case TexturePaintSeamPreset.BoundEdge:
                    s.rows.Add(Row(-.24f));s.rows.Add(Row(.24f));s.roping=.04f;s.wear=.06f;s.puckering=.1f;s.relief=.018f;break;
                case TexturePaintSeamPreset.Piping:
                    s.profile=TexturePaintSeamProfile.Piping;s.bandWidth=.24f;s.relief=.045f;s.roping=0;s.wear=.06f;break;
                case TexturePaintSeamPreset.OverlockedEdge:
                    s.profile=TexturePaintSeamProfile.Lapped;s.rows.Add(Row(0,TexturePaintStitchPattern.Overlock));s.rows[0].span=.4f;
                    s.rows[0].spacing=.12f;s.rows[0].thickness=.01f;s.relief=.014f;s.puckering=.1f;s.roping=0;s.wear=.04f;break;
                case TexturePaintSeamPreset.CoverstitchHem:
                    s.rows.Add(Row(-.16f));s.rows.Add(Row(.16f));s.roping=.08f;s.wear=.04f;s.puckering=.12f;s.relief=.018f;break;
                case TexturePaintSeamPreset.RolledHem:
                    s.profile=TexturePaintSeamProfile.Piping;s.bandWidth=.12f;s.rows.Add(Row(.024f));s.rows[0].thickness=.008f;
                    s.rows[0].spacing=.11f;s.relief=.018f;s.roping=.16f;s.wear=.04f;s.puckering=.1f;break;
                case TexturePaintSeamPreset.BlindHem:
                    s.roping=.05f;s.wear=.02f;s.relief=.016f;s.puckering=.08f;s.rows.Add(Row(.2f,TexturePaintStitchPattern.Blind));
                    s.rows[0].thickness=.006f;s.rows[0].spacing=.28f;s.rows[0].relief=.2f;break;
                case TexturePaintSeamPreset.RawFrayedHem:
                    s.profile=TexturePaintSeamProfile.RawEdge;s.fraying=.7f;s.roping=.08f;s.wear=.25f;s.relief=.012f;break;
                case TexturePaintSeamPreset.Topstitch:
                    s.profile=TexturePaintSeamProfile.None;s.roping=0;s.wear=0;s.puckering=.15f;s.rows.Add(Row(0));break;
                case TexturePaintSeamPreset.BarTack:
                    s.profile=TexturePaintSeamProfile.None;s.roping=0;s.wear=0;s.puckering=.12f;s.rows.Add(Row(0,TexturePaintStitchPattern.BarTack));
                    s.rows[0].spacing=.032f;s.rows[0].span=.4f;s.rows[0].thickness=.014f;s.rows[0].relief=.32f;break;
            }
            return s;
        }
    }
}
