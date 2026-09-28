using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>Explicit conversions for source-local curves, posed surfaces and world-space effects.</summary>
    public static class HairCoordinateUtility
    {
        public static bool IsInvertibleAffine(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++) if (!float.IsFinite(matrix[i])) return false;
            return Mathf.Abs(matrix.m30) < 1e-6f && Mathf.Abs(matrix.m31) < 1e-6f &&
                Mathf.Abs(matrix.m32) < 1e-6f && Mathf.Abs(matrix.m33 - 1f) < 1e-6f &&
                float.IsFinite(matrix.determinant) && Mathf.Abs(matrix.determinant) > 1e-12f;
        }

        // A surface normal is a covector (inverse transpose), whereas the displacement written
        // back to a curve is a vector (inverse). Treating them identically fails under nonuniform
        // scale/shear. Normalize last to keep the authored distance in source-local units.
        public static Vector3 NormalDisplacement(Vector3 sourceNormal, Matrix4x4 worldToSource)
        {
            if (!float.IsFinite(sourceNormal.sqrMagnitude) || sourceNormal.sqrMagnitude < 1e-12f) return Vector3.zero;
            Vector3 worldNormal = worldToSource.transpose.MultiplyVector(sourceNormal);
            Vector3 direction = worldToSource.MultiplyVector(worldNormal);
            return float.IsFinite(direction.sqrMagnitude) && direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector3.zero;
        }
    }
}
