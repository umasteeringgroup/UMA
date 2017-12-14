using System;
using UnityEngine;

namespace UMA
{
	[Serializable]
	public class ColorOverrideProperty : ColorProperty
	{
		public bool overrideR;
		public bool overrideG;
		public bool overrideB;
		public bool overrideA;
		public override void SetValue(BaseProperty source)
		{
			SetColor(ref (source as ColorProperty).color);
		}
		
		public void SetColor(ref Color value)
		{
			if (!overrideR) color.r = value.r;
			if (!overrideG) color.g = value.g;
			if (!overrideB) color.b = value.b;
			if (!overrideA) color.a = value.a;
		}
	}
}