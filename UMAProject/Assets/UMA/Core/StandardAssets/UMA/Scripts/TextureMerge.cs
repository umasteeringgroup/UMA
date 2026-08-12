#define UMA_ADVANCED_BLENDMODES
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using System;

namespace UMA
{
    /// <summary>
    /// Utility class that sets up materials for atlasing.
    /// </summary>
    [ExecuteInEditMode]
	[CreateAssetMenu(menuName = "UMA/Rendering/TextureMerge")]
	public class TextureMerge : ScriptableObject
	{
		[Serializable]
		public class BlendModeShaders
		{
			public BlendOp BlendMode;
			public Shader Combiner;
		}

		public Material material;
		public Shader normalShader;
		public Shader diffuseShader;
		public Shader dataShader;
		public Shader cutoutShader;
		public Shader detailNormalShader;
		public Shader transparentPrefillShader;
		private Vector2 pivotPoint = new Vector2();
		private Material transparentPrefillMaterial;
		private bool reportedMissingTransparentPrefillShader;

		[System.NonSerialized]
		public Color camBackgroundColor = new Color(0, 0, 0, 0);

		public List<UMAPostProcess> diffusePostProcesses = new List<UMAPostProcess>();
		public List<UMAPostProcess> normalPostProcesses = new List<UMAPostProcess>();
		public List<UMAPostProcess> dataPostProcesses = new List<UMAPostProcess>();
		public List<UMAPostProcess> detailNormalPostProcesses = new List<UMAPostProcess>();
		[Header("Blend Mode Shaders.", order = 1)]
		[Header("Note 'logical' blend modes are only available on DX11", order = 2)]
		public List<BlendModeShaders> DiffuseBlendModeShaders = new List<BlendModeShaders>();
		public List<BlendModeShaders> DataBlendModeShaders = new List<BlendModeShaders>();
		public List<BlendModeShaders> NormalBlendModeShaders = new List<BlendModeShaders>();

		private int textureMergeRectCount;
		private TextureMergeRect[] textureMergeRects;

		private void OnDisable()
		{
			ReleaseCachedMaterials();
		}

		private void OnDestroy()
		{
			ReleaseCachedMaterials();
		}

		/// <summary>
		/// Releases the transient material instances used to draw atlas rectangles.
		/// They are a cache owned by this TextureMerge instance, not the source material asset.
		/// </summary>
		private void ReleaseCachedMaterials()
		{
			if (transparentPrefillMaterial != null)
			{
				if (Application.isPlaying)
				{
					Destroy(transparentPrefillMaterial);
				}
				else
				{
					DestroyImmediate(transparentPrefillMaterial);
				}
				transparentPrefillMaterial = null;
			}

			if (textureMergeRects == null)
			{
				return;
			}

			for (int i = 0; i < textureMergeRects.Length; i++)
			{
				Material cachedMaterial = textureMergeRects[i].mat;
				// The source material is an asset owned by the TextureMerge configuration.
				// Only the per-rectangle clones belong to this cache.
				if (cachedMaterial != null && cachedMaterial != material)
				{
					if (Application.isPlaying)
					{
						Destroy(cachedMaterial);
					}
					else
					{
						DestroyImmediate(cachedMaterial);
					}
				}
			}

			textureMergeRects = null;
			textureMergeRectCount = 0;
		}

		public void EnsurePreviewRectCapacity(int count)
		{
			EnsureCapacity(count);
		}

		public TextureMergeRect[] GetPreviewRects()
		{
			return textureMergeRects;
		}

		public void SetPreviewRectCount(int count)
		{
			textureMergeRectCount = count;
		}

		//[System.Serializable] //why was this serializable? the array was public serialized too
		public struct TextureMergeRect
		{
			public Material mat;
			public Texture tex;
			public Rect rect;
			public bool transform;
			public float rotation;
			public Vector3 scale;
			public Vector2 position;
			public bool advancedBlending;
			public int textureChannel;
			public UMAMaterial.ChannelType channelType;
			public bool transparentPrefill;
			public Color transparentPrefillColor;
			// Needed for decals
			public TextureEventParms textureEventParms;
        }

		public void RefreshMaterials()
		{
			if (textureMergeRects != null)
			{
				for (int i = 0; i < textureMergeRects.Length; i++)
				{
                    if (textureMergeRects[i].mat == null)
                    {
                        textureMergeRects[i].mat = new Material(material);
						textureMergeRects[i].mat.name = material.name + "_" + i;
                    }

                    textureMergeRects[i].advancedBlending = false;
				}
			}
		}

        static public Texture2D GetRTPixels(RenderTexture rt)
        {
			Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
			RenderTexture activeTexture = RenderTexture.active;
			bool bkUpSRGBWrite = GL.sRGBWrite;
			RenderTexture outputMap = null;

            try
            {
			    // Disabling linear to srgb conversion is not enough. It seems that Unity always does the conversion when reading from a RenderTexture.
			    // As a workaround, create a non-linear texture to blit to so the conversion happens in the intended place.
                GL.sRGBWrite = false;
                outputMap = new RenderTexture(rt.width, rt.height, 32, rt.format, RenderTextureReadWrite.sRGB);
                outputMap.name = "UMA RT Readback | " + rt.name;
                outputMap.enableRandomWrite = true;
                outputMap.Create();
                RenderTexture.active = outputMap;
                GL.Clear(true, true, Color.black);
                Graphics.Blit(rt, outputMap);

			    tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
			    return tex;
            }
            catch
            {
                UMAUtils.DestroySceneObject(tex);
                throw;
            }
            finally
            {
                GL.sRGBWrite = bkUpSRGBWrite;
			    RenderTexture.active = activeTexture;
                if (outputMap != null)
                {
                    if (outputMap.IsCreated())
                    {
                        outputMap.Release();
                    }
                    UMAUtils.DestroySceneObject(outputMap);
                }
            }
        }

        public static void SaveRenderTexture(RenderTexture texture, string textureName, bool isNormal = false)
        {
            Texture2D tex = GetRTPixels(texture);
            try
            {
                SaveTexture2D(tex, textureName);
            }
            finally
            {
                UMAUtils.DestroySceneObject(tex);
            }
        }

        private static void SaveTexture2D(Texture2D texture, string textureName)
        {
            if (texture.isReadable)
            {
                byte[] data = texture.EncodeToPNG();
                System.IO.File.WriteAllBytes(textureName, data);
            }
            else
            {
                Debug.LogError("Texture: " + texture.name + " is not readable. Skipping.");
            }
        }

		public void DrawAllRects(RenderTexture target, int width, int height, Color background = default(Color), bool sharperFitTextures = true)
		{
			if (textureMergeRects != null)
			{
				//Debug.Log("Drawing " + textureMergeRectCount + " rects to target: " + target.name + " w:" + width + " h:" + height);
				RenderTexture backup = RenderTexture.active;
				RenderTexture.active = target;
#if UMA_ADVANCED_BLENDMODES
				RenderTexture scratch = null;
#endif
				GL.Clear(true, true, background);
				GL.PushMatrix();
				try
				{
					//the matrix needs to be in the original atlas dimensions because the textureMergeRects are in that space.
					GL.LoadPixelMatrix(0, width, height, 0);

					for (int i = 0; i < textureMergeRectCount; i++)
					{
						DrawTransparentPrefill(ref textureMergeRects[i]);
					}

#if UMA_ADVANCED_BLENDMODES

					// This draws the entire atlas.
					// We need to set the base texture on any overlay that is not a base overlay.
					// To do this, we will probably need to track that on the textureMergeRects.
					// We should hard code base overlays to be normal blend mode (ie, they blend with the basic shaders)

					Rect Destination = new Rect(0, 0, 0, 0);
					Rect Src = new Rect(0, 0, 0, 0);
					for (int i = 0; i < textureMergeRectCount; i++)
					{
						var tr = textureMergeRects[i];

						//Debug.Log("Drawing rect for texture: " + (tr.tex != null ? tr.tex.name : "null") + " at " + tr.rect + " advancedBlending: " + tr.advancedBlending + " channelType: " + tr.channelType);
						if (tr.advancedBlending)
						{
							// Create a temporary texture that is the size of the overlay rect in atlas space.
							scratch = RenderTexture.GetTemporary((int)tr.rect.width, (int)tr.rect.height, 0, target.format, RenderTextureReadWrite.Linear);
							scratch.name = "UMA RT Scratch | " + target.name;

							float fw = (float)width;
							float fh = (float)height;

							GL.PushMatrix();
							try
							{
								GL.LoadPixelMatrix(0, scratch.width, scratch.height, 0);

								// Set the destination (The entire scratch texture)
								Destination.Set(0, 0, scratch.width, scratch.height);

								// Set the source rect in UV space
								Src.Set(tr.rect.x / fw, 1.0f - ((tr.rect.y + tr.rect.height) / fh), (tr.rect.width / fw), (tr.rect.height / fh));  // get the rect in UV space

								//SaveRenderTexture(target, System.IO.Path.Combine(Application.dataPath, "target-before.png"));
								// should be drawing to the scratch texture now
								RenderTexture.active = scratch;
								Graphics.DrawTexture(Destination, target, Src, 0, 0, 0, 0);
								RenderTexture.active = target;
								//SaveRenderTexture(scratch, System.IO.Path.Combine(Application.dataPath, "scratch-after.png"));
								//SaveRenderTexture(target, System.IO.Path.Combine(Application.dataPath, "target-after.png"));
							}
							finally { GL.PopMatrix(); }
							tr.mat.SetTexture("_BaseTex", scratch);
							DrawRect(ref tr, sharperFitTextures);
						}
						else
						{
							DrawRect(ref tr, sharperFitTextures);
						}

						if (scratch != null)
						{
							RenderTexture.ReleaseTemporary(scratch);
							scratch = null;
						}
					}

#else
				for (int i = 0; i < textureMergeRectCount; i++)
				{
					DrawRect(ref textureMergeRects[i], sharperFitTextures);
			    }
#endif
				}
				finally
				{
#if UMA_ADVANCED_BLENDMODES
					if (scratch != null)
					{
						RenderTexture.ReleaseTemporary(scratch);
					}
#endif
					GL.PopMatrix();
					RenderTexture.active = backup;
				}
			}
		}

		public static void RotateAroundPivot(float angle, Vector2 pivotPoint)
		{
			Matrix4x4 newMat = Matrix4x4.TRS(pivotPoint, Quaternion.Euler(0, 0, angle), Vector3.one) * Matrix4x4.TRS(-pivotPoint, Quaternion.identity, Vector3.one);
			GL.MultMatrix(newMat);
		}

		private void DrawRect(ref TextureMergeRect tr, bool sharperFitTextures)//, Material overrideMat = null)
		{
			if (tr.tex == null)
            {
                return;
            }

            //Debug.Log("Drawing rect for texture: " + tr.tex.name + " at " + tr.rect);
			GL.PushMatrix();
			try
			{
				Rect drawRect = tr.rect;

				if (tr.transform)
				{
					drawRect.x += tr.position.x;
					drawRect.y += tr.position.y;

					// rotate around the pivot
					pivotPoint.Set(drawRect.x + (drawRect.width / 2.0f), drawRect.y + (drawRect.height / 2.0f));

					Matrix4x4 newMat = Matrix4x4.TRS(pivotPoint, Quaternion.Euler(0, 0, tr.rotation), tr.scale) * Matrix4x4.TRS(-pivotPoint, Quaternion.identity, Vector3.one);

					GL.MultMatrix(newMat);
				}

				if (sharperFitTextures)
				{
					tr.tex.mipMapBias = -1.0f;
				}
				else
				{
					tr.tex.mipMapBias = 0f;
				}

				Graphics.DrawTexture(drawRect, tr.tex, tr.mat);
			}
			finally {
				GL.PopMatrix();
			}

            if (tr.textureEventParms != null)
            {
                tr.textureEventParms.renderTexture = RenderTexture.active;
#if true
                // Ensure SlotData.UVArea is valid BEFORE firing AtlasUpdated (decal stamping depends on it).
                var tp = tr.textureEventParms;
                var baseOverlay = tp.slotData.GetOverlay(0);

				if(tp.slotData != null && tp.renderTexture != null && baseOverlay != null && tp.overlayData == tp.slotData.GetOverlay(0) && tp.slotData.uvAreaUpdateFrame != Time.frameCount) {
                    float w = Mathf.Max(1f, tp.renderTexture.width);
                    float h = Mathf.Max(1f, tp.renderTexture.height);
                    tp.slotData.uvAreaUpdateFrame = Time.frameCount;

					// Convert from top-left pixel space (GL.LoadPixelMatrix(..., height, 0))
					// to bottom-left UV space (UMA SlotData.UVArea / mesh UV convention).
					float xMin = tr.rect.xMin / w;
					float yMin = (h - (tr.rect.yMin + tr.rect.height)) / h; // unflip Y
					float xRange = tr.rect.width / w;
					float yRange = tr.rect.height / h;

					tp.slotData.UVArea.Set(xMin, yMin, xRange, yRange);
                }
#endif
                tr.textureEventParms.umaData.FireAtlasUpdatedEvent(tr.textureEventParms);
				tr.textureEventParms.renderTexture = null;
            }
        }

		private void DrawTransparentPrefill(ref TextureMergeRect tr)
		{
			if (!tr.transparentPrefill)
			{
				return;
			}

			Material fillMaterial = GetTransparentPrefillMaterial();
			if (fillMaterial == null)
			{
				return;
			}

			GL.PushMatrix();
			try
			{
				Rect drawRect = tr.rect;
				if (tr.transform)
				{
					drawRect.x += tr.position.x;
					drawRect.y += tr.position.y;
					pivotPoint.Set(
						drawRect.x + (drawRect.width * 0.5f),
						drawRect.y + (drawRect.height * 0.5f));
					GL.MultMatrix(
						Matrix4x4.TRS(
							pivotPoint,
							Quaternion.Euler(0f, 0f, tr.rotation),
							tr.scale) *
						Matrix4x4.TRS(
							-pivotPoint,
							Quaternion.identity,
							Vector3.one));
				}

				fillMaterial.SetColor("_Color", tr.transparentPrefillColor);
				Graphics.DrawTexture(drawRect, Texture2D.whiteTexture, fillMaterial);
			}
			finally
			{
				GL.PopMatrix();
			}
		}

		private Material GetTransparentPrefillMaterial()
		{
			if (transparentPrefillMaterial != null)
			{
				return transparentPrefillMaterial;
			}

			Shader fillShader = transparentPrefillShader != null
				? transparentPrefillShader
				: Shader.Find("Hidden/UMA/TransparentPrefill");
			if (fillShader == null)
			{
				if (!reportedMissingTransparentPrefillShader)
				{
					reportedMissingTransparentPrefillShader = true;
					Debug.LogWarning(
						"UMA could not find the Hidden/UMA/TransparentPrefill shader. " +
						"Per-overlay transparent prefills will be skipped.",
						this);
				}
				return null;
			}

			transparentPrefillMaterial = new Material(fillShader)
			{
				name = "UMA Transparent Prefill",
				hideFlags = HideFlags.HideAndDontSave
			};
			return transparentPrefillMaterial;
		}

		public void PostProcess(RenderTexture destination, UMAMaterial.ChannelType channelType)
		{
			if (channelType == UMAMaterial.ChannelType.DiffuseTexture && diffusePostProcesses.Count == 0)
            {
                return;
            }

            if (channelType == UMAMaterial.ChannelType.NormalMap && normalPostProcesses.Count == 0)
            {
                return;
            }

            if (channelType == UMAMaterial.ChannelType.Texture && dataPostProcesses.Count == 0)
            {
                return;
            }

            if (channelType == UMAMaterial.ChannelType.DetailNormalMap && detailNormalPostProcesses.Count == 0)
            {
                return;
            }

            var source = RenderTexture.GetTemporary(destination.width, destination.height, 0, destination.format, RenderTextureReadWrite.Linear);
            source.name = "UMA RT Post Process | " + destination.name;
            try
            {
			    switch (channelType)
			    {
				    case UMAMaterial.ChannelType.NormalMap:
                        for (int i = 0; i < normalPostProcesses.Count; i++)
					    {
                            UMAPostProcess postProcess = normalPostProcesses[i];
                            Graphics.Blit(destination, source);
						    postProcess.Process(source, destination);
					    }
					    break;
				    case UMAMaterial.ChannelType.Texture:
					    for (int i = 0; i < dataPostProcesses.Count; i++)
                        {
                            UMAPostProcess postProcess = dataPostProcesses[i];
                            Graphics.Blit(destination, source);
                            postProcess.Process(source, destination);
                        }
					    break;
				    case UMAMaterial.ChannelType.DiffuseTexture:
					    for (int i = 0; i < diffusePostProcesses.Count; i++)
                        {
                            UMAPostProcess postProcess = diffusePostProcesses[i];
                            Graphics.Blit(destination, source);
                            postProcess.Process(source, destination);
                        }
					    break;
				    case UMAMaterial.ChannelType.DetailNormalMap:
					    for (int i = 0; i < detailNormalPostProcesses.Count; i++)
                        {
                            UMAPostProcess postProcess = detailNormalPostProcesses[i];
                            Graphics.Blit(destination, source);
                            postProcess.Process(source, destination);
                        }
					    break;
			    }
            }
            finally
            {
			    RenderTexture.active = null;
			    RenderTexture.ReleaseTemporary(source);
            }
		}

		public void Reset()
		{
			textureMergeRectCount = 0;
		}

		internal void EnsureCapacity(int moduleCount)
		{
			if (textureMergeRects != null && textureMergeRects.Length > moduleCount)
            {
                return;
            }

            var oldTextureMerge = textureMergeRects;
			var newLength = 100;
			while (newLength < moduleCount)
            {
                newLength *= 2;
            }

            textureMergeRects = new TextureMerge.TextureMergeRect[newLength];
			int idx = 0;
			if (oldTextureMerge != null)
			{
				for (idx = 0; idx < oldTextureMerge.Length; idx++)
				{
					textureMergeRects[idx].mat = oldTextureMerge[idx].mat;
				}
			}
			for (; idx < newLength; idx++)
			{
				textureMergeRects[idx].mat = new Material(material);
				textureMergeRects[idx].mat.name = material.name + "_" + idx;
			}
		}

		private void SetupMaterialAndBaseOverlay(ref TextureMergeRect textureMergeRect, UMAData.MaterialFragment source, int textureChannel, UMAData umaData)
		{
			textureMergeRect.transparentPrefill = false;
			textureMergeRect.transparentPrefillColor = Color.clear;
			if (source.isNoTextures)
            {
                return;
            }

            camBackgroundColor = source.GetMultiplier(0, textureChannel);
			camBackgroundColor.a = 0.0f;

			if (textureChannel >= source.baseOverlay.textureList.Length)
			{
				// Debug.Log("Out of range (" + textureType + ") on base overlay: " + source.overlayData[0].overlayName + " on slot: " + source.slotData.slotName);
				return;
			}
			textureMergeRect.tex = source.baseOverlay.textureList[textureChannel];
            textureMergeRect.advancedBlending = false;
			TextureEventParms tep = new TextureEventParms(null, source.overlayData[0], source.slotData, umaData, textureChannel );
			UMAMaterial sourceMaterial = source.umaMaterial != null ? source.umaMaterial : source.slotData?.material;
			if (sourceMaterial?.channels == null || textureChannel >= sourceMaterial.channels.Length)
			{
				return;
			}

            // JRRM debug
			textureMergeRect.textureChannel = textureChannel;
			textureMergeRect.channelType = sourceMaterial.channels[textureChannel].channelType;
			textureMergeRect.textureChannel = textureChannel;
            textureMergeRect.textureEventParms = tep;
			ConfigureTransparentPrefill(
				ref textureMergeRect,
				source,
				0,
				textureChannel);

            switch (sourceMaterial.channels[textureChannel].channelType)
			{
				case UMAMaterial.ChannelType.NormalMap:
					textureMergeRect.mat.shader = normalShader;
					break;
				case UMAMaterial.ChannelType.Texture:
					textureMergeRect.mat.shader = dataShader;
					break;
				case UMAMaterial.ChannelType.DiffuseTexture:
					textureMergeRect.mat.shader = diffuseShader;
					break;
				case UMAMaterial.ChannelType.DetailNormalMap:
					textureMergeRect.mat.shader = detailNormalShader;
					break;
			}

#if debug_texturecombine
            if (source.baseOverlay.textureList[textureType] != null)
            {
                Debug.Log("Base overlay: " + source.baseOverlay.textureList[textureType].name + " on slot: " + source.slotData.slotName);
            }
            else
            {
                Debug.Log("Base overlay: NULL on slot: " + source.slotData.slotName);
            }

			var multiplier = source.GetMultiplier(0, textureType);
			var additive = source.GetAdditive(0, textureType);

            //Debug.Log($"Base overlay multiplier: {multiplier} on slot: {source.slotData.slotName} TextureType {textureType}");
            //Debug.Log($"Base overlay additive: {additive} on slot: {source.slotData.slotName} TextureType {textureType}");



#endif
            textureMergeRect.mat.SetTexture("_MainTex", source.baseOverlay.textureList[textureChannel]);
			textureMergeRect.mat.SetTexture("_ExtraTex", source.baseOverlay.alphaTexture);
			textureMergeRect.mat.SetColor("_Color", source.GetMultiplier(0, textureChannel));
			textureMergeRect.mat.SetColor("_AdditiveColor", source.GetAdditive(0, textureChannel));
		}

		public void SetupMaterialAndBaseOverlay(UMAData.MaterialFragment source, int textureChannel, UMAData umaData)
		{
			if (!source.isNoTextures)
			{
				textureMergeRects[textureMergeRectCount].transform = false;
				textureMergeRects[textureMergeRectCount].rect = source.atlasRegion;
				textureMergeRects[textureMergeRectCount].rect.y = height - textureMergeRects[textureMergeRectCount].rect.y - textureMergeRects[textureMergeRectCount].rect.height;
				atlasRect = textureMergeRects[textureMergeRectCount].rect;
				SetupMaterialAndBaseOverlay(ref textureMergeRects[textureMergeRectCount], source, textureChannel, umaData);
			}
			textureMergeRectCount++;
		}

		Rect atlasRect;
		Vector2 resolutionScale;
		int height;


        /// <summary>
        /// SetupSlotAndOverlayStack sets up the texture merge for a specific material fragment (slot + overlay stack) in an atlas.
		/// �	A MaterialFragment represents one slot�s contribution to a single atlas/material. It holds:
		/// �	Where to draw in the final atlas: atlasRegion(target rect in the combined texture).
		/// �	What to draw: baseOverlay(the first overlay) and overlays[] (additional overlays).
		/// �	How to draw: per-overlay rects[], color multipliers/additives, and supporting data(slotData, overlayData[], masks).
		/// 
		/// So basically, for a single slot, this sets up the base overlay, then sets up each additional overlay in turn, so they can all be drawn in the correct order.
		/// This happens later, in DrawAllRects.
		///  
        /// </summary>
        /// <param name="atlas"></param>
        /// <param name="idx"></param>
        /// <param name="textureChannel"></param>
        public void SetupSlotAndOverlayStack(UMAData.GeneratedMaterial atlas, int idx, int textureChannel, UMAData umaData)
		{
			var atlasElement = atlas.materialFragments[idx];

            if (atlasElement.isRectShared)
            {
                return;
            }

            height = Mathf.FloorToInt(atlas.cropResolution.y);

            // Here we setup the material and the base overlay TextureMergeRect.
            SetupMaterialAndBaseOverlay(atlasElement, textureChannel, umaData);
			resolutionScale = atlas.resolutionScale * atlasElement.slotData.overlayScale;

			if (atlasElement.AdditionalOverlays == null)
            {
				//Debug.LogWarning($"SetupSlotAndOverlayStack: No overlays in atlasElement for slot {atlasElement.slotData.slotName}");
                return;
            }


            // If there are additional overlays, we need to set them up too.
            // Each overlay may have a different blend mode, so we need to set that up on the material.
            // Note that overlayList[0] is the base overlay, so when we need to get to the actual overlayData over any
            // additional overlay, we need to use overlayData[AdditionalOverlayIndex + 1]
            for (int i = 0; i < atlasElement.AdditionalOverlays.Length; i++)
			{
				string texname = "";

				if (atlasElement.AdditionalOverlays[i] != null)
				{

					if (textureChannel < atlasElement.AdditionalOverlays[i].textureList.Length)
					{
						var tex = atlasElement.AdditionalOverlays[i].textureList[textureChannel];

						if (tex != null)
						{
							texname = tex.name;
						}
						else
						{
							texname = "null texture";
						}
					}
					else
					{
						texname = "textureType out of range";
                    }
				}
				else
				{
					texname = "null overlay";
				}

				//Debug.Log($"SetupOverlay [{i}] Slot [{atlasElement.slotData.slotName}], Overlay [{atlasElement.overlayList[i].overlayName}] texture [{texname}] Channel [{textureType}]");
				//DebugCSV($"{atlasElement.slotData.slotName}, {i}, {atlasElement.overlayList[i].overlayName}, {texname}, {textureType}");
                SetupOverlay(atlasElement, i, textureChannel, umaData);
			}
		}

		private void DebugCSV(string msg)
		{
			System.IO.File.AppendAllText("c:\\tmp\\TextureMerge.csv", msg + "\n");
        }

        private void SetupOverlay(UMAData.MaterialFragment source, int AdditionalOverlayIndex, int textureChannel, UMAData umaData)
		{
			if (source.AdditionalOverlays[AdditionalOverlayIndex] == null)
            {
                return;
            }

            if (textureChannel >= source.AdditionalOverlays[AdditionalOverlayIndex].textureList.Length)
            {
                return;
            }

            if (source.AdditionalOverlays[AdditionalOverlayIndex].textureList[textureChannel] == null)
            {
                return;
            }

            if (source.isNoTextures)
            {
                return;
            }

            Rect overlayRect;

			if (source.rects[AdditionalOverlayIndex].width != 0)
			{
				overlayRect = new Rect(atlasRect.xMin + source.rects[AdditionalOverlayIndex].x * resolutionScale.x, atlasRect.yMax - source.rects[AdditionalOverlayIndex].y * resolutionScale.y - source.rects[AdditionalOverlayIndex].height * resolutionScale.y, source.rects[AdditionalOverlayIndex].width * resolutionScale.x, source.rects[AdditionalOverlayIndex].height * resolutionScale.y);
			}
			else
			{
				overlayRect = atlasRect;
			}

			SetupMaterial(ref textureMergeRects[textureMergeRectCount], source, AdditionalOverlayIndex, ref overlayRect, textureChannel);

            /* set the parameters for decal overlays */
            textureMergeRects[textureMergeRectCount].textureEventParms = new TextureEventParms(null,  source.overlayData[AdditionalOverlayIndex + 1], source.slotData, umaData, textureChannel);

            /* Event parameters setup */

            // We only get here if we have more than one overlay. 
            // The additional overlay we are blending is found in the overlayData[AdditionalOverlayIndex + 1] (overlayData[0] is the base overlay)
            if (source.overlayData[AdditionalOverlayIndex + 1].instanceTransformed)
			{
				OverlayData od = source.overlayData[AdditionalOverlayIndex + 1];
				textureMergeRects[textureMergeRectCount].transform = true;
				textureMergeRects[textureMergeRectCount].rotation = od.Rotation;
				textureMergeRects[textureMergeRectCount].scale = od.Scale;

                var tex = source.overlayData[0].GetTexture(0);
				if (tex != null)
				{
					float xx = od.Translate.x * tex.width;
                    float yy = od.Translate.y * tex.height;
					textureMergeRects[textureMergeRectCount].position = new Vector2(xx, yy);
                    textureMergeRects[textureMergeRectCount].rect = new Rect(overlayRect.x, overlayRect.y, overlayRect.width, overlayRect.height);
                }
				else
				{
					textureMergeRects[textureMergeRectCount].position.Set(0, 0);
				}
            }
			else
			{
				textureMergeRects[textureMergeRectCount].transform = false;
			}
			textureMergeRectCount++;
		}

		private Shader GetBlendModeDiffuseShader(OverlayData od, int TextureType, out bool isAdvanced)
		{
			var blendmode = od.GetOverlayBlend(TextureType);

			isAdvanced = false;
			if (blendmode == OverlayDataAsset.OverlayBlend.Normal)
			{
				return DiffuseBlendModeShaders[0].Combiner;
			}
			for (int i = 0; i < DiffuseBlendModeShaders.Count; i++)
            {
                var s = DiffuseBlendModeShaders[i];
                if ((int)s.BlendMode == (int)blendmode)
                {
                    isAdvanced = true;
                    return s.Combiner;
                }
            }
			return diffuseShader;
		}

        private Shader GetBlendModeShader(List<BlendModeShaders> shaderList, OverlayData od, int TextureType, out bool isAdvanced)
        {
            var blendmode = od.GetOverlayBlend(TextureType);

            isAdvanced = false;
            if (blendmode == OverlayDataAsset.OverlayBlend.Normal)
            {
                return shaderList[0].Combiner;
            }
            for (int i = 0; i < shaderList.Count; i++)
            {
                var s = shaderList[i];
                if ((int)s.BlendMode == (int)blendmode)
                {
                    isAdvanced = true;
                    return s.Combiner;
                }
            }
            return shaderList[0].Combiner;
        }

        private void SetupMaterial(ref TextureMergeRect textureMergeRect, UMAData.MaterialFragment source, int i2, ref Rect overlayRect, int textureType)
		{
			textureMergeRect.rect = overlayRect;
			textureMergeRect.tex = source.AdditionalOverlays[i2].textureList[textureType];
			textureMergeRect.advancedBlending = false;
			textureMergeRect.transparentPrefill = false;
			textureMergeRect.transparentPrefillColor = Color.clear;
			UMAMaterial sourceMaterial = source.umaMaterial != null ? source.umaMaterial : source.slotData?.material;
			if (sourceMaterial?.channels == null || textureType >= sourceMaterial.channels.Length)
			{
				return;
			}
            // JRRM debug
            textureMergeRect.textureChannel = textureType;
            textureMergeRect.channelType = sourceMaterial.channels[textureType].channelType;
			ConfigureTransparentPrefill(
				ref textureMergeRect,
				source,
				i2 + 1,
				textureType);


            if (source.AdditionalOverlays[i2].overlayType == OverlayDataAsset.OverlayType.Normal)
			{
                OverlayData od = source.overlayData[i2 + 1];

                switch (sourceMaterial.channels[textureType].channelType)
				{
                    case UMAMaterial.ChannelType.NormalMap:
						textureMergeRect.mat.shader = GetBlendModeShader(NormalBlendModeShaders,od, textureType, out textureMergeRect.advancedBlending);
						break;
					case UMAMaterial.ChannelType.Texture:
						textureMergeRect.mat.shader = GetBlendModeShader(DataBlendModeShaders, od, textureType, out textureMergeRect.advancedBlending);
						break;
					case UMAMaterial.ChannelType.DiffuseTexture:
						textureMergeRect.mat.shader = GetBlendModeDiffuseShader(od, textureType, out textureMergeRect.advancedBlending);
						break;
					case UMAMaterial.ChannelType.DetailNormalMap:
						textureMergeRect.mat.shader = detailNormalShader;
						break;
				}
				textureMergeRect.mat.SetTexture("_MainTex", source.AdditionalOverlays[i2].textureList[textureType]);
				textureMergeRect.mat.SetTexture("_ExtraTex", source.AdditionalOverlays[i2].alphaTexture);
				textureMergeRect.mat.SetColor("_Color", source.GetMultiplier(i2 + 1, textureType));
				textureMergeRect.mat.SetColor("_AdditiveColor", source.GetAdditive(i2 + 1, textureType));
			}
			else
			{
				textureMergeRect.mat.shader = cutoutShader;
			}
		}

		private static void ConfigureTransparentPrefill(
			ref TextureMergeRect textureMergeRect,
			UMAData.MaterialFragment source,
			int overlayIndex,
			int textureChannel)
		{
			UMAMaterial.ChannelType channelType = textureMergeRect.channelType;
			if (channelType != UMAMaterial.ChannelType.Texture &&
				channelType != UMAMaterial.ChannelType.DiffuseTexture)
			{
				return;
			}

			if (source.overlayData == null ||
				overlayIndex < 0 ||
				overlayIndex >= source.overlayData.Length)
			{
				return;
			}

			OverlayData overlay = source.overlayData[overlayIndex];
			if (overlay == null ||
				overlay.TransparentMultiplier == Color.clear ||
				overlay.TransparentMultiplier.a <= 0f)
			{
				return;
			}

			Color prefillColor =
				source.GetMultiplier(overlayIndex, textureChannel) *
				overlay.TransparentMultiplier;
			prefillColor.a = 0f;
			textureMergeRect.transparentPrefillColor = prefillColor;
			textureMergeRect.transparentPrefill = true;
		}
	}
}
