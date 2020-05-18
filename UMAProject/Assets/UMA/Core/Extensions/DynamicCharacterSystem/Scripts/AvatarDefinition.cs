using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using UMA.CharacterSystem;
using UnityEngine;

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
public struct SharedColorDef
{
    public string name;
    public int count;
    public ColorDef[] channels;

    public SharedColorDef(string Name, int ChannelCount)
    {
        name = Name;
        count = ChannelCount;
        channels = new ColorDef[0];
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
    public int    val;

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
    }
}

[Serializable]
public struct AvatarDefinition 
{
    public string RaceName;
    public string[] Wardrobe;
    public SharedColorDef[] Colors;
    public DnaDef[] Dna;
    public string ToCompressedString()
    {
        StringBuilder theString = new StringBuilder();
        theString.Append("AA*");
        theString.Append("\n");
        theString.Append("R:");
        theString.Append(RaceName);
        theString.Append("\n");
        if (Wardrobe != null)
        {
            theString.Append("W:");
            foreach (string w in Wardrobe)
            {
                theString.Append(w);
                theString.Append(",");
            }
            theString.Append("\n");
        }

        if (Colors != null)
        {
            foreach(SharedColorDef scd in Colors)
            {
                theString.Append("C:");
                theString.Append(scd.name);
                theString.Append(',');
                theString.Append(scd.count);
                theString.Append('=');
                foreach (ColorDef c in scd.channels)
                {
                    theString.Append(c.chan);
                    theString.Append(',');
                    theString.Append(c.mCol.ToString("X"));
                    theString.Append(',');
                    theString.Append(c.aCol.ToString("X"));
                    theString.Append(';');
                }
                theString.Append("\n");
            }
        }

        if (Dna != null)
        {
            theString.Append("D:");
            foreach(DnaDef d in Dna)
            {
                theString.Append(d.Name);
                theString.Append('=');
                theString.Append(d.val);
                theString.Append(';');
            }
            theString.Append("\n");
        }
        return theString.ToString();
    }


    public static AvatarDefinition FromCompressedString(string compressed)
    {
        char[] splitter = new char[1];
        AvatarDefinition adf = new AvatarDefinition();
        string[] SplitLines = compressed.Split('\n');
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
                                        ColorDef cdef = new ColorDef(Convert.ToInt32(vals[0]), Convert.ToUInt32(vals[1],16), Convert.ToUInt32(vals[2],16));
                                        theColors.Add(cdef);
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
                    if (Dna.Length > 1)
                    {
                        List<DnaDef> theDna = new List<DnaDef>();
                        foreach (string d in Dna)
                        {
                            splitter[0] = '=';
                            string[] dnaval = d.Split(splitter, StringSplitOptions.RemoveEmptyEntries);
                            if (dnaval.Length > 1)
                            {
                                DnaDef newDna = new DnaDef(dnaval[0], Convert.ToInt32(dnaval[1]));
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
            bf.Serialize(ms,new BinaryDefinition(adf));
            return ms.ToArray();
        }
    }

    public AvatarDefinition FromBinary(byte[] bin, BinaryFormatter bf)
    {
        using (var memStream = new MemoryStream())
        {
            memStream.Write(bin, 0, bin.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            BinaryDefinition bdf = (BinaryDefinition) bf.Deserialize(memStream);
            return bdf.adf;
        }
    }
}


