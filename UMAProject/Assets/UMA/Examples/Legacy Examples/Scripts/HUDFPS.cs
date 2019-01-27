//	============================================================
//	Name:		HUDFPS
//	Author: 	Aras Pranckevicius (NeARAZ)
//	============================================================
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace UMA.Examples
{
    /// <summary>
    /// Attach this to a UI Text to make a frames/second indicator.
    /// </summary>
    /// <remarks>
    /// It calculates frames/second over each updateInterval,
    /// so the display does not keep changing wildly.
    ///
    /// It is also fairly accurate at very low FPS counts ( less than 10).
    /// We do this not by simply counting frames per interval, but
    /// by accumulating FPS for each frame. This way we end up with
    /// correct overall FPS even if the interval renders something like
    /// 5.5 frames.
    /// 
    /// Modified to properly support Unity 5 where guiText property has been removed.
    /// </remarks>
    public class HUDFPS : MonoBehaviour
    {

        public float updateInterval = 0.5F;
        public Text fpsTextOutput;
        private float accum = 0;  // FPS accumulated over the interval
        private int frames = 0;  // Frames drawn over the interval
        private float timeleft;     // Left time for current interval

        void Start()
        {
            if (!fpsTextOutput)
            {
                Debug.LogWarning("UtilityFramesPerSecond needs a Text component for output!");
                enabled = false;
                return;
            }
            timeleft = updateInterval;
            fpsTextOutput.material = new Material(fpsTextOutput.material);
        }

        void Update()
        {
            timeleft -= Time.deltaTime;
            accum += Time.timeScale / Time.deltaTime;
            ++frames;

            // Interval ended - update GUI text and start new interval
            if (timeleft <= 0.0)
            {
                // display two fractional digits (f2 format)
                float fps = accum / frames;
                string format = System.String.Format("{0:F2} FPS", fps);
                fpsTextOutput.text = format;

                if (fps < 30)
                    fpsTextOutput.material.color = Color.yellow;
                else
                if (fps < 10)
                    fpsTextOutput.material.color = Color.red;
                else
                    fpsTextOutput.material.color = Color.green;
                //	DebugConsole.Log(format,level);
                timeleft = updateInterval;
                accum = 0.0F;
                frames = 0;
            }
        }
    }
}