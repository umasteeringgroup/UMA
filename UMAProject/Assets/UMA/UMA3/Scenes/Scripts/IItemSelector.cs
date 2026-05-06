using UMA.CharacterSystem;

namespace UMA
{

    public interface IItemSelector
    {
        public void SetItem(UMAWardrobeRecipe item);
        public void ClearItem(string category);
    }
}