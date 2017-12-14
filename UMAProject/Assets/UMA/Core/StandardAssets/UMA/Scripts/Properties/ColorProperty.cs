using System;
using UnityEngine;

namespace UMA
{
	[Serializable]
	public class ColorProperty : BaseProperty
	{
		public Color color;
		public override void SetValue(BaseProperty source)
		{
			color = (source as ColorProperty).color;
		}
	}
}