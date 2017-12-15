using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[Serializable]
	public class UMAPieceOverlay
	{
		public OverlayDataAsset Overlay;
		public OverlayOperation Operation;
		public PropertyMapping[] MappedProperties;

		public enum OverlayOperation
		{
			Add,
			Remove
		}
	}
}
