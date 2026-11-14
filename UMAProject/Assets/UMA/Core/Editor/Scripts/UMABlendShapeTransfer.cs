using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>How a target vertex follows the source surface it is bound to.</summary>
    public enum UMABlendShapeTransferMethod
    {
        /// <summary>
        /// The vertex keeps its place relative to the surface: it moves with the nearest point of the
        /// surface and its offset from that point turns with the surface. Cloth keeps its distance from
        /// the skin, also where the shape tilts the skin (a chest, a belly).
        /// </summary>
        FollowSurface = 0,
        /// <summary>The vertex takes the movement of the nearest point of the surface, unchanged.</summary>
        CopyMovement = 1,
    }

    /// <summary>Which shape of a race's body the slots are matched against.</summary>
    public enum UMABlendShapeBodyShape
    {
        /// <summary>The body as characters of the race wear it, blend shapes the race bakes in included.</summary>
        AsTheRaceBuildsIt = 0,
        /// <summary>The base slots as they are stored, with no blend shape applied.</summary>
        BaseSlots = 1,
    }

    /// <summary>What happens to a blend shape the target slot already has.</summary>
    public enum UMABlendShapeExistingMode
    {
        Replace = 0,
        Skip = 1,
    }

    public enum UMABlendShapeNormalMode
    {
        /// <summary>Normal and tangent deltas are worked out from how the transferred shape bends the slot's own surface.</summary>
        Recalculate = 0,
        /// <summary>Only vertex positions move; the shape carries no normal or tangent deltas.</summary>
        PositionsOnly = 1,
    }

    /// <summary>Settings for <see cref="UMABlendShapeTransfer"/>. Distances are in metres.</summary>
    [Serializable]
    public sealed class UMABlendShapeTransferSettings
    {
        public UMABlendShapeTransferMethod method = UMABlendShapeTransferMethod.FollowSurface;

        [Tooltip("Multiplies the transferred movement. 1 follows the source exactly.")]
        [Range(0f, 2f)] public float strength = 1f;

        [Tooltip("Fade the movement out with distance from the source surface. Off: every vertex follows its nearest surface point at full strength, however far away it is.")]
        public bool useFalloff = true;

        [Tooltip("Vertices nearer to the source surface than this follow it at full strength.")]
        [Min(0f)] public float fullStrengthDistance = 0.03f;

        [Tooltip("Vertices further from the source surface than this do not move at all. Between the two distances the movement fades smoothly.")]
        [Min(0f)] public float fadeOutDistance = 0.12f;

        [Tooltip("Prefer source surfaces that face the same way as the vertex, so the inside of one leg is not bound to the other leg.")]
        public bool preferFacingSurfaces = true;

        [Tooltip("How much nearer a surface facing the other way must be before it is chosen.")]
        [Min(0f)] public float facingPenalty = 0.03f;

        [Tooltip("Passes of smoothing over the transferred movement. Removes the steps where neighbouring vertices are bound to different parts of the source.")]
        [Range(0, 30)] public int smoothingIterations = 4;

        [Tooltip("How far each smoothing pass moves a vertex toward the average of its neighbours.")]
        [Range(0f, 1f)] public float smoothingStrength = 0.5f;

        [Tooltip("Move each small separate piece of the mesh (a button, a buckle, a stud) as one piece, so it is not stretched or bent.")]
        public bool rigidSmallParts = false;

        [Tooltip("Separate pieces no larger than this are moved as one piece.")]
        [Min(0f)] public float rigidPartMaxSize = 0.05f;

        [Tooltip("After smoothing, push a vertex back out where the shape would put it under the moved source surface.")]
        public bool keepClearance = true;

        [Tooltip("The distance from the moved source surface that is restored for such a vertex (never more than it had to begin with).")]
        [Min(0f)] public float clearance = 0.001f;

        [Tooltip("Movement smaller than this is stored as no movement, which keeps the blend shape sparse.")]
        [Min(0f)] public float minimumMovement = 0.00005f;

        [Tooltip("A blend shape that moves no vertex of a slot further than this is not added to that slot.")]
        [Min(0f)] public float skipShapeBelow = 0.0005f;

        public UMABlendShapeNormalMode normals = UMABlendShapeNormalMode.Recalculate;

        public UMABlendShapeExistingMode existing = UMABlendShapeExistingMode.Replace;

        public UMABlendShapeTransferSettings Clone()
        {
            return (UMABlendShapeTransferSettings)MemberwiseClone();
        }

        /// <summary>The share of the source movement a vertex at this distance from the source surface takes.</summary>
        public float Influence(float distance)
        {
            if (!useFalloff)
            {
                return 1f;
            }
            float full = Mathf.Max(0f, fullStrengthDistance);
            float end = Mathf.Max(full, fadeOutDistance);
            if (distance <= full)
            {
                return 1f;
            }
            if (distance >= end)
            {
                return 0f;
            }
            float t = (distance - full) / (end - full);
            return 1f - t * t * (3f - 2f * t);
        }
    }

    /// <summary>
    /// A race's rig, and what is needed to place a slot on it the way a character wears it: the
    /// legacy rig and rig profile for slots built for the rig the race replaces.
    /// </summary>
    public sealed class UMABlendShapeRigContext
    {
        public RaceData Race { get; private set; }
        public UMARigRestPose Rig { get; private set; }
        public UMARigRestPose LegacyRig { get; private set; }
        public UMASlotRigConversionProfile Profile { get; private set; }
        public readonly List<string> Messages = new List<string>();

        private List<UMAMeshHideAutoGenerator.BodySlot> bodySlots;
        private UMARigBodySurface bodySurface;

        public static bool TryCreate(RaceData race, out UMABlendShapeRigContext context, out string reason)
        {
            context = null;
            if (race == null)
            {
                reason = "Choose a race.";
                return false;
            }
            if (!UMARigRestPose.TryFromRace(race, out UMARigRestPose rig, out reason))
            {
                return false;
            }
            context = new UMABlendShapeRigContext
            {
                Race = race,
                Rig = rig,
                LegacyRig = race.legacyRig != null ? race.legacyRig.GetRestPose() : null,
                Profile = race.legacyRigProfile,
            };
            if (context.LegacyRig != null && context.LegacyRig.BoneCount == 0)
            {
                context.LegacyRig = null;
            }
            return true;
        }

        /// <summary>The slots the race builds its body from, as a character wears them. Collected once.</summary>
        public List<UMAMeshHideAutoGenerator.BodySlot> GetBodySlots()
        {
            if (bodySlots == null)
            {
                bodySlots = UMAMeshHideAutoGenerator.CollectBodySlots(Race, Rig, Messages);
            }
            return bodySlots;
        }

        /// <summary>The race's skin at rest, for rig profile effectors that clear the body.</summary>
        public UMARigBodySurface GetBodySurface()
        {
            if (bodySurface == null)
            {
                bodySurface = UMAMeshHideAutoGenerator.BuildBodySurface(GetBodySlots());
            }
            return bodySurface;
        }
    }

    /// <summary>
    /// A slot placed in avatar space the way a character of the race wears it, with the map between
    /// avatar space and the slot's own mesh space for every vertex. Blend shape deltas are stored in
    /// mesh space; all the matching and measuring is done in avatar space, where every slot of a
    /// character shares one frame however it was exported.
    /// </summary>
    public sealed class UMABlendShapeSlotSpace
    {
        public SlotDataAsset Slot;
        /// <summary>The slot's own mesh data: the space deltas are read from and written in.</summary>
        public UMAMeshData Mesh;
        public string Name;
        public Vector3[] Positions;
        /// <summary>The slot's authored normals in avatar space (geometric normals when it has none).</summary>
        public Vector3[] Normals;
        /// <summary>For each vertex, how a mesh-space delta moves the vertex in avatar space (linear part only).</summary>
        public Matrix4x4[] ToAvatar;
        /// <summary>Base-LOD triangles of every submesh.</summary>
        public int[] Triangles;
        /// <summary>True when the slot was built for the race's legacy rig and is placed as automatic conversion places it.</summary>
        public bool ConvertedFromLegacy;
        /// <summary>
        /// For a legacy slot: per vertex, how conversion maps a delta from the slot's own mesh space to
        /// the mesh space of the converted copy a character wears. Null for any other slot, where the
        /// two are the same space.
        /// </summary>
        public Matrix4x4[] OwnToWorn;
        public string Note;

        private Matrix4x4[] fromAvatar;
        private Matrix4x4[] wornToOwn;

        /// <summary>Turns a delta given in the mesh space of the slot as a character wears it into the slot's own mesh space.</summary>
        public Vector3 WornToOwnSpace(int vertex, Vector3 wornDelta)
        {
            if (OwnToWorn == null)
            {
                return wornDelta;
            }
            if (wornToOwn == null)
            {
                wornToOwn = new Matrix4x4[OwnToWorn.Length];
                for (int i = 0; i < wornToOwn.Length; i++)
                {
                    Matrix4x4 m = OwnToWorn[i];
                    wornToOwn[i] = Mathf.Abs(m.determinant) > 1e-12f ? m.inverse : Matrix4x4.identity;
                }
            }
            return wornToOwn[vertex].MultiplyVector(wornDelta);
        }

        /// <summary>Turns a delta in the slot's own mesh space into the mesh space of the slot as a character wears it.</summary>
        public Vector3 OwnToWornSpace(int vertex, Vector3 ownDelta)
        {
            return OwnToWorn == null ? ownDelta : OwnToWorn[vertex].MultiplyVector(ownDelta);
        }

        public int VertexCount => Positions != null ? Positions.Length : 0;

        /// <summary>A mesh that is already in avatar space, for tests and for meshes without a rig.</summary>
        public static UMABlendShapeSlotSpace FromArrays(string name, Vector3[] positions, Vector3[] normals, int[] triangles, UMAMeshData mesh = null)
        {
            var space = new UMABlendShapeSlotSpace
            {
                Name = name,
                Mesh = mesh,
                Positions = positions,
                Triangles = triangles,
                ToAvatar = new Matrix4x4[positions.Length],
            };
            for (int i = 0; i < space.ToAvatar.Length; i++)
            {
                space.ToAvatar[i] = Matrix4x4.identity;
            }
            space.Normals = normals != null && normals.Length == positions.Length ? normals : UMABlendShapeTransfer.GeometricNormals(positions, triangles);
            return space;
        }

        /// <summary>
        /// Places a slot on the race's rig. A slot built for the race's legacy rig is converted the way
        /// automatic conversion converts it (with the race's rig profile); the per-vertex maps then lead
        /// back to the legacy slot's own mesh space, so deltas written there arrive in the right place
        /// when the slot is converted for a character.
        /// </summary>
        public static bool TryCreate(SlotDataAsset slot, UMABlendShapeRigContext context, out UMABlendShapeSlotSpace space, out string reason)
        {
            space = null;
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                reason = "The slot has no mesh.";
                return false;
            }
            if (context == null || context.Rig == null)
            {
                reason = "No race rig to place the slot on.";
                return false;
            }
            UMAMeshData mesh = slot.meshData;
            if (!UMASlotRigConverter.CanConvert(mesh, out reason))
            {
                return false;
            }
            if (mesh.submeshes == null || mesh.submeshes.Length == 0)
            {
                reason = "The slot has no triangles.";
                return false;
            }

            if (context.LegacyRig != null && UMALegacySlotConverter.IsBuiltForLegacyRig(mesh, context.LegacyRig, context.Rig))
            {
                return TryCreateFromLegacy(slot, context, out space, out reason);
            }

            if (!TrySkin(mesh, context.Rig, out Vector3[] positions, out Vector3[] normals, out Matrix4x4[] skin))
            {
                reason = "The slot could not be skinned to the race's rig.";
                return false;
            }
            space = new UMABlendShapeSlotSpace
            {
                Slot = slot,
                Mesh = mesh,
                Name = slot.slotName,
                Positions = positions,
                Normals = normals,
                ToAvatar = skin,
                Triangles = ConcatTriangles(mesh),
            };
            if (!UMASlotRigConverter.IsBoundToRigMeshSpace(mesh, context.Rig, 1e-3f, out _))
            {
                float own = UMALegacySlotConverter.GetBoneMatch(mesh, context.Rig, out int compared);
                if (compared > 0 && own < UMALegacySlotConverter.OwnRigMatch)
                {
                    space.Note = $"Only {own:P0} of its bones match {context.Race.raceName}'s rig; it may have been built for another race.";
                }
            }
            reason = null;
            return true;
        }

        private static bool TryCreateFromLegacy(SlotDataAsset slot, UMABlendShapeRigContext context, out UMABlendShapeSlotSpace space, out string reason)
        {
            space = null;
            UMAMeshData mesh = slot.meshData;
            int count = mesh.vertices.Length;

            // Three probe blend shapes, one per axis, ride through the conversion with the slot. What
            // comes out is, for each vertex, exactly how conversion maps a delta; no knowledge of the
            // converter's insides is needed, and the map stays right when the converter changes.
            UMAMeshData probe = mesh.ShallowCopy(null);
            probe.boneWeights = mesh.boneWeights;
            probe.blendShapes = new[] { ProbeShape("x", Vector3.right, count), ProbeShape("y", Vector3.up, count), ProbeShape("z", Vector3.forward, count) };

            var options = new UMASlotRigConversionOptions { Profile = context.Profile, ProfileSlotName = slot.slotName };
            if (context.Profile != null && context.Profile.NeedsBodySurface(slot.slotName))
            {
                options.BodySurface = context.GetBodySurface();
            }
            if (!UMASlotRigConverter.TryConvert(probe, context.LegacyRig, context.Rig, options, new UMASlotRigConversionReport(), out UMAMeshData converted, out reason))
            {
                return false;
            }
            if (converted.blendShapes == null || converted.blendShapes.Length != 3 || converted.vertices.Length != count ||
                !TrySkin(converted, context.Rig, out Vector3[] positions, out Vector3[] normals, out Matrix4x4[] skin))
            {
                reason = "The converted slot could not be skinned to the race's rig.";
                return false;
            }

            Vector3[] dx = converted.blendShapes[0].frames[0].deltaVertices;
            Vector3[] dy = converted.blendShapes[1].frames[0].deltaVertices;
            Vector3[] dz = converted.blendShapes[2].frames[0].deltaVertices;
            var toAvatar = new Matrix4x4[count];
            var ownToWorn = new Matrix4x4[count];
            for (int v = 0; v < count; v++)
            {
                Matrix4x4 m = Matrix4x4.identity;
                m.SetColumn(0, skin[v].MultiplyVector(dx[v]));
                m.SetColumn(1, skin[v].MultiplyVector(dy[v]));
                m.SetColumn(2, skin[v].MultiplyVector(dz[v]));
                toAvatar[v] = m;
                Matrix4x4 c = Matrix4x4.identity;
                c.SetColumn(0, dx[v]);
                c.SetColumn(1, dy[v]);
                c.SetColumn(2, dz[v]);
                ownToWorn[v] = c;
            }
            space = new UMABlendShapeSlotSpace
            {
                Slot = slot,
                Mesh = mesh,
                Name = slot.slotName,
                Positions = positions,
                Normals = normals,
                ToAvatar = toAvatar,
                OwnToWorn = ownToWorn,
                Triangles = ConcatTriangles(converted),
                ConvertedFromLegacy = true,
                Note = slot.isLegacySlot
                    ? $"Built for {context.Race.raceName}'s legacy rig: matched as automatic conversion places it" + (context.Profile != null ? $" (with '{context.Profile.name}')." : ".")
                    : $"Built for {context.Race.raceName}'s legacy rig, but its 'Is Legacy Slot' flag is off, so characters wear it unconverted. Turn the flag on, or convert the slot first.",
            };
            reason = null;
            return true;
        }

        private static UMABlendShape ProbeShape(string name, Vector3 axis, int count)
        {
            var deltas = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                deltas[i] = axis;
            }
            return new UMABlendShape
            {
                shapeName = "__uma_transfer_probe_" + name,
                frames = new[] { new UMABlendFrame { frameWeight = 100f, deltaVertices = deltas, deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] } },
            };
        }

        /// <summary>Base-LOD triangles of all submeshes, in order.</summary>
        public static int[] ConcatTriangles(UMAMeshData mesh)
        {
            var all = new List<int>();
            if (mesh.submeshes != null)
            {
                for (int s = 0; s < mesh.submeshes.Length; s++)
                {
                    if (mesh.submeshes[s] == null)
                    {
                        continue;
                    }
                    int[] triangles = mesh.submeshes[s].getManagedTriangles(0);
                    if (triangles != null)
                    {
                        all.AddRange(triangles);
                    }
                }
            }
            int usable = all.Count - all.Count % 3;
            if (usable != all.Count)
            {
                all.RemoveRange(usable, all.Count - usable);
            }
            return all.ToArray();
        }

        /// <summary>
        /// Skins mesh data to avatar space with the rig at rest, as <see cref="UMARigBodySurface.TryBakeToAvatar"/>
        /// does, and also returns each vertex's blended skinning matrix.
        /// </summary>
        public static bool TrySkin(UMAMeshData mesh, UMARigRestPose rig, out Vector3[] positions, out Vector3[] normals, out Matrix4x4[] vertexMatrices)
        {
            positions = null;
            normals = null;
            vertexMatrices = null;
            if (rig == null || !UMASlotRigConverter.CanConvert(mesh, out _))
            {
                return false;
            }
            UMARigRestPose full = rig.WithAdditionalBones(mesh.umaBones);
            int influenceCount = Mathf.Min(mesh.bindPoses.Length, mesh.boneNameHashes.Length);
            var skin = new Matrix4x4[influenceCount];
            for (int i = 0; i < influenceCount; i++)
            {
                if (!full.TryGetAvatarMatrix(mesh.boneNameHashes[i], out Matrix4x4 boneToAvatar))
                {
                    boneToAvatar = full.RigRootToAvatar;
                }
                skin[i] = boneToAvatar * mesh.bindPoses[i];
            }

            int count = mesh.vertices.Length;
            positions = new Vector3[count];
            vertexMatrices = new Matrix4x4[count];
            bool hasNormals = mesh.normals != null && mesh.normals.Length == count;
            normals = hasNormals ? new Vector3[count] : null;
            bool managed = mesh.ManagedBonesPerVertex != null && mesh.ManagedBonesPerVertex.Length == count &&
                           mesh.ManagedBoneWeights != null && mesh.ManagedBoneWeights.Length > 0;
            int cursor = 0;
            for (int v = 0; v < count; v++)
            {
                Matrix4x4 blended = default;
                float total = 0f;
                if (managed)
                {
                    int influences = mesh.ManagedBonesPerVertex[v];
                    for (int k = 0; k < influences; k++)
                    {
                        BoneWeight1 weight = mesh.ManagedBoneWeights[cursor++];
                        if (weight.boneIndex >= 0 && weight.boneIndex < influenceCount)
                        {
                            AddScaled(ref blended, skin[weight.boneIndex], weight.weight);
                            total += weight.weight;
                        }
                    }
                }
                else
                {
                    UMABoneWeight weight = mesh.boneWeights[v];
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex0, weight.weight0);
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex1, weight.weight1);
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex2, weight.weight2);
                    AddLegacy(ref blended, ref total, skin, weight.boneIndex3, weight.weight3);
                }
                if (total <= 1e-6f)
                {
                    blended = full.RigRootToAvatar;
                }
                positions[v] = blended.MultiplyPoint3x4(mesh.vertices[v]);
                if (hasNormals)
                {
                    normals[v] = blended.MultiplyVector(mesh.normals[v]).normalized;
                }
                // Only the linear part matters for deltas.
                blended.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                blended.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
                vertexMatrices[v] = blended;
            }
            if (!hasNormals)
            {
                normals = UMABlendShapeTransfer.GeometricNormals(positions, ConcatTriangles(mesh));
            }
            return true;
        }

        private static void AddLegacy(ref Matrix4x4 blended, ref float total, Matrix4x4[] skin, int boneIndex, float weight)
        {
            if (weight > 0f && boneIndex >= 0 && boneIndex < skin.Length)
            {
                AddScaled(ref blended, skin[boneIndex], weight);
                total += weight;
            }
        }

        private static void AddScaled(ref Matrix4x4 accumulator, Matrix4x4 matrix, float weight)
        {
            for (int i = 0; i < 16; i++)
            {
                accumulator[i] += matrix[i] * weight;
            }
        }

        /// <summary>Turns an avatar-space delta of one vertex into the slot's mesh space.</summary>
        public Vector3 ToMeshSpace(int vertex, Vector3 avatarDelta)
        {
            if (fromAvatar == null)
            {
                fromAvatar = new Matrix4x4[ToAvatar.Length];
                for (int i = 0; i < fromAvatar.Length; i++)
                {
                    Matrix4x4 m = ToAvatar[i];
                    fromAvatar[i] = Mathf.Abs(m.determinant) > 1e-12f ? m.inverse : Matrix4x4.identity;
                }
            }
            return fromAvatar[vertex].MultiplyVector(avatarDelta);
        }
    }

    /// <summary>
    /// The surface blend shapes are taken from: one or more slots in avatar space, with every blend
    /// shape they carry turned into avatar-space movement of the surface's vertices.
    /// </summary>
    public sealed class UMABlendShapeSource
    {
        public sealed class Frame
        {
            public float Weight;
            /// <summary>Avatar-space movement of every source vertex; zero for vertices of slots that lack the shape.</summary>
            public Vector3[] Deltas;
        }

        public sealed class Shape
        {
            public string Name;
            public Frame[] Frames;
            /// <summary>The furthest any source vertex moves in the last frame, in metres.</summary>
            public float MaxMovement;
            public readonly List<string> Slots = new List<string>();
        }

        private readonly List<UMABlendShapeSlotSpace> spaces = new List<UMABlendShapeSlotSpace>();
        private readonly List<int> starts = new List<int>();
        private bool built;

        public Vector3[] Positions { get; private set; }
        /// <summary>Authored normals: which way each vertex's surface faces.</summary>
        public Vector3[] Normals { get; private set; }
        public int[] Triangles { get; private set; }
        /// <summary>Vertices at the same place (UV seams, the seams between slots) share a group.</summary>
        public int[] Groups { get; private set; }
        public int GroupCount { get; private set; }
        /// <summary>Smooth geometric normal of each vertex at rest, oriented like the authored normals.</summary>
        public Vector3[] RestNormals { get; private set; }
        public readonly List<Shape> Shapes = new List<Shape>();
        public readonly List<string> Messages = new List<string>();
        public IReadOnlyList<UMABlendShapeSlotSpace> Spaces => spaces;
        public UMATriangleLookup Lookup { get; private set; }
        private bool[] groupFlipped;
        // Per frame (keyed by its delta array): the moved surface's normals, and how each vertex's surface turned.
        private readonly Dictionary<Vector3[], Vector3[]> movedNormalCache = new Dictionary<Vector3[], Vector3[]>();
        private readonly Dictionary<Vector3[], Quaternion[]> turnCache = new Dictionary<Vector3[], Quaternion[]>();

        public int VertexCount => Positions != null ? Positions.Length : 0;

        public void Add(UMABlendShapeSlotSpace space)
        {
            if (space == null || space.VertexCount == 0)
            {
                return;
            }
            spaces.Add(space);
            built = false;
        }

        public Shape FindShape(string name)
        {
            Build();
            for (int i = 0; i < Shapes.Count; i++)
            {
                if (Shapes[i].Name == name)
                {
                    return Shapes[i];
                }
            }
            return null;
        }

        /// <summary>
        /// The slots a race builds its body from, as the source. <paramref name="slotNames"/> limits it
        /// to some of the base slots; null takes every slot that carries a blend shape.
        /// </summary>
        public static UMABlendShapeSource FromRace(UMABlendShapeRigContext context, UMABlendShapeBodyShape bodyShape, ICollection<string> slotNames = null)
        {
            var source = new UMABlendShapeSource();
            foreach (UMAMeshHideAutoGenerator.BodySlot body in context.GetBodySlots())
            {
                SlotDataAsset baseSlot = body.BaseSlot;
                if (baseSlot == null || (slotNames != null && !slotNames.Contains(baseSlot.slotName)))
                {
                    continue;
                }
                if (slotNames == null && (baseSlot.meshData.blendShapes == null || baseSlot.meshData.blendShapes.Length == 0))
                {
                    continue;
                }
                if (!UMABlendShapeSlotSpace.TryCreate(baseSlot, context, out UMABlendShapeSlotSpace space, out string reason))
                {
                    source.Messages.Add($"Source slot '{baseSlot.slotName}' was left out: {reason}");
                    continue;
                }
                if (bodyShape == UMABlendShapeBodyShape.AsTheRaceBuildsIt && body.Positions != null && body.Positions.Length == space.Positions.Length)
                {
                    // Same vertices, moved by the blend shapes the race bakes in.
                    space.Positions = body.Positions;
                    if (body.Normals != null && body.Normals.Length == space.Positions.Length)
                    {
                        space.Normals = body.Normals;
                    }
                }
                source.Add(space);
            }
            return source;
        }

        public void Build()
        {
            if (built)
            {
                return;
            }
            built = true;
            Shapes.Clear();
            starts.Clear();
            movedNormalCache.Clear();
            turnCache.Clear();
            int total = 0;
            int triangleTotal = 0;
            foreach (UMABlendShapeSlotSpace space in spaces)
            {
                starts.Add(total);
                total += space.VertexCount;
                triangleTotal += space.Triangles.Length;
            }
            Positions = new Vector3[total];
            Normals = new Vector3[total];
            Triangles = new int[triangleTotal];
            int triangleCursor = 0;
            for (int s = 0; s < spaces.Count; s++)
            {
                UMABlendShapeSlotSpace space = spaces[s];
                int start = starts[s];
                Array.Copy(space.Positions, 0, Positions, start, space.VertexCount);
                Array.Copy(space.Normals, 0, Normals, start, space.VertexCount);
                for (int t = 0; t < space.Triangles.Length; t++)
                {
                    Triangles[triangleCursor++] = space.Triangles[t] + start;
                }
            }

            for (int s = 0; s < spaces.Count; s++)
            {
                UMABlendShapeSlotSpace space = spaces[s];
                UMABlendShape[] shapes = space.Mesh != null ? space.Mesh.blendShapes : null;
                if (shapes == null)
                {
                    continue;
                }
                int start = starts[s];
                foreach (UMABlendShape shape in shapes)
                {
                    if (shape == null || string.IsNullOrEmpty(shape.shapeName) || shape.frames == null || shape.frames.Length == 0)
                    {
                        continue;
                    }
                    Shape target = null;
                    foreach (Shape existing in Shapes)
                    {
                        if (existing.Name == shape.shapeName)
                        {
                            target = existing;
                            break;
                        }
                    }
                    if (target == null)
                    {
                        target = new Shape { Name = shape.shapeName, Frames = new Frame[shape.frames.Length] };
                        for (int f = 0; f < target.Frames.Length; f++)
                        {
                            target.Frames[f] = new Frame { Weight = shape.frames[f] != null ? shape.frames[f].frameWeight : 100f, Deltas = new Vector3[total] };
                        }
                        Shapes.Add(target);
                    }
                    else if (target.Frames.Length != shape.frames.Length)
                    {
                        Messages.Add($"'{shape.shapeName}' has {shape.frames.Length} frame(s) on '{space.Name}' and {target.Frames.Length} on '{target.Slots[0]}'; frames are matched in order.");
                    }
                    bool used = false;
                    int frames = Mathf.Min(target.Frames.Length, shape.frames.Length);
                    for (int f = 0; f < frames; f++)
                    {
                        Vector3[] deltas = shape.frames[f] != null ? shape.frames[f].deltaVertices : null;
                        if (deltas == null || deltas.Length != space.VertexCount)
                        {
                            Messages.Add($"'{shape.shapeName}' on '{space.Name}' does not have one delta per vertex and was left out.");
                            continue;
                        }
                        Vector3[] output = target.Frames[f].Deltas;
                        for (int v = 0; v < deltas.Length; v++)
                        {
                            output[start + v] = space.ToAvatar[v].MultiplyVector(deltas[v]);
                        }
                        used = true;
                    }
                    if (used)
                    {
                        target.Slots.Add(space.Name);
                    }
                }
            }
            foreach (Shape shape in Shapes)
            {
                float max = 0f;
                Vector3[] last = shape.Frames[shape.Frames.Length - 1].Deltas;
                for (int v = 0; v < last.Length; v++)
                {
                    max = Mathf.Max(max, last[v].sqrMagnitude);
                }
                shape.MaxMovement = Mathf.Sqrt(max);
            }

            Groups = UMABlendShapeTransfer.WeldByPosition(Positions, UMABlendShapeTransfer.WeldTolerance, out int groupCount);
            GroupCount = groupCount;
            // Orient the smooth geometric normals like the authored ones, so "outside" means the same
            // on slots whose triangles are wound the other way.
            var groupNormals = new Vector3[groupCount];
            UMABlendShapeTransfer.GroupNormals(Positions, null, Triangles, Groups, groupNormals);
            var authored = new Vector3[groupCount];
            for (int v = 0; v < total; v++)
            {
                authored[Groups[v]] += Normals[v];
            }
            groupFlipped = new bool[groupCount];
            RestNormals = new Vector3[total];
            for (int g = 0; g < groupCount; g++)
            {
                groupFlipped[g] = Vector3.Dot(groupNormals[g], authored[g]) < 0f;
            }
            for (int v = 0; v < total; v++)
            {
                Vector3 n = groupNormals[Groups[v]];
                RestNormals[v] = groupFlipped[Groups[v]] ? -n : n;
            }
            Lookup = new UMATriangleLookup(Positions, Normals, Triangles);
        }

        /// <summary>Smooth geometric normals of the surface moved by a frame, oriented like <see cref="RestNormals"/>.</summary>
        internal Vector3[] MovedNormals(Vector3[] deltas)
        {
            if (movedNormalCache.TryGetValue(deltas, out Vector3[] cached))
            {
                return cached;
            }
            var groupNormals = new Vector3[GroupCount];
            UMABlendShapeTransfer.GroupNormals(Positions, deltas, Triangles, Groups, groupNormals);
            var result = new Vector3[Positions.Length];
            for (int v = 0; v < result.Length; v++)
            {
                Vector3 n = groupNormals[Groups[v]];
                result[v] = groupFlipped[Groups[v]] ? -n : n;
            }
            movedNormalCache[deltas] = result;
            return result;
        }

        /// <summary>For each source vertex, the rotation that takes its surface normal at rest to its normal in a frame.</summary>
        internal Quaternion[] Turns(Vector3[] deltas)
        {
            if (turnCache.TryGetValue(deltas, out Quaternion[] cached))
            {
                return cached;
            }
            Vector3[] moved = MovedNormals(deltas);
            var turns = new Quaternion[moved.Length];
            for (int v = 0; v < turns.Length; v++)
            {
                turns[v] = RestNormals[v].sqrMagnitude > 0.5f && moved[v].sqrMagnitude > 0.5f
                    ? Quaternion.FromToRotation(RestNormals[v], moved[v]) : Quaternion.identity;
            }
            turnCache[deltas] = turns;
            return turns;
        }
    }

    /// <summary>Triangles in a uniform grid, with a search for the triangle nearest to a point.</summary>
    public sealed class UMATriangleLookup
    {
        private readonly Vector3[] positions;
        private readonly int[] triangles;
        private readonly Vector3[] facing;   // one per triangle: which way its surface faces
        private readonly float cellSize;
        private readonly Vector3 origin;
        private readonly Vector3Int minCell, maxCell;
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        private readonly int[] stamps;
        private int stamp;

        public int TriangleCount => triangles.Length / 3;

        public UMATriangleLookup(Vector3[] positions, Vector3[] vertexNormals, int[] triangles)
        {
            this.positions = positions;
            this.triangles = triangles;
            int count = triangles.Length / 3;
            facing = new Vector3[count];
            stamps = new int[count];
            if (count == 0)
            {
                cellSize = 1f;
                return;
            }
            Vector3 min = positions[triangles[0]];
            Vector3 max = min;
            float edgeSum = 0f;
            for (int t = 0; t < count; t++)
            {
                Vector3 a = positions[triangles[t * 3]], b = positions[triangles[t * 3 + 1]], c = positions[triangles[t * 3 + 2]];
                min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
                max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
                edgeSum += (b - a).magnitude;
                Vector3 n = vertexNormals != null
                    ? vertexNormals[triangles[t * 3]] + vertexNormals[triangles[t * 3 + 1]] + vertexNormals[triangles[t * 3 + 2]]
                    : Vector3.zero;
                if (n.sqrMagnitude < 1e-12f)
                {
                    n = Vector3.Cross(b - a, c - a);
                }
                facing[t] = n.normalized;
            }
            cellSize = Mathf.Max(0.004f, 3f * edgeSum / count);
            origin = min;
            minCell = CellOf(min);
            maxCell = CellOf(max);
            for (int t = 0; t < count; t++)
            {
                Vector3 a = positions[triangles[t * 3]], b = positions[triangles[t * 3 + 1]], c = positions[triangles[t * 3 + 2]];
                Vector3Int lo = CellOf(Vector3.Min(a, Vector3.Min(b, c)));
                Vector3Int hi = CellOf(Vector3.Max(a, Vector3.Max(b, c)));
                for (int x = lo.x; x <= hi.x; x++)
                {
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        for (int z = lo.z; z <= hi.z; z++)
                        {
                            long key = Key(x, y, z);
                            if (!cells.TryGetValue(key, out List<int> list))
                            {
                                list = new List<int>(8);
                                cells.Add(key, list);
                            }
                            list.Add(t);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The triangle nearest to <paramref name="point"/>. A triangle facing away from
        /// <paramref name="normal"/> counts as <paramref name="facingPenalty"/> further away.
        /// </summary>
        public bool FindNearest(Vector3 point, Vector3 normal, float facingPenalty, out int triangle, out Vector3 closest, out float distance)
        {
            triangle = -1;
            closest = point;
            distance = float.MaxValue;
            if (TriangleCount == 0)
            {
                return false;
            }
            stamp++;
            Vector3Int home = CellOf(point);
            int maxRing = 0;
            maxRing = Mathf.Max(maxRing, Mathf.Abs(home.x - minCell.x), Mathf.Abs(maxCell.x - home.x));
            maxRing = Mathf.Max(maxRing, Mathf.Abs(home.y - minCell.y), Mathf.Abs(maxCell.y - home.y));
            maxRing = Mathf.Max(maxRing, Mathf.Abs(home.z - minCell.z), Mathf.Abs(maxCell.z - home.z));
            float bestScore = float.MaxValue;
            bool usePenalty = facingPenalty > 0f && normal.sqrMagnitude > 1e-12f;
            for (int ring = 0; ring <= maxRing; ring++)
            {
                // Everything outside the cube of cells searched so far is at least this far away.
                if (bestScore <= (ring - 1) * cellSize)
                {
                    break;
                }
                for (int x = -ring; x <= ring; x++)
                {
                    for (int y = -ring; y <= ring; y++)
                    {
                        bool shellXY = Mathf.Abs(x) == ring || Mathf.Abs(y) == ring;
                        for (int z = -ring; z <= ring; z += shellXY ? 1 : Mathf.Max(1, 2 * ring))
                        {
                            if (!cells.TryGetValue(Key(home.x + x, home.y + y, home.z + z), out List<int> list))
                            {
                                continue;
                            }
                            for (int i = 0; i < list.Count; i++)
                            {
                                int t = list[i];
                                if (stamps[t] == stamp)
                                {
                                    continue;
                                }
                                stamps[t] = stamp;
                                Vector3 q = ClosestPointOnTriangle(point, positions[triangles[t * 3]], positions[triangles[t * 3 + 1]], positions[triangles[t * 3 + 2]]);
                                float d = (point - q).magnitude;
                                float score = d + (usePenalty && Vector3.Dot(normal, facing[t]) < 0f ? facingPenalty : 0f);
                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    triangle = t;
                                    closest = q;
                                    distance = d;
                                }
                            }
                        }
                    }
                }
            }
            return triangle >= 0;
        }

        private Vector3Int CellOf(Vector3 p)
        {
            Vector3 local = (p - origin) / cellSize;
            return new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y), Mathf.FloorToInt(local.z));
        }

        private static long Key(int x, int y, int z)
        {
            const long mask = (1L << 21) - 1;
            return ((x + (1L << 20)) & mask) | (((y + (1L << 20)) & mask) << 21) | (((z + (1L << 20)) & mask) << 42);
        }

        /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }

    /// <summary>What a transfer produced for one slot.</summary>
    public sealed class UMABlendShapeTransferResult
    {
        public sealed class ShapeResult
        {
            public string Name;
            /// <summary>The blend shape in the slot's mesh space; null when it was not worth adding.</summary>
            public UMABlendShape Shape;
            /// <summary>Avatar-space movement of each vertex in the last frame, for previews.</summary>
            public Vector3[] AvatarDeltas;
            public float MaxMovement;
            public int MovedVertices;
            /// <summary>Vertices that were outside the source surface at rest and end up under it.</summary>
            public int PokeThrough;
            /// <summary>Per vertex: true when it ends up under the moved source surface.</summary>
            public bool[] Under;
            /// <summary>Vertices the clearance pass pushed back out.</summary>
            public int ClearanceFixed;
            public string Skipped;
        }

        public UMABlendShapeSlotSpace Space;
        public readonly List<ShapeResult> Shapes = new List<ShapeResult>();
        public readonly List<string> Messages = new List<string>();
        /// <summary>Per vertex: the share of the source movement it takes (the falloff).</summary>
        public float[] Influence;
        /// <summary>Per vertex: distance to the source surface at rest.</summary>
        public float[] Distance;
        public int BoundVertices;
        public int VerticesInReach;
        /// <summary>Per vertex: true when it already lies under the source surface before any shape is applied.</summary>
        public bool[] UnderAtRest;
        public int UnderAtRestCount;
        /// <summary>A vertex this close under the source surface at rest counts as under it; further in, it belongs to another layer.</summary>
        public const float UnderAtRestReach = 0.03f;

        public SlotDataAsset Slot => Space != null ? Space.Slot : null;

        public ShapeResult Find(string name)
        {
            for (int i = 0; i < Shapes.Count; i++)
            {
                if (Shapes[i].Name == name)
                {
                    return Shapes[i];
                }
            }
            return null;
        }

        public string Summary()
        {
            int added = 0;
            float max = 0f;
            int poke = 0;
            foreach (ShapeResult shape in Shapes)
            {
                if (shape.Shape != null)
                {
                    added++;
                    max = Mathf.Max(max, shape.MaxMovement);
                    poke += shape.PokeThrough;
                }
            }
            string name = Space != null ? Space.Name : "(slot)";
            return $"{name}: {added} of {Shapes.Count} blend shape(s), {VerticesInReach} of {(Space != null ? Space.VertexCount : 0)} vertices in reach, " +
                   $"largest movement {max * 1000f:0.0} mm" + (poke > 0 ? $", {poke} vertex/shape pair(s) end under the source surface" : string.Empty) +
                   (UnderAtRestCount > 0 ? $", {UnderAtRestCount} vertices already lie under the source surface at rest" : string.Empty);
        }
    }

    /// <summary>
    /// Transfers blend shapes from a source surface (normally a race's body) to slots that do not have
    /// them, so clothing follows the body when the shape is dialled in.
    /// </summary>
    /// <remarks>
    /// Every target vertex is bound to the nearest point of the source surface, in avatar space, and
    /// takes that point's movement, blended between the corners of the triangle it lies on. With
    /// <see cref="UMABlendShapeTransferMethod.FollowSurface"/> the vertex's offset from the surface
    /// also turns with the surface. The movement fades with distance, is smoothed over the slot's own
    /// surface, optionally kept clear of the moved source, and is written back in the slot's own mesh
    /// space with matching normal and tangent deltas. Vertices that share a position (UV seams and
    /// hard edges) always move together.
    /// </remarks>
    public static class UMABlendShapeTransfer
    {
        /// <summary>Vertices nearer than this to each other are the same point of the surface.</summary>
        public const float WeldTolerance = 0.00005f;

        private sealed class Binding
        {
            public int[] Groups;            // per vertex
            public int GroupCount;
            public int[] First;             // a member vertex of each group
            public int[] Triangle;          // per group; -1 when unbound
            public Vector3[] Bary;
            public Vector3[] Offset;        // group position minus the bound surface point
            public float[] Distance;
            public float[] Influence;
            public float[] Clearance;       // signed distance outside the source surface at rest
            public List<int>[] Neighbours;  // per group
            public int[] Island;            // per group
            public bool[] IslandRigid;      // per island
            public int IslandCount;
            public Vector3[] RestNormals;   // per group, geometric
        }

        /// <summary>
        /// Transfers the named blend shapes of <paramref name="source"/> to one slot. Nothing is saved;
        /// see <see cref="Apply"/>.
        /// </summary>
        public static UMABlendShapeTransferResult Transfer(UMABlendShapeSource source, UMABlendShapeSlotSpace target, IList<string> shapeNames,
            UMABlendShapeTransferSettings settings)
        {
            settings = settings ?? new UMABlendShapeTransferSettings();
            var result = new UMABlendShapeTransferResult { Space = target };
            if (source == null || target == null || target.VertexCount == 0)
            {
                result.Messages.Add("A source and a target are needed.");
                return result;
            }
            source.Build();
            if (source.Triangles.Length == 0)
            {
                result.Messages.Add("The source has no surface.");
                return result;
            }

            Binding binding = Bind(source, target, settings);
            int count = target.VertexCount;
            result.Influence = new float[count];
            result.Distance = new float[count];
            result.UnderAtRest = new bool[count];
            for (int v = 0; v < count; v++)
            {
                int g = binding.Groups[v];
                result.Influence[v] = binding.Influence[g];
                result.Distance[v] = binding.Distance[g];
                if (binding.Triangle[g] >= 0)
                {
                    result.BoundVertices++;
                    if (binding.Influence[g] > 0f)
                    {
                        result.VerticesInReach++;
                    }
                    if (binding.Clearance[g] < 0f && binding.Distance[g] <= UMABlendShapeTransferResult.UnderAtRestReach)
                    {
                        result.UnderAtRest[v] = true;
                        result.UnderAtRestCount++;
                    }
                }
            }
            if (!string.IsNullOrEmpty(target.Note))
            {
                result.Messages.Add(target.Note);
            }

            if (shapeNames == null)
            {
                return result;
            }
            foreach (string name in shapeNames)
            {
                UMABlendShapeSource.Shape shape = source.FindShape(name);
                var shapeResult = new UMABlendShapeTransferResult.ShapeResult { Name = name };
                result.Shapes.Add(shapeResult);
                if (shape == null)
                {
                    shapeResult.Skipped = "the source does not have it";
                    continue;
                }
                TransferShape(source, target, binding, shape, settings, shapeResult);
            }
            return result;
        }

        private static Binding Bind(UMABlendShapeSource source, UMABlendShapeSlotSpace target, UMABlendShapeTransferSettings settings)
        {
            var binding = new Binding();
            int count = target.VertexCount;
            binding.Groups = WeldByPosition(target.Positions, WeldTolerance, out binding.GroupCount);
            int groups = binding.GroupCount;
            binding.First = new int[groups];
            var normalSum = new Vector3[groups];
            for (int g = 0; g < groups; g++)
            {
                binding.First[g] = -1;
            }
            for (int v = 0; v < count; v++)
            {
                int g = binding.Groups[v];
                if (binding.First[g] < 0)
                {
                    binding.First[g] = v;
                }
                normalSum[g] += target.Normals[v];
            }

            binding.Triangle = new int[groups];
            binding.Bary = new Vector3[groups];
            binding.Offset = new Vector3[groups];
            binding.Distance = new float[groups];
            binding.Influence = new float[groups];
            binding.Clearance = new float[groups];
            float penalty = settings.preferFacingSurfaces ? settings.facingPenalty : 0f;
            for (int g = 0; g < groups; g++)
            {
                Vector3 p = target.Positions[binding.First[g]];
                if (!source.Lookup.FindNearest(p, normalSum[g], penalty, out int triangle, out Vector3 closest, out float distance))
                {
                    binding.Triangle[g] = -1;
                    binding.Distance[g] = float.MaxValue;
                    continue;
                }
                int a = source.Triangles[triangle * 3], b = source.Triangles[triangle * 3 + 1], c = source.Triangles[triangle * 3 + 2];
                Vector3 bary = Barycentric(closest, source.Positions[a], source.Positions[b], source.Positions[c]);
                binding.Triangle[g] = triangle;
                binding.Bary[g] = bary;
                binding.Offset[g] = p - closest;
                binding.Distance[g] = distance;
                binding.Influence[g] = settings.Influence(distance);
                Vector3 surfaceNormal = (source.RestNormals[a] * bary.x + source.RestNormals[b] * bary.y + source.RestNormals[c] * bary.z).normalized;
                binding.Clearance[g] = Vector3.Dot(binding.Offset[g], surfaceNormal);
            }

            // Topology over groups: neighbours for smoothing, islands for rigid parts.
            binding.Neighbours = new List<int>[groups];
            int[] triangles = target.Triangles;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = binding.Groups[triangles[t]], b = binding.Groups[triangles[t + 1]], c = binding.Groups[triangles[t + 2]];
                Link(binding.Neighbours, a, b);
                Link(binding.Neighbours, b, c);
                Link(binding.Neighbours, c, a);
            }
            binding.Island = new int[groups];
            for (int g = 0; g < groups; g++)
            {
                binding.Island[g] = -1;
            }
            var islandMin = new List<Vector3>();
            var islandMax = new List<Vector3>();
            var stack = new Stack<int>();
            for (int g = 0; g < groups; g++)
            {
                if (binding.Island[g] >= 0)
                {
                    continue;
                }
                int island = islandMin.Count;
                Vector3 min = target.Positions[binding.First[g]];
                Vector3 max = min;
                binding.Island[g] = island;
                stack.Push(g);
                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    Vector3 p = target.Positions[binding.First[current]];
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    List<int> neighbours = binding.Neighbours[current];
                    if (neighbours == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        if (binding.Island[neighbours[i]] < 0)
                        {
                            binding.Island[neighbours[i]] = island;
                            stack.Push(neighbours[i]);
                        }
                    }
                }
                islandMin.Add(min);
                islandMax.Add(max);
            }
            binding.IslandCount = islandMin.Count;
            binding.IslandRigid = new bool[binding.IslandCount];
            if (settings.rigidSmallParts && binding.IslandCount > 1)
            {
                for (int i = 0; i < binding.IslandCount; i++)
                {
                    binding.IslandRigid[i] = (islandMax[i] - islandMin[i]).magnitude <= settings.rigidPartMaxSize;
                }
            }

            binding.RestNormals = new Vector3[groups];
            GroupNormals(target.Positions, null, target.Triangles, binding.Groups, binding.RestNormals);
            return binding;
        }

        private static void Link(List<int>[] neighbours, int a, int b)
        {
            if (a == b)
            {
                return;
            }
            List<int> listA = neighbours[a] ?? (neighbours[a] = new List<int>(6));
            if (!listA.Contains(b))
            {
                listA.Add(b);
            }
            List<int> listB = neighbours[b] ?? (neighbours[b] = new List<int>(6));
            if (!listB.Contains(a))
            {
                listB.Add(a);
            }
        }

        private static void TransferShape(UMABlendShapeSource source, UMABlendShapeSlotSpace target, Binding binding, UMABlendShapeSource.Shape shape,
            UMABlendShapeTransferSettings settings, UMABlendShapeTransferResult.ShapeResult result)
        {
            int count = target.VertexCount;
            int groups = binding.GroupCount;
            var frames = new UMABlendFrame[shape.Frames.Length];
            Vector3[] lastDeltas = null;
            float minimum = settings.minimumMovement * settings.minimumMovement;
            bool anyNormals = false;

            for (int f = 0; f < shape.Frames.Length; f++)
            {
                Vector3[] sourceDeltas = shape.Frames[f].Deltas;
                bool last = f == shape.Frames.Length - 1;
                // How the frame turns the source surface at each of its vertices.
                Vector3[] movedNormals = settings.method == UMABlendShapeTransferMethod.FollowSurface || settings.keepClearance || last
                    ? source.MovedNormals(sourceDeltas) : null;

                Quaternion[] turns = settings.method == UMABlendShapeTransferMethod.FollowSurface ? source.Turns(sourceDeltas) : null;

                var deltas = new Vector3[groups];
                var surfaceMove = new Vector3[groups];
                for (int g = 0; g < groups; g++)
                {
                    int triangle = binding.Triangle[g];
                    if (triangle < 0)
                    {
                        continue;
                    }
                    int a = source.Triangles[triangle * 3], b = source.Triangles[triangle * 3 + 1], c = source.Triangles[triangle * 3 + 2];
                    Vector3 bary = binding.Bary[g];
                    Vector3 move = sourceDeltas[a] * bary.x + sourceDeltas[b] * bary.y + sourceDeltas[c] * bary.z;
                    surfaceMove[g] = move;
                    float influence = binding.Influence[g];
                    if (influence <= 0f)
                    {
                        continue;
                    }
                    if (settings.method == UMABlendShapeTransferMethod.FollowSurface)
                    {
                        Quaternion turn = BlendRotations(turns[a], bary.x, turns[b], bary.y, turns[c], bary.z);
                        Vector3 offset = binding.Offset[g];
                        move += turn * offset - offset;
                    }
                    deltas[g] = move * (influence * settings.strength);
                }

                if (settings.rigidSmallParts)
                {
                    MoveRigidParts(binding, deltas);
                }
                Smooth(binding, deltas, settings.smoothingIterations, settings.smoothingStrength);

                // Where the vertex ends up relative to the moved source surface.
                int fixedCount = 0;
                bool[] under = last ? new bool[groups] : null;
                int poke = 0;
                if (movedNormals != null)
                {
                    for (int g = 0; g < groups; g++)
                    {
                        int triangle = binding.Triangle[g];
                        if (triangle < 0 || binding.Influence[g] <= 0f || binding.Clearance[g] <= 0f)
                        {
                            continue;
                        }
                        int a = source.Triangles[triangle * 3], b = source.Triangles[triangle * 3 + 1], c = source.Triangles[triangle * 3 + 2];
                        Vector3 bary = binding.Bary[g];
                        Vector3 normal = (movedNormals[a] * bary.x + movedNormals[b] * bary.y + movedNormals[c] * bary.z).normalized;
                        // The vertex relative to the moved surface point: its old offset plus how much more (or less) it moved.
                        float clearance = Vector3.Dot(binding.Offset[g] + deltas[g] - surfaceMove[g], normal);
                        if (settings.keepClearance && !binding.IslandRigid[binding.Island[g]])
                        {
                            float wanted = Mathf.Min(binding.Clearance[g], settings.clearance);
                            if (clearance < wanted)
                            {
                                deltas[g] += normal * (wanted - clearance);
                                clearance = wanted;
                                fixedCount++;
                            }
                        }
                        if (last && clearance < 0f)
                        {
                            under[g] = true;
                            poke++;
                        }
                    }
                }

                for (int g = 0; g < groups; g++)
                {
                    if (deltas[g].sqrMagnitude < minimum)
                    {
                        deltas[g] = Vector3.zero;
                    }
                }

                // Per vertex, in the slot's own mesh space.
                var frame = new UMABlendFrame { frameWeight = shape.Frames[f].Weight, deltaVertices = new Vector3[count] };
                var avatarDeltas = new Vector3[count];
                for (int v = 0; v < count; v++)
                {
                    Vector3 delta = deltas[binding.Groups[v]];
                    avatarDeltas[v] = delta;
                    if (delta != Vector3.zero)
                    {
                        frame.deltaVertices[v] = target.ToMeshSpace(v, delta);
                    }
                }

                if (settings.normals == UMABlendShapeNormalMode.Recalculate)
                {
                    anyNormals |= WriteNormalDeltas(target, binding, avatarDeltas, frame);
                }
                else
                {
                    frame.deltaNormals = new Vector3[0];
                    frame.deltaTangents = new Vector3[0];
                }
                frames[f] = frame;

                if (last)
                {
                    lastDeltas = avatarDeltas;
                    result.AvatarDeltas = avatarDeltas;
                    result.ClearanceFixed = fixedCount;
                    result.Under = new bool[count];
                    float max = 0f;
                    for (int v = 0; v < count; v++)
                    {
                        float sq = avatarDeltas[v].sqrMagnitude;
                        if (sq > 0f)
                        {
                            result.MovedVertices++;
                            max = Mathf.Max(max, sq);
                        }
                        if (under[binding.Groups[v]])
                        {
                            result.Under[v] = true;
                            result.PokeThrough++;
                        }
                    }
                    result.MaxMovement = Mathf.Sqrt(max);
                }
            }

            if (lastDeltas == null || result.MaxMovement <= settings.skipShapeBelow)
            {
                // Largest movement over every frame, not only the last.
                float max = result.MaxMovement;
                foreach (UMABlendFrame frame in frames)
                {
                    if (frame == null) continue;
                    for (int v = 0; v < count; v++)
                    {
                        float sq = target.ToAvatar[v].MultiplyVector(frame.deltaVertices[v]).sqrMagnitude;
                        if (sq > max * max)
                        {
                            max = Mathf.Sqrt(sq);
                        }
                    }
                }
                if (max <= settings.skipShapeBelow)
                {
                    result.Skipped = max <= 0f ? "it does not move this slot" : $"it moves this slot no further than {max * 1000f:0.00} mm";
                    return;
                }
            }
            if (!anyNormals)
            {
                foreach (UMABlendFrame frame in frames)
                {
                    frame.deltaNormals = new Vector3[0];
                    frame.deltaTangents = new Vector3[0];
                }
            }
            result.Shape = new UMABlendShape { shapeName = shape.Name, frames = frames };
        }

        /// <summary>Gives every vertex of a small separate piece the piece's average movement.</summary>
        private static void MoveRigidParts(Binding binding, Vector3[] deltas)
        {
            var sum = new Vector3[binding.IslandCount];
            var members = new int[binding.IslandCount];
            for (int g = 0; g < deltas.Length; g++)
            {
                int island = binding.Island[g];
                if (binding.IslandRigid[island])
                {
                    sum[island] += deltas[g];
                    members[island]++;
                }
            }
            for (int g = 0; g < deltas.Length; g++)
            {
                int island = binding.Island[g];
                if (binding.IslandRigid[island] && members[island] > 0)
                {
                    deltas[g] = sum[island] / members[island];
                }
            }
        }

        /// <summary>Moves each group's delta toward the average of its neighbours'. Rigid pieces are left as they are.</summary>
        private static void Smooth(Binding binding, Vector3[] deltas, int iterations, float strength)
        {
            if (iterations <= 0 || strength <= 0f)
            {
                return;
            }
            var next = new Vector3[deltas.Length];
            for (int pass = 0; pass < iterations; pass++)
            {
                for (int g = 0; g < deltas.Length; g++)
                {
                    List<int> neighbours = binding.Neighbours[g];
                    if (neighbours == null || neighbours.Count == 0 || binding.IslandRigid[binding.Island[g]])
                    {
                        next[g] = deltas[g];
                        continue;
                    }
                    Vector3 average = Vector3.zero;
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        average += deltas[neighbours[i]];
                    }
                    next[g] = Vector3.Lerp(deltas[g], average / neighbours.Count, strength);
                }
                Array.Copy(next, deltas, deltas.Length);
            }
        }

        /// <summary>
        /// Normal and tangent deltas from how the frame bends the slot's own surface: each vertex's
        /// authored normal is turned by the rotation between the smooth geometric normal before and
        /// after, so custom normals keep their character. Returns true when any normal changed.
        /// </summary>
        private static bool WriteNormalDeltas(UMABlendShapeSlotSpace target, Binding binding, Vector3[] avatarDeltas, UMABlendFrame frame)
        {
            int count = target.VertexCount;
            var moved = new Vector3[binding.GroupCount];
            GroupNormals(target.Positions, avatarDeltas, target.Triangles, binding.Groups, moved);
            frame.deltaNormals = new Vector3[count];
            Vector4[] tangents = target.Mesh != null ? target.Mesh.tangents : null;
            bool hasTangents = tangents != null && tangents.Length == count;
            frame.deltaTangents = hasTangents ? new Vector3[count] : new Vector3[0];
            bool any = false;
            for (int v = 0; v < count; v++)
            {
                int g = binding.Groups[v];
                Vector3 before = binding.RestNormals[g];
                Vector3 after = moved[g];
                if (before == after || before.sqrMagnitude < 0.5f || after.sqrMagnitude < 0.5f)
                {
                    continue;
                }
                Quaternion turn = Quaternion.FromToRotation(before, after);
                Vector3 normal = target.Normals[v];
                Vector3 normalDelta = turn * normal - normal;
                if (normalDelta.sqrMagnitude < 1e-10f)
                {
                    continue;
                }
                frame.deltaNormals[v] = target.ToMeshSpace(v, normalDelta);
                any = true;
                if (hasTangents)
                {
                    Vector3 tangent = target.ToAvatar[v].MultiplyVector(new Vector3(tangents[v].x, tangents[v].y, tangents[v].z)).normalized;
                    frame.deltaTangents[v] = target.ToMeshSpace(v, turn * tangent - tangent);
                }
            }
            return any;
        }

        /// <summary>
        /// Works out a frame's normal and tangent deltas from its vertex deltas, the way a transfer does:
        /// from how the frame bends the slot's own surface. Use it for blend shapes made or edited by
        /// other tools, so every shape on a slot is lit the same way. The frame's vertex deltas must be
        /// in the slot's own mesh space. Returns true when any normal changed; when none did the frame
        /// is left with empty normal and tangent arrays.
        /// </summary>
        public static bool RecalculateNormalDeltas(UMABlendShapeSlotSpace space, UMABlendFrame frame)
        {
            if (space == null || frame == null || frame.deltaVertices == null || frame.deltaVertices.Length != space.VertexCount)
            {
                return false;
            }
            int count = space.VertexCount;
            var avatarDeltas = new Vector3[count];
            for (int v = 0; v < count; v++)
            {
                if (frame.deltaVertices[v] != Vector3.zero)
                {
                    avatarDeltas[v] = space.ToAvatar[v].MultiplyVector(frame.deltaVertices[v]);
                }
            }
            var binding = new Binding();
            binding.Groups = WeldByPosition(space.Positions, WeldTolerance, out binding.GroupCount);
            binding.RestNormals = new Vector3[binding.GroupCount];
            GroupNormals(space.Positions, null, space.Triangles, binding.Groups, binding.RestNormals);
            bool any = WriteNormalDeltas(space, binding, avatarDeltas, frame);
            if (!any)
            {
                frame.deltaNormals = new Vector3[0];
                frame.deltaTangents = new Vector3[0];
            }
            return any;
        }

        /// <summary>
        /// A slot's blend shape with further movement added to it, for tools that edit a shape (sculpting
        /// on top of a transfer). <paramref name="addition"/> is in the slot's own mesh space and is added
        /// to the last frame in full and to earlier frames in proportion to their weight, so the shape
        /// still grows evenly. With no usable <paramref name="existing"/> shape the addition becomes a new
        /// one-frame shape. Normal and tangent deltas are worked out again when <paramref name="space"/>
        /// is given, and kept as they were otherwise. The existing shape is not modified.
        /// </summary>
        public static UMABlendShape AddToShape(UMABlendShape existing, string name, Vector3[] addition, UMABlendShapeSlotSpace space)
        {
            int count = addition.Length;
            UMABlendFrame[] frames;
            bool usable = existing != null && existing.frames != null && existing.frames.Length > 0;
            if (usable)
            {
                foreach (UMABlendFrame frame in existing.frames)
                {
                    usable &= frame != null && frame.deltaVertices != null && frame.deltaVertices.Length == count;
                }
            }
            if (usable)
            {
                frames = new UMABlendFrame[existing.frames.Length];
                float full = Mathf.Max(1e-4f, existing.frames[frames.Length - 1].frameWeight);
                for (int f = 0; f < frames.Length; f++)
                {
                    UMABlendFrame source = existing.frames[f];
                    float share = f == frames.Length - 1 ? 1f : Mathf.Clamp01(source.frameWeight / full);
                    var deltas = new Vector3[count];
                    for (int i = 0; i < count; i++)
                    {
                        deltas[i] = source.deltaVertices[i] + addition[i] * share;
                    }
                    frames[f] = new UMABlendFrame
                    {
                        frameWeight = source.frameWeight,
                        deltaVertices = deltas,
                        deltaNormals = source.deltaNormals != null ? (Vector3[])source.deltaNormals.Clone() : new Vector3[0],
                        deltaTangents = source.deltaTangents != null ? (Vector3[])source.deltaTangents.Clone() : new Vector3[0],
                    };
                }
            }
            else
            {
                frames = new[] { new UMABlendFrame { frameWeight = 100f, deltaVertices = (Vector3[])addition.Clone(), deltaNormals = new Vector3[0], deltaTangents = new Vector3[0] } };
            }
            if (space != null)
            {
                foreach (UMABlendFrame frame in frames)
                {
                    RecalculateNormalDeltas(space, frame);
                }
            }
            return new UMABlendShape { shapeName = name, frames = frames };
        }

        // --- geometry helpers --------------------------------------------------------------------------

        /// <summary>
        /// Groups vertices that lie within <paramref name="tolerance"/> of each other. Returns each
        /// vertex's group; groups are numbered from zero in order of first appearance.
        /// </summary>
        public static int[] WeldByPosition(Vector3[] positions, float tolerance, out int groupCount)
        {
            var groups = new int[positions.Length];
            var cells = new Dictionary<long, List<int>>();
            var representative = new List<int>();
            float cell = Mathf.Max(tolerance * 2f, 1e-7f);
            float toleranceSqr = tolerance * tolerance;
            for (int v = 0; v < positions.Length; v++)
            {
                Vector3 p = positions[v];
                int cx = Mathf.FloorToInt(p.x / cell), cy = Mathf.FloorToInt(p.y / cell), cz = Mathf.FloorToInt(p.z / cell);
                int found = -1;
                for (int x = -1; x <= 1 && found < 0; x++)
                {
                    for (int y = -1; y <= 1 && found < 0; y++)
                    {
                        for (int z = -1; z <= 1 && found < 0; z++)
                        {
                            if (!cells.TryGetValue(CellKey(cx + x, cy + y, cz + z), out List<int> list))
                            {
                                continue;
                            }
                            for (int i = 0; i < list.Count; i++)
                            {
                                if ((positions[representative[list[i]]] - p).sqrMagnitude <= toleranceSqr)
                                {
                                    found = list[i];
                                    break;
                                }
                            }
                        }
                    }
                }
                if (found < 0)
                {
                    found = representative.Count;
                    representative.Add(v);
                    long key = CellKey(cx, cy, cz);
                    if (!cells.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>(2);
                        cells.Add(key, list);
                    }
                    list.Add(found);
                }
                groups[v] = found;
            }
            groupCount = representative.Count;
            return groups;
        }

        private static long CellKey(int x, int y, int z)
        {
            unchecked
            {
                return ((long)x * 73856093L) ^ ((long)y * 19349663L << 20) ^ ((long)z * 83492791L << 40);
            }
        }

        /// <summary>
        /// Area-weighted geometric normal of every vertex group, with the vertices optionally moved by
        /// <paramref name="deltas"/>. A group no triangle touches gets a zero normal.
        /// </summary>
        public static void GroupNormals(Vector3[] positions, Vector3[] deltas, int[] triangles, int[] groups, Vector3[] result)
        {
            Array.Clear(result, 0, result.Length);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                Vector3 a = positions[ia], b = positions[ib], c = positions[ic];
                if (deltas != null)
                {
                    a += deltas[ia];
                    b += deltas[ib];
                    c += deltas[ic];
                }
                Vector3 n = Vector3.Cross(b - a, c - a);
                result[groups[ia]] += n;
                result[groups[ib]] += n;
                result[groups[ic]] += n;
            }
            for (int g = 0; g < result.Length; g++)
            {
                float magnitude = result[g].magnitude;
                result[g] = magnitude > 1e-20f ? result[g] / magnitude : Vector3.zero;
            }
        }

        /// <summary>Smooth geometric normals per vertex, shared by vertices at the same position.</summary>
        public static Vector3[] GeometricNormals(Vector3[] positions, int[] triangles)
        {
            int[] groups = WeldByPosition(positions, WeldTolerance, out int groupCount);
            var groupNormals = new Vector3[groupCount];
            GroupNormals(positions, null, triangles, groups, groupNormals);
            var normals = new Vector3[positions.Length];
            for (int v = 0; v < normals.Length; v++)
            {
                normals[v] = groupNormals[groups[v]];
            }
            return normals;
        }

        /// <summary>Barycentric weights of a point lying on triangle abc.</summary>
        public static Vector3 Barycentric(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = point - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-20f)
            {
                // A sliver: fall back to the nearest corner.
                float da = (point - a).sqrMagnitude, db = (point - b).sqrMagnitude, dc = (point - c).sqrMagnitude;
                return da <= db && da <= dc ? new Vector3(1f, 0f, 0f) : db <= dc ? new Vector3(0f, 1f, 0f) : new Vector3(0f, 0f, 1f);
            }
            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            v = Mathf.Clamp01(v);
            w = Mathf.Clamp01(w);
            float u = Mathf.Clamp01(1f - v - w);
            float sum = u + v + w;
            return new Vector3(u / sum, v / sum, w / sum);
        }

        private static Quaternion BlendRotations(Quaternion a, float wa, Quaternion b, float wb, Quaternion c, float wc)
        {
            if (Quaternion.Dot(a, b) < 0f)
            {
                b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            }
            if (Quaternion.Dot(a, c) < 0f)
            {
                c = new Quaternion(-c.x, -c.y, -c.z, -c.w);
            }
            var q = new Quaternion(a.x * wa + b.x * wb + c.x * wc, a.y * wa + b.y * wb + c.y * wc, a.z * wa + b.z * wb + c.z * wc, a.w * wa + b.w * wb + c.w * wc);
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return magnitude > 1e-8f ? new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude) : Quaternion.identity;
        }

        // --- writing to slots --------------------------------------------------------------------------

        /// <summary>
        /// Writes a transfer's blend shapes into a slot: the slot the transfer was made for, or a copy
        /// of it. Returns how many were added or replaced. The caller saves the asset.
        /// </summary>
        public static int Apply(UMABlendShapeTransferResult result, SlotDataAsset destination, UMABlendShapeExistingMode existing, List<string> log = null)
        {
            if (result == null || destination == null || UMAMeshData.IsNullOrEmptyMeshData(destination.meshData))
            {
                return 0;
            }
            UMAMeshData mesh = destination.meshData;
            int vertexCount = mesh.vertices != null ? mesh.vertices.Length : 0;
            var shapes = mesh.blendShapes != null ? new List<UMABlendShape>(mesh.blendShapes) : new List<UMABlendShape>();
            int written = 0;
            int added = 0, replaced = 0, kept = 0;
            foreach (UMABlendShapeTransferResult.ShapeResult shapeResult in result.Shapes)
            {
                UMABlendShape shape = shapeResult.Shape;
                if (shape == null)
                {
                    continue;
                }
                if (shape.frames == null || shape.frames.Length == 0)
                {
                    continue;
                }
                if (shape.frames[0].deltaVertices.Length != vertexCount)
                {
                    log?.Add($"'{shape.shapeName}' was made for {shape.frames[0].deltaVertices.Length} vertices and '{destination.slotName}' has {vertexCount}; not written.");
                    continue;
                }
                int index = shapes.FindIndex(s => s != null && s.shapeName == shape.shapeName);
                if (index >= 0)
                {
                    if (existing == UMABlendShapeExistingMode.Skip)
                    {
                        kept++;
                        continue;
                    }
                    shapes[index] = shape;
                    replaced++;
                }
                else
                {
                    shapes.Add(shape);
                    added++;
                }
                written++;
            }
            if (written > 0)
            {
                Undo.RecordObject(destination, "Transfer Blend Shapes");
                mesh.blendShapes = shapes.ToArray();
                mesh.InvalidateBuildData();
                EditorUtility.SetDirty(destination);
            }
            log?.Add($"{destination.slotName}: {added} added, {replaced} replaced" + (kept > 0 ? $", {kept} already there and kept" : string.Empty) + ".");
            return written;
        }

        /// <summary>Removes the named blend shapes from a slot. Returns how many were removed. The caller saves the asset.</summary>
        public static int RemoveShapes(SlotDataAsset slot, ICollection<string> names)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.blendShapes == null || names == null)
            {
                return 0;
            }
            var kept = new List<UMABlendShape>();
            foreach (UMABlendShape shape in slot.meshData.blendShapes)
            {
                if (shape == null || !names.Contains(shape.shapeName))
                {
                    kept.Add(shape);
                }
            }
            int removed = slot.meshData.blendShapes.Length - kept.Count;
            if (removed > 0)
            {
                Undo.RecordObject(slot, "Remove Blend Shapes");
                slot.meshData.blendShapes = kept.ToArray();
                slot.meshData.InvalidateBuildData();
                EditorUtility.SetDirty(slot);
            }
            return removed;
        }

        /// <summary>
        /// Saves a copy of a slot as a new asset, next to the original or in <paramref name="folder"/>,
        /// named with <paramref name="suffix"/>. A copy made earlier at the same path is written over,
        /// keeping its GUID.
        /// </summary>
        public static SlotDataAsset CreateCopy(SlotDataAsset slot, string suffix, string folder, bool addToLibrary)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }
            suffix = string.IsNullOrEmpty(suffix) ? "_Shapes" : suffix;
            string sourcePath = AssetDatabase.GetAssetPath(slot);
            string directory = !string.IsNullOrEmpty(folder)
                ? folder.Replace('\\', '/').TrimEnd('/')
                : (string.IsNullOrEmpty(sourcePath) ? "Assets" : Path.GetDirectoryName(sourcePath).Replace('\\', '/'));
            EnsureFolder(directory);
            string fileName = string.IsNullOrEmpty(sourcePath) ? slot.name : Path.GetFileNameWithoutExtension(sourcePath);
            string path = directory + "/" + fileName + suffix + ".asset";
            string logicalName = slot.slotName + suffix;

            SlotDataAsset copy = UnityEngine.Object.Instantiate(slot);
            copy.meshData = slot.meshData.DeepCopy();
            copy.meshData.SlotName = logicalName;
            copy.PrepareForAssetPath(path, logicalName);

            var existing = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
            SlotDataAsset result;
            if (existing != null && existing != slot && existing.slotName == logicalName)
            {
                UMAMeshData previous = existing.meshData;
                if (previous != null && previous.LoadedBoneweights)
                {
                    previous.FreeBoneWeights();
                }
                UMAMeshData copied = copy.meshData;
                EditorUtility.CopySerialized(copy, existing);
                existing.meshData = copied;
                UnityEngine.Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existing);
                result = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(path));
                result = copy;
            }
            if (addToLibrary && UMAAssetIndexer.Instance != null &&
                UMAAssetIndexer.Instance.GetAssetItem(typeof(SlotDataAsset), AssetItem.GetEvilName(result)) == null)
            {
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), result);
            }
            return result;
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
