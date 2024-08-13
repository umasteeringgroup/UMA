using System;
using System.Collections.Generic;
using UnityEngine.Events;

// @cond doxygen ignore
namespace UnityEngine.UI
{
    public class MyObjectPool<T> where T : new()
    {
        private readonly Stack<T> m_Stack = new Stack<T>();

        private readonly UnityAction<T> m_ActionOnGet;

        private readonly UnityAction<T> m_ActionOnRelease;

        public int countAll
        {
            get;
            private set;
        }

        public int countActive
        {
            get
            {
                return this.countAll - this.countInactive;
            }
        }

        public int countInactive
        {
            get
            {
                return this.m_Stack.Count;
            }
        }

        public MyObjectPool(UnityAction<T> actionOnGet, UnityAction<T> actionOnRelease)
        {
            this.m_ActionOnGet = actionOnGet;
            this.m_ActionOnRelease = actionOnRelease;
        }

        public T Get()
        {
            T t;
            if (this.m_Stack.Count == 0)
            {
                t = ((default(T) == null) ? Activator.CreateInstance<T>() : default(T));
                this.countAll++;
            }
            else
            {
                t = this.m_Stack.Pop();
            }
            if (this.m_ActionOnGet != null)
            {
                this.m_ActionOnGet(t);
            }
            return t;
        }

        public void Release(T element)
        {
            if (this.m_Stack.Count > 0 && object.ReferenceEquals(this.m_Stack.Peek(), element))
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogError("Internal error. Trying to destroy object that is already released to pool.");
                }
            }
            if (this.m_ActionOnRelease != null)
            {
                this.m_ActionOnRelease(element);
            }
            this.m_Stack.Push(element);
        }
    }
}
// @endcond
