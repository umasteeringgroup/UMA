using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;
using System.Collections; // added
using System.IO; // added

namespace UMA {
	/// <summary>
	/// DecalRenderTexture:
	/// - Parallel in spirit to DecalSlotBuilder but writes a stamped overlay texture into UMA's generated RenderTextures (UV space).
	/// - Raycasts (mesh-based) against first SkinnedMeshRenderer hit (closest facing triangle).
	/// - Selects triangles within (radius + fudgeRadius) sphere around hit point (world space).
	/// - Uses existing UV0 to position fragments in the target RenderTexture(s).
	/// - Generates overlay UV (planar projected + rotation) in UV1 for circular mask + falloff.
	/// - Alpha blends (straight alpha) using overlay texture RGBA (overlay.textureList[channel]) per UMA material channel.
	/// - Optional smooth edge falloff controlled by fudgeRadius (simple smoothstep).
	/// - Optional dilation pass (bleedPixels >0) to reduce mip seam artifacts (applied per stamped RenderTexture).
	/// - Returns DecalLayerResult with UV bounding rect and stats.
	/// </summary>
	public sealed class DecalRenderTexture : ScriptableObject {
		private DecalRenderTexture() { }

		private static bool NeedsRenderTargetYFlip()
		{
			if(UMAAssetIndexer.Instance.generator.flipDecalMode == UMAGeneratorBuiltin.FlipDecalMode.Auto) {
				//Debug.Log("DecalRenderTexture: Auto flip mode, checking SystemInfo.graphicsUVStartsAtTop = " + SystemInfo.graphicsUVStartsAtTop);
				return SystemInfo.graphicsUVStartsAtTop;
			}
			if(UMAAssetIndexer.Instance.generator.flipDecalMode == UMAGeneratorBuiltin.FlipDecalMode.Always) {
				//Debug.Log("DecalRenderTexture: Always flip mode, returning true");
				return true;
			}
			//Debug.Log("DecalRenderTexture: Never flip mode, returning false");
			return false;
		}

		private static void DrawStampMesh(Mesh mesh)
		{
			if (mesh == null)
				return;
			// Draw without a matrix; Y-flip is handled in the stamp shader via _FlipY.
			Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
		}

		// Cache the last successfully created stamp so it can be saved/restored from editor menu
		private static DecalRTStampAsset _lastStamp;
		public static DecalRTStampAsset LastStamp => _lastStamp;

		// Sequence for debug image filenames
		private static int _snapshotSequence = 0;

		[Serializable]
		public struct DecalLayerResult {
			public bool success;
			public Rect uvBounds;
			public int vertexCount;
			public int triangleCount;
			public Vector3 hitPoint;
			public Vector3 hitNormal;
		}

		[Serializable]
		public class DecalRTOptions {
			public LayerMask layerMask = ~0;
			public float maxDistance = 100f;
			public float facingThreshold = 0.15f;
			public bool enableDebug = false;
			public bool forceLinearSampling = false; // #16.2
			public int bleedPixels = 2; // #15.2 edge dilation
			public bool useHitNormalForProjection = false; // project using hit triangle normal instead of ray dir
			public float uvExpandPixels = 0.75f; // expand stamped tris in UV space (pixels) to reduce seams
			public bool invertUVYAxis = false; // invert normalized and overlay Y
		}

		private static int _dbgSequence;
		private static SkinnedMeshRenderer _dbgSmr;
		private static int[] _dbgSmrTriangles;
		private static Dictionary<int, int> _dbgTriToOrdinal;

		// Lightweight logging helpers
		[System.Diagnostics.Conditional("UMA_DECALRT_VERBOSE")]
		private static void LogInfo(string msg) 
		{
			//if(Debug.isDebugBuild)
				Debug.Log("[DecalRT] " + msg);
		}

		private static void LogWarn(string msg) 
		{
			Debug.LogWarning("[DecalRT] " + msg);
		}
		private static void LogDebugSkip(string reason) 
		{
			//if(Debug.isDebugBuild) {
				Debug.Log("[DecalRT][Skip] " + reason);
			//}
		}

		private static void LogError(string msg) 
		{
			Debug.LogError("[DecalRT] " + msg);
		}

		// Detailed texture/RT logging to diagnose gray results
		private static void LogTextureInfo(string label, Texture tex) {
			if(tex == null) 
			{ 
				LogWarn(label + ": Texture is null"); 
				return; 
			}
			
			LogInfo($"{label}: type={tex.GetType().Name}, name={tex.name}, size={tex.width}x{tex.height}, aniso={tex.anisoLevel}, filter={tex.filterMode}, wrap={tex.wrapMode}");
			
			var rt = tex as RenderTexture;
			if(rt != null) 
			{
				try {
					LogInfo($"{label}: RT fmt={rt.format}, depth={rt.depth}, msaa={rt.antiAliasing}, useMipMap={rt.useMipMap}, autoGenMips={rt.autoGenerateMips}, colorFmt={rt.graphicsFormat}, vrUsage={rt.vrUsage}");
				} catch { }
			}
		}

		private static void LogRTActive(string label) 
		{
			var act = RenderTexture.active;
			if(act == null) 
			{ 
				LogInfo(label + ": RenderTexture.active = null"); 
				return; 
			}
			LogInfo($"{label}: ActiveRT name={act.name}, size={act.width}x{act.height}, fmt={act.format}, msaa={act.antiAliasing}");
		}

		public static bool TryGetLastDebug(out SkinnedMeshRenderer smr, out int[] triIndices, out Dictionary<int, int> triToOrdinal, out int sequence) {
			smr = _dbgSmr;
			triIndices = _dbgSmrTriangles;
			triToOrdinal = _dbgTriToOrdinal;
			sequence = _dbgSequence;
			return smr != null && triIndices != null && triToOrdinal != null;
		}

		private static SlotData lastSlotForIndex;

		public static SlotData SlotForIndex(int vert, UMAData umaData) {
			if(lastSlotForIndex != null) {
				if(lastSlotForIndex.OwnsVertex(vert)) {
					return lastSlotForIndex;
				}
			}
			lastSlotForIndex = umaData.umaRecipe.FindSlotForVertex(vert);
			return lastSlotForIndex;
		}

		/// <summary>
		/// CreateDecalLayer: stamps an overlay's textures into all UMA-generated RenderTextures for that overlay's UMAMaterial and channels.
		/// Each channel uses the same projected triangle set and UVs.
		/// Also caches a DecalRTStampAsset describing the stamped geometry for save/restore.
		/// </summary>
		/// <param name="avatar">Target avatar used for mesh raycast and skeleton.</param>
		/// <param name="ray">Ray to project decal from.</param>
		/// <param name="radius">World-space radius.</param>
		/// <param name="fudgeRadius">Extra radius to soften edges.</param>
		/// <param name="angleDegrees">Rotation around normal in degrees.</param>
		/// <param name="umaData">UMAData that holds generated RenderTextures.</param>
		/// <param name="overlay">OverlayDataAsset providing per-channel source textures and UMAMaterial mapping.</param>
		/// <param name="options">Stamping options.</param>
		public static DecalLayerResult? CreateDecalLayer(
 DynamicCharacterAvatar avatar,
 Ray ray,
 float radius,
 float fudgeRadius,
 float angleDegrees,
 UMAData umaData,
 OverlayDataAsset overlay,
 DecalRTOptions options = null) {
			lastSlotForIndex = null;
			var result = new DecalLayerResult { success = false };
			if(avatar == null || avatar.umaData == null) {
				LogDebugSkip("CreateDecalLayer: avatar or avatar.umaData null");
				return null;
			}
			if(umaData == null) {
				LogDebugSkip("CreateDecalLayer: provided umaData null");
				return null;
			}
			if(overlay == null || overlay.textureList == null || overlay.textureList.Length == 0) {
				if(options?.enableDebug == true)
					LogWarn("DecalRenderTexture: Overlay missing or has no textures. Aborting.");
				LogDebugSkip("CreateDecalLayer: overlay null or no textures");
				return null;
			}
			if(radius <= 0.00001f) { LogDebugSkip("CreateDecalLayer: radius <=0"); return null; }

			options ??= new DecalRTOptions();
			if(options.enableDebug) {
				LogInfo($"CreateDecalLayer: Begin. ColorSpace={QualitySettings.activeColorSpace}, useHitNormal={options.useHitNormalForProjection}, forceLinearSampling={options.forceLinearSampling}, bleedPixels={options.bleedPixels}, uvExpandPixels={options.uvExpandPixels}");
				LogTextureInfo("Overlay[0]", overlay.textureList[0]);
			}

			if(!MeshRaycastAvatar(avatar, ray, options, out var smr, out var hitPointWorld, out var hitNormalWorld)) {
				if(options.enableDebug)
					LogWarn("DecalRenderTexture: Mesh raycast failed / no facing triangle.");
				LogDebugSkip("CreateDecalLayer: mesh raycast failed");
				return null;
			}

			// Draw a gizmo line showing the hit normal direction for30 seconds
			Debug.DrawLine(hitPointWorld, hitPointWorld + hitNormalWorld.normalized * 0.1f, Color.magenta, 30f, false);

			// Bake SMR (we only need vertex positions for selection & projection)
			Mesh baked = new Mesh();
			smr.BakeMesh(baked);
			try {
				var shared = smr.sharedMesh;
				if(shared == null) { LogDebugSkip("CreateDecalLayer: shared mesh null"); return null; }

				var bakedVertsLocal = baked.vertices;
				var triIndices = shared.triangles;
				var meshUV = shared.uv; // UV0
				if(bakedVertsLocal == null || bakedVertsLocal.Length == 0 ||
				triIndices == null || triIndices.Length == 0 ||
				meshUV == null || meshUV.Length != bakedVertsLocal.Length) { LogDebugSkip("CreateDecalLayer: invalid baked vertex/uv data"); return null; }

				// Prepare selection
				float expandedRadius = radius + fudgeRadius; 
				float radiusSqr = expandedRadius * expandedRadius;
				Transform t = smr.transform;

				var includedVertex = new bool[bakedVertsLocal.Length];
				var selectedTris = new List<int>(2048);
				var selectedTriIds = new List<int>(512);

				// Choose facing cull direction: when projecting using hit normal, face cull should also use hit normal (inverted)
				Vector3 facingDirWorld = options.useHitNormalForProjection ? -hitNormalWorld : ray.direction.normalized;

				SelectTriangles(
				triIndices,
				bakedVertsLocal,
				t,
				facingDirWorld,
				hitPointWorld,
				radiusSqr,
				options.facingThreshold,
				selectedTris,
				includedVertex,
				options.enableDebug,
				selectedTriIds);

				if(selectedTris.Count == 0) {
					if(options.enableDebug)
						LogWarn("DecalRenderTexture: No triangles selected inside radius.");
					LogDebugSkip("CreateDecalLayer: no triangles selected");
					return null;
				}

				// Build tri->ordinal map using selected tri ids
				_dbgSmr = smr;
				_dbgSmrTriangles = triIndices;
				_dbgTriToOrdinal = new Dictionary<int, int>(selectedTriIds.Count);
				for(int ord = 0; ord < selectedTriIds.Count; ord++) {
					int combTri = selectedTriIds[ord];
					if(!_dbgTriToOrdinal.ContainsKey(combTri))
						_dbgTriToOrdinal.Add(combTri, ord);
				}
				_dbgSequence++;

				// Remap vertices for a compact dynamic mesh
				int[] remap = new int[bakedVertsLocal.Length];
				Array.Fill(remap, -1);
				int newVertexCount = 0;
				for(int i = 0; i < bakedVertsLocal.Length; i++) {
					if(includedVertex[i])
						remap[i] = newVertexCount++;
				}
				if(newVertexCount == 0) { LogDebugSkip("CreateDecalLayer: newVertexCount ==0 after remap"); return null; }

				// Precompute per-vertex data for included verts (base UV0, overlay UV1, clip-space pos)
				Vector2[] baseUVAll = new Vector2[bakedVertsLocal.Length];
				Vector2[] overlayUVAll = new Vector2[bakedVertsLocal.Length];
				Vector3[] posCSAll = new Vector3[bakedVertsLocal.Length];

				Vector2 uvMin = new Vector2(1f, 1f);
				Vector2 uvMax = new Vector2(0f, 0f);

				// Build projection axes (planar) like DecalSlotBuilder
				Vector3 localHit = t.InverseTransformPoint(hitPointWorld);
				Vector3 localRayDir = t.InverseTransformDirection(ray.direction).normalized;
				Vector3 localHitNormal = t.InverseTransformDirection(hitNormalWorld).normalized;
				Vector3 projectionDir = options.useHitNormalForProjection ? localHitNormal : localRayDir;
				BuildProjectionAxesAroundRay(projectionDir, angleDegrees, out var axisX, out var axisY);

				for(int v = 0; v < bakedVertsLocal.Length; v++) {
					if(!includedVertex[v])
						continue;

					// Base UV0
					Vector2 uv = meshUV[v];
					uv.x = Mathf.Clamp01(uv.x);
					uv.y = Mathf.Clamp01(uv.y);
					baseUVAll[v] = uv;

					uvMin = Vector2.Min(uvMin, uv);
					uvMax = Vector2.Max(uvMax, uv);

					// Planar projection around hit for overlay space (must use projectionDir)
					Vector3 posedLocal = bakedVertsLocal[v];
					Vector3 offset = posedLocal - localHit;
					float along = Vector3.Dot(offset, projectionDir);
					Vector3 planar = offset - along * projectionDir;

					float px = Vector3.Dot(planar, axisX);
					float py = Vector3.Dot(planar, axisY);
					float u = (px / radius) * 0.5f + 0.5f;
					float v2 = (py / radius) * 0.5f + 0.5f;
					if(options.invertUVYAxis) v2 = 1.0f - v2;
					overlayUVAll[v] = new Vector2(u, v2);

					// Vertex position for stamping mesh: map UV0 -> clip space (-1..1)
					posCSAll[v] = new Vector3(uv.x * 2f - 1f, uv.y * 2f - 1f, 0f);
				}

				// Map combined vertices to their originating SlotData
				var recipe = umaData.umaRecipe;
				// Group selected triangles by SlotData, track ordinals per-triangle
				var slotTriMap = new Dictionary<SlotData, List<int>>();
				var slotTriOrdinals = new Dictionary<SlotData, List<int>>();
				for(int i = 0, ord = 0; i < selectedTris.Count; i += 3, ord++) {
					int i0 = selectedTris[i + 0];
					int i1 = selectedTris[i + 1];
					int i2 = selectedTris[i + 2];
					var s = SlotForIndex(i0, umaData);
					var s1 = SlotForIndex(i1, umaData);
					var s2 = SlotForIndex(i2, umaData);

					//var s = vertexSlot != null ? vertexSlot[i0] : null;
					//var s1 = vertexSlot != null ? vertexSlot[i1] : null;
					//var s2 = vertexSlot != null ? vertexSlot[i2] : null;
					if(s == null || s1 != s || s2 != s)
						continue; // ensure triangle is wholly inside a slot
					if(!slotTriMap.TryGetValue(s, out var list)) {
						list = new List<int>(256);
						slotTriMap.Add(s, list);
					}
					list.Add(i0);
					list.Add(i1);
					list.Add(i2);
					if(!slotTriOrdinals.TryGetValue(s, out var olist)) {
						olist = new List<int>(256);
						slotTriOrdinals.Add(s, olist);
					}
					olist.Add(ord);
				}

				if(slotTriMap.Count == 0) {
					if(options.enableDebug)
						LogWarn("DecalRenderTexture: No per-slot triangles found after grouping.");
					LogDebugSkip("CreateDecalLayer: slotTriMap.Count ==0");
					return null;
				}

				// Prepare stamp material
				Material stampMat = GetOrCreateStampMaterial(options.forceLinearSampling);
				if(stampMat == null) {
					LogDebugSkip("CreateDecalLayer: GetOrCreateStampMaterial returned null");
					return null;
				}
				if(options.enableDebug) {
					LogInfo($"StampMat created. shader={stampMat.shader?.name}, passCount={stampMat.passCount}");
				}
				float fudgeFactor = (fudgeRadius <= 0f) ? 0.0001f : (fudgeRadius / (radius + fudgeRadius));
				stampMat.SetFloat("_Fudge", fudgeFactor);
				stampMat.SetFloat("_UseUVRect", 1.0f);

				// Bind a global mask to gate coverage for all channels: prefer overlay.alphaMask, else overlay.textureList[0].a
				Texture maskTex = null;
				try { if(overlay.alphaMask != null) maskTex = overlay.alphaMask; } catch { }
				if(maskTex == null && overlay.material != null && overlay.material.channels != null) {
					for(int i = 0; i < overlay.material.channels.Length && i < overlay.textureList.Length; i++) {
						if(overlay.material.channels[i].channelType == UMAMaterial.ChannelType.DiffuseTexture &&
						overlay.textureList[i] != null) {
							maskTex = overlay.textureList[i];
							break;
						}
					}
				}
				if(maskTex == null && overlay.textureList != null && overlay.textureList.Length > 0)
					maskTex = overlay.textureList[0];
				stampMat.SetFloat("_UseMask", 0f);
				//stampMat.SetFloat("_UseMask", maskTex != null ? 1f : 0f);
				if(maskTex != null) {
					stampMat.SetTexture("_MaskTex", maskTex);
					if(options.enableDebug)
						LogTextureInfo("MaskTex", maskTex);
				}

				// Prepare Stamp Asset cache
				var stampAsset = ScriptableObject.CreateInstance<DecalRTStampAsset>();
				stampAsset.overlayName = overlay.overlayName; // use overlay.overlayName for restore
				stampAsset.overlayNameHash = UMAUtils.StringToHash(stampAsset.overlayName);
				stampAsset.invertY = options.invertUVYAxis;
				stampAsset.bleedPixels = options.bleedPixels;
				stampAsset.forceLinearSampling = options.forceLinearSampling;
				stampAsset.slots.Clear();

				// Shared draw state for stamping
				var prevRTGlobal = RenderTexture.active;
				GL.PushMatrix();
				GL.LoadOrtho();
				if(options.enableDebug) {
					LogRTActive("Before stamping");
				}


				// Iterate each affected slot, build a mesh source buffers for that slot only, clip to SlotData.UVArea
				foreach(var kv in slotTriMap) {
					var slot = kv.Key;
					var tris = kv.Value;

					if(options.enableDebug) {
						LogInfo($"Stamping slot '{slot.slotName}' with {tris.Count / 3} tris. UVArea={slot.UVArea}.");
					}

					// Skip slots with UMAMaterial type UseExistingMaterial or UseExistingTextures
					if(slot.material != null &&
					(slot.material.materialType == UMAMaterial.MaterialType.UseExistingMaterial ||
					slot.material.materialType == UMAMaterial.MaterialType.UseExistingTextures)) {
						if(options.enableDebug)
							LogInfo($"Skipping slot '{slot.slotName}' due to material type '{slot.material.materialType}'.");
						continue;
					}

					var remapDict = new Dictionary<int, int>(tris.Count);
					var uv0List = new List<Vector2>();
					var uv1List = new List<Vector2>();
					var colList = new List<Color32>();
					var newIndices = new int[tris.Count];

					for(int ti = 0; ti < tris.Count; ti++) {
						int orig = tris[ti];
						if(!remapDict.TryGetValue(orig, out int newIndex)) {
							newIndex = remapDict.Count;
							remapDict.Add(orig, newIndex);
							uv0List.Add(baseUVAll[orig]);
							uv1List.Add(overlayUVAll[orig]);
							colList.Add(new Color32(255, 255, 255, 255));
						}
						newIndices[ti] = newIndex;
					}

					// Build slot stamp entry
					var slotStamp = new DecalRTStampAsset.SlotStamp {
						slotName = slot.slotName,
						slotHash = UMAUtils.StringToHash(slot.slotName),
						umaMaterialName = slot.material != null ? slot.material.name : string.Empty,
						normBaseUV = new Vector2[uv0List.Count],
						overlayUV = uv1List.ToArray(),
						triangles = newIndices,
						#if UNITY_EDITOR
						triOrdinals = slotTriOrdinals.TryGetValue(slot, out var ords) ? ords.ToArray() : null,
						slotRelativeTriangles = null
						#endif
					};

					#if UNITY_EDITOR
					// Store the selected triangles as slot-relative vertex indices (relative to SlotDataAsset.meshData)
					// This is used for editor visualization/editing only.
					try
					{
						if (slot.vertexOffset >= 0 && slot.asset != null && slot.asset.meshData != null)
						{
							var rel = new int[tris.Count];
							for (int ti = 0; ti < tris.Count; ti++)
							{
								rel[ti] = tris[ti] - slot.vertexOffset;
							}
							slotStamp.slotRelativeTriangles = rel;
						}
					}
					catch { }
					#endif

                    // Always normalize base UVs against the slot UVArea and record that area
                    var normRect = slot.UVArea;
                    if(normRect.width <= 0f || normRect.height <= 0f) {
                        // If UVArea is invalid, default to full atlas
                        normRect = new Rect(0f, 0f, 1f, 1f);
                    }
                    for(int iuv = 0; iuv < uv0List.Count; iuv++) {
                        var uv = uv0List[iuv];
					var nu = (uv.x - normRect.xMin) / normRect.width;
					var nv = (uv.y - normRect.yMin) / normRect.height;
					if(options.invertUVYAxis) nv = 1.0f - nv;
                        slotStamp.normBaseUV[iuv] = new Vector2(Mathf.Clamp01(nu), Mathf.Clamp01(nv));
                    }
                    slotStamp.recordedUVArea = normRect; // exact area used for normalization

					stampAsset.slots.Add(slotStamp);
				}


				// Destroy temp stamp material
				if(Application.isPlaying)
					UnityEngine.Object.Destroy(stampMat);
				else
					UnityEngine.Object.DestroyImmediate(stampMat);
				 
				if(options.enableDebug) {
					LogInfo($"DecalRenderTexture: Stamped overlay '{overlay.overlayName}' on {stampAsset.slots.Count} target(s). UVRect clipping per slot.");
				}

				result.success = stampAsset.slots.Count > 0;
				result.vertexCount = 0;
				result.triangleCount = 0;
				result.uvBounds = Rect.MinMaxRect(uvMin.x, uvMin.y, uvMax.x, uvMax.y);
				result.hitPoint = hitPointWorld;
				result.hitNormal = hitNormalWorld;

				// Cache last stamp asset for later save/restore
				_lastStamp = result.success ? stampAsset : null;
				if(options.enableDebug)
					LogInfo($"DecalRenderTexture: Created DecalRTStampAsset with {stampAsset.slots.Count} slot entries. lastStamp: {_lastStamp}");

				GL.PopMatrix();
				return result.success ? result : (DecalLayerResult?)null;
			} finally {
				UMAUtils.DestroySceneObject(baked);
			}
		}

		/// <summary>
		/// Apply a specific slot's stamp into a provided target RenderTexture during compositing.
		/// Limits drawing to SlotStamp with the exact slotName, resolves overlay channel by materialPropertyName
		/// against the overlay's UMAMaterial channels, clips to the runtime slot UVArea, and uses the same masking rule.
		/// Returns true if anything was drawn.
		/// </summary>
		public static bool ApplySlotStamps(
		DynamicCharacterAvatar avatar,
		UMAData umaData,
		DecalRTStampAsset stamp,
		string materialPropertyName,
		RenderTexture targetTexture, int srcOverlayNameHash) 
		{
			if(avatar == null || umaData == null || stamp == null || string.IsNullOrEmpty(materialPropertyName) || targetTexture == null) 
			{
				var missing = new List<string>(6);
				if(avatar == null)
					missing.Add("avatar");
				if(umaData == null)
					missing.Add("umaData");
				if(stamp == null)
					missing.Add("stamp");
				if(string.IsNullOrEmpty(materialPropertyName)) 
					missing.Add("materialPropertyName");
				if(targetTexture == null)
					missing.Add("targetTexture");
				LogWarn("ApplySlotStamps: Missing required parameter(s): " + string.Join(", ", missing));
				return false;
			}
			try {
				// Note: we cannot early-out by comparing the stamp's source overlay to the atlas-update overlay
				// because stamps are re-applied during atlas updates triggered by other overlays on the slot.

				LogInfo($"ApplySlotStamps: Begin for property '{materialPropertyName}'. ColorSpace={QualitySettings.activeColorSpace}");
				//LogTextureInfo("ApplySlotStamps target RT", targetTexture);
				//Debug.Log("ApplySlotStamps: Begin for property '" + materialPropertyName + "'.");
                // Resolve overlay
                OverlayDataAsset overlay = null;
				try { overlay = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(stamp.overlayName); } catch { }
				if(overlay == null)
				{
					LogDebugSkip($"ApplySlotStamps: overlay not found for overlayName='{stamp.overlayName}'");
					return false;
				}

				overlay.EnsureMaterial();
				if(overlay.material == null)
				{
					LogDebugSkip($"ApplySlotStamps: overlay '{overlay.name}' has no UMAMaterial");
					return false;
				}
				if(overlay.textureList == null)
				{
					LogDebugSkip($"ApplySlotStamps: overlay '{overlay.name}' textureList is null");
					return false;
				}
				if(overlay.textureList.Length == 0)
				{
					LogDebugSkip($"ApplySlotStamps: overlay '{overlay.name}' textureList is empty");
					return false;
				}

				int channelIndex = overlay.material.GetChannelIndex(materialPropertyName);
				if(channelIndex < 0 || channelIndex >= overlay.textureList.Length) {
					// Channel for property not found in overlay
					return false;
				}

				var stampMat = GetOrCreateStampMaterial(stamp.forceLinearSampling);
				if(stampMat == null) {
					LogDebugSkip("ApplySlotStamps: stamp material null");
					return false;
				}
				stampMat.SetFloat("_FlipY", NeedsRenderTargetYFlip() ? 1f : 0f);
				stampMat.SetFloat("_Fudge", 0.0001f);
				stampMat.SetFloat("_UseUVRect", 0.0f);

				// Optional masking
				Texture maskTex = null;
				try { if(overlay.alphaMask != null) maskTex = overlay.alphaMask; } catch { }
				if(maskTex == null && overlay.textureList != null && overlay.textureList.Length > 0) {
					maskTex = overlay.textureList[0];
				}

				stampMat.SetFloat("_UseMask", maskTex != null ? 1f : 0f);
				if(maskTex != null) 
				{
					//Debug.Log("ApplySlotStamps: Setting maskTex.");
                    stampMat.SetTexture("_MaskTex", maskTex); 
					//LogTextureInfo("ApplySlotStamps maskTex", maskTex); 
				}

                bool stampedAny = false;
                var prevRTGlobal = RenderTexture.active;
				GL.PushMatrix();
				try
				{
					GL.LoadOrtho();
					//Debug.Log("ApplySlotStamps: Before stamping");

					int currenttime = Time.frameCount;

					for (int si = 0; si < stamp.slots.Count; si++)
					{
						var slotStamp = stamp.slots[si];
						if (slotStamp == null)
							continue;
						if (slotStamp.debugDontUse)
							continue;
						SlotData slot = umaData.umaRecipe.GetSlot(slotStamp.slotName);
						if (slot == null || slot.asset == null)
							continue;

						if (!slot.hasOverlay(srcOverlayNameHash))
							continue;

						int vcount = (slotStamp.normBaseUV != null) ? slotStamp.normBaseUV.Length : 0;
						if (vcount == 0 || slotStamp.overlayUV == null || slotStamp.triangles == null)
						{
							LogDebugSkip($"ApplySlotStamps: invalid stamp data for slot '{slotStamp.slotName}'");
							continue;
						}

						// ... inside ApplyStampToUMA loop, before building uv lists:
						bool hasRecorded = (slotStamp.recordedUVArea.width > 0f && slotStamp.recordedUVArea.height > 0f);
						bool hasRuntime = (slot.UVArea.width > 0f && slot.UVArea.height > 0f);


						// Always map normalized UVs from recorded area into current slot UVArea

						// Build UV lists; convert normalized slot UVs back to atlas UVs
						var uv0List = new List<Vector2>(vcount);
						var uv1List = new List<Vector2>(vcount);
						var colList = new List<Color32>(vcount);
						var vertsList = new List<Vector3>(vcount);
						for (int v = 0; v < vcount; v++)
						{
							Vector2 nuv = slotStamp.normBaseUV[v];
							// normalized -> atlas (recorded area)
							Vector2 atlasUVRecorded = hasRecorded
								? new Vector2(
									slotStamp.recordedUVArea.xMin + nuv.x * slotStamp.recordedUVArea.width,
									slotStamp.recordedUVArea.yMin + nuv.y * slotStamp.recordedUVArea.height)
								: nuv;
							// atlas (recorded) -> normalized (current)
							Vector2 normCurrent = hasRuntime
								? new Vector2(
									(atlasUVRecorded.x - slot.UVArea.xMin) / slot.UVArea.width,
									(atlasUVRecorded.y - slot.UVArea.yMin) / slot.UVArea.height)
								: atlasUVRecorded;
							// normalized (current) -> atlas (current)
							Vector2 globalUV = hasRuntime
								? slot.ConvertToAtlasUV(new Vector2(Mathf.Clamp01(normCurrent.x), Mathf.Clamp01(normCurrent.y)))
								: normCurrent;

							uv0List.Add(globalUV);
							var ov = slotStamp.overlayUV[v];
							if (stamp.invertY) ov.y = 1.0f - ov.y;
							uv1List.Add(ov);
							colList.Add(new Color32(255, 255, 255, 255));
							vertsList.Add(new Vector3(globalUV.x * 2f - 1f, globalUV.y * 2f - 1f, 0f));
						}

						// Build mesh for optional debug/consistency (not strictly required for DrawMeshNow)
						var mesh = new Mesh { name = $"DecalRT_ApplyStampAsset_{slotStamp.slotName}" };
						mesh.indexFormat = (vcount > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
						mesh.SetVertices(vertsList);
						mesh.SetTriangles(slotStamp.triangles, 0);
						mesh.SetUVs(0, uv0List);
						mesh.SetUVs(1, uv1List);
						mesh.SetColors(colList);
						mesh.RecalculateBounds();

						//Debug.Log("ApplySlotStamps: UV Area for slot = " + slot.UVArea);
                        var uvRect = hasRuntime ? slot.UVArea : new Rect(0f, 0f, 1f, 1f);
                        //Debug.Log("ApplySlotStamps: Adjusted UV Area for slot = " + slot.UVArea);
                        stampMat.SetVector("_UVRect", new Vector4(uvRect.xMin, uvRect.yMin, uvRect.xMax, uvRect.yMax));

						if (uvRect.width <= 0f || uvRect.height <= 0f)
						{
							LogWarn($"ApplySlotStamps: slot '{slotStamp.slotName}' has invalid UVArea={uvRect}. Shader will clip everything.");
						}
						else
						{
							// Compare current mesh UV0 bounds against the clipping rect to detect full discard.
							float minU = 1f;
							float minV = 1f;
							float maxU = 0f;
							float maxV = 0f;
							for (int uvi = 0; uvi < uv0List.Count; uvi++)
							{
								Vector2 uv = uv0List[uvi];
								if (uv.x < minU) minU = uv.x;
								if (uv.y < minV) minV = uv.y;
								if (uv.x > maxU) maxU = uv.x;
								if (uv.y > maxV) maxV = uv.y;
							}

							bool disjoint = (maxU < uvRect.xMin) || (minU > uvRect.xMax) || (maxV < uvRect.yMin) || (minV > uvRect.yMax);
							if (disjoint)
							{
								LogWarn($"ApplySlotStamps: UVRect would clip everything for slot '{slotStamp.slotName}'. UVArea={uvRect} meshUVBounds=({minU:F4},{minV:F4})-({maxU:F4},{maxV:F4}).");
							}
							else
							{
								// If bounds extend outside the rect, we're partially clipped.
								bool partial = (minU < uvRect.xMin) || (maxU > uvRect.xMax) || (minV < uvRect.yMin) || (maxV > uvRect.yMax);
								if (partial)
								{
									LogInfo($"ApplySlotStamps: UVRect partially clips slot '{slotStamp.slotName}'. UVArea={uvRect} meshUVBounds=({minU:F4},{minV:F4})-({maxU:F4},{maxV:F4}).");
								}
							}
						}

						// Source texture for resolved channel
						var srcTex = overlay.textureList[channelIndex];
						if (srcTex == null)
							continue;
						stampMat.color = Color.white;
                        stampMat.SetTexture("_OverlayTex", srcTex);
						// SaveStampTexturePNG(srcTex, stamp, stamp.overlayName, Time.frameCount, "source before drawing mesh");

                        // Build expanded mesh for stamping this slot (seam fix)
                        var expandedMesh = BuildStampMeshWithExpansion(uv0List, uv1List, colList, slotStamp.triangles,
						/*expandPixels*/0.75f, targetTexture.width, targetTexture.height);
						if (expandedMesh == null)
						{
							Debug.Log("Expanded mesh is null for slot '" + slotStamp.slotName + "'");
							continue;
						}

					// Draw to provided target texture
					if (!targetTexture.IsCreated())
					{
						targetTexture.Create();
					}
					var prevActive = RenderTexture.active;
					RenderTexture.active = targetTexture;
					GL.Viewport(new Rect(0, 0, targetTexture.width, targetTexture.height));
						//SaveRenderTexturePNG(targetTexture, stamp, stamp.overlayName, Time.frameCount, "before drawing mesh");

						bool SetPassReturn = stampMat.SetPass(0);
						DrawStampMesh(expandedMesh);
						RenderTexture.active = prevActive;

						// HERE: Record After
						//SaveRenderTexturePNG(targetTexture, stamp, stamp.overlayName, currenttime, "after drawing mesh");


						stampedAny = true;

						//if(stamp.bleedPixels > 0)
						//	RunDilation(targetTexture, stamp.bleedPixels);
						//SaveRenderTexturePNG(targetTexture, stamp, stamp.overlayName, Time.frameCount, "After Dilation");

						UMAUtils.DestroySceneObject(expandedMesh);
					}
				}
				finally {
					GL.PopMatrix();
					RenderTexture.active = prevRTGlobal;
				}

				// Destroy temp stamp material
				if(Application.isPlaying)
					UnityEngine.Object.Destroy(stampMat);
				else
					UnityEngine.Object.DestroyImmediate(stampMat);

				return stampedAny;
			}
			finally {
			}
		}

		// NEW API: Remove triangles (by ordinal) from the cached last stamp. Does not modify already drawn atlases.
		public static bool RemoveTrianglesFromLastStamp(HashSet<int> ordinalsToRemove, DynamicCharacterAvatar avatar, UMAData umaData) {
			#if !UNITY_EDITOR
			return false;
			#else
			if(ordinalsToRemove == null || ordinalsToRemove.Count == 0) {
				LogDebugSkip("RemoveTrianglesFromLastStamp: no ordinals provided");
				return false;
			}
			if(_lastStamp == null || _lastStamp.slots == null || _lastStamp.slots.Count == 0) {
				LogDebugSkip("RemoveTrianglesFromLastStamp: no last stamp available");
				return false;
			}

			bool changed = false;
			for(int si = _lastStamp.slots.Count - 1; si >= 0; si--) {
				var s = _lastStamp.slots[si];
				if(s == null || s.triangles == null || s.triangles.Length == 0)
					continue;

				int triCount = s.triangles.Length / 3;
				int[] triOrd = s.triOrdinals;
				if(triOrd == null || triOrd.Length != triCount) {
					// If ordinals missing or mismatched, we cannot reliably remove by ordinals.
					// Skip this slot.
					LogDebugSkip($"RemoveTrianglesFromLastStamp: slot '{s.slotName}' missing ordinals or mismatch");
					continue;
				}

				var newTri = new List<int>(s.triangles.Length);
				var newOrd = new List<int>(triOrd.Length);
				for(int t = 0; t < triCount; t++) {
					int ord = triOrd[t];
					if(ordinalsToRemove.Contains(ord)) {
						changed = true;
						continue; // drop
					}
					// keep triangle
					newTri.Add(s.triangles[t * 3 + 0]);
					newTri.Add(s.triangles[t * 3 + 1]);
					newTri.Add(s.triangles[t * 3 + 2]);
					newOrd.Add(ord);
				}

				if(changed) {
					s.triangles = newTri.ToArray();
					s.triOrdinals = newOrd.ToArray();
				}

				// If slot has no triangles left, remove the slot entry
				if(s.triangles == null || s.triangles.Length == 0) {
					_lastStamp.slots.RemoveAt(si);
				}
			}

			if(changed) {
				LogInfo($"RemoveTrianglesFromLastStamp: removed ordinals count={ordinalsToRemove.Count}. Slots now={_lastStamp.slots.Count}");
			}
			return changed;
			#endif
		}

		#region Mesh Raycast (copied style from DecalSlotBuilder)
		private struct MeshHit {
			public SkinnedMeshRenderer smr;
			public float distance;
			public Vector3 point;
			public Vector3 normal;
			public int triangleIndex;
		}

		private static bool MeshRaycastAvatar(DynamicCharacterAvatar avatar,
		Ray ray,
		DecalRTOptions options,
		out SkinnedMeshRenderer hitSmr,
		out Vector3 hitPoint,
		out Vector3 hitNormal) {
			hitSmr = null;
			hitPoint = default;
			hitNormal = default;

			var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			if(smrs == null || smrs.Length == 0)
				return false;

			Mesh bakeMesh = new Mesh();
			MeshHit best = new MeshHit { distance = float.MaxValue, triangleIndex = -1 };

			foreach(var smr in smrs) {
				if(smr == null || !smr.enabled)
					continue;
				int layerBit = 1 << smr.gameObject.layer;
				if((options.layerMask.value & layerBit) == 0)
					continue;

				var shared = smr.sharedMesh;
				if(shared == null || shared.vertexCount == 0)
					continue;

				smr.BakeMesh(bakeMesh);
				var verts = bakeMesh.vertices;
				var tris = shared.triangles;
				if(verts == null || tris == null || tris.Length == 0)
					continue;

				Transform tr = smr.transform;
				Vector3 ro = ray.origin;
				Vector3 rd = ray.direction;
				int triCount = tris.Length / 3;

				for(int t = 0; t < triCount; t++) {
					int i0 = tris[t * 3 + 0];
					int i1 = tris[t * 3 + 1];
					int i2 = tris[t * 3 + 2];
					if((uint)i0 >= verts.Length || (uint)i1 >= verts.Length || (uint)i2 >= verts.Length)
						continue;

					Vector3 w0 = tr.TransformPoint(verts[i0]);
					Vector3 w1 = tr.TransformPoint(verts[i1]);
					Vector3 w2 = tr.TransformPoint(verts[i2]);

					Vector3 e1 = w1 - w0;
					Vector3 e2 = w2 - w0;
					Vector3 n = Vector3.Cross(e1, e2);
					float nm = n.magnitude;
					if(nm < 1e-6f)
						continue;
					n /= nm;
					if(Vector3.Dot(n, rd) > -options.facingThreshold)
						continue;

					if(RayTriangle(ro, rd, w0, w1, w2, out float dist, out Vector3 bary)) {
						if(dist < 0 || dist > options.maxDistance)
							continue;
						if(dist < best.distance) {
							best.distance = dist;
							best.point = w0 * (1 - bary.x - bary.y) + w1 * bary.x + w2 * bary.y;
							best.normal = n;
							best.smr = smr;
							best.triangleIndex = t;
							if(dist <= 1e-5f)
								break;
						}
					}
				}
			}

			UMAUtils.DestroySceneObject(bakeMesh);

			if(best.smr == null)
				return false;

			hitSmr = best.smr;
			hitPoint = best.point;
			hitNormal = best.normal;

			if(options.enableDebug) {
				Debug.DrawLine(hitPoint, hitPoint + hitNormal * 0.05f, Color.cyan, 2f);
				LogInfo($"MeshRaycastAvatar: Hit '{best.smr.name}', tri={best.triangleIndex}, dist={best.distance:F4}");
			}

			return true;
		}

		private static bool RayTriangle(Vector3 ro, Vector3 rd,
		Vector3 v0, Vector3 v1, Vector3 v2,
		out float distance,
		out Vector3 bary) {
			bary = default;
			distance = 0f;
			const float EPS = 1e-7f;
			Vector3 e1 = v1 - v0;
			Vector3 e2 = v2 - v0;
			Vector3 p = Vector3.Cross(rd, e2);
			float det = Vector3.Dot(e1, p);
			if(det > -EPS && det < EPS)
				return false;
			float invDet = 1f / det;
			Vector3 tvec = ro - v0;
			float u = Vector3.Dot(tvec, p) * invDet;
			if(u < 0 || u > 1)
				return false;
			Vector3 q = Vector3.Cross(tvec, e1);
			float v = Vector3.Dot(rd, q) * invDet;
			if(v < 0 || (u + v) > 1)
				return false;
			float t = Vector3.Dot(e2, q) * invDet;
			if(t < 0)
				return false;
			distance = t;
			bary = new Vector3(u, v, 1 - u - v);
			return true;
		}
		#endregion

		#region Triangle Selection (mirrors DecalSlotBuilder)
		private static void SelectTriangles(
		int[] triIndices,
		Vector3[] bakedVertsLocal,
		Transform rendererTransform,
		Vector3 rayDirWorld,
		Vector3 hitPointWorld,
		float radiusSqr,
		float facingThreshold,
		List<int> includedTriangles,
		bool[] includedVertex,
		bool debug,
		List<int> selectedTriIds) {
			int triCount = triIndices.Length / 3;
			for(int tri = 0; tri < triCount; tri++) {
				int i0 = triIndices[tri * 3 + 0];
				int i1 = triIndices[tri * 3 + 1];
				int i2 = triIndices[tri * 3 + 2];
				if((uint)i0 >= bakedVertsLocal.Length || (uint)i1 >= bakedVertsLocal.Length || (uint)i2 >= bakedVertsLocal.Length)
					continue;

				Vector3 w0 = rendererTransform.TransformPoint(bakedVertsLocal[i0]);
				Vector3 w1 = rendererTransform.TransformPoint(bakedVertsLocal[i1]);
				Vector3 w2 = rendererTransform.TransformPoint(bakedVertsLocal[i2]);

				Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
				float nm = n.magnitude;
				if(nm < 1e-7f)
					continue;
				n /= nm;
				if(Vector3.Dot(n, rayDirWorld) > -facingThreshold)
					continue;

				bool anyInside =
				(w0 - hitPointWorld).sqrMagnitude <= radiusSqr ||
				(w1 - hitPointWorld).sqrMagnitude <= radiusSqr ||
				(w2 - hitPointWorld).sqrMagnitude <= radiusSqr;

				bool edgeIntersects = false;
				if(!anyInside) {
					if(SegmentSphereIntersect(w0, w1, hitPointWorld, radiusSqr) ||
					SegmentSphereIntersect(w1, w2, hitPointWorld, radiusSqr) ||
					SegmentSphereIntersect(w2, w0, hitPointWorld, radiusSqr))
						edgeIntersects = true;
				}

				if(!anyInside && !edgeIntersects)
					continue;

				includedTriangles.Add(i0);
				includedTriangles.Add(i1);
				includedTriangles.Add(i2);
				includedVertex[i0] = includedVertex[i1] = includedVertex[i2] = true;
				selectedTriIds?.Add(tri);
			}

			if(debug)
				LogInfo($"DecalRenderTexture.SelectTriangles: {includedTriangles.Count / 3} tris selected.");
		}

		private static bool SegmentSphereIntersect(Vector3 a, Vector3 b, Vector3 center, float radiusSqr) {
			Vector3 ab = b - a;
			float lenSqr = ab.sqrMagnitude;
			if(lenSqr < 1e-12f)
				return (a - center).sqrMagnitude <= radiusSqr;
			float t = Vector3.Dot(center - a, ab) / lenSqr;
			t = Mathf.Clamp01(t);
			Vector3 closest = a + t * ab;
			return (closest - center).sqrMagnitude <= radiusSqr;
		}
		#endregion

		#region Projection Axis (reused)
		private static void BuildProjectionAxesAroundRay(Vector3 rayDirLocal, float angleDeg, out Vector3 axisX, out Vector3 axisY) {
			Vector3 up = (Mathf.Abs(Vector3.Dot(rayDirLocal, Vector3.up)) > 0.95f) ? Vector3.right : Vector3.up;
			axisX = Vector3.Cross(up, rayDirLocal).normalized;
			axisY = Vector3.Cross(rayDirLocal, axisX).normalized;
			float rad = angleDeg * Mathf.Deg2Rad;
			float c = Mathf.Cos(rad);
			float s = Mathf.Sin(rad);
			Vector3 rx = axisX * c + axisY * s;
			Vector3 ry = -axisX * s + axisY * c;
			axisX = rx.normalized;
			axisY = ry.normalized;
		}
		#endregion

		#region Materials & Shaders
		private static Material GetOrCreateStampMaterial(bool forceLinear) {
			Shader stampShader = Shader.Find("Hidden/UMA/DecalRTStamp");
			if(stampShader == null) {
				LogWarn("DecalRenderTexture: stamp shader 'Hidden/UMA/DecalRTStamp' not found.");
				return null;
			}
			var mat = new Material(stampShader) { name = "DecalRTStamp_Mat" };
			mat.SetFloat("_ForceLinear", forceLinear ? 1f : 0f);
			mat.SetFloat("_FlipY", NeedsRenderTargetYFlip() ? 1f : 0f);
			// Force a fixed LOD during stamping to keep both sides of a UV island consistent at the seam
			mat.SetFloat("_UseFixedLOD", 1.0f);
			mat.SetFloat("_FixedLOD", 0.0f); // LOD0 for sharpest sampling; change if you want a different mip level
			return mat;
		}

		#endregion
		private static void RebindTextureOnMaterials(UMAData.GeneratedMaterial gm, int channel, Texture newTex, string explicitPropertyName) {
			if(gm == null || newTex == null)
				return;
			string propName = explicitPropertyName;
			if(string.IsNullOrEmpty(propName) && gm.textureNameList != null && channel >= 0 && channel < gm.textureNameList.Length) {
				propName = gm.textureNameList[channel];
			}
			if(string.IsNullOrEmpty(propName) && gm.umaMaterial != null && gm.umaMaterial.channels != null && channel < gm.umaMaterial.channels.Length) {
				propName = gm.umaMaterial.channels[channel].materialPropertyName;
			}
			bool rebound = false;
			if(!string.IsNullOrEmpty(propName)) {
				if(gm.material != null && gm.material.HasProperty(propName)) {
					gm.material.SetTexture(propName, newTex);
					rebound = true;
					LogInfo($"RebindTextureOnMaterials: Set '{propName}' on '{gm.material.name}'.");
				}
				if(gm.secondPassMaterial != null && gm.secondPassMaterial.HasProperty(propName)) {
					gm.secondPassMaterial.SetTexture(propName, newTex);
					rebound = true;
					LogInfo($"RebindTextureOnMaterials: Set '{propName}' on second pass '{gm.secondPassMaterial.name}'.");
				}
				if(rebound)
					return;
			}
			var canFallbackToCommon = false;
			if(gm.umaMaterial != null && gm.umaMaterial.channels != null && channel >= 0 && channel < gm.umaMaterial.channels.Length) {
				var chType = gm.umaMaterial.channels[channel].channelType;
				canFallbackToCommon = (chType == UMAMaterial.ChannelType.DiffuseTexture);
			}
			if(!canFallbackToCommon) {
				LogWarn($"RebindTextureOnMaterials: Could not resolve property for channel {channel} on material '{gm.material?.name}'. No fallback used (not Diffuse). Texture may not display.");
				return;
			}
			string[] commonProps = { "_BaseMap", "_MainTex", "_BaseColorMap" };
			foreach(var p in commonProps) {
				if(gm.material != null && gm.material.HasProperty(p)) {
					gm.material.SetTexture(p, newTex);
					LogInfo($"RebindTextureOnMaterials: Fallback set '{p}' on '{gm.material.name}'.");
					rebound = true;
					break;
				}
			}
			foreach(var p in commonProps) {
				if(gm.secondPassMaterial != null && gm.secondPassMaterial.HasProperty(p)) {
					gm.secondPassMaterial.SetTexture(p, newTex);
					LogInfo($"RebindTextureOnMaterials: Fallback set '{p}' on second pass '{gm.secondPassMaterial.name}'.");
					rebound = true;
					break;
				}
			}
			if(!rebound) {
				LogWarn($"RebindTextureOnMaterials: No matching property found on materials for channel {channel}. Texture may not be visible.");
			}
		}

		private static void RunDilation(RenderTexture rt, int bleedPixels) {
			if(bleedPixels <= 0)
				return;
			Shader dilateShader = Shader.Find("Hidden/UMA/DecalRTDilate");
			if(dilateShader == null) {
				LogWarn("DecalRenderTexture: Dilation shader 'Hidden/UMA/DecalRTDilate' not found.");
				return;
			}
			var mat = new Material(dilateShader) { name = "DecalRT_DilateMat" };
			mat.SetFloat("_PreserveAlpha", 1.0f);
			mat.SetFloat("_MinNeighborAlpha", 0.1f);
			int remaining = Mathf.Max(0, bleedPixels);
			while(remaining > 0) {
				int step = Mathf.Min(remaining, 16);
				mat.SetFloat("_Radius", step);
				RenderTexture tmp = RenderTexture.GetTemporary(rt.descriptor);
				if(tmp == null) {
					LogWarn("RunDilation: Temporary RT allocation failed. Skipping dilation step.");
					break;
				}
				Graphics.Blit(rt, tmp);
				Graphics.Blit(tmp, rt, mat);
				RenderTexture.ReleaseTemporary(tmp);
				remaining -= step;
			}
			if(Application.isPlaying)
				UnityEngine.Object.Destroy(mat);
			else
				UnityEngine.Object.DestroyImmediate(mat);
		}

		// Helper to expand triangles slightly in UV space and build a mesh for stamping
		private static Mesh BuildStampMeshWithExpansion(List<Vector2> baseUV, List<Vector2> overlayUV, List<Color32> colors, int[] triangles, float expandPixels, int texWidth, int texHeight) {
			int vcount = baseUV != null ? baseUV.Count :0;
			if(vcount ==0 || overlayUV == null || colors == null || triangles == null) {
				LogWarn("BuildStampMeshWithExpansion: Invalid inputs (null lists or zero vertices).");
				return null;
			}

			var expandedUV = new Vector2[vcount];
			for(int i =0; i < vcount; i++)
				expandedUV[i] = baseUV[i];

			// Convert pixel expansion to normalized UV offset per axis
			float ex = (texWidth >0) ? (expandPixels / texWidth) :0f;
			float ey = (texHeight >0) ? (expandPixels / texHeight) :0f;

			if((ex >0f || ey >0f) && triangles.Length >=3) {
			 int triCount = triangles.Length /3;
			 for(int ti =0; ti < triCount; ti++) {
				 int a = triangles[ti *3 +0];
				 int b = triangles[ti *3 +1];
				 int c = triangles[ti *3 +2];
				 if((uint)a >= vcount || (uint)b >= vcount || (uint)c >= vcount)
					 continue;

				 Vector2 ua = expandedUV[a];
				 Vector2 ub = expandedUV[b];
				 Vector2 uc = expandedUV[c];
				 Vector2 centroid = (ua + ub + uc) /3f;

				 // Move the vertices slightly away from the centroid in UV space
				 int[] ids = { a, b, c };
				 for(int k =0; k <3; k++) {
					 int id = ids[k];
					 Vector2 v = expandedUV[id];
					 Vector2 dir = v - centroid;
					 float len2 = dir.sqrMagnitude;
					 if(len2 >1e-12f) {
						 float invLen =1.0f / Mathf.Sqrt(len2);
						 Vector2 dn = dir * invLen;
						 v += new Vector2(dn.x * ex, dn.y * ey);
						 v.x = Mathf.Clamp01(v.x);
						 v.y = Mathf.Clamp01(v.y);
						 expandedUV[id] = v;
					 }
				 }
			 }
			}

			// Build clip-space vertices (-1..1) from expanded UVs
			var vertsList = new List<Vector3>(vcount);
			for(int i =0; i < vcount; i++) {
			 Vector2 uv = expandedUV[i];
			 vertsList.Add(new Vector3(uv.x *2f -1f, uv.y *2f -1f,0f));
			}

			var mesh = new Mesh { name = "DecalRT_Stamp_Expanded" };
			mesh.indexFormat = (vcount >65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
			mesh.SetVertices(vertsList);
			mesh.SetTriangles(triangles,0);
			mesh.SetUVs(0, expandedUV);
			mesh.SetUVs(1, overlayUV);
			mesh.SetColors(colors);
			mesh.RecalculateBounds();
			return mesh;
		}

		public static void SaveStampTexturePNG(Texture srcTex, DecalRTStampAsset stamp, String overlayName, int frame, String suffix)
		{
			if (srcTex == null)
			{
				LogWarn("SaveStampTexturePNG: source texture is null");
				return;
			}
			if (stamp == null)
			{
				LogWarn("SaveStampTexturePNG: stamp asset is null");
				return;
			}
			string FileNameSafe(string s)
			{
				foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
				return s;
            }
			string folder = null;
			string fullPath = null;

            int seq = System.Threading.Interlocked.Increment(ref _snapshotSequence);
            string baseName = $"{seq:0000}-{FileNameSafe(overlayName)}_frame{frame}_{suffix}.png";

#if UNITY_EDITOR
			try
			{
				string stampPath = UnityEditor.AssetDatabase.GetAssetPath(stamp);
				if (!string.IsNullOrEmpty(stampPath))
				{
					folder = Path.GetDirectoryName(stampPath).Replace('\\', '/');
					if (!folder.StartsWith("Assets", StringComparison.Ordinal))
					{
						folder = "Assets/UMA/GeneratedDecalStamps";
					}
					string projectRoot = Path.GetDirectoryName(Application.dataPath);
					string absFolder = Path.Combine(projectRoot, folder);
					if (!Directory.Exists(absFolder)) Directory.CreateDirectory(absFolder);
					fullPath = Path.Combine(absFolder, baseName);
				}
			}
			catch
			{
				folder = null;
				fullPath = null;
			}
#endif
			if (string.IsNullOrEmpty(fullPath))
			{
				folder = Path.Combine(Application.persistentDataPath, "UMADecals");
				if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
				fullPath = Path.Combine(folder, baseName);
			}

			Texture2D readTex = null;
			RenderTexture tmp = null;
			try
			{
				var rt = srcTex as RenderTexture;
				if (rt != null)
				{
					var prev = RenderTexture.active;
					try
					{
						RenderTexture.active = rt;
						readTex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
						readTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
						readTex.Apply(false, false);
					}
					finally
					{
						RenderTexture.active = prev;
					}
				}
				else
				{
					var tex2D = srcTex as Texture2D;
					if (tex2D == null)
					{
						LogWarn("SaveStampTexturePNG: Unsupported texture type: " + srcTex.GetType().Name);
						return;
					}

					if (tex2D.isReadable)
					{
						readTex = new Texture2D(tex2D.width, tex2D.height, TextureFormat.RGBA32, false);
						readTex.SetPixels32(tex2D.GetPixels32());
						readTex.Apply(false, false);
					}
					else
					{
						tmp = RenderTexture.GetTemporary(tex2D.width, tex2D.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
						Graphics.Blit(tex2D, tmp);
						var prev = RenderTexture.active;
						try
						{
							RenderTexture.active = tmp;
							readTex = new Texture2D(tmp.width, tmp.height, TextureFormat.RGBA32, false);
							readTex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0, false);
							readTex.Apply(false, false);
						}
						finally
						{
							RenderTexture.active = prev;
						}
					}
				}

				byte[] png = readTex.EncodeToPNG();
				File.WriteAllBytes(fullPath, png);
				LogInfo("Saved stamp source texture snapshot: " + fullPath);

#if UNITY_EDITOR
				try
				{
					string dataPath = Application.dataPath.Replace('\\', '/');
					string full = fullPath.Replace('\\', '/');
					string absAssetsRoot = dataPath.Substring(0, dataPath.Length - "/Assets".Length) + "/Assets";
					if (full.StartsWith(absAssetsRoot, StringComparison.OrdinalIgnoreCase))
					{
						string rel = "Assets" + full.Substring(absAssetsRoot.Length);
						UnityEditor.AssetDatabase.ImportAsset(rel);
					}
				}
				catch { }
#endif
			}
			catch (Exception ex)
			{
				LogWarn("SaveStampTexturePNG failed: " + ex.Message);
			}
			finally
			{
				if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
				if (readTex != null)
				{
					if (Application.isPlaying) UnityEngine.Object.Destroy(readTex);
					else UnityEngine.Object.DestroyImmediate(readTex);
				}
			}
		}

        int sequence = 0;
		// Save RT as PNG next to the stamp asset (Editor); fallback to persistentDataPath at runtime
		public static void SaveRenderTexturePNG(RenderTexture rt, DecalRTStampAsset stamp, String overlayName, int frame, String suffix) {

			//return; // Disable for now

			if (rt == null) {
				LogWarn("SaveRenderTexturePNG: target RT is null");
				return;
			}

			string FileNameSafe(string s) {
				foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
				return s;
			}
			Debug.Log("SaveRenderTexturePNG called");
			string folder = null;
#if UNITY_EDITOR
			try {
				string stampPath = UnityEditor.AssetDatabase.GetAssetPath(stamp);
				if (!string.IsNullOrEmpty(stampPath)) {
					folder = Path.GetDirectoryName(stampPath).Replace('\\', '/');
					if (!folder.StartsWith("Assets")) folder = "Assets/UMA/GeneratedDecalStamps";
					string projectRoot = Path.GetDirectoryName(Application.dataPath);
					string abs = Path.Combine(projectRoot, folder);
					if (!Directory.Exists(abs)) Directory.CreateDirectory(abs);
				} else {
					folder = "Assets/UMA/GeneratedDecalStamps";
					string abs = Path.Combine(Path.GetDirectoryName(Application.dataPath), folder);
					if (!Directory.Exists(abs)) Directory.CreateDirectory(abs);
				}
			} catch {
				folder = null;
			}
#endif
			if (string.IsNullOrEmpty(folder)) {
				// Runtime fallback
				folder = Path.Combine(Application.persistentDataPath, "UMADecals");
				if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
			}

			int seq = System.Threading.Interlocked.Increment(ref _snapshotSequence);
			string baseName = $"{seq:0000}-{FileNameSafe(overlayName)}_frame{frame}_{suffix}.png";

			string fullPath;
#if UNITY_EDITOR
			// Convert relative Assets path to absolute
			if (folder.StartsWith("Assets")) {
				string projectRoot = Path.GetDirectoryName(Application.dataPath);
				fullPath = Path.Combine(projectRoot, folder, baseName);
			} else {
				fullPath = Path.Combine(folder, baseName);
			}
#else
			fullPath = Path.Combine(folder, baseName);
#endif

			var prev = RenderTexture.active;
			try {
				RenderTexture.active = rt;
				Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
				tex.ReadPixels(new Rect(0,0, rt.width, rt.height),0,0, false);
				tex.Apply(false, false);
				byte[] png = tex.EncodeToPNG();
				File.WriteAllBytes(fullPath, png);
#if UNITY_EDITOR
				if (fullPath.Replace('\\', '/').StartsWith(Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/Assets")) {
					string rel = "Assets" + fullPath.Replace('\\', '/').Split(new[] { "/Assets" }, StringSplitOptions.None)[1];
					UnityEditor.AssetDatabase.ImportAsset(rel);
				}
#endif
				LogInfo($"Saved RT snapshot: {fullPath}");
				if (Application.isPlaying) UnityEngine.Object.Destroy(tex); else UnityEngine.Object.DestroyImmediate(tex);
			} catch (Exception ex) {
				LogWarn($"SaveRenderTexturePNG failed: {ex.Message}");
			} finally {
				RenderTexture.active = prev;
			}
		}
	}
}