using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    /// <summary>
    /// The Rig Profile Editor's view of a preview character's mesh hides: which body triangles the
    /// worn recipes hide, drawn as a red overlay, and a small brush that corrects them.
    /// </summary>
    /// <remarks>
    /// A correction changes the Mesh Hide Asset and is also recorded in it as a "never hide" or
    /// "always hide" mark, which the automatic generator applies after its own pass. So a painted
    /// triangle stays as painted when the hides are generated again. Heavier editing belongs in the
    /// Face Editor; lock the hide afterwards so the generator leaves it alone.
    /// </remarks>
    public sealed class UMAMeshHidePainter : IDisposable
    {
        /// <summary>One Mesh Hide Asset a worn recipe applies to the body.</summary>
        public sealed class WornHide
        {
            public MeshHideAsset Asset;
            public UMATextRecipe Recipe;
            /// <summary>The hide reaches the recipe through a Mesh Hide Asset Collection rather than directly.</summary>
            public bool FromCollection;
        }

        /// <summary>One body slot of the preview character, where Unity draws it now.</summary>
        public sealed class BodyPart
        {
            /// <summary>The slot the character is built from; for a race with baked blend shapes, the baked copy.</summary>
            public SlotDataAsset BuiltSlot;
            /// <summary>The slot asset in the project that hides name. Null when it is not in the UMA Global Library.</summary>
            public SlotDataAsset BaseSlot;
            public string BaseSlotName;
            /// <summary>Avatar space, in the pose the preview is in.</summary>
            public Vector3[] Positions;
            public Vector3[] Normals;
            /// <summary>Base-LOD triangles of each submesh.</summary>
            public int[][] Triangles;
            /// <summary>Per submesh, per triangle: hidden by any worn recipe.</summary>
            public BitArray[] Hidden;
            public readonly List<WornHide> Hides = new List<WornHide>();
            internal int VertexStart;
            /// <summary>
            /// The triangles wind the other way in avatar space than their normals say, as they do on a
            /// rig whose root is mirrored. Unity reverses culling for such a character, and so does this.
            /// </summary>
            internal bool Flipped;

            public int[] TriangleCounts()
            {
                var counts = new int[Triangles.Length];
                for (int s = 0; s < counts.Length; s++)
                {
                    counts[s] = Triangles[s] != null ? Triangles[s].Length / 3 : 0;
                }
                return counts;
            }
        }

        public struct Hit
        {
            public BodyPart Part;
            public int Submesh;
            public int Triangle;
            public Vector3 Point;
            public float Distance;
        }

        private readonly List<BodyPart> parts = new List<BodyPart>();
        private readonly List<WornHide> hides = new List<WornHide>();
        private readonly HashSet<MeshHideAsset> strokeAssets = new HashSet<MeshHideAsset>();
        private Mesh overlayMesh;
        private Material material;
        private bool overlayDirty = true;

        public IReadOnlyList<BodyPart> Parts => parts;

        /// <summary>Every hide the worn recipes apply to the body, once each.</summary>
        public IReadOnlyList<WornHide> Hides => hides;

        /// <summary>Body triangles hidden by the worn recipes together.</summary>
        public int HiddenTriangles { get; private set; }

        public int TotalTriangles { get; private set; }

        /// <summary>Notes from the last <see cref="Rebuild"/>, for the editor to show.</summary>
        public readonly List<string> Messages = new List<string>();

        /// <summary>
        /// Reads the preview character's body where it stands now and the hides its recipes apply.
        /// Call after the preview is built, refreshed or posed. Returns false when there is no body
        /// to show.
        /// </summary>
        public bool Rebuild(UMARigProfilePreview preview)
        {
            parts.Clear();
            Messages.Clear();
            overlayDirty = true;
            HiddenTriangles = 0;
            TotalTriangles = 0;
            if (preview == null || !preview.IsBuilt || preview.Avatar.umaData.umaRecipe == null || preview.Avatar.umaData.umaRecipe.slotDataList == null)
            {
                hides.Clear();
                return false;
            }

            UMAData umaData = preview.Avatar.umaData;
            Matrix4x4 worldToAvatar = preview.AvatarObject.transform.worldToLocalMatrix;
            int vertexStart = 0;
            foreach (SlotData slot in umaData.umaRecipe.slotDataList)
            {
                if (slot == null || slot.asset == null || preview.IsPreviewSlot(slot) || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }
                SlotDataAsset built = slot.asset;
                if (!UMAMeshHideOcclusion.TryBakeWithSkeleton(built.meshData, umaData.skeleton, worldToAvatar, out Vector3[] positions, out Vector3[] normals))
                {
                    continue;
                }
                string baseName = built.isBaked ? built.sourceSlot : built.slotName;
                int submeshCount = built.meshData.submeshes != null ? built.meshData.submeshes.Length : 0;
                var part = new BodyPart
                {
                    BuiltSlot = built,
                    BaseSlotName = baseName,
                    BaseSlot = UMAAssetIndexer.Instance != null ? UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(baseName) : null,
                    Positions = positions,
                    Normals = normals,
                    Triangles = new int[submeshCount][],
                    Hidden = new BitArray[submeshCount],
                    VertexStart = vertexStart,
                };
                for (int s = 0; s < submeshCount; s++)
                {
                    part.Triangles[s] = built.meshData.submeshes[s] != null ? built.meshData.submeshes[s].getManagedTriangles(0) : new int[0];
                    part.Hidden[s] = new BitArray(part.Triangles[s].Length / 3);
                    TotalTriangles += part.Triangles[s].Length / 3;
                }
                part.Flipped = WindsAgainstNormals(part);
                vertexStart += positions.Length;
                parts.Add(part);
            }
            ReloadHides(preview);
            return parts.Count > 0;
        }

        /// <summary>
        /// Reads the worn recipes' hides again without touching the geometry. Call after the hides were
        /// generated, pasted, or edited elsewhere.
        /// </summary>
        public void ReloadHides(UMARigProfilePreview preview)
        {
            hides.Clear();
            for (int p = 0; p < parts.Count; p++)
            {
                parts[p].Hides.Clear();
            }
            if (preview != null && preview.IsBuilt)
            {
                SlotData[] slotDataList = preview.Avatar.umaData.umaRecipe.slotDataList;
                IReadOnlyList<UMATextRecipe> recipes = preview.WornRecipes;
                for (int r = 0; r < recipes.Count; r++)
                {
                    UMATextRecipe recipe = recipes[r];
                    if (recipe == null)
                    {
                        continue;
                    }
                    if (recipe.MeshHideAssets != null)
                    {
                        for (int i = 0; i < recipe.MeshHideAssets.Count; i++)
                        {
                            AddHide(recipe.MeshHideAssets[i], recipe, false, slotDataList);
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
                                AddHide(collection.Assets[i], recipe, true, slotDataList);
                            }
                        }
                    }
                }
            }
            RecomputeHidden();
        }

        private void AddHide(MeshHideAsset hide, UMATextRecipe recipe, bool fromCollection, SlotData[] slotDataList)
        {
            if (hide == null)
            {
                return;
            }
            SlotData slot = hide.FindMatchingSlot(slotDataList);
            if (slot == null || slot.asset == null)
            {
                return;
            }
            BodyPart part = null;
            for (int p = 0; p < parts.Count; p++)
            {
                if (parts[p].BuiltSlot == slot.asset)
                {
                    part = parts[p];
                    break;
                }
            }
            if (part == null || !hide.IsCompatibleWithSlot(slot.asset, out _))
            {
                return;
            }
            for (int i = 0; i < part.Hides.Count; i++)
            {
                if (part.Hides[i].Asset == hide)
                {
                    return;   // two recipes can share a hide; the first one owns it here
                }
            }
            var worn = new WornHide { Asset = hide, Recipe = recipe, FromCollection = fromCollection };
            part.Hides.Add(worn);
            hides.Add(worn);
        }

        /// <summary>Works the union of the hides out again after their flags changed.</summary>
        public void RecomputeHidden()
        {
            int hidden = 0;
            for (int p = 0; p < parts.Count; p++)
            {
                BodyPart part = parts[p];
                for (int s = 0; s < part.Hidden.Length; s++)
                {
                    BitArray union = part.Hidden[s];
                    union.SetAll(false);
                    for (int h = 0; h < part.Hides.Count; h++)
                    {
                        BitArray[] flags = part.Hides[h].Asset != null ? part.Hides[h].Asset.triangleFlags : null;
                        if (flags != null && s < flags.Length && flags[s] != null && flags[s].Count == union.Count)
                        {
                            union.Or(flags[s]);
                        }
                    }
                    for (int t = 0; t < union.Count; t++)
                    {
                        if (union[t]) hidden++;
                    }
                }
            }
            HiddenTriangles = hidden;
            overlayDirty = true;
        }

        // --- picking and painting ------------------------------------------------------------------

        /// <summary>The nearest body triangle a ray meets, hidden or not. The ray is in avatar space.</summary>
        public bool Raycast(Ray ray, out Hit hit)
        {
            hit = default;
            float nearest = float.MaxValue;
            for (int p = 0; p < parts.Count; p++)
            {
                BodyPart part = parts[p];
                for (int s = 0; s < part.Triangles.Length; s++)
                {
                    int[] triangles = part.Triangles[s];
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        Vector3 a = part.Positions[triangles[t]];
                        Vector3 b = part.Positions[triangles[t + 1]];
                        Vector3 c = part.Positions[triangles[t + 2]];
                        if (RayMeetsTriangle(ray, a, b, c, out float distance) && distance < nearest)
                        {
                            nearest = distance;
                            hit = new Hit { Part = part, Submesh = s, Triangle = t / 3, Distance = distance, Point = ray.origin + ray.direction * distance };
                        }
                    }
                }
            }
            return nearest < float.MaxValue;
        }

        /// <summary>
        /// Paints with a round brush centred where the ray meets the body. Un-hiding clears the
        /// triangles under the brush in every worn hide that hides them, and marks them "never hide"
        /// there. Hiding sets them in <paramref name="hideTarget"/>'s generated hide for the slot
        /// (created when there is none) and marks them "always hide". Only triangles facing the ray
        /// are painted, so the brush does not reach the far side of a thin part. Locked hides are not
        /// changed. Returns how many triangles changed.
        /// </summary>
        /// <param name="ray">In avatar space.</param>
        /// <param name="radius">Brush radius in metres. Zero paints the one triangle under the cursor.</param>
        /// <param name="hideTarget">The recipe whose hide takes painted-hidden triangles. Not needed to un-hide.</param>
        public int Paint(Ray ray, float radius, bool hide, UMATextRecipe hideTarget)
        {
            if (!Raycast(ray, out Hit hit))
            {
                return 0;
            }
            int changed = 0;
            float radiusSquared = radius * radius;
            for (int p = 0; p < parts.Count; p++)
            {
                BodyPart part = parts[p];
                for (int s = 0; s < part.Triangles.Length; s++)
                {
                    int[] triangles = part.Triangles[s];
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        int triangle = t / 3;
                        bool underCursor = part == hit.Part && s == hit.Submesh && triangle == hit.Triangle;
                        if (!underCursor)
                        {
                            if (radius <= 0f)
                            {
                                continue;
                            }
                            Vector3 a = part.Positions[triangles[t]];
                            Vector3 b = part.Positions[triangles[t + 1]];
                            Vector3 c = part.Positions[triangles[t + 2]];
                            Vector3 centre = (a + b + c) / 3f;
                            if ((centre - hit.Point).sqrMagnitude > radiusSquared)
                            {
                                continue;
                            }
                            float facing = Vector3.Dot(Vector3.Cross(b - a, c - a), ray.direction);
                            if (part.Flipped ? facing <= 0f : facing >= 0f)
                            {
                                continue;   // faces away from the viewer
                            }
                        }
                        if (part.Hidden[s][triangle] == hide)
                        {
                            continue;
                        }
                        if (hide ? PaintHidden(part, s, triangle, hideTarget) : PaintVisible(part, s, triangle))
                        {
                            changed++;
                        }
                    }
                }
            }
            if (changed > 0)
            {
                RecomputeHidden();
            }
            return changed;
        }

        private bool PaintVisible(BodyPart part, int submesh, int triangle)
        {
            bool changed = false;
            bool blocked = false;
            for (int h = 0; h < part.Hides.Count; h++)
            {
                MeshHideAsset asset = part.Hides[h].Asset;
                BitArray[] flags = asset != null ? asset.triangleFlags : null;
                if (flags == null || submesh >= flags.Length || flags[submesh] == null || triangle >= flags[submesh].Count || !flags[submesh][triangle])
                {
                    continue;
                }
                if (asset.autoGenerationLocked)
                {
                    blocked = true;
                    continue;
                }
                RecordForStroke(asset);
                if (asset.SetAutoCorrection(submesh, triangle, false))
                {
                    EditorUtility.SetDirty(asset);
                    changed = true;
                }
            }
            // While a locked hide still hides it, nothing the viewer can see has changed.
            return changed && !blocked;
        }

        /// <summary>One undo step per hide per stroke, taken before the stroke first changes it.</summary>
        private void RecordForStroke(MeshHideAsset asset)
        {
            if (strokeAssets.Add(asset))
            {
                Undo.RecordObject(asset, "Paint Mesh Hide");
            }
        }

        private bool PaintHidden(BodyPart part, int submesh, int triangle, UMATextRecipe hideTarget)
        {
            if (hideTarget == null || part.BaseSlot == null)
            {
                return false;
            }
            MeshHideAsset asset = null;
            for (int h = 0; h < part.Hides.Count; h++)
            {
                // Prefer the recipe's own hide for this slot, wherever it is kept.
                if (part.Hides[h].Recipe == hideTarget && !part.Hides[h].FromCollection && part.Hides[h].Asset != null && !part.Hides[h].Asset.autoGenerationLocked)
                {
                    asset = part.Hides[h].Asset;
                    break;
                }
            }
            if (asset == null)
            {
                asset = UMAMeshHideAutoGenerator.GetOrCreateHide(hideTarget, part.BaseSlot, part.TriangleCounts());
                if (asset == null || asset.autoGenerationLocked || !asset.IsCompatibleWithSlot(part.BuiltSlot, out _))
                {
                    return false;
                }
                bool known = false;
                for (int h = 0; h < part.Hides.Count; h++)
                {
                    known |= part.Hides[h].Asset == asset;
                }
                if (!known)
                {
                    var worn = new WornHide { Asset = asset, Recipe = hideTarget };
                    part.Hides.Add(worn);
                    hides.Add(worn);
                }
            }
            RecordForStroke(asset);
            if (!asset.SetAutoCorrection(submesh, triangle, true))
            {
                return false;
            }
            EditorUtility.SetDirty(asset);
            return true;
        }

        /// <summary>
        /// Ends a brush stroke: brings each painted hide's stored hash and UV mask up to date with its
        /// flags, as the generator does, so the hide survives a change to the slot's mesh, and saves
        /// the hide. Returns how many hides the stroke changed. Everything painted between two calls
        /// undoes as one step.
        /// </summary>
        public int CommitStroke()
        {
            int count = 0;
            foreach (MeshHideAsset asset in strokeAssets)
            {
                if (asset != null)
                {
                    asset.UpdateEditorHashAndUVMaskFromFlags(FindBaseSlot(asset));
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                    count++;
                }
            }
            strokeAssets.Clear();
            return count;
        }

        private SlotDataAsset FindBaseSlot(MeshHideAsset asset)
        {
            for (int p = 0; p < parts.Count; p++)
            {
                for (int h = 0; h < parts[p].Hides.Count; h++)
                {
                    if (parts[p].Hides[h].Asset == asset)
                    {
                        return parts[p].BaseSlot != null ? parts[p].BaseSlot : parts[p].BuiltSlot;
                    }
                }
            }
            return null;
        }

        // --- drawing -------------------------------------------------------------------------------

        /// <summary>
        /// Draws the hidden triangles. Call from a Scene view callback during Repaint.
        /// </summary>
        /// <param name="seeThrough">Draw over everything, so hidden triangles show through the clothing that covers them.</param>
        public void Draw(Matrix4x4 avatarToWorld, Color color, bool seeThrough)
        {
            if (parts.Count == 0 || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            EnsureOverlay();
            if (overlayMesh == null || material == null || HiddenTriangles == 0)
            {
                return;
            }
            material.SetColor("_Color", color);
            material.SetInt("_ZTest", (int)(seeThrough ? CompareFunction.Always : CompareFunction.LessEqual));
            material.SetPass(0);
            Graphics.DrawMeshNow(overlayMesh, avatarToWorld);
        }

        /// <summary>The overlay as a mesh in avatar space: every body vertex, and the hidden triangles only.</summary>
        public Mesh GetOverlayMesh()
        {
            EnsureOverlay();
            return overlayMesh;
        }

        private void EnsureOverlay()
        {
            if (material == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    return;
                }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull", (int)CullMode.Back);
                material.SetInt("_ZWrite", 0);
                // Pulled a little toward the camera, so it wins against the body it lies on.
                material.SetFloat("_ZBias", -1f);
            }
            if (!overlayDirty && overlayMesh != null)
            {
                return;
            }
            overlayDirty = false;
            if (overlayMesh == null)
            {
                overlayMesh = new Mesh { name = "Mesh Hide Overlay", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                overlayMesh.MarkDynamic();
            }

            int vertexCount = 0;
            for (int p = 0; p < parts.Count; p++)
            {
                vertexCount += parts[p].Positions.Length;
            }
            var vertices = new Vector3[vertexCount];
            var indices = new List<int>(HiddenTriangles * 3);
            for (int p = 0; p < parts.Count; p++)
            {
                BodyPart part = parts[p];
                Array.Copy(part.Positions, 0, vertices, part.VertexStart, part.Positions.Length);
                for (int s = 0; s < part.Triangles.Length; s++)
                {
                    int[] triangles = part.Triangles[s];
                    BitArray hidden = part.Hidden[s];
                    int second = part.Flipped ? 2 : 1;
                    int third = part.Flipped ? 1 : 2;
                    for (int t = 0; t < hidden.Count; t++)
                    {
                        if (!hidden[t])
                        {
                            continue;
                        }
                        indices.Add(part.VertexStart + triangles[t * 3]);
                        indices.Add(part.VertexStart + triangles[t * 3 + second]);
                        indices.Add(part.VertexStart + triangles[t * 3 + third]);
                    }
                }
            }
            overlayMesh.Clear();
            overlayMesh.vertices = vertices;
            overlayMesh.SetTriangles(indices, 0, true);
        }

        /// <summary>True when most triangles' winding disagrees with their vertex normals.</summary>
        private static bool WindsAgainstNormals(BodyPart part)
        {
            int agree = 0;
            int disagree = 0;
            for (int s = 0; s < part.Triangles.Length; s++)
            {
                int[] triangles = part.Triangles[s];
                // A sample is enough: a mesh winds one way throughout.
                int step = Mathf.Max(3, (triangles.Length / 3 / 500) * 3);
                for (int t = 0; t + 2 < triangles.Length; t += step)
                {
                    Vector3 a = part.Positions[triangles[t]];
                    Vector3 b = part.Positions[triangles[t + 1]];
                    Vector3 c = part.Positions[triangles[t + 2]];
                    Vector3 normal = part.Normals[triangles[t]] + part.Normals[triangles[t + 1]] + part.Normals[triangles[t + 2]];
                    float dot = Vector3.Dot(Vector3.Cross(b - a, c - a), normal);
                    if (dot > 0f) agree++;
                    else if (dot < 0f) disagree++;
                }
            }
            return disagree > agree;
        }

        public void Dispose()
        {
            parts.Clear();
            hides.Clear();
            strokeAssets.Clear();
            HiddenTriangles = 0;
            TotalTriangles = 0;
            if (overlayMesh != null)
            {
                Object.DestroyImmediate(overlayMesh);
                overlayMesh = null;
            }
            if (material != null)
            {
                Object.DestroyImmediate(material);
                material = null;
            }
        }

        /// <summary>Möller–Trumbore, front or back face; <paramref name="distance"/> is along the ray.</summary>
        private static bool RayMeetsTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-12f)
            {
                return false;
            }
            float inverse = 1f / determinant;
            Vector3 s = ray.origin - a;
            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }
            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }
    }
}
