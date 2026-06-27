using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
#if true
	internal struct SkinningSolver
	{
		// Fixed-capacity arrays (no heap allocations per vertex, no List<T> overhead)
		float[] _w;      // bone weights per influence
		int[] _idx;      // bone indices per influence
		int count;       // number of influences this vertex
		private Vector3 globalNormal;
		private Vector3 globalTangent;
		private bool tangentSign;
		public Vector3 globalVertex;

		public int boneWeights { get { return count; } }

		public void Allocate()
		{
			_w = new float[8];
			_idx = new int[8];
		}

		public void Reset()
		{
			count = 0;
			globalNormal = Vector3.zero;
			globalVertex = Vector3.zero;
			globalTangent = Vector3.zero;

			// Lazy-init arrays (struct fields default to null)
			if (_w == null)
			{
				_w = new float[8];
				_idx = new int[8];
			}
		}

		internal void Add(ref Matrix4x4 m, ref Vector3 vert, ref Vector3 norm, ref Vector4 tan, float weight, int boneIndex)
		{
			if (count == 0) { tangentSign = tan.w > 0; }

			// Inline MultiplyPoint3x4 (avoids Unity engine call overhead)
			float vx = m.m00 * vert.x + m.m01 * vert.y + m.m02 * vert.z + m.m03;
			float vy = m.m10 * vert.x + m.m11 * vert.y + m.m12 * vert.z + m.m13;
			float vz = m.m20 * vert.x + m.m21 * vert.y + m.m22 * vert.z + m.m23;
			globalVertex.x += vx * weight;
			globalVertex.y += vy * weight;
			globalVertex.z += vz * weight;

			// Inline MultiplyVector for normal (no translation)
			float nx = m.m00 * norm.x + m.m01 * norm.y + m.m02 * norm.z;
			float ny = m.m10 * norm.x + m.m11 * norm.y + m.m12 * norm.z;
			float nz = m.m20 * norm.x + m.m21 * norm.y + m.m22 * norm.z;
			globalNormal.x += nx * weight;
			globalNormal.y += ny * weight;
			globalNormal.z += nz * weight;

			// Inline MultiplyVector for tangent direction
			float tx = tan.x, ty = tan.y, tz = tan.z;
			float rtx = m.m00 * tx + m.m01 * ty + m.m02 * tz;
			float rty = m.m10 * tx + m.m11 * ty + m.m12 * tz;
			float rtz = m.m20 * tx + m.m21 * ty + m.m22 * tz;
			globalTangent.x += rtx * weight;
			globalTangent.y += rty * weight;
			globalTangent.z += rtz * weight;

			// Check if this bone index already has a weight
			for (int i = 0; i < count; i++)
			{
				if (_idx[i] == boneIndex)
				{
					_w[i] += weight;
					return;
				}
			}

			// Expand array if needed (rare: typically ≤8 influences)
			if (count >= _w.Length)
			{
				int newCap = _w.Length * 2;
				Array.Resize(ref _w, newCap);
				Array.Resize(ref _idx, newCap);
			}

			_w[count] = weight;
			_idx[count] = boneIndex;
			count++;
		}

		/// <summary>
		/// Sort bone weights in descending order (Unity requirement).
		/// </summary>
		public void SortWeightsDescending()
		{
			if (count <= 1) return;
			// In-place selection sort (alloc-free, fast for n ≤ 8)
			for (int i = 0; i < count - 1; i++)
			{
				int best = i;
				for (int j = i + 1; j < count; j++)
					if (_w[j] > _w[best]) best = j;
				if (best != i)
				{
					float tw = _w[i]; _w[i] = _w[best]; _w[best] = tw;
					int ti = _idx[i]; _idx[i] = _idx[best]; _idx[best] = ti;
				}
			}
		}

		public void SkinVertex(Matrix4x4[] matrices, ref Vector3 vector)
		{
			float gx = globalVertex.x, gy = globalVertex.y, gz = globalVertex.z;
			float rx = 0, ry = 0, rz = 0;
			for (int i = 0; i < count; i++)
			{
				var m = matrices[_idx[i]];
				float w = _w[i];
				// Inline MultiplyPoint3x4
				rx += (m.m00 * gx + m.m01 * gy + m.m02 * gz + m.m03) * w;
				ry += (m.m10 * gx + m.m11 * gy + m.m12 * gz + m.m13) * w;
				rz += (m.m20 * gx + m.m21 * gy + m.m22 * gz + m.m23) * w;
			}
			vector.x = rx; vector.y = ry; vector.z = rz;
		}

		public void SkinNormal(Matrix4x4[] matrices, ref Vector3 normal)
		{
			float gx = globalNormal.x, gy = globalNormal.y, gz = globalNormal.z;
			float rx = 0, ry = 0, rz = 0;
			for (int i = 0; i < count; i++)
			{
				var m = matrices[_idx[i]];
				float w = _w[i];
				// Inline MultiplyVector (no translation)
				rx += (m.m00 * gx + m.m01 * gy + m.m02 * gz) * w;
				ry += (m.m10 * gx + m.m11 * gy + m.m12 * gz) * w;
				rz += (m.m20 * gx + m.m21 * gy + m.m22 * gz) * w;
			}
			normal.x = rx; normal.y = ry; normal.z = rz;
		}

		public void SkinTangent(Matrix4x4[] matrices, ref Vector4 tangent)
		{
			float gx = globalTangent.x, gy = globalTangent.y, gz = globalTangent.z;
			float rx = 0, ry = 0, rz = 0;
			for (int i = 0; i < count; i++)
			{
				var m = matrices[_idx[i]];
				float w = _w[i];
				// Inline MultiplyVector (no translation)
				rx += (m.m00 * gx + m.m01 * gy + m.m02 * gz) * w;
				ry += (m.m10 * gx + m.m11 * gy + m.m12 * gz) * w;
				rz += (m.m20 * gx + m.m21 * gy + m.m22 * gz) * w;
			}
			tangent.x = rx; tangent.y = ry; tangent.z = rz;
			tangent.w = tangentSign ? 1f : -1f;
		}

		public void UpdateBoneWeights(ref BoneWeight boneWeight)
		{
			int n = count;
			if (n < 1) { boneWeight.weight0 = 0; boneWeight.boneIndex0 = 0; }
			else { boneWeight.weight0 = _w[0]; boneWeight.boneIndex0 = _idx[0]; }
			if (n < 2) { boneWeight.weight1 = 0; boneWeight.boneIndex1 = 0; }
			else { boneWeight.weight1 = _w[1]; boneWeight.boneIndex1 = _idx[1]; }
			if (n < 3) { boneWeight.weight2 = 0; boneWeight.boneIndex2 = 0; }
			else { boneWeight.weight2 = _w[2]; boneWeight.boneIndex2 = _idx[2]; }
			if (n < 4) { boneWeight.weight3 = 0; boneWeight.boneIndex3 = 0; }
			else { boneWeight.weight3 = _w[3]; boneWeight.boneIndex3 = _idx[3]; }
		}

		/// <summary>
		/// Append all retargeted bone weights to the output list and return the count for this vertex.
		/// </summary>
		public byte UpdateBoneWeights(List<BoneWeight1> outWeights)
		{
			byte written = 0;
			for (int i = 0; i < count; i++)
			{
				float w = _w[i];
				if (w <= 0f) continue;
				outWeights.Add(new BoneWeight1 { boneIndex = _idx[i], weight = w });
				written++;
			}
			return written;
		}

		internal void DebugVertex()
		{
			//Debug.Log(globalVertex);
			Debug.DrawLine(globalVertex, globalVertex+globalNormal*0.1f, Color.red, 10000);
		}
	}
#endif
}