using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class UMAManualLOD : MonoBehaviour
    {
        [Tooltip("The current LOD level (manually set)")]
        public int currentLODLevel = 0;

        [Tooltip("Enable manual LOD control. If false, the script will manage LOD levels based on Quality Settings.")]
        public bool manualLOD = false;
        

        private DynamicCharacterAvatar _avatar;
        private UMAData _umaData;

        public void OnEnable()
        {
            _avatar = GetComponent<DynamicCharacterAvatar>();
            
            // Subscribe to Begun event
            if (_avatar != null)
            {
                _avatar.CharacterBegun.AddListener(CharacterBegun);
            }

        }

        public void OnDisable()
        {
            // Unsubscribe from event to prevent memory leaks
            if (_avatar != null)
            {
                _avatar.CharacterBegun.RemoveListener(CharacterBegun);
            }
            
        }


        private void CharacterBegun(UMAData umaData)
        {
            _umaData = umaData;

            
            if (!manualLOD)
            {
                // I have 4 quality levels: 0, 1, 2, 3; Where 3 is the best, and 0 is the worst. 
                // Since LOD 0 is the best LOD, I invert the quality level to match the LOD levels. 
                int currentQualityLevel = QualitySettings.GetQualityLevel();
                currentLODLevel = 3 - currentQualityLevel;
            }

            SetLODLevel(currentLODLevel);
        }


        public void SetLODLevel(int lodLevel)
        {
            if (_avatar == null || _umaData == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("UMAManualLOD: Avatar or UMAData is not initialized.");
#endif
                return;
            }
 
            bool slotsChanged = ProcessSlotDropping(currentLODLevel);

            if (slotsChanged)
            {
                // Request UMA to rebuild with updated slots
                _umaData.Dirty(true, false, true);
            }

        }


        // Process slot dropping based on LOD level
        private bool ProcessSlotDropping(int lodLevel)
        {
            if (_umaData.umaRecipe?.slotDataList == null)
            {
                return false;
            }

            bool changedSlots = false;

            for (int i = 0; i < _umaData.umaRecipe.slotDataList.Length; i++)
            {
                var slot = _umaData.umaRecipe.slotDataList[i];
                if (slot == null) continue;

                // MaxLod = -1 means no LOD limit for this slot
                if (slot.MaxLod < 0) continue;

                // Check if slot should be dropped based on MaxLod
                // Example: If slot.MaxLod = 2 and lodLevel = 3, the slot should be suppressed
                bool shouldSuppress = lodLevel > slot.MaxLod;

                if (shouldSuppress && !slot.Suppressed)
                {
                    // Suppress the slot
                    slot.Suppressed = true;
                    changedSlots = true;
                    
                }
                else if (!shouldSuppress && slot.Suppressed)
                {
                    // Restore the slot
                    slot.Suppressed = false;
                    changedSlots = true;
                    
                }
            }

            return changedSlots;
        }


    }
}