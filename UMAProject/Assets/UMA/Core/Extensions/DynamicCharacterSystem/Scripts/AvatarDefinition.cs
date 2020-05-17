using System;
using System.Collections;
using System.Collections.Generic;
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
}
