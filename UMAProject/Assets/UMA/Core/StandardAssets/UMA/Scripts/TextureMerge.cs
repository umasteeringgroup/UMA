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
		private Vector2 pivotPoint = new Vector2();

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
			public int textureType;
			public UMAMaterial.ChannelType channelType;
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

			// Disabling linear to srgb conversion is not enough. It seems that Unity always does the conversion when reading from a RenderTexture.
			// As a workaround, we will nead to create a non-linear texture to blit to, so the conversion happens, but it's OK for it to happen.
			GL.sRGBWrite = false;            
		    RenderTexture outputMap = new RenderTexture(rt.width, rt.height, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            outputMap.enableRandomWrite = true;
            outputMap.Create();
            RenderTexture.active = outputMap;
            GL.Clear(true, true, Color.black);
            Graphics.Blit(rt, outputMap);	
			// End of workaround.

			tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

            GL.sRGBWrite = bkUpSRGBWrite;
			RenderTexture.active = activeTexture;

			outputMap.Release();
			DestroyImmediate(outputMap);
			return tex;
        }

        public static void SaveRenderTexture(RenderTexture texture, string textureName, bool isNormal = false)
        {
            Texture2D tex;
            tex = GetRTPixels(texture);
            SaveTexture2D(tex, textureName);
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
				RenderTexture backup = RenderTexture.active;
				RenderTexture.active = target;
#if UMA_ADVANCED_BLENDMODES
				RenderTexture scratch = null;
#endif
				GL.Clear(true, true, background);
				GL.PushMatrix();
				//the matrix needs to be in the original atlas dimensions because the textureMergeRects are in that space.
				GL.LoadPixelMatrix(0, width, height, 0);

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

					if (tr.advancedBlending)
					{
                        // Create a temporary texture that is the size of the overlay rect in atlas space.
                        scratch = RenderTexture.GetTemporary((int)tr.rect.width, (int)tr.rect.height, 0, target.format, RenderTextureReadWrite.Linear);

						float fw = (float)width;
						float fh = (float)height;

                        GL.PushMatrix();
                        GL.LoadPixelMatrix(0, scratch.width, scratch.height, 0); 

						// Set the destination (The entire scratch texture)
                        Destination.Set(0, 0, scratch.width, scratch.height);

						// Set the source rect in UV space
						Src.Set(tr.rect.x/fw, 1.0f-((tr.rect.y+tr.rect.height)/fh), (tr.rect.width/fw), (tr.rect.height / fh));  // get the rect in UV space

                        //SaveRenderTexture(target, System.IO.Path.Combine(Application.dataPath, "target-before.png"));
                        // should be drawing to the scratch texture now
                        RenderTexture.active = scratch;
                        Graphics.DrawTexture(Destination, target, Src, 0, 0, 0, 0);
						RenderTexture.active = target;
                        //SaveRenderTexture(scratch, System.IO.Path.Combine(Application.dataPath, "scratch-after.png"));
                        //SaveRenderTexture(target, System.IO.Path.Combine(Application.dataPath, "target-after.png"));
						GL.PopMatrix();
						tr.mat.SetTexture("_BaseTex", scratch);
						DrawRect (ref tr, sharperFitTextures);
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
				GL.PopMatrix();
				RenderTexture.active = backup;
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

            if (tr.transform)
			{
				// rotate texture here?
				GL.PushMatrix();

				// rotate around the pivot
				pivotPoint.Set(tr.rect.x + (tr.rect.width / 2.0f) , tr.rect.y + (tr.rect.height / 2.0f) );
				
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

			if (tr.channelType == UMAMaterial.ChannelType.DiffuseTexture)
			{
				// Debug.Log($"Drawing = {tr.textureType} with texture {tr.mat.mainTexture.name} and shader {tr.mat.shader.name}");
			}

		
			Graphics.DrawTexture(tr.rect, tr.tex, tr.mat);		
			if (tr.transform)
			{
				GL.PopMatrix();
			}
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
			RenderTexture.active = null;
			RenderTexture.ReleaseTemporary(source);
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

		private void SetupMaterial(ref TextureMergeRect textureMergeRect, UMAData.MaterialFragment source, int textureType)
		{
			if (source.isNoTextures)
            {
                return;
            }

            camBackgroundColor = source.GetMultiplier(0, textureType);
			camBackgroundColor.a = 0.0f;

			if (textureType >= source.baseOverlay.textureList.Length)
			{
				// Debug.Log("Out of range (" + textureType + ") on base overlay: " + source.overlayData[0].overlayName + " on slot: " + source.slotData.slotName);
				return;
			}
			textureMergeRect.tex = source.baseOverlay.textureList[textureType];
            textureMergeRect.advancedBlending = false;

			// JRRM debug
			textureMergeRect.textureType = textureType;
			textureMergeRect.channelType = source.slotData.material.channels[textureType].channelType;

            switch (source.slotData.material.channels[textureType].channelType)
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

			textureMergeRect.mat.SetTexture("_MainTex", source.baseOverlay.textureList[textureType]);
			textureMergeRect.mat.SetTexture("_ExtraTex", source.baseOverlay.alphaTexture);
			textureMergeRect.mat.SetColor("_Color", source.GetMultiplier(0, textureType));
			textureMergeRect.mat.SetColor("_AdditiveColor", source.GetAdditive(0, textureType));
		}

		public void SetupModule(UMAData.MaterialFragment source, int textureType)
		{
			if (!source.isNoTextures)
			{
				textureMergeRects[textureMergeRectCount].transform = false;
				textureMergeRects[textureMergeRectCount].rect = source.atlasRegion;
				textureMergeRects[textureMergeRectCount].rect.y = height - textureMergeRects[textureMergeRectCount].rect.y - textureMergeRects[textureMergeRectCount].rect.height;
				atlasRect = textureMergeRects[textureMergeRectCount].rect;
				SetupMaterial(ref textureMergeRects[textureMergeRectCount], source, textureType);
			}
			textureMergeRectCount++;
		}

		Rect atlasRect;
		Vector2 resolutionScale;
		int height;
		public void SetupModule(UMAData.GeneratedMaterial atlas, int idx, int textureType)
		{
			var atlasElement = atlas.materialFragments[idx];
			if (atlasElement.isRectShared)
            {
                return;
            }

            height = Mathf.FloorToInt(atlas.cropResolution.y);
			SetupModule(atlasElement, textureType);
			resolutionScale = atlas.resolutionScale * atlasElement.slotData.overlayScale;

			if (atlasElement.overlays == null)
            {
                return;
            }

            for (int i2 = 0; i2 < atlasElement.overlays.Length; i2++)
			{
				SetupOverlay(atlasElement, i2, textureType);
			}
		}

		private void SetupOverlay(UMAData.MaterialFragment source, int OverlayIndex, int textureType)
		{
			if (source.overlays[OverlayIndex] == null)
            {
                return;
            }

            if (textureType >= source.overlays[OverlayIndex].textureList.Length)
            {
                return;
            }

            if (source.overlays[OverlayIndex].textureList[textureType] == null)
            {
                return;
            }

            if (source.isNoTextures)
            {
                return;
            }

            Rect overlayRect;

			if (source.rects[OverlayIndex].width != 0)
			{
				overlayRect = new Rect(atlasRect.xMin + source.rects[OverlayIndex].x * resolutionScale.x, atlasRect.yMax - source.rects[OverlayIndex].y * resolutionScale.y - source.rects[OverlayIndex].height * resolutionScale.y, source.rects[OverlayIndex].width * resolutionScale.x, source.rects[OverlayIndex].height * resolutionScale.y);
			}
			else
			{
				overlayRect = atlasRect;
			}

			SetupMaterial(ref textureMergeRects[textureMergeRectCount], source, OverlayIndex, ref overlayRect, textureType);

			// We only get here if we have more than one overlay. 
			// The overlay we are blending is the current overlay +1.
			// when there are more than one overlay, that means we are blending the overlay in. 
			if (source.overlayData[OverlayIndex + 1].instanceTransformed)
			{
				OverlayData od = source.overlayData[OverlayIndex + 1];
				textureMergeRects[textureMergeRectCount].transform = true;
				textureMergeRects[textureMergeRectCount].rotation = od.Rotation;
				textureMergeRects[textureMergeRectCount].scale = od.Scale;

                var tex = source.overlayData[0].GetTexture(0);
				if (tex != null)
				{
					float xx = od.Translate.x * tex.width;
                    float yy = od.Translate.y * tex.height;

                    textureMergeRects[textureMergeRectCount].rect = new Rect(overlayRect.x + xx, overlayRect.y + yy, overlayRect.width, overlayRect.height);
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
			textureMergeRect.tex = source.overlays[i2].textureList[textureType];
			textureMergeRect.advancedBlending = false;
            // JRRM debug
            textureMergeRect.textureType = textureType;
            textureMergeRect.channelType = source.slotData.material.channels[textureType].channelType;


            if (source.overlays[i2].overlayType == OverlayDataAsset.OverlayType.Normal)
			{
                OverlayData od = source.overlayData[i2 + 1];

                switch (source.slotData.material.channels[textureType].channelType)
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
				textureMergeRect.mat.SetTexture("_MainTex", source.overlays[i2].textureList[textureType]);
				textureMergeRect.mat.SetTexture("_ExtraTex", source.overlays[i2].alphaTexture);
				textureMergeRect.mat.SetColor("_Color", source.GetMultiplier(i2 + 1, textureType));
				textureMergeRect.mat.SetColor("_AdditiveColor", source.GetAdditive(i2 + 1, textureType));
			}
			else
			{
				textureMergeRect.mat.shader = cutoutShader;
			}
		}
	}
}
