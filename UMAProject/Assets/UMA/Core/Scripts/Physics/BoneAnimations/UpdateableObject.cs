using UMA;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace UMA
{
    [System.Serializable]
    public class UpdateableObject : ScriptableObject
    {
        protected bool initialized = false;
        protected UMAData umaData;

        public virtual void Initialize(UMAData umaData)
        {
            this.umaData = umaData;
        }

        public virtual void DoUpdate(UMAData umaData, float step)
        {
            // Default implementation does nothing.
        }

        public void FixedUpdate()
        {
            // This method can be overridden in derived classes if needed.
            // It is called every fixed frame-rate frame.
            if (umaData != null && initialized)
            {
                DoUpdate(umaData, Time.fixedDeltaTime);
            }
            else
            {
                Debug.LogWarning("BaseBoneAnimator not initialized or UMAData is null. Please call Initialize() before using DoUpdate().");
            }
        }
    }
}