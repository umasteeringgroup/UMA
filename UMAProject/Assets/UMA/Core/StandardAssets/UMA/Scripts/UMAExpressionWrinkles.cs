using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Gives an UMA character expression wrinkles. Each build, it adds the wrinkle overlay to the head slot and switches
    /// that slot to the overlay's UMA Material, which has the two wrinkle channels (<c>_WrinkleMap</c>,
    /// <c>_WrinkleControlMap</c>). UMA then builds both maps into the atlas like any other channel. No slot, overlay or
    /// recipe asset is changed. The zone weights are set per character, on the renderer (a MaterialPropertyBlock), so
    /// characters that share a generated material still wrinkle on their own. With a <see cref="profile"/>, the weights
    /// follow the character's facial expressions (Dynamic Expression Player, or the classic Expression Player).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UMA/Expression Wrinkles")]
    public class UMAExpressionWrinkles : MonoBehaviour
    {
        [Tooltip("Overlay with the wrinkle maps in the wrinkle channels and nothing in the others. Its UMA Material is used for the head unless Wrinkle Material is set. Empty: the Global Library's " + UMAWrinkleZones.UMA3OverlayName + ".")]
        public OverlayDataAsset wrinkleOverlay;

        [Tooltip("Optional. A UMA Material for the head with the wrinkle channels, for example a wrinkle copy of your own skin. Empty: the overlay's UMA Material.")]
        public UMAMaterial wrinkleMaterial;

        [Tooltip("Slots that get the wrinkle overlay. The wrinkle maps are laid out for these slots' UVs.")]
        public string[] headSlots = { UMAWrinkleZones.UMA3HeadSlotName };

        [Tooltip("Sets the zone weights from the character's facial expressions every frame. Empty: the weights are set by hand or from code.")]
        public UMAWrinkleProfile profile;

        [SerializeField, Tooltip("Zone weights, -1 (stretched) to 1 (compressed). Index 0 is zone 1.")]
        private float[] zoneWeights = new float[UMAWrinkleZones.ZoneCount];

        private DynamicExpressionPlayer dynamicPlayer;
        private PoseTools.ExpressionPlayer legacyPlayer;
        private UMAWrinkleProfile boundProfile;
        private int boundRuleCount = -1;
        private bool rulesDirty = true;
        private int nextSourceSearch;
        private int[] ruleSource = new int[0];
        private float[] ruleNeutral = new float[0];
        private float[] ruleValues = new float[0];
        private readonly float[] driven = new float[UMAWrinkleZones.ZoneCount];

        private static int[] weightIds;
        private static bool suppressRebuild;
        private UMAData hookedData;
        private CharacterSystem.DynamicCharacterAvatar hookedAvatar;
        private MaterialPropertyBlock block;
        private bool weightsDirty = true;
        private readonly List<KeyValuePair<Renderer, int>> targets = new List<KeyValuePair<Renderer, int>>();

        /// <summary>The overlay this component adds, after the Global Library fallback.</summary>
        public OverlayDataAsset ResolvedOverlay
        {
            get
            {
                if (wrinkleOverlay != null) return wrinkleOverlay;
                var indexer = UMAAssetIndexer.Instance;
                return indexer != null ? indexer.GetAsset<OverlayDataAsset>(UMAWrinkleZones.UMA3OverlayName) : null;
            }
        }

        /// <summary>The UMA Material the head slots use.</summary>
        public UMAMaterial ResolvedMaterial
        {
            get
            {
                if (wrinkleMaterial != null) return wrinkleMaterial;
                var overlay = ResolvedOverlay;
                return overlay != null ? overlay.material : null;
            }
        }

        /// <summary>True while a profile sets the zone weights from expressions; <see cref="SetWeight"/> is then overwritten every frame.</summary>
        public bool IsDriven => profile != null && isActiveAndEnabled;

        public float GetWeight(UMAWrinkleZone zone)
        {
            EnsureWeights();
            return zoneWeights[ZoneIndex(zone)];
        }

        /// <summary>Sets one zone's weight, -1 (stretched) to 1 (compressed). Cheap to call every frame.</summary>
        public void SetWeight(UMAWrinkleZone zone, float weight)
        {
            EnsureWeights();
            int index = ZoneIndex(zone);
            weight = Mathf.Clamp(weight, -1f, 1f);
            if (zoneWeights[index] == weight) return;
            zoneWeights[index] = weight;
            weightsDirty = true;
        }

        /// <summary>Sets every zone to 0, which also skips every wrinkle texture read in the shader.</summary>
        public void ClearWeights()
        {
            EnsureWeights();
            Array.Clear(zoneWeights, 0, zoneWeights.Length);
            weightsDirty = true;
        }

        /// <summary>The project's expression wrinkle setting (UMA Settings), Per Avatar when there are no settings.</summary>
        public static UMASettings.ExpressionWrinkleMode ProjectMode
        {
            get
            {
                var settings = UMASettings.GetSettings();
                return settings != null ? settings.expressionWrinkles : UMASettings.ExpressionWrinkleMode.PerAvatar;
            }
        }

        private static bool ProjectOff => ProjectMode == UMASettings.ExpressionWrinkleMode.Off;

        /// <summary>
        /// With the project set to All Avatars, gives <paramref name="avatar"/> the component (with the project's overlay
        /// and profile) unless it has one. The added component is never saved with the scene or prefab. Called by the
        /// avatar at the start of each build, so the build picks it up without a second one.
        /// </summary>
        public static UMAExpressionWrinkles AddForProjectSetting(CharacterSystem.DynamicCharacterAvatar avatar)
        {
            if (avatar == null) return null;
            var existing = avatar.GetComponent<UMAExpressionWrinkles>();
            if (existing != null) return existing;
            var settings = UMASettings.GetSettings();
            if (settings == null || settings.expressionWrinkles != UMASettings.ExpressionWrinkleMode.AllAvatars) return null;
            suppressRebuild = true;
            try
            {
                var component = avatar.gameObject.AddComponent<UMAExpressionWrinkles>();
                component.hideFlags = HideFlags.DontSave;
                if (settings.wrinkleOverlay != null) component.wrinkleOverlay = settings.wrinkleOverlay;
                if (settings.wrinkleProfile != null) component.profile = settings.wrinkleProfile;
                return component;
            }
            finally { suppressRebuild = false; }
        }

        /// <summary>True when <paramref name="material"/> has the two wrinkle channels.</summary>
        public static bool HasWrinkleChannels(UMAMaterial material)
        {
            if (material == null || material.channels == null) return false;
            bool wrinkle = false, control = false;
            foreach (var channel in material.channels)
            {
                wrinkle |= channel.materialPropertyName == UMAWrinkleZones.WrinkleMapProperty;
                control |= channel.materialPropertyName == UMAWrinkleZones.ControlMapProperty;
            }
            return wrinkle && control;
        }

        private void OnEnable()
        {
            Hook();
            weightsDirty = true;
            // A character built before the component was enabled needs one more build to get the wrinkle channels.
            if (!suppressRebuild && !ProjectOff && IsBuilt(hookedData) && !HasWrinkleMaterial(hookedData)) RebuildCharacter();
        }

        private void OnDisable()
        {
            // The wrinkles disappear at once: without weights the shader reads none of its wrinkle textures.
            // The head keeps the wrinkle material until the character is built again (see RebuildCharacter).
            ClearTargets();
            if (hookedData != null) RemoveFrom(hookedData);
            Unhook();
        }

        private void OnValidate()
        {
            EnsureWeights();
            for (int i = 0; i < zoneWeights.Length; i++) zoneWeights[i] = Mathf.Clamp(zoneWeights[i], -1f, 1f);
            weightsDirty = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private void LateUpdate()
        {
            if (hookedData == null) Hook();
            if (ProjectOff)
            {
                if (targets.Count > 0) ClearTargets();
                return;
            }
            // A built character whose head has no wrinkle material (another race, say) has nothing to drive.
            bool nothingToDrive = IsBuilt(hookedData) && targets.Count == 0 && !weightsDirty;
            if (profile != null && !nothingToDrive) DriveFromExpressions();
            else if (boundProfile != null)
            {
                // The profile was removed: leave the face neutral rather than frozen in its last expression.
                boundProfile = null;
                ClearWeights();
            }
            if (weightsDirty) ApplyWeights();
        }

        /// <summary>Sets the zone weights from the expression player's current values through <see cref="profile"/>.</summary>
        private void DriveFromExpressions()
        {
            if (!ResolveExpressionSource()) return;
            int count = profile.rules != null ? profile.rules.Count : 0;
            if (rulesDirty || boundProfile != profile || boundRuleCount != count) BindRules(count);
            float[] legacyValues = dynamicPlayer == null ? legacyPlayer.Values : null;
            for (int i = 0; i < count; i++)
            {
                int index = ruleSource[i];
                float value = 0f;
                if (index >= 0)
                {
                    if (dynamicPlayer != null)
                    {
                        float raw;
                        if (dynamicPlayer.TryGetExpression(index, out raw)) value = UMAWrinkleProfile.Signed(raw, ruleNeutral[i]);
                    }
                    else value = Mathf.Clamp(legacyValues[index], -1f, 1f);
                }
                ruleValues[i] = value;
            }
            profile.Evaluate(ruleValues, driven);
            for (int z = 0; z < driven.Length; z++) SetWeight((UMAWrinkleZone)(z + 1), driven[z]);
        }

        /// <summary>Finds the expression player on this character: the Dynamic Expression Player first, then a classic one.</summary>
        private bool ResolveExpressionSource()
        {
            if (dynamicPlayer != null || legacyPlayer != null) return true;
            if (dynamicPlayer is object || legacyPlayer is object) ReleaseExpressionSource(); // destroyed
            // Avatars add their player while building; look again now and then, not every frame.
            if (Time.frameCount < nextSourceSearch) return false;
            nextSourceSearch = Time.frameCount + 30;
            dynamicPlayer = GetComponent<DynamicExpressionPlayer>();
            if (dynamicPlayer != null)
            {
                dynamicPlayer.GroupReboundAction -= OnGroupRebound;
                dynamicPlayer.GroupReboundAction += OnGroupRebound;
            }
            else
            {
                dynamicPlayer = null;
                legacyPlayer = GetComponent<PoseTools.ExpressionPlayer>();
                if (legacyPlayer == null) legacyPlayer = null;
            }
            rulesDirty = true;
            return dynamicPlayer != null || legacyPlayer != null;
        }

        private void ReleaseExpressionSource()
        {
            if (dynamicPlayer != null) dynamicPlayer.GroupReboundAction -= OnGroupRebound;
            dynamicPlayer = null;
            legacyPlayer = null;
            nextSourceSearch = 0;
            rulesDirty = true;
        }

        private void OnGroupRebound(UMAExpressionGroup group)
        {
            rulesDirty = true;
        }

        /// <summary>Looks up each rule's expression once, so the per-frame work is index reads only.</summary>
        private void BindRules(int count)
        {
            if (ruleSource.Length != count)
            {
                ruleSource = new int[count];
                ruleNeutral = new float[count];
                ruleValues = new float[count];
            }
            var group = dynamicPlayer != null ? dynamicPlayer.ResolvedGroup : null;
            for (int i = 0; i < count; i++)
            {
                string id = profile.rules[i].expression;
                int index = -1;
                float neutral = 0.5f;
                if (!string.IsNullOrEmpty(id))
                {
                    if (dynamicPlayer != null)
                    {
                        if (!dynamicPlayer.TryGetExpressionIndex(id, out index)) index = -1;
                        UMAExpressionDefinition definition;
                        if (group != null && group.TryGetDefinition(id, out definition)) neutral = definition.DefaultValue;
                    }
                    else
                    {
                        var names = PoseTools.ExpressionPlayer.PoseNames;
                        for (int n = 0; n < names.Length; n++)
                            if (string.Equals(names[n], id, StringComparison.OrdinalIgnoreCase)) { index = n; break; }
                    }
                }
                ruleSource[i] = index;
                ruleNeutral[i] = neutral;
            }
            boundProfile = profile;
            boundRuleCount = count;
            rulesDirty = false;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            wrinkleOverlay = UnityEditor.AssetDatabase.LoadAssetAtPath<OverlayDataAsset>("Assets/UMA/UMA3/Overlays/" + UMAWrinkleZones.UMA3OverlayName + ".asset");
            profile = UnityEditor.AssetDatabase.LoadAssetAtPath<UMAWrinkleProfile>(UMAWrinkleZones.UMA3ProfilePath);
        }
#endif

        private void Hook()
        {
            if (hookedAvatar == null)
            {
                hookedAvatar = GetComponent<CharacterSystem.DynamicCharacterAvatar>();
                if (hookedAvatar != null)
                {
                    // The avatar creates its UMAData on its first build; hook it there, before the generator runs.
                    hookedAvatar.BuildCharacterBegun.RemoveListener(OnBuildBegun);
                    hookedAvatar.BuildCharacterBegun.AddListener(OnBuildBegun);
                }
            }
            var data = hookedAvatar != null && hookedAvatar.umaData != null ? hookedAvatar.umaData : GetComponent<UMAData>();
            if (data == null || data == hookedData) return;
            if (hookedData != null) UnhookData();
            hookedData = data;
            hookedData.OnCharacterBegun -= OnCharacterBegun;
            hookedData.OnCharacterBegun += OnCharacterBegun;
            hookedData.OnCharacterUpdated -= OnCharacterUpdated;
            hookedData.OnCharacterUpdated += OnCharacterUpdated;
        }

        private void Unhook()
        {
            ReleaseExpressionSource();
            UnhookData();
            if (hookedAvatar != null) hookedAvatar.BuildCharacterBegun.RemoveListener(OnBuildBegun);
            hookedAvatar = null;
        }

        private void UnhookData()
        {
            if (hookedData == null) return;
            hookedData.OnCharacterBegun -= OnCharacterBegun;
            hookedData.OnCharacterUpdated -= OnCharacterUpdated;
            hookedData = null;
        }

        private void OnBuildBegun(UMAData data)
        {
            Hook();
        }

        private void OnCharacterBegun(UMAData data)
        {
            if (!isActiveAndEnabled || ProjectOff) return;
            AddTo(data);
        }

        private void OnCharacterUpdated(UMAData data)
        {
            weightsDirty = true;
            ApplyWeights();
        }

        /// <summary>Adds the wrinkle overlay and material to the head slots of <paramref name="data"/>'s recipe.</summary>
        private void AddTo(UMAData data)
        {
            var overlay = ResolvedOverlay;
            var material = ResolvedMaterial;
            var slots = data != null && data.umaRecipe != null ? data.umaRecipe.slotDataList : null;
            if (overlay == null || slots == null) return;
            if (!HasWrinkleChannels(material))
            {
                if (Debug.isDebugBuild)
                    Debug.LogWarning("UMA Expression Wrinkles: the UMA Material '" + (material != null ? material.name : "none") + "' has no " + UMAWrinkleZones.WrinkleMapProperty + " and " + UMAWrinkleZones.ControlMapProperty + " channels.", this);
                return;
            }
            foreach (var slot in slots)
            {
                if (!IsHeadSlot(slot)) continue;
                if (slot.GetOverlay(overlay.overlayName) == null) slot.AddOverlay(new OverlayData(overlay));
                slot.altMaterial = material;
            }
        }

        private void RemoveFrom(UMAData data)
        {
            var overlay = ResolvedOverlay;
            var material = ResolvedMaterial;
            var slots = data != null && data.umaRecipe != null ? data.umaRecipe.slotDataList : null;
            if (slots == null) return;
            foreach (var slot in slots)
            {
                if (!IsHeadSlot(slot)) continue;
                if (overlay != null) slot.RemoveOverlay(overlay.overlayName);
                // Without an alternative the slot uses its first overlay's material, as before.
                if (slot.altMaterial == material) slot.altMaterial = null;
            }
        }

        private bool IsHeadSlot(SlotData slot)
        {
            if (slot == null || slot.asset == null || headSlots == null) return false;
            return IsHeadSlotName(slot.slotName, headSlots);
        }

        /// <summary>
        /// True for one of <paramref name="headSlots"/>, or a race's baked copy of one ("&lt;slot&gt;_baked_&lt;race&gt;"),
        /// which keeps the slot's UVs.
        /// </summary>
        public static bool IsHeadSlotName(string slotName, string[] headSlots)
        {
            if (string.IsNullOrEmpty(slotName) || headSlots == null) return false;
            foreach (var head in headSlots)
            {
                if (string.IsNullOrEmpty(head)) continue;
                if (slotName == head) return true;
                if (slotName.Length > head.Length + 7 && slotName.StartsWith(head, StringComparison.Ordinal) &&
                    string.CompareOrdinal(slotName, head.Length, "_baked_", 0, 7) == 0) return true;
            }
            return false;
        }

        /// <summary>True when <paramref name="data"/>'s head already uses the wrinkle material.</summary>
        public bool HasWrinkleMaterial(UMAData data)
        {
            var material = ResolvedMaterial;
            if (material == null || !IsBuilt(data)) return false;
            foreach (var generated in data.generatedMaterials.materials)
                if (generated != null && generated.umaMaterial != null && (generated.umaMaterial == material || generated.umaMaterial.Equals(material)))
                    return true;
            return false;
        }

        private static bool IsBuilt(UMAData data)
        {
            return data != null && data.generatedMaterials != null && data.generatedMaterials.materials != null &&
                data.generatedMaterials.materials.Count > 0;
        }

        /// <summary>
        /// Builds the character again, so the head gets the wrinkle channels (enabled) or loses them (disabled).
        /// Enabling does this by itself; call it after disabling to drop the wrinkle material.
        /// </summary>
        public void RebuildCharacter()
        {
            var data = hookedData != null ? hookedData : GetComponent<UMAData>();
            if (!IsBuilt(data)) return; // Not built yet; the first build picks the wrinkles up.
            var avatar = hookedAvatar != null ? hookedAvatar : GetComponent<CharacterSystem.DynamicCharacterAvatar>();
            if (avatar != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    if (avatar.editorTimeGeneration) avatar.GenerateSingleUMA();
                    return;
                }
#endif
                avatar.BuildCharacter(true);
                return;
            }
            if (isActiveAndEnabled) AddTo(data);
            data.Dirty(false, true, true);
        }

        private void ApplyWeights()
        {
            weightsDirty = false;
            EnsureWeights();
            FindTargets();
            if (targets.Count == 0) return;
            if (block == null) block = new MaterialPropertyBlock();
            if (weightIds == null)
            {
                weightIds = new int[UMAWrinkleZones.ZoneCount];
                for (int i = 0; i < weightIds.Length; i++)
                    weightIds[i] = Shader.PropertyToID(UMAWrinkleZones.WeightProperty((UMAWrinkleZone)(i + 1)));
            }
            foreach (var target in targets)
            {
                if (target.Key == null) continue;
                target.Key.GetPropertyBlock(block, target.Value);
                for (int i = 0; i < weightIds.Length; i++) block.SetFloat(weightIds[i], zoneWeights[i]);
                target.Key.SetPropertyBlock(block, target.Value);
            }
        }

        /// <summary>Finds the renderer material slots that use the wrinkle material, and clears ones that no longer do.</summary>
        private void FindTargets()
        {
            var found = new List<KeyValuePair<Renderer, int>>();
            var material = ResolvedMaterial;
            var data = hookedData;
            if (isActiveAndEnabled && material != null && data != null && data.generatedMaterials != null && data.generatedMaterials.materials != null)
            {
                var renderers = data.GetRenderers();
                foreach (var generated in data.generatedMaterials.materials)
                {
                    if (generated == null || generated.material == null || generated.umaMaterial == null) continue;
                    if (generated.umaMaterial != material && !generated.umaMaterial.Equals(material)) continue;
                    if (renderers == null) continue;
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null) continue;
                        var shared = renderer.sharedMaterials;
                        for (int i = 0; i < shared.Length; i++)
                            if (shared[i] == generated.material) found.Add(new KeyValuePair<Renderer, int>(renderer, i));
                    }
                }
            }
            foreach (var old in targets)
                if (old.Key != null && !found.Contains(old)) old.Key.SetPropertyBlock(null, old.Value);
            targets.Clear();
            targets.AddRange(found);
        }

        private void ClearTargets()
        {
            foreach (var old in targets)
                if (old.Key != null) old.Key.SetPropertyBlock(null, old.Value);
            targets.Clear();
        }

        private void EnsureWeights()
        {
            if (zoneWeights == null || zoneWeights.Length != UMAWrinkleZones.ZoneCount)
            {
                var weights = new float[UMAWrinkleZones.ZoneCount];
                if (zoneWeights != null) Array.Copy(zoneWeights, weights, Math.Min(zoneWeights.Length, weights.Length));
                zoneWeights = weights;
            }
        }

        private static int ZoneIndex(UMAWrinkleZone zone)
        {
            int id = (int)zone;
            if (id < 1 || id > UMAWrinkleZones.ZoneCount) throw new ArgumentOutOfRangeException(nameof(zone));
            return id - 1;
        }
    }
}
