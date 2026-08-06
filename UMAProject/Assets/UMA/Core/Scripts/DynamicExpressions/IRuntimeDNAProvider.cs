using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// Supplies transient, non-recipe DNA effects to UMA generation.
    /// </summary>
    public interface IRuntimeDNAProvider
    {
        DNAInstanceCollection.DNABuildType AfterRecipeGenerated(
            DynamicCharacterAvatar avatar);

        DNAInstanceCollection.DNABuildType PreApply(UMAData umaData);

        DNAInstanceCollection.DNABuildType Apply(UMAData umaData);

        DNAInstanceCollection.DNABuildType PostApply(UMAData umaData);
    }
}
