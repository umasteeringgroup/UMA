using UnityEngine;
using System;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// Overlay color data.
    /// </summary>
    [System.Serializable]
	public class OverlayColorData :  System.IEquatable<OverlayColorData>
	{
		public static int currentinstance = 0;
		[NonSerialized]
		public int instance;
		public static Color EmptyAdditive = new Color(0, 0, 0, 0);

		public const string UNSHARED = "-";
		public string name;
		[ColorUsage(true, true)]
		public Color[] channelMask = new Color[0];
		public Color[] channelAdditiveMask = new Color[0];
		public UMAMaterialPropertyBlock PropertyBlock; // may be null.
#if UNITY_EDITOR
		public bool foldout;
		public bool showAdvanced;
		public bool isBaseColor = false;
		public bool deleteThis = false;
		public bool colorsExpanded = false;
		public bool propertiesExpanded = false;
#endif
        public Color displayColor = Color.white;

        public Color color
		{
			get
			{
				if (channelMask.Length < 1)
                {
                    return Color.white;
                }

                return channelMask[0];
			}
			set
			{
				if (channelMask.Length > 0)
                {
                    channelMask[0] = value;
                }
            }
		}

		public Color Add
        {
			get
            {
				if (channelAdditiveMask.Length < 1)
                {
                    return EmptyAdditive;
                }

                return channelAdditiveMask[0];
            }
        }


		public Color GetTint(int channel)
		{
			if (channel < channelMask.Length)
            {
                return channelMask[channel];
            }

            return Color.white;
		}

		public Color GetAdditive(int channel)
        {
			if (channel < channelAdditiveMask.Length)
            {
                return channelAdditiveMask[channel];
            }

            return EmptyAdditive;
		}

		public int channelCount { get { return channelMask.Length; } }
		public bool isDefault(int Channel)
		{
			if (Channel <= channelCount)
			{
				uint multiply = ColorDef.ToUInt(channelMask[Channel]);
				uint additive = ColorDef.ToUInt(channelAdditiveMask[Channel]);

				if (multiply == 0xffffffff && additive == 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Default constructor
		/// </summary>
		public OverlayColorData()
		{
			instance = currentinstance++;
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
			res.displayColor = displayColor;
#if UNITY_EDITOR
			res.foldout = foldout;
			res.isBaseColor = isBaseColor;
#endif

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
			if (PropertyBlock != null)
            {
				res.PropertyBlock = new UMAMaterialPropertyBlock();
				res.PropertyBlock.alwaysUpdate = PropertyBlock.alwaysUpdate;
				res.PropertyBlock.alwaysUpdateParms = PropertyBlock.alwaysUpdateParms;
				res.PropertyBlock.shaderProperties = new List<UMAProperty>(PropertyBlock.shaderProperties.Count);
				for(int i=0;i<PropertyBlock.shaderProperties.Count;i++)
                {
					UMAProperty up = PropertyBlock.shaderProperties[i];
					if (up != null)
					{
						res.PropertyBlock.shaderProperties.Add(up.Clone());
					}
                }
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
            if (HasName() && name != UNSHARED)
                {
                    return true;
                }

                return false;
         }
      }


		public bool isValid
        {
			get
            {
				return PropertyBlock == null && channelMask.Length == 0;
            }
        }

		public bool HasColors
        {
			get
            {
				return channelMask.Length > 0;
            }
        }

		public bool HasPropertyBlock
        {
			get
            {
				return PropertyBlock != null;
            }
        }
		public bool HasProperties
        {
			get
            {
				if (PropertyBlock == null)
                {
                    return false;
                }

                return PropertyBlock.shaderProperties.Count > 0;
            }
        }

		public bool isOnlyColors
        {
			get
            {
				return PropertyBlock == null && channelMask.Length > 0;
            }
        }

		public bool isOnlyProperties
        {
			get
            {
				return PropertyBlock != null && channelMask.Length > 0;
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
					if (cd1.displayColor != cd2.displayColor)
					{
						return false;
					}
					if (cd2.channelMask.Length != cd1.channelMask.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < cd1.channelMask.Length; i++)
					{
						if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i]))
                        {
                            return false;
                        }
                    }

					for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
					{
						if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i]))
                        {
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
					if (cd1.displayColor != cd2.displayColor)
					{
                        return true;
                    }
					if (cd2.channelMask.Length != cd1.channelMask.Length)
                    {
                        return true;
                    }

                    for (int i = 0; i < cd1.channelMask.Length; i++)
					{
						if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i]))
                        {
                            return true;
                        }
                    }
					for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
					{
						if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i]))
                        {
                            return true;
                        }
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

		public void EnsureChannelsExact(int ChannelCount)
		{
			if (channelMask == null)
			{
				channelMask = new Color[ChannelCount];
				channelAdditiveMask = new Color[ChannelCount];
			}

            if (channelMask.Length != ChannelCount)
			{
                Array.Resize(ref channelMask, ChannelCount);
                Array.Resize(ref channelAdditiveMask, ChannelCount);
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
			dest.displayColor = displayColor;
			dest.PropertyBlock = new UMAMaterialPropertyBlock();
			if (PropertyBlock != null)
			{
                dest.PropertyBlock.alwaysUpdate = PropertyBlock.alwaysUpdate;
				dest.PropertyBlock.alwaysUpdateParms = PropertyBlock.alwaysUpdateParms;
                dest.PropertyBlock.shaderProperties = new List<UMAProperty>(PropertyBlock.shaderProperties.Count);
                for (int i = 0; i < PropertyBlock.shaderProperties.Count; i++)
				{
                    UMAProperty up = PropertyBlock.shaderProperties[i];
                    dest.PropertyBlock.shaderProperties.Add(up.Clone());
                }
            }
#if UNITY_EDITOR
            dest.isBaseColor = isBaseColor;
#endif
		}
		public void AssignFrom(OverlayColorData src, bool CopyParmsOnly=false)
		{
            // If CopyParmsOnly is false, then we need to copy the colors and the property block.
            // If it is true, then we are copying the property block only.
            if (CopyParmsOnly == false)
			{
                // Copy the colors.
#if UNITY_EDITOR
                isBaseColor = src.isBaseColor;
#endif
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

			displayColor = src.displayColor;
			PropertyBlock = new UMAMaterialPropertyBlock();
			if (src.PropertyBlock != null)
			{
				PropertyBlock.alwaysUpdate = src.PropertyBlock.alwaysUpdate;
				PropertyBlock.alwaysUpdateParms = src.PropertyBlock.alwaysUpdateParms;
				PropertyBlock.shaderProperties = new List<UMAProperty>(src.PropertyBlock.shaderProperties.Count);
				for (int i = 0; i < src.PropertyBlock.shaderProperties.Count; i++)
				{
					UMAProperty up = src.PropertyBlock.shaderProperties[i];
					if (up != null)
					{
						PropertyBlock.shaderProperties.Add(up.Clone());
					}
				}
			}
        }
    }
}
