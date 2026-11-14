using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Touchup Weights: vertices that are one point of the surface are weighted as one.
    /// </summary>
    /// <remarks>
    /// A mesh splits a point of its surface into several vertices wherever UVs, normals or materials
    /// change. Those copies sit at the same position and carry the same weights. If one of them is
    /// painted and another is not (it faces away, or it is hidden behind the first), the copies get
    /// different weights and the surface tears open along the split as soon as a bone moves.
    /// Touchup Weights therefore groups the vertices of the slot that are at the same position and
    /// have the same weights, and treats each group as a single vertex: selecting one selects them
    /// all, painting or smoothing one gives them all the same result, and they smooth against the
    /// neighbours of every copy. Vertices at the same position with different weights are not
    /// grouped: they were weighted apart on purpose, as on two layers of cloth.
    /// </remarks>
    public partial class VertexEditorStage
    {
        /// <summary>Vertices closer than this, in the slot's own space, are at the same position.</summary>
        private const float TouchupBuddyDistance = 0.0001f;

        // For each vertex of the slot, the other vertices that are the same point; null when it is alone.
        private int[][] touchupBuddies;

        /// <summary>How many vertices of the current slot share their position and weights with another.</summary>
        internal int TouchupBuddyVertexCount
        {
            get
            {
                if (touchupBuddies == null)
                {
                    return 0;
                }
                int count = 0;
                for (int i = 0; i < touchupBuddies.Length; i++)
                {
                    if (touchupBuddies[i] != null)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>The other vertices that are the same point as this one; null when it is alone.</summary>
        internal int[] GetTouchupBuddies(int vertexIndex)
        {
            return touchupBuddies != null && vertexIndex >= 0 && vertexIndex < touchupBuddies.Length
                ? touchupBuddies[vertexIndex]
                : null;
        }

        /// <summary>
        /// Groups the slot's vertices by position and weights, and gives every vertex of a group the
        /// connected vertices of all of them. Called when the paint data is built.
        /// </summary>
        private void BuildTouchupBuddies(UMAMeshData meshData)
        {
            touchupBuddies = null;
            Vector3[] vertices = meshData != null ? meshData.vertices : null;
            if (vertices == null || touchupAssetWeights == null || touchupAssetWeights.Length != meshData.vertexCount)
            {
                return;
            }
            int count = meshData.vertexCount;
            float cell = TouchupBuddyDistance * 2f;
            float limit = TouchupBuddyDistance * TouchupBuddyDistance;
            // The first vertex of each group stands for it; looked up by the cell it is in.
            var leadersByCell = new Dictionary<Vector3Int, List<int>>(count);
            var groupOf = new int[count];
            var members = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                Vector3 p = vertices[i];
                var home = new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                int leader = -1;
                for (int dx = -1; dx <= 1 && leader < 0; dx++)
                {
                    for (int dy = -1; dy <= 1 && leader < 0; dy++)
                    {
                        for (int dz = -1; dz <= 1 && leader < 0; dz++)
                        {
                            if (!leadersByCell.TryGetValue(new Vector3Int(home.x + dx, home.y + dy, home.z + dz), out List<int> leaders))
                            {
                                continue;
                            }
                            for (int k = 0; k < leaders.Count; k++)
                            {
                                int candidate = leaders[k];
                                if ((vertices[candidate] - p).sqrMagnitude <= limit &&
                                    BoneWeightListsApproximatelyEqual(touchupAssetWeights[candidate], touchupAssetWeights[i]))
                                {
                                    leader = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (leader < 0)
                {
                    groupOf[i] = i;
                    if (!leadersByCell.TryGetValue(home, out List<int> list))
                    {
                        list = new List<int>(1);
                        leadersByCell.Add(home, list);
                    }
                    list.Add(i);
                    continue;
                }
                groupOf[i] = leader;
                if (!members.TryGetValue(leader, out List<int> group))
                {
                    group = new List<int> { leader };
                    members.Add(leader, group);
                }
                group.Add(i);
            }
            if (members.Count == 0)
            {
                return;
            }

            touchupBuddies = new int[count][];
            foreach (List<int> group in members.Values)
            {
                // Every copy smooths against the neighbours of all the copies.
                HashSet<int> shared = null;
                if (touchupConnectedVertices != null && touchupConnectedVertices.Length == count)
                {
                    shared = new HashSet<int>();
                    foreach (int member in group)
                    {
                        int[] connected = touchupConnectedVertices[member];
                        if (connected != null)
                        {
                            shared.UnionWith(connected);
                        }
                    }
                    foreach (int member in group)
                    {
                        shared.Remove(member);
                    }
                }
                foreach (int member in group)
                {
                    var others = new int[group.Count - 1];
                    int write = 0;
                    foreach (int other in group)
                    {
                        if (other != member)
                        {
                            others[write++] = other;
                        }
                    }
                    touchupBuddies[member] = others;
                    if (shared != null)
                    {
                        var connected = new int[shared.Count];
                        shared.CopyTo(connected);
                        touchupConnectedVertices[member] = connected;
                    }
                }
            }
        }

        /// <summary>Adds to a set of the slot's vertices every vertex that is the same point as one in it.</summary>
        private void AddTouchupBuddies(HashSet<int> vertexIndices)
        {
            if (vertexIndices == null || vertexIndices.Count == 0 || !EnsureTouchupPaintData(out _) || touchupBuddies == null)
            {
                return;
            }
            List<int> added = null;
            foreach (int vertexIndex in vertexIndices)
            {
                int[] buddies = GetTouchupBuddies(vertexIndex);
                if (buddies == null)
                {
                    continue;
                }
                for (int i = 0; i < buddies.Length; i++)
                {
                    if (!vertexIndices.Contains(buddies[i]))
                    {
                        (added ?? (added = new List<int>())).Add(buddies[i]);
                    }
                }
            }
            if (added != null)
            {
                vertexIndices.UnionWith(added);
            }
        }

        /// <summary>
        /// A brush gave a vertex new weights: the vertices that are the same point get them too, and
        /// count as painted in this stroke.
        /// </summary>
        private void ApplyTouchupPaintToBuddies(int vertexIndex, List<BoneWeight1> paintedWeights, List<int> changedVertices)
        {
            int[] buddies = GetTouchupBuddies(vertexIndex);
            if (buddies == null)
            {
                return;
            }
            for (int i = 0; i < buddies.Length; i++)
            {
                int buddy = buddies[i];
                if (buddy < 0 || buddy >= touchupWorkingWeights.Length)
                {
                    continue;
                }
                selectionBrushedVerticesThisStroke.Add("weight:" + buddy);
                if (BoneWeightListsApproximatelyEqual(touchupWorkingWeights[buddy], paintedWeights))
                {
                    continue;
                }
                touchupWorkingWeights[buddy] = CloneBoneWeightList(paintedWeights);
                touchupPaintStrokeWeights[buddy] = CloneBoneWeightList(paintedWeights);
                if (!changedVertices.Contains(buddy))
                {
                    changedVertices.Add(buddy);
                }
            }
        }
    }
}
