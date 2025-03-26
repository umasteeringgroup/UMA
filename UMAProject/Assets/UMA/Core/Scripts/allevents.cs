using UnityEngine;
using UMA;
namespace UMA
{
    public class allevents : MonoBehaviour
    {
        public void CharacterBegun(UMAData umaData)
        {
            Debug.Log("Character Begun");
        }

        public void CharacterCreated(UMAData umaData)
        {
            Debug.Log("Character Created");
        }
        public void CharacterUpdated(UMAData umaData)
        {
            Debug.Log("Character Updated");
        }
        public void CharacterDestroyed(UMAData umaData)
        {
            Debug.Log("Character Destroyed");
        }
        public void CharacterDNAUpdated(UMAData umaData)
        {
            Debug.Log("Character DNA Updated");
        }
        public void RecipeUpdated(UMAData umaData)
        {
            Debug.Log("Recipe Updated");
        }
        public void AnimatorStateSaved(UMAData umaData)
        {
            Debug.Log("Animator State Saved");
        }
        public void AnimatorStateRestored(UMAData umaData)
        {
            Debug.Log("Animator State Restored");
        }
    }
}
