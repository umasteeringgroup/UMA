using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class TextureMerge : MonoBehaviour
	{
		public Camera myCamera;
		public Material material;
		public int textureMergeRectCount;

		public TextureMergeRect[] textureMergeRects;
		[System.Serializable]
		public struct TextureMergeRect
		{
			public Material mat;
			public Texture tex;
			public Rect rect;
		}

		void OnRenderObject()
		{
			if (textureMergeRects != null)
			{
				for (int i = 0; i < textureMergeRectCount; i++)
				{
					DrawRect(ref textureMergeRects[i]);
				}
			}
		}

		private void DrawRect(ref TextureMergeRect textureMergeRect)
		{
			Graphics.DrawTexture(textureMergeRect.rect, textureMergeRect.tex, textureMergeRect.mat);
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

		private void SetupMaterial(ref TextureMergeRect textureMergeRect, UMAData.MaterialDefinition source, int textureType)
		{
			textureMergeRect.tex = source.baseTexture[textureType];
			textureMergeRect.mat.SetTexture("_MainTex", source.baseTexture[textureType]);
			textureMergeRect.mat.SetTexture("_ExtraTex", source.baseTexture[0]);
			textureMergeRect.mat.SetColor("_Color", source.GetMultiplier(0, textureType));
			textureMergeRect.mat.SetColor("_AdditiveColor", source.GetAdditive(0, textureType));
		}

		public void SetupModule(UMAData.AtlasMaterialDefinition atlasElement, int textureType)
		{
			textureMergeRects[textureMergeRectCount].rect = atlasElement.atlasRegion;
			textureMergeRects[textureMergeRectCount].rect.y = height - textureMergeRects[textureMergeRectCount].rect.y - textureMergeRects[textureMergeRectCount].rect.height;
			atlasOffset = textureMergeRects[textureMergeRectCount].rect.min;
			atlasRect = textureMergeRects[textureMergeRectCount].rect;
			SetupMaterial(ref textureMergeRects[textureMergeRectCount], atlasElement.source, textureType);
			textureMergeRectCount++;
		}

		Vector2 atlasOffset;
		Rect atlasRect;
		float resolutionScale;
		int height;
		public void SetupModule(UMAData.AtlasElement atlas, int idx, int textureType)
		{
            var atlasElement = atlas.atlasMaterialDefinitions[idx];
            if (atlasElement.isRectShared) return;

			int width = Mathf.FloorToInt(atlas.cropResolution.x);
			height = Mathf.FloorToInt(atlas.cropResolution.y);
			SetupModule(atlasElement, textureType);
			resolutionScale = atlas.resolutionScale * atlasElement.source.slotData.overlayScale;

			for (int i2 = 0; i2 < atlasElement.source.overlays.Length; i2++)
			{
				SetupOverlay(atlasElement.source, i2, textureType);
			}
		}

		private void SetupOverlay(UMAData.MaterialDefinition source, int i2, int textureType)
		{
			if (source.overlays[i2] == null) return;

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

		private void SetupMaterial(ref TextureMergeRect textureMergeRect, UMAData.MaterialDefinition source, int i2, ref Rect overlayRect, int textureType)
		{
			textureMergeRect.rect = overlayRect;
			textureMergeRect.tex = source.overlays[i2].textureList[textureType];
			textureMergeRect.mat.SetTexture("_MainTex", source.overlays[i2].textureList[textureType]);
			textureMergeRect.mat.SetTexture("_ExtraTex", source.overlays[i2].textureList[0]);
			textureMergeRect.mat.SetColor("_Color", source.GetMultiplier(i2 + 1, textureType));
			textureMergeRect.mat.SetColor("_AdditiveColor", source.GetAdditive(i2 + 1, textureType));
		}
	}
}
