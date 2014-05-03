using System;
using System.Collections.Generic;
using System.Threading;

namespace UMA.Common
{
    public static class RandomUtil
    {
        private static readonly Dictionary<int, Random> _randomDictionary = new Dictionary<int, Random>();
        public static Random ThreadSafeRandom
        {
            get
            {
                int currentThreadId = Thread.CurrentThread.ManagedThreadId;

                if (!_randomDictionary.ContainsKey(currentThreadId))
                {
                    var newRandom = new Random(Environment.TickCount);
                    _randomDictionary.Add(currentThreadId, newRandom);
                    return newRandom;
                }

                return _randomDictionary[currentThreadId];
            }
        }

        private static Random _seededRandom;
        public static Random ThreadStaticRandom
        {
            get
            {
                if (_seededRandom == null)
                    _seededRandom = new Random(1234324325);
                return _seededRandom;
            }
        }

        public static int StaticRange(int min, int max)
        {
            return ThreadStaticRandom.Next(min, max);
        }

        public static float StaticRange(float min, float max)
        {
            return StaticValue / (max - min) + min;
        }

        public static float StaticValue
        {
            get
            {
                return (float)ThreadStaticRandom.NextDouble();
            }
        }
        
        public static int Range(int min, int max)
        {
            return ThreadSafeRandom.Next(min, max);
        }

        public static float Range(float min, float max)
        {
            return value / (max - min) + min;
        }

// ReSharper disable InconsistentNaming
        // Mimicing Unity's naming style.
        public static float value
        {
            get
            {
                return (float)ThreadSafeRandom.NextDouble();
            }
        }
// ReSharper restore InconsistentNaming

        public static float Range(float max)
        {
            return (float)(ThreadSafeRandom.NextDouble() * max);
        }

        public static int Range(int max)
        {
            return ThreadSafeRandom.Next(max);
        }
    }
}
