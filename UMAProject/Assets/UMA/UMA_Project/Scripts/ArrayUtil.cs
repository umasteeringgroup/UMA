using System.Collections.Generic;

namespace UMA.Common
{
    public static class ArrayUtil
    {
        public static void MoveElementUpAt<T>(this T[] array, int i)
        {
            if (i + 1 < array.Length)
            {
                var orig = array[i + 1];
                array[i + 1] = array[i];
                array[i] = orig;
            }
        }

        public static void MoveElementDownAt<T>(this T[] array, int i)
        {
            if (i > 0)
            {
                var orig = array[i - 1];
                array[i - 1] = array[i];
                array[i] = orig;
            }
        }

        public static IEnumerable<T> AllExcept<T>(this T[] array, int index)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (i != index)
                {
                    yield return array[i];
                }
            }
        }

        public static T GetRandom<T>(this T[] array)
        {
            if (array == null)
            {
                return default(T);
            }

            int length = array.Length;
            if (length == 0)
            {
                return default(T);
            }

            return array[RandomUtil.Range(0, length)];
        }

        public static int GetRandomIndex<T>(this T[] array)
        {
            if (array == null)
            {
                return 0;
            }

            int length = array.Length;
            if (length == 0)
            {
                return 0;
            }

            return RandomUtil.Range(0, length);
        }
    }
}
