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
		public Color32 color;

		public Color32[] channelMask;
		public Color32[] channelAdditiveMask;

		public OverlayColorData Duplicate()
		{
			var res = new OverlayColorData();
			res.color = color;
			if (channelMask != null)
			{
				res.channelMask = new Color32[channelMask.Length];
				for (int i = 0; i < channelMask.Length; i++)
				{
					res.channelMask[i] = channelMask[i];
				}
			}
			if (channelAdditiveMask != null)
			{
				res.channelAdditiveMask = new Color32[channelAdditiveMask.Length];
				for (int i = 0; i < channelAdditiveMask.Length; i++)
				{
					res.channelAdditiveMask[i] = channelAdditiveMask[i];
				}
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
			res.color = new Color32(color.r, color.g, color.b, color.a);
			Color32 maskColor;
			if (channelMask != null)
			{
				res.channelMask = new Color32[channelMask.Length];
				for (int i = 0; i < channelMask.Length; i++)
				{
					maskColor = channelMask[i];
					res.channelMask[i] = new Color32(maskColor.r, maskColor.g, maskColor.b, maskColor.a);
				}
			}
			if (channelAdditiveMask != null)
			{
				res.channelAdditiveMask = new Color32[channelAdditiveMask.Length];
				for (int i = 0; i < channelAdditiveMask.Length; i++)
				{
					maskColor = channelMask[i];
					res.channelAdditiveMask[i] = new Color32(maskColor.r, maskColor.g, maskColor.b, maskColor.a);
				}
			}
			return res;
		}

		public bool HasName()
		{
			return ((name != null) && (name.Length > 0));
		}
		
		public static bool SameColor(Color32 color1, Color32 color2)
		{
			return ((color1.r == color2.r) &&
			        (color1.g == color2.g) &&
			        (color1.b == color2.b) &&
			        (color1.a == color2.a));
		}
		public static bool DifferentColor(Color32 color1, Color32 color2)
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
					if (DifferentColor(cd1.color, cd2.color))
						return false;

					bool emptyArray1 = ((cd1.channelMask == null) || (cd1.channelMask.Length == 0));
					bool emptyArray2 = ((cd2.channelMask == null) || (cd2.channelMask.Length == 0));
					if (emptyArray1 != emptyArray2)
						return false;
					if (!emptyArray1)
					{
						for (int i = 0; i < cd1.channelMask.Length; i++)
						{
							if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i]))
								return false;
						}
					}

					emptyArray1 = ((cd1.channelAdditiveMask == null) || (cd1.channelAdditiveMask.Length == 0));
					emptyArray2 = ((cd2.channelAdditiveMask == null) || (cd2.channelAdditiveMask.Length == 0));
					if (emptyArray1 != emptyArray2)
						return false;
					if (!emptyArray1)
					{

						for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
						{
							if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i]))
								return false;
						}
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
					if (DifferentColor(cd1.color, cd2.color))
						return true;
					
					bool emptyArray1 = ((cd1.channelMask == null) || (cd1.channelMask.Length == 0));
					bool emptyArray2 = ((cd2.channelMask == null) || (cd2.channelMask.Length == 0));
					if (emptyArray1 != emptyArray2)
						return true;
					emptyArray1 = ((cd1.channelAdditiveMask == null) || (cd1.channelAdditiveMask.Length == 0));
					emptyArray2 = ((cd2.channelAdditiveMask == null) || (cd2.channelAdditiveMask.Length == 0));
					if (emptyArray1 != emptyArray2)
						return true;
					
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
