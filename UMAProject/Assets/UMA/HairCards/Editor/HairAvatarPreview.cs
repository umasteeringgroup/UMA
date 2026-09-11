using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

[assembly: InternalsVisibleTo("UMA.HairCards.Editor.Tests")]

namespace UMA.HairCards.Editor
{
    internal static class HairBoneFocus
    {
        internal static Vector3? Capture(DynamicCharacterAvatar avatar, HumanBodyBones bone)
        {
            if (avatar == null || (bone != HumanBodyBones.Head && bone != HumanBodyBones.Neck)) return null;
            Animator animator = avatar.umaData != null ? avatar.umaData.animator : null;
            if (animator == null) animator = avatar.GetComponentInChildren<Animator>(true);
            Transform target = animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman
                ? animator.GetBoneTransform(bone) : null;
            string boneName = bone.ToString();
            if (target == null && avatar.umaData?.skeleton != null)
                target = avatar.umaData.skeleton.GetBoneTransform(boneName);
            if (target == null)
            {
                // Generic UMA rigs may not have a humanoid Avatar. Match exact names only;
                // accessories such as Head_End and NeckTwist must never become the pivot.
                foreach (Transform candidate in avatar.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(candidate.name, boneName, StringComparison.OrdinalIgnoreCase))
                    { target = candidate; break; }
            }
            if (target == null) return null;
            // The stage displays an avatar-local, static baked pose. Snapshot now, not on click.
            Vector3 position = avatar.transform.InverseTransformPoint(target.position);
            return float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z)
                ? position : (Vector3?)null;
        }
    }

    internal static class HairPaintedAreaBounds
    {
        // Capture with the baked authoring surface so later animation in the source scene cannot
        // move the focus away from the static pose displayed in the grooming stage.
        internal static Vector3?[] CaptureBones(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return null;
            Transform[] bones = renderer.bones;
            if (bones.Length == 0) return null;
            Vector3?[] positions = new Vector3?[bones.Length];
            Matrix4x4 worldToSource = renderer.transform.worldToLocalMatrix;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) positions[i] = worldToSource.MultiplyPoint3x4(bones[i].position);
            return positions;
        }

        internal static bool TryCalculate(Mesh source, float[] growth, HairAuthoringPose pose,
            IReadOnlyList<Vector3?> bonePositions, Matrix4x4 sourceToStage, out Bounds bounds, out int boneCount)
        {
            bounds = default;
            boneCount = 0;
            if (source == null || !source.isReadable || growth == null || growth.Length != source.vertexCount)
                return false;
            Vector3[] vertices = pose == null ? source.vertices : null;
            // Mesh-owned, read-only views (Allocator.None): do not dispose or retain them.
            var counts = source.GetBonesPerVertex();
            var weights = source.GetAllBoneWeights();
            bool hasWeights = counts.Length == source.vertexCount;
            HashSet<int> affectedBones = new HashSet<int>();
            bool found = false;
            int offset = 0;
            for (int vertex = 0; vertex < source.vertexCount; vertex++)
            {
                int count = hasWeights ? counts[vertex] : 0;
                float amount = growth[vertex];
                if (amount > 0f && !float.IsInfinity(amount))
                {
                    Include(sourceToStage.MultiplyPoint3x4(pose != null ? pose.PosedVertex(vertex) : vertices[vertex]),
                        ref bounds, ref found);
                    for (int i = 0; i < count && offset + i < weights.Length; i++)
                    {
                        BoneWeight1 weight = weights[offset + i];
                        if (weight.weight > 0f && !float.IsInfinity(weight.weight) && weight.boneIndex >= 0)
                            affectedBones.Add(weight.boneIndex);
                    }
                }
                offset += count;
            }
            if (!found) return false;
            // A standalone groom may retain skin weights/bind poses without a live avatar.
            Matrix4x4[] bindPoses = bonePositions == null ? source.bindposes : null;
            foreach (int bone in affectedBones)
            {
                Vector3? position = bonePositions != null
                    ? bone < bonePositions.Count ? bonePositions[bone] : null
                    : bone < bindPoses.Length ? bindPoses[bone].inverse.MultiplyPoint3x4(Vector3.zero) : (Vector3?)null;
                if (position.HasValue && Include(sourceToStage.MultiplyPoint3x4(position.Value), ref bounds, ref found))
                    boneCount++;
            }
            // The stage displays the avatar in character-local coordinates (its root is at the
            // stage origin). Center on that vertical axis, NOT the source renderer's origin or
            // the painted patch's midpoint. Recompute extents before padding to retain every
            // painted point/bone and its counterpart across the axis, including one-sided paint.
            Vector3 min = bounds.min, max = bounds.max;
            bounds = new Bounds(new Vector3(0f, bounds.center.y, 0f), new Vector3(
                2f * Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)), bounds.size.y,
                2f * Mathf.Max(Mathf.Abs(min.z), Mathf.Abs(max.z))));
            // Padding plus a scale-relative minimum prevents zero-size/single-vertex focus jumps.
            float scale = Mathf.Max(sourceToStage.MultiplyVector(Vector3.right).magnitude,
                sourceToStage.MultiplyVector(Vector3.up).magnitude, sourceToStage.MultiplyVector(Vector3.forward).magnitude);
            float minimum = Mathf.Max(0.001f, source.bounds.size.magnitude * scale * 0.02f);
            Vector3 size = bounds.size * 1.15f;
            bounds.size = new Vector3(Mathf.Max(size.x, minimum), Mathf.Max(size.y, minimum), Mathf.Max(size.z, minimum));
            return true;
        }

        private static bool Include(Vector3 point, ref Bounds bounds, ref bool found)
        {
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z)) return false;
            if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
            else bounds.Encapsulate(point);
            return true;
        }
    }

    internal enum HairVisibilityState
    {
        Hidden,
        Mixed,
        Visible
    }

    internal enum HairVisibilityGroupKind
    {
        Recipe,
        Udim,
        Slot
    }

    internal sealed class HairAvatarVisibilityGroup
    {
        internal readonly string Id;
        internal readonly string DisplayName;
        internal readonly HairVisibilityGroupKind Kind;
        internal readonly List<string> SlotNames;

        internal HairAvatarVisibilityGroup(string id, string displayName, HairVisibilityGroupKind kind,
            IEnumerable<string> slotNames)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            SlotNames = new List<string>();
            if (slotNames == null) return;
            foreach (string slotName in slotNames)
            {
                if (!string.IsNullOrEmpty(slotName) && !SlotNames.Contains(slotName)) SlotNames.Add(slotName);
            }
            SlotNames.Sort(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal sealed class HairAvatarVisibilityCatalog
    {
        private readonly List<HairAvatarVisibilityGroup> recipeGroups = new List<HairAvatarVisibilityGroup>();
        private readonly List<HairAvatarVisibilityGroup> udimGroups = new List<HairAvatarVisibilityGroup>();
        private readonly List<HairAvatarVisibilityGroup> slotGroups = new List<HairAvatarVisibilityGroup>();
        private readonly HashSet<string> slotNames = new HashSet<string>(StringComparer.Ordinal);

        internal IReadOnlyList<HairAvatarVisibilityGroup> RecipeGroups => recipeGroups;
        internal IReadOnlyList<HairAvatarVisibilityGroup> UdimGroups => udimGroups;
        internal IReadOnlyList<HairAvatarVisibilityGroup> SlotGroups => slotGroups;
        internal IReadOnlyCollection<string> SlotNames => slotNames;

        internal static HairAvatarVisibilityCatalog Build(DynamicCharacterAvatar avatar,
            IReadOnlyDictionary<string, SlotData> renderedSlots)
        {
            HairAvatarVisibilityCatalog catalog = new HairAvatarVisibilityCatalog();
            if (renderedSlots == null || renderedSlots.Count == 0) return catalog;

            List<SlotData> slots = new List<SlotData>();
            foreach (KeyValuePair<string, SlotData> pair in renderedSlots)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                catalog.slotNames.Add(pair.Key);
                if (pair.Value != null) slots.Add(pair.Value);

                SlotDataAsset asset = pair.Value?.asset;
                string label = pair.Key;
                if (asset != null && asset.IsUdimMember)
                    label += $"  (UDIM {asset.udimTileNumber})";
                catalog.slotGroups.Add(new HairAvatarVisibilityGroup("slot:" + pair.Key, label,
                    HairVisibilityGroupKind.Slot, new[] { pair.Key }));
            }
            catalog.slotGroups.Sort(CompareGroups);

            Dictionary<string, List<string>> udimMembers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, string> udimLabels = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData slot = slots[i];
                SlotDataAsset asset = slot?.asset;
                if (asset == null || !asset.IsUdimMember || string.IsNullOrEmpty(slot.slotName)) continue;
                if (!udimMembers.TryGetValue(asset.udimGroupId, out List<string> members))
                {
                    members = new List<string>();
                    udimMembers.Add(asset.udimGroupId, members);
                    udimLabels.Add(asset.udimGroupId, string.IsNullOrWhiteSpace(asset.udimGroupName)
                        ? asset.udimGroupId : asset.udimGroupName);
                }
                if (!members.Contains(slot.slotName)) members.Add(slot.slotName);
            }
            foreach (KeyValuePair<string, List<string>> pair in udimMembers)
            {
                string display = udimLabels[pair.Key] + $"  ({pair.Value.Count} tiles)";
                catalog.udimGroups.Add(new HairAvatarVisibilityGroup("udim:" + pair.Key, display,
                    HairVisibilityGroupKind.Udim, pair.Value));
            }
            catalog.udimGroups.Sort(CompareGroups);

            if (avatar != null)
            {
                HashSet<UMARecipeBase> addedRecipes = new HashSet<UMARecipeBase>();
                RaceData race = avatar.activeRace?.data;
                AddRecipeGroup(catalog, race?.baseRaceRecipe,
                    race != null ? "Base Race: " + race.raceName : "Base Race", "base", renderedSlots,
                    addedRecipes, race);

                UMATextRecipe[] wearables = avatar.GetVisibleWearables();
                if (wearables != null)
                {
                    Array.Sort(wearables, (left, right) => string.Compare(left?.name, right?.name,
                        StringComparison.OrdinalIgnoreCase));
                    for (int i = 0; i < wearables.Length; i++)
                    {
                        UMATextRecipe recipe = wearables[i];
                        string wardrobeSlot = recipe != null && !string.IsNullOrWhiteSpace(recipe.wardrobeSlot)
                            ? recipe.wardrobeSlot + ": " : string.Empty;
                        AddRecipeGroup(catalog, recipe, "Wardrobe / " + wardrobeSlot + recipe?.name,
                            "wearable", renderedSlots, addedRecipes, race);
                    }
                }

                if (avatar.AdditiveRecipes != null)
                {
                    List<string> keys = new List<string>(avatar.AdditiveRecipes.Keys);
                    keys.Sort(StringComparer.OrdinalIgnoreCase);
                    for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                    {
                        string key = keys[keyIndex];
                        if (!avatar.AdditiveRecipes.TryGetValue(key, out List<UMATextRecipe> recipes) || recipes == null)
                            continue;
                        for (int recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
                        {
                            UMATextRecipe recipe = recipes[recipeIndex];
                            AddRecipeGroup(catalog, recipe, "Additive / " + key + ": " + recipe?.name,
                                "additive", renderedSlots, addedRecipes, race);
                        }
                    }
                }

                if (avatar.umaAdditionalRecipes != null)
                {
                    for (int i = 0; i < avatar.umaAdditionalRecipes.Length; i++)
                    {
                        UMARecipeBase recipe = avatar.umaAdditionalRecipes[i];
                        AddRecipeGroup(catalog, recipe, "Additional: " + recipe?.name,
                            "additional", renderedSlots, addedRecipes, race);
                    }
                }
            }

            HashSet<string> assigned = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < catalog.recipeGroups.Count; i++)
                assigned.UnionWith(catalog.recipeGroups[i].SlotNames);
            List<string> unassigned = new List<string>();
            foreach (string slotName in catalog.slotNames)
                if (!assigned.Contains(slotName)) unassigned.Add(slotName);
            if (unassigned.Count > 0)
                catalog.recipeGroups.Add(new HairAvatarVisibilityGroup("recipe:generated-other",
                    "Generated / Other", HairVisibilityGroupKind.Recipe, unassigned));
            catalog.recipeGroups.Sort(CompareGroups);
            return catalog;
        }

        private static void AddRecipeGroup(HairAvatarVisibilityCatalog catalog, UMARecipeBase recipe,
            string displayName, string category, IReadOnlyDictionary<string, SlotData> renderedSlots,
            HashSet<UMARecipeBase> addedRecipes, RaceData activeRace)
        {
            if (recipe == null || !addedRecipes.Add(recipe)) return;
            UMAData.UMARecipe cached;
            try
            {
                cached = recipe.GetCachedRecipe();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UMA Hair Cards] Could not inspect recipe '{recipe.name}' for preview visibility: " +
                                 exception.Message);
                return;
            }
            if (cached?.slotDataList == null) return;

            List<string> members = new List<string>();
            for (int i = 0; i < cached.slotDataList.Length; i++)
            {
                SlotData recipeSlot = cached.slotDataList[i];
                if (recipeSlot == null) continue;
                if (!string.IsNullOrEmpty(recipeSlot.slotName) && renderedSlots.ContainsKey(recipeSlot.slotName))
                {
                    if (!members.Contains(recipeSlot.slotName)) members.Add(recipeSlot.slotName);
                    continue;
                }
                if (recipeSlot.asset == null) continue;
                foreach (KeyValuePair<string, SlotData> rendered in renderedSlots)
                {
                    if (rendered.Value?.asset == recipeSlot.asset && !members.Contains(rendered.Key))
                        members.Add(rendered.Key);
                }
                if (recipe is not UMATextRecipe textRecipe || activeRace == null ||
                    textRecipe.compatibleRaces == null) continue;
                string equivalent = activeRace.FindEquivalentSlot(textRecipe.compatibleRaces,
                    recipeSlot.slotName, false);
                if (!string.IsNullOrEmpty(equivalent) && renderedSlots.ContainsKey(equivalent) &&
                    !members.Contains(equivalent)) members.Add(equivalent);
            }
            if (members.Count == 0) return;
            catalog.recipeGroups.Add(new HairAvatarVisibilityGroup(
                "recipe:" + category + ":" + StableAssetId(recipe),
                string.IsNullOrWhiteSpace(displayName) ? recipe.name : displayName,
                HairVisibilityGroupKind.Recipe, members));
        }

        private static string StableAssetId(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path))
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) return guid;
            }
            return asset != null ? asset.GetType().FullName + ":" + asset.name : "none";
        }

        private static int CompareGroups(HairAvatarVisibilityGroup left, HairAvatarVisibilityGroup right)
        {
            return string.Compare(left?.DisplayName, right?.DisplayName, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class HairAvatarGeometryUtility
    {
        internal static void FindGeneratedMaterial(UMAData data, SkinnedMeshRenderer renderer,
            Material material, int materialIndex, out UMAData.GeneratedMaterial generated, out bool isSecondPass)
        {
            generated = null;
            isSecondPass = false;
            List<UMAData.GeneratedMaterial> candidates = data?.generatedMaterials?.materials;
            if (candidates == null) return;
            for (int i = 0; i < candidates.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = candidates[i];
                if (candidate == null || candidate.material != material ||
                    (candidate.skinnedMeshRenderer != null && candidate.skinnedMeshRenderer != renderer)) continue;
                generated = candidate;
                return;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = candidates[i];
                if (candidate == null ||
                    (candidate.skinnedMeshRenderer != null && candidate.skinnedMeshRenderer != renderer)) continue;
                Material generatedSecondPass = candidate.secondPassMaterial;
                Material declaredSecondPass = candidate.umaMaterial?.secondPass;
                if (generatedSecondPass != material &&
                    (generatedSecondPass != null || declaredSecondPass != material)) continue;
                generated = candidate;
                isSecondPass = true;
                return;
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                UMAData.GeneratedMaterial candidate = candidates[i];
                if (candidate == null || candidate.skinnedMeshRenderer != renderer ||
                    candidate.materialIndex != materialIndex) continue;
                generated = candidate;
                return;
            }
        }

        internal static List<SlotData> FindSlots(UMAData.GeneratedMaterial generated)
        {
            List<SlotData> result = new List<SlotData>();
            if (generated?.materialFragments == null) return result;
            for (int i = 0; i < generated.materialFragments.Count; i++)
            {
                SlotData slot = generated.materialFragments[i]?.slotData;
                if (slot != null && !result.Contains(slot)) result.Add(slot);
            }
            return result;
        }

        internal static SlotData FindTriangleOwner(int a, int b, int c, IReadOnlyList<SlotData> candidates)
        {
            if (candidates == null) return null;
            for (int i = 0; i < candidates.Count; i++)
            {
                SlotData slot = candidates[i];
                if (slot?.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData)) continue;
                if (slot.OwnsVertex(a) && slot.OwnsVertex(b) && slot.OwnsVertex(c)) return slot;
            }
            return candidates.Count == 1 ? candidates[0] : null;
        }
    }

    /// <summary>
    /// Populate the active handle target's depth without touching its color. Some Scene view
    /// rendering paths do not retain scene depth for duringSceneGui; a ZTest alone is insufficient.
    /// Visible opaque/alpha-tested surfaces participate; transparent cards keep their holes.
    /// </summary>
    internal sealed class HairGuideDepthRenderer : IDisposable
    {
        private Material material;
        private readonly List<Material> surfaceMaterials = new List<Material>();

        internal Material DepthMaterial
        {
            get
            {
                if (material == null)
                {
                    Shader shader = Shader.Find("Hidden/UMA/HairCards/GuideOccluderDepth");
                    if (shader != null) material = new Material(shader)
                    {
                        name = "Hair Guide Occluder Depth", hideFlags = HideFlags.HideAndDontSave
                    };
                }
                return material;
            }
        }

        internal static bool CanOccludeGuides(Material surface)
        {
            if (surface == null || surface.renderQueue > (int)RenderQueue.GeometryLast) return false;
            string renderType = surface.GetTag("RenderType", false, "Opaque");
            return (renderType == "Opaque" || renderType == "TransparentCutout") &&
                   !surface.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
        }

        private static void ConfigureCutout(Material depth, Material surface)
        {
            bool clipped = surface.renderQueue >= (int)RenderQueue.AlphaTest ||
                surface.GetTag("RenderType", false) == "TransparentCutout" || surface.IsKeywordEnabled("_ALPHATEST_ON") ||
                (surface.HasProperty("_AlphaClip") && surface.GetFloat("_AlphaClip") > 0f) ||
                (surface.HasProperty("_AlphaCutoffEnable") && surface.GetFloat("_AlphaCutoffEnable") > 0f);
            depth.SetFloat("_AlphaClip", clipped ? 1f : 0f);
            if (!clipped) return;
            // Standard, URP Lit and HDRP Lit texture conventions. Honor alpha holes rather than
            // replacing an alpha-tested skin/hair surface with an opaque silhouette.
            string textureProperty = surface.HasProperty("_BaseMap") ? "_BaseMap" :
                surface.HasProperty("_BaseColorMap") ? "_BaseColorMap" : "_MainTex";
            bool hasTexture = surface.HasProperty(textureProperty);
            depth.SetTexture("_MainTex", hasTexture ? surface.GetTexture(textureProperty) : Texture2D.whiteTexture);
            depth.SetTextureScale("_MainTex", hasTexture ? surface.GetTextureScale(textureProperty) : Vector2.one);
            depth.SetTextureOffset("_MainTex", hasTexture ? surface.GetTextureOffset(textureProperty) : Vector2.zero);
            depth.SetFloat("_Cutoff", surface.HasProperty("_Cutoff") ? surface.GetFloat("_Cutoff") :
                surface.HasProperty("_AlphaCutoff") ? surface.GetFloat("_AlphaCutoff") : 0.5f);
            depth.SetFloat("_BaseAlpha", surface.HasProperty("_BaseColor") ? surface.GetColor("_BaseColor").a :
                surface.HasProperty("_Color") ? surface.GetColor("_Color").a : 1f);
        }

        internal void Draw(Renderer renderer, Mesh mesh)
        {
            if (renderer == null || mesh == null || !renderer.enabled || renderer.forceRenderingOff ||
                !renderer.gameObject.activeInHierarchy) return;
            Camera camera = Camera.current;
            if (camera != null && (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) return;
            renderer.GetSharedMaterials(surfaceMaterials);
            try
            {
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    Material surface = surfaceMaterials.Count > 0
                        ? surfaceMaterials[Mathf.Min(submesh, surfaceMaterials.Count - 1)] : null;
                    if (!CanOccludeGuides(surface)) continue;
                    Material depth = DepthMaterial;
                    if (depth == null) continue;
                    ConfigureCutout(depth, surface);
                    if (depth.SetPass(0))
                        Graphics.DrawMeshNow(mesh, renderer.localToWorldMatrix, submesh);
                }
            }
            finally
            {
                surfaceMaterials.Clear();
                if (material != null) material.SetTexture("_MainTex", null);
            }
        }

        public void Dispose()
        {
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            material = null;
            surfaceMaterials.Clear();
        }
    }

    internal sealed class HairAvatarPreview : IDisposable
    {
        private sealed class Surface
        {
            internal GameObject gameObject;
            internal MeshRenderer renderer;
            internal readonly List<string> slotNames = new List<string>();
        }

        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private readonly Dictionary<string, SlotData> renderedSlots =
            new Dictionary<string, SlotData>(StringComparer.Ordinal);

        internal GameObject Root { get; private set; }
        internal IReadOnlyDictionary<string, SlotData> RenderedSlots => renderedSlots;

        internal static HairAvatarPreview Build(DynamicCharacterAvatar avatar)
        {
            if (avatar?.umaData == null) return null;
            SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0) return null;

            HairAvatarPreview preview = new HairAvatarPreview
            {
                Root = new GameObject(avatar.name + " Hair Card Avatar Preview")
            };
            preview.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            preview.Root.transform.localScale = Vector3.one;

            try
            {
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer sourceRenderer = renderers[rendererIndex];
                    if (sourceRenderer == null || sourceRenderer.sharedMesh == null) continue;
                    Mesh baked = new Mesh
                    {
                        name = sourceRenderer.name + " Hair Card Preview Bake",
                        indexFormat = sourceRenderer.sharedMesh.indexFormat,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    try
                    {
                        sourceRenderer.BakeMesh(baked);
                        Material[] sourceMaterials = sourceRenderer.sharedMaterials;
                        int submeshCount = Mathf.Min(baked.subMeshCount, sourceMaterials.Length);
                        Matrix4x4 toAvatar = avatar.transform.worldToLocalMatrix *
                                             sourceRenderer.transform.localToWorldMatrix;
                        for (int submesh = 0; submesh < submeshCount; submesh++)
                            preview.AddSubmesh(avatar.umaData, sourceRenderer, baked, toAvatar,
                                sourceMaterials[submesh], rendererIndex, submesh);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                }
                return preview;
            }
            catch
            {
                preview.Dispose();
                throw;
            }
        }

        internal void ApplyVisibility(bool showAvatar, ISet<string> hiddenSlots)
        {
            if (Root != null) Root.SetActive(showAvatar);
            if (!showAvatar) return;
            for (int i = 0; i < surfaces.Count; i++)
            {
                Surface surface = surfaces[i];
                bool visible = surface.slotNames.Count == 0;
                for (int slotIndex = 0; !visible && slotIndex < surface.slotNames.Count; slotIndex++)
                    visible = hiddenSlots == null || !hiddenSlots.Contains(surface.slotNames[slotIndex]);
                if (surface.gameObject != null) surface.gameObject.SetActive(visible);
            }
        }

        internal bool TryGetVisibleBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (Root == null || !Root.activeSelf) return false;
            for (int i = 0; i < surfaces.Count; i++)
            {
                Renderer renderer = surfaces[i].gameObject != null
                    ? surfaces[i].gameObject.GetComponent<Renderer>() : null;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        internal void DrawGuideOccluderDepth(HairGuideDepthRenderer depth)
        {
            if (Root == null || !Root.activeInHierarchy) return;
            for (int i = 0; i < surfaces.Count; i++) depth.Draw(surfaces[i].renderer, meshes[i]);
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            Root = null;
            for (int i = 0; i < meshes.Count; i++)
                if (meshes[i] != null) UnityEngine.Object.DestroyImmediate(meshes[i]);
            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null) UnityEngine.Object.DestroyImmediate(materials[i]);
            meshes.Clear();
            materials.Clear();
            surfaces.Clear();
            renderedSlots.Clear();
        }

        private void AddSubmesh(UMAData data, SkinnedMeshRenderer sourceRenderer, Mesh baked,
            Matrix4x4 toAvatar, Material sourceMaterial, int rendererIndex, int submesh)
        {
            HairAvatarGeometryUtility.FindGeneratedMaterial(data, sourceRenderer, sourceMaterial, submesh,
                out UMAData.GeneratedMaterial generated, out bool secondPass);
            if (secondPass) return;
            List<SlotData> slots = HairAvatarGeometryUtility.FindSlots(generated);
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData slot = slots[i];
                if (slot != null && !string.IsNullOrEmpty(slot.slotName)) renderedSlots[slot.slotName] = slot;
            }

            int[] sourceTriangles = baked.GetTriangles(submesh, true);
            Dictionary<string, List<int>> trianglesByOwner = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            List<int> unresolved = new List<int>();
            for (int index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                SlotData owner = HairAvatarGeometryUtility.FindTriangleOwner(sourceTriangles[index],
                    sourceTriangles[index + 1], sourceTriangles[index + 2], slots);
                List<int> target = unresolved;
                if (owner != null && !string.IsNullOrEmpty(owner.slotName))
                {
                    if (!trianglesByOwner.TryGetValue(owner.slotName, out target))
                    {
                        target = new List<int>();
                        trianglesByOwner.Add(owner.slotName, target);
                    }
                }
                target.Add(sourceTriangles[index]);
                target.Add(sourceTriangles[index + 1]);
                target.Add(sourceTriangles[index + 2]);
            }

            int sliceIndex = 0;
            foreach (KeyValuePair<string, List<int>> pair in trianglesByOwner)
            {
                AddSurface(baked, pair.Value, toAvatar, sourceMaterial,
                    $"{rendererIndex:D2}_{submesh:D2}_{pair.Key}", new[] { pair.Key });
                sliceIndex++;
            }
            if (unresolved.Count > 0 || sliceIndex == 0)
            {
                List<string> memberNames = new List<string>();
                for (int i = 0; i < slots.Count; i++)
                    if (slots[i] != null && !string.IsNullOrEmpty(slots[i].slotName)) memberNames.Add(slots[i].slotName);
                AddSurface(baked, unresolved.Count > 0 ? unresolved : new List<int>(sourceTriangles), toAvatar,
                    sourceMaterial, $"{rendererIndex:D2}_{submesh:D2}_Unresolved", memberNames);
            }
        }

        private void AddSurface(Mesh source, List<int> triangles, Matrix4x4 transform, Material sourceMaterial,
            string objectName, IEnumerable<string> slotNames)
        {
            if (triangles == null || triangles.Count == 0) return;
            Mesh mesh = ExtractTriangles(source, triangles, transform, objectName);
            meshes.Add(mesh);
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(Root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            if (sourceMaterial != null)
            {
                Material previewMaterial = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + " (Hair Card Preview)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                materials.Add(previewMaterial);
                renderer.sharedMaterial = previewMaterial;
            }
            Surface surface = new Surface { gameObject = child, renderer = renderer };
            if (slotNames != null)
                foreach (string slotName in slotNames)
                    if (!string.IsNullOrEmpty(slotName) && !surface.slotNames.Contains(slotName))
                        surface.slotNames.Add(slotName);
            surfaces.Add(surface);
        }

        private static Mesh ExtractTriangles(Mesh source, List<int> sourceTriangles, Matrix4x4 transform,
            string meshName)
        {
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUv = source.uv;
            Color32[] sourceColors = source.colors32;
            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv = new List<Vector2>();
            List<Color32> colors = new List<Color32>();
            int[] triangles = new int[sourceTriangles.Count];
            Matrix4x4 normalMatrix = transform.inverse.transpose;
            for (int i = 0; i < sourceTriangles.Count; i++)
            {
                int sourceIndex = sourceTriangles[i];
                if (!remap.TryGetValue(sourceIndex, out int destination))
                {
                    destination = vertices.Count;
                    remap.Add(sourceIndex, destination);
                    vertices.Add(transform.MultiplyPoint3x4(sourceVertices[sourceIndex]));
                    if (sourceNormals.Length == sourceVertices.Length)
                        normals.Add(normalMatrix.MultiplyVector(sourceNormals[sourceIndex]).normalized);
                    if (sourceTangents.Length == sourceVertices.Length)
                    {
                        Vector4 sourceTangent = sourceTangents[sourceIndex];
                        Vector3 tangent = transform.MultiplyVector(new Vector3(sourceTangent.x,
                            sourceTangent.y, sourceTangent.z)).normalized;
                        tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, sourceTangent.w));
                    }
                    if (sourceUv.Length == sourceVertices.Length) uv.Add(sourceUv[sourceIndex]);
                    if (sourceColors.Length == sourceVertices.Length) colors.Add(sourceColors[sourceIndex]);
                }
                triangles[i] = destination;
            }
            Mesh mesh = new Mesh
            {
                name = source.name + " " + meshName,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) mesh.SetNormals(normals);
            if (tangents.Count == vertices.Count) mesh.SetTangents(tangents);
            if (uv.Count == vertices.Count) mesh.SetUVs(0, uv);
            if (colors.Count == vertices.Count) mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);
            if (normals.Count != vertices.Count) mesh.RecalculateNormals();
            if (tangents.Count != vertices.Count && uv.Count == vertices.Count) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Maps persistent groom coordinates from the source mesh onto the posed mesh baked for the
    /// current avatar preview. A triangle-local frame keeps every guide rooted to the same posed
    /// surface used by painting without writing transient pose coordinates into the groom asset.
    /// </summary>
    internal sealed class HairAuthoringPose
    {
        private readonly Mesh source;
        private readonly Mesh posed;
        private readonly Vector3[] sourceVertices;
        private readonly Vector3[] posedVertices;
        private readonly Vector3[] posedNormals;
        private readonly int[][] sourceTriangles;
        private readonly int[] flattenedTriangles;

        internal bool IsActive => source != null && posed != null && !ReferenceEquals(source, posed) &&
                                  sourceVertices.Length == posedVertices.Length;

        internal HairAuthoringPose(Mesh sourceMesh, Mesh posedMesh)
        {
            source = sourceMesh;
            posed = posedMesh;
            sourceVertices = source != null ? source.vertices : Array.Empty<Vector3>();
            posedVertices = posed != null ? posed.vertices : Array.Empty<Vector3>();
            posedNormals = posed != null ? posed.normals : Array.Empty<Vector3>();
            int submeshCount = source != null ? source.subMeshCount : 0;
            sourceTriangles = new int[submeshCount][];
            List<int> flattened = new List<int>();
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                sourceTriangles[submesh] = source.GetTriangles(submesh, true);
                flattened.AddRange(sourceTriangles[submesh]);
            }
            flattenedTriangles = flattened.ToArray();
        }

        private static readonly ProfilerMarker PoseMarker = new ProfilerMarker("HairCards.PosePreview");
        private readonly Dictionary<string, Matrix4x4> guideMatrices = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        private HairGroomAsset matrixGroom;

        // The posed source is an immutable snapshot. Refresh root bindings once per evaluation,
        // not once per child/card/SceneView line (which previously scanned every guide).
        internal void RefreshGuideMatrices(HairGroomAsset groom)
        {
            guideMatrices.Clear();
            matrixGroom = groom;
            if (!IsActive || groom == null) return;
            foreach (HairGroup group in groom.Groups)
                if (group?.guides != null)
                    foreach (HairGuide guide in group.guides)
                        if (guide != null && !string.IsNullOrEmpty(guide.Id) && !guideMatrices.ContainsKey(guide.Id))
                            guideMatrices.Add(guide.Id, TryGetMatrix(guide.root, out Matrix4x4 matrix)
                                ? matrix : Matrix4x4.identity);
        }

        internal Matrix4x4 MatrixForGuide(HairGroomAsset groom, string guideId)
        {
            if (!IsActive) return Matrix4x4.identity;
            if (!ReferenceEquals(groom, matrixGroom)) RefreshGuideMatrices(groom);
            return guideId != null && guideMatrices.TryGetValue(guideId, out Matrix4x4 matrix)
                ? matrix : Matrix4x4.identity;
        }

        internal bool TryGetMatrix(HairSurfaceAnchor anchor, out Matrix4x4 sourceToPose)
        {
            sourceToPose = Matrix4x4.identity;
            if (!IsActive || !anchor.IsValid) return false;
            int submesh = anchor.SubmeshIndex;
            if ((uint)submesh >= (uint)sourceTriangles.Length) return false;
            int[] triangles = sourceTriangles[submesh];
            int triangleOffset = anchor.TriangleIndex * 3;
            if (triangles == null || triangleOffset < 0 || triangleOffset + 2 >= triangles.Length)
                return false;
            int a = triangles[triangleOffset];
            int b = triangles[triangleOffset + 1];
            int c = triangles[triangleOffset + 2];
            if ((uint)a >= (uint)sourceVertices.Length || (uint)b >= (uint)sourceVertices.Length ||
                (uint)c >= (uint)sourceVertices.Length || (uint)a >= (uint)posedVertices.Length ||
                (uint)b >= (uint)posedVertices.Length || (uint)c >= (uint)posedVertices.Length)
                return false;
            return HairPoseUtility.TryCreateTriangleTransform(
                sourceVertices[a], sourceVertices[b], sourceVertices[c],
                posedVertices[a], posedVertices[b], posedVertices[c],
                anchor.Barycentric, out sourceToPose);
        }

        internal Vector3 PosedVertex(int vertex)
        {
            return IsActive && (uint)vertex < (uint)posedVertices.Length
                ? posedVertices[vertex]
                : (uint)vertex < (uint)sourceVertices.Length ? sourceVertices[vertex] : Vector3.zero;
        }

        internal bool TryPoseTrianglePoint(int flattenedTriangleIndex, Vector3 barycentric,
            out Vector3 posedPoint, out Vector3 posedNormal)
        {
            posedPoint = Vector3.zero;
            posedNormal = Vector3.up;
            int offset = flattenedTriangleIndex * 3;
            if (offset < 0 || offset + 2 >= flattenedTriangles.Length) return false;
            int a = flattenedTriangles[offset];
            int b = flattenedTriangles[offset + 1];
            int c = flattenedTriangles[offset + 2];
            Vector3[] vertices = IsActive ? posedVertices : sourceVertices;
            if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length ||
                (uint)c >= (uint)vertices.Length) return false;
            posedPoint = vertices[a] * barycentric.x + vertices[b] * barycentric.y +
                         vertices[c] * barycentric.z;
            if (IsActive && posedNormals.Length == posedVertices.Length)
            {
                posedNormal = posedNormals[a] * barycentric.x + posedNormals[b] * barycentric.y +
                             posedNormals[c] * barycentric.z;
            }
            else
            {
                posedNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            }
            posedNormal = posedNormal.sqrMagnitude > 1e-12f ? posedNormal.normalized : Vector3.up;
            return true;
        }

        internal Matrix4x4 MatrixNearSourcePoint(string sourceMeshId, Vector3 sourcePoint)
        {
            if (!IsActive || !HairMeshUtility.TryFindClosestSurface(source, sourceMeshId, sourcePoint,
                    out HairSurfaceAnchor anchor)) return Matrix4x4.identity;
            return TryGetMatrix(anchor, out Matrix4x4 matrix) ? matrix : Matrix4x4.identity;
        }

        internal Matrix4x4 MatrixNearPosedPoint(string sourceMeshId, Vector3 posedPoint)
        {
            if (!IsActive || !HairMeshUtility.TryFindClosestSurface(posed, sourceMeshId, posedPoint,
                    out HairSurfaceAnchor anchor)) return Matrix4x4.identity;
            return TryGetMatrix(anchor, out Matrix4x4 matrix) ? matrix : Matrix4x4.identity;
        }

        internal Vector3 SourcePointFromPose(string sourceMeshId, Vector3 posedPoint)
        {
            Matrix4x4 sourceToPose = MatrixNearPosedPoint(sourceMeshId, posedPoint);
            return sourceToPose.inverse.MultiplyPoint3x4(posedPoint);
        }

        internal HairEvaluationResult TransformEvaluation(HairGroomAsset groom, HairEvaluationResult sourceResult,
            HairEvaluationResult reusableResult = null)
        {
            if (!IsActive || sourceResult == null) return sourceResult;
            if (ReferenceEquals(sourceResult, reusableResult)) throw new ArgumentException("Pose destination must not be the source evaluation.");
            using var poseScope = PoseMarker.Auto();
            RefreshGuideMatrices(groom);
            HairEvaluationResult transformed = reusableResult ?? new HairEvaluationResult();
            transformed.guideCurveCount = sourceResult.guideCurveCount;
            transformed.childCurveCount = sourceResult.childCurveCount;
            transformed.rejectedCurveCount = sourceResult.rejectedCurveCount;
            transformed.revision = sourceResult.revision;
            transformed.warnings.Clear();
            transformed.warnings.AddRange(sourceResult.warnings);
            TransformCurves(groom, sourceResult.evaluatedGuides, transformed.evaluatedGuides);
            TransformCurves(groom, sourceResult.curves, transformed.curves);
            return transformed;
        }

        private void TransformCurves(HairGroomAsset groom, List<HairEvaluatedCurve> sourceCurves, List<HairEvaluatedCurve> targets)
        {
            for (int i = 0; i < sourceCurves.Count; i++)
            {
                HairEvaluatedCurve transformed = TransformCurve(groom, sourceCurves[i], i < targets.Count ? targets[i] : null);
                if (i < targets.Count) targets[i] = transformed;
                else targets.Add(transformed);
            }
            if (targets.Count > sourceCurves.Count) targets.RemoveRange(sourceCurves.Count, targets.Count - sourceCurves.Count);
        }

        private HairEvaluatedCurve TransformCurve(HairGroomAsset groom, HairEvaluatedCurve sourceCurve, HairEvaluatedCurve transformed)
        {
            if (sourceCurve == null) return null;
            Matrix4x4 matrix = MatrixForGuide(groom, sourceCurve.parentGuideId);
            transformed ??= new HairEvaluatedCurve(sourceCurve.points.Count);
            sourceCurve.CopyTo(transformed);
            transformed.rootNormal = HairPoseUtility.TransformNormal(matrix, sourceCurve.rootNormal);
            for (int pointIndex = 0; pointIndex < transformed.points.Count; pointIndex++)
            {
                HairCurvePoint point = transformed.points[pointIndex];
                point.position = matrix.MultiplyPoint3x4(point.position);
                transformed.points[pointIndex] = point;
            }
            return transformed;
        }
    }


    internal sealed class HairSourceVisibility : IDisposable
    {
        internal readonly struct TriangleReference
        {
            internal readonly int Submesh;
            internal readonly int Triangle;
            internal readonly int A;
            internal readonly int B;
            internal readonly int C;

            internal TriangleReference(int submesh, int triangle, int a, int b, int c)
            {
                Submesh = submesh;
                Triangle = triangle;
                A = a;
                B = b;
                C = c;
            }
        }

        private sealed class SourceTriangle
        {
            internal TriangleReference reference;
            internal string slotName;
            internal bool duplicatePass;
        }

        private readonly Mesh source;
        private readonly Mesh surface;
        private readonly List<SourceTriangle> triangles = new List<SourceTriangle>();
        private readonly List<TriangleReference> visibleTriangles = new List<TriangleReference>();
        private readonly HashSet<string> appliedHiddenSlots = new HashSet<string>(StringComparer.Ordinal);
        private readonly string[] vertexOwners;
        private Mesh visibleMesh;
        private bool hasBuiltVisibility;

        internal Mesh VisibleMesh => visibleMesh;

        internal HairSourceVisibility(Mesh sourceMesh, UMAData data, SkinnedMeshRenderer renderer,
            IReadOnlyDictionary<string, SlotData> renderedSlots)
            : this(sourceMesh, sourceMesh, data, renderer, renderedSlots)
        {
        }

        internal HairSourceVisibility(Mesh sourceMesh, Mesh surfaceMesh, UMAData data,
            SkinnedMeshRenderer renderer, IReadOnlyDictionary<string, SlotData> renderedSlots)
        {
            source = sourceMesh;
            surface = surfaceMesh != null && sourceMesh != null &&
                      surfaceMesh.vertexCount == sourceMesh.vertexCount ? surfaceMesh : sourceMesh;
            vertexOwners = source != null ? new string[source.vertexCount] : Array.Empty<string>();
            if (source == null) return;

            List<SlotData> allSlots = new List<SlotData>();
            int rendererIndex = -1;
            SkinnedMeshRenderer[] dataRenderers = data?.GetRenderers();
            if (dataRenderers != null)
                for (int i = 0; i < dataRenderers.Length; i++)
                    if (dataRenderers[i] == renderer) { rendererIndex = i; break; }
            if (renderedSlots != null)
                foreach (KeyValuePair<string, SlotData> pair in renderedSlots)
                    if (pair.Value != null && (rendererIndex < 0 || pair.Value.skinnedMeshRenderer == rendererIndex) &&
                        !allSlots.Contains(pair.Value)) allSlots.Add(pair.Value);
            for (int vertex = 0; vertex < vertexOwners.Length; vertex++)
            {
                for (int slotIndex = 0; slotIndex < allSlots.Count; slotIndex++)
                {
                    SlotData slot = allSlots[slotIndex];
                    if (!slot.OwnsVertex(vertex)) continue;
                    vertexOwners[vertex] = slot.slotName;
                    break;
                }
            }

            Material[] materials = renderer != null ? renderer.sharedMaterials : Array.Empty<Material>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                UMAData.GeneratedMaterial generated = null;
                bool secondPass = false;
                if (renderer != null)
                {
                    Material material = submesh < materials.Length ? materials[submesh] : null;
                    HairAvatarGeometryUtility.FindGeneratedMaterial(data, renderer, material, submesh,
                        out generated, out secondPass);
                }
                List<SlotData> candidates = HairAvatarGeometryUtility.FindSlots(generated);
                if (candidates.Count == 0) candidates = allSlots;
                int[] submeshTriangles = source.GetTriangles(submesh, true);
                for (int index = 0, triangle = 0; index + 2 < submeshTriangles.Length; index += 3, triangle++)
                {
                    int a = submeshTriangles[index];
                    int b = submeshTriangles[index + 1];
                    int c = submeshTriangles[index + 2];
                    SlotData owner = HairAvatarGeometryUtility.FindTriangleOwner(a, b, c, candidates);
                    triangles.Add(new SourceTriangle
                    {
                        reference = new TriangleReference(submesh, triangle, a, b, c),
                        slotName = owner?.slotName,
                        duplicatePass = secondPass
                    });
                }
            }
        }

        internal Mesh Rebuild(ISet<string> hiddenSlots)
        {
            if (hasBuiltVisibility && SetEquals(appliedHiddenSlots, hiddenSlots)) return visibleMesh;
            appliedHiddenSlots.Clear();
            if (hiddenSlots != null) appliedHiddenSlots.UnionWith(hiddenSlots);
            hasBuiltVisibility = true;
            visibleTriangles.Clear();
            List<int> indices = new List<int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                SourceTriangle sourceTriangle = triangles[i];
                if (sourceTriangle.duplicatePass ||
                    (!string.IsNullOrEmpty(sourceTriangle.slotName) && hiddenSlots != null &&
                     hiddenSlots.Contains(sourceTriangle.slotName))) continue;
                visibleTriangles.Add(sourceTriangle.reference);
                indices.Add(sourceTriangle.reference.A);
                indices.Add(sourceTriangle.reference.B);
                indices.Add(sourceTriangle.reference.C);
            }

            Mesh replacement = UnityEngine.Object.Instantiate(surface);
            replacement.name = source.name + " Hair Card Visible Source";
            replacement.hideFlags = HideFlags.HideAndDontSave;
            replacement.subMeshCount = 1;
            replacement.SetTriangles(indices, 0, true);
            replacement.RecalculateBounds();
            Mesh previous = visibleMesh;
            visibleMesh = replacement;
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            return visibleMesh;
        }

        private static bool SetEquals(HashSet<string> applied, ISet<string> requested)
        {
            if (requested == null || requested.Count == 0) return applied.Count == 0;
            return applied.Count == requested.Count && applied.SetEquals(requested);
        }

        internal bool TryResolveVisibleTriangle(int triangleIndex, out TriangleReference reference)
        {
            if ((uint)triangleIndex < (uint)visibleTriangles.Count)
            {
                reference = visibleTriangles[triangleIndex];
                return true;
            }
            reference = default;
            return false;
        }

        internal bool IsVertexVisible(int vertex, ISet<string> hiddenSlots)
        {
            if ((uint)vertex >= (uint)vertexOwners.Length) return false;
            string owner = vertexOwners[vertex];
            return string.IsNullOrEmpty(owner) || hiddenSlots == null || !hiddenSlots.Contains(owner);
        }

        public void Dispose()
        {
            if (visibleMesh != null) UnityEngine.Object.DestroyImmediate(visibleMesh);
            visibleMesh = null;
            visibleTriangles.Clear();
            triangles.Clear();
            appliedHiddenSlots.Clear();
            hasBuiltVisibility = false;
        }
    }
}
