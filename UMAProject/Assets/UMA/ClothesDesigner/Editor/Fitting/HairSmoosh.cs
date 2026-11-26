using System;
using System.Collections.Generic;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Hats and hoods that hold the hair down: UMA squashes ("smooshes") hair under a hat at build time
    /// (<c>DynamicCharacterAvatar.SmooshSlotPhysics</c>) when the recipe has a clipping-plane slot, the hat's slot is
    /// tagged <see cref="TargetTag"/> and the hair's slot is smooshable and tagged <see cref="SmooshableTag"/>. Hair on
    /// the inside of the plane is moved towards the plane's middle until it is inside the hat.
    /// <para>
    /// The bake publishes the plane as a four-point slot fitted to the openings of the garment's head pieces (a beanie's
    /// brim, a hood's face), turned to face into the garment, and tags the garment's slot. See
    /// Plans/RealtimeHoodSmooshPlan.md for doing this every frame, for hoods that move as cloth.
    /// </para>
    /// </summary>
    public static class HairSmoosh
    {
        public const string TargetTag = "Smooshtarget";
        public const string SmooshableTag = "Smooshable";
        /// <summary>How far past the openings the plane reaches (m).</summary>
        public const float PlaneMargin = 0.02f;

        /// <summary>True when any piece is laid round the head (caps, hoods, coifs).</summary>
        public static bool HasHeadPieces(ClothesPattern pattern)
        {
            if (pattern == null) return false;
            foreach (PatternPiece piece in pattern.pieces) if (piece != null && piece.placement != null && piece.placement.part == BodyPart.Head) return true;
            return false;
        }

        /// <summary>
        /// The plane through the openings of the head pieces (renderer space): its middle, the normal into the garment,
        /// and two half-extents along it. False when the head pieces have no opening.
        /// </summary>
        public static bool FitPlane(ClothesPattern pattern, PatternMesh mesh, Vector3[] positions, out Vector3 centre, out Vector3 inward, out Vector3 u, out Vector3 v)
        {
            centre = inward = u = v = Vector3.zero;
            var onOpening = new List<Vector3>();
            var head = new List<Vector3>();
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int p = mesh.piece[i];
                if (p < pattern.pieces.Count && pattern.pieces[p].placement.part == BodyPart.Head) head.Add(positions[i]);
            }
            foreach (Vector3Int segment in mesh.outlineSegments)
            {
                int p = mesh.piece[segment.x];
                if (p >= pattern.pieces.Count || pattern.pieces[p].placement.part != BodyPart.Head) continue;
                PatternPiece piece = pattern.pieces[p];
                if (segment.z < 0 || segment.z >= piece.edges.Count || !piece.edges[segment.z].opening) continue;
                onOpening.Add(positions[segment.x]);
                onOpening.Add(positions[segment.y]);
            }
            if (onOpening.Count < 3 || head.Count == 0) return false;
            foreach (Vector3 q in onOpening) centre += q;
            centre /= onOpening.Count;
            // The plane's normal: the direction the openings spread least in (power iteration on the inverse is
            // overkill; the smallest of the covariance's eigenvectors by Jacobi sweeps).
            var c = new double[3, 3];
            foreach (Vector3 q in onOpening)
            {
                Vector3 d = q - centre;
                for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) c[a, b] += d[a] * d[b];
            }
            Eigen(c, out double[] values, out Vector3[] vectors);
            int smallest = 0, largest = 0;
            for (int k = 1; k < 3; k++) { if (values[k] < values[smallest]) smallest = k; if (values[k] > values[largest]) largest = k; }
            Vector3 normal = vectors[smallest].normalized;
            Vector3 middle = Vector3.zero;
            foreach (Vector3 q in head) middle += q;
            middle /= head.Count;
            if (Vector3.Dot(middle - centre, normal) < 0f) normal = -normal;
            inward = normal;
            Vector3 axis = Vector3.ProjectOnPlane(vectors[largest], normal).normalized;
            Vector3 cross = Vector3.Cross(axis, normal);
            float extentU = 0f, extentV = 0f;
            foreach (Vector3 q in onOpening)
            {
                extentU = Mathf.Max(extentU, Mathf.Abs(Vector3.Dot(q - centre, axis)));
                extentV = Mathf.Max(extentV, Mathf.Abs(Vector3.Dot(q - centre, cross)));
            }
            u = axis * (extentU + PlaneMargin);
            v = cross * (extentV + PlaneMargin);
            return true;
        }

        /// <summary>
        /// The plane's four corners, ordered so UMA's smoosh (which takes the plane through corners 0, 2 and 1) has its
        /// normal into the garment: with no flipping in the recipe, hair on that side is moved in.
        /// </summary>
        public static Vector3[] PlaneCorners(Vector3 centre, Vector3 inward, Vector3 u, Vector3 v)
        {
            // Plane(a, c, b)'s normal is cross(c - a, b - a); with these corners that is -cross(u, v), so v runs as
            // cross(u, inward) to make it point inward.
            Vector3 side = Vector3.Cross(u.normalized, inward.normalized) * v.magnitude;
            return new[] { centre - u - side, centre + u - side, centre + u + side, centre - u + side };
        }

        /// <summary>
        /// Publishes the plane slot (<c>slot_SmooshPlane</c>) beside the garment's, weighted to the head, marked as a
        /// clipping plane, and tags the garment's slot as the target. Null, with a message, when there is no plane to fit.
        /// </summary>
        public static SlotDataAsset Publish(FitSession session, SlotDataAsset garment, PublishSettings settings, List<string> messages)
        {
            Vector3[] positions = session.SyncPositions();
            if (!FitPlane(session.Pattern, session.Mesh, positions, out Vector3 centre, out Vector3 inward, out Vector3 u, out Vector3 v))
            {
                messages.Add("Holds Hair Down: the head pieces have no opening to put the plane in; no plane made.");
                return null;
            }
            BodyReference body = session.Body;
            Vector3[] corners = PlaneCorners(centre, inward, u, v);
            int head = Array.FindIndex(body.Skin.Bones, b => b != null && b.name == "Head");
            if (head < 0) head = body.FallbackBone;
            var weights = new UMASlotSkinTransfer.WeightTransferResult
            {
                BonesPerVertex = new byte[] { 1, 1, 1, 1 },
                BoneWeights = new[] { Bone(head), Bone(head), Bone(head), Bone(head) }
            };
            string name = UMASlotSkinTransfer.MakeSafeAssetName(settings.slotName) + "_SmooshPlane";
            Mesh mesh = null;
            GameObject temporary = null;
            try
            {
                var source = new UMASlotSkinTransfer.SourceGeometry { TargetLocalVertices = corners, Triangles = new[] { 0, 1, 2, 0, 2, 3 } };
                mesh = UMASlotSkinTransfer.BuildSlotMesh(source, body.Skin, weights, new UMASlotSkinTransfer.SlotMeshOptions { SlotName = name, RecalculateMissingNormals = true });
                mesh.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                mesh.RecalculateTangents();
                temporary = UMASlotSkinTransfer.CreateTemporaryRenderer(body.Skin.Renderer);
                var renderer = temporary.GetComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = body.Skin.Bones;
                renderer.rootBone = body.Skin.RootBone;
                var request = new UMASlotPublisher.Request
                {
                    SlotName = name,
                    FolderPath = settings.folder,
                    AddToGlobalLibrary = settings.addToGlobalLibrary,
                    UpdateInPlace = true,
                    OverwriteExisting = false,
                    CreateWardrobeRecipe = false,
                    Race = session.Fit.race != null ? session.Fit.race : body.Skin.RaceData,
                    WardrobeSlot = settings.wardrobeSlot,
                    OverlayMaterial = settings.material
                };
                UMASlotPublisher.Result published = UMASlotPublisher.Publish(request, renderer, body.Skin);
                SlotDataAsset plane = published.Slot;
                if (plane == null) { messages.Add("Holds Hair Down: the plane slot could not be published."); return null; }
                plane.isClippingPlane = true;
                plane.clippingPlaneOffset = new Vector3[4];
                EditorUtility.SetDirty(plane);
                AssetDatabase.SaveAssetIfDirty(plane);
                GarmentBaker.KeepOutOfLibrary(plane, !settings.addToGlobalLibrary);
                // The plane needs no overlay of its own.
                if (published.Overlay != null) GarmentBaker.KeepOutOfLibrary(published.Overlay, !settings.addToGlobalLibrary);
                Tag(garment, TargetTag);
                messages.Add("Holds Hair Down: '" + name + "', a plane across the head pieces' openings; the slot is tagged '" + TargetTag + "'. Hair slots must be smooshable and tagged '" + SmooshableTag + "' to be held down.");
                return plane;
            }
            finally
            {
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static BoneWeight1 Bone(int index) => new BoneWeight1 { boneIndex = index, weight = 1f };

        /// <summary>Adds a tag to a slot asset (once) and saves it.</summary>
        public static void Tag(SlotDataAsset slot, string tag)
        {
            if (slot == null) return;
            var tags = new List<string>(slot.tags ?? new string[0]);
            if (tags.Contains(tag)) return;
            tags.Add(tag);
            slot.tags = tags.ToArray();
            EditorUtility.SetDirty(slot);
            AssetDatabase.SaveAssetIfDirty(slot);
        }

        /// <summary>The recipe entry for the plane: the plane slot with no flipping, so its own normal decides the side.</summary>
        public static SlotData PlaneSlotData(SlotDataAsset plane, RaceData race)
        {
            var data = new SlotData(plane)
            {
                smooshInvertX = false,
                smooshInvertY = false,
                smooshInvertZ = false,
                smooshInvertDist = false,
                smooshTargetTag = TargetTag,
                smooshableTag = SmooshableTag
            };
            if (race != null && !string.IsNullOrWhiteSpace(race.raceName)) data.Races = new[] { race.raceName };
            return data;
        }

        // ---- Hair ----------------------------------------------------------------------------------------

        /// <summary>Hair slots in the Global Library (by name) that UMA will not smoosh: not smooshable, or untagged.</summary>
        public static List<SlotDataAsset> HairNotSmooshable()
        {
            var list = new List<SlotDataAsset>();
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null) return list;
            foreach (SlotDataAsset slot in indexer.GetAllAssets<SlotDataAsset>())
            {
                if (slot == null || string.IsNullOrEmpty(slot.slotName)) continue;
                if (slot.slotName.IndexOf("hair", StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool tagged = slot.tags != null && Array.IndexOf(slot.tags, SmooshableTag) >= 0;
                if (!slot.isSmooshable || !tagged) list.Add(slot);
            }
            return list;
        }

        /// <summary>Makes hair slots smooshable and tags them, saving only those slots.</summary>
        public static int MakeSmooshable(IEnumerable<SlotDataAsset> slots)
        {
            int changed = 0;
            foreach (SlotDataAsset slot in slots)
            {
                if (slot == null) continue;
                slot.isSmooshable = true;
                EditorUtility.SetDirty(slot);
                AssetDatabase.SaveAssetIfDirty(slot);
                Tag(slot, SmooshableTag);
                changed++;
            }
            return changed;
        }

        /// <summary>Eigenvalues and eigenvectors of a symmetric 3x3 matrix (Jacobi rotations).</summary>
        private static void Eigen(double[,] a, out double[] values, out Vector3[] vectors)
        {
            var m = (double[,])a.Clone();
            var e = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 32; sweep++)
            {
                for (int p = 0; p < 2; p++)
                {
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(m[p, q]) < 1e-15) continue;
                        double theta = (m[q, q] - m[p, p]) / (2.0 * m[p, q]);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        if (theta == 0.0) t = 1.0;
                        double cs = 1.0 / Math.Sqrt(t * t + 1.0), sn = t * cs;
                        for (int k = 0; k < 3; k++)
                        {
                            double mkp = m[k, p], mkq = m[k, q];
                            m[k, p] = cs * mkp - sn * mkq; m[k, q] = sn * mkp + cs * mkq;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double mpk = m[p, k], mqk = m[q, k];
                            m[p, k] = cs * mpk - sn * mqk; m[q, k] = sn * mpk + cs * mqk;
                        }
                        for (int k = 0; k < 3; k++)
                        {
                            double ekp = e[k, p], ekq = e[k, q];
                            e[k, p] = cs * ekp - sn * ekq; e[k, q] = sn * ekp + cs * ekq;
                        }
                    }
                }
            }
            values = new[] { m[0, 0], m[1, 1], m[2, 2] };
            vectors = new Vector3[3];
            for (int k = 0; k < 3; k++) vectors[k] = new Vector3((float)e[0, k], (float)e[1, k], (float)e[2, k]);
        }
    }
}
