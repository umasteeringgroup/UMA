#define UMA_INTERNALLOD_DIAGNOSTICS

using UnityEngine;
using UMA.CharacterSystem;
using System;
using System.Collections.Generic;
using System.Collections; // added for BitArray
namespace UMA.Examples
{
    public class UMASimpleLOD : MonoBehaviour
    {
#if UNITY_EDITOR && UMA_INTERNALLOD_DIAGNOSTICS
        private static int _lastLoggedFrame = -1;
#endif
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

#if UNITY_EDITOR
        [Header("Editor")]
        [Tooltip("Editor-only override for Current LOD. When enabled, the inspector can force a specific LOD level at edit time.")]
        public bool editorOverrideLOD;

        [Tooltip("Editor-only forced LOD level (0..maxLOD-1) used when 'Editor Override LOD' is enabled.")]
        public int editorForcedLOD;
#endif

        private int _currentLOD = -1;
        private float lastDist = 0.0f;
        private float NextTime = 0.0f;

        [Tooltip("How much time must pass before this is checked again. Default = 0.5 seconds")]
        public float MinCheck = 0.5f;

        [Tooltip("Random Variance in time (added to MinCheck) so that everything doesn't trigger at the same time. Default = 0.25 seconds")]
        public float CheckRange = 0.25f;

        [Tooltip("When useInternalMeshLOD is true, then slots that have been built with LOD will swap LOD levels based on the zone. This is much faster than the previous version that required a mesh rebuild.")]
        public bool useInternalMeshLOD = true;

        /// <summary>
        /// Records what LOD was actually set per slot during UpdateInternalLOD calls.
        /// Read by the editor inspector to show ground-truth LOD status.
        /// </summary>
        [System.Serializable]
        public struct SlotLodEntry
        {
            public string slotName;
            public int globalDesiredLOD;
            public int slotLodCount;
            public int actualChosenLod;
            public bool wasSuppressed;
            public bool wasDroppedByMaxLod;
            public bool hadAnyLOD;
            public int count;       // number of times this slot has been updated
            public double totalMS;  // accumulated milliseconds across updates
        }

        /// <summary>
        /// Per-slot LOD status accumulated across UpdateInternalLOD calls.
        /// Keyed by slot name. Entries are updated in-place; never cleared.
        /// </summary>
        public Dictionary<string, SlotLodEntry> SlotLodStatuses = new Dictionary<string, SlotLodEntry>();

        /// <summary>
        /// Total milliseconds spent in UpdateInternalLOD across all calls.
        /// </summary>
        public double TotalLodUpdateMS { get; private set; }

        private DynamicCharacterAvatar _avatar;
        private UMAData _umaData;

        private bool initialized = false;
        private bool _initialFallbackApplied = false;

        public void SetSwapSlots(bool swapSlots, int lodOffset)
        {
            Debug.Log("SetSwapSlots called with swapSlots=" + swapSlots + " lodOffset=" + lodOffset);
            this.lodOffset = lodOffset;
            this.swapSlots = swapSlots;
            bool changedSlots = ProcessRecipe(_currentLOD);
            if (changedSlots)
            {
                if (_avatar != null)
                {
                    _avatar.ForceUpdate(false, false, false);
                    return;
                }

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
            _umaData = GetComponent<UMAData>();
            if (_avatar != null)
            {
                _avatar.CharacterBegun.AddListener(CharacterBegun);
            }
            else
            {
                if (_umaData != null)
                {
                    _umaData.CharacterCreated.AddListener(CharacterCreated);
                }
            }
        }

        public void OnDisable()
        {
            if (_avatar != null)
            {
                _avatar.CharacterBegun.RemoveListener(CharacterBegun);
            }

            if (_umaData != null)
            {
                _umaData.CharacterCreated.RemoveListener(CharacterCreated);
            }
        }

        private bool TryLateInitialize()
        {
            if (initialized)
            {
                return true;
            }

            if (_avatar == null)
            {
                _avatar = GetComponent<DynamicCharacterAvatar>();
            }

            if (_umaData == null)
            {
                _umaData = GetComponent<UMAData>();
            }

            if (_umaData != null && _umaData.umaRecipe != null)
            {
                initialized = true;
                if (_currentLOD < 0)
                {
                    DoLODCheck(_umaData);
                }
                return true;
            }

            return false;
        }

        public void CharacterCreated(UMAData umaData)
        {
            initialized = true;
            DoLODCheck(umaData);
        }

        public void CharacterBegun(UMAData umaData)
        {
            initialized = true;
            DoLODCheck(umaData);
        }

        private void DoLODCheck(UMAData umaData)
        {
            //Debug.Log("Performing LOD check for " + gameObject.name);
            if (PerformLodCheck())
            {
                _initialFallbackApplied = false;
                return;
            }

            // If a check fails due to transient readiness issues (camera/renderer/update pending),
            // avoid repeatedly forcing LOD0 and slot swaps. Apply a single startup fallback only.
            if (_currentLOD < 0 && !_initialFallbackApplied)
            {
                _currentLOD = 0;
                _initialFallbackApplied = true;
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
            Debug.Log("Manually processing LOD level " + lodLevel + " for " + gameObject.name);
            if (_umaData == null)
            {
                _umaData = gameObject.GetComponent<UMAData>();
            }
            if (_umaData == null)
            {
                Debug.Log("UMAData not found!");
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

#if UNITY_EDITOR
        public int GetEditorDesiredLODLevel()
        {
            if (!editorOverrideLOD)
            {
                return -1;
            }

            int max = maxLOD;
            if (max < 1)
            {
                max = 1;
            }
            return Mathf.Clamp(editorForcedLOD, 0, max - 1);
        }
#endif

        public void Update()
        {
            if (!initialized && !TryLateInitialize())
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
            //Debug.Log("PerformLodCheck called for " + gameObject.name);
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
            if (_umaData.GetRenderer(0) == null)
            {
                                return false;
            }

            if (_avatar == null)
            {
                _avatar = gameObject.GetComponent<DynamicCharacterAvatar>();
            }
            if (_avatar != null && _avatar.UpdatePending())
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
                if (useInternalMeshLOD && updatedSlots)
                {
                    Debug.LogWarning("Warning! UMASimpleLOD: useInternalMeshLOD is true, but slots were swapped. This may cause internal LOD to be out of sync with the actual slot data.");
                }
            }

            if (_currentLOD != effectiveLevel)
            {
                lastDist = cameraDistance;
                _currentLOD = effectiveLevel;

                // Only update internal mesh LOD when the LOD level actually changes
                if (useInternalMeshLOD)
                {
                    UpdateInternalLOD();
                }
            }

            if (updatedTextures || updatedSlots)
            {
                Debug.Log($"[UMASimpleLOD] LOD {effectiveLevel} applied for {gameObject.name}: " +
                    $"atlasScale={_umaData.atlasResolutionScale}, updatedTextures={updatedTextures}, updatedSlots={updatedSlots}");
                if (_avatar != null)
                {
                    Debug.Log($"[UMASimpleLOD] Forcing _avatar update for {gameObject.name} due to LOD change. updatedTextures={updatedTextures}, updatedSlots={updatedSlots}");
                    _avatar.ForceUpdate(updatedSlots, updatedTextures, updatedSlots);
                    return true;
                }
                else
                {
                    Debug.Log($"[UMASimpleLOD] Forcing UMAData update for {gameObject.name} due to LOD change. updatedTextures={updatedTextures}, updatedSlots={updatedSlots}");
                    _umaData.Dirty(updatedSlots, updatedTextures, updatedSlots);
                }
            }

            return true;
        }

        public void UpdateInternalLOD()
        {
           // Debug.Log("Updating Internal LOD");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (_umaData == null || _umaData.umaRecipe == null || _umaData.GetRenderers() == null)
            {
                return;
            }

            // Determine the new LOD level (account for lodOffset like slot LOD switching)
            int desiredLOD = _currentLOD - lodOffset;
            if (desiredLOD < 0) desiredLOD = 0;
            if (desiredLOD >= maxLOD) desiredLOD = maxLOD - 1;

            // Early out if nothing to do
            // NOTE: We cannot early-out solely based on UMAData.currentLODLevel, because the renderer meshes
            // may have been modified by other edit-time operations and need indices rebuilt even when the
            // desired level matches UMAData.

            var slots = _umaData.umaRecipe.slotDataList;
            if (slots == null || slots.Length == 0)
            {
                return;
            }

            // First pass: check for any LOD slots AND populate per-slot tracking dictionary
            bool anyHasLOD = false;
            for (int i = 0; i < slots.Length; i++)
            {
                var sd = slots[i];
                if (sd == null) continue;

                string key = sd.slotName;
                if (string.IsNullOrEmpty(key)) continue;

                // Get or create the entry for this slot
                SlotLodEntry entry;
                if (!SlotLodStatuses.TryGetValue(key, out entry))
                {
                    entry = new SlotLodEntry { slotName = key };
                }

                entry.globalDesiredLOD = desiredLOD;
                entry.slotLodCount = 0;
                entry.actualChosenLod = 0;
                entry.wasSuppressed = sd.Suppressed;
                entry.wasDroppedByMaxLod = false;
                entry.hadAnyLOD = false;
                entry.count++;

                if (sd.asset != null && !UMAMeshData.IsNullOrEmptyMeshData(sd.asset.meshData))
                {
                    int sm = sd.asset.subMeshIndex;
                    if (sm >= 0 && sm < sd.asset.meshData.subMeshCount)
                    {
                        var smt = sd.asset.meshData.submeshes[sm];
                        if (smt != null)
                        {
                            int lodCount = smt.LODCount();
                            entry.slotLodCount = lodCount;
                            if (lodCount > 1)
                            {
                                anyHasLOD = true;
                                entry.hadAnyLOD = true;

                                int slotDesired = desiredLOD;
                                if (slotDesired < 0) slotDesired = 0;
                                if (slotDesired >= lodCount) slotDesired = lodCount - 1;

                                int chosenLod = lodCount - 1;
                                for (int l = slotDesired; l < lodCount; l++)
                                {
                                    if (smt.HasLODLevel(l) && smt.GetTriangleCount(l) > 0)
                                    {
                                        chosenLod = l;
                                        break;
                                    }
                                }
                                entry.actualChosenLod = chosenLod;
                            }
                        }
                    }
                }

                // Check MaxLod dropping (matches the renderer loop condition)
                if (!entry.wasSuppressed && sd.MaxLod > -1 && desiredLOD > sd.MaxLod)
                {
                    entry.wasDroppedByMaxLod = true;
                }

                SlotLodStatuses[key] = entry;
            }

            if (!anyHasLOD)
            {
                Debug.Log("[UMASimpleLOD] No slots with multiple LODs found; skipping internal LOD update.");
                // Nothing with multiple LODs — just keep UMAData in sync and exit
                _umaData.currentLODLevel = desiredLOD;
                return;
            }

            // Update UMAData LOD and refresh mesh hide masks for this LOD
            _umaData.currentLODLevel = desiredLOD;
            _umaData.umaRecipe.UpdateMeshHideMasks(desiredLOD);

#if UNITY_EDITOR && UMA_INTERNALLOD_DIAGNOSTICS
            // Avoid spamming: log at most once per editor frame.
            if (_lastLoggedFrame != Time.frameCount)
            {
                _lastLoggedFrame = Time.frameCount;
            }
#endif

            // Build per-renderer, per-submesh new index buffers
            var renderers = _umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0) return;

            // Accumulate indices by renderer->submesh
            for (int r = 0; r < renderers.Length; r++)
            {
                var smr = renderers[r];
                if (smr == null || smr.sharedMesh == null) continue;

                int subMeshCount = smr.sharedMesh.subMeshCount;
                if (subMeshCount <= 0) continue;

                // Prepare containers for new indices
                var submeshIndices = new List<int>[subMeshCount];
                for (int i = 0; i < subMeshCount; i++) submeshIndices[i] = new List<int>(1024);

                // Scan all slots that contribute to this renderer
                for (int i = 0; i < slots.Length; i++)
                {
                    var sd = slots[i];
                    if (sd == null || sd.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sd.asset.meshData)) continue;
                    if (sd.skinnedMeshRenderer != r) continue; // only this renderer
                    if (sd.Suppressed) continue; // dropped by LOD rules

                    // If the slot has a MaxLod set and the current LOD exceeds it, skip this slot entirely
                    // (contribute no triangles, effectively hiding it at higher LOD levels)
                    if (sd.MaxLod > -1 && desiredLOD > sd.MaxLod) continue;

                    int sourceSub = sd.asset.subMeshIndex;
                    if (sourceSub < 0 || sourceSub >= sd.asset.meshData.subMeshCount) continue;

                    var smt = sd.asset.meshData.submeshes[sourceSub];
                    if (smt == null) continue;

                    // Retrieve triangles for desired LOD (managed path)
                    int[] localTris = null;

                    int slotLodCount = 0;
                    try
                    {
                        slotLodCount = smt.LODCount();
                    }
                    catch
                    {
                        slotLodCount = 0;
                    }

                    // Slots without internal LOD ranges must always render their base triangles.
                    // (They are not expected to have per-LOD triangle buffers.)
                    if (slotLodCount <= 0)
                    {
                        localTris = smt.GetBaseTriangles();
                    }
                    else
                    {
                        int slotDesired = desiredLOD;
                        if (slotDesired < 0)
                        {
                            slotDesired = 0;
                        }
                        if (slotDesired >= slotLodCount)
                        {
                            slotDesired = slotLodCount - 1;
                        }

                        // If the requested LOD for this slot has no triangles, migrate to next valid higher LOD.
                        int chosenLod = slotLodCount - 1;
                        for (int l = slotDesired; l < slotLodCount; l++)
                        {
                            if (!smt.HasLODLevel(l))
                            {
                                continue;
                            }
                            if (smt.GetTriangleCount(l) > 0)
                            {
                                chosenLod = l;
                                break;
                            }
                        }

                        if (chosenLod <= 0)
                        {
                            localTris = smt.GetBaseTriangles();
                        }
                        else
                        {
                            localTris = smt.getManagedTriangles(chosenLod);
                        }
                    }
                    if (localTris == null || localTris.Length == 0) continue;

                    // Always work on a copy since we will apply vertex offsets and may filter
                    var localTrisCopy = new int[localTris.Length];
                    Array.Copy(localTris, localTrisCopy, localTris.Length);
                    localTris = localTrisCopy;

                    // Apply mesh hide if present (mask is per-submesh BitArray[]; true bits mean remove)
                    BitArray[] masks = sd.meshHideMask;
                    BitArray mask = null;
                    if (masks != null && masks.Length > 0)
                    {
                        if (sourceSub >= 0 && sourceSub < masks.Length)
                        {
                            mask = masks[sourceSub];
                        }
                        else if (masks.Length == 1)
                        {
                            // Some assets may provide a single mask for all submeshes
                            mask = masks[0];
                        }
                    }
                    if (mask != null && mask.Length > 0)
                    {
#if UNITY_EDITOR && UMA_INTERNALLOD_DIAGNOSTICS
                        int beforeLen = localTris.Length;
#endif
                        if (mask.Length == localTris.Length)
                        {
                            // per-index mask: true means hide index
                            var filtered = new List<int>(localTris.Length);
                            for (int k = 0; k < localTris.Length; k += 3)
                            {
                                if (mask[k] || mask[k + 1] || mask[k + 2])
                                {
                                    continue;
                                }
                                filtered.Add(localTris[k]);
                                filtered.Add(localTris[k + 1]);
                                filtered.Add(localTris[k + 2]);
                            }
                            localTris = filtered.Count > 0 ? filtered.ToArray() : Array.Empty<int>();
                        }
                        else if (mask.Length == (localTris.Length / 3))
                        {
                            // per-triangle mask: true means remove triangle
                            var filtered = new List<int>(localTris.Length);
                            for (int t = 0, k = 0; t < mask.Length; t++, k += 3)
                            {
                                if (mask[t]) continue;
                                filtered.Add(localTris[k]);
                                filtered.Add(localTris[k + 1]);
                                filtered.Add(localTris[k + 2]);
                            }
                            localTris = filtered.Count > 0 ? filtered.ToArray() : Array.Empty<int>();
                        }

                        int afterLen = localTris.Length;
                    }

                    if (localTris.Length == 0) continue;

                    // Remap by vertex offset into combined mesh
                    int vOffset = sd.vertexOffset;
                    for (int k = 0; k < localTris.Length; k++)
                    {
                        localTris[k] += vOffset;
                    }

                    // Target combined submesh that this slot contributes to
                    int targetSubmesh = sd.submeshIndex;
                    if (targetSubmesh < 0 || targetSubmesh >= subMeshCount) continue;

                    submeshIndices[targetSubmesh].AddRange(localTris);
                }

                CopySecondPassSubmeshIndices(r, submeshIndices);

                // Push new indices back to the Mesh. Only indices change; vertices, bones stay intact.
                var mesh = smr.sharedMesh;
                for (int sm = 0; sm < subMeshCount; sm++)
                {
                    //Debug.Log("Updating renderer " + r + " submesh " + sm + " with " + submeshIndices[sm].Count + " indices for LOD " + desiredLOD);    
                    var arr = submeshIndices[sm].Count > 0 ? submeshIndices[sm].ToArray() : Array.Empty<int>();
                    // SetIndices updates only the given submesh
                    mesh.SetIndices(arr, MeshTopology.Triangles, sm, false);
                }
                // Not changing bounds; leave as-is for performance. If needed: mesh.RecalculateBounds();
            }

            sw.Stop();
            double elapsedMS = sw.Elapsed.TotalMilliseconds;
            TotalLodUpdateMS += elapsedMS;

            // Add the elapsed time to every slot that was part of this update.
            // Snapshot keys first — dict[key]=value bumps the version counter in Unity's runtime.
            var keys = new List<string>(SlotLodStatuses.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var entry = SlotLodStatuses[keys[i]];
                entry.totalMS += elapsedMS;
                SlotLodStatuses[keys[i]] = entry;
            }

#if UNITY_EDITOR
            // Force the inspector to repaint so the LOD status grid updates promptly
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void CopySecondPassSubmeshIndices(int rendererIndex, List<int>[] submeshIndices)
        {
            if (_umaData == null || _umaData.generatedMaterials == null || _umaData.generatedMaterials.materials == null || submeshIndices == null)
                return;

            var rendererAsset = _umaData.GetRendererAsset(rendererIndex);
            var materials = _umaData.generatedMaterials.materials;
            int finalSubmeshIndex = 0;

            for (int i = 0; i < materials.Count; i++)
            {
                var generatedMaterial = materials[i];
                if (generatedMaterial == null || generatedMaterial.rendererAsset != rendererAsset)
                    continue;

                int firstPassIndex = finalSubmeshIndex++;
                if (generatedMaterial.umaMaterial == null || generatedMaterial.umaMaterial.secondPass == null)
                    continue;

                int secondPassIndex = finalSubmeshIndex++;
                if (firstPassIndex < 0 || firstPassIndex >= submeshIndices.Length || secondPassIndex < 0 || secondPassIndex >= submeshIndices.Length)
                    continue;

                submeshIndices[secondPassIndex].Clear();
                submeshIndices[secondPassIndex].AddRange(submeshIndices[firstPassIndex]);
            }
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
            //Debug.Log("Applying hysteresis: currentLevel=" + currentLevel + " targetLevel=" + targetLevel + " cameraDistance=" + cameraDistance);
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
                    return currentSlotName;
                }
            }

            // get all the Lods. Fill out the lodlevels.
            string[] SlotLods = new string[maxLOD];
            string lastSlot = currentSlotName;
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
            //Debug.Log("Processing recipe for LOD level " + currentLevel);
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
                        if (slot.MaxLod > -1 && currentLevel > slot.MaxLod)
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
                        SlotData replacementSlot = UMAAssetIndexer.Instance.InstantiateSlot(newSlot);
                        if (replacementSlot != null)
                        {
                            replacementSlot.SetOverlayList(slot.GetOverlayList());
                            _umaData.umaRecipe.slotDataList[i] = replacementSlot;
                            changedSlots = true;
                        }
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