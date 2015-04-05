using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// UMA time utilities.
	/// </summary>
	public static class UMATime
	{
		private static int frame = -10;
		private static float frameTime;
		public static float deltaTime;
		/// <summary>
		/// Report Time Spendt This Frame
		/// </summary>
		/// <param name="ticks">10,000,000 ticks is 1 second (1/10,000ms)</param>
		public static void ReportTimeSpendtThisFrameTicks(long ticks)
		{
			ReportTimeSpendtThisFrame(ticks / 10000000f);
		}

		/// <summary>
		/// Report Time Spendt This Frame
		/// </summary>
		/// <param name="seconds">floating point value 1.0f = 1 second</param>
		public static void ReportTimeSpendtThisFrame(float seconds)
		{
			int currentFrame = Time.frameCount;
			if (frame != currentFrame)
			{
				frame++;
				deltaTime = Time.deltaTime + seconds;
				if (frame == currentFrame)
				{
					deltaTime -= frameTime;
				}
				frame = Time.frameCount;
				frameTime = seconds;
			}
			else
			{
				frameTime += seconds;
				deltaTime += seconds;
			}
		}

		/// <summary>
		/// Report Time Spendt This Frame
		/// </summary>
		/// <param name="ms">1000 ms equals 1 second</param>
		public static void ReportTimeSpendtThisFrameMS(int ms)
		{
			ReportTimeSpendtThisFrame(ms / 1000f);
		}


	}
}