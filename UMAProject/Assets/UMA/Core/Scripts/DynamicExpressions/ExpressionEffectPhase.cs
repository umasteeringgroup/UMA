using System;

namespace UMA
{
    [Flags]
    public enum ExpressionEffectPhase
    {
        None = 0,
        EarlyRestore = 1 << 0,
        LateRig = 1 << 1,
        LateBlendShape = 1 << 2,
        RuntimeMaterial = 1 << 3,
        BuildAfterRecipe = 1 << 4,
        BuildPreApply = 1 << 5,
        BuildApply = 1 << 6,
        BuildPostApply = 1 << 7
    }
}
