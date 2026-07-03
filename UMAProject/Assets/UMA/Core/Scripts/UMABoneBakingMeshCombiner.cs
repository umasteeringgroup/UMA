using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace UMA
{
	/// <summary>
	/// The Bone Baking mesh combiner from the UMA Power Tools third party package.
	/// </summary>
    public class UMABoneBakingMeshCombiner : UMAMeshCombiner
    {
        protected List<Material> combinedMaterialList;
		UMAImprovedSkeleton umaSkeleton;
		Matrix4x4[] inverseResolvedBoneMatrixes;
		MeshBuilder umaMesh;

		public bool dontCacheBoneWeights;

		public int CachedBoneWeights { get { return umaMesh != null ? umaMesh.CachedBoneWeights : 0; } }
		public int CachedBoneWeightEntries { get { return umaMesh != null ? umaMesh.CachedBoneWeightEntries : 0; } }

		UMAData umaData;
        int atlasResolution;
		int animatedBonesCount;
		Dictionary<int, int> mergeBoneDictionary;
		private int mergeBoneDictionaryCapacity;
		private List<Matrix4x4> _inverseResolvedBoneMatrixes;
		SkinnedMeshRenderer myRenderer;

		protected void EnsureUMADataSetup(bool updatedAtlas)
		{
			if (umaData.umaRoot == null)
			{
				DestroyGeneratedRenderers();
				umaData.SetRenderers(null);
				umaData.SetRendererAssets(null);

				GameObject newRoot = new GameObject("Root");
				newRoot.transform.parent = umaData.transform;
				newRoot.transform.localPosition = Vector3.zero;
				newRoot.transform.localRotation = Quaternion.Euler(270f, 0, 0f);
				umaData.umaRoot = newRoot;

				GameObject newGlobal = new GameObject("Global");
				newGlobal.transform.parent = newRoot.transform;
				newGlobal.transform.localPosition = Vector3.zero;
				newGlobal.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);

				umaSkeleton = new UMAImprovedSkeleton(newGlobal.transform);
				umaData.skeleton = umaSkeleton;

				GameObject newSMRGO = new GameObject("UMARenderer");
				//make UMARenderer GO respect the layer setting of the UMAAvatar so cameras can just target this layer
				newSMRGO.layer = umaData.gameObject.layer;
				newSMRGO.transform.parent = umaData.transform;
				newSMRGO.transform.localPosition = Vector3.zero;
				newSMRGO.transform.localRotation = Quaternion.Euler(0, 0, 0f);
				newSMRGO.transform.localScale = Vector3.one;


				myRenderer = newSMRGO.AddComponent<SkinnedMeshRenderer>();
				myRenderer.rootBone = newGlobal.transform;
				myRenderer.sharedMesh = new Mesh();
				umaData.SetRenderers(new SkinnedMeshRenderer[1] { myRenderer });
			}
			else
			{
				myRenderer = umaData.GetRenderer(0);
				if (updatedAtlas)
				{
					umaData.CleanMesh(false);
				}
				umaSkeleton = umaData.skeleton as UMAImprovedSkeleton;
				if (umaSkeleton == null)
				{
					// happens after compile and continue
					umaSkeleton = new UMAImprovedSkeleton(umaData.umaRoot.transform.Find("Global"));
					umaData.skeleton = umaSkeleton;
				}
			}
		}

		private void DestroyGeneratedRenderers()
		{
			var renderers = umaData.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			for (int i = 0; i < renderers.Length; i++)
			{
				var renderer = renderers[i];
				if (renderer == null || renderer.gameObject == null)
				{
					continue;
				}

				if (!renderer.gameObject.name.StartsWith("UMARenderer", StringComparison.Ordinal))
				{
					continue;
				}

				UMAUtils.DestroySceneObject(renderer.gameObject);
			}
		}


		public override void Preprocess(UMAData umaData)
		{
			umaData.isMeshDirty |= umaData.isShapeDirty;
		}

		/// <summary>
		/// Updates the UMA mesh and skeleton to match current slots.
		/// </summary>
		/// <param name="updatedAtlas">If set to <c>true</c> atlas has changed.</param>
		/// <param name="umaData">UMA data.</param>
		/// <param name="atlasResolution">Atlas resolution.</param>
        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)
        {
			this.umaData = umaData;
            this.atlasResolution = atlasResolution;

            combinedMaterialList = new List<Material>();

			umaData.ResetAnimatedBones();
            var combinedMeshArray = BuildCombineInstances();

			EnsureUMADataSetup(updatedAtlas);
			umaSkeleton.BeginSkeletonUpdate();

			if (umaMesh == null)
			{
				umaMesh = new MeshBuilder();
				umaMesh.cacheBoneWeights = !dontCacheBoneWeights;
			}

			PopulateSkeleton(combinedMeshArray);

#if UNITY_EDITOR
			// Diagnostic: check if any slot's meshData is missing umaBones (legacy UMA 2 data)
			{
				int slotsMissingBones = 0;
				int totalSourceBones = 0;
				foreach (var ci in combinedMeshArray)
				{
					if (ci.meshData.umaBones == null || ci.meshData.umaBoneCount == 0)
						slotsMissingBones++;
					else
						totalSourceBones += ci.meshData.umaBoneCount;
				}
				if (slotsMissingBones > 0)
				{
					Debug.LogWarning($"[UMABoneBakingMeshCombiner] PopulateSkeleton: {slotsMissingBones} slot(s) have no umaBones (legacy UMA 2 data). " +
						$"Skeleton has {umaSkeleton.boneCount} total bones from {totalSourceBones} source bone entries across {combinedMeshArray.Length - slotsMissingBones} slot(s) with bones.");
				}
			}
#endif

			umaData.umaRecipe.ClearDNAConverters();
			for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
			{
				SlotData slotData = umaData.umaRecipe.slotDataList[i];
				if (slotData != null)
				{
					umaData.umaRecipe.AddDNAUpdater(slotData.asset.slotDNA);
				}
			}
			umaData.ResetToTPoseAndApplyDNA();
			AddHumanoidBones();
			MarkAnimatedBones();

			// CRITICAL: The root bone ("Global") must be marked as preserved so
			// EnsureBoneHierarchy recreates its Transform if EndSkeletonUpdate
			// previously destroyed it. The parent chain (e.g. "Root") is protected
			// by EndSkeletonUpdate's anchor-chain logic and does not need explicit
			// preservation here.
			umaSkeleton.SetAnimatedBone(umaSkeleton.rootBoneHash);
			umaData.FireDNAAppliedEvents();

			// Force Transform hierarchy to match skeleton cache before computing matrices.
			// Without this, the Animator may have shifted Transforms since the last build,
			// causing PopulateMatrix to use cache values that differ from what the
			// SkinnedMeshRenderer will see at runtime.
			umaSkeleton.EnsureBoneHierarchy();

			MergeSkeletons(combinedMeshArray);
			PopulateMatrix(combinedMeshArray);

			SkinnedMeshCombinerRetargeting.CombineMeshes(umaMesh, combinedMeshArray, inverseResolvedBoneMatrixes, umaData.blendShapeSettings, uniformTargetPoses: true);

			if (updatedAtlas)
				RecalculateUV();

			umaMesh.ReleaseBuffers();
	        umaMesh.ApplyDataToUnityMesh(myRenderer, umaSkeleton);
	        umaSkeleton.EndSkeletonUpdate();

	        ApplyBlendShapes();

			myRenderer.quality = SkinQuality.Bone4;
            //umaData.myRenderer.useLightProbes = true;
			if (updatedAtlas)
			{
				var materials = combinedMaterialList.ToArray();
				myRenderer.sharedMaterials = materials;
			}
			ApplySecondPassMaterials();
			//umaData.myRenderer.sharedMesh.RecalculateBounds();
			myRenderer.sharedMesh.name = "UMAMesh";

			umaData.isShapeDirty = false;
            umaData.firstBake = false;

			umaData.umaGenerator.UpdateAvatar(umaData);
			RefreshFinalBindPoses();
			umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
			//FireSlotAtlasNotification(umaData, materials);

#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
		}

		private void RefreshFinalBindPoses()
		{
			if (myRenderer == null || myRenderer.sharedMesh == null || myRenderer.bones == null)
				return;

			var bones = myRenderer.bones;
			var bindposes = myRenderer.sharedMesh.bindposes;
			if (bindposes == null || bindposes.Length != bones.Length)
				return;

			var rendererMatrix = myRenderer.transform.localToWorldMatrix;
			float maxAngleDelta = 0f;
			int maxAngleIndex = 0;

			for (int i = 0; i < bones.Length; i++)
			{
				if (bones[i] == null)
					continue;

				var updatedBindPose = bones[i].worldToLocalMatrix * rendererMatrix;
				float angleDelta = Quaternion.Angle(bindposes[i].rotation, updatedBindPose.rotation);
				if (angleDelta > maxAngleDelta)
				{
					maxAngleDelta = angleDelta;
					maxAngleIndex = i;
				}
				bindposes[i] = updatedBindPose;
			}

			myRenderer.sharedMesh.bindposes = bindposes;
		}

		/// <summary>
		/// Handles second-pass materials by duplicating the first-pass submesh
		/// and appending the second-pass material, matching the default combiner's
		/// AssignRendererMaterials pattern.
		/// </summary>
		private void ApplySecondPassMaterials()
		{
			if (myRenderer == null || myRenderer.sharedMesh == null)
				return;

			var genMaterials = umaData.generatedMaterials.materials;
			int secondPassCount = 0;
			for (int i = 0; i < genMaterials.Count; i++)
			{
				if (genMaterials[i].umaMaterial != null && genMaterials[i].umaMaterial.secondPass != null)
				{
					secondPassCount++;
				}
			}

			var mesh = myRenderer.sharedMesh;
			var newSubMeshes = new System.Collections.Generic.List<UnityEngine.Rendering.SubMeshDescriptor>(genMaterials.Count + secondPassCount);
			var newMaterials = new System.Collections.Generic.List<Material>(genMaterials.Count + secondPassCount);

			for (int i = 0; i < genMaterials.Count; i++)
			{
				var gm = genMaterials[i];
				Material firstPass = gm.material != null ? gm.material : gm.umaMaterial != null ? gm.umaMaterial.material : null;

				var subMesh = i < mesh.subMeshCount ? mesh.GetSubMesh(i) : new UnityEngine.Rendering.SubMeshDescriptor(0, 0, MeshTopology.Triangles);
				newSubMeshes.Add(subMesh);
				newMaterials.Add(firstPass);

				if (gm.umaMaterial != null && gm.umaMaterial.secondPass != null)
				{
					Material secondPass = gm.secondPassMaterial;
					if (secondPass == null || secondPass == firstPass || secondPass.shader != gm.umaMaterial.secondPass.shader)
					{
						secondPass = UnityEngine.Object.Instantiate(gm.umaMaterial.secondPass);
						gm.secondPassMaterial = secondPass;
					}

					UMAGeneratorPro.ApplyMaterialParameters(gm, umaData, secondPass);
					UMADefaultMeshCombiner.CopyMaterialTextures(secondPass, gm.material, gm.umaMaterial);
					if (gm.material != null && gm.material.HasProperty("_OverlayCount"))
					{
						UMADefaultMeshCombiner.SetCompositingParameters(secondPass, gm);
					}

					newSubMeshes.Add(subMesh);
					newMaterials.Add(secondPass);
				}

				gm.skinnedMeshRenderer = myRenderer;
			}

			mesh.subMeshCount = newSubMeshes.Count;
			mesh.SetSubMeshes(newSubMeshes.ToArray(), UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices);
			myRenderer.sharedMaterials = newMaterials.ToArray();
		}

		private void ApplyBlendShapes()
		{
			var blendShapeSettings = umaData.blendShapeSettings;
			if (blendShapeSettings.ignoreBlendShapes) return;
			var renderers = umaData.GetRenderers();
			foreach(var entry in blendShapeSettings.blendShapes)
			{
				if (!entry.Value.isBaked || (!blendShapeSettings.ignoreBlendShapes))
				{
					var weight = entry.Value.value * 100f; //Scale up to 1-100 for SetBlendShapeWeight.

					foreach (var renderer in renderers)
					{
						if (renderer == null)
							continue;
						int index = renderer.sharedMesh.GetBlendShapeIndex(entry.Key);
						if (index >= 0)
							renderer.SetBlendShapeWeight(index, weight);
					}
				}
			}
		}

		private void AddHumanoidBones()
		{
			var tpose = umaData.umaRecipe.raceData.TPose;
			if (tpose != null)
			{
				tpose.DeSerialize();
				for (int i = 0; i < tpose.humanInfo.Length; i++)
				{
					var bone = tpose.humanInfo[i];
					var hash = UMAUtils.StringToHash(bone.boneName);
					umaData.RegisterAnimatedBone(hash);
				}
			}
		}

		private void MarkAnimatedBones()
		{
			// Collect animated bone hashes from the TPose humanoid data
			var animatedBones = new List<int>();
			var tpose = umaData.umaRecipe.raceData.TPose;
			if (tpose != null)
			{
				tpose.DeSerialize();
				for (int i = 0; i < tpose.humanInfo.Length; i++)
				{
					animatedBones.Add(UMAUtils.StringToHash(tpose.humanInfo[i].boneName));
				}
			}

			// Also collect bone hashes from each slot's UnbakedAnimatedBones list.
			// These are bones the user explicitly wants preserved during bone baking.
			var slotDataList = umaData.umaRecipe.slotDataList;
			if (slotDataList != null)
			{
				for (int s = 0; s < slotDataList.Length; s++)
				{
					var slot = slotDataList[s];
					if (slot?.asset?.UnbakedAnimatedBones == null) continue;
					for (int b = 0; b < slot.asset.UnbakedAnimatedBones.Length; b++)
					{
						string boneName = slot.asset.UnbakedAnimatedBones[b];
						if (!string.IsNullOrEmpty(boneName))
							animatedBones.Add(UMAUtils.StringToHash(boneName));
					}
				}
			}

			animatedBonesCount = animatedBones.Count;
			int foundCount = 0;
			int missingCount = 0;
			foreach (var animatedBone in animatedBones)
			{
				if (umaSkeleton.HasBone(animatedBone))
				{
					umaSkeleton.SetAnimatedBone(animatedBone);
					foundCount++;
				}
				else
				{
					missingCount++;
				}
			}

			if (missingCount > 0)
			{
				Debug.LogWarning($"[UMABoneBakingMeshCombiner] MarkAnimatedBones: {missingCount} of {animatedBonesCount} humanoid bones not found in skeleton. " +
					$"These bones will collapse to their preserved parent. Check that TPose humanInfo boneNames match the mesh bone names.");
			}
		}

		private void MergeSkeletons(SkinnedMeshCombinerRetargeting.CombineInstance[] combinedInstances)
		{
			if (mergeBoneDictionary == null)
				mergeBoneDictionary = new Dictionary<int, int>(animatedBonesCount);
			else
				mergeBoneDictionary.Clear();
			mergeBoneDictionaryCapacity = animatedBonesCount;

			var mergedBones = mergeBoneDictionary;
			foreach (var combineInstance in combinedInstances)
			{
				var meshData = combineInstance.meshData;
				combineInstance.targetBoneIndices = new int[meshData.boneNameHashes.Length];
				for (int i = 0; i < meshData.boneNameHashes.Length; i++)
				{
					var targetHash = umaSkeleton.ResolvePreservedHash(meshData.boneNameHashes[i]);
					int targetIndex;
					if (!mergedBones.TryGetValue(targetHash, out targetIndex))
					{
						targetIndex = mergedBones.Count;
						mergedBones.Add(targetHash, targetIndex);
					}
					combineInstance.targetBoneIndices[i] = targetIndex;
				}
			}
			umaMesh.PrepareBones(mergedBones.Count);
			foreach (var entry in mergedBones)
			{
				umaMesh.boneNameHashes[entry.Value] = entry.Key;
			}

#if UNITY_EDITOR
			if (mergedBones.Count <= 1)
			{
				Debug.LogWarning($"[UMABoneBakingMeshCombiner] MergeSkeletons: only {mergedBones.Count} merged bone(s). " +
					$"All vertices will be weighted to a single bone — mesh may appear invisible. " +
					$"Skeleton has {umaSkeleton.boneCount} bones. Check that bones are marked as preserved.");
			}
#endif
		}


		private void PopulateMatrix(SkinnedMeshCombinerRetargeting.CombineInstance[] combinedInstances)
		{
			int identityBindPoses = 0;
			int totalBindPoses = 0;

			foreach (var combineInstance in combinedInstances)
			{
				var meshData = combineInstance.meshData;
				combineInstance.resolvedBoneMatrixes = new Matrix4x4[meshData.boneNameHashes.Length];
				for(int i = 0; i < meshData.boneNameHashes.Length; i++)
				{
					var boneNameHash = meshData.boneNameHashes[i];
					// Ensure a Transform exists for this bone so we always use the
					// Unity-computed localToWorldMatrix (not the skeleton cache's
					// CalculateMatrix fallback, which can disagree for non-preserved
					// bones whose cache data may not match the actual hierarchy).
					if (!umaSkeleton.HasBoneTransform(boneNameHash))
						umaSkeleton.EnsureBoneTransform(boneNameHash);

					var boneXform = umaSkeleton.GetBoneTransform(boneNameHash);
					var boneMatrix = boneXform.localToWorldMatrix;
					MatrixMultiply(ref combineInstance.resolvedBoneMatrixes[i], ref boneMatrix, ref meshData.bindPoses[i]);

					// Track identity bind poses (common in legacy UMA 2 data)
					if (meshData.bindPoses != null && i < meshData.bindPoses.Length)
					{
						totalBindPoses++;
						if (meshData.bindPoses[i] == Matrix4x4.identity)
							identityBindPoses++;
					}
				}
			}

#if false
			if (identityBindPoses > 0 && identityBindPoses == totalBindPoses)
			{
				Debug.LogWarning($"[UMABoneBakingMeshCombiner] PopulateMatrix: ALL {totalBindPoses} bind poses are identity. " +
					$"This causes incorrect skinning — vertices will not transform correctly. Legacy UMA 2 slots may need reimport.");
			}
			else if (identityBindPoses > 0)
			{
				Debug.LogWarning($"[UMABoneBakingMeshCombiner] PopulateMatrix: {identityBindPoses} of {totalBindPoses} bind poses are identity.");
			}
#endif

			// Bind poses are defined relative to the SkinnedMeshRenderer transform,
			// not renderer.rootBone. The baked vertices must therefore be written in
			// renderer local space, matching Unity's bind pose convention:
			// bindpose = bone.worldToLocalMatrix * renderer.localToWorldMatrix.
			ListHelper<Matrix4x4>.AllocateArray(ref _inverseResolvedBoneMatrixes, out inverseResolvedBoneMatrixes, umaMesh.bonesCount);
			var rootMatrix = myRenderer != null ? myRenderer.transform.localToWorldMatrix : Matrix4x4.identity;
			var rootMatrixInv = rootMatrix.inverse;

			for (int i = 0; i < umaMesh.bonesCount; i++)
			{
				var boneXform = umaSkeleton.GetBoneTransform(umaMesh.boneNameHashes[i]);
				var boneMatrix = boneXform != null ? boneXform.localToWorldMatrix : umaSkeleton.GetLocalToWorldMatrix(umaMesh.boneNameHashes[i]);
				var boneMatrixInv = boneMatrix.inverse;
				MatrixMultiply(ref umaMesh.bindPoses[i], ref boneMatrixInv, ref rootMatrix);
				inverseResolvedBoneMatrixes[i] = rootMatrixInv;
			}

		}

		private void MatrixMultiply(ref Matrix4x4 result, ref Matrix4x4 lhs, ref Matrix4x4 rhs)
		{
			result.m00 = lhs.m00 * rhs.m00 + lhs.m01 * rhs.m10 + lhs.m02 * rhs.m20 + lhs.m03 * rhs.m30;
			result.m01 = lhs.m00 * rhs.m01 + lhs.m01 * rhs.m11 + lhs.m02 * rhs.m21 + lhs.m03 * rhs.m31;
			result.m02 = lhs.m00 * rhs.m02 + lhs.m01 * rhs.m12 + lhs.m02 * rhs.m22 + lhs.m03 * rhs.m32;
			result.m03 = lhs.m00 * rhs.m03 + lhs.m01 * rhs.m13 + lhs.m02 * rhs.m23 + lhs.m03 * rhs.m33;
			result.m10 = lhs.m10 * rhs.m00 + lhs.m11 * rhs.m10 + lhs.m12 * rhs.m20 + lhs.m13 * rhs.m30;
			result.m11 = lhs.m10 * rhs.m01 + lhs.m11 * rhs.m11 + lhs.m12 * rhs.m21 + lhs.m13 * rhs.m31;
			result.m12 = lhs.m10 * rhs.m02 + lhs.m11 * rhs.m12 + lhs.m12 * rhs.m22 + lhs.m13 * rhs.m32;
			result.m13 = lhs.m10 * rhs.m03 + lhs.m11 * rhs.m13 + lhs.m12 * rhs.m23 + lhs.m13 * rhs.m33;
			result.m20 = lhs.m20 * rhs.m00 + lhs.m21 * rhs.m10 + lhs.m22 * rhs.m20 + lhs.m23 * rhs.m30;
			result.m21 = lhs.m20 * rhs.m01 + lhs.m21 * rhs.m11 + lhs.m22 * rhs.m21 + lhs.m23 * rhs.m31;
			result.m22 = lhs.m20 * rhs.m02 + lhs.m21 * rhs.m12 + lhs.m22 * rhs.m22 + lhs.m23 * rhs.m32;
			result.m23 = lhs.m20 * rhs.m03 + lhs.m21 * rhs.m13 + lhs.m22 * rhs.m23 + lhs.m23 * rhs.m33;
			result.m30 = lhs.m30 * rhs.m00 + lhs.m31 * rhs.m10 + lhs.m32 * rhs.m20 + lhs.m33 * rhs.m30;
			result.m31 = lhs.m30 * rhs.m01 + lhs.m31 * rhs.m11 + lhs.m32 * rhs.m21 + lhs.m33 * rhs.m31;
			result.m32 = lhs.m30 * rhs.m02 + lhs.m31 * rhs.m12 + lhs.m32 * rhs.m22 + lhs.m33 * rhs.m32;
			result.m33 = lhs.m30 * rhs.m03 + lhs.m31 * rhs.m13 + lhs.m32 * rhs.m23 + lhs.m33 * rhs.m33;
		}

		private void PopulateSkeleton(SkinnedMeshCombinerRetargeting.CombineInstance[] combinedInstances)
		{
			foreach (var combineInstance in combinedInstances)
			{
				var meshData = combineInstance.meshData;
				for (int i = 0; i < meshData.umaBoneCount; i++)
				{
					var umaBone = meshData.umaBones[i];
					if (!umaSkeleton.BoneAddedThisUpdate(umaBone.hash))
					{
						umaSkeleton.AddBone(umaBone);
					}
				}
			}
		}


		//private void FireSlotAtlasNotification(UMAData umaData, Material[] materials)
		//{
		//    for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
		//    {
		//        for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
		//        {
		//            var materialDefinition = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex];
		//            var slotData = materialDefinition.source.slotData;
		//            if (slotData.SlotAtlassed != null)
		//            {
		//                slotData.SlotAtlassed.Invoke(umaData, slotData, materials[atlasIndex], materialDefinition.atlasRegion);
		//            }
		//        }
		//    }
		//    SlotData[] slots = umaData.umaRecipe.slotDataList;
		//    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
		//    {
		//        var slotData = slots[slotIndex];
		//        if (slotData == null) continue;
		//        if (slotData.textureNameList.Length == 1 && string.IsNullOrEmpty(slotData.textureNameList[0]))
		//        {
		//            if (slotData.SlotAtlassed != null)
		//            {
		//                slotData.SlotAtlassed.Invoke(umaData, slotData, materials[atlasIndex], materialDefinition.atlasRegion);
		//            }
		//        }
		//    }
		//}

        protected SkinnedMeshCombinerRetargeting.CombineInstance[] BuildCombineInstances()
        {
			var combinedMeshList = new List<SkinnedMeshCombinerRetargeting.CombineInstance>();

			SkinnedMeshCombinerRetargeting.CombineInstance combineInstance;

            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
				var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
				combinedMaterialList.Add(generatedMaterial.material);

				for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
                {
					var materialDefinition = generatedMaterial.materialFragments[materialDefinitionIndex];
					var slotData = materialDefinition.slotData;
					if (slotData == null || slotData.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slotData.asset.meshData)) continue;

					// Shallow-copy the asset mesh data to prevent mutating shared
					// asset state across builds. The bone-baking combiner pipeline
					// (ConvertLegacyBoneWeights / LoadBoneWeights) writes to
					// ManagedBoneWeights, ManagedBonesPerVertex, and vertexCount
					// on the source, which would permanently corrupt the asset.
					var md = slotData.asset.meshData;
				combineInstance = new SkinnedMeshCombinerRetargeting.CombineInstance();
				combineInstance.meshData = md.ShallowCopy(null);

				// Ensure bone weights on the copy (not the asset)
				var copyMd = combineInstance.meshData;
				int vertCount = copyMd.vertices != null ? copyMd.vertices.Length : copyMd.vertexCount;
				bool needsConversion = (copyMd.ManagedBonesPerVertex == null || copyMd.ManagedBonesPerVertex.Length != vertCount);
				if (needsConversion && copyMd.boneWeights != null && copyMd.boneWeights.Length > 0)
					copyMd.LoadBoneWeights();
					// Apply mesh modifiers (position bones, etc.) — default combiner does this
					if (slotData.meshModifiers != null && slotData.meshModifiers.Count > 0)
					{
						for (int m = 0; m < slotData.meshModifiers.Count; m++)
						{
							var mod = slotData.meshModifiers[m];
							if (mod != null)
								combineInstance.meshData = mod.Process(combineInstance.meshData);
						}
					}
					combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
					for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
					{
						combineInstance.targetSubmeshIndices[i] = -1;
					}
					combineInstance.targetSubmeshIndices[slotData.asset.subMeshIndex] = materialIndex;
                    combinedMeshList.Add(combineInstance);
					if (materialDefinition.overlayData != null)
					{
						for (int i = 0; i < materialDefinition.overlayData.Length; i++)
						{
							var overlay = materialDefinition.overlayData[i];
							if (overlay == null || overlay.asset == null) continue;
							var occlusion = overlay.asset.GetOcclusion(slotData.asset.nameHash, slotData.asset.subMeshIndex);
							if (occlusion != null)
							{
								if (combineInstance.triangleOcclusion == null)
									combineInstance.triangleOcclusion = new int[combineInstance.meshData.subMeshCount][];
								combineInstance.triangleOcclusion[slotData.asset.subMeshIndex] = occlusion;
							}
						}
					}

					if (slotData.asset.SlotAtlassed != null)
					{
						slotData.asset.SlotAtlassed.Invoke(umaData, slotData, generatedMaterial.material, materialDefinition.atlasRegion);
					}
                }
            }
			return combinedMeshList.ToArray();
        }

		protected void RecalculateUV()
        {
            int idx = 0;
            //Handle Atlassed Verts
            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
				var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
				if (generatedMaterial.umaMaterial.materialType != UMAMaterial.MaterialType.Atlas)
				{
					// Non-atlas materials: skip all fragments (each contributed vertices
					// to the combined mesh via BuildCombineInstances).
					for (int fragIdx = 0; fragIdx < generatedMaterial.materialFragments.Count; fragIdx++)
					{
						var fragment = generatedMaterial.materialFragments[fragIdx];
						int vertexCount = fragment.slotData.asset.meshData.vertices.Length;
						idx += vertexCount;
					}
					continue;
				}

				for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
                {
					var fragment = generatedMaterial.materialFragments[materialDefinitionIndex];
					var tempAtlasRect = fragment.atlasRegion;
					int vertexCount = fragment.slotData.asset.meshData.vertices.Length;
					float atlasXMin = tempAtlasRect.xMin / atlasResolution;
					float atlasXMax = tempAtlasRect.xMax / atlasResolution;
					float atlasXRange = atlasXMax - atlasXMin;
					float atlasYMin = tempAtlasRect.yMin / atlasResolution;
					float atlasYMax = tempAtlasRect.yMax / atlasResolution;
					float atlasYRange = atlasYMax - atlasYMin;

					while (vertexCount-- > 0)
                    {
						umaMesh.uv[idx].x = atlasXMin + atlasXRange * umaMesh.uv[idx].x;
						umaMesh.uv[idx].y = atlasYMin + atlasYRange * umaMesh.uv[idx].y;
						idx++;
                    }
				}
			}
        }
	}
}
