using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    /// <summary>
    /// A temporary character for the Rig Profile Editor: a generated character of a race wearing
    /// legacy slots converted with the profile being edited. <see cref="Refresh"/> converts the slots
    /// again and rebuilds the character's mesh, so a profile change shows at once.
    /// </summary>
    /// <remarks>
    /// The preview makes its own converted copies with the same settings automatic conversion uses,
    /// so what it shows is what a built character will get. Nothing it creates is saved: the
    /// character is a DontSave scene object, and the copies are destroyed with it.
    /// </remarks>
    public sealed class UMARigProfilePreview : IDisposable
    {
        public sealed class PreviewSlot
        {
            /// <summary>The slot asset from the project.</summary>
            public SlotDataAsset Original;
            /// <summary>The converted copy the character wears; null when the slot is worn as it is.</summary>
            public SlotDataAsset Copy;
            public SlotData SlotData;
            public bool Converted;
            public string Note;
        }

        /// <summary>How much of one previewed slot stands inside an effector.</summary>
        public struct EffectorReach
        {
            public string SlotName;
            /// <summary>Vertices of the slot inside the effector, counted where the bone entries leave them, before any effector moves them.</summary>
            public int Inside;
        }

        /// <summary>One vertex of a previewed slot that stands inside an effector.</summary>
        public struct EffectorVertex
        {
            /// <summary>Where the preview shows the vertex, in avatar space at rest: after every effector that acts on its slot has moved it.</summary>
            public Vector3 Position;
            /// <summary>How strongly the effector holds it, from just above 0 at the edge to 1 in the core. Taken where the vertex stood before any effector.</summary>
            public float Strength;
            /// <summary>False when the vertex belongs to a slot the effector leaves alone because of the slot it is kept to.</summary>
            public bool ActedOn;
        }

        private readonly List<PreviewSlot> slots = new List<PreviewSlot>();
        private readonly List<UMATextRecipe> wornRecipes = new List<UMATextRecipe>();
        // The converted slots in avatar space as the bone entries leave them, before any effector,
        // and the bone entries they were converted with.
        private readonly Dictionary<PreviewSlot, Vector3[]> positionsBeforeEffectors = new Dictionary<PreviewSlot, Vector3[]>();
        private string positionsBeforeEffectorsKey;
        // The converted slots in avatar space as the preview shows them, and the conversion they are from.
        private readonly Dictionary<PreviewSlot, Vector3[]> positionsShown = new Dictionary<PreviewSlot, Vector3[]>();
        private int positionsShownFor = -1;
        private UMAGenerator generator;

        /// <summary>
        /// Whether the preview applies the Mesh Hide Assets of the recipes it wears, as a built
        /// character would. Off shows the whole body, to look for poke-through.
        /// </summary>
        public bool ShowMeshHides { get; private set; } = true;

        /// <summary>How many body triangles the worn recipes' mesh hides remove from the preview.</summary>
        public int HiddenTriangles { get; private set; }

        /// <summary>
        /// Whether the character wears the previewed slots. Off leaves the bare body, with the mesh
        /// hides still applied when <see cref="ShowMeshHides"/> is on, to see what lies under the clothing.
        /// </summary>
        public bool ShowClothing { get; private set; } = true;

        /// <summary>The recipes the preview was built with; their Mesh Hide Assets are the ones applied.</summary>
        public IReadOnlyList<UMATextRecipe> WornRecipes => wornRecipes;

        public RaceData Race { get; private set; }
        public UMASlotRigConversionProfile Profile { get; private set; }
        public UMARigRestPose LegacyRig { get; private set; }
        public UMARigRestPose TargetRig { get; private set; }
        /// <summary>The character's skin at rest, which the profile's "clear the body" effectors measure the clothing against.</summary>
        public UMARigBodySurface BodySurface { get; private set; }
        public GameObject AvatarObject { get; private set; }
        public DynamicCharacterAvatar Avatar { get; private set; }
        public IReadOnlyList<PreviewSlot> Slots => slots;

        /// <summary>Problems and notes from the last build, for the editor to show.</summary>
        public readonly List<string> Messages = new List<string>();

        public double LastConvertMilliseconds { get; private set; }
        /// <summary>How many times the slots have been converted; changes whenever what the preview shows may have.</summary>
        public int ConvertCount { get; private set; }
        public double LastBuildMilliseconds { get; private set; }

        /// <summary>
        /// True while the character stands in the base slots' rest pose, the pose profile offsets are
        /// defined in. False shows the race's T-pose instead.
        /// </summary>
        public bool RestPose { get; private set; } = true;

        public bool IsBuilt => AvatarObject != null && Avatar != null && Avatar.umaData != null;

        /// <summary>
        /// Builds the character and dresses it. Recipes contribute their slots with their overlays;
        /// loose slots get an overlay found near the slot asset. Returns false, with the cause in
        /// <see cref="Messages"/>, when the character cannot be built.
        /// </summary>
        public bool Build(RaceData race, UMASlotRigConversionProfile profile, IList<UMATextRecipe> recipes, IList<SlotDataAsset> looseSlots, bool restPose = true)
        {
            Dispose();
            Messages.Clear();
            if (race == null)
            {
                Messages.Add("Choose a race.");
                return false;
            }
            if (race.legacyRig == null)
            {
                Messages.Add($"'{race.raceName}' has no Legacy Rig, so there is nothing to convert from. Assign one on the race.");
                return false;
            }
            if (!UMARigRestPose.TryFromRace(race, out UMARigRestPose target, out string reason))
            {
                Messages.Add(reason);
                return false;
            }
            generator = UMAAssetIndexer.Instance != null ? UMAAssetIndexer.Instance.Generator : null;
            if (generator == null)
            {
                Messages.Add("No UMA generator is available to build the preview character.");
                return false;
            }

            Race = race;
            Profile = profile;
            LegacyRig = race.legacyRig.GetRestPose();
            TargetRig = target;

            AvatarObject = new GameObject("Rig Profile Preview (not saved)") { hideFlags = HideFlags.DontSave };
            Avatar = AvatarObject.AddComponent<DynamicCharacterAvatar>();
            // Only the preview drives this character. With edit-time generation on, the avatar queues
            // its own delayed rebuild (while it initialises inside GenerateNow, from Start, and from
            // Update whenever its mesh is missing). That rebuild clears the character and builds it
            // again from the avatar's own, empty wardrobe, dropping the preview's slots and pose. So
            // the flag is off before the first build, and the component is disabled after it.
            Avatar.editorTimeGeneration = false;
            Avatar.showPlaceholder = false;
            Avatar.activeRace.name = race.raceName;
            Avatar.activeRace.data = race;
            if (Avatar.predefinedDNA == null)
            {
                // An avatar added from code has none, and the avatar's own build code assumes one.
                Avatar.predefinedDNA = new UMAPredefinedDNA();
            }
            Avatar.GenerateNow();
            Avatar.enabled = false;
            if (Avatar.umaData == null)
            {
                Messages.Add("The preview character could not be generated.");
                Dispose();
                return false;
            }

            // The character wears only its body at this point.
            var bodyAssets = new List<SlotDataAsset>();
            foreach (SlotData bodySlot in Avatar.umaData.umaRecipe.slotDataList)
            {
                if (bodySlot != null && bodySlot.asset != null)
                {
                    bodyAssets.Add(bodySlot.asset);
                }
            }
            BodySurface = UMARigBodySurface.FromSlots(bodyAssets, TargetRig);

            var seen = new HashSet<SlotDataAsset>();
            if (recipes != null)
            {
                for (int r = 0; r < recipes.Count; r++)
                {
                    UMATextRecipe recipe = recipes[r];
                    if (recipe == null)
                    {
                        continue;
                    }
                    wornRecipes.Add(recipe);
                    UMAData.UMARecipe cached = recipe.GetCachedRecipe(true);
                    if (cached == null || cached.slotDataList == null)
                    {
                        Messages.Add($"Recipe '{recipe.name}' has no slots.");
                        continue;
                    }
                    int usable = 0;
                    for (int i = 0; i < cached.slotDataList.Length; i++)
                    {
                        SlotData slotData = cached.slotDataList[i];
                        if (slotData == null || slotData.asset == null)
                        {
                            continue;
                        }
                        usable++;
                        if (seen.Add(slotData.asset))
                        {
                            AddSlot(slotData.Copy(), slotData.asset);
                        }
                    }
                    if (usable == 0)
                    {
                        // A recipe whose slots are not in the UMA asset index unpacks to nothing.
                        Messages.Add($"Recipe '{recipe.name}' gave no slots. Its slots may be missing from the UMA Global Library.");
                    }
                }
            }
            if (looseSlots != null)
            {
                for (int i = 0; i < looseSlots.Count; i++)
                {
                    SlotDataAsset slot = looseSlots[i];
                    if (slot == null || !seen.Add(slot))
                    {
                        continue;
                    }
                    OverlayDataAsset overlay = FindOverlayFor(slot);
                    if (overlay == null)
                    {
                        Messages.Add($"No overlay was found near slot '{slot.slotName}'; add it through a recipe to preview it.");
                        continue;
                    }
                    var slotData = new SlotData(slot);
                    slotData.AddOverlay(new OverlayData(overlay));
                    AddSlot(slotData, slot);
                }
            }
            if (slots.Count == 0)
            {
                Messages.Add("Nothing to wear yet: add a recipe or slot built for the legacy rig.");
            }

            ConvertAll();
            UMAData umaData = Avatar.umaData;
            SyncSlotsWithRecipe(umaData);
            UpdateHideMasks(umaData);
            var watch = Stopwatch.StartNew();
            umaData.Dirty(true, true, true);
            generator.GenerateSingleUMA(umaData, false);
            LastBuildMilliseconds = watch.Elapsed.TotalMilliseconds;
            SetPose(restPose);
            return true;
        }

        /// <summary>
        /// Converts the legacy slots again with the profile as it is now and rebuilds the character's
        /// mesh. Call after the profile changes.
        /// </summary>
        public bool Refresh()
        {
            if (!IsBuilt)
            {
                return false;
            }
            ConvertAll();

            UMAData umaData = Avatar.umaData;
            // The avatar can rebuild its own recipe (for example after an inspector change), which
            // drops the preview slots. Put them back and rebuild everything in that case.
            bool readded = SyncSlotsWithRecipe(umaData);
            UpdateHideMasks(umaData);
            var watch = Stopwatch.StartNew();
            umaData.Dirty(readded, readded, true);
            generator.GenerateSingleUMA(umaData, false);
            LastBuildMilliseconds = watch.Elapsed.TotalMilliseconds;
            SetPose(RestPose);
            return true;
        }

        /// <summary>
        /// Counts, for every slot the preview converts, the vertices that stand inside an effector,
        /// whether or not the effector acts on that slot. This is what tells an effector that is kept
        /// to the wrong slot from one that is in the wrong place.
        /// </summary>
        public void MeasureEffector(UMARigEffector effector, List<EffectorReach> into)
        {
            into.Clear();
            if (!IsBuilt || effector == null || Profile == null)
            {
                return;
            }
            UpdatePositionsBeforeEffectors();
            for (int i = 0; i < slots.Count; i++)
            {
                PreviewSlot slot = slots[i];
                if (slot.Original != null && positionsBeforeEffectors.TryGetValue(slot, out Vector3[] positions))
                {
                    into.Add(new EffectorReach { SlotName = slot.Original.slotName, Inside = effector.CountInside(positions, TargetRig) });
                }
            }
        }

        /// <summary>
        /// Lists every vertex of the converted slots that stands inside an effector or its mirror
        /// image, at the place the preview shows it, with how strongly the effector holds it. Slots
        /// the effector does not act on are included and marked, so an effector kept to the wrong slot
        /// shows what it would have held.
        /// </summary>
        public void CollectEffectorVertices(UMARigEffector effector, List<EffectorVertex> into)
        {
            into.Clear();
            if (!IsBuilt || effector == null || Profile == null || effector.radius <= 0f)
            {
                return;
            }
            UpdatePositionsBeforeEffectors();
            if (positionsShownFor != ConvertCount)
            {
                positionsShownFor = ConvertCount;
                positionsShown.Clear();
                for (int i = 0; i < slots.Count; i++)
                {
                    PreviewSlot slot = slots[i];
                    if (slot.Converted && slot.Copy != null && UMARigBodySurface.TryBakeToAvatar(slot.Copy.meshData, TargetRig, out Vector3[] positions, out _))
                    {
                        positionsShown[slot] = positions;
                    }
                }
            }
            bool held = Profile.TryGetEffectorSlot(effector, out string keptTo);
            for (int i = 0; i < slots.Count; i++)
            {
                PreviewSlot slot = slots[i];
                if (slot.Original == null
                    || !positionsBeforeEffectors.TryGetValue(slot, out Vector3[] before)
                    || !positionsShown.TryGetValue(slot, out Vector3[] shown)
                    || before.Length != shown.Length)
                {
                    continue;
                }
                bool actedOn = held && (keptTo == null || keptTo == slot.Original.slotName);
                for (int v = 0; v < before.Length; v++)
                {
                    float strength = effector.GetWeight(before[v], TargetRig);
                    if (effector.mirror)
                    {
                        strength = Mathf.Max(strength, effector.GetWeight(before[v], TargetRig, true));
                    }
                    if (strength > 0f)
                    {
                        into.Add(new EffectorVertex { Position = shown[v], Strength = strength, ActedOn = actedOn });
                    }
                }
            }
        }

        /// <summary>
        /// Converts the slots with the profile's bone entries and none of its effectors, and keeps
        /// where their vertices land. Done again only when a bone entry has changed.
        /// </summary>
        private void UpdatePositionsBeforeEffectors()
        {
            UMASlotRigConversionProfile bare = Object.Instantiate(Profile);
            bare.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                bare.effectors.Clear();
                for (int i = 0; i < bare.slotOverrides.Count; i++)
                {
                    if (bare.slotOverrides[i] != null)
                    {
                        bare.slotOverrides[i].effectors.Clear();
                    }
                }
                string key = JsonUtility.ToJson(bare);
                if (key == positionsBeforeEffectorsKey)
                {
                    return;
                }
                positionsBeforeEffectorsKey = key;
                positionsBeforeEffectors.Clear();
                var options = new UMASlotRigConversionOptions { Profile = bare };
                for (int i = 0; i < slots.Count; i++)
                {
                    PreviewSlot slot = slots[i];
                    if (!slot.Converted || slot.Original == null)
                    {
                        continue;
                    }
                    options.ProfileSlotName = slot.Original.slotName;
                    if (UMASlotRigConverter.TryConvert(slot.Original.meshData, LegacyRig, TargetRig, options, null, out UMAMeshData converted, out _)
                        && UMARigBodySurface.TryBakeToAvatar(converted, TargetRig, out Vector3[] positions, out _))
                    {
                        positionsBeforeEffectors[slot] = positions;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }

        /// <summary>Turns the worn recipes' mesh hides on or off in the preview and rebuilds it.</summary>
        public void SetShowMeshHides(bool show)
        {
            if (ShowMeshHides == show)
            {
                return;
            }
            ShowMeshHides = show;
            Refresh();
        }

        /// <summary>Puts the previewed slots on the character or takes them off, and rebuilds it.</summary>
        public void SetShowClothing(bool show)
        {
            if (ShowClothing == show)
            {
                return;
            }
            ShowClothing = show;
            Refresh();
        }

        /// <summary>True for a slot the preview added to the character, as opposed to the race's own body.</summary>
        public bool IsPreviewSlot(SlotData slotData)
        {
            if (slotData == null)
            {
                return false;
            }
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].SlotData == slotData)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gives each slot its hide mask from the worn recipes' Mesh Hide Assets and collections, the
        /// way a character build does. The preview adds slots to the recipe itself, so the avatar's own
        /// pass never sees these recipes.
        /// </summary>
        private void UpdateHideMasks(UMAData umaData)
        {
            var dictionary = new Dictionary<string, List<MeshHideAsset>>(StringComparer.Ordinal);
            if (ShowMeshHides)
            {
                for (int r = 0; r < wornRecipes.Count; r++)
                {
                    UMATextRecipe recipe = wornRecipes[r];
                    if (recipe == null)
                    {
                        continue;
                    }
                    if (recipe.MeshHideAssets != null)
                    {
                        for (int i = 0; i < recipe.MeshHideAssets.Count; i++)
                        {
                            AddHide(dictionary, umaData, recipe.MeshHideAssets[i]);
                        }
                    }
                    if (recipe.MeshHideAssetCollections != null)
                    {
                        for (int c = 0; c < recipe.MeshHideAssetCollections.Count; c++)
                        {
                            MeshHideAssetCollection collection = recipe.MeshHideAssetCollections[c];
                            if (collection == null || collection.Assets == null)
                            {
                                continue;
                            }
                            for (int i = 0; i < collection.Assets.Count; i++)
                            {
                                AddHide(dictionary, umaData, collection.Assets[i]);
                            }
                        }
                    }
                }
            }
            umaData.umaRecipe.MeshHideDictionary = dictionary;
            umaData.umaRecipe.UpdateMeshHideMasks();

            int hidden = 0;
            foreach (SlotData slot in umaData.umaRecipe.slotDataList)
            {
                if (slot == null || slot.meshHideMask == null)
                {
                    continue;
                }
                foreach (System.Collections.BitArray mask in slot.meshHideMask)
                {
                    if (mask == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < mask.Count; i++)
                    {
                        if (mask[i]) hidden++;
                    }
                }
            }
            HiddenTriangles = hidden;
        }

        private static void AddHide(Dictionary<string, List<MeshHideAsset>> dictionary, UMAData umaData, MeshHideAsset hide)
        {
            if (hide == null)
            {
                return;
            }
            SlotData slot = hide.FindMatchingSlot(umaData.umaRecipe.slotDataList);
            if (slot == null || slot.asset == null || !hide.IsCompatibleWithSlot(slot.asset, out _))
            {
                return;
            }
            if (!dictionary.TryGetValue(slot.slotName, out List<MeshHideAsset> list))
            {
                list = new List<MeshHideAsset>(2);
                dictionary.Add(slot.slotName, list);
            }
            if (!list.Contains(hide))
            {
                list.Add(hide);
            }
        }

        /// <summary>Shows the character in the rest pose (true) or the race T-pose (false).</summary>
        public void SetPose(bool restPose)
        {
            RestPose = restPose;
            if (!IsBuilt || Avatar.umaData.skeleton == null)
            {
                return;
            }
            UMASkeleton skeleton = Avatar.umaData.skeleton;
            if (!restPose)
            {
                skeleton.ResetAll();
                return;
            }
            foreach (KeyValuePair<int, UMATransform> entry in TargetRig.Bones)
            {
                if (!skeleton.HasBone(entry.Key))
                {
                    continue;
                }
                Transform bone = skeleton.GetBoneTransform(entry.Key);
                if (bone == null)
                {
                    continue;
                }
                bone.localPosition = entry.Value.position;
                bone.localRotation = entry.Value.rotation;
                bone.localScale = entry.Value.scale;
            }
        }

        /// <summary>The preview character's transform for a bone, or null.</summary>
        public Transform GetBone(int boneHash)
        {
            if (!IsBuilt || Avatar.umaData.skeleton == null || !Avatar.umaData.skeleton.HasBone(boneHash))
            {
                return null;
            }
            return Avatar.umaData.skeleton.GetBoneTransform(boneHash);
        }

        /// <summary>Hashes of every bone the previewed slots are skinned to.</summary>
        public HashSet<int> GetSkinnedBoneHashes()
        {
            var result = new HashSet<int>();
            for (int i = 0; i < slots.Count; i++)
            {
                SlotDataAsset asset = slots[i].Copy != null ? slots[i].Copy : slots[i].Original;
                if (asset == null || UMAMeshData.IsNullOrEmptyMeshData(asset.meshData) || asset.meshData.boneNameHashes == null)
                {
                    continue;
                }
                for (int b = 0; b < asset.meshData.boneNameHashes.Length; b++)
                {
                    result.Add(asset.meshData.boneNameHashes[b]);
                }
            }
            return result;
        }

        /// <summary>World-space bounds of the preview character, for framing it in the Scene view.</summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            if (!IsBuilt)
            {
                return false;
            }
            bool any = false;
            foreach (Renderer renderer in AvatarObject.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return any;
        }

        public void Dispose()
        {
            if (Avatar != null)
            {
                try { Avatar.CleanupGeneratedData(); } catch (Exception) { }
            }
            if (AvatarObject != null)
            {
                Object.DestroyImmediate(AvatarObject);
            }
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Copy != null)
                {
                    Object.DestroyImmediate(slots[i].Copy);
                }
            }
            slots.Clear();
            wornRecipes.Clear();
            positionsBeforeEffectors.Clear();
            positionsBeforeEffectorsKey = null;
            positionsShown.Clear();
            positionsShownFor = -1;
            BodySurface = null;
            HiddenTriangles = 0;
            Avatar = null;
            AvatarObject = null;
        }

        private void AddSlot(SlotData slotData, SlotDataAsset original)
        {
            var preview = new PreviewSlot { Original = original, SlotData = slotData };
            UMAMeshData mesh = original.meshData;
            if (UMALegacySlotConverter.IsBuiltForLegacyRig(mesh, LegacyRig, TargetRig))
            {
                if (UMASlotRigConverter.CanConvert(mesh, out string blocker))
                {
                    SlotDataAsset copy = Object.Instantiate(original);
                    copy.name = original.name;
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    copy.isLegacySlot = false;
                    preview.Copy = copy;
                    preview.Converted = true;
                    slotData.asset = copy;
                    if (!original.isLegacySlot)
                    {
                        preview.Note = "isLegacySlot is cleared on this slot, so automatic conversion will skip it.";
                    }
                }
                else
                {
                    preview.Note = "Not converted: " + blocker;
                }
            }
            else
            {
                preview.Note = "Not built for the legacy rig; worn as it is.";
            }
            slots.Add(preview);
        }

        private void ConvertAll()
        {
            var watch = Stopwatch.StartNew();
            if (Profile != null)
            {
                Profile.InvalidateLookup();
            }
            // The same settings automatic conversion uses for the race.
            var options = new UMASlotRigConversionOptions { Profile = Profile, BodySurface = BodySurface };
            for (int i = 0; i < slots.Count; i++)
            {
                PreviewSlot slot = slots[i];
                if (!slot.Converted || slot.Copy == null)
                {
                    continue;
                }
                options.ProfileSlotName = slot.Original.slotName;
                if (UMASlotRigConverter.TryConvert(slot.Original.meshData, LegacyRig, TargetRig, options, null, out UMAMeshData converted, out string reason))
                {
                    converted.SlotName = slot.Original.slotName;
                    slot.Copy.meshData = converted;
                }
                else
                {
                    slot.Note = "Not converted: " + reason;
                }
            }
            LastConvertMilliseconds = watch.Elapsed.TotalMilliseconds;
            ConvertCount++;
        }

        /// <summary>
        /// Makes the character's recipe hold the preview slots when clothing is shown, and none of them
        /// when it is not. Returns true when the recipe had to change, which needs a full rebuild.
        /// </summary>
        private bool SyncSlotsWithRecipe(UMAData umaData)
        {
            if (ShowClothing)
            {
                return AddSlotsToRecipe(umaData);
            }
            SlotData[] current = umaData.umaRecipe.slotDataList;
            if (current == null)
            {
                return false;
            }
            var kept = new List<SlotData>(current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                if (!IsPreviewSlot(current[i]))
                {
                    kept.Add(current[i]);
                }
            }
            if (kept.Count == current.Length)
            {
                return false;
            }
            umaData.umaRecipe.SetSlots(kept.ToArray());
            return true;
        }

        /// <summary>Makes sure every preview slot is in the character's recipe. Returns true when any had to be added.</summary>
        private bool AddSlotsToRecipe(UMAData umaData)
        {
            bool added = false;
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData slotData = slots[i].SlotData;
                if (slotData == null || Array.IndexOf(umaData.umaRecipe.slotDataList, slotData) >= 0)
                {
                    continue;
                }
                umaData.umaRecipe.SetSlot(umaData.umaRecipe.slotDataList.Length, slotData);
                added = true;
            }
            return added;
        }

        /// <summary>Finds an overlay for a slot added without a recipe: the best name match in the slot's folder or the nearest folder above it that has any.</summary>
        public static OverlayDataAsset FindOverlayFor(SlotDataAsset slot)
        {
            string path = AssetDatabase.GetAssetPath(slot);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string slotName = (slot.slotName ?? slot.name ?? string.Empty).ToLowerInvariant();
            for (int level = 0; level < 4 && !string.IsNullOrEmpty(folder) && folder != "Assets"; level++)
            {
                OverlayDataAsset best = null;
                int bestScore = -1;
                foreach (string guid in AssetDatabase.FindAssets("t:OverlayDataAsset", new[] { folder }))
                {
                    var overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (overlay == null)
                    {
                        continue;
                    }
                    int score = SharedWordLength(slotName, overlay.name.ToLowerInvariant());
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = overlay;
                    }
                }
                if (best != null)
                {
                    return best;
                }
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
            return null;
        }

        /// <summary>Total length of the words of <paramref name="a"/> that also appear in <paramref name="b"/>.</summary>
        private static int SharedWordLength(string a, string b)
        {
            int score = 0;
            foreach (string word in a.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length > 2 && b.Contains(word))
                {
                    score += word.Length;
                }
            }
            return score;
        }
    }
}
