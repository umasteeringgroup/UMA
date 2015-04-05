using UnityEngine;
using System.Collections;


namespace UMA
{
	/// <summary>
	/// UMA utility class with various static methods.
	/// </summary>
	public static class UMAUtils
	{
		/// <summary>
		/// Hash value for a string.
		/// </summary>
		/// <returns>Hash value.</returns>
		/// <param name="name">String to hash.</param>
		public static int StringToHash(string name) { return Animator.StringToHash(name); }

		/// <summary>
		/// Gaussian random value.
		/// </summary>
		/// <returns>Random value centered on mean.</returns>
		/// <param name="mean">Mean.</param>
		/// <param name="dev">Deviation.</param>
		static public float GaussianRandom(float mean, float dev)
		{
			float u1 = Random.value;
			float u2 = Random.value;
			
			float rand_std_normal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
			
			return mean + dev * rand_std_normal;
		}
		
	}
}
