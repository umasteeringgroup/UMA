using System;
using UnityEngine;

namespace UMA
{
	[Serializable]
	public class TextureProperty : BaseProperty
	{
		public Texture value;
		public override void SetValue(BaseProperty source)
		{
			value = (source as TextureProperty).value;
		}
	}
}