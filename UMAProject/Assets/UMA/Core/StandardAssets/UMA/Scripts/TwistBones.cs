// Assets/UMA/Core/StandardAssets/UMA/Scripts/TwistBones.cs
using UnityEngine;

namespace UMA
{
    public class TwistBones : MonoBehaviour
    {
        public enum Axis { X, Y, Z }

        [Range(0f, 1f)] public float twistValue = 1f;
        public Axis twistAxis = Axis.X;

        public Transform[] twistBone;
        public Transform[] refBone;

        private Quaternion[] _twistOriginal;   // original twist bone local rotations
        private Vector3[] _refDirInitial;      // initial reference directions (local)
        private Vector3 _axisVector;           // unit axis in local space
        private int _count;

        void Awake() => Initialize();

#if UNITY_EDITOR
        void OnValidate()
        {
            // keep editor changes responsive
            if (isActiveAndEnabled) Initialize();
        }
#endif

        public void Initialize()
        {
            _axisVector = twistAxis switch
            {
                Axis.X => Vector3.right,
                Axis.Y => Vector3.up,
                _ => Vector3.forward
            };

            _count = (twistBone != null && refBone != null)
                ? Mathf.Min(twistBone.Length, refBone.Length)
                : 0;

            if (_count == 0) return;

            if (_twistOriginal == null || _twistOriginal.Length != _count)
                _twistOriginal = new Quaternion[_count];
            if (_refDirInitial == null || _refDirInitial.Length != _count)
                _refDirInitial = new Vector3[_count];

            for (int i = 0; i < _count; i++)
            {
                var tb = twistBone[i];
                var rb = refBone[i];
                if (!tb || !rb) continue;

                _twistOriginal[i] = tb.localRotation;
                _refDirInitial[i] = rb.localRotation * Vector3.up; // local up at init
            }
        }

        void LateUpdate()
        {
            if (_count == 0) return;

            for (int i = 0; i < _count; i++)
            {
                var tb = twistBone[i];
                var rb = refBone[i];
                if (!tb || !rb) continue;

                // current local up of reference bone
                Vector3 refDirNow = rb.localRotation * Vector3.up;

                // signed twist around selected axis (local-space)
                float angle = Vector3.SignedAngle(_refDirInitial[i], refDirNow, _axisVector);

                // apply partial twist on top of original rotation
                var twist = Quaternion.AngleAxis(angle * Mathf.Clamp01(twistValue), _axisVector);
                tb.localRotation = twist * _twistOriginal[i];
            }
        }
    }
}