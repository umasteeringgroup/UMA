using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	[Serializable]
	public struct PropertyMapping
	{
		public BasePieceProperty Source;
		public BasePieceProperty Dest;
	}
}