using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;
using Object = UnityEngine.Object;

namespace UMA
{
    // Only generated native objects live here. User scripts, colliders, animators,
    // attachments and mutable character instances are never cloned into the template.
    internal sealed class UMANPCSnapshot : IDisposable
    {
        private GameObject scaffold;
        private Avatar avatar;
        private UMAData.UMARecipe recipe;
        private UMAData.GeneratedMaterials materials;
        private UMARendererAsset[] rendererAssets;
        private Action<DynamicCharacterAvatar> applyInputs;
        private Bounds originalBounds;
        private float height, radius, mass;
        private int lod;
        private UMASkeleton.BoneData[] boneStates;
        private int[] boneIndices;
        private int rootBoneHash;
        internal bool IsAlive => scaffold != null;

        internal static string Validate(UMAData data)
        {
            if (data.HasExternalSkeletonRoot || data.umaRoot == null || data.umaRecipe?.raceData == null ||
                !data.umaRecipe.raceData.useNewDNA || data.skeleton == null || data.skeleton.GetType() != typeof(UMASkeleton))
                return "NPC templates require a generated UMA 3 transform rig.";
            if (data.HasRuntimeDNAProviders || data.TextureOverrides.Count != 0 || data.VertexOverrides.Count != 0 || data.UVOverrides.Count != 0)
                return "Runtime DNA providers or direct source overrides require normal generation.";
            foreach (var component in data.umaRoot.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && !(component is UMAGeneratedBone))
                    return "The generated rig contains " + component.GetType().Name + " on " + component.name + ".";
            var skeletonTransforms = new HashSet<Transform>();
            foreach (var bone in data.skeleton.boneHashData.Values) skeletonTransforms.Add(bone.boneTransform);
            foreach (var transform in data.umaRoot.GetComponentsInChildren<Transform>(true))
                if (transform != data.umaRoot.transform && !skeletonTransforms.Contains(transform))
                    return "The generated rig contains an attachment outside its skeleton.";
            foreach (var slot in data.umaRecipe.slotDataList)
            {
                if (slot?.asset == null) continue;
                if (slot.asset.animatedBones != null && slot.asset.animatedBones.Length != 0)
                    return "Embedded bone physics requires normal generation.";
                if (UMATextureEvent.HasAnyListeners(slot.asset.CharacterBegun) || UMATextureEvent.HasAnyListeners(slot.asset.SlotAtlassed) ||
                    UMATextureEvent.HasAnyListeners(slot.asset.DNAApplied)) return "Slot build callbacks require normal generation.";
            }
            foreach (var renderer in data.GetRenderers())
            {
                if (renderer == null || renderer.transform.parent != data.transform || !UMAResourceLeaseOwner.IsSharedMesh(renderer))
                    return "NPC templates require shared meshes and direct generated renderer children.";
                if (renderer.HasPropertyBlock()) return "Source renderer has per-instance shader overrides.";
                foreach (var component in renderer.GetComponents<Component>())
                    if (!(component is Transform) && !(component is SkinnedMeshRenderer) && !(component is UMAResourceLeaseOwner) &&
                        !(component is UMAGeneratedRenderer)) return "Renderer has a custom component or Cloth.";
            }
            foreach (var gm in data.generatedMaterials.materials)
            {
                if (gm.umaMaterial.materialType != UMAMaterial.MaterialType.UseExistingMaterial && gm.cachedFirstPass == null)
                    return "A generated material is private; normal generation preserves its custom behavior.";
                if (gm.cachedFirstPass == null && gm.material != gm.umaMaterial.material)
                    return "An existing-material override is private.";
                if (gm.secondPassMaterial != null && gm.cachedSecondPass == null) return "Second pass material is private.";
                if (gm.cachedAtlasBindings != null)
                    foreach (var binding in gm.cachedAtlasBindings)
                        if (binding != null && binding.IsPending) return "Atlas conversion is still pending.";
            }
            return null;
        }

        internal UMANPCSnapshot(DynamicCharacterAvatar source)
        {
            try
            {
                applyInputs = source.CaptureNPCInputs();
                recipe = CloneRecipe(source.umaRecipe);
                scaffold = new GameObject("UMA NPC Template") { hideFlags = HideFlags.HideAndDontSave };
                scaffold.SetActive(false);
                Object.DontDestroyOnLoad(scaffold);
                var root = Object.Instantiate(source.umaRoot, scaffold.transform, false); root.name = source.umaRoot.name;
                var sourceBones = source.umaRoot.GetComponentsInChildren<Transform>(true);
                var templateBones = root.GetComponentsInChildren<Transform>(true);
                var bones = new Dictionary<Transform, Transform>(sourceBones.Length);
                for (int i = 0; i < sourceBones.Length; i++) bones.Add(sourceBones[i], templateBones[i]);
                boneStates = new UMASkeleton.BoneData[source.skeleton.boneHashData.Count];
                boneIndices = new int[boneStates.Length];
                rootBoneHash = source.skeleton.rootBoneHash;
                int boneIndex = 0;
                foreach (var pair in source.skeleton.boneHashData)
                {
                    var state = pair.Value;
                    boneIndices[boneIndex] = Array.IndexOf(sourceBones, state.boneTransform);
                    if (boneIndices[boneIndex] < 0) throw new NotSupportedException("Skeleton references a bone outside its generated rig.");
                    boneStates[boneIndex++] = new UMASkeleton.BoneData { umaTransform = state.umaTransform.Duplicate(),
                        boneNameHash = pair.Key, parentBoneNameHash = state.parentBoneNameHash,
                        position = state.position, rotation = state.rotation, scale = state.scale };
                    if (state.boneTransform != null && bones.TryGetValue(state.boneTransform, out var bone))
                    {
                        bone.localPosition = state.position; bone.localRotation = state.rotation; bone.localScale = state.scale;
                    }
                }
                var renderers = new SkinnedMeshRenderer[source.RendererCount];
                for (int i = 0; i < renderers.Length; i++)
                {
                    var original = source.GetRenderer(i);
                    var go = Object.Instantiate(original.gameObject, scaffold.transform, false); go.name = original.name;
                    var renderer = go.GetComponent<SkinnedMeshRenderer>();
                    var remapped = original.bones;
                    for (int b = 0; b < remapped.Length; b++) remapped[b] = remapped[b] == null ? null : bones[remapped[b]];
                    renderer.bones = remapped; renderer.rootBone = bones[original.rootBone];
                    UMAResourceLeaseOwner.Get(renderer).AdoptNPCReferences();
                    renderers[i] = renderer;
                }
                materials = CloneMaterials(source.generatedMaterials, source.umaRecipe, recipe, renderers);
                rendererAssets = (UMARendererAsset[])source.GetRendererAssets().Clone();
                if (source.animator != null && source.animator.avatar != null) avatar = Object.Instantiate(source.animator.avatar);
                originalBounds = source.originalMeshBounds;
                height = source.characterHeight; radius = source.characterRadius; mass = source.characterMass; lod = source.currentLODLevel;
            }
            catch { Dispose(); throw; }
        }

        internal void Apply(DynamicCharacterAvatar target)
        {
            applyInputs(target);
            // Cleanup destroys the prefab's old rig at the END of the frame. If it
            // stays under the Animator, both rigs have identical bone paths and
            // Unity binds to the old one, leaving null handles when it is destroyed.
            // Detach it before attaching/binding the replacement hierarchy.
            if (target.umaRoot != null) target.umaRoot.transform.SetParent(null, false);
            target.HideAndCleanup();
            var container = Object.Instantiate(scaffold, target.transform, false);
            try
            {
                container.hideFlags = HideFlags.None;
                var root = container.transform.Find("Root");
                var renderers = container.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                target.umaRoot = root.gameObject;
                target.umaRecipe = CloneRecipe(recipe);
                if (!target.keepPredefinedDNA) target.predefinedDNA?.Clear();
                target.SetRenderers(renderers); target.SetRendererAssets((UMARendererAsset[])rendererAssets.Clone());
                target.generatedMaterials = CloneMaterials(materials, recipe, target.umaRecipe, renderers);
                // Detach only our generated objects. No gameplay children are moved or destroyed.
                while (container.transform.childCount > 0) container.transform.GetChild(0).SetParent(target.transform, false);
                target.skeleton = UMASkeleton.FromNPCBones(rootBoneHash, boneStates, boneIndices, root.GetComponentsInChildren<Transform>(true));
                foreach (var renderer in renderers) UMAResourceLeaseOwner.Get(renderer).AdoptNPCReferences();
                target.originalMeshBounds = originalBounds; target.characterHeight = height; target.characterRadius = radius; target.characterMass = mass;
                target.currentLODLevel = lod;
                if (avatar != null)
                {
                    target.SetAnimatorController(true);
                    target.animator = target.GetComponent<Animator>();
                    if (target.animator == null) target.animator = target.gameObject.AddComponent<Animator>();
                    target.animator.avatar = Object.Instantiate(avatar); // Private native ownership; no AvatarBuilder call.
                    UMAGeneratorBase.CreatedAvatars.Add(target.animator.avatar.GetUmaObjectId());
                    target.animator.runtimeAnimatorController = target.animationController;
                    target.animator.applyRootMotion = target.applyRootMotion;
                    // The Animator existed before the generated hierarchy was attached.
                    // An assigned controller/Avatar alone does not rebind its humanoid
                    // transform handles to these newly instantiated bones.
                    target.animator.Rebind();
                }
                target.isMeshDirty = target.isShapeDirty = target.isTextureDirty = target.isAtlasDirty = false;
                target.firstBake = false;
                UMAResourceReuse.FinalizeSurfaces(target);
            }
            finally
            {
                // On failure before detachment these never-active owners still need release.
                foreach (var owner in container.GetComponentsInChildren<UMAResourceLeaseOwner>(true)) owner.ReleaseNPCReferences();
                Object.Destroy(container);
            }
        }

        internal static UMAData.UMARecipe CloneRecipe(UMAData.UMARecipe source)
        {
            var copy = new UMAData.UMARecipe { raceData = source.raceData, recipeName = source.recipeName,
                texturePaintStageState = source.texturePaintStageState };
            var colors = new Dictionary<OverlayColorData, OverlayColorData>(NPCReferenceComparer<OverlayColorData>.Instance);
            if (source.sharedColors != null)
            {
                copy.sharedColors = new OverlayColorData[source.sharedColors.Length];
                for (int i = 0; i < source.sharedColors.Length; i++)
                {
                    var original = source.sharedColors[i]; if (original == null) continue;
                    if (!colors.TryGetValue(original, out var color)) colors.Add(original, color = original.Clone());
                    copy.sharedColors[i] = color;
                }
            }
            copy.slotDataList = new SlotData[source.slotDataList.Length];
            var overlayLists = new Dictionary<List<OverlayData>, List<OverlayData>>();
            for (int i = 0; i < copy.slotDataList.Length; i++) copy.slotDataList[i] = source.slotDataList[i]?.CopyForNPC(colors, overlayLists);
            foreach (var pair in overlayLists)
                for (int i = 0; i < pair.Key.Count; i++)
                {
                    if (pair.Key[i]?.mergedFromSlot == null) continue;
                    int sourceSlot = Array.IndexOf(source.slotDataList, pair.Key[i].mergedFromSlot);
                    if (sourceSlot >= 0) pair.Value[i].mergedFromSlot = copy.slotDataList[sourceSlot];
                }
            if (source.dnaInstanceCollection != null) copy.dnaInstanceCollection = source.dnaInstanceCollection.Clone();
            foreach (var pair in source.MeshHideDictionary) copy.MeshHideDictionary.Add(pair.Key, new List<MeshHideAsset>(pair.Value));
            foreach (var pair in source.BlendshapeSlots) copy.BlendshapeSlots.Add(pair.Key, new List<UMAMeshData>(pair.Value));
            return copy;
        }

        private static UMAData.GeneratedMaterials CloneMaterials(UMAData.GeneratedMaterials source, UMAData.UMARecipe sourceRecipe,
            UMAData.UMARecipe targetRecipe, SkinnedMeshRenderer[] renderers)
        {
            var copy = new UMAData.GeneratedMaterials { rendererAssets = new List<UMARendererAsset>(source.rendererAssets) };
            try
            {
                foreach (var gm in source.materials)
                {
                    int rendererIndex = Array.IndexOf(sourceRecipe.slotDataList, gm.materialFragments.Count > 0 ? gm.materialFragments[0].slotData : null);
                    rendererIndex = rendererIndex < 0 ? 0 : sourceRecipe.slotDataList[rendererIndex].skinnedMeshRenderer;
                    var result = new UMAData.GeneratedMaterial { umaMaterial = gm.umaMaterial, material = gm.material,
                        secondPassMaterial = gm.secondPassMaterial, rendererAsset = gm.rendererAsset,
                        skinnedMeshRenderer = renderers[rendererIndex], materialIndex = gm.materialIndex,
                        cropResolution = gm.cropResolution, resolutionScale = gm.resolutionScale,
                        textureNameList = gm.textureNameList == null ? null : (string[])gm.textureNameList.Clone(),
                        resultingAtlasList = gm.resultingAtlasList == null ? null : (Texture[])gm.resultingAtlasList.Clone() };
                    copy.materials.Add(result);
                    result.cachedFirstPass = gm.cachedFirstPass?.Retain(); result.cachedSecondPass = gm.cachedSecondPass?.Retain();
                    if (gm.cachedAtlasBindings != null)
                    {
                        result.cachedAtlasBindings = new UMAAtlasBinding[gm.cachedAtlasBindings.Length];
                        for (int c = 0; c < result.cachedAtlasBindings.Length; c++)
                        {
                            var binding = gm.cachedAtlasBindings[c];
                            if (binding != null) result.cachedAtlasBindings[c] = new UMAAtlasBinding(binding.RetainAtlas(), result, c, binding.Property);
                        }
                    }
                    foreach (var fragment in gm.materialFragments)
                    {
                        int index = Array.IndexOf(sourceRecipe.slotDataList, fragment.slotData);
                        if (index < 0) throw new NotSupportedException("Generated fragment is not part of the prepared recipe.");
                        var slot = targetRecipe.slotDataList[index];
                        result.materialFragments.Add(CloneFragment(fragment, slot));
                    }
                }
                var fragments = new Dictionary<UMAData.MaterialFragment, UMAData.MaterialFragment>();
                for (int m = 0; m < source.materials.Count; m++)
                    for (int f = 0; f < source.materials[m].materialFragments.Count; f++)
                        fragments[source.materials[m].materialFragments[f]] = copy.materials[m].materialFragments[f];
                foreach (var pair in fragments)
                    if (pair.Key.rectFragment != null) pair.Value.rectFragment = fragments[pair.Key.rectFragment];
                return copy;
            }
            catch { foreach (var gm in copy.materials) UMAResourceReuse.ReleaseSurfaceReferences(gm); throw; }
        }

        private static UMAData.textureData CloneTextureData(UMAData.textureData value) => value == null ? null :
            new UMAData.textureData { textureList = value.textureList == null ? null : (Texture[])value.textureList.Clone(),
                alphaTexture = value.alphaTexture, overlayType = value.overlayType };
        private static Color[][] CloneChannels(Color[][] source)
        {
            if (source == null) return null;
            var copy = new Color[source.Length][];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i] == null ? null : (Color[])source[i].Clone();
            return copy;
        }
        private static UMAData.MaterialFragment CloneFragment(UMAData.MaterialFragment source, SlotData slot)
        {
            var copy = new UMAData.MaterialFragment { slotData = slot, overlayList = slot.GetOverlayList(),
                umaMaterial = source.umaMaterial, atlasRegion = source.atlasRegion, sourceUVRect = source.sourceUVRect,
                baseVertexInMesh = source.baseVertexInMesh, size = source.size, baseColor = source.baseColor,
                isRectShared = source.isRectShared, isNoTextures = source.isNoTextures,
                baseOverlay = CloneTextureData(source.baseOverlay), channelMask = CloneChannels(source.channelMask),
                channelAdditiveMask = CloneChannels(source.channelAdditiveMask),
                rects = source.rects == null ? null : (Rect[])source.rects.Clone(),
                overlayColors = source.overlayColors == null ? null : (Color32[])source.overlayColors.Clone() };
            if (source.overlayData != null)
            {
                copy.overlayData = new OverlayData[source.overlayData.Length];
                for (int i = 0; i < copy.overlayData.Length; i++)
                {
                    int index = source.overlayList.IndexOf(source.overlayData[i]);
                    if (index >= 0) copy.overlayData[i] = copy.overlayList[index];
                }
            }
            if (source.AdditionalOverlays != null)
            {
                copy.AdditionalOverlays = new UMAData.textureData[source.AdditionalOverlays.Length];
                for (int i = 0; i < copy.AdditionalOverlays.Length; i++) copy.AdditionalOverlays[i] = CloneTextureData(source.AdditionalOverlays[i]);
            }
            foreach (var entry in source.overrides) copy.overrides.Add(entry == null ? null : new Dictionary<int, Texture>(entry));
            return copy;
        }

        public void Dispose()
        {
            if (materials != null) foreach (var gm in materials.materials) UMAResourceReuse.ReleaseSurfaceReferences(gm);
            materials = null;
            if (scaffold != null)
            {
                foreach (var owner in scaffold.GetComponentsInChildren<UMAResourceLeaseOwner>(true)) owner.ReleaseNPCReferences();
                Object.Destroy(scaffold);
            }
            if (avatar != null) Object.Destroy(avatar);
            scaffold = null; avatar = null; recipe = null; applyInputs = null;
        }
    }
    internal sealed class NPCReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly NPCReferenceComparer<T> Instance = new NPCReferenceComparer<T>();
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
