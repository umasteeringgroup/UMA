using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UMA
{

    public class FPSCounter : MonoBehaviour
    {
        public Text Text;

        private Dictionary<int, string> CachedNumberStrings = new();
        private int[] _frameRateSamples;
        private int _cacheNumbersAmount = 1000;
        private int _averageFromAmount = 30;
        private int _averageCounter = 0;
        private int _currentAveraged;
        public float updateRate = 0.5f;
        public float updateTime = 0.0f;

        void Awake()
        {
            // Cache strings and create array
            {
                for (int i = 0; i < _cacheNumbersAmount; i++)
                {
                    CachedNumberStrings[i] = i.ToString();
                }
                _frameRateSamples = new int[_averageFromAmount];
            }
        }

        private void OnGUI()
        {
            if (Text == null)
            {
                Vector2 TopLeft = new Vector2(Screen.width - 100, 10);
                GUI.Label(new Rect(TopLeft.x, TopLeft.y, 100, 20), _currentAveraged.ToString());
            }
        }
        void Update()
        {
            // Sample
            {
                var currentFrame = (int)Math.Round(1f / Time.smoothDeltaTime); // If your game modifies Time.timeScale, use unscaledDeltaTime and smooth manually (or not).
                _frameRateSamples[_averageCounter] = currentFrame;
            }


            // Average
            {
                var average = 0f;

                foreach (var frameRate in _frameRateSamples)
                {
                    average += frameRate;
                }

                _currentAveraged = (int)Math.Round(average / _averageFromAmount);
                _averageCounter = (_averageCounter + 1) % _averageFromAmount;
            }


            updateTime -= Time.unscaledDeltaTime;
            if (updateTime <= 0.0f)
            // Assign to UI
            {
                if (Text != null)
                {
                    Text.text = _currentAveraged switch
                    {
                        var x when x >= 0 && x < _cacheNumbersAmount => CachedNumberStrings[x],
                        var x when x >= _cacheNumbersAmount => $"> {_cacheNumbersAmount}",
                        var x when x < 0 => "< 0",
                        _ => "?"
                    };
                }
                updateTime = updateRate;
            }
        }
    }
}