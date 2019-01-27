#if UNITY_2017_1_OR_NEWER
using UnityEngine;
using UMA.CharacterSystem;
using UnityEngine.Playables;

namespace UMA.Examples
{
    public class UMAPlayOnAwake : MonoBehaviour
    {
        public PlayableDirector playableDirector;

        DynamicCharacterAvatar avatar;

        // Use this for initialization
        void Start()
        {
            avatar = GetComponent<DynamicCharacterAvatar>();
            avatar.CharacterCreated.AddListener(OnCharacterCreated);
        }

        public void OnCharacterCreated(UMAData umaData)
        {
            playableDirector.Play();
        }
    }
}
#endif
