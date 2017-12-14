using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[CreateAssetMenu(menuName ="UMA/Piece")]
	public class UMAPiece : UMAPropertyAsset
	{
		public UMAPieceBlock[] Blocks = new UMAPieceBlock[0];
		public UMALocation Location;
	}
}
