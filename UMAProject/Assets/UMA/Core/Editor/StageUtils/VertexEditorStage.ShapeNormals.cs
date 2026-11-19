using System;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Normals after the shape changes. Vertices at one position ("battle buddies": the copies at a UV
    /// seam or where two slots connect) always end with one normal, and only the normals around
    /// vertices that moved are touched. See <see cref="UMABuddyNormals"/>.
    /// </summary>
    /// <remarks>
    /// The normals are not worked out afresh: where the faces around a position turned, its normal
    /// turns by the same amount. The slot's own normals and normals set with the normals tools are
    /// therefore kept, and a stroke leaves no edge in the shading where it stopped. A vertex of a slot
    /// that is not editable keeps its normal, and its buddies in editable slots take that normal, so
    /// what is shown is what the saved slots give.
    /// </remarks>
    public partial class VertexEditorStage
    {
        private UMABuddyNormals buddyNormals;
        private Mesh buddyNormalsMesh;
        private int[] buddyNormalsTriangles;
        private Vector3[] shapeStrokeStartPositions;
        private Vector3[] shapeStrokeStartNormals;
        private bool[] shapeStrokeLocks;

        /// <summary>Which baked vertices are buddies, and the faces around each. Built once for a baked mesh.</summary>
        private UMABuddyNormals GetBuddyNormals()
        {
            int[] triangles = GetBakedTriangles();
            if (buddyNormals == null || !ReferenceEquals(buddyNormalsMesh, BakedMesh) ||
                !ReferenceEquals(buddyNormalsTriangles, triangles) || buddyNormals.VertexCount != BakedMesh.vertexCount)
            {
                buddyNormals = new UMABuddyNormals(BakedMesh.vertices, triangles);
                buddyNormalsMesh = BakedMesh;
                buddyNormalsTriangles = triangles;
            }
            return buddyNormals;
        }

        /// <summary>True for every baked vertex of a slot that is not editable: its normal is not ours to change.</summary>
        private bool[] BuildNormalLocks()
        {
            var locked = new bool[BakedMesh.vertexCount];
            for (int i = 0; i < locked.Length; i++)
            {
                locked[i] = true;
            }
            foreach (SlotData slot in GetEditableSlots())
            {
                if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }
                int count = slot.asset.meshData.vertexCount;
                int start = GetVisibleBakedVertexIndex(slot, 0);
                if (start < 0 || start + count > locked.Length)
                {
                    continue;
                }
                Array.Clear(locked, start, count);
            }
            return locked;
        }

        /// <summary>Remembers the mesh as a shape stroke starts, for <see cref="FollowShapeStroke"/>.</summary>
        private void CaptureShapeStrokeStart(bool shapeStroke)
        {
            if (!shapeStroke || BakedMesh == null)
            {
                shapeStrokeStartPositions = null;
                shapeStrokeStartNormals = null;
                shapeStrokeLocks = null;
                return;
            }
            // Buddies are found before anything moves.
            GetBuddyNormals();
            shapeStrokeStartPositions = BakedMesh.vertices;
            shapeStrokeStartNormals = BakedMesh.normals;
            shapeStrokeLocks = BuildNormalLocks();
        }

        /// <summary>Normals for the stroke so far. <paramref name="vertices"/> may be null to read them from the mesh.</summary>
        private void FollowShapeStroke(Vector3[] vertices)
        {
            if (shapeStrokeStartPositions == null)
            {
                return;
            }
            FollowShape(shapeStrokeStartPositions, shapeStrokeStartNormals, vertices ?? BakedMesh.vertices, shapeStrokeLocks);
        }

        /// <summary>Puts on the baked mesh the normals that follow a change of shape from <paramref name="before"/> to <paramref name="after"/>.</summary>
        private void FollowShape(Vector3[] before, Vector3[] beforeNormals, Vector3[] after, bool[] locked = null)
        {
            if (BakedMesh == null || before == null || beforeNormals == null || after == null)
            {
                return;
            }
            int count = BakedMesh.vertexCount;
            if (before.Length != count || beforeNormals.Length != count || after.Length != count)
            {
                return;
            }
            var normals = (Vector3[])beforeNormals.Clone();
            GetBuddyNormals().Follow(before, after, beforeNormals, normals, locked ?? BuildNormalLocks());
            BakedMesh.normals = normals;
        }
    }
}
