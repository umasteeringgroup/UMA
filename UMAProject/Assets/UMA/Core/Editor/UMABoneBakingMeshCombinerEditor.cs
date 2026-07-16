using UnityEngine;
using UnityEditor;

namespace UMA
{
	[CustomEditor(typeof(UMADefaultBoneBakingMeshCombiner), true)]
	public class UMABoneBakingMeshCombinerEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();
			var combiner = (UMADefaultBoneBakingMeshCombiner)target;
			EditorGUILayout.LabelField("Cached Arrays", combiner.CachedBoneWeights.ToString());
			EditorGUILayout.LabelField("Entries", combiner.CachedBoneWeightEntries.ToString());
		}

		[MenuItem("UMA/Debug/Compare Combiners On Selected")]
		public static void CompareMeshCombiners()
		{
			var go = Selection.activeGameObject;
			if (go == null)
			{
				Debug.LogError("Select a GameObject with UMAData in the scene first.");
				return;
			}
			var umaData = go.GetComponent<UMAData>();
			if (umaData == null) { Debug.LogError("No UMAData."); return; }
			var generator = umaData.umaGenerator as UMAGeneratorBuiltin;
			if (generator == null) { Debug.LogError("No UMAGeneratorBuiltin."); return; }
			var boneBaking = generator.meshCombiner as UMADefaultBoneBakingMeshCombiner;
			if (boneBaking == null) { Debug.LogError("Combiner is not a bone-baking mesh combiner."); return; }

			UMADefaultMeshCombiner defaultCombiner = null;
			var defaultCandidates = generator.GetComponents<UMADefaultMeshCombiner>();
			for (int i = 0; i < defaultCandidates.Length; i++)
			{
				if (defaultCandidates[i].GetType() == typeof(UMADefaultMeshCombiner))
				{
					defaultCombiner = defaultCandidates[i];
					break;
				}
			}
			if (defaultCombiner == null)
				defaultCombiner = generator.gameObject.AddComponent<UMADefaultMeshCombiner>();

			// Build with bone baking
			generator.meshCombiner = boneBaking;
			generator.GenerateSingleUMA(umaData, false);
			var bbMesh = umaData.GetRenderer(0)?.sharedMesh;
			if (bbMesh == null) { Debug.LogError("BB build produced no mesh."); return; }
			var bbVerts = bbMesh.vertices;
			var bbBindPoses = bbMesh.bindposes;
			var bbRenderer = umaData.GetRenderer(0);
			var bbBones = bbRenderer?.bones;
			var bbSkinnedVerts = SkinVertices(bbMesh, bbRenderer);

			// Build with default
			umaData.Dirty(true, true, true);
			generator.meshCombiner = defaultCombiner;
			generator.GenerateSingleUMA(umaData, false);
			var defMesh = umaData.GetRenderer(0)?.sharedMesh;
			if (defMesh == null) { Debug.LogError("Default build produced no mesh."); return; }
			var defVerts = defMesh.vertices;
			var defBindPoses = defMesh.bindposes;
			var defRenderer = umaData.GetRenderer(0);
			var defBones = defRenderer?.bones;
			var defSkinnedVerts = SkinVertices(defMesh, defRenderer);

			generator.meshCombiner = boneBaking;

			Debug.Log("<color=yellow>=== COMBINER COMPARISON ===</color>");
			Debug.Log($"BB: {bbVerts.Length} verts, bounds center={bbMesh.bounds.center} size={bbMesh.bounds.size}");
			Debug.Log($"DEF: {defVerts.Length} verts, bounds center={defMesh.bounds.center} size={defMesh.bounds.size}");

			int count = Mathf.Min(bbVerts.Length, defVerts.Length);
			float maxDist = 0f; int maxIdx = 0; float total = 0f; int nan = 0;
			for (int i = 0; i < count; i++)
			{
				float d = (bbVerts[i] - defVerts[i]).magnitude;
				if (float.IsNaN(d)) { nan++; continue; }
				total += d;
				if (d > maxDist) { maxDist = d; maxIdx = i; }
			}
			Debug.Log($"Vertices: maxDelta={maxDist:F4} idx={maxIdx} avgDelta={total/(count-nan):F4} NaN={nan}");
			if (maxIdx < count) Debug.Log($"  Max: BB={bbVerts[maxIdx]} DEF={defVerts[maxIdx]}");

			count = Mathf.Min(bbSkinnedVerts?.Length ?? 0, defSkinnedVerts?.Length ?? 0);
			maxDist = 0f; maxIdx = 0; total = 0f; nan = 0;
			for (int i = 0; i < count; i++)
			{
				float d = (bbSkinnedVerts[i] - defSkinnedVerts[i]).magnitude;
				if (float.IsNaN(d)) { nan++; continue; }
				total += d;
				if (d > maxDist) { maxDist = d; maxIdx = i; }
			}
			Debug.Log($"Skinned vertices: maxDelta={maxDist:F4} idx={maxIdx} avgDelta={total/(count-nan):F4} NaN={nan}");
			if (maxIdx < count) Debug.Log($"  Skinned Max: BB={bbSkinnedVerts[maxIdx]} DEF={defSkinnedVerts[maxIdx]}");

			int bpCount = Mathf.Min(bbBindPoses?.Length ?? 0, defBindPoses?.Length ?? 0);
			float maxAngle = 0f; int maxAngleIdx = 0;
			for (int i = 0; i < bpCount; i++)
			{
				float a = Quaternion.Angle(bbBindPoses[i].rotation, defBindPoses[i].rotation);
				if (a > maxAngle) { maxAngle = a; maxAngleIdx = i; }
			}
			Debug.Log($"BindPoses: maxAngleDelta={maxAngle:F4} deg idx={maxAngleIdx}");
			if (maxAngleIdx < bpCount) Debug.Log($"  BB={bbBindPoses[maxAngleIdx].rotation.eulerAngles} DEF={defBindPoses[maxAngleIdx].rotation.eulerAngles}");

			int boneCount = Mathf.Min(bbBones?.Length ?? 0, defBones?.Length ?? 0);
			int boneMismatch = 0;
			for (int i = 0; i < boneCount; i++) if (bbBones[i] != defBones[i]) boneMismatch++;
			Debug.Log($"Bones: {boneMismatch}/{boneCount} differ");
			Debug.Log("<color=yellow>=== END ===</color>");
		}

		private static Vector3[] SkinVertices(Mesh mesh, SkinnedMeshRenderer renderer)
		{
			if (mesh == null || renderer == null)
				return null;

			var vertices = mesh.vertices;
			var bindposes = mesh.bindposes;
			var bones = renderer.bones;
			if (vertices == null || bindposes == null || bones == null || bindposes.Length == 0 || bones.Length == 0)
				return vertices;

			var result = new Vector3[vertices.Length];
			var rendererWorldToLocal = renderer.transform.worldToLocalMatrix;

			var bonesPerVertex = mesh.GetBonesPerVertex();
			var allWeights = mesh.GetAllBoneWeights();
			if (bonesPerVertex.Length == vertices.Length && allWeights.Length > 0)
			{
				int weightOffset = 0;
				for (int i = 0; i < vertices.Length; i++)
				{
					Vector3 skinned = Vector3.zero;
					byte count = bonesPerVertex[i];
					for (int b = 0; b < count; b++)
					{
						var weight = allWeights[weightOffset + b];
						int boneIndex = weight.boneIndex;
						if (boneIndex < 0 || boneIndex >= bones.Length || boneIndex >= bindposes.Length || bones[boneIndex] == null)
							continue;

						var skinMatrix = rendererWorldToLocal * bones[boneIndex].localToWorldMatrix * bindposes[boneIndex];
						skinned += skinMatrix.MultiplyPoint3x4(vertices[i]) * weight.weight;
					}
					weightOffset += count;
					result[i] = skinned;
				}
				bonesPerVertex.Dispose();
				allWeights.Dispose();
				return result;
			}
			bonesPerVertex.Dispose();
			allWeights.Dispose();

			var legacyWeights = mesh.boneWeights;
			for (int i = 0; i < vertices.Length; i++)
			{
				if (legacyWeights == null || i >= legacyWeights.Length)
				{
					result[i] = vertices[i];
					continue;
				}

				var weight = legacyWeights[i];
				result[i] =
					SkinVertex(vertices[i], weight.boneIndex0, weight.weight0, bones, bindposes, rendererWorldToLocal) +
					SkinVertex(vertices[i], weight.boneIndex1, weight.weight1, bones, bindposes, rendererWorldToLocal) +
					SkinVertex(vertices[i], weight.boneIndex2, weight.weight2, bones, bindposes, rendererWorldToLocal) +
					SkinVertex(vertices[i], weight.boneIndex3, weight.weight3, bones, bindposes, rendererWorldToLocal);
			}
			return result;
		}

		private static Vector3 SkinVertex(Vector3 vertex, int boneIndex, float weight, Transform[] bones, Matrix4x4[] bindposes, Matrix4x4 rendererWorldToLocal)
		{
			if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length || boneIndex >= bindposes.Length || bones[boneIndex] == null)
				return Vector3.zero;

			var skinMatrix = rendererWorldToLocal * bones[boneIndex].localToWorldMatrix * bindposes[boneIndex];
			return skinMatrix.MultiplyPoint3x4(vertex) * weight;
		}
	}
}
