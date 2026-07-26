using UnityEngine;
using System.Diagnostics;

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void StaticInitializeOnLoad()
        {
			frame = -10;
			frameTime = 0f;
			deltaTime = 0f;
        }

        /// <summary>
        /// Report Time Spendt This Frame
        /// </summary>
        /// <param name="ticks">Ticks measured by <see cref="Stopwatch"/>.</param>
        public static void ReportTimeSpendtThisFrameTicks(long ticks)
		{
			ReportTimeSpendtThisFrame((float)(ticks / (double)Stopwatch.Frequency));
		}

		/// <summary>
		/// Converts ticks measured by <see cref="Stopwatch"/> to milliseconds.
		/// Stopwatch frequency is platform dependent and must not be treated as
		/// TimeSpan ticks.
		/// </summary>
		public static double StopwatchTicksToMilliseconds(long ticks)
		{
			return ticks * 1000d / Stopwatch.Frequency;
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
