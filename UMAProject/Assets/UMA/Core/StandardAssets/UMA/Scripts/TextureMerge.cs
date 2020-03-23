using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Utility class that sets up materials for atlasing.
	/// </summary>
	[ExecuteInEditMode]
    [CreateAssetMenu(menuName = "UMA/Rendering/TextureMerge")]
    public class TextureMerge : ScriptableObject
	{
		public Material material;
		public Shader normalShader; 
		public Shader diffuseShader;
		public Shader dataShader;
		public Shader cutoutShader;
		public Shader detailNormalShader;

		[System.NonSerialized] 
		public Color camBackgroundColor = new Color(0, 0, 0, 0);

		public List<UMAPostProcess> diffusePostProcesses = new List<UMAPostProcess>();
		public List<UMAPostProcess> normalPostProcesses = new List<UMAPostProcess>();
		public List<UMAPostProcess> dataPostProcesses = new List<UMAPostProcess>();
		public List<UMAPostProcess> detailNormalPostProcesses = new List<UMAPostProcess>();

		private int textureMergeRectCount;
		private TextureMergeRect[] textureMergeRects;

		//[System.Serializable] //why was this serializable? the array was public serialized too
		public struct TextureMergeRect
		{
			public Material mat;
			public Texture tex;
			public Rect rect;
		}

		public void RefreshMaterials()
		{
			if (textureMergeRects != null)
			{
				for (int i = 0; i < textureMergeRects.Length; i++)
				{
					if (textureMergeRects[i].mat == null)
						textureMergeRects[i].mat = new Material(material);
				}
			}
		}

			public void DrawAllRects(RenderTexture target, int width, int height, Color background = default(Color))
		{
			if (textureMergeRects != null)
			{
				RenderTexture backup = RenderTexture.active;
				RenderTexture.active = target;
				GL.Clear(true, true, background);
				GL.PushMatrix();
				//the matrix needs to be in the original atlas dimensions because the textureMergeRects are in that space.
				GL.LoadPixelMatrix(0, width, height, 0);

				for (int i = 0; i < textureMergeRectCount; i++)
				{
					DrawRect(ref textureMergeRects[i]);
				}

				GL.PopMatrix();
				RenderTexture.active = backup;
			}
		}

		private void DrawRect(ref TextureMergeRect textureMergeRect)
		{
			//TODO JRRM: add an event here and let the end user modify the textureMertRect.tex if needed.
			Graphics.DrawTexture(textureMergeRect.rect, textureMergeRect.tex, textureMergeRect.mat);
		}

		public void PostProcess(RenderTexture destination, UMAMaterial.ChannelType channelType)
		{
			var source = RenderTexture.GetTemporary(destination.width, destination.height, 0, destination.format, RenderTextureReadWrite.Linear);

			switch (channelType)
			{
				case UMAMaterial.ChannelType.NormalMap:
					foreach (UMAPostProcess postProcess in normalPostProcesses)
					{
						Graphics.Blit(destination, source);
						postProcess.Process(source, destination);
					}
					break;
				case UMAMaterial.ChannelType.Texture:
					foreach (UMAPostProcess postProcess in dataPostProcesses)
					{
						Graphics.Blit(destination, source);
						postProcess.Process(source, destination);
					}
					break;
				case UMAMaterial.ChannelType.DiffuseTexture:
					foreach (UMAPostProcess postProcess in diffusePostProcesses)
					{
						Graphics.Blit(destination, source);
						postProcess.Process(source, destination);
					}
					break;
				case UMAMaterial.ChannelType.DetailNormalMap:
					foreach (UMAPostProcess postProcess in detailNormalPostProcesses)
					{
						Graphics.Blit(destination, source);
						postProcess.Process(source, destination);
					}
					break;
			}
			RenderTexture.ReleaseTemporary(source);
		}

		public void Reset()
		{
			textureMergeRectCount = 0;
		}

		internal void EnsureCapacity(int moduleCount)
		{
			if (textureMergeRects != null && textureMergeRects.Length > moduleCount)
				return;

			var oldTextureMerge = textureMergeRects;
			var newLength = 100;
			while (newLength < moduleCount) newLength *= 2;

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
			}
		}

		private void SetupMaterial(ref TextureMergeRect textureMergeRect, UMAData.MaterialFragment source, int textureType)
		{
			camBackgroundColor = source.GetMultiplier(0, textureType);
			camBackgroundColor.a = 0.0f;

			textureMergeRect.tex = source.baseOverlay.textureList[textureType];

			switch (source.slotData.asset.material.channels[textureType].channelType)
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
			textureMergeRects[textureMergeRectCount].rect = source.atlasRegion;
			textureMergeRects[textureMergeRectCount].rect.y = height - textureMergeRects[textureMergeRectCount].rect.y - textureMergeRects[textureMergeRectCount].rect.height;
			atlasRect = textureMergeRects[textureMergeRectCount].rect;
			SetupMaterial(ref textureMergeRects[textureMergeRectCount], source, textureType);
			textureMergeRectCount++;
		}

		Rect atlasRect;
		float resolutionScale;
		int height;
		public void SetupModule(UMAData.GeneratedMaterial atlas, int idx, int textureType)
		{
			var atlasElement = atlas.materialFragments[idx];
			if (atlasElement.isRectShared) return;

			height = Mathf.FloorToInt(atlas.cropResolution.y);
			SetupModule(atlasElement, textureType);
			resolutionScale = atlas.resolutionScale * atlasElement.slotData.overlayScale;

			for (int i2 = 0; i2 < atlasElement.overlays.Length; i2++)
			{
				SetupOverlay(atlasElement, i2, textureType);
			}
		}

		private void SetupOverlay(UMAData.MaterialFragment source, int i2, int textureType)
		{
			if (source.overlays[i2] == null) return;
			if (source.overlays[i2].textureList[textureType] == null) return;

			Rect overlayRect;

            if (source.rects[i2].width != 0)
			{
				overlayRect = new Rect(atlasRect.xMin + source.rects[i2].x * resolutionScale, atlasRect.yMax - source.rects[i2].y * resolutionScale - source.rects[i2].height * resolutionScale, source.rects[i2].width * resolutionScale, source.rects[i2].height * resolutionScale);
            }
            else
            {
				overlayRect = atlasRect;
            }

			SetupMaterial(ref textureMergeRects[textureMergeRectCount], source, i2, ref overlayRect, textureType);
			textureMergeRectCount++;
		}

		private void SetupMaterial(ref TextureMergeRect textureMergeRect, UMAData.MaterialFragment source, int i2, ref Rect overlayRect, int textureType)
		{
			textureMergeRect.rect = overlayRect;
			textureMergeRect.tex = source.overlays[i2].textureList[textureType];

			if (source.overlays[i2].overlayType == OverlayDataAsset.OverlayType.Normal)
			{
				switch (source.slotData.asset.material.channels[textureType].channelType)
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
