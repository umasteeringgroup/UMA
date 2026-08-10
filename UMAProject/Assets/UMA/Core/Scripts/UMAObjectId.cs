using System;
using System.Globalization;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// A version-independent identifier for a live Unity object.
    /// </summary>
    /// <remarks>
    /// Unity 6.3 introduced EntityId, and later Unity versions widen its native
    /// representation. Keeping the native value as raw data lets UMA use one key
    /// type without reducing wider identifiers to a potentially colliding integer.
    /// </remarks>
    [Serializable]
    public struct UMAObjectId : IEquatable<UMAObjectId>
    {
        [SerializeField]
        private ulong rawValue;

#if UNITY_6000_5_OR_NEWER
        internal UMAObjectId(EntityId value)
        {
            rawValue = EntityId.ToULong(value);
        }

        public static implicit operator EntityId(UMAObjectId value)
        {
            return EntityId.FromULong(value.rawValue);
        }
#else
        internal UMAObjectId(int value)
        {
            rawValue = unchecked((ulong)(long)value);
        }

        public static implicit operator int(UMAObjectId value)
        {
            return unchecked((int)value.rawValue);
        }
#endif

        public static implicit operator UMAObjectId(int value)
        {
            return FromInt32(value);
        }

        public static bool operator ==(UMAObjectId left, UMAObjectId right)
        {
            return left.rawValue == right.rawValue;
        }

        public static bool operator !=(UMAObjectId left, UMAObjectId right)
        {
            return left.rawValue != right.rawValue;
        }

        public bool Equals(UMAObjectId other)
        {
            return rawValue == other.rawValue;
        }

        public override bool Equals(object obj)
        {
            return obj is UMAObjectId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((int)(rawValue ^ (rawValue >> 32)));
        }

        public override string ToString()
        {
#if UNITY_6000_5_OR_NEWER
            return rawValue.ToString(CultureInfo.InvariantCulture);
#else
            return unchecked((int)rawValue).ToString(CultureInfo.InvariantCulture);
#endif
        }

        public static ulong ToULong(UMAObjectId value)
        {
            return value.rawValue;
        }

        public static bool TryParse(string value, out UMAObjectId objectId)
        {
#if UNITY_6000_5_OR_NEWER
            ulong parsedValue;
            if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                objectId = new UMAObjectId(EntityId.FromULong(parsedValue));
                return true;
            }
#else
            int parsedValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                objectId = new UMAObjectId(parsedValue);
                return true;
            }
#endif

            objectId = default(UMAObjectId);
            return false;
        }

        private static UMAObjectId FromInt32(int value)
        {
#if UNITY_6000_5_OR_NEWER
            return new UMAObjectId(EntityId.FromULong(unchecked((ulong)(long)value)));
#else
            return new UMAObjectId(value);
#endif
        }
    }

    public static class UMAObjectIdExtensions
    {
        /// <summary>
        /// Gets Unity's unique identifier for an object.
        /// </summary>
        public static UMAObjectId GetUmaObjectId(this UnityEngine.Object obj)
        {
            return new UMAObjectId(obj.GetEntityId());
        }
    }
}
