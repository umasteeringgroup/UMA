using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class RandomDNA : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;

        public void RandomizeDna()
        {
            if (avatar == null)
                return;

            avatar.GetComponent<NetworkDCA>().CmdUpdateDNA();
        }
    }
}
