using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[Serializable]
	public class UMAPieceSlot
	{
		public enum SlotOperation
		{
			Add,
			Remove,
			Extend
		}
		public SlotOperation Operation = SlotOperation.Add;
		public SlotDataAsset Slot;
		public UMAPieceOverlay[] Overlays = new UMAPieceOverlay[0];
	}
}