using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: InternalsVisibleTo("UMA.HairCards.Editor.Tests")]

namespace UMA.HairCards.Editor
{
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

    internal sealed class HairAvatarPreview : IDisposable
    {
        private sealed class Surface
        {
            internal GameObject gameObject;
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
            Surface surface = new Surface { gameObject = child };
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

        internal Matrix4x4 MatrixForGuide(HairGroomAsset groom, string guideId)
        {
            HairGuide guide = groom?.FindGuide(guideId, out _);
            return guide != null && TryGetMatrix(guide.root, out Matrix4x4 matrix)
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

        internal HairEvaluationResult TransformEvaluation(HairGroomAsset groom, HairEvaluationResult sourceResult)
        {
            if (!IsActive || sourceResult == null) return sourceResult;
            HairEvaluationResult transformed = new HairEvaluationResult
            {
                guideCurveCount = sourceResult.guideCurveCount,
                childCurveCount = sourceResult.childCurveCount,
                rejectedCurveCount = sourceResult.rejectedCurveCount,
                revision = sourceResult.revision
            };
            transformed.warnings.AddRange(sourceResult.warnings);
            for (int i = 0; i < sourceResult.evaluatedGuides.Count; i++)
                transformed.evaluatedGuides.Add(TransformCurve(groom, sourceResult.evaluatedGuides[i]));
            for (int i = 0; i < sourceResult.curves.Count; i++)
                transformed.curves.Add(TransformCurve(groom, sourceResult.curves[i]));
            return transformed;
        }

        private HairEvaluatedCurve TransformCurve(HairGroomAsset groom, HairEvaluatedCurve sourceCurve)
        {
            if (sourceCurve == null) return null;
            Matrix4x4 matrix = MatrixForGuide(groom, sourceCurve.parentGuideId);
            HairEvaluatedCurve transformed = sourceCurve.Clone();
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

    internal readonly struct HairMeshRaycastHit
    {
        internal readonly int TriangleIndex;
        internal readonly Vector3 Point;
        internal readonly Vector3 Normal;
        internal readonly Vector3 Barycentric;
        internal readonly float Distance;

        internal HairMeshRaycastHit(int triangleIndex, Vector3 point, Vector3 normal,
            Vector3 barycentric, float distance)
        {
            TriangleIndex = triangleIndex;
            Point = point;
            Normal = normal;
            Barycentric = barycentric;
            Distance = distance;
        }
    }

    /// <summary>
    /// Preview scenes do not consistently register colliders with a queryable physics scene. This
    /// small BVH keeps hair-authoring hover and strokes independent of editor physics while avoiding
    /// a full triangle scan for every mouse-move event.
    /// </summary>
    internal sealed class HairMeshRaycaster
    {
        private const int LeafTriangleCount = 8;
        private const float IntersectionEpsilon = 0.0000001f;

        private struct Node
        {
            internal Vector3 min;
            internal Vector3 max;
            internal int left;
            internal int right;
            internal int start;
            internal int count;
        }

        private sealed class CentroidComparer : IComparer<int>
        {
            internal Vector3[] centroids;
            internal int axis;

            public int Compare(int left, int right)
            {
                float difference = centroids[left][axis] - centroids[right][axis];
                if (difference < 0f) return -1;
                if (difference > 0f) return 1;
                return left.CompareTo(right);
            }
        }

        private Vector3[] vertices = Array.Empty<Vector3>();
        private int[] triangleVertices = Array.Empty<int>();
        private Vector3[] triangleMinimums = Array.Empty<Vector3>();
        private Vector3[] triangleMaximums = Array.Empty<Vector3>();
        private Vector3[] triangleCentroids = Array.Empty<Vector3>();
        private int[] triangleOrder = Array.Empty<int>();
        private Node[] nodes = Array.Empty<Node>();
        private int[] traversalStack = Array.Empty<int>();
        private readonly CentroidComparer centroidComparer = new CentroidComparer();

        internal int TriangleCount => triangleVertices.Length / 3;

        internal HairMeshRaycaster(Mesh mesh)
        {
            Rebuild(mesh);
        }

        internal void Rebuild(Mesh mesh)
        {
            vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
            List<int> flattened = new List<int>();
            if (mesh != null)
            {
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] indices = mesh.GetTriangles(submesh, true);
                    flattened.AddRange(indices);
                }
            }
            triangleVertices = flattened.ToArray();
            int triangleCount = TriangleCount;
            triangleMinimums = new Vector3[triangleCount];
            triangleMaximums = new Vector3[triangleCount];
            triangleCentroids = new Vector3[triangleCount];
            triangleOrder = new int[triangleCount];
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int index = triangle * 3;
                int a = triangleVertices[index];
                int b = triangleVertices[index + 1];
                int c = triangleVertices[index + 2];
                if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length ||
                    (uint)c >= (uint)vertices.Length)
                {
                    triangleMinimums[triangle] = Vector3.zero;
                    triangleMaximums[triangle] = Vector3.zero;
                    triangleCentroids[triangle] = Vector3.zero;
                }
                else
                {
                    Vector3 minimum = Vector3.Min(vertices[a], Vector3.Min(vertices[b], vertices[c]));
                    Vector3 maximum = Vector3.Max(vertices[a], Vector3.Max(vertices[b], vertices[c]));
                    triangleMinimums[triangle] = minimum;
                    triangleMaximums[triangle] = maximum;
                    triangleCentroids[triangle] = (minimum + maximum) * 0.5f;
                }
                triangleOrder[triangle] = triangle;
            }

            if (triangleCount == 0)
            {
                nodes = Array.Empty<Node>();
                traversalStack = Array.Empty<int>();
                return;
            }

            List<Node> buildNodes = new List<Node>(triangleCount * 2);
            centroidComparer.centroids = triangleCentroids;
            BuildNode(buildNodes, 0, triangleCount);
            nodes = buildNodes.ToArray();
            traversalStack = new int[nodes.Length];
        }

        internal bool Raycast(Ray ray, out HairMeshRaycastHit hit)
        {
            hit = default;
            if (nodes.Length == 0 || ray.direction.sqrMagnitude < IntersectionEpsilon) return false;
            ray.direction = ray.direction.normalized;
            float closest = float.MaxValue;
            int closestTriangle = -1;
            Vector3 closestBarycentric = Vector3.zero;
            int stackCount = 0;
            traversalStack[stackCount++] = 0;
            while (stackCount > 0)
            {
                Node node = nodes[traversalStack[--stackCount]];
                if (!IntersectsBounds(ray, node.min, node.max, closest)) continue;
                if (node.count > 0)
                {
                    for (int ordered = node.start; ordered < node.start + node.count; ordered++)
                    {
                        int triangle = triangleOrder[ordered];
                        int index = triangle * 3;
                        int a = triangleVertices[index];
                        int b = triangleVertices[index + 1];
                        int c = triangleVertices[index + 2];
                        if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length ||
                            (uint)c >= (uint)vertices.Length ||
                            !IntersectsTriangle(ray, vertices[a], vertices[b], vertices[c],
                                out float distance, out Vector3 barycentric) || distance >= closest) continue;
                        closest = distance;
                        closestTriangle = triangle;
                        closestBarycentric = barycentric;
                    }
                    continue;
                }
                if (node.left >= 0) traversalStack[stackCount++] = node.left;
                if (node.right >= 0) traversalStack[stackCount++] = node.right;
            }

            if (closestTriangle < 0) return false;
            int triangleOffset = closestTriangle * 3;
            Vector3 first = vertices[triangleVertices[triangleOffset]];
            Vector3 second = vertices[triangleVertices[triangleOffset + 1]];
            Vector3 third = vertices[triangleVertices[triangleOffset + 2]];
            Vector3 normal = Vector3.Cross(second - first, third - first);
            normal = normal.sqrMagnitude > IntersectionEpsilon ? normal.normalized : -ray.direction;
            if (Vector3.Dot(normal, ray.direction) > 0f) normal = -normal;
            hit = new HairMeshRaycastHit(closestTriangle, ray.GetPoint(closest), normal,
                closestBarycentric, closest);
            return true;
        }

        internal bool TryGetTriangleVertices(int triangleIndex, out int a, out int b, out int c)
        {
            int offset = triangleIndex * 3;
            if (offset < 0 || offset + 2 >= triangleVertices.Length)
            {
                a = b = c = -1;
                return false;
            }
            a = triangleVertices[offset];
            b = triangleVertices[offset + 1];
            c = triangleVertices[offset + 2];
            return true;
        }

        private int BuildNode(List<Node> buildNodes, int start, int count)
        {
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            Vector3 centroidMinimum = minimum;
            Vector3 centroidMaximum = maximum;
            for (int ordered = start; ordered < start + count; ordered++)
            {
                int triangle = triangleOrder[ordered];
                minimum = Vector3.Min(minimum, triangleMinimums[triangle]);
                maximum = Vector3.Max(maximum, triangleMaximums[triangle]);
                centroidMinimum = Vector3.Min(centroidMinimum, triangleCentroids[triangle]);
                centroidMaximum = Vector3.Max(centroidMaximum, triangleCentroids[triangle]);
            }

            int nodeIndex = buildNodes.Count;
            buildNodes.Add(default);
            Vector3 centroidSize = centroidMaximum - centroidMinimum;
            if (count <= LeafTriangleCount || centroidSize.sqrMagnitude < IntersectionEpsilon)
            {
                buildNodes[nodeIndex] = new Node
                {
                    min = minimum, max = maximum, left = -1, right = -1, start = start, count = count
                };
                return nodeIndex;
            }

            centroidComparer.axis = centroidSize.x >= centroidSize.y && centroidSize.x >= centroidSize.z
                ? 0 : centroidSize.y >= centroidSize.z ? 1 : 2;
            Array.Sort(triangleOrder, start, count, centroidComparer);
            int leftCount = count / 2;
            int left = BuildNode(buildNodes, start, leftCount);
            int right = BuildNode(buildNodes, start + leftCount, count - leftCount);
            buildNodes[nodeIndex] = new Node
            {
                min = minimum, max = maximum, left = left, right = right, start = 0, count = 0
            };
            return nodeIndex;
        }

        private static bool IntersectsBounds(Ray ray, Vector3 minimum, Vector3 maximum,
            float maximumDistance)
        {
            float near = 0f;
            float far = maximumDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                float direction = ray.direction[axis];
                float origin = ray.origin[axis];
                if (Mathf.Abs(direction) < IntersectionEpsilon)
                {
                    if (origin < minimum[axis] || origin > maximum[axis]) return false;
                    continue;
                }
                float inverse = 1f / direction;
                float first = (minimum[axis] - origin) * inverse;
                float second = (maximum[axis] - origin) * inverse;
                if (first > second) (first, second) = (second, first);
                near = Mathf.Max(near, first);
                far = Mathf.Min(far, second);
                if (near > far) return false;
            }
            return far >= 0f;
        }

        private static bool IntersectsTriangle(Ray ray, Vector3 first, Vector3 second, Vector3 third,
            out float distance, out Vector3 barycentric)
        {
            distance = 0f;
            barycentric = Vector3.zero;
            Vector3 edgeOne = second - first;
            Vector3 edgeTwo = third - first;
            Vector3 cross = Vector3.Cross(ray.direction, edgeTwo);
            float determinant = Vector3.Dot(edgeOne, cross);
            if (Mathf.Abs(determinant) < IntersectionEpsilon) return false;
            float inverse = 1f / determinant;
            Vector3 fromFirst = ray.origin - first;
            float secondWeight = Vector3.Dot(fromFirst, cross) * inverse;
            if (secondWeight < 0f || secondWeight > 1f) return false;
            Vector3 sideCross = Vector3.Cross(fromFirst, edgeOne);
            float thirdWeight = Vector3.Dot(ray.direction, sideCross) * inverse;
            if (thirdWeight < 0f || secondWeight + thirdWeight > 1f) return false;
            distance = Vector3.Dot(edgeTwo, sideCross) * inverse;
            if (distance <= IntersectionEpsilon) return false;
            barycentric = new Vector3(1f - secondWeight - thirdWeight, secondWeight, thirdWeight);
            return true;
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
