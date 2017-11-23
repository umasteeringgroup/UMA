using UnityEngine;
using System.Collections;

namespace UMA
{
	[System.Serializable]
	public partial class UMADnaTutorial : UMADna
	{
		public float eyeSpacing = 0.5f;

		// HACK
		public override int GetIndex(string name)
		{
			return System.Array.IndexOf(Names, name);;
		}
		public override void SetValue(string name, float value)
		{
			int index = GetIndex(name);
			if (index >= 0)
			{
				SetValue(index, value);
			}
		}
		public override float GetValue(string name)
		{
			int index = GetIndex(name);
			if (index >= 0)
			{
				return GetValue(index);
			}

			return 0.5f;
		}
	}
}