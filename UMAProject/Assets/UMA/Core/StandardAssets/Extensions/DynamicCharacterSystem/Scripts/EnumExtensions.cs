using System;

namespace UMA.CharacterSystem
{
    public static class EnumExtensions
    {

        public static bool HasFlagSet<T>(this T self, T flag) where T : struct, Enum
        {
            // No boxing occurs for generic value types
            ulong selfValue = unchecked((ulong)Convert.ToInt64(self));
            ulong flagValue = unchecked((ulong)Convert.ToInt64(flag));
            return (selfValue & flagValue) == flagValue;
        }
    }
}
