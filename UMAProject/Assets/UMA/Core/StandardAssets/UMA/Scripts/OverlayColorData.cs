using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Overlay color data.
	/// </summary>
	[System.Serializable]
	public class OverlayColorData : System.IEquatable<OverlayColorData>
	{
		public static Color EmptyAdditive = new Color(0, 0, 0, 0);

		public const string UNSHARED = "-";
		public string name;
		public Color[] channelMask;
		public Color[] channelAdditiveMask;
		public Color color { get { return channelMask[0]; } set { channelMask[0] = value; } }
		public int channelCount { get { return channelMask.Length; } }
		public bool isDefault(int Channel)
		{
			if (Channel <= channelCount)
			{
				if (channelMask[Channel] == Color.white)
				{
					if (channelAdditiveMask[Channel] == EmptyAdditive)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Default constructor
		/// </summary>
		public OverlayColorData()
		{
		}

		/// <summary>
		/// Constructor for a given number of channels.
		/// </summary>
		/// <param name="channels">Channels.</param>
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

		/// <summary>
		/// Deep copy of the OverlayColorData.
		/// </summary>
		public OverlayColorData Duplicate()
		{
			var res = new OverlayColorData();
			res.name = name;
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

      /// <summary>
      /// This needs to be better
      /// </summary>
      /// <returns><c>true</c> if this instance is A shared color; otherwise, <c>false</c>.</returns>
      public bool IsASharedColor
      {
         get
         {
            if (HasName() && name != UNSHARED) return true;
            return false;
         }
      }

		/// <summary>
		/// Does the OverlayColorData have a valid name?
		/// </summary>
		/// <returns><c>true</c> if this instance has a valid name; otherwise, <c>false</c>.</returns>
		public bool HasName()
		{
			return ((name != null) && (name.Length > 0));
		}

		/// <summary>
		/// Are two Unity Colors the same?
		/// </summary>
		/// <returns><c>true</c>, if colors are identical, <c>false</c> otherwise.</returns>
		/// <param name="color1">Color1.</param>
		/// <param name="color2">Color2.</param>
		public static bool SameColor(Color color1, Color color2)
		{
			return (Mathf.Approximately(color1.r, color2.r) &&
					Mathf.Approximately(color1.g, color2.g) &&
					Mathf.Approximately(color1.b, color2.b) &&
					Mathf.Approximately(color1.a, color2.a));
		}
		/// <summary>
		/// Are two Unity Colors different?
		/// </summary>
		/// <returns><c>true</c>, if colors are different, <c>false</c> otherwise.</returns>
		/// <param name="color1">Color1.</param>
		/// <param name="color2">Color2.</param>
		public static bool DifferentColor(Color color1, Color color2)
		{
			return (!Mathf.Approximately(color1.r, color2.r) ||
					!Mathf.Approximately(color1.g, color2.g) ||
					!Mathf.Approximately(color1.b, color2.b) ||
			        !Mathf.Approximately(color1.a, color2.a));
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
					if (cd2.channelMask.Length != cd1.channelMask.Length) return false;
						
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
					if (cd2.channelMask.Length != cd1.channelMask.Length) return true;
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

		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public void SetChannels(int channels)
		{
			EnsureChannels(channels);
			if (channelMask.Length > channels)
			{
				Array.Resize(ref channelMask, channels);
				Array.Resize(ref channelAdditiveMask, channels);
			}
		}

        public void EnsureChannels(int channels)
        {
			if (channelMask == null)
            {
				   channelMask = new Color[channels];
				   channelAdditiveMask = new Color[channels];
                for (int i = 0; i < channels; i++)
                {
					   channelMask[i] = Color.white;
					   channelAdditiveMask[i] = new Color(0, 0, 0, 0);
                }
            }
            else
            {
               if (channelMask.Length < channels)
               {
                  var newMask = new Color[channels];
                  System.Array.Copy(channelMask, newMask, channelMask.Length);
                  for (int i = channelMask.Length; i < channels; i++)
                  {
                     newMask[i] = Color.white;
                  }
                  channelMask = newMask;
               }
               if (channelAdditiveMask.Length < channels)
               {
                  var newAdditiveMask = new Color[channels];
                  System.Array.Copy(channelAdditiveMask, newAdditiveMask, channelAdditiveMask.Length);
                  for (int i = channelAdditiveMask.Length; i < channels; i++)
                  {
                     newAdditiveMask[i] = new Color(0,0,0,0);
                  }
                  channelAdditiveMask = newAdditiveMask;
               }
            }
        }

		public void AssignTo(OverlayColorData dest)
		{
			if (name != null)
			{
				dest.name = String.Copy(name);
			}
			dest.channelMask = new Color[channelMask.Length];
			for (int i = 0; i < channelMask.Length; i++)
			{
				dest.channelMask[i] = channelMask[i];
			}
			dest.channelAdditiveMask = new Color[channelAdditiveMask.Length];
			for (int i = 0; i < channelAdditiveMask.Length; i++)
			{
				dest.channelAdditiveMask[i] = channelAdditiveMask[i];
			}			
		}
		public void AssignFrom(OverlayColorData src)
		{
			if (src.name != null)
			{
				name = String.Copy(src.name);
			}
			EnsureChannels(src.channelMask.Length);
			for (int i = 0; i < src.channelMask.Length; i++)
			{
				channelMask[i] = src.channelMask[i];
			}
			for (int i = 0; i < src.channelAdditiveMask.Length; i++)
			{
				channelAdditiveMask[i] = src.channelAdditiveMask[i];
			}
		}
	}
}
