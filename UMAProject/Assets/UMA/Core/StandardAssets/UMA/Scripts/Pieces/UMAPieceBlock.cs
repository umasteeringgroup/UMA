using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[Serializable]
	public class UMAPieceBlock
	{
		public BaseCondition Condition;
		public UMAPieceSlot[] Slots = new UMAPieceSlot[0];
		public UMAPieceOverlay[] Overlays = new UMAPieceOverlay[0];
		public UMALocation[] SuppressLocations = new UMALocation[0];
	}
}