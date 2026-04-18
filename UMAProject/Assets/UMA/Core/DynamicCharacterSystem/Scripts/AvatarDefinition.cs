using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using UMA;
using UnityEngine;

namespace UMA
{
    [Serializable]
    public struct ColorDef
    {
        public int chan; // What channel this is
        public uint mCol; // The multiplicative
        public uint aCol; // The additive

        public ColorDef(int Channels, uint MCol, uint ACol)
        {
            chan = Channels;
            mCol = MCol;
            aCol = ACol;
        }

        public static uint ToUInt(Color32 color)
        {
            return (uint)((color.a << 24) | (color.r << 16) |
                          (color.g << 8) | (color.b << 0));
        }
        public static Color32 ToColor(uint color)
        {
            byte a = (byte)((color >> 24) & 0xff);
            byte r = (byte)((color >> 16) & 0xff);
            byte g = (byte)((color >> 8) & 0xff);
            byte b = (byte)((color >> 0) & 0xff);
            return new Color32(r, g, b, a);
        }
    }

    [Serializable]
    public struct ShaderParmDef
    {
        public string name;
        public int type;
        public uint value;   // could be a float, int, or color
    }

    [Serializable]
    public struct SharedColorDef
    {
        public string name;
        public int count;
        public ColorDef[] channels;
        public string[] shaderParms;

        public SharedColorDef(string Name, int ChannelCount)
        {
            name = Name;
            count = ChannelCount;
            channels = new ColorDef[0];
            shaderParms = new string[0];
        }

        public void SetChannels(ColorDef[] Channels)
        {
            channels = Channels;
        }
    }

    [Serializable]
    public struct DnaDef
    {
        public string Name;
        public int val;

        public DnaDef(string name, float value)
        {
            Name = name;
            val = Convert.ToInt16(value * 10000);
        }
        public DnaDef(string name, int value)
        {
            Name = name;
            val = value;
        }

        public float Value
        {
            get
            {
                return ((float)val) / 10000;
            }
            set
            {
                val = Convert.ToInt16(value * 10000);
            }
        }
    }

    [Serializable]
    public struct AvatarDefinition
    {
        public string RaceName;
        public string[] Wardrobe;
        public SharedColorDef[] Colors;
        public DnaDef[] Dna;

        #region Setters
        public void SetColors(OverlayColorData[] CurrentColors)
        {
            List<SharedColorDef> newColors = new List<SharedColorDef>();

            for (int i1 = 0; i1 < CurrentColors.Length; i1++)
            {
                OverlayColorData col = CurrentColors[i1];
                SharedColorDef scd = new SharedColorDef(col.name, col.channelCount);
                List<ColorDef> colorchannels = new List<ColorDef>();

                if (col.PropertyBlock != null)
                {
                    List<string> shaderParms = new List<string>();
                    foreach (UMAProperty prop in col.PropertyBlock.shaderProperties)
                    {
                        string property = prop.ToString();
                    }
                    scd.shaderParms = shaderParms.ToArray();
                }

                for (int i = 0; i < col.channelCount; i++)
                {
                    if (!col.isDefault(i))
                    {
                        Color Mask = col.channelMask[i];
                        Color Additive = col.channelAdditiveMask[i];
                        colorchannels.Add(new ColorDef(i, ColorDef.ToUInt(Mask), ColorDef.ToUInt(Additive)));
                    }
                }
                if (colorchannels.Count > 0)
                {
                    scd.SetChannels(colorchannels.ToArray());
                    newColors.Add(scd);
                }
            }
            Colors = newColors.ToArray();
        }

        public void SetDefaultColors(string[] colorNames, uint[] colors)
        {
            if (colorNames.Length != colors.Length)
            {
                Debug.LogError("Color lengths must match");
                return;
            }
            List<SharedColorDef> sharedcolors = new List<SharedColorDef>();
            for (int i = 0; i < colorNames.Length; i++)
            {
                SharedColorDef scd = new SharedColorDef(colorNames[i], 1);
                ColorDef col = new ColorDef(1, colors[i], 0);
                scd.channels = new ColorDef[1];
                scd.channels[0] = col;
            }
            Colors = sharedcolors.ToArray();
        }

        public void SetDNA(UMAPredefinedDNA dna)
        {
            List<DnaDef> defs = new List<DnaDef>();
            for (int i = 0; i < dna.PreloadValues.Count; i++)
            {
                DnaValue d = dna.PreloadValues[i];
                defs.Add(new DnaDef(d.Name, d.Value));
            }
            Dna = defs.ToArray();
        }

        // No Garbage Version
        public void SetDNA(DnaValue[] dna)
        {
            Dna = new DnaDef[dna.Length];
            for (int i = 0; i < dna.Length; i++)
            {
                Dna[i].Name = dna[i].Name;
                Dna[i].Value = dna[i].Value;
            }
        }

        public void SetDNA(string[] names, float[] values)
        {
            if (names.Length != values.Length)
            {
                Debug.LogError("SetDNA: length of names and values must match.");
                return;
            }

            Dna = new DnaDef[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                Dna[i].Name = names[i];
                Dna[i].Value = values[i];
            }
        }

        #endregion


        public string ToCompressedString(string seperator = "\n")
        {
            StringBuilder theString = new StringBuilder();
            theString.Append("BB*");
            theString.Append(seperator);
            theString.Append("R:");
            theString.Append(RaceName);
            theString.Append(seperator);
            if (Wardrobe != null)
            {
                theString.Append("W:");
                for (int i = 0; i < Wardrobe.Length; i++)
                {
                    string w = Wardrobe[i];
                    theString.Append(w);
                    theString.Append(",");
                }
                theString.Append(seperator);
            }

            if (Colors != null)
            {
                for (int i = 0; i < Colors.Length; i++)
                {
                    SharedColorDef scd = Colors[i];
                    theString.Append("C:");
                    theString.Append(scd.name);
                    theString.Append(',');
                    theString.Append(scd.count);
                    theString.Append('>');
                    for (int i1 = 0; i1 < scd.channels.Length; i1++)
                    {
                        ColorDef c = scd.channels[i1];
                        theString.Append(c.chan);
                        theString.Append(',');
                        theString.Append(c.mCol.ToString("X"));
                        if (c.aCol != 0)
                        {
                            theString.Append(',');
                            theString.Append(c.aCol.ToString("X"));
                        }
                        theString.Append(';');
                    }
                    if (scd.shaderParms != null)
                    {
                        for (int i2 = 0; i2 < scd.shaderParms.Length; i2++)
                        {
                            theString.Append("P:");
                            theString.Append(Base64Encode(scd.shaderParms[i2]));
                            theString.Append(';');
                        }
                    }
                    theString.Append('<');
                }
                theString.Append(seperator);
            }

            if (Dna != null)
            {
                theString.Append("D:");
                for (int i = 0; i < Dna.Length; i++)
                {
                    DnaDef d = Dna[i];
                    theString.Append(d.Name);
                    theString.Append('=');
                    theString.Append(d.val.ToString("X"));
                    theString.Append(';');
                }
                theString.Append(seperator);
            }
            return theString.ToString();
        }

        public static AvatarDefinition FromCompressedString(string compressed, char seperator = '\n')
        {
            if (compressed.StartsWith("AA*"))
            {
                return FromCompressedStringV1(compressed.Substring(3), seperator);
            }
            else
            {
                return FromCompressedStringV2(compressed.Substring(3), seperator);
            }
        }
        public static AvatarDefinition FromCompressedStringV1(string compressed, char seperator = '\n')
        {
            char[] splitter = new char[1];
            AvatarDefinition adf = new AvatarDefinition();
            splitter[0] = seperator;
            string[] SplitLines = compressed.Split(splitter);
            List<SharedColorDef> Colors = new List<SharedColorDef>();

            foreach (string s in SplitLines)
            {
                if (String.IsNullOrEmpty(s)) continue;
                switch (s[0])
                {
                    case 'R':
                        // Unpack Race
                        adf.RaceName = s.Substring(2).Trim();
                        break;
                    case 'W':
                        // Unpack Wardrobe
                        splitter[0] = ',';
                        adf.Wardrobe = s.Substring(2).Trim().Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                        break;
                    case 'C':
                        // Unpack Colors
                        splitter[0] = '=';
                        string[] SharedColor = s.Substring(2).Trim().Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                        if (SharedColor.Length > 1)
                        {
                            SharedColorDef scd = new SharedColorDef();
                            splitter[0] = ',';
                            string[] maincol = SharedColor[0].Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                            if (maincol.Length > 1)
                            {
                                scd.name = maincol[0];
                                scd.count = Convert.ToInt32(maincol[1]);

                                splitter[0] = ';';
                                string[] ColorDefs = SharedColor[1].Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                                List<ColorDef> theColors = new List<ColorDef>();
                                if (ColorDefs != null)
                                {
                                    if (ColorDefs.Length > 0)
                                    {
                                        foreach (string c in ColorDefs)
                                        {
                                            splitter[0] = ',';
                                            string[] vals = c.Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                                            if (vals.Length == 2)
                                            {
                                                ColorDef cdef = new ColorDef(Convert.ToInt32(vals[0]), Convert.ToUInt32(vals[1], 16), 0);
                                                theColors.Add(cdef);
                                            }
                                            else if (vals.Length == 3)
                                            {
                                                ColorDef cdef = new ColorDef(Convert.ToInt32(vals[0]), Convert.ToUInt32(vals[1], 16), Convert.ToUInt32(vals[2], 16));
                                                theColors.Add(cdef);
                                            }
                                        }
                                    }

                                }
                                scd.channels = theColors.ToArray();
                                Colors.Add(scd);
                            }
                        }
                        break;
                    case 'D':
                        // Unpack DNA
                        splitter[0] = ';';
                        string[] Dna = s.Substring(2).Trim().Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                        if (Dna.Length > 0)
                        {
                            List<DnaDef> theDna = new List<DnaDef>();
                            foreach (string d in Dna)
                            {
                                splitter[0] = '=';
                                string[] dnaval = d.Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                                if (dnaval.Length > 1)
                                {
                                    DnaDef newDna = new DnaDef(dnaval[0], Convert.ToInt32(dnaval[1], 16));
                                    theDna.Add(newDna);
                                }
                            }
                            adf.Dna = theDna.ToArray();
                        }
                        break;
                }
            }

            adf.Colors = Colors.ToArray();
            return adf;
        }

        public static AvatarDefinition FromCompressedStringV2(string compressed, char seperator = '\n')
        {
            char[] splitter = new char[1];
            AvatarDefinition adf = new AvatarDefinition();
            splitter[0] = seperator;
            string[] SplitLines = compressed.Split(splitter);
            //  List<SharedColorDef> Colors = new List<SharedColorDef>();

            for (int i = 0; i < SplitLines.Length; i++)
            {
                string s = SplitLines[i];
                if (String.IsNullOrEmpty(s))
                {
                    continue;
                }

                switch (s[0])
                {
                    case 'R':
                        // Unpack Race
                        adf.RaceName = s.Substring(2).Trim();
                        break;
                    case 'W':
                        // Unpack Wardrobe
                        splitter[0] = ',';
                        adf.Wardrobe = s.Substring(2).Trim().Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                        break;
                    case 'C':
                        // Unpack Colors
                        adf.Colors = UnpackColors(s);
                        break;
                    case 'D':
                        // Unpack DNA
                        splitter[0] = ';';
                        string[] Dna = s.Substring(2).Trim().Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                        if (Dna.Length > 0)
                        {
                            List<DnaDef> theDna = new List<DnaDef>();
                            for (int i1 = 0; i1 < Dna.Length; i1++)
                            {
                                string d = Dna[i1];
                                splitter[0] = '=';
                                string[] dnaval = d.Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                                if (dnaval.Length > 1)
                                {
                                    DnaDef newDna = new DnaDef(dnaval[0], Convert.ToInt32(dnaval[1], 16));
                                    theDna.Add(newDna);
                                }
                            }
                            adf.Dna = theDna.ToArray();
                        }
                        break;
                }
            }

            //adf.Colors = Colors.ToArray();
            return adf;
        }

        private static SharedColorDef[] UnpackColors(string s)
        {
            List<SharedColorDef> colors = new List<SharedColorDef>();

            string[] encodedColors = s.Split(new char[] { '<' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < encodedColors.Length; i++)
            {
                UnpackAColor(colors, encodedColors[i]);
            }

            return colors.ToArray();
        }

        private static void UnpackAColor(List<SharedColorDef> Colors, string s)
        {
            char[] splitter = { '>' };
            string[] SharedColor = s.Substring(2).Trim().Split(splitter, StringSplitOptions.RemoveEmptyEntries);
            if (SharedColor.Length > 1)
            {
                SharedColorDef scd = new SharedColorDef();
                List<string> ShaderParms = new List<string>();
                splitter[0] = ',';
                string[] maincol = SharedColor[0].Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                if (maincol.Length > 1)
                {
                    scd.name = maincol[0];
                    scd.count = Convert.ToInt32(maincol[1]);

                    splitter[0] = ';';
                    string[] ColorDefs = SharedColor[1].Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                    List<ColorDef> theColors = new List<ColorDef>();
                    if (ColorDefs != null)
                    {
                        if (ColorDefs.Length > 0)
                        {
                            for (int i1 = 0; i1 < ColorDefs.Length; i1++)
                            {
                                if (String.IsNullOrEmpty(ColorDefs[i1]))
                                    continue;
                                if (ColorDefs[i1][0] == 'P')
                                {
                                    ShaderParms.Add(Base64Decode(ColorDefs[i1].Substring(2)));
                                    continue;
                                }
                                string c = ColorDefs[i1];
                                splitter[0] = ',';
                                string[] vals = c.Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                                if (vals.Length == 2)
                                {
                                    ColorDef cdef = new ColorDef(Convert.ToInt32(vals[0]), Convert.ToUInt32(vals[1], 16), 0);
                                    theColors.Add(cdef);
                                }
                                else if (vals.Length == 3)
                                {
                                    ColorDef cdef = new ColorDef(Convert.ToInt32(vals[0]), Convert.ToUInt32(vals[1], 16), Convert.ToUInt32(vals[2], 16));
                                    theColors.Add(cdef);
                                }
                            }
                        }

                    }
                    scd.channels = theColors.ToArray();
                    scd.shaderParms = ShaderParms.ToArray();
                    Colors.Add(scd);
                }
            }
        }

        // Ascii version of the string. Not as good as binary formatter,
        // but 1/2 the size of a string.
        public byte[] ToASCIIString()
        {
            return Encoding.ASCII.GetBytes(ToCompressedString());
        }

        public static AvatarDefinition FromASCIIString(byte[] asciiString)
        {
            return FromCompressedString(Encoding.ASCII.GetString(asciiString));
        }
        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

    }

    [Serializable]
    public class BinaryDefinition
    {
        public AvatarDefinition adf;

        public BinaryDefinition(AvatarDefinition Adf)
        {
            adf = Adf;
        }

        /// <summary>
        /// This is likely not compatible cross platform
        /// </summary>
        /// <param name="bf"></param>
        /// <returns></returns>
        public static byte[] ToBinary(BinaryFormatter bf, AvatarDefinition adf)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                bf.Serialize(ms, new BinaryDefinition(adf));
                return ms.ToArray();
            }
        }

        public AvatarDefinition FromBinary(byte[] bin, BinaryFormatter bf)
        {
            using (var memStream = new MemoryStream())
            {
                memStream.Write(bin, 0, bin.Length);
                memStream.Seek(0, SeekOrigin.Begin);
                BinaryDefinition bdf = (BinaryDefinition)bf.Deserialize(memStream);
                return bdf.adf;
            }
        }
    }
}

