using UnityEngine;

namespace UMA
{
    public enum UMAEffectorShape
    {
        Box,
        Sphere,
        Capsule
    }

    public enum UMAEffectorMode
    {
        ScaleAlongNormal,
        Translate
    }

    [System.Flags]
    public enum UMAEffectorAxisMask
    {
        None = 0,
        X = 1 << 0,
        Y = 1 << 1,
        Z = 1 << 2,
        All = X | Y | Z
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UMAEffector : MonoBehaviour
    {
        [SerializeField] public UMAEffectorShape shape = UMAEffectorShape.Box;
        [SerializeField] public UMAEffectorMode mode = UMAEffectorMode.ScaleAlongNormal;
        [SerializeField] public UMAEffectorAxisMask axisMask = UMAEffectorAxisMask.All;
        [SerializeField, Range(0.0001f, 0.02f)] public float amount = 0.001f;
        [SerializeField] public bool accumulate = true;
        [SerializeField] public bool simulateVertexMerging = false;

        public void NotifyParentLattice(bool refreshHierarchy = false)
        {
            UMALattice lattice = GetComponentInParent<UMALattice>();
            if (lattice == null)
                return;

            if (refreshHierarchy)
                lattice.RefreshEffectorsFromChildren();

            lattice.DeformTarget();
        }

        private void OnEnable()
        {
            NotifyParentLattice(true);
        }

        private void OnDisable()
        {
            NotifyParentLattice(true);
        }

        private void OnDestroy()
        {
            UMALattice lattice = GetComponentInParent<UMALattice>();
            if (lattice != null)
                lattice.RefreshEffectorsFromChildren();
        }

        private void OnValidate()
        {
            amount = Mathf.Clamp(amount, 0.0001f, 0.02f);
            NotifyParentLattice(false);
        }

        private void Update()
        {
            if (!enabled || !gameObject.activeInHierarchy)
                return;

            if (!transform.hasChanged)
                return;

            transform.hasChanged = false;
            NotifyParentLattice(false);
        }

        public bool TryGetWorldDelta(Vector3 worldPoint, Vector3 worldNormal, out Vector3 worldDelta)
        {
            worldDelta = Vector3.zero;

            if (!enabled || !gameObject.activeInHierarchy)
                return false;

            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            if (!ContainsLocalPoint(localPoint))
                return false;

            if (mode == UMAEffectorMode.ScaleAlongNormal)
            {
                Vector3 normal = worldNormal;
                if (normal.sqrMagnitude < 1e-8f)
                    normal = transform.up;
                else
                    normal.Normalize();

                Vector3 localNormal = transform.InverseTransformDirection(normal);
                if ((axisMask & UMAEffectorAxisMask.X) == 0)
                    localNormal.x = 0f;
                if ((axisMask & UMAEffectorAxisMask.Y) == 0)
                    localNormal.y = 0f;
                if ((axisMask & UMAEffectorAxisMask.Z) == 0)
                    localNormal.z = 0f;

                if (localNormal.sqrMagnitude < 1e-8f)
                    return false;

                worldDelta = transform.TransformDirection(localNormal).normalized * amount;
            }
            else
            {
                worldDelta = transform.up * amount;
            }

            return worldDelta.sqrMagnitude > 0f;
        }

        public bool ContainsLocalPoint(Vector3 localPoint)
        {
            switch (shape)
            {
                case UMAEffectorShape.Sphere:
                    return IsInsideSphere(localPoint);
                case UMAEffectorShape.Capsule:
                    return IsInsideCapsule(localPoint);
                default:
                    return IsInsideBox(localPoint);
            }
        }

        private static bool IsInsideBox(Vector3 localPoint)
        {
            return Mathf.Abs(localPoint.x) <= 0.5f && Mathf.Abs(localPoint.y) <= 0.5f && Mathf.Abs(localPoint.z) <= 0.5f;
        }

        private static bool IsInsideSphere(Vector3 localPoint)
        {
            return localPoint.sqrMagnitude <= 0.25f;
        }

        private static bool IsInsideCapsule(Vector3 localPoint)
        {
            const float radius = 0.5f;
            const float halfHeight = 1f;
            float cylinderHalfHeight = Mathf.Max(0f, halfHeight - radius);
            float absY = Mathf.Abs(localPoint.y);

            if (absY <= cylinderHalfHeight)
            {
                return (localPoint.x * localPoint.x) + (localPoint.z * localPoint.z) <= radius * radius;
            }

            Vector3 capCenter = new Vector3(0f, Mathf.Sign(localPoint.y) * cylinderHalfHeight, 0f);
            Vector3 toCapCenter = localPoint - capCenter;
            return toCapCenter.sqrMagnitude <= radius * radius;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            DrawGizmoWireframe(new Color(0.35f, 0.9f, 1f, 0.35f));
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmoWireframe(new Color(0.35f, 0.9f, 1f, 0.95f));
        }

        private void DrawGizmoWireframe(Color color)
        {
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;

            switch (shape)
            {
                case UMAEffectorShape.Sphere:
                    Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
                    break;
                case UMAEffectorShape.Capsule:
                    DrawWireCapsule();
                    break;
                default:
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    break;
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private static void DrawWireCapsule()
        {
            const float radius = 0.5f;
            const float halfHeight = 1f;
            float cylinderHalfHeight = Mathf.Max(0f, halfHeight - radius);
            Vector3 topCenter = Vector3.up * cylinderHalfHeight;
            Vector3 bottomCenter = Vector3.down * cylinderHalfHeight;

            Gizmos.DrawWireSphere(topCenter, radius);
            Gizmos.DrawWireSphere(bottomCenter, radius);
            Gizmos.DrawLine(topCenter + Vector3.right * radius, bottomCenter + Vector3.right * radius);
            Gizmos.DrawLine(topCenter - Vector3.right * radius, bottomCenter - Vector3.right * radius);
            Gizmos.DrawLine(topCenter + Vector3.forward * radius, bottomCenter + Vector3.forward * radius);
            Gizmos.DrawLine(topCenter - Vector3.forward * radius, bottomCenter - Vector3.forward * radius);
        }
#endif
    }
}
