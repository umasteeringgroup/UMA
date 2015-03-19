using UnityEngine;
using System.Collections;


namespace UMA
{
	public static class UMAUtils
	{
		public static int StringToHash(string name) { return Animator.StringToHash(name); }

		static public float GaussianRandom(float mean, float dev)
		{
			float u1 = Random.value;
			float u2 = Random.value;
			
			float rand_std_normal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
			
			return mean + dev * rand_std_normal;
		}
		
	}
}
