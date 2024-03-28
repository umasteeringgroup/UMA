using System;

namespace UMA.CharacterSystem
{
    [Serializable]
	public class UMAPreset
	{
		public UMAPredefinedDNA PredefinedDNA;
		public DynamicCharacterAvatar.WardrobeRecipeList DefaultWardrobe;
		public DynamicCharacterAvatar.ColorValueList DefaultColors;
	}
}
