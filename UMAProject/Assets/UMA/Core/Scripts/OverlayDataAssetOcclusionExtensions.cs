using UnityEngine;
using System;

/*
Purpose
OverlayDataAssetOcclusionExtensions is a triangle-culling optimization for UMA overlays. It precomputes which triangles in a slot's mesh are completely hidden by the overlay's cutout texture, so they can be skipped during mesh generation instead of being rendered transparent.

How it works
Input: An OverlayDataAsset with a cutout texture (first texture in textureList[0]) and a target SlotDataAsset

UpdateOcclusion reads the cutout texture's red-channel pixels and maps each vertex's UV coordinate to a pixel. Vertices mapping to pixels where R == 0 are marked as "occluded"

A triangle is occluded when all three of its vertices are occluded. These results are packed into a per-submesh int[] bitmask (one bit per triangle)

GetOcclusion retrieves the precomputed bitmask at runtime by slot name hash and submesh index

Why it exists
Without this, a cutout overlay still renders all triangles � the GPU shades transparent pixels wastefully. This extension lets the UMA combiner exclude fully-hidden triangles from the final mesh entirely, reducing draw calls and vertex shader work. It's an advanced optimization in the "Power Tools" extension package.
*/

/*Overlay cutout texture (R channel)
         �
         ?
  Per-vertex occlusion check (UV ? pixel R==0?)
         �
         ?
  Per-triangle mask (all 3 verts occluded ? hide triangle)
         �
         ?
  Stored in OverlayDataAsset.OcclusionEntries[]
         �
         ?
  Retrieved at mesh combine time via GetOcclusion()
         �
         ?
  Occluded triangles skipped in final mesh*/



namespace UMA
{
	public static class OverlayDataAssetOcclusionExtensions
	{
		public static void CleanUp()
		{
			pixels = null;
			pixelsTexture = null;
		}

public static System.Int32[] GetOcclusion(this OverlayDataAsset asset, int slotNameHash, int subMesh, int lodLevel = 0)
	{
		if (subMesh < 0)
			return null;

		var occlusionIndex = asset.GetOcclusionIndex(slotNameHash);
		if (occlusionIndex < 0)
			return null;

		var entry = asset.OcclusionEntries[occlusionIndex];
		if (entry.occlusion == null || entry.occlusion.Length <= subMesh) 
			return null;

		var subOcclusion = entry.occlusion[subMesh];
		if (subOcclusion.occlusionLODs == null || subOcclusion.occlusionLODs.Length == 0)
			return null;

		if (lodLevel >= subOcclusion.occlusionLODs.Length)
			lodLevel = 0;

		return subOcclusion.occlusionLODs[lodLevel];
		}

		public static void UpdateOcclusion(this OverlayDataAsset asset, SlotDataAsset slot)
		{
			var occlusionIndex = asset.GetOcclusionIndex(slot.nameHash);
			if (occlusionIndex < 0)
			{
				occlusionIndex = asset.OcclusionEntries == null ? 0 : asset.OcclusionEntries.Length;
				Array.Resize(ref asset.OcclusionEntries, occlusionIndex + 1);
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(asset);
#endif
				asset.OcclusionEntries[occlusionIndex] = new OverlayDataAsset.OcclusionEntry();
				asset.OcclusionEntries[occlusionIndex].slotNameHash = slot.nameHash;
				asset.SortOcclusion();
				occlusionIndex = asset.GetOcclusionIndex(slot.nameHash);
			}
			var occlusionEntry = asset.OcclusionEntries[occlusionIndex];

			var cutoutMask = asset.textureList[0];
			if (pixels == null || pixelsTexture != cutoutMask)
			{
				var temporaryRT = RenderTexture.GetTemporary(cutoutMask.width, cutoutMask.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
				Graphics.Blit(cutoutMask, temporaryRT);
				RenderTexture.active = temporaryRT;
				var workingBuffer = new Texture2D(cutoutMask.width, cutoutMask.height, TextureFormat.ARGB32, false, true);
				workingBuffer.ReadPixels(new Rect(0, 0, cutoutMask.width, cutoutMask.height), 0, 0, false);
				pixels = workingBuffer.GetPixels32();
				pixelsTexture = cutoutMask;
				RenderTexture.ReleaseTemporary(temporaryRT);
#if UNITY_EDITOR
				UnityEngine.Object.DestroyImmediate(workingBuffer, false);
#else
				UnityEngine.Object.Destroy(workingBuffer);
#endif
				stride = cutoutMask.width;
				uScale = (float)(cutoutMask.width - 1);
				vScale = (float)(cutoutMask.height - 1);
			}
			ProcessSlot(occlusionEntry, slot);
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(asset);
#endif
		}

		static Color32[] pixels;
		static Texture pixelsTexture;
		static float uScale;
		static float vScale;
		static int stride;

		static int GetOcclusionIndex(this OverlayDataAsset asset, int slotNameHash)
		{
		if (asset.OcclusionEntries == null)
			return -1;

		return Array.BinarySearch(asset.OcclusionEntries, slotNameHash, OverlayDataAsset.OcclusionEntry.OcclusionEntryComparer.Instance);
		}

	private static void ProcessSlot(OverlayDataAsset.OcclusionEntry entry, SlotDataAsset slot)
		{
			bool[] vertexCutout = new bool[slot.meshData.vertexCount];
			for (int i = 0; i < slot.meshData.vertexCount; i++)
			{
				var uv = slot.meshData.uv[i];
				var x = Mathf.RoundToInt(uScale * uv.x);
				var y = Mathf.RoundToInt(vScale * uv.y);
				vertexCutout[i] = pixels[y * stride + x].r == 0;
			}

		int subMeshCount = slot.meshData.subMeshCount;
		Array.Resize(ref entry.occlusion, subMeshCount);

		for (int sm = 0; sm < subMeshCount; sm++)
		{
			var subMesh = slot.meshData.submeshes[sm];
			int lodCount = subMesh.LODCount();

			if (lodCount == 0)
			{
				// No LOD ranges: treat as single LOD 0
				var triangles = subMesh.GetBaseTriangles();
				int occEntries = (triangles.Length / 3 + 31) / 32;
				var occlusion = new System.Int32[occEntries];
				ProcessSubMesh(triangles, vertexCutout, occlusion);
				entry.occlusion[sm].occlusionLODs = new System.Int32[][] { occlusion };
			}
			else
			{
				entry.occlusion[sm].occlusionLODs = new System.Int32[lodCount][];
				for (int lod = 0; lod < lodCount; lod++)
				{
					if (!subMesh.HasLODLevel(lod)) continue;

					var triArray = subMesh.getManagedTriangles(lod);
					int occEntries = (triArray.Length / 3 + 31) / 32;
					var occlusion = new System.Int32[occEntries];
					ProcessSubMesh(triArray, vertexCutout, occlusion);
					entry.occlusion[sm].occlusionLODs[lod] = occlusion;
				}
			}
		}
	}

	private static void ProcessSubMesh(int[] triangles, bool[] vertexCutout, System.Int32[] occlussion)
	{
		int i = 0;
		uint mask = 0;
		uint modifier = 1;
		int occlusionIndex = 0;
		while (i < triangles.Length)
		{
			var v1 = vertexCutout[triangles[i++]];
			var v2 = vertexCutout[triangles[i++]];
			var v3 = vertexCutout[triangles[i++]];
			if (!(v1 && v2 && v3))
			{
				mask += modifier;
			}
			modifier = modifier << 1;
			if (modifier == 0)
			{
				occlussion[occlusionIndex++] = (System.Int32)mask;
				mask = 0;
				modifier = 1;
			}
		}
		if (modifier != 1)
			occlussion[occlusionIndex++] = (System.Int32)mask;
	}
}
}