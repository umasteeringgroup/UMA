using UnityEngine;
using System;
using System.Collections;


namespace UMA
{
	[System.Serializable]
	public class OverlayColorData : System.IEquatable<OverlayColorData>
	{
		public const string UNSHARED = "-";
		public string name;
		public Color[] channelMask;
		public Color[] channelAdditiveMask;
		public Color color { get { return channelMask[0]; } set { channelMask[0] = value; } }

		public OverlayColorData()
		{
		}

		public OverlayColorData(int channels)
		{
			channelMask = new Color[channels];
			channelAdditiveMask = new Color[channels];
			for(int i= 0; i < channels; i++ )
			{
				channelMask[i] = Color.white;
				channelAdditiveMask[i] = new Color(0,0,0,0);
			}
		}

		public OverlayColorData Duplicate()
		{
			var res = new OverlayColorData();
			res.channelMask = new Color[channelMask.Length];
			for (int i = 0; i < channelMask.Length; i++)
			{
				res.channelMask[i] = channelMask[i];
			}
			res.channelAdditiveMask = new Color[channelAdditiveMask.Length];
			for (int i = 0; i < channelAdditiveMask.Length; i++)
			{
				res.channelAdditiveMask[i] = channelAdditiveMask[i];
			}
			return res;
		}

		public OverlayColorData Copy()
		{
			var res = new OverlayColorData();
			if (name != null)
			{
				res.name = String.Copy(name);
			}
			res.channelMask = new Color[channelMask.Length];
			for (int i = 0; i < channelMask.Length; i++)
			{
				res.channelMask[i] = channelMask[i];
			}
			res.channelAdditiveMask = new Color[channelAdditiveMask.Length];
			for (int i = 0; i < channelAdditiveMask.Length; i++)
			{
				res.channelAdditiveMask[i] = channelAdditiveMask[i];
			}
			return res;
		}

		public bool HasName()
		{
			return ((name != null) && (name.Length > 0));
		}
		
		public static bool SameColor(Color color1, Color color2)
		{
			return ((color1.r == color2.r) &&
			        (color1.g == color2.g) &&
			        (color1.b == color2.b) &&
			        (color1.a == color2.a));
		}
		public static bool DifferentColor(Color color1, Color color2)
		{
			return ((color1.r != color2.r) ||
			        (color1.g != color2.g) ||
			        (color1.b != color2.b) ||
			        (color1.a != color2.a));
		}

		public static implicit operator bool(OverlayColorData obj) 
		{
			return ((System.Object)obj) != null;
		}

		public bool Equals(OverlayColorData other)
		{
			return (this == other);
		}
		public override bool Equals(object other)
		{
			return Equals(other as OverlayColorData);
		}
		
		public static bool operator == (OverlayColorData cd1, OverlayColorData cd2)
		{
			if (cd1)
			{
				if (cd2)
				{
					for (int i = 0; i < cd1.channelMask.Length; i++)
					{
						if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i]))
							return false;
					}

					for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
					{
						if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i]))
							return false;
					}
					return true;
				}
				return false;
			}

			return (!(bool)cd2);
		}
		public static bool operator != (OverlayColorData cd1, OverlayColorData cd2)
		{
			if (cd1)
			{
				if (cd2)
				{
					for (int i = 0; i < cd1.channelMask.Length; i++)
					{
						if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i]))
							return true;
					}
					for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
					{
						if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i]))
							return true;
					}
					
					return false;
				}
				return true;
			}
			
			return ((bool)cd2);
		}
	}
}
