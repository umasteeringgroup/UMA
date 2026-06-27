using System;
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

		internal void Add(ref Matrix4x4 sourceEffectivePose, ref Vector3 vertice, ref Vector3 normal, ref Vector4 tangent, float weight, int boneIndex)
		{
			if (count == 0)
			{
				tangentSign = tangent.w > 0;
			}
			var v = sourceEffectivePose.MultiplyPoint3x4(vertice);
			globalVertex.x += v.x * weight;
			globalVertex.y += v.y * weight;
			globalVertex.z += v.z * weight;
			v = sourceEffectivePose.MultiplyVector(normal);
			globalNormal.x += v.x * weight;
			globalNormal.y += v.y * weight;
			globalNormal.z += v.z * weight;
			v = sourceEffectivePose.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z));
			globalTangent.x += v.x * weight;
			globalTangent.y += v.y * weight;
			globalTangent.z += v.z * weight;

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
			vector.x = 0;
			vector.y = 0;
			vector.z = 0;
			for (int i = 0; i < count; i++)
			{
				var v = matrices[_idx[i]].MultiplyPoint3x4(globalVertex);
				float w = _w[i];
				vector.x += v.x * w;
				vector.y += v.y * w;
				vector.z += v.z * w;
			}
		}

		public void SkinNormal(Matrix4x4[] matrices, ref Vector3 normal)
		{
			normal.x = 0;
			normal.y = 0;
			normal.z = 0;
			for (int i = 0; i < count; i++)
			{
				var v = matrices[_idx[i]].MultiplyVector(globalNormal);
				float w = _w[i];
				normal.x += v.x * w;
				normal.y += v.y * w;
				normal.z += v.z * w;
			}
		}

		public void SkinTangent(Matrix4x4[] matrices, ref Vector4 tangent)
		{
			tangent.x = 0;
			tangent.y = 0;
			tangent.z = 0;
			for (int i = 0; i < count; i++)
			{
				var v = matrices[_idx[i]].MultiplyVector(globalTangent);
				float w = _w[i];
				tangent.x += v.x * w;
				tangent.y += v.y * w;
				tangent.z += v.z * w;
			}
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