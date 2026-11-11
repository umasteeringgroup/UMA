using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    /// <summary>Settings for the automatic mesh hide generator. The defaults for the two distances are the Face Editor's.</summary>
    [Serializable]
    public sealed class UMAMeshHideOcclusionSettings
    {
        [Tooltip("How far, in metres, to look outward along each body vertex's normal for clothing that covers it.")]
        public float outwardDistance = 0.1f;

        [Tooltip("How far, in metres, to look inward for clothing the body pokes through.")]
        public float inwardDistance = 0.02f;

        [Tooltip("Strict hides a triangle only when all three of its vertices are covered, so nothing visible is removed. Weighted needs two, Conservative one: they hide more and leave less to poke through, but can open gaps at collars and cuffs.")]
        public MeshHideAsset.TriangleHideStrategy strategy = MeshHideAsset.TriangleHideStrategy.Strict;

        [Tooltip("Only clothing that lies over the skin counts: its surface must face the same way as the body surface under it. This keeps, for example, the inside of a bare arm from being hidden because it faces the side of a vest.")]
        public bool requireSameFacing = true;
    }

    /// <summary>
    /// The geometry side of automatic mesh hides: bakes slots to avatar space at a rig's rest pose and
    /// tests which body vertices are covered by clothing. It follows the Face Editor's Raycast
    /// Occlusion (a ray outward along each vertex normal, then a short one inward, then a per-triangle
    /// strategy), and adds a facing rule so it can run without someone checking the result by hand.
    /// </summary>
    public static class UMAMeshHideOcclusion
    {
        private const float OriginEpsilon = 0.0005f;
        private const float FacingThreshold = 0.2f;

        /// <summary>Clothing triangles in avatar space, in a grid so short rays only meet nearby triangles.</summary>
        public sealed class OccluderSet
        {
            private readonly float cellSize;
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
            private readonly List<Vector3> v0 = new List<Vector3>();
            private readonly List<Vector3> v1 = new List<Vector3>();
            private readonly List<Vector3> v2 = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private int[] visited = new int[0];
            private int visit;

            public OccluderSet(float cellSize = 0.05f)
            {
                this.cellSize = Mathf.Max(0.005f, cellSize);
            }

            public int TriangleCount => v0.Count;

            /// <summary>Adds a mesh's triangles. Degenerate triangles are skipped.</summary>
            public void Add(Vector3[] positions, int[] triangles)
            {
                if (positions == null || triangles == null)
                {
                    return;
                }
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= positions.Length || i1 >= positions.Length || i2 >= positions.Length)
                    {
                        continue;
                    }
                    Vector3 a = positions[i0], b = positions[i1], c = positions[i2];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    float length = normal.magnitude;
                    if (length < 1e-12f)
                    {
                        continue;
                    }
                    int index = v0.Count;
                    v0.Add(a);
                    v1.Add(b);
                    v2.Add(c);
                    normals.Add(normal / length);

                    Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
                    Vector3 max = Vector3.Max(a, Vector3.Max(b, c));
                    ForEachCell(min, max, key =>
                    {
                        if (!cells.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>(8);
                            cells.Add(key, list);
                        }
                        list.Add(index);
                    });
                }
            }

            /// <summary>
            /// True when a ray from <paramref name="origin"/> meets a triangle within
            /// <paramref name="distance"/>. With <paramref name="surfaceNormal"/> given, only triangles
            /// facing the same way as that normal count.
            /// </summary>
            public bool Covers(Vector3 origin, Vector3 direction, float distance, Vector3? surfaceNormal)
            {
                if (v0.Count == 0 || distance <= 0f)
                {
                    return false;
                }
                if (visited.Length < v0.Count)
                {
                    visited = new int[v0.Count];
                    visit = 0;
                }
                visit++;
                int stamp = visit;
                Vector3 end = origin + direction * distance;
                bool hit = false;
                ForEachCell(Vector3.Min(origin, end), Vector3.Max(origin, end), key =>
                {
                    if (hit || !cells.TryGetValue(key, out List<int> list))
                    {
                        return;
                    }
                    for (int i = 0; i < list.Count; i++)
                    {
                        int index = list[i];
                        if (visited[index] == stamp)
                        {
                            continue;
                        }
                        visited[index] = stamp;
                        if (surfaceNormal.HasValue && Vector3.Dot(normals[index], surfaceNormal.Value) <= FacingThreshold)
                        {
                            continue;
                        }
                        if (RayMeetsTriangle(origin, direction, v0[index], v1[index], v2[index], out float t) && t <= distance)
                        {
                            hit = true;
                            return;
                        }
                    }
                });
                return hit;
            }

            private void ForEachCell(Vector3 min, Vector3 max, Action<long> action)
            {
                int x0 = Mathf.FloorToInt(min.x / cellSize), x1 = Mathf.FloorToInt(max.x / cellSize);
                int y0 = Mathf.FloorToInt(min.y / cellSize), y1 = Mathf.FloorToInt(max.y / cellSize);
                int z0 = Mathf.FloorToInt(min.z / cellSize), z1 = Mathf.FloorToInt(max.z / cellSize);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int z = z0; z <= z1; z++)
                        {
                            action(((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF));
                        }
                    }
                }
            }

            /// <summary>Möller–Trumbore, either side of the triangle; <paramref name="t"/> is the distance along the ray.</summary>
            private static bool RayMeetsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
            {
                t = 0f;
                Vector3 edge1 = b - a;
                Vector3 edge2 = c - a;
                Vector3 p = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, p);
                if (Mathf.Abs(determinant) < 1e-12f)
                {
                    return false;
                }
                float inverse = 1f / determinant;
                Vector3 s = origin - a;
                float u = Vector3.Dot(s, p) * inverse;
                if (u < 0f || u > 1f)
                {
                    return false;
                }
                Vector3 q = Vector3.Cross(s, edge1);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < 0f || u + v > 1f)
                {
                    return false;
                }
                t = Vector3.Dot(edge2, q) * inverse;
                return t >= 0f;
            }
        }

        /// <summary>
        /// Skins mesh data into avatar space with the rig at rest: every vertex through the bind poses
        /// of its own influences. Slots bound to the same rig then share one space, however each was
        /// exported. Returns false for mesh data that is not skinned or has no normals.
        /// </summary>
        public static bool TryBakeToAvatar(UMAMeshData mesh, UMARigRestPose rig, out Vector3[] positions, out Vector3[] normals)
        {
            return UMARigBodySurface.TryBakeToAvatar(mesh, rig, out positions, out normals);
        }

        /// <summary>
        /// Skins mesh data into avatar space with a character's skeleton as it stands now, whatever
        /// pose it is in: where Unity draws the slot on that character. Returns false for mesh data
        /// that is not skinned or has no normals, or when the skeleton lacks one of its bones.
        /// </summary>
        /// <param name="worldToAvatar">The character root's world-to-local matrix.</param>
        public static bool TryBakeWithSkeleton(UMAMeshData mesh, UMASkeleton skeleton, Matrix4x4 worldToAvatar, out Vector3[] positions, out Vector3[] normals)
        {
            positions = null;
            normals = null;
            if (skeleton == null || !UMASlotRigConverter.CanConvert(mesh, out _) || mesh.normals == null || mesh.normals.Length != mesh.vertices.Length)
            {
                return false;
            }
            int influenceCount = Mathf.Min(mesh.bindPoses.Length, mesh.boneNameHashes.Length);
            var skin = new Matrix4x4[influenceCount];
            for (int i = 0; i < influenceCount; i++)
            {
                int hash = mesh.boneNameHashes[i];
                Transform bone = skeleton.HasBone(hash) ? skeleton.GetBoneTransform(hash) : null;
                if (bone == null)
                {
                    return false;
                }
                skin[i] = worldToAvatar * bone.localToWorldMatrix * mesh.bindPoses[i];
            }
            UMARigBodySurface.Skin(mesh, skin, Matrix4x4.identity, out positions, out normals);
            return true;
        }

        /// <summary>
        /// Which vertices are covered: clothing within the outward distance along the normal, or, failing
        /// that, clothing within the inward distance below the surface (the body poking through it).
        /// </summary>
        public static bool[] FindCoveredVertices(Vector3[] positions, Vector3[] normals, OccluderSet occluders, UMAMeshHideOcclusionSettings settings)
        {
            var covered = new bool[positions.Length];
            if (occluders == null || occluders.TriangleCount == 0)
            {
                return covered;
            }
            float outward = Mathf.Max(0f, settings.outwardDistance);
            float inward = Mathf.Max(0f, settings.inwardDistance);
            for (int v = 0; v < positions.Length; v++)
            {
                Vector3 normal = normals[v];
                if (normal.sqrMagnitude < 1e-8f)
                {
                    continue;
                }
                Vector3? facing = settings.requireSameFacing ? normal : (Vector3?)null;
                bool hit = false;
                if (outward > 0f)
                {
                    hit = occluders.Covers(positions[v] + normal * Mathf.Max(OriginEpsilon, outward * 0.001f), normal, outward, facing);
                }
                if (!hit && inward > 0f)
                {
                    hit = occluders.Covers(positions[v] - normal * Mathf.Max(OriginEpsilon, inward * 0.001f), -normal, inward, facing);
                }
                covered[v] = hit;
            }
            return covered;
        }

        /// <summary>Turns covered vertices into hidden triangles by the Face Editor's three strategies.</summary>
        public static BitArray FindHiddenTriangles(int[] triangles, bool[] coveredVertices, MeshHideAsset.TriangleHideStrategy strategy, out int hiddenCount)
        {
            hiddenCount = 0;
            int triangleCount = triangles != null ? triangles.Length / 3 : 0;
            var flags = new BitArray(triangleCount);
            for (int t = 0; t < triangleCount; t++)
            {
                int covered = 0;
                for (int k = 0; k < 3; k++)
                {
                    int index = triangles[t * 3 + k];
                    if (index >= 0 && index < coveredVertices.Length && coveredVertices[index])
                    {
                        covered++;
                    }
                }
                bool hide = strategy == MeshHideAsset.TriangleHideStrategy.Strict ? covered == 3
                    : strategy == MeshHideAsset.TriangleHideStrategy.Weighted ? covered >= 2
                    : covered >= 1;
                if (hide)
                {
                    flags[t] = true;
                    hiddenCount++;
                }
            }
            return flags;
        }
    }

    /// <summary>
    /// Creates Mesh Hide Assets for wardrobe recipes automatically: for each recipe it finds the
    /// triangles of the race's body that the recipe's clothing covers, saves one Mesh Hide Asset per
    /// body slot next to the recipe, and adds them to the recipe. Clothing built for the race's legacy
    /// rig is converted first, with the race's rig profile, so the hides match what the character wears.
    /// </summary>
    /// <remarks>
    /// The hides name the body's base slots, not the copies a race bakes blend shapes into; UMA applies
    /// a hide for a base slot to its baked copy as well. The body's shape, though, is taken from the
    /// slots the race really builds with, baked copies included.
    /// </remarks>
    public static class UMAMeshHideAutoGenerator
    {
        public const string FolderName = "MeshHides";

        /// <summary>One body slot of a race, as the race builds it, in avatar space at rest.</summary>
        public sealed class BodySlot
        {
            /// <summary>The base slot asset in the project, which the hide names.</summary>
            public SlotDataAsset BaseSlot;
            public string BuiltSlotName;
            public Vector3[] Positions;
            public Vector3[] Normals;
            /// <summary>Base-LOD triangles of each submesh of the built slot.</summary>
            public int[][] Triangles;
            /// <summary>The blend shapes the built slot still carries: the ones a character of the race can drive.</summary>
            public string[] BuiltBlendShapeNames = System.Array.Empty<string>();
        }

        public sealed class SlotResult
        {
            public string SlotName;
            public int HiddenTriangles;
            public int TotalTriangles;
            public MeshHideAsset Asset;
            /// <summary>The hide is locked, so it was left exactly as it was.</summary>
            public bool Locked;
            /// <summary>How many painted "never hide" and "always hide" marks were applied on top of the computed result.</summary>
            public int Corrections;
        }

        public sealed class RecipeResult
        {
            public UMATextRecipe Recipe;
            public int OccluderTriangles;
            public readonly List<SlotResult> Slots = new List<SlotResult>();
            public readonly List<string> Messages = new List<string>();

            public int HiddenTriangles
            {
                get
                {
                    int total = 0;
                    for (int i = 0; i < Slots.Count; i++) total += Slots[i].HiddenTriangles;
                    return total;
                }
            }

            public string Summary()
            {
                var parts = new List<string>();
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i].HiddenTriangles > 0 || Slots[i].Locked)
                    {
                        string note = Slots[i].Locked ? " locked"
                            : Slots[i].Corrections > 0 ? $" with {Slots[i].Corrections} painted" : string.Empty;
                        parts.Add($"{Slots[i].SlotName} {Slots[i].HiddenTriangles}/{Slots[i].TotalTriangles}{note}");
                    }
                }
                string name = Recipe != null ? Recipe.name : "(recipe)";
                return parts.Count == 0 ? $"{name}: nothing covered" : $"{name}: {HiddenTriangles} triangles hidden ({string.Join(", ", parts)})";
            }
        }

        /// <summary>
        /// The slots a race builds its body from, baked to avatar space. A temporary character is
        /// generated to get them, because a race with prebaked blend shapes builds from baked copies
        /// whose shape differs from the base slots'; it is destroyed again before this returns.
        /// </summary>
        public static List<BodySlot> CollectBodySlots(RaceData race, UMARigRestPose rig, List<string> messages = null)
        {
            var result = new List<BodySlot>();
            var avatarObject = new GameObject("Mesh Hide Generator (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            DynamicCharacterAvatar avatar = null;
            try
            {
                avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
                // The same precautions as the rig profile preview: nothing but this code may build it.
                avatar.editorTimeGeneration = false;
                avatar.showPlaceholder = false;
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                if (avatar.predefinedDNA == null)
                {
                    avatar.predefinedDNA = new UMAPredefinedDNA();
                }
                avatar.GenerateNow();
                avatar.enabled = false;
                if (avatar.umaData == null || avatar.umaData.umaRecipe == null || avatar.umaData.umaRecipe.slotDataList == null)
                {
                    messages?.Add($"A character of '{race.raceName}' could not be generated.");
                    return result;
                }

                foreach (SlotData slot in avatar.umaData.umaRecipe.slotDataList)
                {
                    if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                    {
                        continue;
                    }
                    SlotDataAsset built = slot.asset;
                    string baseName = built.isBaked ? built.sourceSlot : built.slotName;
                    SlotDataAsset baseSlot = UMAAssetIndexer.Instance != null ? UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(baseName) : null;
                    if (baseSlot == null)
                    {
                        messages?.Add($"Body slot '{baseName}' is not in the UMA Global Library; no hide can name it.");
                        continue;
                    }
                    if (!UMAMeshHideOcclusion.TryBakeToAvatar(built.meshData, rig, out Vector3[] positions, out Vector3[] normals))
                    {
                        continue;
                    }
                    int submeshCount = built.meshData.submeshes != null ? built.meshData.submeshes.Length : 0;
                    var triangles = new int[submeshCount][];
                    for (int s = 0; s < submeshCount; s++)
                    {
                        triangles[s] = built.meshData.submeshes[s] != null ? built.meshData.submeshes[s].getManagedTriangles(0) : null;
                    }
                    var shapeNames = new List<string>();
                    if (built.meshData.blendShapes != null)
                    {
                        foreach (UMABlendShape shape in built.meshData.blendShapes)
                        {
                            if (shape != null && !string.IsNullOrEmpty(shape.shapeName))
                            {
                                shapeNames.Add(shape.shapeName);
                            }
                        }
                    }
                    result.Add(new BodySlot
                    {
                        BaseSlot = baseSlot, BuiltSlotName = built.slotName, Positions = positions, Normals = normals, Triangles = triangles,
                        BuiltBlendShapeNames = shapeNames.ToArray(),
                    });
                }
            }
            finally
            {
                if (avatar != null)
                {
                    try { avatar.CleanupGeneratedData(); } catch (Exception) { }
                }
                Object.DestroyImmediate(avatarObject);
            }
            return result;
        }

        /// <summary>
        /// The clothing a recipe adds, in avatar space, as the character will wear it: slots built for
        /// the race's legacy rig are converted with <paramref name="profile"/> first.
        /// </summary>
        /// <param name="bodySurface">The race's skin at rest, for profile effectors that clear the body. May be null.</param>
        public static UMAMeshHideOcclusion.OccluderSet CollectOccluders(UMATextRecipe recipe, RaceData race, UMARigRestPose rig,
            UMASlotRigConversionProfile profile, ICollection<SlotDataAsset> bodySlots, List<string> messages = null, UMARigBodySurface bodySurface = null)
        {
            var occluders = new UMAMeshHideOcclusion.OccluderSet();
            UMAData.UMARecipe cached = recipe != null ? recipe.GetCachedRecipe(true) : null;
            if (cached == null || cached.slotDataList == null)
            {
                return occluders;
            }
            UMARigRestPose legacyRig = race.legacyRig != null ? race.legacyRig.GetRestPose() : null;
            var options = new UMASlotRigConversionOptions { Profile = profile, BodySurface = bodySurface };
            foreach (SlotData slot in cached.slotDataList)
            {
                if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }
                if (bodySlots != null && bodySlots.Contains(slot.asset))
                {
                    continue;   // a recipe that carries body parts must not hide the body with itself
                }
                UMAMeshData mesh = slot.asset.meshData;
                if (legacyRig != null && UMALegacySlotConverter.IsBuiltForLegacyRig(mesh, legacyRig, rig))
                {
                    options.ProfileSlotName = slot.asset.slotName;
                    if (!UMASlotRigConverter.TryConvert(mesh, legacyRig, rig, options, null, out UMAMeshData converted, out string reason))
                    {
                        messages?.Add($"'{slot.asset.slotName}' could not be converted and was left out: {reason}");
                        continue;
                    }
                    mesh = converted;
                }
                if (!UMAMeshHideOcclusion.TryBakeToAvatar(mesh, rig, out Vector3[] positions, out _))
                {
                    messages?.Add($"'{slot.asset.slotName}' is not skinned with normals and was left out.");
                    continue;
                }
                if (mesh.submeshes == null)
                {
                    continue;
                }
                for (int s = 0; s < mesh.submeshes.Length; s++)
                {
                    if (mesh.submeshes[s] != null)
                    {
                        occluders.Add(positions, mesh.submeshes[s].getManagedTriangles(0));
                    }
                }
            }
            return occluders;
        }

        /// <summary>
        /// Creates or updates the Mesh Hide Assets for one recipe and adds them to it. Assets are saved in
        /// a "MeshHides" folder next to the recipe, one per body slot that has something to hide, and a
        /// second run updates them in place. The hides and the recipe are saved as they are changed.
        /// </summary>
        /// <param name="bodySlots">From <see cref="CollectBodySlots"/>; pass the same list for every recipe of a race.</param>
        public static RecipeResult Generate(UMATextRecipe recipe, RaceData race, UMARigRestPose rig, List<BodySlot> bodySlots,
            UMASlotRigConversionProfile profile, UMAMeshHideOcclusionSettings settings)
        {
            var result = new RecipeResult { Recipe = recipe };
            if (recipe == null || race == null || rig == null || bodySlots == null)
            {
                result.Messages.Add("A recipe, a race and its body slots are needed.");
                return result;
            }
            string recipePath = AssetDatabase.GetAssetPath(recipe);
            if (string.IsNullOrEmpty(recipePath))
            {
                result.Messages.Add($"'{recipe.name}' is not an asset in the project, so its hides have nowhere to be saved.");
                return result;
            }
            settings = settings ?? new UMAMeshHideOcclusionSettings();

            var bodyAssets = new HashSet<SlotDataAsset>();
            for (int i = 0; i < bodySlots.Count; i++)
            {
                bodyAssets.Add(bodySlots[i].BaseSlot);
            }
            UMARigBodySurface bodySurface = profile != null && profile.HasClearBodyEffectors ? BuildBodySurface(bodySlots) : null;
            UMAMeshHideOcclusion.OccluderSet occluders = CollectOccluders(recipe, race, rig, profile, bodyAssets, result.Messages, bodySurface);
            result.OccluderTriangles = occluders.TriangleCount;
            if (occluders.TriangleCount == 0)
            {
                result.Messages.Add($"'{recipe.name}' adds no clothing geometry.");
            }

            string folder = Path.GetDirectoryName(recipePath).Replace('\\', '/') + "/" + FolderName;
            var generated = new List<MeshHideAsset>();
            var emptied = new List<MeshHideAsset>();
            for (int b = 0; b < bodySlots.Count; b++)
            {
                BodySlot body = bodySlots[b];
                bool[] covered = UMAMeshHideOcclusion.FindCoveredVertices(body.Positions, body.Normals, occluders, settings);
                var flags = new BitArray[body.Triangles.Length];
                int hidden = 0;
                int total = 0;
                for (int s = 0; s < body.Triangles.Length; s++)
                {
                    flags[s] = UMAMeshHideOcclusion.FindHiddenTriangles(body.Triangles[s], covered, settings.strategy, out int hiddenInSubmesh);
                    hidden += hiddenInSubmesh;
                    total += flags[s].Count;
                }

                string path = GetHidePath(recipe, body.BaseSlot);
                var asset = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(path);
                if (asset != null && asset.autoGenerationLocked)
                {
                    // Finished by hand: not recomputed, not emptied, not taken off the recipe.
                    result.Slots.Add(new SlotResult
                    {
                        SlotName = body.BaseSlot.slotName, HiddenTriangles = asset.HiddenCount, TotalTriangles = total, Asset = asset, Locked = true,
                    });
                    continue;
                }
                int corrections = 0;
                if (asset != null && (asset.autoKeepVisible.Count > 0 || asset.autoForceHidden.Count > 0))
                {
                    // Painted corrections outlive every regeneration: applied after the computed pass.
                    asset.ApplyAutoCorrections(flags);
                    corrections = asset.autoKeepVisible.Count + asset.autoForceHidden.Count;
                    hidden = CountSet(flags);
                }
                if (hidden == 0 && asset == null)
                {
                    result.Slots.Add(new SlotResult { SlotName = body.BaseSlot.slotName, TotalTriangles = total });
                    continue;
                }
                bool isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<MeshHideAsset>();
                }
                // Naming the base slot is what lets the hide follow the slot into any race's baked copy.
                asset.asset = body.BaseSlot;
                asset.SaveSelectionForLOD(flags, 0);
                asset.UpdateEditorHashAndUVMaskFromFlags(body.BaseSlot);
                // The file always holds what the object holds. Unity can import a new asset's file again
                // before the next save, which reloads the object from it; a hide filled in after its
                // file was written came back empty when that happened.
                if (isNew)
                {
                    EnsureFolder(folder);
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
                (hidden > 0 ? generated : emptied).Add(asset);
                result.Slots.Add(new SlotResult { SlotName = body.BaseSlot.slotName, HiddenTriangles = hidden, TotalTriangles = total, Asset = asset, Corrections = corrections });
            }

            // Put the hides on the recipe; a hide this tool emptied on a later run comes off again.
            bool changed = false;
            if (recipe.MeshHideAssets == null)
            {
                recipe.MeshHideAssets = new List<MeshHideAsset>();
            }
            for (int i = 0; i < generated.Count; i++)
            {
                if (!recipe.MeshHideAssets.Contains(generated[i]) && !IsInACollection(recipe, generated[i]))
                {
                    if (!changed) Undo.RecordObject(recipe, "Add Mesh Hide Assets");
                    recipe.MeshHideAssets.Add(generated[i]);
                    changed = true;
                }
            }
            for (int i = 0; i < emptied.Count; i++)
            {
                // A hide that carries painted marks stays on the recipe even while it hides nothing.
                bool painted = emptied[i].autoKeepVisible.Count > 0 || emptied[i].autoForceHidden.Count > 0;
                if (!painted && recipe.MeshHideAssets.Contains(emptied[i]))
                {
                    if (!changed) Undo.RecordObject(recipe, "Remove Empty Mesh Hide Assets");
                    recipe.MeshHideAssets.Remove(emptied[i]);
                    changed = true;
                    result.Messages.Add($"'{emptied[i].name}' no longer hides anything and was taken off the recipe; the asset file was left in place.");
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(recipe);
                AssetDatabase.SaveAssetIfDirty(recipe);
            }
            return result;
        }

        /// <summary>Runs <see cref="Generate"/> for several recipes of one race and saves the assets.</summary>
        public static List<RecipeResult> GenerateAll(IList<UMATextRecipe> recipes, RaceData race, UMASlotRigConversionProfile profile,
            UMAMeshHideOcclusionSettings settings, List<string> messages = null)
        {
            var results = new List<RecipeResult>();
            if (race == null || recipes == null)
            {
                return results;
            }
            if (!UMARigRestPose.TryFromRace(race, out UMARigRestPose rig, out string reason))
            {
                messages?.Add(reason);
                return results;
            }
            List<BodySlot> bodySlots = CollectBodySlots(race, rig, messages);
            if (bodySlots.Count == 0)
            {
                messages?.Add($"'{race.raceName}' has no body slots to hide.");
                return results;
            }
            try
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    if (recipes[i] == null)
                    {
                        continue;
                    }
                    EditorUtility.DisplayProgressBar("Creating mesh hides", recipes[i].name, (float)i / recipes.Count);
                    results.Add(Generate(recipes[i], race, rig, bodySlots, profile, settings));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            return results;
        }

        /// <summary>The skin made of a race's body slots, for profile effectors that clear the body.</summary>
        public static UMARigBodySurface BuildBodySurface(List<BodySlot> bodySlots)
        {
            var surface = new UMARigBodySurface();
            if (bodySlots == null)
            {
                return surface;
            }
            for (int b = 0; b < bodySlots.Count; b++)
            {
                BodySlot body = bodySlots[b];
                for (int s = 0; s < body.Triangles.Length; s++)
                {
                    surface.Add(body.Positions, body.Normals, body.Triangles[s]);
                }
            }
            return surface;
        }

        /// <summary>Where the generator keeps a recipe's hide for one body slot.</summary>
        public static string GetHidePath(UMATextRecipe recipe, SlotDataAsset baseSlot)
        {
            string recipePath = recipe != null ? AssetDatabase.GetAssetPath(recipe) : null;
            if (string.IsNullOrEmpty(recipePath) || baseSlot == null)
            {
                return null;
            }
            string folder = Path.GetDirectoryName(recipePath).Replace('\\', '/') + "/" + FolderName;
            return folder + "/" + SafeFileName(recipe.name + "_" + baseSlot.slotName) + ".asset";
        }

        /// <summary>
        /// The recipe's generated hide for a body slot, created empty and added to the recipe when it
        /// does not exist yet. Used when a triangle is painted hidden on a slot the generator found
        /// nothing to hide on. Returns null when the recipe is not an asset.
        /// </summary>
        /// <param name="triangleCounts">Triangles in each submesh of the slot, to size a new hide.</param>
        public static MeshHideAsset GetOrCreateHide(UMATextRecipe recipe, SlotDataAsset baseSlot, int[] triangleCounts)
        {
            string path = GetHidePath(recipe, baseSlot);
            if (path == null || triangleCounts == null)
            {
                return null;
            }
            var asset = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MeshHideAsset>();
                asset.asset = baseSlot;
                var flags = new BitArray[triangleCounts.Length];
                for (int s = 0; s < flags.Length; s++)
                {
                    flags[s] = new BitArray(Mathf.Max(0, triangleCounts[s]));
                }
                asset.SaveSelectionForLOD(flags, 0);
                asset.UpdateEditorHashAndUVMaskFromFlags(baseSlot);
                // Written complete, like the generator's own hides.
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(asset, path);
            }
            if (recipe.MeshHideAssets == null)
            {
                recipe.MeshHideAssets = new List<MeshHideAsset>();
            }
            if (!recipe.MeshHideAssets.Contains(asset) && !IsInACollection(recipe, asset))
            {
                Undo.RecordObject(recipe, "Add Mesh Hide Asset");
                recipe.MeshHideAssets.Add(asset);
                EditorUtility.SetDirty(recipe);
                AssetDatabase.SaveAssetIfDirty(recipe);
            }
            return asset;
        }

        private static int CountSet(BitArray[] flags)
        {
            int count = 0;
            for (int s = 0; s < flags.Length; s++)
            {
                if (flags[s] == null)
                {
                    continue;
                }
                for (int i = 0; i < flags[s].Count; i++)
                {
                    if (flags[s][i]) count++;
                }
            }
            return count;
        }

        private static bool IsInACollection(UMATextRecipe recipe, MeshHideAsset asset)
        {
            if (recipe.MeshHideAssetCollections == null)
            {
                return false;
            }
            foreach (MeshHideAssetCollection collection in recipe.MeshHideAssetCollections)
            {
                if (collection == null || collection.Assets == null)
                {
                    continue;
                }
                for (int i = 0; i < collection.Assets.Count; i++)
                {
                    if (collection.Assets[i] == asset)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string SafeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
