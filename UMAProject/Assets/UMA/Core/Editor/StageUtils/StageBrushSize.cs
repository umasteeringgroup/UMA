using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Brush sizes for the editing stages. Sizes are stored in meters (world units) and shown
    /// in millimeters, which suit character-scale work better.
    /// </summary>
    public static class StageBrushSize
    {
        /// <summary>The smallest brush radius, in millimeters.</summary>
        public const float MinRadiusMm = 1f;

        /// <summary>The largest brush radius, in millimeters.</summary>
        public const float MaxRadiusMm = 250f;

        /// <summary>
        /// A slider that edits a distance stored in meters, shown in millimeters. A value
        /// outside the range is clamped to it.
        /// </summary>
        public static float MillimeterSlider(GUIContent label, float meters, float minMm, float maxMm)
        {
            EditorGUI.BeginChangeCheck();
            float mm = EditorGUILayout.Slider(label, meters * 1000f, minMm, maxMm);
            if (EditorGUI.EndChangeCheck())
            {
                return mm / 1000f;
            }
            return Mathf.Clamp(meters, minMm / 1000f, maxMm / 1000f);
        }

        /// <summary>A brush radius slider: 1 to 250 mm.</summary>
        public static float RadiusSlider(GUIContent label, float meters) =>
            MillimeterSlider(label, meters, MinRadiusMm, MaxRadiusMm);

        /// <summary>
        /// How many Scene view GUI points a world-space radius covers at a point, seen from
        /// the camera. Call from Scene view GUI code (it uses the Handles camera).
        /// </summary>
        public static float PixelRadiusAt(Vector3 worldPoint, float radiusMeters, Camera camera)
        {
            if (camera == null || radiusMeters <= 0f)
            {
                return 0f;
            }
            Vector2 center = HandleUtility.WorldToGUIPoint(worldPoint);
            Vector2 edge = HandleUtility.WorldToGUIPoint(worldPoint + (camera.transform.right * radiusMeters));
            return Vector2.Distance(center, edge);
        }
    }
}
