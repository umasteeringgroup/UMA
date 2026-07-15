#define UMA_COMBINER_TIMINGS
using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UMA.Dynamics;

namespace UMA
{
    public class UMAJobifiedMeshCombiner : UMAMeshCombiner
    {
#if UNITY_EDITOR
        private const bool UMA_INTERNALLOD_BUILD_DIAGNOSTICS = true;
#endif
        protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
        protected List<UMAData.GeneratedMaterial> combinedMaterialList;

        // These buffers are deliberately per-combiner. Static scratch collections made two
        // UMAJobifiedMeshCombiner instances corrupt each other's in-progress builds.
        private readonly List<Material> _materialBuffer = new List<Material>(32);
        private readonly List<SubMeshDescriptor> _submeshBuffer = new List<SubMeshDescriptor>(32);
        private readonly List<UMAData.GeneratedMaterial> _filteredMaterials = new List<UMAData.GeneratedMaterial>(32);
        private readonly List<int> _emptyRendererIndices = new List<int>(4);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void StaticInitializeOnLoad()
        {
#if UMA_COMBINER_TIMINGS
            ResetCombinerTimings();
#endif
        }

        // Optional timings (enable by define UMA_COMBINER_TIMINGS)
#if UMA_COMBINER_TIMINGS
        public static long Ticks_BuildCombineInstances;
        public static long Ticks_PerRendererTotal;
        public static long Ticks_PerRendererMaterials;
        public static long Ticks_LegacyUV;
        public static long Ticks_SkeletonEnsure;
        public static long Ticks_ClearDNA;
        public static long Ticks_EnsureUMADataSetup;
        public static long Ticks_BuildActiveModifiers;

        public static void ResetCombinerTimings()
        {
            Ticks_BuildCombineInstances = 0;
            Ticks_PerRendererTotal = 0;
            Ticks_PerRendererMaterials = 0;
            Ticks_LegacyUV = 0;
            Ticks_SkeletonEnsure = 0;
            Ticks_ClearDNA = 0;
            Ticks_EnsureUMADataSetup = 0;
            Ticks_BuildActiveModifiers = 0;
        }
#endif

        UMAData umaData;
        int atlasResolution;
        private UMAClothProperties clothProperties;
        int currentRendererIndex;
        SkinnedMeshRenderer[] renderers;

        protected void EnsureUMADataSetup(UMAData umaData)
        {
            if (umaData.umaRecipe != null)
            {
                umaData.umaRecipe.UpdateMeshHideMasks(umaData.currentLODLevel);
            }

            #region SetupSkeleton
            // First, ensure that the skeleton is setup, and if not,
            // then generate the root, global and set it up.
            if (umaData.umaRoot == null)
            {
                umaData.SetupSkeleton();
            }
            else
            {
                umaData.CheckSkeletonSetup();
            }
            #endregion
            if (umaData.umaRoot != null)
            {
                // The MeshData combiner sets the destination index format on every rebuild.
                // Do not mutate a previous renderer mesh here: it may have been made
                // non-readable by UploadMeshData(true), and it is not the source of truth for
                // the new index buffer in any case.
                umaData.CleanMesh(false);

                renderers = umaData.GetRenderers();
                if (UMAAssetIndexer.Instance.generator.alwaysRegenerateRenderers)
                {
                    if (renderers != null)
                    {
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            if (renderers[i] != null)
                            {
                                DestroyImmediate(renderers[i].gameObject);
                            }
                        }
                    }
                    renderers = null;
                    umaData.SetRenderers(null);
                    umaData.SetRendererAssets(null);
                }

                if ((umaData.RendererCount == umaData.generatedMaterials.rendererAssets.Count && umaData.AreRenderersEqual(umaData.generatedMaterials.rendererAssets)))
                {
                    umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
                }
                else
                {
                    var oldRenderers = renderers;
                    var globalTransform = umaData.GetGlobalTransform();

                    renderers = new SkinnedMeshRenderer[umaData.generatedMaterials.rendererAssets.Count];

                    for (int i = 0; i < umaData.generatedMaterials.rendererAssets.Count; i++)
                    {
                        if (oldRenderers != null && oldRenderers.Length > i)
                        {
                            renderers[i] = oldRenderers[i];
                            renderers[i].rootBone = globalTransform;
                            if (umaData.generatedMaterials.rendererAssets[i] != null)
                            {
                                umaData.generatedMaterials.rendererAssets[i].ApplySettingsToRenderer(renderers[i]);
                            }
                            else
                            {
                                umaData.ResetRendererSettings(i);
                                if (umaData.defaultRendererAsset != null)
                                {
                                    umaData.defaultRendererAsset.ApplySettingsToRenderer(renderers[i]);
                                }
                            }

                            continue;
                        }
                        UMARendererAsset rendererAsset = umaData.generatedMaterials.rendererAssets[i];
                        if (rendererAsset == null)
                        {
                            rendererAsset = umaData.defaultRendererAsset;
                        }

                        renderers[i] = MakeRenderer(i, umaData, globalTransform, rendererAsset);
                    }

                    if (oldRenderers != null)
                    {
                        for (int i = umaData.generatedMaterials.rendererAssets.Count; i < oldRenderers.Length; i++)
                        {
                            DestroyImmediate(oldRenderers[i].gameObject);
                            //For cloth, be aware of issue: 845868
                            //https://issuetracker.unity3d.com/issues/cloth-repeatedly-destroying-objects-with-cloth-components-causes-a-crash-in-unity-cloth-updatenormals
                        }
                    }
                    umaData.SetRenderers(renderers);
                    umaData.SetRendererAssets(umaData.generatedMaterials.rendererAssets.ToArray());
                }
                return;
            }

            //Clear out old cloth components
            for (int i = 0; i < umaData.RendererCount; i++)
            {
                Cloth cloth = renderers[i].GetComponent<Cloth>();
                if (cloth != null)
                {
                    DestroyImmediate(cloth, false); //Crashes if trying to use Destroy()
                }
            }
        }

        private SkinnedMeshRenderer MakeRenderer(int i, UMAData umaData, Transform rootBone, UMARendererAsset rendererAsset = null)
        {
            GameObject newSMRGO = new GameObject(i == 0 ? "UMARenderer" : ("UMARenderer " + i));
            newSMRGO.transform.parent = umaData.transform;
            newSMRGO.transform.localPosition = Vector3.zero;
            newSMRGO.transform.localRotation = Quaternion.Euler(0, 0, 0f);
            newSMRGO.transform.localScale = Vector3.one;
            newSMRGO.gameObject.layer = umaData.gameObject.layer;

            var newRenderer = newSMRGO.AddComponent<SkinnedMeshRenderer>();
            // newSMRGO.AddComponent<DestroyDebugger>();
            newRenderer.enabled = false;
            newRenderer.sharedMesh = new Mesh();
            if (umaData.markDynamic)
            {
                newRenderer.sharedMesh.MarkDynamic();
            }

            if (umaData.force32bit)
            {
                newRenderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            else
            {
                newRenderer.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            }
            newRenderer.rootBone = rootBone;
            newRenderer.quality = SkinQuality.Auto;
            newRenderer.sharedMesh.name = i == 0 ? "UMAMesh" : ("UMAMesh " + i);

            if (rendererAsset != null)
            {
                rendererAsset.ApplySettingsToRenderer(newRenderer);
            }

            return newRenderer;
        }

        /// <summary>
        /// Updates the UMA mesh and skeleton to match current slots.
        /// </summary>
        /// <param name="updatedAtlas">If set to <c>true</c> atlas has changed.</param>
        /// <param name="umaData">UMA data.</param>
        /// <param name="atlasResolution">Atlas resolution.</param>
        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)
        {
#if UMA_COMBINER_TIMINGS
            var swRendererTotal = System.Diagnostics.Stopwatch.StartNew();
#endif
            this.umaData = umaData;
            this.atlasResolution = atlasResolution;

            // Reuse lists instead of re-allocating each update
            if (combinedMeshList == null)
                combinedMeshList = new List<SkinnedMeshCombiner.CombineInstance>(umaData.umaRecipe.slotDataList.Length);
            else
                combinedMeshList.Clear();

            if (combinedMaterialList == null)
                combinedMaterialList = new List<UMAData.GeneratedMaterial>(umaData.generatedMaterials.materials.Count);
            else
                combinedMaterialList.Clear();

#if UMA_COMBINER_TIMINGS
             var swSingleStop = System.Diagnostics.Stopwatch.StartNew();
#endif
            EnsureUMADataSetup(umaData);
#if UMA_COMBINER_TIMINGS
            swSingleStop.Stop();
            Ticks_EnsureUMADataSetup += swSingleStop.ElapsedTicks;
#endif

            // (Outer skeleton BeginSkeletonUpdate removed: bones ensured inside renderer loop)
#if UMA_COMBINER_TIMINGS
            swSingleStop.Restart();
#endif
            umaData.BuildActiveModifiers();
#if UMA_COMBINER_TIMINGS
            swSingleStop.Stop();
            Ticks_BuildActiveModifiers += swSingleStop.ElapsedTicks;
#endif
            // Precompute baked blendshapes dict once (MeshAPI path)
            Dictionary<string, float> bakedBlendshapes = null;
            bakedBlendshapes = BuildBakedBlendshapeDict(umaData.blendShapeSettings);

            var rendererAssets = umaData.generatedMaterials.rendererAssets;
            renderers = umaData.GetRenderers();
            if (renderers == null || renderers.Length != rendererAssets.Count)
                throw new InvalidOperationException($"Jobified mesh combiner expected {rendererAssets.Count} renderers but found {renderers?.Length ?? 0}.");
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex] == null)
                    throw new InvalidOperationException($"Jobified mesh combiner renderer {rendererIndex} is missing.");
            }

            var rendererBatches = new List<SkinnedMeshCombinerMeshAPI.RendererBatch>(rendererAssets.Count);
            var rendererMaterials = new UMAData.GeneratedMaterial[rendererAssets.Count][];
            var rendererClothProperties = new UMAClothProperties[rendererAssets.Count];
            _emptyRendererIndices.Clear();

            for (currentRendererIndex = 0; currentRendererIndex < rendererAssets.Count; currentRendererIndex++)
            {
#if UMA_COMBINER_TIMINGS
                var swPerRenderer = System.Diagnostics.Stopwatch.StartNew();
#endif
                var targetRendererAsset = rendererAssets[currentRendererIndex];
                combinedMeshList.Clear();
                combinedMaterialList.Clear();
                clothProperties = null;

                // Filter materials once for this renderer asset
                _filteredMaterials.Clear();
                var gmats = umaData.generatedMaterials.materials;
                for (int m = 0; m < gmats.Count; m++)
                {
                    var gm = gmats[m];
                    if (gm != null && gm.rendererAsset == targetRendererAsset)
                        _filteredMaterials.Add(gm);
                }

                // Build combine instances
#if UMA_COMBINER_TIMINGS
                var swBuildCI = System.Diagnostics.Stopwatch.StartNew();
#endif
                BuildCombineInstancesFiltered(_filteredMaterials);
#if UMA_COMBINER_TIMINGS
                swBuildCI.Stop();
                Ticks_BuildCombineInstances += swBuildCI.ElapsedTicks;
#endif
                if (combinedMeshList.Count == 0)
                {
                    for (int materialIndex = 0; materialIndex < _filteredMaterials.Count; materialIndex++)
                    {
                        if (_filteredMaterials[materialIndex] != null)
                            _filteredMaterials[materialIndex].skinnedMeshRenderer = null;
                    }
                    // Defer renderer mutation until every non-empty batch has validated and
                    // combined successfully. A malformed later source must not leave earlier
                    // empty renderers cleared while the rest of the character remains old.
                    _emptyRendererIndices.Add(currentRendererIndex);
                    continue;
                }

                if (updatedAtlas)
                    SetSlotUVAreasForRendererFiltered(_filteredMaterials);

                rendererMaterials[currentRendererIndex] = combinedMaterialList.ToArray();
                rendererClothProperties[currentRendererIndex] = clothProperties;
                rendererBatches.Add(new SkinnedMeshCombinerMeshAPI.RendererBatch
                {
                    Renderer = renderers[currentRendererIndex],
                    Sources = combinedMeshList.ToArray(),
                    CurrentRendererIndex = currentRendererIndex,
                    AtlasResolution = atlasResolution,
                    SkipSkeletonUpdate = true
                });

#if UMA_COMBINER_TIMINGS
                swPerRenderer.Stop();
                Ticks_PerRendererTotal += swPerRenderer.ElapsedTicks;
#endif
            }

            if (rendererBatches.Count > 0)
            {
                var boundsRotation = umaData.umaRecipe.raceData.FixupRotations
                    ? SkinnedMeshCombinerMeshAPI.FixupRotation
                    : Quaternion.identity;
                var clothResults = SkinnedMeshCombinerMeshAPI.CombineIntoRenderers(
                    rendererBatches.ToArray(),
                    umaData,
                    bakedBlendshapes,
                    boundsRotation,
                    umaData.markDynamic,
                    false);

                for (int batchIndex = 0; batchIndex < rendererBatches.Count; batchIndex++)
                {
                    int rendererIndex = rendererBatches[batchIndex].CurrentRendererIndex;
                    var renderer = renderers[rendererIndex];
                    SetupCloth(rendererIndex, clothResults[batchIndex], rendererClothProperties[rendererIndex]);

                    if (rendererIndex == 0 && renderer.sharedMesh != null)
                        umaData.originalMeshBounds = renderer.sharedMesh.bounds;

#if UMA_COMBINER_TIMINGS
                    var swMaterials = System.Diagnostics.Stopwatch.StartNew();
#endif
                    AssignRendererMaterials(renderer, rendererMaterials[rendererIndex]);
#if UMA_COMBINER_TIMINGS
                    swMaterials.Stop();
                    Ticks_PerRendererMaterials += swMaterials.ElapsedTicks;
#endif
                }
            }

            for (int emptyIndex = 0; emptyIndex < _emptyRendererIndices.Count; emptyIndex++)
                ClearRendererState(_emptyRendererIndices[emptyIndex]);

#if UMA_COMBINER_TIMINGS
            var swMat = System.Diagnostics.Stopwatch.StartNew();
#endif
            umaData.umaRecipe.ClearDNAConverters();
            var slots = umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var sd = slots[i];
                if (sd != null && !sd.isBlendShapeSource && !sd.isPlaceholderSlot && sd.asset != null)
                    umaData.umaRecipe.AddDNAUpdater(sd.asset.slotDNA);
            }
#if UMA_COMBINER_TIMINGS
            swMat.Stop();
            Ticks_ClearDNA += swMat.ElapsedTicks;
#endif
            umaData.firstBake = false;

#if UMA_COMBINER_TIMINGS
            swRendererTotal.Stop();
#endif
        }
        private void SetupCloth(int rendererIndex, ClothSkinningCoefficient[] clothSkinning, UMAClothProperties properties)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            Cloth existingCloth = renderer.GetComponent<Cloth>();
            if (clothSkinning == null || clothSkinning.Length == 0)
            {
                if (existingCloth != null)
                    DestroyImmediate(existingCloth, false);
                return;
            }

            if (clothSkinning != null && clothSkinning.Length > 0)
            {
                Cloth cloth = existingCloth;
                if (cloth != null)
                {
                    DestroyImmediate(cloth, false);
                    cloth = null;
                }

                cloth = renderer.gameObject.AddComponent<Cloth>();
                UMAPhysicsAvatar physicsAvatar = renderer.gameObject.GetComponentInParent<UMAPhysicsAvatar>();
                if (physicsAvatar != null)
                {
                    cloth.sphereColliders = physicsAvatar.SphereColliders.ToArray();
                    cloth.capsuleColliders = physicsAvatar.CapsuleColliders.ToArray();
                }

                cloth.coefficients = clothSkinning;
                properties?.ApplyValues(cloth);
            }
        }

        private void ClearRendererState(int rendererIndex)
        {
            if (renderers == null || (uint)rendererIndex >= (uint)renderers.Length) return;
            var renderer = renderers[rendererIndex];
            if (renderer == null) return;

            var cloth = renderer.GetComponent<Cloth>();
            if (cloth != null)
                DestroyImmediate(cloth, false);

            var mesh = renderer.sharedMesh;
            if (mesh != null)
            {
                if (mesh.isReadable)
                {
                    mesh.Clear();
                    mesh.ClearBlendShapes();
                    mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
                }
                else
                {
                    var oldMesh = mesh;
                    mesh = new Mesh { name = oldMesh.name };
                    if (umaData != null && umaData.markDynamic) mesh.MarkDynamic();
                    mesh.indexFormat = umaData != null && umaData.force32bit ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    renderer.sharedMesh = mesh;
                    UMAUtils.DestroySceneObject(oldMesh);
                }
            }
            renderer.localBounds = new Bounds(Vector3.zero, Vector3.zero);
            renderer.sharedMaterials = Array.Empty<Material>();
            renderer.bones = Array.Empty<Transform>();
            if (rendererIndex == 0 && umaData != null)
                umaData.originalMeshBounds = new Bounds(Vector3.zero, Vector3.zero);
        }

		// Add this helper inside UMADefaultMeshCombiner (e.g., below RecalculateUV)
		private void SetSlotUVAreasForRendererFiltered(List<UMAData.GeneratedMaterial> materialsForRenderer) {
			for(int materialIndex = 0; materialIndex < materialsForRenderer.Count; materialIndex++) {
				var generatedMaterial = materialsForRenderer[materialIndex];
				// Only process materials for this renderer
				if(generatedMaterial.rendererAsset != umaData.GetRendererAsset(currentRendererIndex))
					continue;

				for(int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++) {
					var fragment = generatedMaterial.materialFragments[materialDefinitionIndex];
					if(fragment?.slotData == null || UMAMeshData.IsNullOrEmptyMeshData(fragment.slotData.asset?.meshData))
						continue;

					var sdTemp = fragment.slotData;
					var tempAtlasRect = fragment.atlasRegion;
					int vertexCount = sdTemp.asset.meshData.vertices.Length;

					// Normalize rect by atlas resolution
					float atlasXMin = tempAtlasRect.xMin / atlasResolution;
					float atlasXMax = tempAtlasRect.xMax / atlasResolution;
					float atlasYMin = tempAtlasRect.yMin / atlasResolution;
					float atlasYMax = tempAtlasRect.yMax / atlasResolution;
					float atlasXRange = atlasXMax - atlasXMin;
					float atlasYRange = atlasYMax - atlasYMin;

					// Shared-rect adjustment (matches RecalculateUV conditions)
					if(fragment.isRectShared && sdTemp.useAtlasOverlay) {
						OverlayData foundRect = null;
						for(int i = 0; i < fragment.overlayList.Count; i++) {
							var szname = fragment.overlayList[i];
							if(sdTemp.slotName != null && szname.overlayName != null && szname.overlayName.Contains(sdTemp.slotName)) {
								foundRect = szname;
								break;
							}
						}
						if(foundRect != null && foundRect.rect != Rect.zero) {
							var size = foundRect.rect.size * generatedMaterial.resolutionScale;
							var offsetX = foundRect.rect.x * generatedMaterial.resolutionScale.x;
							var offsetY = foundRect.rect.y * generatedMaterial.resolutionScale.y;

							atlasXMin += (offsetX / generatedMaterial.cropResolution.x);
							atlasXRange = size.x / generatedMaterial.cropResolution.x;

							atlasYMin += (offsetY / generatedMaterial.cropResolution.y);
							atlasYRange = size.y / generatedMaterial.cropResolution.y;
						}
					}

					// Set UV area (normalized 0..1)
					sdTemp.UVArea.Set(atlasXMin, atlasYMin, atlasXRange, atlasYRange);
				}
			}
		}

        private void ApplyClothIfNeeded(ClothSkinningCoefficient[] clothSkinning)
        {
            var cloth = renderers[currentRendererIndex].GetComponent<Cloth>();
            if (clothProperties != null)
            {
                if (cloth == null) cloth = renderers[currentRendererIndex].gameObject.AddComponent<Cloth>();
                if (clothSkinning != null && clothSkinning.Length > 0)
                    cloth.coefficients = clothSkinning;
                clothProperties.ApplyValues(cloth);

            }
            else
            {
                if (cloth != null)
                {
                    UMAUtils.DestroySceneObject(cloth);
                }
            }
        }

        protected void RecalculateUV(UMAMeshData umaMesh)
        {
            int idx = 0;
            //Handle Atlassed Verts
            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
                var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];

                if (generatedMaterial.rendererAsset != umaData.GetRendererAsset(currentRendererIndex))
                {
                    continue;
                }

                if (!generatedMaterial.umaMaterial.IsGeneratedTextures)
                //if (generatedMaterial.umaMaterial.materialType != UMAMaterial.MaterialType.Atlas)
                {
                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                    {
                        UMAData.MaterialFragment fragment = generatedMaterial.materialFragments[i];
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
                    SlotData sdTemp = fragment.slotData;
                    UMAMeshData meshData = sdTemp.asset.meshData;


                    // code below is for UVs remap based on rel pos in the atlas
                    if (fragment.isRectShared && fragment.slotData.useAtlasOverlay)
                    {
                        OverlayData foundRect = null;
                        for (int i = 0; i < fragment.overlayList.Count; i++)
                        {
                            OverlayData szname = fragment.overlayList[i];
                            if (fragment.slotData.slotName != null && szname.overlayName != null && szname.overlayName.Contains(fragment.slotData.slotName))
                            {
                                foundRect = szname;
                                break;
                            }
                        }
                        if (foundRect != null && foundRect.rect != Rect.zero)
                        {
                            var size = foundRect.rect.size * generatedMaterial.resolutionScale;
                            var offsetX = foundRect.rect.x * generatedMaterial.resolutionScale.x;
                            var offsetY = foundRect.rect.y * generatedMaterial.resolutionScale.y;

                            atlasXMin += (offsetX / generatedMaterial.cropResolution.x);
                            atlasXRange = size.x / generatedMaterial.cropResolution.x;

                            atlasYMin += (offsetY / generatedMaterial.cropResolution.y);
                            atlasYRange = size.y / generatedMaterial.cropResolution.y;
                        }
                    }

                    var sd = fragment.slotData;
                    sd.UVArea.Set(atlasXMin, atlasYMin, atlasXRange, atlasYRange);

                    while (vertexCount-- > 0)
                    {
#if UNITY_EDITOR
                        try
                        {
#endif
                        umaMesh.uv[idx].x = atlasXMin + atlasXRange * umaMesh.uv[idx].x;
                        umaMesh.uv[idx].y = atlasYMin + atlasYRange * umaMesh.uv[idx].y;
#if UNITY_EDITOR
                        }
                        catch
                        {
                            Debug.LogError("Error adjusting UVs for " + sd.slotName + " atlasRect:" + tempAtlasRect + " atlasRes:" + atlasResolution + " uvArea:" + sd.UVArea + " idx:" + idx + " umaMesh.uv.len:" + umaMesh.uv.Length);
                            throw;
                        }
#endif
                        idx++;
                    }
                }
            }
        }
    


/// <summary>
/// Build dictionary of blendshapes to bake (name -> weight).
/// </summary>
private static Dictionary<string, float> BuildBakedBlendshapeDict(BlendShapeSettings settings)
        {
            var dict = new Dictionary<string, float>();
            if (settings == null || settings.blendShapes == null) return dict;
            foreach (var kv in settings.blendShapes)
            {
                if (kv.Value != null && kv.Value.isBaked)
                {
                    dict[kv.Key] = kv.Value.value; // if value stored elsewhere you can use kv.Value.value; original code used provided value
                }
            }
            return dict;
        }

        // Filter-aware variant avoids scanning all materials inside BuildCombineInstances
        private void BuildCombineInstancesFiltered(List<UMAData.GeneratedMaterial> materialsForRenderer)
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;
            int rendererMaterialIndex = 0;

            for (int m = 0; m < materialsForRenderer.Count; m++)
            {
                var generatedMaterial = materialsForRenderer[m];
                ValidateGeneratedMaterialForCombine(generatedMaterial, m);
                combinedMaterialList.Add(generatedMaterial);
                generatedMaterial.materialIndex = m;

                var frags = generatedMaterial.materialFragments;
                for (int fragIdx = 0; fragIdx < frags.Count; fragIdx++)
                {
                    var fragment = frags[fragIdx];
                    if (fragment == null)
                        throw new InvalidOperationException($"Generated material {m} contains a null fragment at index {fragIdx}.");
                    var slotData = fragment.slotData;
                    if (slotData == null || slotData.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slotData.asset.meshData)) continue;

                    // Lazy-copy meshData: skip ShallowCopy when possible (no vertex overrides, no UV remap)
                    bool hasVertexOverrides = umaData.VertexOverrides.ContainsKey(slotData.slotName);
                    bool needsCopy = hasVertexOverrides || slotData.UVRemapped;
                    combineInstance = new SkinnedMeshCombiner.CombineInstance
                    {
                        meshData = needsCopy
                            ? (hasVertexOverrides
                                ? slotData.asset.meshData.ShallowCopy(umaData.VertexOverrides[slotData.slotName])
                                : slotData.asset.meshData.ShallowCopy(null))
                            : slotData.asset.meshData,
                        slotData = slotData
                    };
                    combineInstance.meshData.SlotName = slotData.slotName;

                    if (slotData.UVRemapped)
                    {
                        switch (slotData.UVSet)
                        {
                            case 1: combineInstance.meshData.uv = slotData.asset.meshData.uv2; break;
                            case 2: combineInstance.meshData.uv = slotData.asset.meshData.uv3; break;
                            case 3: combineInstance.meshData.uv = slotData.asset.meshData.uv4; break;
                        }
                    }

                    // Sparse vertex-delta modifiers (including modifiers saved by the sculpt
                    // editor) are accumulated into the MeshData job graph. Custom and
                    // non-additive adjustment types retain the established managed path.
                    combineInstance.applyMeshModifiersInJobs = SkinnedMeshCombinerMeshAPI.SupportsJobifiedMeshModifiers(slotData);
                    if (!combineInstance.applyMeshModifiersInJobs)
                        combineInstance.meshData = ApplyMeshModifiers(umaData, combineInstance.meshData, slotData);

                    // Apply MeshHide mask if present
                    if (slotData.meshHideMask != null)
                        combineInstance.triangleMask = slotData.meshHideMask;

                    var smCount2 = combineInstance.meshData.subMeshCount;
                    if (smCount2 == 0) continue;
                    if ((uint)slotData.asset.subMeshIndex >= (uint)smCount2)
                        throw new InvalidOperationException($"Slot '{slotData.slotName}' maps material submesh {slotData.asset.subMeshIndex}, but its mesh only has {smCount2} submeshes.");

                    combineInstance.targetSubmeshIndices = new int[smCount2];
                    for (int i = 0; i < smCount2; i++) combineInstance.targetSubmeshIndices[i] = -1;
                    combineInstance.targetSubmeshIndices[slotData.asset.subMeshIndex] = rendererMaterialIndex;

                    combinedMeshList.Add(combineInstance);

                    if (slotData.asset.SlotAtlassed != null)
                        slotData.asset.SlotAtlassed.Invoke(umaData, slotData, generatedMaterial.material, fragment.atlasRegion);

                    if (generatedMaterial.rendererAsset?.ClothProperties != null)
                        clothProperties = generatedMaterial.rendererAsset.ClothProperties;
                }

                rendererMaterialIndex++;
            }
        }

        private static void ValidateGeneratedMaterialForCombine(UMAData.GeneratedMaterial material, int materialIndex)
        {
            if (material == null)
                throw new InvalidOperationException($"Generated material {materialIndex} is null.");
            if (material.umaMaterial == null)
                throw new InvalidOperationException($"Generated material {materialIndex} has no UMA material definition.");
            if (material.materialFragments == null)
                throw new InvalidOperationException($"Generated material {materialIndex} has no fragment collection.");
            if (material.material == null && material.umaMaterial.material == null)
                throw new InvalidOperationException($"Generated material {materialIndex} has no usable first-pass material.");
        }

        private void AssignRendererMaterials(SkinnedMeshRenderer renderer, IReadOnlyList<UMAData.GeneratedMaterial> materials)
        {
            _materialBuffer.Clear();
            _submeshBuffer.Clear();
            var mesh = renderer.sharedMesh;
            var submeshCount = mesh.subMeshCount;

            for (int i = 0; i < materials.Count; i++)
            {
                if (i >= submeshCount) break;
                var cm = materials[i];
                if (cm == null || cm.umaMaterial == null)
                    throw new InvalidOperationException($"Renderer material {i} is missing its generated material or UMA material definition.");
                Material firstPass = cm.material != null ? cm.material : cm.umaMaterial != null ? cm.umaMaterial.material : null;
                if (firstPass == null)
                    continue;

                var subMesh = mesh.GetSubMesh(i);
                int firstPassSubmeshIndex = _submeshBuffer.Count;
                _submeshBuffer.Add(subMesh);
                _materialBuffer.Add(firstPass);

                for (int k = 0; k < cm.materialFragments.Count; k++)
                {
                    if (cm.materialFragments[k].slotData != null)
                        cm.materialFragments[k].slotData.submeshIndex = firstPassSubmeshIndex;
                }

                if (cm.umaMaterial.materialType == UMAMaterial.MaterialType.UseExistingTextures)
                {
                    UMAGeneratorPro.ApplyMaterialParameters(cm, umaData, firstPass);
                }
                if (cm.umaMaterial.secondPass != null)
                {
                    Material secondPass = cm.secondPassMaterial;
                    if (secondPass == null || secondPass == firstPass || secondPass.shader != cm.umaMaterial.secondPass.shader)
                    {
                        if (secondPass != null && secondPass != firstPass)
                            UMAUtils.DestroySceneObject(secondPass);
                        secondPass = UnityEngine.Object.Instantiate(cm.umaMaterial.secondPass);
                        cm.secondPassMaterial = secondPass;
                    }

                    UMAGeneratorPro.ApplyMaterialParameters(cm, umaData, secondPass);
                    CopyMaterialTextures(secondPass, cm.material, cm.umaMaterial);
                    if (cm.material != null && cm.material.HasProperty("_OverlayCount"))
                        SetCompositingParameters(secondPass, cm);

                    _materialBuffer.Add(secondPass);
                    _submeshBuffer.Add(subMesh);
                }
                else if (cm.secondPassMaterial != null && cm.secondPassMaterial != firstPass)
                {
                    UMAUtils.DestroySceneObject(cm.secondPassMaterial);
                    cm.secondPassMaterial = null;
                }
                cm.skinnedMeshRenderer = renderer;
            }
#if UNITY_2023_1_OR_NEWER
            renderer.SetSharedMaterials(_materialBuffer);
#else
            // Renderer.SetSharedMaterials(List<Material>) was added after UMA's Unity 2021.3
            // minimum. Keep the allocation-free path where available and preserve older
            // supported editors with the array property.
            renderer.sharedMaterials = _materialBuffer.ToArray();
#endif
            mesh.SetSubMeshes(_submeshBuffer, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.UploadMeshData(umaData.markNotReadable);
        }

        // ---- Restored helper methods ----

        protected UMAMeshData ApplyMeshModifiers(UMAData umaDataRef, UMAMeshData meshData, SlotData slotData)
        {
            if (slotData?.meshModifiers != null)
            {
                var mods = slotData.meshModifiers;
                for (int i = 0; i < mods.Count; i++)
                {
                    var mod = mods[i];
                    if (mod != null)
                        meshData = mod.Process(meshData);
                }
            }
            return meshData;
        }

        public static void SetCompositingParameters(Material secondPass, UMAData.GeneratedMaterial cm)
        {
            // If this is a compositing shader, there is only one fragment; delegate to TextureProcessPRO
            if (cm.materialFragments != null && cm.materialFragments.Count == 1)
            {
                TextureProcessPRO.SetCompositingProperties(cm, secondPass, cm.materialFragments[0]);
            }
        }

        public static void CopyMaterialTextures(Material target, Material source, UMAMaterial umaMat)
        {
            if (target == null || source == null || umaMat == null || umaMat.channels == null)
                return;

            var chans = umaMat.channels;
            for (int i = 0; i < chans.Length; i++)
            {
                var ch = chans[i];
                if (string.IsNullOrEmpty(ch.materialPropertyName))
                    continue;
                if (source.HasProperty(ch.materialPropertyName))
                {
                    var tex = source.GetTexture(ch.materialPropertyName);
                    if (tex != null)
                        target.SetTexture(ch.materialPropertyName, tex);
                }
            }
        }
    }
}
