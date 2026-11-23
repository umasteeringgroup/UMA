using System;
using System.Collections.Generic;
using System.IO;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Fit Existing Slot: binds a clothing slot to the body it was made on, then moves it with the body
    /// when the body changes shape. Each vertex keeps its body triangle, its place on it and its distance
    /// from it; a short relaxation with the cloth solver then removes kinks and keeps it out of the body.
    /// The slot's vertex order, UVs, weights and blendshapes are never changed.
    /// </summary>
    public sealed class SlotRefit : IDisposable
    {
        public SlotDataAsset Source;
        public bool IsBound;
        /// <summary>The slot's vertices as worn when bound (renderer space).</summary>
        public Vector3[] Bound;
        /// <summary>The slot's vertices on the current body.</summary>
        public Vector3[] Current;
        public int VertexCount;
        /// <summary>Vertices farther from the body than the binding distance: they keep their bound position.</summary>
        public int Unbound;
        public float BindingDistance = 0.15f;
        public string BodyRevision;
        /// <summary>How closely unskinning the bound shape gives back the slot's own vertices (m). Checked when binding.</summary>
        public float BindRoundTrip;
        public readonly List<string> Messages = new List<string>();

        private int[] triangle;
        private Vector3[] barycentric;
        private float[] offset;
        private int slotIndex = -1;
        private int[] slotTriangles;
        /// <summary>The slot's triangles, for drawing the refit.</summary>
        public int[] SourceTriangles => slotTriangles;
        private int[] weldGroup;

        /// <summary>The clothing slot's place in the stage character's combined mesh.</summary>
        private static bool FindWorn(VertexEditorStage stage, SlotDataAsset slot, out SlotData worn, out int index)
        {
            worn = null; index = -1;
            SlotData[] slots = stage.Character != null && stage.Character.umaData != null ? stage.Character.umaData.umaRecipe.slotDataList : null;
            if (slots == null) return false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].asset == slot) { worn = slots[i]; index = i; return true; }
            }
            return false;
        }

        private static Vector3[] WornVertices(VertexEditorStage stage, SlotData worn, int count)
        {
            SkinnedMeshRenderer renderer = stage.CharacterRenderer;
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, true);
                Vector3[] all = baked.vertices;
                var result = new Vector3[count];
                Array.Copy(all, worn.vertexOffset, result, 0, count);
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        /// <summary>The body with this slot left out, so the slot is not bound to itself.</summary>
        private BodyCollider BodyWithoutSlot(BodyReference body)
        {
            var kept = new List<UMASlotSkinTransfer.TriangleInfo>();
            foreach (UMASlotSkinTransfer.TriangleInfo t in body.Surface.Triangles) if (t.SlotIndex != slotIndex) kept.Add(t);
            return new BodyCollider(kept.ToArray());
        }

        /// <summary>Binds the slot as it is worn now. Call this before changing the body.</summary>
        public bool Bind(VertexEditorStage stage, BodyReference body, SlotDataAsset slot)
        {
            Messages.Clear();
            IsBound = false;
            Source = slot;
            if (slot == null || !FindWorn(stage, slot, out SlotData worn, out slotIndex))
            {
                Messages.Add("The slot is not on the character.");
                return false;
            }
            VertexCount = slot.meshData.vertexCount;
            Bound = WornVertices(stage, worn, VertexCount);
            Current = (Vector3[])Bound.Clone();
            slotTriangles = slot.meshData.submeshes != null && slot.meshData.submeshes.Length > 0 ? slot.meshData.submeshes[0].GetBaseTriangles() : new int[0];
            weldGroup = WeldByPosition(Bound);
            BodyCollider collider = BodyWithoutSlot(body);
            triangle = new int[VertexCount];
            barycentric = new Vector3[VertexCount];
            offset = new float[VertexCount];
            Unbound = 0;
            for (int v = 0; v < VertexCount; v++)
            {
                triangle[v] = -1;
                if (!Nearest(collider, Bound[v], out int t, out Vector3 point, out float d) || Mathf.Abs(d) > BindingDistance) { Unbound++; continue; }
                triangle[v] = t;
                barycentric[v] = UMASlotSkinTransfer.CalculateHitVertexBlend(point, collider.A[t], collider.B[t], collider.C[t]);
                offset[v] = d;
            }
            BodyRevision = body.Revision;
            IsBound = true;
            CurrentInBindSpace(stage, out BindRoundTrip);
            if (BindRoundTrip > 0.001f)
            {
                Messages.Add("This slot's mesh does not match how it is worn here (" + (BindRoundTrip * 1000f).ToString("0.0") + " mm apart): a refit saved from it may be offset. Was it converted from another rig?");
            }
            if (Unbound > 0) Messages.Add(Unbound + " vertices are more than " + Mathf.RoundToInt(BindingDistance * 100f) + " cm from the body and keep their place. They are drawn in magenta.");
            return true;
        }

        /// <summary>Like <see cref="BodyCollider.Closest"/> but searches further, for loose clothing.</summary>
        private static bool Nearest(BodyCollider collider, Vector3 p, out int triangle, out Vector3 point, out float signedDistance)
        {
            if (collider.Closest(p, out triangle, out point, out signedDistance)) return true;
            float best = float.MaxValue;
            triangle = -1;
            for (int t = 0; t < collider.TriangleCount; t++)
            {
                Vector3 q = UMASlotSkinTransfer.ClosestPointOnTriangle(p, collider.A[t], collider.B[t], collider.C[t]);
                float d = (q - p).sqrMagnitude;
                if (d < best) { best = d; triangle = t; point = q; }
            }
            if (triangle < 0) return false;
            signedDistance = Vector3.Dot(p - point, collider.N[triangle]);
            return true;
        }

        private static int[] WeldByPosition(Vector3[] positions)
        {
            var groups = new int[positions.Length];
            var first = new Dictionary<Vector3Int, int>();
            for (int v = 0; v < positions.Length; v++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(positions[v].x * 1e5f), Mathf.RoundToInt(positions[v].y * 1e5f), Mathf.RoundToInt(positions[v].z * 1e5f));
                if (first.TryGetValue(key, out int g)) groups[v] = g; else { first[key] = v; groups[v] = v; }
            }
            return groups;
        }

        /// <summary>Moves the slot onto the current body (after its shape changed), then relaxes it with the cloth solver.</summary>
        public void Conform(BodyReference body, int relaxFrames, float clearance)
        {
            if (!IsBound) return;
            BodyCollider collider = BodyWithoutSlot(body);
            Messages.Clear();
            var moved = new Vector3[VertexCount];
            for (int v = 0; v < VertexCount; v++)
            {
                int t = triangle[v];
                if (t < 0 || t >= collider.TriangleCount) { moved[v] = Bound[v]; continue; }
                moved[v] = collider.A[t] * barycentric[v].x + collider.B[t] * barycentric[v].y + collider.C[t] * barycentric[v].z + collider.N[t] * offset[v];
            }
            // Copies of one point (UV seams, slot edges) move together.
            var sum = new Dictionary<int, Vector3>();
            var count = new Dictionary<int, int>();
            for (int v = 0; v < VertexCount; v++)
            {
                sum[weldGroup[v]] = (sum.TryGetValue(weldGroup[v], out Vector3 s) ? s : Vector3.zero) + moved[v];
                count[weldGroup[v]] = (count.TryGetValue(weldGroup[v], out int c) ? c : 0) + 1;
            }
            for (int v = 0; v < VertexCount; v++) moved[v] = sum[weldGroup[v]] / count[weldGroup[v]];

            if (relaxFrames > 0)
            {
                // Relaxing only: a firm, damped cloth with no gravity, so the slot keeps its own shape.
                var fabric = new FabricSettings { preset = FabricPresets.Custom, stretchCompliance = 1e-6f, bendCompliance = 1e-4f, density = 0.2f, friction = 0.5f };
                ClothModel model = ClothModel.BuildFromMesh(Bound, slotTriangles, weldGroup, fabric);
                using (var solver = new ClothSolverCpu())
                {
                    solver.Initialize(model, collider, moved);
                    SimParams p = ClothModel.Params(8f, clearance, 0f, 0f, Vector3.zero, false);
                    solver.Step(p, relaxFrames);
                    solver.GetPositions(moved);
                }
            }
            Current = moved;
            if (BodyRevision == body.Revision) Messages.Add("The body has not changed since binding.");
        }

        /// <summary>
        /// The current shape in the slot's own bind space: each vertex is unskinned with its own weights
        /// through the character's current skeleton.
        /// </summary>
        /// <remarks><paramref name="roundTripError"/> compares the bound shape, unskinned, with the slot's own vertices; it is only meaningful on the body the slot was bound on.</remarks>
        public Vector3[] CurrentInBindSpace(VertexEditorStage stage, out float roundTripError)
        {
            roundTripError = 0f;
            FindWorn(stage, Source, out SlotData worn, out _);
            SkinnedMeshRenderer renderer = stage.CharacterRenderer;
            Mesh shared = renderer.sharedMesh;
            var bonesPerVertex = shared.GetBonesPerVertex();
            var weights = shared.GetAllBoneWeights();
            Matrix4x4[] bindposes = shared.bindposes;
            Transform[] bones = renderer.bones;
            Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;
            var skin = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++) skin[b] = toLocal * bones[b].localToWorldMatrix * bindposes[b];
            var offsets = new int[bonesPerVertex.Length + 1];
            for (int v = 0; v < bonesPerVertex.Length; v++) offsets[v + 1] = offsets[v] + bonesPerVertex[v];
            var result = new Vector3[VertexCount];
            for (int v = 0; v < VertexCount; v++)
            {
                int cv = worn.vertexOffset + v;
                var m = new Matrix4x4();
                for (int k = offsets[cv]; k < offsets[cv + 1]; k++)
                {
                    BoneWeight1 w = weights[k];
                    for (int e = 0; e < 16; e++) m[e] += skin[w.boneIndex][e] * w.weight;
                }
                Matrix4x4 inv = m.inverse;
                result[v] = inv.MultiplyPoint3x4(Current[v]);
                Vector3 original = inv.MultiplyPoint3x4(Bound[v]);
                roundTripError = Mathf.Max(roundTripError, (original - Source.meshData.vertices[v]).magnitude);
            }
            return result;
        }

        /// <summary>Saves the refitted shape as a new slot beside the original. Returns its path.</summary>
        public string SaveAsNewSlot(VertexEditorStage stage, string suffix)
        {
            Vector3[] bind = CurrentInBindSpace(stage, out _);
            SlotDataAsset copy = UnityEngine.Object.Instantiate(Source);
            Vector3[] beforeNormals = Source.meshData.normals;
            copy.meshData.vertices = bind;
            if (beforeNormals != null && beforeNormals.Length == bind.Length)
            {
                var normals = (Vector3[])beforeNormals.Clone();
                new UMABuddyNormals(Source.meshData.vertices, slotTriangles).Follow(Source.meshData.vertices, bind, beforeNormals, normals);
                copy.meshData.normals = normals;
            }
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(Source)).Replace('\\', '/');
            string name = Source.slotName + "_" + suffix;
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + "_slot.asset");
            copy.PrepareForAssetPath(path, name);
            copy.meshData.SlotName = name;
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssetIfDirty(copy);
            return path;
        }

        /// <summary>Saves the refit as a blendshape on a copy of the slot (the original asset is not changed).</summary>
        public string SaveAsBlendshape(VertexEditorStage stage, string shapeName)
        {
            Vector3[] bind = CurrentInBindSpace(stage, out _);
            SlotDataAsset copy = UnityEngine.Object.Instantiate(Source);
            var frame = new UMABlendFrame(VertexCount, false, false);
            for (int v = 0; v < VertexCount; v++) frame.deltaVertices[v] = bind[v] - Source.meshData.vertices[v];
            var shapes = new List<UMABlendShape>(copy.meshData.blendShapes ?? new UMABlendShape[0]);
            shapes.RemoveAll(s => s != null && s.shapeName == shapeName);
            shapes.Add(new UMABlendShape { shapeName = shapeName, frames = new[] { frame } });
            copy.meshData.blendShapes = shapes.ToArray();
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(Source)).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Source.slotName + "_WithShapes_slot.asset");
            copy.PrepareForAssetPath(path, Source.slotName + "_WithShapes");
            copy.meshData.SlotName = Source.slotName + "_WithShapes";
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssetIfDirty(copy);
            return path;
        }

        public void Dispose()
        {
            IsBound = false;
        }
    }
}
