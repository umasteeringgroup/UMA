using Unity.Profiling;

namespace UMA
{
    /// <summary>
    /// CPU samples for recipe preparation, separate from queued generator work.
    /// These markers retain no recipes or assets. Nested samples are inclusive;
    /// do not add their durations to their parents. Async loading wait time is
    /// deliberately not included in the synchronous Preload sample.
    /// </summary>
    internal static class UMACharacterBuildProfiler
    {
        internal static readonly ProfilerMarker BuildCharacter = new ProfilerMarker("UMA.Character.BuildCharacter");
        internal static readonly ProfilerMarker Startup = new ProfilerMarker("UMA.Character.Startup");
        internal static readonly ProfilerMarker BuildBegun = new ProfilerMarker("UMA.Character.BuildBegunEvent");
        internal static readonly ProfilerMarker LoadCharacter = new ProfilerMarker("UMA.Character.LoadCharacter");
        internal static readonly ProfilerMarker Preload = new ProfilerMarker("UMA.Character.PreloadRequest");
        internal static readonly ProfilerMarker BaseRecipe = new ProfilerMarker("UMA.Character.BaseRecipe");
        internal static readonly ProfilerMarker AdditionalRecipes = new ProfilerMarker("UMA.Character.AdditionalRecipes");
        internal static readonly ProfilerMarker WardrobeRecipes = new ProfilerMarker("UMA.Character.WardrobeRecipes");
        internal static readonly ProfilerMarker CrossCompatible = new ProfilerMarker("UMA.Character.CrossCompatibleSlots");
        internal static readonly ProfilerMarker PostProcessSlots = new ProfilerMarker("UMA.Character.PostProcessSlots");
        internal static readonly ProfilerMarker ReplaceSlot = new ProfilerMarker("UMA.Character.ReplaceSlot");
        internal static readonly ProfilerMarker MeshHides = new ProfilerMarker("UMA.Character.MeshHides");
        internal static readonly ProfilerMarker SortOverlays = new ProfilerMarker("UMA.Character.SortOverlays");
        internal static readonly ProfilerMarker Smoosh = new ProfilerMarker("UMA.Character.Smoosh");
        internal static readonly ProfilerMarker InitializeDNA = new ProfilerMarker("UMA.Character.InitializeDNA");
        internal static readonly ProfilerMarker DNARecipeCallbacks = new ProfilerMarker("UMA.Character.DNARecipeCallbacks");
        internal static readonly ProfilerMarker UpdateColors = new ProfilerMarker("UMA.Character.UpdateColors");
        internal static readonly ProfilerMarker RecipeUpdated = new ProfilerMarker("UMA.Character.RecipeUpdatedEvent");
        internal static readonly ProfilerMarker QueueBuild = new ProfilerMarker("UMA.Character.QueueBuild");
        internal static readonly ProfilerMarker RecipePrepared = new ProfilerMarker("UMA.Character.RecipePreparedEvents");
        internal static readonly ProfilerMarker Modifiers = new ProfilerMarker("UMA.Character.Modifiers");
        internal static readonly ProfilerMarker GetRecipe = new ProfilerMarker("UMA.Recipe.GetCachedRecipe");
        internal static readonly ProfilerMarker LoadRecipe = new ProfilerMarker("UMA.Recipe.Load");
        internal static readonly ProfilerMarker ParseRecipe = new ProfilerMarker("UMA.Recipe.PackedLoadForUnpack");
        internal static readonly ProfilerMarker UnpackRecipe = new ProfilerMarker("UMA.Recipe.Unpack");
        internal static readonly ProfilerMarker BakeRaceSlots = new ProfilerMarker("UMA.Recipe.BakeRaceSlots");
        internal static readonly ProfilerMarker UnpackDNA = new ProfilerMarker("UMA.Recipe.UnpackDNA");
        internal static readonly ProfilerMarker EnsureDNA = new ProfilerMarker("UMA.Recipe.EnsureDNA");
        internal static readonly ProfilerMarker GetDefinedDNA = new ProfilerMarker("UMA.Recipe.GetDefinedDNA");
        internal static readonly ProfilerMarker UnpackColors = new ProfilerMarker("UMA.Recipe.UnpackColors");
        internal static readonly ProfilerMarker Merge = new ProfilerMarker("UMA.Recipe.Merge");
        internal static readonly ProfilerMarker MergeSlot = new ProfilerMarker("UMA.Recipe.MergeSlot");
        internal static readonly ProfilerMarker MergeMatchingOverlays = new ProfilerMarker("UMA.Recipe.MergeMatchingOverlays");
        internal static readonly ProfilerMarker GetAsset = new ProfilerMarker("UMA.Assets.GetAsset");
        internal static readonly ProfilerMarker LoadAsset = new ProfilerMarker("UMA.Assets.LoadFromAssetDatabase");
    }
}
