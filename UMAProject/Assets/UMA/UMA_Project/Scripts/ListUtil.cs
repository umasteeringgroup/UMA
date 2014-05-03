#region
using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace UMA.Common
{
    public static class ListUtil
    {
        public static void MoveElementUpAt<T>(this List<T> list, int i)
        {
            if (i + 1 < list.Count)
            {
                var orig = list[i + 1];
                list[i + 1] = list[i];
                list[i] = orig;
            }
        }

        public static void MoveElementDownAt<T>(this List<T> list, int i)
        {
            if (i > 0)
            {
                var orig = list[i - 1];
                list[i - 1] = list[i];
                list[i] = orig;
            }
        }

        public static List <T> GetList <T> (List <T>[] arrayOfLists, int index)
        {
            if (index < 0 || index > arrayOfLists.Length)
            {
                throw new IndexOutOfRangeException ("index");
            }

            List <T> list = arrayOfLists [index];
            if (list == null)
            {
                list = new List <T> ();
                arrayOfLists [index] = list;
            }

            return list;
        }

        public static void InsertSorted <T> (this List <T> list, T item, IComparer <T> comparer)
        {
            if (list.Count == 0)
            {
                list.Add (item);
            }
            else
            {
                int insertionPoint = list.BinarySearch (item, comparer);

                if (insertionPoint < 0)
                {
                    list.Insert (~insertionPoint, item);
                }
                else
                {
                    list.Insert (insertionPoint, item);
                }
            }
        }

        public static void InsertSorted <TSource, TKey> (
            this List <TSource> list, TSource item, Func <TSource, TKey> func)
        {
            list.InsertSorted (item, new ComparerSelect <TSource, TKey> (func));
        }

        public static void ExpandSetAt <T> (this List <T> list, int index, T item)
        {
            if (index >= list.Count)
            {
                list.AddRange (new T[index - list.Count + 1]);
            }

            list [index] = item;
        }

        public static void ExpandSize <T> (this List <T> list, int newSize)
        {
            if (newSize >= list.Count)
            {
                list.AddRange (Enumerable.Repeat (default(T), newSize - list.Count));
            }
        }

        public static bool Exists <T> (this List <T> list, int index)
        {
            if (list == null)
            {
                return false;
            }

            if (index >= list.Count)
            {
                return false;
            }
            // ReSharper disable CompareNonConstrainedGenericWithNull
            return list [index] != null;
            // ReSharper restore CompareNonConstrainedGenericWithNull
        }

        public static bool AddDistinct <T> (this List <T> list, T item)
        {
            if (!list.Contains (item))
            {
                list.Add (item);
                return true;
            }
            return false;
        }

        public static bool RemoveIfContains <T> (this List <T> list, T item)
        {
            if (list.Contains (item))
            {
                list.Remove (item);
                return true;
            }
            return false;
        }

        public static T GetRandom <T> (this List <T> list)
        {
            if (list == null)
            {
                return default(T);
            }

            int count = list.Count;
            if (count == 0)
            {
                return default(T);
            }

            return list[RandomUtil.Range(0, count)];
        }

        public class ComparerSelect <TSource, TKey> : IComparer <TSource>
        {
            private readonly Func <TSource, TKey> _mFunc;

            public ComparerSelect (Func <TSource, TKey> func)
            {
                _mFunc = func;
            }

            public int Compare (TSource x, TSource y)
            {
                return Comparer <TKey>.Default.Compare (_mFunc (x), _mFunc (y));
            }
        }

        static Random rng = new Random(); 
        public static void FastShuffle<T>(this IList<T> list)
        {
            int n = list.Count;  
            while (n > 1) {  
                n--;  
                int k = rng.Next(n + 1);  
                T value = list[k];  
                list[k] = list[n];  
                list[n] = value;  
            } 
        }
    }
}
