using System;
using System.Collections.Generic;

namespace UnityEngine.UI
{
    public static class MyListPool<T>
    {
        private static readonly MyObjectPool<List<T>> s_ListPool = new MyObjectPool<List<T>>(null, delegate (List<T> l)
        {
            l.Clear();
        });

        public static List<T> Get()
        {
            return MyListPool<T>.s_ListPool.Get();
        }

        public static void Release(List<T> toRelease)
        {
            MyListPool<T>.s_ListPool.Release(toRelease);
        }
    }
}
