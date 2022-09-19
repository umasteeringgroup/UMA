using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    public class UMALODLogger : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public string slotID;
        public string lastLOD;

        // Update is called once per frame
        void Update()
        {
            if (avatar != null)
            {
                if (avatar.umaData != null && avatar.umaData.umaRecipe != null)
                {
                    var slots = avatar.umaData.umaRecipe.slotDataList;

                    foreach (var slot in slots)
                    {
                        if (slot != null)
                        {
                            if (slot.slotName != null)
                            {
                                if (slot.slotName.ToLower().Contains(slotID.ToLower()))
                                {
                                    lastLOD = slot.slotName;
                                }
                            }
                        }
                    }

                }
            }

        }
    }
}
