using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[CreateAssetMenu(menuName ="UMA/Piece")]
	public class UMAPiece : ScriptableObject
	{
		public BasePieceProperty[] Properties = new BasePieceProperty[0];
		public UMAPieceBlock[] Blocks = new UMAPieceBlock[0];
		public UMALocation Location;
	}
}
