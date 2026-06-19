#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Editor-only helper that builds a single covering surface over a set of metaball
	/// centers using a voxel scalar field and marching cubes. Used by
	/// <see cref="UMASlotNormalNormalizer"/> to create volume-based normals for hair cards.
	/// Implemented from the standard public marching-cubes tables (Paul Bourke).
	/// </summary>
	public static class UMAMetaballMesher
	{
		/// <summary>
		/// Holds the voxel scalar field produced when building a covering mesh so that
		/// normals can be re-sampled later when projecting onto the hair mesh.
		/// </summary>
		public class ScalarField
		{
			public float[] Values;
			public int SizeX;
			public int SizeY;
			public int SizeZ;
			public Vector3 Origin;
			public float VoxelSize;
			public float Iso;

			public int Index(int x, int y, int z)
			{
				return x + SizeX * (y + SizeY * z);
			}

			public float Sample(int x, int y, int z)
			{
				if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ)
				{
					return 0f;
				}
				return Values[Index(x, y, z)];
			}

			/// <summary>Trilinearly samples the scalar field value at a world-space position.</summary>
			public float SampleValue(Vector3 worldPos)
			{
				Vector3 local = (worldPos - Origin) / VoxelSize;
				int x0 = Mathf.FloorToInt(local.x);
				int y0 = Mathf.FloorToInt(local.y);
				int z0 = Mathf.FloorToInt(local.z);
				float fx = local.x - x0;
				float fy = local.y - y0;
				float fz = local.z - z0;

				float c000 = Sample(x0, y0, z0);
				float c100 = Sample(x0 + 1, y0, z0);
				float c010 = Sample(x0, y0 + 1, z0);
				float c110 = Sample(x0 + 1, y0 + 1, z0);
				float c001 = Sample(x0, y0, z0 + 1);
				float c101 = Sample(x0 + 1, y0, z0 + 1);
				float c011 = Sample(x0, y0 + 1, z0 + 1);
				float c111 = Sample(x0 + 1, y0 + 1, z0 + 1);

				float x00 = Mathf.Lerp(c000, c100, fx);
				float x10 = Mathf.Lerp(c010, c110, fx);
				float x01 = Mathf.Lerp(c001, c101, fx);
				float x11 = Mathf.Lerp(c011, c111, fx);
				float y0v = Mathf.Lerp(x00, x10, fy);
				float y1v = Mathf.Lerp(x01, x11, fy);
				return Mathf.Lerp(y0v, y1v, fz);
			}

			/// <summary>
			/// Returns the (outward) field gradient direction at a world-space position via
			/// trilinear sampling of central differences. The field increases toward ball
			/// centers, so the negated gradient points outward from the volume.
			/// </summary>
			public Vector3 SampleOutwardNormal(Vector3 worldPos)
			{
				Vector3 g = SampleGradient(worldPos);
				// Field grows toward centers; outward surface normal is the negative gradient.
				Vector3 n = -g;
				if (n.sqrMagnitude < 1e-12f)
				{
					return Vector3.zero;
				}
				return n.normalized;
			}

			private Vector3 SampleGradient(Vector3 worldPos)
			{
				Vector3 local = (worldPos - Origin) / VoxelSize;
				int x = Mathf.Clamp(Mathf.RoundToInt(local.x), 1, SizeX - 2);
				int y = Mathf.Clamp(Mathf.RoundToInt(local.y), 1, SizeY - 2);
				int z = Mathf.Clamp(Mathf.RoundToInt(local.z), 1, SizeZ - 2);

				float dx = Sample(x + 1, y, z) - Sample(x - 1, y, z);
				float dy = Sample(x, y + 1, z) - Sample(x, y - 1, z);
				float dz = Sample(x, y, z + 1) - Sample(x, y, z - 1);
				return new Vector3(dx, dy, dz);
			}
		}

		/// <summary>
		/// Builds a covering mesh over the supplied metaball centers.
		/// </summary>
		/// <param name="centers">Metaball centers in the same space the resulting mesh should use.</param>
		/// <param name="bounds">Bounds of the source geometry (used to size the voxel grid).</param>
		/// <param name="radius">Metaball influence radius.</param>
		/// <param name="resolution">Number of voxels along the longest grid axis.</param>
		/// <param name="blurIterations">Separable blur passes applied to the field (smoothness).</param>
		/// <param name="field">Receives the scalar field so normals can be re-sampled later.</param>
		public static Mesh Build(IList<Vector3> centers, Bounds bounds, float radius, int resolution, int blurIterations, out ScalarField field)
		{
			field = null;
			if (centers == null || centers.Count == 0 || radius <= 0f)
			{
				return null;
			}

			resolution = Mathf.Clamp(resolution, 8, 256);

			// Pad the grid so the iso-surface (which sits ~radius outside each center) is contained.
			float pad = radius * 1.5f;
			Bounds grid = bounds;
			grid.Expand(pad * 2f);

			Vector3 size = grid.size;
			float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
			if (maxDim <= 0f)
			{
				return null;
			}

			float voxelSize = maxDim / resolution;
			if (voxelSize <= 0f)
			{
				return null;
			}

			int sx = Mathf.Max(2, Mathf.CeilToInt(size.x / voxelSize) + 1);
			int sy = Mathf.Max(2, Mathf.CeilToInt(size.y / voxelSize) + 1);
			int sz = Mathf.Max(2, Mathf.CeilToInt(size.z / voxelSize) + 1);

			// Guard against pathological memory use.
			long voxelCount = (long)sx * sy * sz;
			if (voxelCount > 16_000_000L)
			{
				Debug.LogWarning($"[UMAMetaballMesher] Voxel grid too large ({sx}x{sy}x{sz}); lower the resolution or increase metaball size.");
				return null;
			}

			Vector3 origin = grid.min;
			field = new ScalarField
			{
				Values = new float[sx * sy * sz],
				SizeX = sx,
				SizeY = sy,
				SizeZ = sz,
				Origin = origin,
				VoxelSize = voxelSize
			};

			SplatMetaballs(field, centers, radius);

			for (int i = 0; i < blurIterations; i++)
			{
				BlurSeparable(field);
			}

			const float iso = 0.5f;
			field.Iso = iso;
			Mesh mesh = MarchingCubes(field, iso);
			return mesh;
		}

		private static void SplatMetaballs(ScalarField field, IList<Vector3> centers, float radius)
		{
			float voxelSize = field.VoxelSize;
			float invR = 1f / radius;
			int reach = Mathf.CeilToInt(radius / voxelSize) + 1;

			for (int c = 0; c < centers.Count; c++)
			{
				Vector3 p = centers[c];
				Vector3 local = (p - field.Origin) / voxelSize;
				int cx = Mathf.RoundToInt(local.x);
				int cy = Mathf.RoundToInt(local.y);
				int cz = Mathf.RoundToInt(local.z);

				int minX = Mathf.Max(0, cx - reach);
				int maxX = Mathf.Min(field.SizeX - 1, cx + reach);
				int minY = Mathf.Max(0, cy - reach);
				int maxY = Mathf.Min(field.SizeY - 1, cy + reach);
				int minZ = Mathf.Max(0, cz - reach);
				int maxZ = Mathf.Min(field.SizeZ - 1, cz + reach);

				for (int z = minZ; z <= maxZ; z++)
				{
					for (int y = minY; y <= maxY; y++)
					{
						for (int x = minX; x <= maxX; x++)
						{
							Vector3 voxelWorld = field.Origin + new Vector3(x, y, z) * voxelSize;
							float dist = Vector3.Distance(voxelWorld, p);
							if (dist >= radius)
							{
								continue;
							}

							// Wyvill falloff: smooth, finite-support metaball contribution.
							float t = dist * invR;
							float t2 = t * t;
							float falloff = (1f - t2);
							falloff = falloff * falloff * falloff;
							field.Values[field.Index(x, y, z)] += falloff;
						}
					}
				}
			}
		}

		private static void BlurSeparable(ScalarField field)
		{
			int sx = field.SizeX;
			int sy = field.SizeY;
			int sz = field.SizeZ;
			float[] src = field.Values;
			float[] tmp = new float[src.Length];

			// X
			for (int z = 0; z < sz; z++)
			{
				for (int y = 0; y < sy; y++)
				{
					for (int x = 0; x < sx; x++)
					{
						float a = field.Sample(x - 1, y, z);
						float b = src[field.Index(x, y, z)];
						float c = field.Sample(x + 1, y, z);
						tmp[field.Index(x, y, z)] = (a + 4f * b + c) / 6f;
					}
				}
			}
			System.Array.Copy(tmp, src, src.Length);

			// Y
			for (int z = 0; z < sz; z++)
			{
				for (int y = 0; y < sy; y++)
				{
					for (int x = 0; x < sx; x++)
					{
						float a = field.Sample(x, y - 1, z);
						float b = src[field.Index(x, y, z)];
						float c = field.Sample(x, y + 1, z);
						tmp[field.Index(x, y, z)] = (a + 4f * b + c) / 6f;
					}
				}
			}
			System.Array.Copy(tmp, src, src.Length);

			// Z
			for (int z = 0; z < sz; z++)
			{
				for (int y = 0; y < sy; y++)
				{
					for (int x = 0; x < sx; x++)
					{
						float a = field.Sample(x, y, z - 1);
						float b = src[field.Index(x, y, z)];
						float c = field.Sample(x, y, z + 1);
						tmp[field.Index(x, y, z)] = (a + 4f * b + c) / 6f;
					}
				}
			}
			System.Array.Copy(tmp, src, src.Length);
		}

		private static Mesh MarchingCubes(ScalarField field, float iso)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			Dictionary<long, int> edgeCache = new Dictionary<long, int>();

			int sx = field.SizeX;
			int sy = field.SizeY;
			int sz = field.SizeZ;
			float voxelSize = field.VoxelSize;
			Vector3 origin = field.Origin;

			float[] cube = new float[8];
			Vector3[] cubePos = new Vector3[8];

			for (int z = 0; z < sz - 1; z++)
			{
				for (int y = 0; y < sy - 1; y++)
				{
					for (int x = 0; x < sx - 1; x++)
					{
						for (int i = 0; i < 8; i++)
						{
							int cx = x + CornerOffset[i, 0];
							int cy = y + CornerOffset[i, 1];
							int cz = z + CornerOffset[i, 2];
							cube[i] = field.Values[field.Index(cx, cy, cz)];
							cubePos[i] = origin + new Vector3(cx, cy, cz) * voxelSize;
						}

						int cubeIndex = 0;
						for (int i = 0; i < 8; i++)
						{
							if (cube[i] > iso)
							{
								cubeIndex |= 1 << i;
							}
						}

						int edges = EdgeTable[cubeIndex];
						if (edges == 0)
						{
							continue;
						}

						int[] edgeVertex = new int[12];
						for (int e = 0; e < 12; e++)
						{
							if ((edges & (1 << e)) == 0)
							{
								continue;
							}

							int a = EdgeConnection[e, 0];
							int b = EdgeConnection[e, 1];

							long key = EdgeKey(x, y, z, e);
							if (!edgeCache.TryGetValue(key, out int vi))
							{
								float t = (iso - cube[a]) / (cube[b] - cube[a]);
								Vector3 pos = Vector3.Lerp(cubePos[a], cubePos[b], t);
								vi = vertices.Count;
								vertices.Add(pos);
								edgeCache[key] = vi;
							}
							edgeVertex[e] = vi;
						}

						for (int t = 0; TriTable[cubeIndex, t] != -1; t += 3)
						{
							triangles.Add(edgeVertex[TriTable[cubeIndex, t]]);
							triangles.Add(edgeVertex[TriTable[cubeIndex, t + 1]]);
							triangles.Add(edgeVertex[TriTable[cubeIndex, t + 2]]);
						}
					}
				}
			}

			if (vertices.Count == 0 || triangles.Count == 0)
			{
				return null;
			}

			Mesh mesh = new Mesh();
			if (vertices.Count > 65535)
			{
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);

			// Gradient-based normals from the field (volume normals).
			Vector3[] normals = new Vector3[vertices.Count];
			for (int i = 0; i < vertices.Count; i++)
			{
				Vector3 n = field.SampleOutwardNormal(vertices[i]);
				normals[i] = n.sqrMagnitude > 1e-10f ? n : Vector3.up;
			}
			mesh.normals = normals;
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>
		/// Finds the closest point on the supplied mesh triangles to <paramref name="query"/> and
		/// returns the barycentric-interpolated vertex normal at that point. The covering mesh's
		/// normals already point outward (away from the metaball volume), so the result is an
		/// outward, volume-based normal. Returns false if the mesh has no triangles.
		/// </summary>
		public static bool ClosestPointNormal(Vector3[] vertices, Vector3[] normals, int[] triangles, Vector3 query, out Vector3 normal, out Vector3 closestPoint)
		{
			normal = Vector3.zero;
			closestPoint = query;
			if (vertices == null || triangles == null || triangles.Length < 3)
			{
				return false;
			}

			float bestSqr = float.MaxValue;
			int bestTri = -1;
			Vector3 bestPoint = query;

			for (int t = 0; t < triangles.Length; t += 3)
			{
				Vector3 a = vertices[triangles[t]];
				Vector3 b = vertices[triangles[t + 1]];
				Vector3 c = vertices[triangles[t + 2]];
				Vector3 p = ClosestPointOnTriangle(query, a, b, c);
				float sqr = (p - query).sqrMagnitude;
				if (sqr < bestSqr)
				{
					bestSqr = sqr;
					bestTri = t;
					bestPoint = p;
				}
			}

			if (bestTri < 0)
			{
				return false;
			}

			closestPoint = bestPoint;

			int i0 = triangles[bestTri];
			int i1 = triangles[bestTri + 1];
			int i2 = triangles[bestTri + 2];

			if (normals != null && i0 < normals.Length && i1 < normals.Length && i2 < normals.Length)
			{
				Barycentric(bestPoint, vertices[i0], vertices[i1], vertices[i2], out float u, out float v, out float w);
				normal = (normals[i0] * u + normals[i1] * v + normals[i2] * w);
			}

			if (normal.sqrMagnitude < 1e-12f)
			{
				// Fall back to the geometric face normal.
				Vector3 a = vertices[i0];
				Vector3 b = vertices[i1];
				Vector3 c = vertices[i2];
				normal = Vector3.Cross(b - a, c - a);
			}

			if (normal.sqrMagnitude < 1e-12f)
			{
				return false;
			}

			normal = normal.normalized;
			return true;
		}

		private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 ab = b - a;
			Vector3 ac = c - a;
			Vector3 ap = p - a;

			float d1 = Vector3.Dot(ab, ap);
			float d2 = Vector3.Dot(ac, ap);
			if (d1 <= 0f && d2 <= 0f)
			{
				return a;
			}

			Vector3 bp = p - b;
			float d3 = Vector3.Dot(ab, bp);
			float d4 = Vector3.Dot(ac, bp);
			if (d3 >= 0f && d4 <= d3)
			{
				return b;
			}

			float vc = d1 * d4 - d3 * d2;
			if (vc <= 0f && d1 >= 0f && d3 <= 0f)
			{
				float v = d1 / (d1 - d3);
				return a + v * ab;
			}

			Vector3 cp = p - c;
			float d5 = Vector3.Dot(ab, cp);
			float d6 = Vector3.Dot(ac, cp);
			if (d6 >= 0f && d5 <= d6)
			{
				return c;
			}

			float vb = d5 * d2 - d1 * d6;
			if (vb <= 0f && d2 >= 0f && d6 <= 0f)
			{
				float w = d2 / (d2 - d6);
				return a + w * ac;
			}

			float va = d3 * d6 - d5 * d4;
			if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
			{
				float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
				return b + w * (c - b);
			}

			float denom = 1f / (va + vb + vc);
			float vv = vb * denom;
			float ww = vc * denom;
			return a + ab * vv + ac * ww;
		}

		private static void Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w)
		{
			Vector3 v0 = b - a;
			Vector3 v1 = c - a;
			Vector3 v2 = p - a;
			float d00 = Vector3.Dot(v0, v0);
			float d01 = Vector3.Dot(v0, v1);
			float d11 = Vector3.Dot(v1, v1);
			float d20 = Vector3.Dot(v2, v0);
			float d21 = Vector3.Dot(v2, v1);
			float denom = d00 * d11 - d01 * d01;
			if (Mathf.Abs(denom) < 1e-12f)
			{
				u = 1f;
				v = 0f;
				w = 0f;
				return;
			}
			v = (d11 * d20 - d01 * d21) / denom;
			w = (d00 * d21 - d01 * d20) / denom;
			u = 1f - v - w;
		}

		// Unique key per grid edge so shared edges produce one vertex.
		private static long EdgeKey(int x, int y, int z, int edge)
		{
			// Map the edge to an owning corner + axis to deduplicate across cells.
			long ax = x + EdgeOwner[edge, 0];
			long ay = y + EdgeOwner[edge, 1];
			long az = z + EdgeOwner[edge, 2];
			long axis = EdgeOwner[edge, 3];
			return ((ax & 0xFFFFF) << 42) | ((ay & 0xFFFFF) << 22) | ((az & 0xFFFFF) << 2) | (axis & 0x3);
		}

		// Corner index -> grid offset.
		private static readonly int[,] CornerOffset =
		{
			{ 0, 0, 0 }, { 1, 0, 0 }, { 1, 1, 0 }, { 0, 1, 0 },
			{ 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 }
		};

		// Edge index -> the two corner indices it connects.
		private static readonly int[,] EdgeConnection =
		{
			{ 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
			{ 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
			{ 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
		};

		// Edge -> owning cell corner offset (x,y,z) and axis (0 = X edge, 1 = Y edge, 2 = Z edge).
		// Each of the 12 cube edges is mapped to a canonical owning corner + axis so that
		// edges shared between neighbouring cells resolve to the same key.
		private static readonly int[,] EdgeOwner =
		{
			{ 0, 0, 0, 0 }, // 0: 0-1 X edge at (0,0,0)
			{ 1, 0, 0, 1 }, // 1: 1-2 Y edge at (1,0,0)
			{ 0, 1, 0, 0 }, // 2: 2-3 X edge at (0,1,0)
			{ 0, 0, 0, 1 }, // 3: 3-0 Y edge at (0,0,0)
			{ 0, 0, 1, 0 }, // 4: 4-5 X edge at (0,0,1)
			{ 1, 0, 1, 1 }, // 5: 5-6 Y edge at (1,0,1)
			{ 0, 1, 1, 0 }, // 6: 6-7 X edge at (0,1,1)
			{ 0, 0, 1, 1 }, // 7: 7-4 Y edge at (0,0,1)
			{ 0, 0, 0, 2 }, // 8: 0-4 Z edge at (0,0,0)
			{ 1, 0, 0, 2 }, // 9: 1-5 Z edge at (1,0,0)
			{ 1, 1, 0, 2 }, // 10: 2-6 Z edge at (1,1,0)
			{ 0, 1, 0, 2 }  // 11: 3-7 Z edge at (0,1,0)
		};

		private static readonly int[] EdgeTable = new int[256]
		{
			0x0,0x109,0x203,0x30a,0x406,0x50f,0x605,0x70c,0x80c,0x905,0xa0f,0xb06,0xc0a,0xd03,0xe09,0xf00,
			0x190,0x99,0x393,0x29a,0x596,0x49f,0x795,0x69c,0x99c,0x895,0xb9f,0xa96,0xd9a,0xc93,0xf99,0xe90,
			0x230,0x339,0x33,0x13a,0x636,0x73f,0x435,0x53c,0xa3c,0xb35,0x83f,0x936,0xe3a,0xf33,0xc39,0xd30,
			0x3a0,0x2a9,0x1a3,0xaa,0x7a6,0x6af,0x5a5,0x4ac,0xbac,0xaa5,0x9af,0x8a6,0xfaa,0xea3,0xda9,0xca0,
			0x460,0x569,0x663,0x76a,0x66,0x16f,0x265,0x36c,0xc6c,0xd65,0xe6f,0xf66,0x86a,0x963,0xa69,0xb60,
			0x5f0,0x4f9,0x7f3,0x6fa,0x1f6,0xff,0x3f5,0x2fc,0xdfc,0xcf5,0xfff,0xef6,0x9fa,0x8f3,0xbf9,0xaf0,
			0x650,0x759,0x453,0x55a,0x256,0x35f,0x55,0x15c,0xe5c,0xf55,0xc5f,0xd56,0xa5a,0xb53,0x859,0x950,
			0x7c0,0x6c9,0x5c3,0x4ca,0x3c6,0x2cf,0x1c5,0xcc,0xfcc,0xec5,0xdcf,0xcc6,0xbca,0xac3,0x9c9,0x8c0,
			0x8c0,0x9c9,0xac3,0xbca,0xcc6,0xdcf,0xec5,0xfcc,0xcc,0x1c5,0x2cf,0x3c6,0x4ca,0x5c3,0x6c9,0x7c0,
			0x950,0x859,0xb53,0xa5a,0xd56,0xc5f,0xf55,0xe5c,0x15c,0x55,0x35f,0x256,0x55a,0x453,0x759,0x650,
			0xaf0,0xbf9,0x8f3,0x9fa,0xef6,0xfff,0xcf5,0xdfc,0x2fc,0x3f5,0xff,0x1f6,0x6fa,0x7f3,0x4f9,0x5f0,
			0xb60,0xa69,0x963,0x86a,0xf66,0xe6f,0xd65,0xc6c,0x36c,0x265,0x16f,0x66,0x76a,0x663,0x569,0x460,
			0xca0,0xda9,0xea3,0xfaa,0x8a6,0x9af,0xaa5,0xbac,0x4ac,0x5a5,0x6af,0x7a6,0xaa,0x1a3,0x2a9,0x3a0,
			0xd30,0xc39,0xf33,0xe3a,0x936,0x83f,0xb35,0xa3c,0x53c,0x435,0x73f,0x636,0x13a,0x33,0x339,0x230,
			0xe90,0xf99,0xc93,0xd9a,0xa96,0xb9f,0x895,0x99c,0x69c,0x795,0x49f,0x596,0x29a,0x393,0x99,0x190,
			0xf00,0xe09,0xd03,0xc0a,0xb06,0xa0f,0x905,0x80c,0x70c,0x605,0x50f,0x406,0x30a,0x203,0x109,0x0
		};

		private static readonly int[,] TriTable = new int[256, 16]
		{
			{-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,8,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,1,9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,8,3,9,8,1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,2,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,8,3,1,2,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{9,2,10,0,2,9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{2,8,3,2,10,8,10,9,8,-1,-1,-1,-1,-1,-1,-1},
			{3,11,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,11,2,8,11,0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,9,0,2,3,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,11,2,1,9,11,9,8,11,-1,-1,-1,-1,-1,-1,-1},
			{3,10,1,11,10,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,10,1,0,8,10,8,11,10,-1,-1,-1,-1,-1,-1,-1},
			{3,9,0,3,11,9,11,10,9,-1,-1,-1,-1,-1,-1,-1},
			{9,8,10,10,8,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,7,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,3,0,7,3,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,1,9,8,4,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,1,9,4,7,1,7,3,1,-1,-1,-1,-1,-1,-1,-1},
			{1,2,10,8,4,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{3,4,7,3,0,4,1,2,10,-1,-1,-1,-1,-1,-1,-1},
			{9,2,10,9,0,2,8,4,7,-1,-1,-1,-1,-1,-1,-1},
			{2,10,9,2,9,7,2,7,3,7,9,4,-1,-1,-1,-1},
			{8,4,7,3,11,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{11,4,7,11,2,4,2,0,4,-1,-1,-1,-1,-1,-1,-1},
			{9,0,1,8,4,7,2,3,11,-1,-1,-1,-1,-1,-1,-1},
			{4,7,11,9,4,11,9,11,2,9,2,1,-1,-1,-1,-1},
			{3,10,1,3,11,10,7,8,4,-1,-1,-1,-1,-1,-1,-1},
			{1,11,10,1,4,11,1,0,4,7,11,4,-1,-1,-1,-1},
			{4,7,8,9,0,11,9,11,10,11,0,3,-1,-1,-1,-1},
			{4,7,11,4,11,9,9,11,10,-1,-1,-1,-1,-1,-1,-1},
			{9,5,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{9,5,4,0,8,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,5,4,1,5,0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{8,5,4,8,3,5,3,1,5,-1,-1,-1,-1,-1,-1,-1},
			{1,2,10,9,5,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{3,0,8,1,2,10,4,9,5,-1,-1,-1,-1,-1,-1,-1},
			{5,2,10,5,4,2,4,0,2,-1,-1,-1,-1,-1,-1,-1},
			{2,10,5,3,2,5,3,5,4,3,4,8,-1,-1,-1,-1},
			{9,5,4,2,3,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,11,2,0,8,11,4,9,5,-1,-1,-1,-1,-1,-1,-1},
			{0,5,4,0,1,5,2,3,11,-1,-1,-1,-1,-1,-1,-1},
			{2,1,5,2,5,8,2,8,11,4,8,5,-1,-1,-1,-1},
			{10,3,11,10,1,3,9,5,4,-1,-1,-1,-1,-1,-1,-1},
			{4,9,5,0,8,1,8,10,1,8,11,10,-1,-1,-1,-1},
			{5,4,0,5,0,11,5,11,10,11,0,3,-1,-1,-1,-1},
			{5,4,8,5,8,10,10,8,11,-1,-1,-1,-1,-1,-1,-1},
			{9,7,8,5,7,9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{9,3,0,9,5,3,5,7,3,-1,-1,-1,-1,-1,-1,-1},
			{0,7,8,0,1,7,1,5,7,-1,-1,-1,-1,-1,-1,-1},
			{1,5,3,3,5,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{9,7,8,9,5,7,10,1,2,-1,-1,-1,-1,-1,-1,-1},
			{10,1,2,9,5,0,5,3,0,5,7,3,-1,-1,-1,-1},
			{8,0,2,8,2,5,8,5,7,10,5,2,-1,-1,-1,-1},
			{2,10,5,2,5,3,3,5,7,-1,-1,-1,-1,-1,-1,-1},
			{7,9,5,7,8,9,3,11,2,-1,-1,-1,-1,-1,-1,-1},
			{9,5,7,9,7,2,9,2,0,2,7,11,-1,-1,-1,-1},
			{2,3,11,0,1,8,1,7,8,1,5,7,-1,-1,-1,-1},
			{11,2,1,11,1,7,7,1,5,-1,-1,-1,-1,-1,-1,-1},
			{9,5,8,8,5,7,10,1,3,10,3,11,-1,-1,-1,-1},
			{5,7,0,5,0,9,7,11,0,1,0,10,11,10,0,-1},
			{11,10,0,11,0,3,10,5,0,8,0,7,5,7,0,-1},
			{11,10,5,7,11,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{10,6,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,8,3,5,10,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{9,0,1,5,10,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,8,3,1,9,8,5,10,6,-1,-1,-1,-1,-1,-1,-1},
			{1,6,5,2,6,1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,6,5,1,2,6,3,0,8,-1,-1,-1,-1,-1,-1,-1},
			{9,6,5,9,0,6,0,2,6,-1,-1,-1,-1,-1,-1,-1},
			{5,9,8,5,8,2,5,2,6,3,2,8,-1,-1,-1,-1},
			{2,3,11,10,6,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{11,0,8,11,2,0,10,6,5,-1,-1,-1,-1,-1,-1,-1},
			{0,1,9,2,3,11,5,10,6,-1,-1,-1,-1,-1,-1,-1},
			{5,10,6,1,9,2,9,11,2,9,8,11,-1,-1,-1,-1},
			{6,3,11,6,5,3,5,1,3,-1,-1,-1,-1,-1,-1,-1},
			{0,8,11,0,11,5,0,5,1,5,11,6,-1,-1,-1,-1},
			{3,11,6,0,3,6,0,6,5,0,5,9,-1,-1,-1,-1},
			{6,5,9,6,9,11,11,9,8,-1,-1,-1,-1,-1,-1,-1},
			{5,10,6,4,7,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,3,0,4,7,3,6,5,10,-1,-1,-1,-1,-1,-1,-1},
			{1,9,0,5,10,6,8,4,7,-1,-1,-1,-1,-1,-1,-1},
			{10,6,5,1,9,7,1,7,3,7,9,4,-1,-1,-1,-1},
			{6,1,2,6,5,1,4,7,8,-1,-1,-1,-1,-1,-1,-1},
			{1,2,5,5,2,6,3,0,4,3,4,7,-1,-1,-1,-1},
			{8,4,7,9,0,5,0,6,5,0,2,6,-1,-1,-1,-1},
			{7,3,9,7,9,4,3,2,9,5,9,6,2,6,9,-1},
			{3,11,2,7,8,4,10,6,5,-1,-1,-1,-1,-1,-1,-1},
			{5,10,6,4,7,2,4,2,0,2,7,11,-1,-1,-1,-1},
			{0,1,9,4,7,8,2,3,11,5,10,6,-1,-1,-1,-1},
			{9,2,1,9,11,2,9,4,11,7,11,4,5,10,6,-1},
			{8,4,7,3,11,5,3,5,1,5,11,6,-1,-1,-1,-1},
			{5,1,11,5,11,6,1,0,11,7,11,4,0,4,11,-1},
			{0,5,9,0,6,5,0,3,6,11,6,3,8,4,7,-1},
			{6,5,9,6,9,11,4,7,9,7,11,9,-1,-1,-1,-1},
			{10,4,9,6,4,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,10,6,4,9,10,0,8,3,-1,-1,-1,-1,-1,-1,-1},
			{10,0,1,10,6,0,6,4,0,-1,-1,-1,-1,-1,-1,-1},
			{8,3,1,8,1,6,8,6,4,6,1,10,-1,-1,-1,-1},
			{1,4,9,1,2,4,2,6,4,-1,-1,-1,-1,-1,-1,-1},
			{3,0,8,1,2,9,2,4,9,2,6,4,-1,-1,-1,-1},
			{0,2,4,4,2,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{8,3,2,8,2,4,4,2,6,-1,-1,-1,-1,-1,-1,-1},
			{10,4,9,10,6,4,11,2,3,-1,-1,-1,-1,-1,-1,-1},
			{0,8,2,2,8,11,4,9,10,4,10,6,-1,-1,-1,-1},
			{3,11,2,0,1,6,0,6,4,6,1,10,-1,-1,-1,-1},
			{6,4,1,6,1,10,4,8,1,2,1,11,8,11,1,-1},
			{9,6,4,9,3,6,9,1,3,11,6,3,-1,-1,-1,-1},
			{8,11,1,8,1,0,11,6,1,9,1,4,6,4,1,-1},
			{3,11,6,3,6,0,0,6,4,-1,-1,-1,-1,-1,-1,-1},
			{6,4,8,11,6,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{7,10,6,7,8,10,8,9,10,-1,-1,-1,-1,-1,-1,-1},
			{0,7,3,0,10,7,0,9,10,6,7,10,-1,-1,-1,-1},
			{10,6,7,1,10,7,1,7,8,1,8,0,-1,-1,-1,-1},
			{10,6,7,10,7,1,1,7,3,-1,-1,-1,-1,-1,-1,-1},
			{1,2,6,1,6,8,1,8,9,8,6,7,-1,-1,-1,-1},
			{2,6,9,2,9,1,6,7,9,0,9,3,7,3,9,-1},
			{7,8,0,7,0,6,6,0,2,-1,-1,-1,-1,-1,-1,-1},
			{7,3,2,6,7,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{2,3,11,10,6,8,10,8,9,8,6,7,-1,-1,-1,-1},
			{2,0,7,2,7,11,0,9,7,6,7,10,9,10,7,-1},
			{1,8,0,1,7,8,1,10,7,6,7,10,2,3,11,-1},
			{11,2,1,11,1,7,10,6,1,6,7,1,-1,-1,-1,-1},
			{8,9,6,8,6,7,9,1,6,11,6,3,1,3,6,-1},
			{0,9,1,11,6,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{7,8,0,7,0,6,3,11,0,11,6,0,-1,-1,-1,-1},
			{7,11,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{7,6,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{3,0,8,11,7,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,1,9,11,7,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{8,1,9,8,3,1,11,7,6,-1,-1,-1,-1,-1,-1,-1},
			{10,1,2,6,11,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,2,10,3,0,8,6,11,7,-1,-1,-1,-1,-1,-1,-1},
			{2,9,0,2,10,9,6,11,7,-1,-1,-1,-1,-1,-1,-1},
			{6,11,7,2,10,3,10,8,3,10,9,8,-1,-1,-1,-1},
			{7,2,3,6,2,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{7,0,8,7,6,0,6,2,0,-1,-1,-1,-1,-1,-1,-1},
			{2,7,6,2,3,7,0,1,9,-1,-1,-1,-1,-1,-1,-1},
			{1,6,2,1,8,6,1,9,8,8,7,6,-1,-1,-1,-1},
			{10,7,6,10,1,7,1,3,7,-1,-1,-1,-1,-1,-1,-1},
			{10,7,6,1,7,10,1,8,7,1,0,8,-1,-1,-1,-1},
			{0,3,7,0,7,10,0,10,9,6,10,7,-1,-1,-1,-1},
			{7,6,10,7,10,8,8,10,9,-1,-1,-1,-1,-1,-1,-1},
			{6,8,4,11,8,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{3,6,11,3,0,6,0,4,6,-1,-1,-1,-1,-1,-1,-1},
			{8,6,11,8,4,6,9,0,1,-1,-1,-1,-1,-1,-1,-1},
			{9,4,6,9,6,3,9,3,1,11,3,6,-1,-1,-1,-1},
			{6,8,4,6,11,8,2,10,1,-1,-1,-1,-1,-1,-1,-1},
			{1,2,10,3,0,11,0,6,11,0,4,6,-1,-1,-1,-1},
			{4,11,8,4,6,11,0,2,9,2,10,9,-1,-1,-1,-1},
			{10,9,3,10,3,2,9,4,3,11,3,6,4,6,3,-1},
			{8,2,3,8,4,2,4,6,2,-1,-1,-1,-1,-1,-1,-1},
			{0,4,2,4,6,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,9,0,2,3,4,2,4,6,4,3,8,-1,-1,-1,-1},
			{1,9,4,1,4,2,2,4,6,-1,-1,-1,-1,-1,-1,-1},
			{8,1,3,8,6,1,8,4,6,6,10,1,-1,-1,-1,-1},
			{10,1,0,10,0,6,6,0,4,-1,-1,-1,-1,-1,-1,-1},
			{4,6,3,4,3,8,6,10,3,0,3,9,10,9,3,-1},
			{10,9,4,6,10,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,9,5,7,6,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,8,3,4,9,5,11,7,6,-1,-1,-1,-1,-1,-1,-1},
			{5,0,1,5,4,0,7,6,11,-1,-1,-1,-1,-1,-1,-1},
			{11,7,6,8,3,4,3,5,4,3,1,5,-1,-1,-1,-1},
			{9,5,4,10,1,2,7,6,11,-1,-1,-1,-1,-1,-1,-1},
			{6,11,7,1,2,10,0,8,3,4,9,5,-1,-1,-1,-1},
			{7,6,11,5,4,10,4,2,10,4,0,2,-1,-1,-1,-1},
			{3,4,8,3,5,4,3,2,5,10,5,2,11,7,6,-1},
			{7,2,3,7,6,2,5,4,9,-1,-1,-1,-1,-1,-1,-1},
			{9,5,4,0,8,6,0,6,2,6,8,7,-1,-1,-1,-1},
			{3,6,2,3,7,6,1,5,0,5,4,0,-1,-1,-1,-1},
			{6,2,8,6,8,7,2,1,8,4,8,5,1,5,8,-1},
			{9,5,4,10,1,6,1,7,6,1,3,7,-1,-1,-1,-1},
			{1,6,10,1,7,6,1,0,7,8,7,0,9,5,4,-1},
			{4,0,10,4,10,5,0,3,10,6,10,7,3,7,10,-1},
			{7,6,10,7,10,8,5,4,10,4,8,10,-1,-1,-1,-1},
			{6,9,5,6,11,9,11,8,9,-1,-1,-1,-1,-1,-1,-1},
			{3,6,11,0,6,3,0,5,6,0,9,5,-1,-1,-1,-1},
			{0,11,8,0,5,11,0,1,5,5,6,11,-1,-1,-1,-1},
			{6,11,3,6,3,5,5,3,1,-1,-1,-1,-1,-1,-1,-1},
			{1,2,10,9,5,11,9,11,8,11,5,6,-1,-1,-1,-1},
			{0,11,3,0,6,11,0,9,6,5,6,9,1,2,10,-1},
			{11,8,5,11,5,6,8,0,5,10,5,2,0,2,5,-1},
			{6,11,3,6,3,5,2,10,3,10,5,3,-1,-1,-1,-1},
			{5,8,9,5,2,8,5,6,2,3,8,2,-1,-1,-1,-1},
			{9,5,6,9,6,0,0,6,2,-1,-1,-1,-1,-1,-1,-1},
			{1,5,8,1,8,0,5,6,8,3,8,2,6,2,8,-1},
			{1,5,6,2,1,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,3,6,1,6,10,3,8,6,5,6,9,8,9,6,-1},
			{10,1,0,10,0,6,9,5,0,5,6,0,-1,-1,-1,-1},
			{0,3,8,5,6,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{10,5,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{11,5,10,7,5,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{11,5,10,11,7,5,8,3,0,-1,-1,-1,-1,-1,-1,-1},
			{5,11,7,5,10,11,1,9,0,-1,-1,-1,-1,-1,-1,-1},
			{10,7,5,10,11,7,9,8,1,8,3,1,-1,-1,-1,-1},
			{11,1,2,11,7,1,7,5,1,-1,-1,-1,-1,-1,-1,-1},
			{0,8,3,1,2,7,1,7,5,7,2,11,-1,-1,-1,-1},
			{9,7,5,9,2,7,9,0,2,2,11,7,-1,-1,-1,-1},
			{7,5,2,7,2,11,5,9,2,3,2,8,9,8,2,-1},
			{2,5,10,2,3,5,3,7,5,-1,-1,-1,-1,-1,-1,-1},
			{8,2,0,8,5,2,8,7,5,10,2,5,-1,-1,-1,-1},
			{9,0,1,5,10,3,5,3,7,3,10,2,-1,-1,-1,-1},
			{9,8,2,9,2,1,8,7,2,10,2,5,7,5,2,-1},
			{1,3,5,3,7,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,8,7,0,7,1,1,7,5,-1,-1,-1,-1,-1,-1,-1},
			{9,0,3,9,3,5,5,3,7,-1,-1,-1,-1,-1,-1,-1},
			{9,8,7,5,9,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{5,8,4,5,10,8,10,11,8,-1,-1,-1,-1,-1,-1,-1},
			{5,0,4,5,11,0,5,10,11,11,3,0,-1,-1,-1,-1},
			{0,1,9,8,4,10,8,10,11,10,4,5,-1,-1,-1,-1},
			{10,11,4,10,4,5,11,3,4,9,4,1,3,1,4,-1},
			{2,5,1,2,8,5,2,11,8,4,5,8,-1,-1,-1,-1},
			{0,4,11,0,11,3,4,5,11,2,11,1,5,1,11,-1},
			{0,2,5,0,5,9,2,11,5,4,5,8,11,8,5,-1},
			{9,4,5,2,11,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{2,5,10,3,5,2,3,4,5,3,8,4,-1,-1,-1,-1},
			{5,10,2,5,2,4,4,2,0,-1,-1,-1,-1,-1,-1,-1},
			{3,10,2,3,5,10,3,8,5,4,5,8,0,1,9,-1},
			{5,10,2,5,2,4,1,9,2,9,4,2,-1,-1,-1,-1},
			{8,4,5,8,5,3,3,5,1,-1,-1,-1,-1,-1,-1,-1},
			{0,4,5,1,0,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{8,4,5,8,5,3,9,0,5,0,3,5,-1,-1,-1,-1},
			{9,4,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,11,7,4,9,11,9,10,11,-1,-1,-1,-1,-1,-1,-1},
			{0,8,3,4,9,7,9,11,7,9,10,11,-1,-1,-1,-1},
			{1,10,11,1,11,4,1,4,0,7,4,11,-1,-1,-1,-1},
			{3,1,4,3,4,8,1,10,4,7,4,11,10,11,4,-1},
			{4,11,7,9,11,4,9,2,11,9,1,2,-1,-1,-1,-1},
			{9,7,4,9,11,7,9,1,11,2,11,1,0,8,3,-1},
			{11,7,4,11,4,2,2,4,0,-1,-1,-1,-1,-1,-1,-1},
			{11,7,4,11,4,2,8,3,4,3,2,4,-1,-1,-1,-1},
			{2,9,10,2,7,9,2,3,7,7,4,9,-1,-1,-1,-1},
			{9,10,7,9,7,4,10,2,7,8,7,0,2,0,7,-1},
			{3,7,10,3,10,2,7,4,10,1,10,0,4,0,10,-1},
			{1,10,2,8,7,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,9,1,4,1,7,7,1,3,-1,-1,-1,-1,-1,-1,-1},
			{4,9,1,4,1,7,0,8,1,8,7,1,-1,-1,-1,-1},
			{4,0,3,7,4,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{4,8,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{9,10,8,10,11,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{3,0,9,3,9,11,11,9,10,-1,-1,-1,-1,-1,-1,-1},
			{0,1,10,0,10,8,8,10,11,-1,-1,-1,-1,-1,-1,-1},
			{3,1,10,11,3,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,2,11,1,11,9,9,11,8,-1,-1,-1,-1,-1,-1,-1},
			{3,0,9,3,9,11,1,2,9,2,11,9,-1,-1,-1,-1},
			{0,2,11,8,0,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{3,2,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{2,3,8,2,8,10,10,8,9,-1,-1,-1,-1,-1,-1,-1},
			{9,10,2,0,9,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{2,3,8,2,8,10,0,1,8,1,10,8,-1,-1,-1,-1},
			{1,10,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{1,3,8,9,1,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,9,1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{0,3,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
			{-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1}
		};
	}
}
#endif
