using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
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
