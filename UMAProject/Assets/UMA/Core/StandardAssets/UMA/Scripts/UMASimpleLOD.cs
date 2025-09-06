using UnityEngine;
using UMA.CharacterSystem;
using System;
using System.Collections.Generic;

namespace UMA.Examples
{
    public class UMASimpleLOD : MonoBehaviour
    {
        [Tooltip("The distance to step to another LOD")]
        [Range(0.01f, 100f)]
        public float lodDistance = 5.0f;

        [Tooltip("The LOD distance is cumulatively multiplied by this each level ie - 5 distance and multiplier 2 would give 5/10/20/40/80")]
        [Range(1.5f, 4.0f)]
        public float distanceMultiplier = 2.0f;

        [Tooltip("Look for LOD slots in the library.")]
        public bool swapSlots;

        [Tooltip("This value is subtracted from the slot LOD counter.")]
        public int lodOffset;

        [Tooltip("This is the max LOD to search for if the current LOD can't be found.")]
        public int maxLOD = 5;

        [Tooltip("The maximum scale reduction (8 means the texture can be reduced in half 8 times)")]
        public int maxReduction = 8;

        [Tooltip("Allow the system to drop slots based on the SlotDataAsset MaxLOD")]
        public bool useSlotDropping;

        [Tooltip("Allow the system to drop slots based on the SlotDataAsset MaxLOD")]
        public bool useTextureResize;

        [Tooltip("Disable the automated processing of LOD changes. This is useful if you want to control when the LOD changes happen, such as in a custom update loop or event system.")]
        public bool disableAutomatedProcessing;

        [Header("Buffer / Hysteresis")]
        [Tooltip("If true, BufferPercent is used (as a fraction of the LOD threshold). If false, BufferZone is used in world units.")]
        public bool UsePercentageBuffer = true;

        [Tooltip("Percentage buffer (0..1) used around LOD thresholds to avoid thrashing. Only used when 'Use Percentage Buffer' is true.")]
        [Range(0f, 0.5f)]
        public float BufferPercent = 0.1f;

        [Tooltip("Absolute distance buffer in world units to avoid thrashing near thresholds. Used when 'Use Percentage Buffer' is false.")]
        public float BufferZone = 0.5f;

        public int CurrentLOD { get { return _currentLOD - lodOffset; } }

        private int _currentLOD = -1;
        private float lastDist = 0.0f;
        private float NextTime = 0.0f;

        [Tooltip("How much time must pass before this is checked again. Default = 0.5 seconds")]
        public float MinCheck = 0.5f;

        [Tooltip("Random Variance in time (added to MinCheck) so that everything doesn't trigger at the same time. Default = 0.25 seconds")]
        public float CheckRange = 0.25f;

        private DynamicCharacterAvatar _avatar;
        private UMAData _umaData;

        private bool initialized = false;

        public void SetSwapSlots(bool swapSlots, int lodOffset)
        {
            this.lodOffset = lodOffset;
            this.swapSlots = swapSlots;
            bool changedSlots = ProcessRecipe(_currentLOD);
            if (changedSlots)
            {
                _umaData.Dirty(true, true, true);
            }
        }

        public void Awake()
        {
            _currentLOD = -1;
        }

        public void Reset()
        {
            _currentLOD = -1;
            NextTime = Time.time;
        }

        public void OnEnable()
        {
            _avatar = GetComponent<DynamicCharacterAvatar>();
            if (_avatar != null)
            {
                _avatar.CharacterBegun.AddListener(CharacterBegun);
            }
            else
            {
                _umaData = GetComponent<UMAData>();
                if (_umaData != null)
                {
                    _umaData.CharacterCreated.AddListener(CharacterCreated);
                }
            }
        }

        public void CharacterCreated(UMAData umaData)
        {
            initialized = true;
        }

        public void CharacterBegun(UMAData umaData)
        {
            initialized = true;
            DoLODCheck(umaData);
        }

        private void DoLODCheck(UMAData umaData)
        {
            if (!PerformLodCheck())
            {
                _currentLOD = 0;
                if (umaData != null)
                {
                    umaData.atlasResolutionScale = 1.0f;
                    ProcessRecipe(_currentLOD);
                }
            }
        }

        /// <summary>
        /// Manually check the LOD level without hysteresis gating. Call from CharacterBegun or when UMAData is ready.
        /// </summary>
        public void DoManualLODCheck(int lodLevel)
        {
            if (_umaData == null)
            {
                _umaData = gameObject.GetComponent<UMAData>();
            }
            if (_umaData == null)
            {
                return;
            }
            if (lodLevel < 0)
            {
                lodLevel = 0;
            }
            if (lodLevel >= maxLOD)
            {
                lodLevel = maxLOD - 1;
            }
            _currentLOD = lodLevel + lodOffset;
            ProcessRecipe(_currentLOD);
        }

        public void Update()
        {
            if (!initialized)
            {
                return;
            }

            if (disableAutomatedProcessing)
            {
                return;
            }

            if (Time.time > NextTime)
            {
                DoLODCheck(_umaData);
                NextTime = Time.time + MinCheck;
                if (CheckRange > 0.0f)
                {
                    NextTime += UnityEngine.Random.Range(0.0f, CheckRange);
                }
            }
        }

        public bool PerformLodCheck()
        {
            if (_umaData == null)
            {
                _umaData = gameObject.GetComponent<UMAData>();
            }

            if (_umaData == null)
            {
                return false;
            }

            if (_umaData.umaRecipe == null)
            {
                return false;
            }

            if (lodDistance <= 0f)
            {
                return false;
            }

            if (Camera.main == null)
            {
                return false;
            }

            Transform _cameraTransform = Camera.main.transform;

            if (_cameraTransform == null)
            {
                Debug.Log("Camera transform is null in UMASimpleLOD");
                return false;
            }

            float cameraDistance = (transform.position - _cameraTransform.position).magnitude;

            // Compute naive level from distance, then apply hysteresis to avoid thrashing.
            int naiveLevel = LevelFromDistance(cameraDistance);
            int effectiveLevel = ApplyBufferHysteresis(_currentLOD, naiveLevel, cameraDistance);

            // Compute atlas scale from the effective level
            float atlasResolutionScale = 1f;
            for (int i = 0; i < effectiveLevel; i++)
            {
                atlasResolutionScale *= 0.5f;
            }
            float maxReductionf = (maxReduction <= 0) ? 1f : (1.0f / maxReduction);
            if (atlasResolutionScale < maxReductionf)
            {
                atlasResolutionScale = maxReductionf;
            }

            bool updatedTextures = false;
            bool updatedSlots = false;

            if (useTextureResize && (_umaData.atlasResolutionScale != atlasResolutionScale))
            {
                updatedTextures = true;
                _umaData.atlasResolutionScale = atlasResolutionScale;
            }

            if (useSlotDropping || swapSlots)
            {
                updatedSlots = ProcessRecipe(effectiveLevel);
            }

            if (_currentLOD != effectiveLevel)
            {
                lastDist = cameraDistance;
                _currentLOD = effectiveLevel;
            }

            if (updatedTextures || updatedSlots)
            {
                _umaData.Dirty(updatedSlots, updatedTextures, updatedSlots);
            }

            return true;
        }

        // ---------- Hysteresis helpers ----------

        // Compute the distance threshold (upper bound) for a given LOD level:
        // Level 0 threshold S0 = lodDistance
        // Level 1 threshold S1 = lodDistance * distanceMultiplier
        // ...
        private float ThresholdForLevel(int level)
        {
            if (lodDistance <= 0f) return float.PositiveInfinity;
            if (level <= 0) return lodDistance;
            return lodDistance * Mathf.Pow(distanceMultiplier, level);
        }

        // Convert distance to naive LOD level (without hysteresis)
        private int LevelFromDistance(float cameraDistance)
        {
            if (lodDistance <= 0f) return 0;

            int level = 0;
            float step = lodDistance;
            while (cameraDistance > step && level < (maxLOD - 1))
            {
                step *= distanceMultiplier;
                level++;
            }
            return level;
        }

        // Apply BufferZone/BufferPercent hysteresis around the current LOD boundaries
        private int ApplyBufferHysteresis(int currentLevel, int targetLevel, float cameraDistance)
        {
            if (currentLevel < 0) return targetLevel; // first time, adopt target
            if (targetLevel == currentLevel) return currentLevel;

            // Gate distances for switching
            float upThreshold = ThresholdForLevel(currentLevel); // boundary to go up from current
            float downThreshold = (currentLevel > 0) ? ThresholdForLevel(currentLevel - 1) : 0f; // boundary to go down into previous

            if (UsePercentageBuffer)
            {
                float pct = Mathf.Clamp01(BufferPercent);
                float upGate = upThreshold * (1f + pct);
                float downGate = Mathf.Max(0f, downThreshold * (1f - pct));

                if (targetLevel > currentLevel)
                {
                    // Moving farther: only switch if beyond current upper threshold + % buffer
                    return (cameraDistance > upGate) ? targetLevel : currentLevel;
                }
                else
                {
                    // Moving closer: only switch if below lower threshold - % buffer
                    return (cameraDistance < downGate) ? targetLevel : currentLevel;
                }
            }
            else
            {
                float abs = Mathf.Max(0f, BufferZone);
                float upGate = upThreshold + abs;
                float downGate = Mathf.Max(0f, downThreshold - abs);

                if (targetLevel > currentLevel)
                {
                    // Moving farther: only switch if beyond current upper threshold + absolute buffer
                    return (cameraDistance > upGate) ? targetLevel : currentLevel;
                }
                else
                {
                    // Moving closer: only switch if below lower threshold - absolute buffer
                    return (cameraDistance < downGate) ? targetLevel : currentLevel;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void StaticInitializeOnLoad()
        {
            // Clear the LOD cache when we do a domain reload.
            LODSFound = new Dictionary<string, string[]>();
        }

        // Should this be in the library?
        // Key:   string slotName.  This is the base slot name.
        // Value: Array of strings, one for each possible LOD level.
        private static Dictionary<string, string[]> LODSFound = new Dictionary<string, string[]>();  // SlotName,  LODNames - one for each level.

        /// <summary>
        /// Get the slot name for the current LOD level. If there is one.
        /// Calculate and cache the slot names for each LOD level.
        /// </summary>
        private string GetNextLODName(string currentSlotName, string baseSlotName, int lodLevel)
        {
            if (lodLevel < 0)
            {
                lodLevel = 0;
            }

            if (lodLevel >= maxLOD)
            {
                lodLevel = maxLOD - 1;
            }

            // See if we have already looked for LOD's for this slot.
            if (LODSFound.ContainsKey(baseSlotName))
            {
                if (LODSFound[baseSlotName] != null)
                {
                    // If there *are* lods for this slot, then return the slot name
                    // for the specific LOD level.
                    return LODSFound[baseSlotName][lodLevel];
                }
                else
                {
                    // if there are NO lods for this slot, just return the current slot name.
                    return baseSlotName;
                }
            }

            // get all the Lods. Fill out the lodlevels.
            string[] SlotLods = new string[maxLOD];
            string lastSlot = baseSlotName;
            int foundLODS = 0;

            for (int i = 0; i < maxLOD; i++)
            {
                SlotLods[i] = string.Empty;
                string possibleSlot = string.Concat(baseSlotName, "_LOD", i.ToString());
                if (UMAAssetIndexer.Instance.HasSlot(possibleSlot))
                {
                    SlotLods[i] = possibleSlot;
                    foundLODS++;
                    lastSlot = possibleSlot;
                }
                else
                {
                    if (i == 0 && UMAAssetIndexer.Instance.HasSlot(baseSlotName))
                    {
                        SlotLods[i] = baseSlotName;
                        foundLODS++;
                        lastSlot = baseSlotName;
                    }
                }
                if (SlotLods[i] == String.Empty)
                {
                    SlotLods[i] = lastSlot;
                }
            }

            if (foundLODS > 0)
            {
                // save the generated LOD list
                LODSFound.Add(baseSlotName, SlotLods);
                return SlotLods[lodLevel];
            }
            else
            {
                // No lod's for this slot.
                LODSFound.Add(baseSlotName, null);
                return currentSlotName;
            }
        }

        private bool ProcessRecipe(int currentLevel)
        {
            bool changedSlots = false;

            if (_umaData.umaRecipe.slotDataList == null)
            {
                return false;
            }

            for (int i = 0; i < _umaData.umaRecipe.slotDataList.Length; i++)
            {
                var slot = _umaData.umaRecipe.slotDataList[i];
                if (slot != null)
                {
                    if (useSlotDropping)
                    {
                        // mark the slots as dirty if one is over the limit.
                        if (slot.MaxLod > -1 && _currentLOD > slot.MaxLod)
                        {
                            // Only trigger this the first time, so we only force a rebuild
                            // once (or possibly later if slots change...)
                            if (!slot.Suppressed)
                            {
                                // no need to look for LOD's if this is suppressed.
                                changedSlots = true;
                                slot.Suppressed = true;
                                continue;
                            }
                        }
                        else
                        {
                            if (slot.Suppressed)
                            {
                                changedSlots = true;
                                slot.Suppressed = false;
                            }
                        }
                    }

                    var slotName = slot.slotName;
                    var lodIndex = slotName.IndexOf("_LOD");
                    string baseSlotName = slotName;
                    if (lodIndex >= 0)
                    {
                        slotName = slotName.Substring(0, lodIndex);
                        baseSlotName = slotName;
                    }

                    string newSlot = GetNextLODName(slot.slotName, baseSlotName, currentLevel - lodOffset);
                    // if there is a new LOD slot, then switch to that, and schedule for regeneration
                    if (newSlot != null && newSlot != string.Empty && newSlot != slot.slotName)
                    {
                        _umaData.umaRecipe.slotDataList[i] = UMAAssetIndexer.Instance.InstantiateSlot(newSlot, slot.GetOverlayList());
                        changedSlots = true;
                    }
                }
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_umaData);
#endif
            return changedSlots;
        }
    }
}