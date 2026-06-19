using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
	[DisallowMultipleComponent]
	[AddComponentMenu("UMA/Utilities/UMA Slot Normal Normalizer")]
	public class UMASlotNormalNormalizer : MonoBehaviour
	{
		[Header("Source Data")]
		public UMAWardrobeRecipe wardrobeRecipe;
		public SharedColorTable sharedColorTable;

		[Header("Covering Mesh")]
		[Range(0f, 1f)] public float metaballSize = 0.5f;
		[Range(0f, 1f)] public float smoothness = 0.5f;
		[Range(8, 256)] public int coveringMeshResolution = 64;
		public bool showCoveringMesh = true;
		[Range(0f, 1f)] public float coveringMeshAlpha = 0.5f;

		[Header("Normal Visualization")]
		public bool showCoveringMeshNormals = false;
		public bool showPreviewMeshNormals = false;
		public float normalDisplayLength = 0.02f;

		[Header("Normal Projection")]
		[Range(0, 5)] public int smoothingIterations = 2;
		[Tooltip("DBSCAN cluster radius as a fraction of preview mesh bounds magnitude. Larger values merge nearby ponytail/root groups; smaller values split them.")]
		public float clusterEpsScale = 0.05f;
		public int minClusterSize = 4;
		[Range(-1f, 1f)] public float normalDotThreshold = 0.5f;
		[Tooltip("Ray length in local mesh units. If 0 or lower, this is set from the covering mesh extents when normals are projected.")]
		public float maxRayDistance = 0f;
		[Tooltip("Effectiveness of the normal projection. 0 = no change, 1 = full projection to covering mesh normals.")]
		[Range(0f, 1f)]
		public float normalEffectiveness = 1f;

		[Header("Projection Debug")]
		public bool enableDebugVisualization = false;
		public bool showRayGizmos = false;
		public Color acceptedRayColor = Color.green;
		public Color rejectedRayColor = Color.red;
		public int maxDebugRayCount = 500;

#if UNITY_EDITOR
		[HideInInspector] public int selectedSlotIndex;
		[HideInInspector] public int selectedColorIndex;
		[HideInInspector] public GameObject previewObject;
		[HideInInspector] public Mesh previewMesh;
		[HideInInspector] public Material previewMaterial;

		[HideInInspector] public GameObject coveringMeshObject;
		[HideInInspector] public Mesh coveringMesh;
		[HideInInspector] public Material coveringMeshMaterial;

		private UMAMetaballMesher.ScalarField _coveringField;
		private Vector3[] _sourceNormalsForEffectiveness;
		private Vector3[] _projectedNormalsForEffectiveness;
		private readonly List<ProjectionDebugRay> _projectionDebugRays = new List<ProjectionDebugRay>();
		private readonly List<Vector3> _projectionDebugClusterCentroids = new List<Vector3>();
		private readonly List<Vector3> _projectionDebugUnresolved = new List<Vector3>();

		/// <summary>Minimum and maximum metaball radius mapped from <see cref="metaballSize"/>.</summary>
		private const float MaxMetaballRadius = 0.05f;
		private const float MinMetaballRadius = 0.001f;
		private const int MaxBlurIterations = 4;
		private const float ProjectionRayEpsilon = 0.0001f;

		private enum ProjectionFallback
		{
			AcceptedRay = 0,
			ReverseRay = 1,
			SampleNormalRay = 2,
			ClusterAxisRay = 3,
			NearestCoveringVertex = 4,
			CentroidFallback = 5
		}

		private struct ProjectionDebugRay
		{
			public Vector3 Start;
			public Vector3 End;
			public Color Color;

			public ProjectionDebugRay(Vector3 start, Vector3 end, Color color)
			{
				Start = start;
				End = end;
				Color = color;
			}
		}

		/*
		 * Usage note: clusterEpsScale is multiplied by previewMesh.bounds.size.magnitude so
		 * ponytail/root clustering scales with character and hair size. Increase it when related
		 * hair-card roots are not grouped; decrease it when unrelated regions merge. Increase
		 * normalDotThreshold to require normals to align more strongly with the outward ray.
		 */

		private static void DestroyEditorObject(Object obj)
		{
			if (obj == null)
			{
				return;
			}

			if (Application.isPlaying)
			{
				Destroy(obj);
			}
			else
			{
				DestroyImmediate(obj);
			}
		}

		public void DestroyPreview()
		{
			DestroyCoveringMesh();
			ClearNormalEffectivenessCache();

			if (previewObject != null)
			{
				DestroyEditorObject(previewObject);
				previewObject = null;
			}

			if (previewMesh != null)
			{
				DestroyEditorObject(previewMesh);
				previewMesh = null;
			}

			if (previewMaterial != null)
			{
				DestroyEditorObject(previewMaterial);
				previewMaterial = null;
			}
		}

		public void DestroyCoveringMesh()
		{
			if (coveringMeshObject != null)
			{
				DestroyEditorObject(coveringMeshObject);
				coveringMeshObject = null;
			}

			if (coveringMesh != null)
			{
				DestroyEditorObject(coveringMesh);
				coveringMesh = null;
			}

			if (coveringMeshMaterial != null)
			{
				DestroyEditorObject(coveringMeshMaterial);
				coveringMeshMaterial = null;
			}

			_coveringField = null;
		}

		/// <summary>
		/// Radius (in mesh local units) of each metaball, mapped from <see cref="metaballSize"/>:
		/// 0 -> 0.05 (large balls), 1 -> 0.001 (small balls).
		/// </summary>
		public float GetMetaballRadius()
		{
			return Mathf.Lerp(MaxMetaballRadius, MinMetaballRadius, Mathf.Clamp01(metaballSize));
		}

		/// <summary>
		/// Builds a temporary single-surface covering mesh over the current preview mesh using a
		/// metaball voxel field. The mesh is kept only for normal projection and debug display.
		/// </summary>
		public void BuildCoveringMesh()
		{
			DestroyCoveringMesh();

			if (previewMesh == null)
			{
				Debug.LogWarning("[UMASlotNormalNormalizer] Build a preview before constructing a covering mesh.", this);
				return;
			}

			Vector3[] vertices = previewMesh.vertices;
			if (vertices == null || vertices.Length == 0)
			{
				Debug.LogWarning("[UMASlotNormalNormalizer] Preview mesh has no vertices.", this);
				return;
			}

			float radius = GetMetaballRadius();
			int blurIterations = Mathf.RoundToInt(Mathf.Clamp01(smoothness) * MaxBlurIterations);

			Mesh built = UMAMetaballMesher.Build(vertices, previewMesh.bounds, radius, coveringMeshResolution, blurIterations, out _coveringField);
			if (built == null)
			{
				Debug.LogWarning("[UMASlotNormalNormalizer] Failed to construct a covering mesh. Try a larger metaball size or higher resolution.", this);
				return;
			}

			coveringMesh = built;
			coveringMesh.name = previewMesh.name + "_CoveringMesh";
			coveringMesh.hideFlags = HideFlags.DontSave;

			coveringMeshMaterial = new Material(Shader.Find("Sprites/Default"));
			coveringMeshMaterial.name = "UMACoveringMeshDebugMaterial";
			coveringMeshMaterial.hideFlags = HideFlags.DontSave;
			ApplyCoveringMeshAlpha();

			coveringMeshObject = new GameObject(previewMesh.name + "_CoveringMeshPreview");
			coveringMeshObject.hideFlags = HideFlags.DontSave;
			coveringMeshObject.layer = gameObject.layer;
			coveringMeshObject.transform.SetParent(transform, false);

			MeshFilter meshFilter = coveringMeshObject.AddComponent<MeshFilter>();
			meshFilter.sharedMesh = coveringMesh;

			MeshRenderer meshRenderer = coveringMeshObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = coveringMeshMaterial;

			coveringMeshObject.SetActive(showCoveringMesh);
		}

		/// <summary>Applies <see cref="coveringMeshAlpha"/> to the debug material tint color.</summary>
		public void ApplyCoveringMeshAlpha()
		{
			if (coveringMeshMaterial == null)
			{
				return;
			}
			Color c = new Color(0.3f, 0.7f, 1f, Mathf.Clamp01(coveringMeshAlpha));
			if (coveringMeshMaterial.HasProperty("_Color"))
			{
				coveringMeshMaterial.SetColor("_Color", c);
			}
			if (coveringMeshMaterial.HasProperty("_BaseColor"))
			{
				coveringMeshMaterial.SetColor("_BaseColor", c);
			}
		}

		/// <summary>Toggles visibility of the temporary covering mesh in the scene.</summary>
		public void SetCoveringMeshVisible(bool visible)
		{
			showCoveringMesh = visible;
			if (coveringMeshObject != null)
			{
				coveringMeshObject.SetActive(visible);
			}
		}

		/// <summary>
		/// Projects the covering mesh's volume normals back onto the preview (hair-card) mesh.
		/// For each preview vertex the closest point on the covering surface is found and its
		/// outward normal is used. Normals are forced to point away from the volume center
		/// (never inward). After projection a Laplacian smooth pass is applied using mesh
		/// adjacency to reduce faceting.
		/// </summary>
		public void ProjectNormalsToPreview()
		{
			if (previewMesh == null)
			{
				Debug.LogWarning("[UMASlotNormalNormalizer] Build a preview before projecting normals.", this);
				return;
			}

			if (coveringMesh == null)
			{
				Debug.LogWarning("[UMASlotNormalNormalizer] Construct a covering mesh before projecting normals.", this);
				return;
			}

			Vector3[] vertices = previewMesh.vertices;
			Vector3[] coveringVerticesForCentroid = coveringMesh.vertices;
			List<Vector3> coveringPositions = new List<Vector3>(coveringVerticesForCentroid.Length);
			for (int i = 0; i < coveringVerticesForCentroid.Length; i++)
			{
				coveringPositions.Add(coveringVerticesForCentroid[i]);
			}
			Vector3 coveringCentroid = ComputeCentroid(coveringPositions);

			Vector3[] sampleNormals = previewMesh.normals;
			if (sampleNormals == null || sampleNormals.Length != vertices.Length)
			{
				previewMesh.RecalculateNormals();
				sampleNormals = previewMesh.normals;
			}

			List<Vector3> samplePositions = new List<Vector3>(vertices.Length);
			for (int i = 0; i < vertices.Length; i++)
			{
				samplePositions.Add(vertices[i]);
			}

			Vector3 globalCentroid = ComputeCentroid(samplePositions);
			Bounds previewBounds = previewMesh.bounds;
			float clusterEps = Mathf.Max(ProjectionRayEpsilon, clusterEpsScale * previewBounds.size.magnitude);
			List<int> clusterIds = DBSCANClusterIndices(samplePositions, clusterEps, Mathf.Max(1, minClusterSize));
			Dictionary<int, List<Vector3>> clusterPoints = new Dictionary<int, List<Vector3>>();
			for (int i = 0; i < clusterIds.Count; i++)
			{
				int clusterId = clusterIds[i];
				if (clusterId < 0)
				{
					continue;
				}

				if (!clusterPoints.TryGetValue(clusterId, out List<Vector3> points))
				{
					points = new List<Vector3>();
					clusterPoints.Add(clusterId, points);
				}
				points.Add(vertices[i]);
			}

			Dictionary<int, Vector3> clusterCentroids = new Dictionary<int, Vector3>();
			Dictionary<int, Vector3> clusterAxes = new Dictionary<int, Vector3>();
			foreach (KeyValuePair<int, List<Vector3>> pair in clusterPoints)
			{
				Vector3 centroid = ComputeCentroid(pair.Value);
				clusterCentroids[pair.Key] = centroid;
				clusterAxes[pair.Key] = ComputeClusterPrincipalAxis(pair.Value);
			}

			if (maxRayDistance <= 0f)
			{
				maxRayDistance = coveringMesh.bounds.extents.magnitude * 2f;
			}

			_projectionDebugRays.Clear();
			_projectionDebugClusterCentroids.Clear();
			_projectionDebugUnresolved.Clear();
			if (enableDebugVisualization)
			{
				foreach (KeyValuePair<int, Vector3> pair in clusterCentroids)
				{
					_projectionDebugClusterCentroids.Add(pair.Value);
				}
			}

			Vector3[] normals = new Vector3[vertices.Length];
			Vector3[] outwardHints = new Vector3[vertices.Length];
			int acceptedHits = 0;
			int[] fallbackCounts = new int[6];

			for (int i = 0; i < vertices.Length; i++)
			{
				Vector3 vertex = vertices[i];
				int clusterId = clusterIds[i];
				Vector3 clusterCentroid = globalCentroid;
				bool clustered = clusterId >= 0 && clusterCentroids.TryGetValue(clusterId, out clusterCentroid);
				Vector3 origin = clustered ? clusterCentroid : globalCentroid;
				if (!PointInMesh(coveringMesh, origin))
				{
					origin = Vector3.Lerp(origin, coveringCentroid, 0.5f);
				}

				Vector3 rayDir = vertex - origin;
				if (rayDir.sqrMagnitude < 1e-12f)
				{
					Vector3 jitter = (sampleNormals != null && i < sampleNormals.Length && sampleNormals[i].sqrMagnitude > 1e-12f) ? sampleNormals[i].normalized : Vector3.up;
					origin -= jitter * ProjectionRayEpsilon;
					rayDir = vertex - origin;
				}
				rayDir.Normalize();
				outwardHints[i] = rayDir;

				Vector3 projectedNormal;
				ProjectionFallback usedFallback;
				if (TryRayProjection(coveringMesh, origin, rayDir, normalDotThreshold, maxRayDistance, out projectedNormal, out Vector3 hitPoint))
				{
					usedFallback = ProjectionFallback.AcceptedRay;
					acceptedHits++;
					AddDebugRay(origin, hitPoint, acceptedRayColor);
				}
				else if (TryRayProjection(coveringMesh, origin, -rayDir, normalDotThreshold, maxRayDistance, out projectedNormal, out hitPoint))
				{
					usedFallback = ProjectionFallback.ReverseRay;
					AddDebugRay(origin, origin - rayDir * Mathf.Min(maxRayDistance, previewBounds.size.magnitude), rejectedRayColor);
					AddDebugRay(origin, hitPoint, acceptedRayColor);
				}
				else if (sampleNormals != null && i < sampleNormals.Length && sampleNormals[i].sqrMagnitude > 1e-12f && TryRayProjection(coveringMesh, vertex + sampleNormals[i].normalized * ProjectionRayEpsilon, sampleNormals[i].normalized, normalDotThreshold, maxRayDistance, out projectedNormal, out hitPoint))
				{
					usedFallback = ProjectionFallback.SampleNormalRay;
					AddDebugRay(vertex, hitPoint, acceptedRayColor);
				}
				else if (clustered && clusterAxes.TryGetValue(clusterId, out Vector3 axis) && TryClusterAxisProjection(coveringMesh, vertex, clusterCentroid, axis, normalDotThreshold, maxRayDistance, out projectedNormal, out hitPoint))
				{
					usedFallback = ProjectionFallback.ClusterAxisRay;
					AddDebugRay(vertex, hitPoint, acceptedRayColor);
				}
				else if (TryNearestCoveringVertexNormal(coveringMesh, vertex, normalDotThreshold, out projectedNormal))
				{
					usedFallback = ProjectionFallback.NearestCoveringVertex;
					AddDebugRay(vertex, vertex + projectedNormal * normalDisplayLength, acceptedRayColor);
				}
				else
				{
					Vector3 finalFallback = vertex - coveringCentroid;
					projectedNormal = finalFallback.sqrMagnitude > 1e-12f ? finalFallback.normalized : Vector3.up;
					usedFallback = ProjectionFallback.CentroidFallback;
					_projectionDebugUnresolved.Add(vertex);
					AddDebugRay(origin, origin + rayDir * Mathf.Min(maxRayDistance, previewBounds.size.magnitude), rejectedRayColor);
				}

				fallbackCounts[(int)usedFallback]++;
				normals[i] = projectedNormal;
			}

			// Laplacian smooth over triangle adjacency.
			int iters = Mathf.Clamp(smoothingIterations, 0, 5);
			if (iters > 0)
			{
				normals = SmoothNormals(previewMesh, normals, iters);
			}

			for (int i = 0; i < normals.Length; i++)
			{
				Vector3 hint = outwardHints[i];
				if (hint.sqrMagnitude > 1e-12f && Vector3.Dot(normals[i], hint) < 0f)
				{
					normals[i] = -normals[i];
				}
			}

			_sourceNormalsForEffectiveness = sampleNormals;
			_projectedNormalsForEffectiveness = normals;
			ApplyNormalEffectivenessToPreviewMesh();

			Debug.Log($"[UMASlotNormalNormalizer] Normal projection complete. Vertices={vertices.Length}, accepted primary hits={acceptedHits}, clusters={clusterCentroids.Count}, primary={fallbackCounts[(int)ProjectionFallback.AcceptedRay]}, reverse={fallbackCounts[(int)ProjectionFallback.ReverseRay]}, sampleNormal={fallbackCounts[(int)ProjectionFallback.SampleNormalRay]}, clusterAxis={fallbackCounts[(int)ProjectionFallback.ClusterAxisRay]}, nearestVertex={fallbackCounts[(int)ProjectionFallback.NearestCoveringVertex]}, centroidFallback={fallbackCounts[(int)ProjectionFallback.CentroidFallback]}.", this);
		}

		public void ApplyNormalEffectivenessToPreviewMesh()
		{
			if (previewMesh == null || _sourceNormalsForEffectiveness == null || _projectedNormalsForEffectiveness == null)
			{
				return;
			}

			int count = Mathf.Min(_sourceNormalsForEffectiveness.Length, _projectedNormalsForEffectiveness.Length);
			if (count <= 0 || previewMesh.vertexCount != count)
			{
				return;
			}

			float effectiveness = Mathf.Clamp01(normalEffectiveness);
			Vector3[] effectiveNormals = new Vector3[count];
			for (int i = 0; i < count; i++)
			{
				Vector3 blendedNormal = Vector3.Lerp(_sourceNormalsForEffectiveness[i], _projectedNormalsForEffectiveness[i], effectiveness);
				if (blendedNormal.sqrMagnitude < 1e-12f)
				{
					blendedNormal = _projectedNormalsForEffectiveness[i].sqrMagnitude > 1e-12f ? _projectedNormalsForEffectiveness[i] : _sourceNormalsForEffectiveness[i];
				}

				effectiveNormals[i] = blendedNormal.sqrMagnitude > 1e-12f ? blendedNormal.normalized : blendedNormal;
			}

			previewMesh.normals = effectiveNormals;
			previewMesh.RecalculateTangents();
		}

		public bool HasEffectiveNormalPreview()
		{
			if (previewMesh == null || _sourceNormalsForEffectiveness == null || _projectedNormalsForEffectiveness == null)
			{
				return false;
			}

			int count = Mathf.Min(_sourceNormalsForEffectiveness.Length, _projectedNormalsForEffectiveness.Length);
			return count > 0 && previewMesh.vertexCount == count;
		}

		private void ClearNormalEffectivenessCache()
		{
			_sourceNormalsForEffectiveness = null;
			_projectedNormalsForEffectiveness = null;
		}

		private bool TryRayProjection(Mesh mesh, Vector3 origin, Vector3 direction, float dotThreshold, float rayDistance, out Vector3 normal, out Vector3 hitPoint)
		{
			normal = Vector3.zero;
			hitPoint = Vector3.zero;
			if (direction.sqrMagnitude < 1e-12f)
			{
				return false;
			}

			Vector3 dir = direction.normalized;
			Vector3 rayOrigin = origin + dir * ProjectionRayEpsilon;
			if (!RaycastCoveringMeshFacing(mesh, rayOrigin, dir, rayDistance, dotThreshold, out hitPoint, out Vector3 hitNormal, out float _))
			{
				return false;
			}

			normal = hitNormal.normalized;
			return true;
		}

		private bool TryClusterAxisProjection(Mesh mesh, Vector3 vertex, Vector3 clusterCentroid, Vector3 axis, float dotThreshold, float rayDistance, out Vector3 normal, out Vector3 hitPoint)
		{
			Vector3 outwardAxis = axis.sqrMagnitude > 1e-12f ? axis.normalized : (vertex - clusterCentroid).normalized;
			if (Vector3.Dot(outwardAxis, vertex - clusterCentroid) < 0f)
			{
				outwardAxis = -outwardAxis;
			}

			return TryRayProjection(mesh, vertex + outwardAxis * ProjectionRayEpsilon, outwardAxis, dotThreshold, rayDistance, out normal, out hitPoint);
		}

		private void AddDebugRay(Vector3 start, Vector3 end, Color color)
		{
			if (!enableDebugVisualization || !showRayGizmos || _projectionDebugRays.Count >= Mathf.Max(0, maxDebugRayCount))
			{
				return;
			}

			_projectionDebugRays.Add(new ProjectionDebugRay(start, end, color));
		}

		private static bool TryNearestCoveringVertexNormal(Mesh mesh, Vector3 point, float dotThreshold, out Vector3 normal)
		{
			normal = Vector3.zero;
			int index = FindNearestCoveringVertex(mesh, point);
			if (index < 0)
			{
				return false;
			}

			Vector3[] verts = mesh.vertices;
			Vector3[] norms = mesh.normals;
			if (norms == null || index >= norms.Length || norms[index].sqrMagnitude < 1e-12f)
			{
				return false;
			}

			Vector3 outward = point - verts[index];
			if (outward.sqrMagnitude < 1e-12f)
			{
				return false;
			}

			normal = norms[index].normalized;
			if (Vector3.Dot(normal, outward.normalized) < dotThreshold)
			{
				normal = -normal;
				if (Vector3.Dot(normal, outward.normalized) < dotThreshold)
				{
					return false;
				}
			}

			return true;
		}

		private static List<int> DBSCANClusterIndices(List<Vector3> points, float eps, int minPts)
		{
			List<int> clusterIds = new List<int>(points.Count);
			for (int i = 0; i < points.Count; i++)
			{
				clusterIds.Add(-2); // -2 = unvisited, -1 = noise.
			}

			float epsSqr = eps * eps;
			int clusterId = 0;
			for (int i = 0; i < points.Count; i++)
			{
				if (clusterIds[i] != -2)
				{
					continue;
				}

				List<int> neighbours = RegionQuery(points, i, epsSqr);
				if (neighbours.Count < minPts)
				{
					clusterIds[i] = -1;
					continue;
				}

				clusterIds[i] = clusterId;
				Queue<int> seeds = new Queue<int>(neighbours);
				while (seeds.Count > 0)
				{
					int current = seeds.Dequeue();
					if (clusterIds[current] == -1)
					{
						clusterIds[current] = clusterId;
					}

					if (clusterIds[current] != -2)
					{
						continue;
					}

					clusterIds[current] = clusterId;
					List<int> currentNeighbours = RegionQuery(points, current, epsSqr);
					if (currentNeighbours.Count >= minPts)
					{
						foreach (int neighbour in currentNeighbours)
						{
							if (clusterIds[neighbour] == -2 || clusterIds[neighbour] == -1)
							{
								seeds.Enqueue(neighbour);
							}
						}
					}
				}

				clusterId++;
			}

			for (int i = 0; i < clusterIds.Count; i++)
			{
				if (clusterIds[i] == -2)
				{
					clusterIds[i] = -1;
				}
			}

			return clusterIds;
		}

		private static List<int> RegionQuery(List<Vector3> points, int pointIndex, float epsSqr)
		{
			List<int> result = new List<int>();
			Vector3 p = points[pointIndex];
			for (int i = 0; i < points.Count; i++)
			{
				if ((points[i] - p).sqrMagnitude <= epsSqr)
				{
					result.Add(i);
				}
			}

			return result;
		}

		private static Vector3 ComputeCentroid(List<Vector3> points)
		{
			if (points == null || points.Count == 0)
			{
				return Vector3.zero;
			}

			Vector3 sum = Vector3.zero;
			for (int i = 0; i < points.Count; i++)
			{
				sum += points[i];
			}

			return sum / points.Count;
		}

		private static bool RaycastCoveringMesh(Mesh mesh, Vector3 origin, Vector3 dir, float maxDistance, out Vector3 hitPoint, out Vector3 hitNormal, out float hitDistance)
		{
			hitPoint = Vector3.zero;
			hitNormal = Vector3.zero;
			hitDistance = 0f;

			Vector3[] vertices = mesh.vertices;
			Vector3[] normals = mesh.normals;
			int[] triangles = mesh.triangles;
			if (vertices == null || triangles == null || triangles.Length < 3 || dir.sqrMagnitude < 1e-12f)
			{
				return false;
			}

			Vector3 rayDir = dir.normalized;
			float limit = maxDistance > 0f ? maxDistance : float.MaxValue;
			float bestDistance = float.MaxValue;
			int bestTri = -1;
			Vector3 bestPoint = Vector3.zero;

			for (int t = 0; t < triangles.Length; t += 3)
			{
				int i0 = triangles[t];
				int i1 = triangles[t + 1];
				int i2 = triangles[t + 2];
				if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
				{
					continue;
				}

				if (RayIntersectsTriangle(origin, rayDir, vertices[i0], vertices[i1], vertices[i2], out float distance, out Vector3 point) && distance > ProjectionRayEpsilon && distance < bestDistance && distance <= limit)
				{
					bestDistance = distance;
					bestTri = t;
					bestPoint = point;
				}
			}

			if (bestTri < 0)
			{
				return false;
			}

			int aIndex = triangles[bestTri];
			int bIndex = triangles[bestTri + 1];
			int cIndex = triangles[bestTri + 2];
			if (normals != null && aIndex < normals.Length && bIndex < normals.Length && cIndex < normals.Length)
			{
				Barycentric(bestPoint, vertices[aIndex], vertices[bIndex], vertices[cIndex], out float u, out float v, out float w);
				hitNormal = normals[aIndex] * u + normals[bIndex] * v + normals[cIndex] * w;
			}

			if (hitNormal.sqrMagnitude < 1e-12f)
			{
				hitNormal = Vector3.Cross(vertices[bIndex] - vertices[aIndex], vertices[cIndex] - vertices[aIndex]);
			}

			if (hitNormal.sqrMagnitude < 1e-12f)
			{
				return false;
			}

			hitPoint = bestPoint;
			hitNormal = hitNormal.normalized;
			hitDistance = bestDistance;
			return true;
		}

		private static bool RaycastCoveringMeshFacing(Mesh mesh, Vector3 origin, Vector3 dir, float maxDistance, float dotThreshold, out Vector3 hitPoint, out Vector3 hitNormal, out float hitDistance)
		{
			hitPoint = Vector3.zero;
			hitNormal = Vector3.zero;
			hitDistance = 0f;

			Vector3[] vertices = mesh.vertices;
			Vector3[] normals = mesh.normals;
			int[] triangles = mesh.triangles;
			if (vertices == null || triangles == null || triangles.Length < 3 || dir.sqrMagnitude < 1e-12f)
			{
				return false;
			}

			Vector3 rayDir = dir.normalized;
			float limit = maxDistance > 0f ? maxDistance : float.MaxValue;
			float bestDistance = float.MaxValue;
			Vector3 bestPoint = Vector3.zero;
			Vector3 bestNormal = Vector3.zero;

			for (int t = 0; t < triangles.Length; t += 3)
			{
				int i0 = triangles[t];
				int i1 = triangles[t + 1];
				int i2 = triangles[t + 2];
				if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
				{
					continue;
				}

				if (!RayIntersectsTriangle(origin, rayDir, vertices[i0], vertices[i1], vertices[i2], out float distance, out Vector3 point) || distance <= ProjectionRayEpsilon || distance > limit || distance >= bestDistance)
				{
					continue;
				}

				Vector3 candidateNormal = Vector3.zero;
				if (normals != null && i0 < normals.Length && i1 < normals.Length && i2 < normals.Length)
				{
					Barycentric(point, vertices[i0], vertices[i1], vertices[i2], out float u, out float v, out float w);
					candidateNormal = normals[i0] * u + normals[i1] * v + normals[i2] * w;
				}

				if (candidateNormal.sqrMagnitude < 1e-12f)
				{
					candidateNormal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
				}

				if (candidateNormal.sqrMagnitude < 1e-12f)
				{
					continue;
				}

				candidateNormal.Normalize();
				if (Vector3.Dot(candidateNormal, rayDir) < dotThreshold)
				{
					candidateNormal = -candidateNormal;
					if (Vector3.Dot(candidateNormal, rayDir) < dotThreshold)
					{
						continue;
					}
				}

				bestDistance = distance;
				bestPoint = point;
				bestNormal = candidateNormal;
			}

			if (bestDistance == float.MaxValue)
			{
				return false;
			}

			hitPoint = bestPoint;
			hitNormal = bestNormal;
			hitDistance = bestDistance;
			return true;
		}

		private static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 point)
		{
			distance = 0f;
			point = Vector3.zero;
			Vector3 edge1 = b - a;
			Vector3 edge2 = c - a;
			Vector3 pvec = Vector3.Cross(dir, edge2);
			float det = Vector3.Dot(edge1, pvec);
			if (Mathf.Abs(det) < 1e-8f)
			{
				return false;
			}

			float invDet = 1f / det;
			Vector3 tvec = origin - a;
			float u = Vector3.Dot(tvec, pvec) * invDet;
			if (u < 0f || u > 1f)
			{
				return false;
			}

			Vector3 qvec = Vector3.Cross(tvec, edge1);
			float v = Vector3.Dot(dir, qvec) * invDet;
			if (v < 0f || u + v > 1f)
			{
				return false;
			}

			distance = Vector3.Dot(edge2, qvec) * invDet;
			if (distance <= ProjectionRayEpsilon)
			{
				return false;
			}

			point = origin + dir * distance;
			return true;
		}

		private static int FindNearestCoveringVertex(Mesh mesh, Vector3 point)
		{
			Vector3[] vertices = mesh.vertices;
			if (vertices == null || vertices.Length == 0)
			{
				return -1;
			}

			int best = -1;
			float bestSqr = float.MaxValue;
			for (int i = 0; i < vertices.Length; i++)
			{
				float sqr = (vertices[i] - point).sqrMagnitude;
				if (sqr < bestSqr)
				{
					bestSqr = sqr;
					best = i;
				}
			}

			return best;
		}

		private static Vector3 ComputeClusterPrincipalAxis(List<Vector3> points)
		{
			if (points == null || points.Count < 2)
			{
				return Vector3.up;
			}

			Vector3 centroid = ComputeCentroid(points);
			float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
			for (int i = 0; i < points.Count; i++)
			{
				Vector3 d = points[i] - centroid;
				xx += d.x * d.x;
				xy += d.x * d.y;
				xz += d.x * d.z;
				yy += d.y * d.y;
				yz += d.y * d.z;
				zz += d.z * d.z;
			}

			Vector3 axis = Vector3.right;
			for (int i = 0; i < 12; i++)
			{
				Vector3 next = new Vector3(
					xx * axis.x + xy * axis.y + xz * axis.z,
					xy * axis.x + yy * axis.y + yz * axis.z,
					xz * axis.x + yz * axis.y + zz * axis.z);
				if (next.sqrMagnitude < 1e-12f)
				{
					return Vector3.up;
				}
				axis = next.normalized;
			}

			return axis;
		}

		private static bool PointInMesh(Mesh mesh, Vector3 point)
		{
			Vector3 dir = new Vector3(0.937f, 0.311f, 0.159f).normalized;
			Vector3 origin = point + dir * ProjectionRayEpsilon;
			Vector3[] vertices = mesh.vertices;
			int[] triangles = mesh.triangles;
			if (vertices == null || triangles == null || triangles.Length < 3)
			{
				return false;
			}

			int hitCount = 0;
			for (int t = 0; t < triangles.Length; t += 3)
			{
				int i0 = triangles[t];
				int i1 = triangles[t + 1];
				int i2 = triangles[t + 2];
				if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
				{
					continue;
				}

				if (RayIntersectsTriangle(origin, dir, vertices[i0], vertices[i1], vertices[i2], out float _, out Vector3 _))
				{
					hitCount++;
				}
			}

			return (hitCount & 1) == 1;
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

		/// <summary>
		/// Laplacian normal smoothing: each vertex normal is averaged with the normals of
		/// its adjacent neighbours (shared-edge connectivity). Repeated <paramref name="iterations"/> times.
		/// </summary>
		private static Vector3[] SmoothNormals(Mesh mesh, Vector3[] normals, int iterations)
		{
			int[] tris = mesh.triangles;
			int vertexCount = normals.Length;

			// Build adjacency list: for each vertex, collect all vertex indices that share an edge.
			List<int>[] neighbours = new List<int>[vertexCount];
			for (int i = 0; i < vertexCount; i++)
			{
				neighbours[i] = new List<int>();
			}

			for (int t = 0; t < tris.Length; t += 3)
			{
				int a = tris[t];
				int b = tris[t + 1];
				int c = tris[t + 2];

				if (a < vertexCount && b < vertexCount && c < vertexCount)
				{
					neighbours[a].Add(b); neighbours[a].Add(c);
					neighbours[b].Add(a); neighbours[b].Add(c);
					neighbours[c].Add(a); neighbours[c].Add(b);
				}
			}

			// De-duplicate and remove self-references.
			for (int i = 0; i < vertexCount; i++)
			{
				List<int> unique = new List<int>();
				foreach (int n in neighbours[i])
				{
					if (n != i && !unique.Contains(n))
					{
						unique.Add(n);
					}
				}
				neighbours[i] = unique;
			}

			Vector3[] src = normals;
			Vector3[] dst = new Vector3[vertexCount];
			for (int iter = 0; iter < iterations; iter++)
			{
				for (int i = 0; i < vertexCount; i++)
				{
					List<int> nbrs = neighbours[i];
					if (nbrs.Count == 0)
					{
						dst[i] = src[i];
						continue;
					}

					Vector3 sum = Vector3.zero;
					foreach (int n in nbrs)
					{
						sum += src[n];
					}
					dst[i] = ((src[i] + sum) / (nbrs.Count + 1)).normalized;
				}

				// Swap source and destination for next iteration.
				Vector3[] tmp = src;
				src = dst;
				dst = tmp;
			}

			return src;
		}

		public void BuildPreview(SlotData slot, OverlayColorData colorData)
		{
			DestroyPreview();

			if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
			{
				return;
			}

			UMAMaterial sourceMaterial = slot.material;
			if (sourceMaterial == null || sourceMaterial.material == null)
			{
				Debug.LogWarning($"[UMASlotNormalNormalizer] Slot '{slot.slotName}' does not have a valid material.", this);
				return;
			}

			previewMesh = slot.asset.meshData.ToUnityMesh();
			previewMesh.name = slot.slotName + "_UMASlotNormalNormalizerMesh";
			previewMesh.hideFlags = HideFlags.DontSave;
			previewMesh.RecalculateBounds();

			previewMaterial = new Material(sourceMaterial.material);
			previewMaterial.name = sourceMaterial.material.name + "_UMASlotNormalNormalizerMaterial";
			previewMaterial.hideFlags = HideFlags.DontSave;
			previewMaterial.CopyPropertiesFromMaterial(sourceMaterial.material);

			if (sourceMaterial.shaderKeywords != null)
			{
				for (int i = 0; i < sourceMaterial.shaderKeywords.Count; i++)
				{
					string keyword = sourceMaterial.shaderKeywords[i];
					if (!string.IsNullOrEmpty(keyword))
					{
						previewMaterial.EnableKeyword(keyword);
					}
				}
			}

			ApplyOverlayTextures(sourceMaterial, slot);
			ApplyColorData(sourceMaterial, colorData);

			previewObject = new GameObject(slot.slotName + "_UMASlotNormalNormalizerPreview");
			previewObject.hideFlags = HideFlags.DontSave;
			previewObject.layer = gameObject.layer;
			previewObject.transform.SetParent(transform, false);

			MeshFilter meshFilter = previewObject.AddComponent<MeshFilter>();
			meshFilter.sharedMesh = previewMesh;

			MeshRenderer meshRenderer = previewObject.AddComponent<MeshRenderer>();
			int materialCount = Mathf.Max(1, previewMesh.subMeshCount);
			Material[] materials = new Material[materialCount];

            if (colorData != null && colorData.HasProperties && colorData.PropertyBlock != null && colorData.PropertyBlock.shaderProperties != null)
            {
                for (int i = 0; i < colorData.PropertyBlock.shaderProperties.Count; i++)
                {
                    UMAProperty property = colorData.PropertyBlock.shaderProperties[i];
                    if (property != null)
                    {
                        property.Apply(previewMaterial, -1);
                    }
                }
            }


			for (int i = 0; i < materialCount; i++)
			{
				materials[i] = previewMaterial;
			}
			meshRenderer.sharedMaterials = materials;
		}

		private void ApplyOverlayTextures(UMAMaterial sourceMaterial, SlotData slot)
		{
			List<OverlayData> overlays = slot.GetOverlayList();
			OverlayData firstOverlay = null;
			if (overlays != null)
			{
				for (int i = 0; i < overlays.Count; i++)
				{
					OverlayData overlay = overlays[i];
					if (overlay != null && overlay.asset != null)
					{
						firstOverlay = overlay;
						break;
					}
				}
			}

			if (firstOverlay == null || firstOverlay.asset == null)
			{
				return;
			}

			Texture[] textures = firstOverlay.textureArray;
			UMAMaterial.MaterialChannel[] channels = sourceMaterial.channels;
			if (textures == null || channels == null)
			{
				return;
			}

			int channelCount = Mathf.Min(channels.Length, textures.Length);
			for (int i = 0; i < channelCount; i++)
			{
				UMAMaterial.MaterialChannel channel = channels[i];
				if (string.IsNullOrEmpty(channel.materialPropertyName))
				{
					continue;
				}
				if (channel.NonShaderTexture)
				{
					continue;
				}
				if (channel.channelType == UMAMaterial.ChannelType.MaterialColor)
				{
					continue;
				}

				Texture texture = textures[i];
				if (texture != null && previewMaterial.HasProperty(channel.materialPropertyName))
				{
					previewMaterial.SetTexture(channel.materialPropertyName, texture);
				}
			}
		}

		private void ApplyColorData(UMAMaterial sourceMaterial, OverlayColorData colorData)
		{
			if (previewMaterial == null || colorData == null)
			{
				return;
			}

			if (colorData.HasProperties && colorData.PropertyBlock != null && colorData.PropertyBlock.shaderProperties != null)
			{
				for (int i = 0; i < colorData.PropertyBlock.shaderProperties.Count; i++)
				{
					UMAProperty property = colorData.PropertyBlock.shaderProperties[i];
					if (property != null)
					{
						property.Apply(previewMaterial, -1);
					}
				}
			}

			Color selectedColor = colorData.color;
			if (previewMaterial.HasProperty("_Color"))
			{
				previewMaterial.SetColor("_Color", selectedColor);
			}
			if (previewMaterial.HasProperty("_BaseColor"))
			{
				previewMaterial.SetColor("_BaseColor", selectedColor);
			}

			if (sourceMaterial != null && sourceMaterial.shaderParms != null && !string.IsNullOrEmpty(colorData.name))
			{
				for (int i = 0; i < sourceMaterial.shaderParms.Length; i++)
				{
					UMAMaterial.ShaderParms shaderParm = sourceMaterial.shaderParms[i];
					if (shaderParm == null)
					{
						continue;
					}

					if (shaderParm.ColorName != colorData.name)
					{
						continue;
					}

					if (!string.IsNullOrEmpty(shaderParm.ParameterName) && previewMaterial.HasProperty(shaderParm.ParameterName))
					{
						previewMaterial.SetColor(shaderParm.ParameterName, selectedColor);
					}
				}
			}
		}

		private void OnDrawGizmos()
		{
			if (normalDisplayLength <= 0f)
			{
				DrawProjectionDebugGizmos();
				return;
			}

			if (showPreviewMeshNormals && previewMesh != null)
			{
				DrawGizmoNormals(previewMesh, Color.red);
			}

			if (showCoveringMeshNormals && coveringMesh != null)
			{
				DrawGizmoNormals(coveringMesh, new Color(1f, 0.4f, 0f));
			}

			DrawProjectionDebugGizmos();
		}

		private void DrawProjectionDebugGizmos()
		{
			if (!enableDebugVisualization)
			{
				return;
			}

			float markerSize = Mathf.Max(0.002f, normalDisplayLength * 0.25f);
			if (showRayGizmos)
			{
				int count = Mathf.Min(_projectionDebugRays.Count, Mathf.Max(0, maxDebugRayCount));
				for (int i = 0; i < count; i++)
				{
					ProjectionDebugRay ray = _projectionDebugRays[i];
					Gizmos.color = ray.Color;
					Gizmos.DrawLine(transform.TransformPoint(ray.Start), transform.TransformPoint(ray.End));
				}
			}

			Gizmos.color = Color.blue;
			for (int i = 0; i < _projectionDebugClusterCentroids.Count; i++)
			{
				Gizmos.DrawSphere(transform.TransformPoint(_projectionDebugClusterCentroids[i]), markerSize);
			}

			Gizmos.color = Color.yellow;
			int unresolvedCount = Mathf.Min(_projectionDebugUnresolved.Count, Mathf.Max(0, maxDebugRayCount));
			for (int i = 0; i < unresolvedCount; i++)
			{
				Gizmos.DrawSphere(transform.TransformPoint(_projectionDebugUnresolved[i]), markerSize);
			}
		}

		private void DrawGizmoNormals(Mesh mesh, Color lineColor)
		{
			Vector3[] verts = mesh.vertices;
			Vector3[] norms = mesh.normals;
			if (verts == null || norms == null || verts.Length == 0 || norms.Length == 0)
			{
				return;
			}

			int vertexCount = Mathf.Min(verts.Length, norms.Length);

			// Cap drawn vertices to avoid freezing the editor on dense meshes.
			const int maxDraw = 500;
			int step = 1;
			if (vertexCount > maxDraw)
			{
				step = vertexCount / maxDraw;
			}

			// Draw a bright sphere at each sampled vertex — far more visible than a thin line alone.
			float sphereRadius = normalDisplayLength * 0.25f;
			Gizmos.color = Color.yellow;
			for (int i = 0; i < vertexCount; i += step)
			{
				Vector3 worldPos = transform.TransformPoint(verts[i]);
				Gizmos.DrawSphere(worldPos, sphereRadius);
			}

			// Draw the normal direction rays.
			Gizmos.color = lineColor;
			for (int i = 0; i < vertexCount; i += step)
			{
				Vector3 worldPos = transform.TransformPoint(verts[i]);
				Vector3 worldDir = transform.TransformDirection(norms[i]);
				if (worldDir.sqrMagnitude < 1e-10f)
				{
					continue;
				}
				Vector3 end = worldPos + worldDir.normalized * normalDisplayLength;
				Gizmos.DrawLine(worldPos, end);
				// Small sphere at the tip so direction is unambiguous.
				Gizmos.DrawSphere(end, sphereRadius * 0.5f);
			}
		}
#endif

		private void OnDisable()
		{
#if UNITY_EDITOR
			DestroyPreview();
#endif
		}

		private void OnDestroy()
		{
#if UNITY_EDITOR
			DestroyPreview();
#endif
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			ApplyNormalEffectivenessToPreviewMesh();
		}
#endif
	}
}