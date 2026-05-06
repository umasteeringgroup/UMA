using UnityEngine;
using System;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// Overlay color data.
    /// </summary>
    [System.Serializable]
    public class OverlayColorData : System.IEquatable<OverlayColorData>
    {
        // Shared constant; avoids re-alloc per instance
        public static readonly Color EmptyAdditive = new Color(0, 0, 0, 0);

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
        public bool moveUpThis = false;
        public bool moveDownThis = false;
        public bool colorsExpanded = false;
        public bool propertiesExpanded = false;
        public bool showSelected = false;
        public bool isSelected = false;
        public bool showDisplayColor = false;
#endif
        public Color displayColor = Color.white;

        #region Property Helpers

        public T GetProperty<T>(string parameterName) where T : UMAProperty
        {
            if (PropertyBlock == null)
            {
                PropertyBlock = new UMAMaterialPropertyBlock();
            }
            if (PropertyBlock.shaderProperties == null)
            {
                PropertyBlock.shaderProperties = new List<UMAProperty>();
            }
            var value = PropertyBlock.GetProperty<T>(parameterName);
            if (value == null)
            {
                value = Activator.CreateInstance<T>();
                value.name = parameterName;
                PropertyBlock.shaderProperties.Add(value);
            }
            return value;
        }

        public void SetColorProperty(string parameterName, Color color)
        {
            var prop = GetProperty<UMAColorProperty>(parameterName);
            prop.Value = color;
        }

        public void SetFloatProperty(string parameterName, float value)
        {
            var prop = GetProperty<UMAFloatProperty>(parameterName);
            prop.Value = value;
        }

        public void SetVectorProperty(string parameterName, Vector4 value)
        {
            var prop = GetProperty<UMAVectorProperty>(parameterName);
            prop.Value = value;
        }

        // Preferred casing
        public void SetIntProperty(string parameterName, int value)
        {
            var prop = GetProperty<UMAIntProperty>(parameterName);
            prop.Value = value;
        }

        // Backward-compat wrapper
        public void setIntProperty(string parameterName, int value) => SetIntProperty(parameterName, value);

        public void SetTextureProperty(string parameterName, Texture value)
        {
            var prop = GetProperty<UMATextureProperty>(parameterName);
            prop.Value = value;
        }

        public void SetOverlayTransformProperty(string parameterName, float rotate, Vector2 translate, Vector2 scale)
        {
            var prop = GetProperty<UMAOverlayTransformProperty>(parameterName);
            prop.Rotate = rotate;
            prop.Translate = translate;
            prop.Scale = scale;
        }

        public void ClearProperties()
        {
            if (PropertyBlock != null && PropertyBlock.shaderProperties != null)
            {
                PropertyBlock.shaderProperties.Clear();
            }
        }

        #endregion

        public Color color
        {
            get
            {
                if (channelMask.Length < 1) return Color.white;
                return channelMask[0];
            }
            set
            {
                if (channelMask.Length > 0) channelMask[0] = value;
            }
        }

        public Color Add
        {
            get
            {
                if (channelAdditiveMask.Length < 1) return EmptyAdditive;
                return channelAdditiveMask[0];
            }
        }

        public Color GetTint(int channel)
        {
            if ((uint)channel < (uint)channelMask.Length) return channelMask[channel];
            return Color.white;
        }

        public Color GetAdditive(int channel)
        {
            if ((uint)channel < (uint)channelAdditiveMask.Length) return channelAdditiveMask[channel];
            return EmptyAdditive;
        }

        public int channelCount { get { return channelMask != null ? channelMask.Length : 0; } }

        public bool isDefault(int Channel)
        {
            // Guard both arrays and index strictly within bounds
            if (Channel >= 0 && Channel < channelCount && Channel < channelAdditiveMask.Length)
            {
                uint multiply = ColorDef.ToUInt(channelMask[Channel]);
                uint additive = ColorDef.ToUInt(channelAdditiveMask[Channel]);
                return (multiply == 0xffffffff && additive == 0);
            }
            return false;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public OverlayColorData() { }

        /// <summary>
        /// Constructor for a given number of channels. Initializes defaults (white for mask, empty for additive).
        /// </summary>
        public OverlayColorData(int channels)
        {
            channelMask = new Color[channels];
            channelAdditiveMask = new Color[channels];
            for (int i = 0; i < channels; i++)
            {
                channelMask[i] = Color.white;
                channelAdditiveMask[i] = EmptyAdditive;
            }
        }

        /// <summary>
        /// Deep copy of the OverlayColorData.
        /// </summary>
        public OverlayColorData Clone()
        {
            var res = new OverlayColorData();
            res.name = name; // strings are immutable; copy reference is fine
            res.displayColor = displayColor;
            
#if UNITY_EDITOR
            res.foldout = foldout;
            res.isBaseColor = isBaseColor;
            res.showDisplayColor = showDisplayColor;
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
                res.PropertyBlock = new UMAMaterialPropertyBlock
                {
                    alwaysUpdate = PropertyBlock.alwaysUpdate,
                    alwaysUpdateParms = PropertyBlock.alwaysUpdateParms,
                    shaderProperties = new List<UMAProperty>(PropertyBlock.shaderProperties?.Count ?? 0)
                };
                if (PropertyBlock.shaderProperties != null)
                {
                    for (int i = 0; i < PropertyBlock.shaderProperties.Count; i++)
                    {
                        UMAProperty up = PropertyBlock.shaderProperties[i];
                        if (up != null) res.PropertyBlock.shaderProperties.Add(up.Clone());
                    }
                }
            }
            return res;
        }

        /// <summary>
        /// True if this references a shared color (vs being unique/inline).
        /// </summary>
        public bool IsASharedColor
        {
            get
            {
                return HasName() && name != UNSHARED;
            }
        }

        [Obsolete("Use IsEmpty instead. This property returned true when the data was empty, which was confusing.")]
        public bool isValid
        {
            get { return IsEmpty; }
        }

        /// <summary>
        /// True if this contains no colors and no properties.
        /// </summary>
        public bool IsEmpty
        {
            get { return (PropertyBlock == null || (PropertyBlock.shaderProperties == null || PropertyBlock.shaderProperties.Count == 0)) && (channelMask == null || channelMask.Length == 0); }
        }

        public bool HasColors => channelMask != null && channelMask.Length > 0;

        public bool HasPropertyBlock => PropertyBlock != null;

        public bool HasProperties
        {
            get
            {
                return PropertyBlock != null && PropertyBlock.shaderProperties != null && PropertyBlock.shaderProperties.Count > 0;
            }
        }

        /// <summary>
        /// True if only colors exist (no properties).
        /// </summary>
        public bool isOnlyColors
        {
            get { return HasColors && !HasProperties; }
        }

        /// <summary>
        /// True if only properties exist (no colors).
        /// </summary>
        public bool isOnlyProperties
        {
            get { return !HasColors && HasProperties; }
        }

        /// <summary>
        /// Does the OverlayColorData have a valid name?
        /// </summary>
        public bool HasName() => !string.IsNullOrEmpty(name);

        public static bool SameColor(Color c1, Color c2)
        {
            return Mathf.Approximately(c1.r, c2.r) &&
                   Mathf.Approximately(c1.g, c2.g) &&
                   Mathf.Approximately(c1.b, c2.b) &&
                   Mathf.Approximately(c1.a, c2.a);
        }

        public static bool DifferentColor(Color c1, Color c2) => !SameColor(c1, c2);

        public static implicit operator bool(OverlayColorData obj)
        {
            return ((System.Object)obj) != null;
        }

        public bool Equals(OverlayColorData other) => (this == other);

        public override bool Equals(object other) => Equals(other as OverlayColorData);

        public static bool operator ==(OverlayColorData cd1, OverlayColorData cd2)
        {
            if (cd1)
            {
                if (cd2)
                {
                    if (cd1.displayColor != cd2.displayColor) return false;

                    // Require both masks and additive arrays to be same length
                    if (cd1.channelMask.Length != cd2.channelMask.Length) return false;
                    if (cd1.channelAdditiveMask.Length != cd2.channelAdditiveMask.Length) return false;

                    for (int i = 0; i < cd1.channelMask.Length; i++)
                    {
                        if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i])) return false;
                    }
                    for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
                    {
                        if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i])) return false;
                    }
                    // PropertyBlock intentionally not included (by design)
                    return true;
                }
                return false;
            }
            return !(bool)cd2;
        }

        public static bool operator !=(OverlayColorData cd1, OverlayColorData cd2)
        {
            if (cd1)
            {
                if (cd2)
                {
                    if (cd1.displayColor != cd2.displayColor) return true;
                    if (cd1.channelMask.Length != cd2.channelMask.Length) return true;
                    if (cd1.channelAdditiveMask.Length != cd2.channelAdditiveMask.Length) return true;

                    for (int i = 0; i < cd1.channelMask.Length; i++)
                    {
                        if (DifferentColor(cd1.channelMask[i], cd2.channelMask[i])) return true;
                    }
                    for (int i = 0; i < cd1.channelAdditiveMask.Length; i++)
                    {
                        if (DifferentColor(cd1.channelAdditiveMask[i], cd2.channelAdditiveMask[i])) return true;
                    }
                    return false;
                }
                return true;
            }
            return ((bool)cd2);
        }

        public override int GetHashCode()
        {
            // Keep default behavior to avoid breaking serialized dictionaries; override only if needed later
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

        public Color GetColor(int textureNumber, bool additive)
        {
            if (textureNumber < 0) return Color.white;
            if (!additive)
            {
                if (textureNumber < channelMask.Length) return channelMask[textureNumber];
                return Color.white;
            }
            else
            {
                if (textureNumber < channelAdditiveMask.Length) return channelAdditiveMask[textureNumber];
                return EmptyAdditive;
            }
        }

        public void SetColor(int textureNumber, bool additive, Color col)
        {
            if (textureNumber < 0) return;
            if (!additive)
            {
                if (textureNumber < channelMask.Length) channelMask[textureNumber] = col;
            }
            else
            {
                if (textureNumber < channelAdditiveMask.Length) channelAdditiveMask[textureNumber] = col;
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
                    channelAdditiveMask[i] = EmptyAdditive;
                }
            }
            else
            {
                if (channelMask.Length < channels)
                {
                    var newMask = new Color[channels];
                    Array.Copy(channelMask, newMask, channelMask.Length);
                    for (int i = channelMask.Length; i < channels; i++)
                    {
                        newMask[i] = Color.white;
                    }
                    channelMask = newMask;
                }
                if (channelAdditiveMask == null) channelAdditiveMask = new Color[0];
                if (channelAdditiveMask.Length < channels)
                {
                    var newAdditiveMask = new Color[channels];
                    Array.Copy(channelAdditiveMask, newAdditiveMask, channelAdditiveMask.Length);
                    for (int i = channelAdditiveMask.Length; i < channels; i++)
                    {
                        newAdditiveMask[i] = EmptyAdditive;
                    }
                    channelAdditiveMask = newAdditiveMask;
                }
            }
        }

        public void EnsureChannelsExact(int ChannelCount)
        {
            if (channelMask == null) channelMask = new Color[0];
            if (channelAdditiveMask == null) channelAdditiveMask = new Color[0];

            int oldMask = channelMask.Length;
            int oldAdd = channelAdditiveMask.Length;

            Array.Resize(ref channelMask, ChannelCount);
            Array.Resize(ref channelAdditiveMask, ChannelCount);

            // Fill defaults for any new elements when expanding
            for (int i = oldMask; i < ChannelCount; i++)
            {
                channelMask[i] = Color.white;
            }
            for (int i = oldAdd; i < ChannelCount; i++)
            {
                channelAdditiveMask[i] = EmptyAdditive;
            }
        }

        public void AssignTo(OverlayColorData dest)
        {
            if (dest == null) return;
            dest.name = name; // strings are immutable
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
                dest.PropertyBlock.shaderProperties = new List<UMAProperty>(PropertyBlock.shaderProperties?.Count ?? 0);
                if (PropertyBlock.shaderProperties != null)
                {
                    for (int i = 0; i < PropertyBlock.shaderProperties.Count; i++)
                    {
                        UMAProperty up = PropertyBlock.shaderProperties[i];
                        if (up != null) dest.PropertyBlock.shaderProperties.Add(up.Clone());
                    }
                }
            }
#if UNITY_EDITOR
            dest.isBaseColor = isBaseColor;
#endif
        }

        public void AssignFrom(OverlayColorData src, bool CopyParmsOnly = false)
        {
            if (src == null) return;

            bool preservePropertyUpdateFlags = CopyParmsOnly && PropertyBlock != null;
            bool previousAlwaysUpdate = preservePropertyUpdateFlags && PropertyBlock.alwaysUpdate;
            bool previousAlwaysUpdateParms = preservePropertyUpdateFlags && PropertyBlock.alwaysUpdateParms;

            // If CopyParmsOnly is false, then we need to copy the colors and the property block.
            // If it is true, then we are copying the property block only.
            if (!CopyParmsOnly)
            {
#if UNITY_EDITOR
                isBaseColor = src.isBaseColor;
#endif
                name = src.name; // strings are immutable
                EnsureChannels(src.channelMask?.Length ?? 0);
                for (int i = 0; i < src.channelMask.Length; i++)
                {
                    channelMask[i] = src.channelMask[i];
                }
                EnsureChannels(src.channelAdditiveMask?.Length ?? 0);
                for (int i = 0; i < src.channelAdditiveMask.Length; i++)
                {
                    channelAdditiveMask[i] = src.channelAdditiveMask[i];
                }
            }

            displayColor = src.displayColor;
            PropertyBlock = new UMAMaterialPropertyBlock();
            if (src.PropertyBlock != null)
            {
                PropertyBlock.alwaysUpdate = preservePropertyUpdateFlags ? previousAlwaysUpdate : src.PropertyBlock.alwaysUpdate;
                PropertyBlock.alwaysUpdateParms = preservePropertyUpdateFlags ? previousAlwaysUpdateParms : src.PropertyBlock.alwaysUpdateParms;
                PropertyBlock.shaderProperties = new List<UMAProperty>(src.PropertyBlock.shaderProperties?.Count ?? 0);
                if (src.PropertyBlock.shaderProperties != null)
                {
                    for (int i = 0; i < src.PropertyBlock.shaderProperties.Count; i++)
                    {
                        UMAProperty up = src.PropertyBlock.shaderProperties[i];
                        if (up != null) PropertyBlock.shaderProperties.Add(up.Clone());
                    }
                }
            }
            else if (preservePropertyUpdateFlags)
            {
                PropertyBlock.alwaysUpdate = previousAlwaysUpdate;
                PropertyBlock.alwaysUpdateParms = previousAlwaysUpdateParms;
            }
        }

        /// <summary>
        /// Optional validation to keep internal arrays consistent (call after deserialization if needed).
        /// </summary>
        public void Validate()
        {
            if (channelMask == null) channelMask = Array.Empty<Color>();
            if (channelAdditiveMask == null) channelAdditiveMask = Array.Empty<Color>();
            // No-op by default; could enforce same length if UMA material requires it.
        }
    }
}
